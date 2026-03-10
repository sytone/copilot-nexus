# Architecture Overview

## High-Level Design

Copilot Family is a WPF desktop application that manages multiple GitHub Copilot SDK
sessions in a tabbed interface. It uses the **MVVM pattern** with interface-based
abstractions for testability.

### SDK Integration

The app integrates with the [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
(`GitHub.Copilot.SDK` NuGet package) which communicates with the locally installed
Copilot CLI over **JSON-RPC**. This is not a process wrapper — the SDK manages the
connection lifecycle and provides typed events for streaming responses.

```
┌──────────────────────────────────────────────┐
│  CopilotFamily.App  (WPF / MVVM)             │
│                                              │
│  MainWindowViewModel                         │
│    └── SessionTabViewModel (one per tab)     │
│          └── handles streaming deltas        │
│                                              │
│  State persistence: save/restore on exit     │
│  Update detection: staging → hot restart     │
│  Logging: Serilog → file + debug sinks       │
│  Global exception handlers                   │
│  --test-mode with mock services              │
└────────────────┬─────────────────────────────┘
                 │ uses interfaces
┌────────────────▼─────────────────────────────┐
│  CopilotFamily.Core  (business logic)        │
│                                              │
│  ISessionManager                             │
│    └── SessionManager                        │
│          ├── ICopilotClientService           │
│          │     ├── CopilotClientService      │
│          │     │     └── CopilotClient (SDK) │
│          │     └── MockCopilotClientService  │
│          └── ICopilotSessionWrapper          │
│                ├── CopilotSessionWrapper     │
│                │     └── CopilotSession (SDK)│
│                └── MockCopilotSessionWrapper │
│                                              │
│  IStatePersistenceService                    │
│    └── JsonStatePersistenceService           │
│  IUpdateDetectionService                     │
│    └── StagingUpdateDetectionService         │
└──────────────────────────────────────────────┘
                 │ JSON-RPC (production)
┌────────────────▼─────────────────────────────┐
│  GitHub Copilot CLI  (local process)         │
│    └── authenticates, routes to LLM          │
│    └── session state: ~/.copilot/session-state/
└──────────────────────────────────────────────┘
```

## Layer Responsibilities

### CopilotFamily.Core

No UI dependencies. Contains all business logic and SDK abstractions.

| Component                       | Responsibility                                                                    |
| ------------------------------- | --------------------------------------------------------------------------------- |
| `ICopilotClientService`         | Manages the single `CopilotClient` — create, resume, delete sessions              |
| `CopilotClientService`          | Implementation — creates client, starts connection, creates/resumes sessions      |
| `MockCopilotClientService`      | Test implementation — simulates connection for UI testing                         |
| `ICopilotSessionWrapper`        | Wraps one `CopilotSession` with typed output events                               |
| `CopilotSessionWrapper`         | Implementation — subscribes to SDK events, translates to `SessionOutputEventArgs` |
| `MockCopilotSessionWrapper`     | Test implementation — streams word-by-word mock responses                         |
| `ISessionManager`               | Coordinates multiple sessions (create, resume, send, remove)                      |
| `SessionManager`                | Implementation — maps session IDs to wrappers, generates structured SDK IDs       |
| `IStatePersistenceService`      | Save/load application state (open tabs, SDK session IDs)                          |
| `JsonStatePersistenceService`   | JSON persistence with atomic file writes for crash safety                         |
| `IUpdateDetectionService`       | Watch for staged updates in dist/staging/                                         |
| `StagingUpdateDetectionService` | FileSystemWatcher + timer fallback for staging detection                          |
| `IUiDispatcher`                 | Abstracts UI thread marshalling for testability                                   |
| `SessionMessage`                | Chat message model with `INotifyPropertyChanged` for streaming                    |
| `SessionInfo`                   | Session metadata (id, name, model, state, SDK session ID)                         |
| `SessionOutputEventArgs`        | Carries output data with `OutputKind` (Delta, Message, Idle)                      |
| `AppState` / `TabState`         | Serialization models for lightweight state persistence                            |

### CopilotFamily.App

WPF application following strict MVVM. Views are pure XAML data-binding with no
business logic in code-behind (only focus management and scroll behavior).

| Component                            | Responsibility                                                            |
| ------------------------------------ | ------------------------------------------------------------------------- |
| `App.xaml.cs`                        | Serilog logging config, global exception handlers, `--test-mode` flag     |
| `MainWindow.xaml.cs`                 | Service creation, state save/restore, update detection, updater wiring    |
| `MainWindowViewModel`                | Manages tab collection, creates/closes tabs, update notification commands |
| `SessionTabViewModel`                | Manages one tab — input, streaming message accumulation, commands         |
| `ViewModelBase`                      | `INotifyPropertyChanged` base class                                       |
| `RelayCommand` / `AsyncRelayCommand` | `ICommand` implementations for MVVM                                       |
| `WpfUiDispatcher`                    | `IUiDispatcher` implementation using WPF's `Dispatcher`                   |
| Converters                           | `MessageRole` → brush/label, empty count → visibility, bool → visibility  |
| `Resources/update.ps1`               | Embedded PowerShell updater script for hot restart                        |

## Session Persistence (SDK Native)

The app leverages the Copilot SDK's built-in session persistence:

1. **Structured session IDs** — generated as `copilot-family-{timestamp}-{guid}` and
   passed to `SessionConfig.SessionId`. The SDK stores all conversation history, tool
   call results, and planning state at `~/.copilot/session-state/{sessionId}/`.

2. **Disconnect on exit** — when the app closes, sessions are disconnected (not disposed)
   via `session.Disconnect()`. This releases in-memory resources but preserves data on disk.

3. **Resume on startup** — `client.ResumeSessionAsync(sessionId, config)` restores a
   previous session. If resume fails (session expired/deleted), the app falls back to
   creating a fresh session.

4. **Delete on tab close** — `client.DeleteSessionAsync(sessionId)` permanently removes
   session data when the user explicitly closes a tab.

5. **App state file** — only lightweight metadata is persisted locally:
   `%LOCALAPPDATA%\CopilotFamily\app-state.json` stores tab names, models, and SDK
   session IDs. The SDK handles all conversation history.

## Distribution and Hot Restart

The app supports a fast iteration pipeline:

1. **Dist folder** — `dotnet publish` outputs to `dist/` for immediate use
2. **Staging folder** — new builds placed in `dist/staging/` trigger update detection
3. **Update notification** — `StagingUpdateDetectionService` watches staging via
   FileSystemWatcher + 30-second timer fallback; shows notification bar in UI
4. **Hot restart** — "Restart Now" extracts embedded `update.ps1` to `%TEMP%`,
   saves state, launches updater, exits. The script waits for the old process to exit,
   copies staged files to dist root, clears staging, and relaunches the app.
5. **Session continuity** — on restart, saved SDK session IDs are used to resume sessions

## Logging

All services and ViewModels accept `ILogger<T>` via constructor injection.

- **Serilog** is configured in `App.xaml.cs` with two sinks:
  - **File sink** — rolling daily logs at `%LOCALAPPDATA%\CopilotFamily\logs\`
  - **Debug sink** — writes to Visual Studio Output window
- **Global exception handlers** catch unhandled exceptions on the UI thread,
  domain, and task scheduler to prevent silent crashes
- Log levels:
  - `Information` — session lifecycle, tab creation/close, initialization
  - `Debug` — message sends, output events, disposal
  - `Warning` — non-fatal errors (e.g., session close failures)
  - `Error` / `Critical` — unhandled exceptions, SDK failures

## Test Mode

Launch with `--test-mode` to use mock services instead of the real Copilot SDK:

```
CopilotFamily.App.exe --test-mode
```

Mock services simulate streaming responses with word-by-word delays, allowing
UI testing and development without a Copilot CLI installation.

## Streaming Flow

When a user sends a prompt, the following sequence occurs:

1. `SessionTabViewModel.SendCommand` → adds `User` message to `Messages`
2. Calls `ICopilotSessionWrapper.SendAsync(prompt)` (internally `SendAndWaitAsync`)
3. SDK fires `AssistantMessageDeltaEvent` events during generation
4. `CopilotSessionWrapper` translates to `SessionOutputEventArgs(OutputKind.Delta)`
5. `SessionTabViewModel.HandleOutput` receives delta via `IUiDispatcher.BeginInvoke`:
   - First delta → creates new `SessionMessage(isStreaming: true)`, adds to collection
   - Subsequent deltas → calls `AppendContent()` on the same message
6. SDK fires `SessionIdleEvent` when response is complete
7. `HandleOutput` calls `CompleteStreaming()` on the message
8. `SendAndWaitAsync` returns, `IsProcessing` set to false, send button re-enabled

## Testability

All SDK and UI dependencies are behind interfaces, enabling pure unit tests:

- **`SynchronousUiDispatcher`** — test helper that executes dispatched actions inline
- **`Mock<ICopilotSessionWrapper>`** — mocks SDK session for ViewModel tests
- **`Mock<ICopilotClientService>`** — mocks client for SessionManager tests
- **`Mock<ISessionManager>`** — mocks manager for MainWindowViewModel tests
- **`NullLogger<T>`** — from `Microsoft.Extensions.Logging.Abstractions` for test logging

### Test Projects

| Project                    | Tests | Coverage                                                                |
| -------------------------- | ----- | ----------------------------------------------------------------------- |
| `CopilotFamily.Core.Tests` | 62    | SessionManager, SessionMessage, persistence, staging detection, dist/staging integration |
| `CopilotFamily.App.Tests`  | 47    | ViewModels (MainWindow, SessionTab), Converters                         |
| `CopilotFamily.UI.Tests`   | 10    | FlaUI automation — app launch, tab create/close, input/send             |

## Dependencies

| Package                                     | Purpose                              |
| ------------------------------------------- | ------------------------------------ |
| `GitHub.Copilot.SDK`                        | Copilot CLI integration via JSON-RPC |
| `Microsoft.Extensions.Logging.Abstractions` | `ILogger<T>` for structured logging  |
| `Serilog.Extensions.Logging`                | Serilog → `ILoggerFactory` bridge    |
| `Serilog.Sinks.File`                        | Rolling file log sink                |
| `Serilog.Sinks.Debug`                       | Debug output log sink                |
| `System.Text.Json`                          | State persistence serialization      |
| `FlaUI.UIA3`                                | WPF UI automation testing            |
| `xunit`                                     | Test framework                       |
| `Moq`                                       | Mocking framework                    |
