# Architecture Overview

## High-Level Design

Copilot Nexus is a cross-platform desktop application (Avalonia 11.3.12 / .NET 8) that
manages multiple agent-runtime sessions in a tabbed interface. It uses the
**MVVM pattern** with interface-based abstractions for testability.

The application follows a **client–service split architecture**. The Avalonia desktop app
is a thin client that communicates with **CopilotNexus.Service**, an ASP.NET Core backend
service that owns all runtime interactions. **CopilotNexus.Cli** is a separate console
application that provides the `nexus` command-line interface for managing the service,
publishing updates, and launching the desktop app. The app has no direct dependency on the
runtime process — all session management, streaming, and model queries flow through the Nexus
service via **SignalR** (real-time events) and **REST** (CRUD operations).

This split enables multiple future clients (web UI, CLI, webhook-driven automation) to
share the same backend without duplicating SDK integration logic.

### Architecture Diagram

```
┌────────────────────────────────┐
│  CopilotNexus.Cli              │
│  (nexus CLI — user-facing)     │
│                                │
│  start/stop/status/install     │
│  build/publish/update          │
│  winapp start                  │
│                                │
│  Launches & manages Service    │
└───────────────┬────────────────┘
                │ spawns/manages
                ▼
┌────────────────────────────────────────┐
│  CopilotNexus.Service                  │
│  (ASP.NET Core web host — ~40 lines)   │
│                                        │
│  SignalR Hub (SessionHub)              │
│  REST API (SessionsController)         │
│  Webhook (WebhookController)           │
│  ModelsController                      │
│                                        │
│  SessionManager ◄── PiRpcClientService │
│  (Pi coding agent RPC runtime)         │
└──────────────────┬─────────────────────┘
                   ▲
┌──────────────────┘
│
┌────────────────────────────┐     │
│  Avalonia App (thin client)│     │
│                            │     │
│  NexusSessionManager ──────┼─────┘
│  NexusSessionProxy    ──────┼─────►  SignalR + REST
│                            │
│  Renders session UI        │
│  No direct SDK dependency  │
│                            │
│  In test mode: uses local  │
│  SessionManager + mocks    │
└────────────────────────────┘
                                   ▲
┌────────────────────────────┐     │
│  Future: Web UI            │─────┘
│  Future: CLI client        │
│  Future: Webhooks/CI       │────► POST /api/webhooks/sessions/{id}/message
└────────────────────────────┘
```

### Runtime integration

Nexus uses Pi coding agent RPC (`pi --mode rpc`) as the runtime interface.
`PiRpcClientService` hosts the process and `PiRpcSessionWrapper` maps Pi events to
`SessionOutputEventArgs` for the app/service contracts.

The Avalonia app remains runtime-agnostic and interacts only through Nexus APIs.

## Solution Projects

| Project                      | Purpose                                                                    |
| ---------------------------- | -------------------------------------------------------------------------- |
| `CopilotNexus.Core`         | Core business logic, SDK abstractions, shared DTOs/contracts               |
| `CopilotNexus.App`          | Avalonia 11 desktop application (MVVM, thin SignalR client)                |
| `CopilotNexus.Cli`          | CLI console app — `nexus` commands (start, stop, build, publish, update)   |
| `CopilotNexus.Service`      | ASP.NET Core web host — SignalR hub, REST API, webhooks (~40-line Program.cs) |
| `CopilotNexus.Core.Tests`   | 65 unit tests (xUnit + Moq)                                               |
| `CopilotNexus.App.Tests`    | 47 ViewModel/converter tests (xUnit + Moq)                                |
| `CopilotNexus.Service.Tests`  | 20 integration tests (WebApplicationFactory)                               |
| `CopilotNexus.UI.Tests`     | 17 headless UI tests (Avalonia.Headless.XUnit)                             |

## Layer Responsibilities

### CopilotNexus.Core

No UI dependencies. Contains all business logic, SDK abstractions, and the shared
contract types used by both App and Nexus.

| Component                       | Responsibility                                                                    |
| ------------------------------- | --------------------------------------------------------------------------------- |
| `IAgentClientService`           | Runtime client contract — create, resume, delete sessions                         |
| `PiRpcClientService`            | Implementation — hosts Pi coding agent in RPC mode                                |
| `MockCopilotClientService`      | Test implementation — simulates connection for UI testing                         |
| `ICopilotSessionWrapper`        | Wraps one `CopilotSession` with typed output events                               |
| `CopilotSessionWrapper`         | Implementation — subscribes to SDK events, translates to `SessionOutputEventArgs` |
| `MockCopilotSessionWrapper`     | Test implementation — streams word-by-word mock responses                         |
| `ISessionManager`               | Coordinates multiple sessions (create, resume, send, remove)                      |
| `SessionManager`                | Implementation — maps session IDs to wrappers, generates structured SDK IDs       |
| `IStatePersistenceService`      | Save/load application state (open tabs, SDK session IDs)                          |
| `JsonStatePersistenceService`   | JSON persistence with atomic file writes for crash safety                         |
| `ISessionProfileService`        | CRUD for reusable session profiles (model/mode/MCP/agent defaults)               |
| `JsonSessionProfileService`     | JSON-backed Nexus profile persistence                                              |
| `IUpdateDetectionService`       | Watch for staged updates in dist/staging/                                         |
| `StagingUpdateDetectionService` | FileSystemWatcher + timer fallback for staging detection                          |
| `IUiDispatcher`                 | Abstracts UI thread marshalling for testability                                   |
| `SessionMessage`                | Chat message model with `INotifyPropertyChanged` for streaming                    |
| `SessionInfo`                   | Session metadata (id, name, model, state, SDK session ID); includes `FromRemote()` static factory for reconstructing from DTOs |
| `SessionOutputEventArgs`        | Carries output data with `OutputKind` (Delta, Message, Reasoning, Activity, Idle) |
| `AppState` / `TabState`         | Serialization models for lightweight state persistence                            |
| `Contracts/Dtos.cs`             | Shared DTOs: `SessionInfoDto`, `SessionOutputDto`, `ModelInfoDto`, `CreateSessionRequest`, etc. |
| `Contracts/ISessionHubClient.cs`| SignalR client interface — defines methods the hub can invoke on connected clients |

### CopilotNexus.App

Avalonia 11.3.12 application following strict MVVM. Views are pure AXAML data-binding
with no business logic in code-behind (only focus management and scroll behavior).

In **production mode**, the app connects to the Nexus backend via `NexusSessionManager`
and `NexusSessionProxy`. In **test mode**, it uses the local `SessionManager` with
`MockCopilotClientService` directly — no Nexus service required.

| Component                            | Responsibility                                                                   |
| ------------------------------------ | -------------------------------------------------------------------------------- |
| `App.axaml.cs`                       | Serilog logging config, global exception handlers, startup arg parsing           |
| `MainWindow.axaml.cs`               | Service creation — `NexusSessionManager(nexusUrl)` in prod, local `SessionManager` in test; state save/restore, update detection, updater wiring |
| `MainWindowViewModel`                | Manages tab collection, creates/closes tabs, update notification commands        |
| `SessionTabViewModel`                | Manages one tab — input, streaming message accumulation, commands                |
| `NexusSessionManager`                | `ISessionManager` implementation via SignalR + REST calls to Nexus (production)  |
| `NexusSessionProxy`                  | `ICopilotSessionWrapper` for Nexus-backed sessions — receives SignalR events     |
| `ViewModelBase`                      | `INotifyPropertyChanged` base class                                              |
| `RelayCommand` / `AsyncRelayCommand` | `ICommand` implementations for MVVM                                              |
| `AvaloniaUiDispatcher`               | `IUiDispatcher` implementation using Avalonia's `Dispatcher`                     |
| Converters                           | `MessageRole` → brush/label, empty count → visibility, bool → visibility         |
| `Resources/update.ps1`               | *(Removed)* — replaced by `CopilotNexus.Updater` project                       |

**Startup arguments:**

| Argument         | Effect                                                                   |
| ---------------- | ------------------------------------------------------------------------ |
| `--nexus-url`    | URL of the Nexus backend service (e.g., `https://localhost:5001`)        |
| `--test-mode`    | Use local `SessionManager` + `MockCopilotClientService` (no Nexus)      |
| `--reset-state`  | Clear persisted app state on launch                                      |
| `--minimized`    | Start the window minimized                                               |

### CopilotNexus.Service

Pure ASP.NET Core web host (~40 lines) that owns all SDK interactions and exposes them
through multiple protocols. Contains no CLI commands — all command-line functionality lives
in `CopilotNexus.Cli`. Any client (desktop, web, CLI, automation) connects here.

| Component              | Responsibility                                                                        |
| ---------------------- | ------------------------------------------------------------------------------------- |
| `SessionHub` (SignalR) | Real-time session operations: `JoinSession`, `LeaveSession`, `SendInput`, `AbortSession` |
| `SessionsController`   | REST API for session CRUD — create, list, get, configure, send input, delete          |
| `SessionProfilesController` | REST API for profile CRUD (`/api/session-profiles`)                              |
| `ModelsController`     | REST API for listing available models                                               |
| `WebhookController`    | Automation endpoint — accepts callback URLs for async session interaction (`POST /api/webhooks/sessions/{id}/message`) |
| `SessionManager`       | Reuses `SessionManager` from Core to manage SDK session lifecycle                     |
| `PiRpcClientService`   | Reuses `PiRpcClientService` from Core for Pi RPC runtime management                 |

## Session Persistence

Nexus stores lightweight tab metadata in `%LOCALAPPDATA%\\CopilotNexus\\state\\session-state.json`
and uses runtime session IDs to reconnect sessions.

If a requested session cannot be resumed (for example, `Session not found`), Nexus creates a
fresh session using the same persisted session ID and continues.

## Distribution and Hot Restart

The app supports a fast iteration pipeline:

1. **Dist folder** — `dotnet publish` outputs to `dist/` for immediate use
2. **Staging folder** — new builds placed in `dist/staging/` trigger update detection
3. **Update notification** — `StagingUpdateDetectionService` watches staging via
   FileSystemWatcher + 30-second timer fallback; shows notification bar in UI
4. **Hot restart** — "Restart Now" launches `CopilotNexus.Updater.exe` (a
   cross-platform C# console app), saves state, and exits. The updater waits for the
   old process to exit, copies staged files to the install directory, clears staging,
   and relaunches the app — no PowerShell dependency required.
5. **Session continuity** — on restart, saved SDK session IDs are used to resume sessions

## Logging

All services and ViewModels accept `ILogger<T>` via constructor injection.

- **Serilog** is configured in `App.axaml.cs` with two sinks:
  - **File sink** — rolling daily logs at `%LOCALAPPDATA%\CopilotNexus\logs\`
  - **Debug sink** — writes to Visual Studio Output window
- **Global exception handlers** catch unhandled exceptions on the UI thread,
  domain, and task scheduler to prevent silent crashes
- Log levels:
  - `Information` — session lifecycle, tab creation/close, initialization
  - `Debug` — message sends, output events, disposal
  - `Warning` — non-fatal errors (e.g., session close failures)
  - `Error` / `Critical` — unhandled exceptions, SDK failures

## Test Mode

Launch with `--test-mode` to use local mock services instead of the Nexus backend:

```
CopilotNexus.App.exe --test-mode
```

In test mode, the app creates a local `SessionManager` with `MockCopilotClientService`
directly — no Nexus service is required. Mock services simulate streaming responses with
word-by-word delays, allowing UI testing and development without a Copilot CLI
installation or a running Nexus instance.

## Streaming Flow

When a user sends a prompt, the following sequence occurs:

### Production Mode (via Nexus)

1. `SessionTabViewModel.SendCommand` → adds `User` message to `Messages`
2. Calls `NexusSessionProxy.SendAsync(prompt)` which invokes `SessionHub.SendInput` via SignalR
3. Nexus dispatches the prompt to the Pi RPC session wrapper
4. Pi runtime emits streaming events during generation
5. `PiRpcSessionWrapper` translates runtime events to `SessionOutputEventArgs`
6. Nexus broadcasts delta events to connected clients via SignalR hub
7. `NexusSessionProxy` receives SignalR delta, raises `OutputReceived` event
8. `SessionTabViewModel.HandleOutput` receives delta via `IUiDispatcher.BeginInvoke`:
   - First delta → creates new `SessionMessage(isStreaming: true)`, adds to collection
   - Subsequent deltas → calls `AppendContent()` on the same message
9. Runtime emits completion/idle signal when response is complete
10. Nexus broadcasts idle event via SignalR; `HandleOutput` calls `CompleteStreaming()`
11. `IsProcessing` set to false, send button re-enabled

### Test Mode (local)

1. `SessionTabViewModel.SendCommand` → adds `User` message to `Messages`
2. Calls `MockCopilotSessionWrapper.SendAsync(prompt)` directly (no Nexus)
3. Mock wrapper streams word-by-word deltas with simulated delays
4. `SessionTabViewModel.HandleOutput` processes deltas identically to production mode

## Testability

All SDK and UI dependencies are behind interfaces, enabling pure unit tests:

- **`SynchronousUiDispatcher`** — test helper that executes dispatched actions inline
- **`Mock<ICopilotSessionWrapper>`** — mocks SDK session for ViewModel tests
- **`Mock<IAgentClientService>`** — mocks runtime client for SessionManager tests
- **`Mock<ISessionManager>`** — mocks manager for MainWindowViewModel tests
- **`NullLogger<T>`** — from `Microsoft.Extensions.Logging.Abstractions` for test logging

### Test Projects

| Project                      | Tests | Coverage                                                                              |
| ---------------------------- | ----- | ------------------------------------------------------------------------------------- |
| `CopilotNexus.Core.Tests`   | 65    | SessionManager, SessionMessage, persistence, staging detection, dist/staging integration |
| `CopilotNexus.App.Tests`    | 47    | ViewModels (MainWindow, SessionTab), Converters                                       |
| `CopilotNexus.Service.Tests`  | 20    | Integration tests — REST API + webhooks via `WebApplicationFactory<Program>` with mock SDK |
| `CopilotNexus.UI.Tests`     | 17    | Headless UI tests — Avalonia.Headless.XUnit, no visible windows                       |

## Dependencies

| Package                                     | Purpose                                              |
| ------------------------------------------- | ---------------------------------------------------- |
| `GitHub.Copilot.SDK`                        | Copilot CLI integration via JSON-RPC (Nexus + Core)  |
| `Avalonia` (11.3.12)                        | Cross-platform UI framework (replaces WPF)           |
| `Avalonia.Desktop`                          | Avalonia desktop platform support                    |
| `Avalonia.Themes.Fluent`                    | Fluent design theme for Avalonia                     |
| `Microsoft.AspNetCore.SignalR`              | Real-time hub for Nexus ↔ App communication          |
| `Microsoft.AspNetCore.SignalR.Client`       | SignalR client used by NexusSessionManager           |
| `Microsoft.Extensions.Logging.Abstractions` | `ILogger<T>` for structured logging                  |
| `Serilog.Extensions.Logging`                | Serilog → `ILoggerFactory` bridge                    |
| `Serilog.Sinks.File`                        | Rolling file log sink                                |
| `Serilog.Sinks.Debug`                       | Debug output log sink                                |
| `System.Text.Json`                          | State persistence and DTO serialization              |
| `Avalonia.Headless.XUnit`                   | Headless UI testing (replaces FlaUI)                 |
| `Microsoft.AspNetCore.Mvc.Testing`          | `WebApplicationFactory` for Nexus integration tests  |
| `xunit`                                     | Test framework                                       |
| `Moq`                                       | Mocking framework                                    |
