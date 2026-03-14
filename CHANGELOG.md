# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).

## [Unreleased]

### Added

- Pi-only runtime contracts and services (`IAgentClientService`, `PiRpcClientService`, `PiRpcSessionWrapper`) for Nexus session execution over Pi RPC.
- Opt-in E2E test project (`test/CopilotNexus.E2E.Tests`) for running-server API smoke checks and CLI lifecycle validation.
- Repository-managed git hook setup (`.githooks/pre-push`, `scripts/Enable-GitHooks.ps1`) to block pushes with uncommitted changes.
- **CopilotNexus.Cli** — standalone CLI console app (`src/CopilotNexus.Cli/`) providing the `nexus` command interface (start, stop, status, build, install, publish, version, winapp)
- `CopilotNexusPaths.CliExe` and `CopilotNexusPaths.ServiceExe` path constants replacing the former `NexusExe`
- Session footer agent profile selector in the Windows app for applying per-session profile configuration (model/agent/MCP/skills) without leaving the tab.
- Versioned launcher shim project (`CopilotNexus.Shim`) plus SemVer resolver utilities for side-by-side payload execution.
- Send-timeout hardening coverage: new BDD spec (`docs/bdd/send-input-timeout-flow.md`) plus hub/API/app/core regression tests.
- Runtime selection persistence (`RuntimeAgentType`, `RuntimeAgentConfig`, `IRuntimeAgentConfigService`, `JsonRuntimeAgentConfigService`) for choosing `pi` or `copilot-sdk`.

### Changed

- Nexus now runs Pi by default with simplified Pi-only API/model/session contracts (adapter-type payloads removed).
- `nexus start` now performs startup validation (`/health`, `/api/models`, session create/delete probe) and returns a non-zero exit code on validation failure.
- Session output view auto-scroll now pauses on manual scroll while active and resumes after inactivity.
- Pi model catalog discovery now queries Pi RPC `get_available_models`, and `GET /api/models` returns the live runtime list.
- **CLI/Service split** — `CopilotNexus.Service` is now a pure ASP.NET Core web host (~40 lines) with no CLI commands, no Spectre.Console, and no System.CommandLine; all CLI functionality moved to `CopilotNexus.Cli`
- `nexus` alias now points to `CopilotNexus.Cli.exe` instead of `CopilotNexus.Service.exe`
- `CopilotNexusPaths.NexusExe` replaced by `CliExe` (CLI entry point) and `ServiceExe` (web server)
- User-specific session state moved from `%LOCALAPPDATA%\CopilotNexus\...` to `%USERPROFILE%\.copilot-nexus\session-state.json`; install/runtime binaries remain under `%LOCALAPPDATA%\CopilotNexus\`
- Install/publish flow now writes versioned payload directories under `%LOCALAPPDATA%\CopilotNexus\app\{cli|service|winapp}\<version>\...`.
- Session send paths (`SessionHub` + `SessionsController`) now dispatch long-running sends in background and return acceptance immediately.
- `scripts/Update-Nexus.ps1` now reconciles the `nexus` alias, removes legacy install artifacts, and performs shim-based stop/restart when legacy paths are detected.
- `nexus start` now accepts `--agent pi|copilot-sdk`, and service startup selects/runtime-persists the active `IAgentClientService` adapter without changing public API contracts.

### Fixed

- Webhook background-send cancellation now logs as informational cancellation (with optional canceled callback status) instead of error-level failure noise.
- Pi runtime executable detection on Windows now defaults to `pi.cmd`, avoiding false "pi not found" failures when npm shims are installed.
- Removed `nexus update` command from CLI and switched publish to shim-resolved side-by-side versions (no staged copy-over step).
- Pi-only runtime startup/validation no longer forces model id `pi-auto`, and Pi wrapper now maps `pi-auto`/`auto` to runtime-default model selection.
- Shim-launched payloads now preserve the caller working directory so repo-root commands (`build`/`publish`) resolve correctly.
- Installed-shim publish now skips in-use shim binaries during refresh to avoid Windows file-lock failures.
- Shim `--help`/`-h` now passes through to the target CLI unless a shim-specific flag (`--previous`, `--resolve-path`, `--cleanup`) is used.
- App update banner now suppresses downgrade/same-version notifications by requiring available version to be newer than the running version.

### Added (prior)

- **CopilotNexus.Updater** — cross-platform C# console app replacing the PowerShell updater script
- `UpdaterService` with testable logic: wait for process exit, copy staged files, clear staging, relaunch
- 14 pure C# updater tests (no process spawning) replacing 12 PowerShell-dependent tests
- `CopilotNexusPaths.UpdaterExe` path constant for the updater shim location
- **Spectre.Console** rich CLI output for all Nexus commands — coloured markup, status spinners, tables, and panels
- `nexus status` renders a formatted table with process, health, staging, and path info
- `nexus install` and `nexus publish` show animated status spinners during `dotnet publish`
- `nexus publish` shows a "Next steps" panel with available follow-up commands
- `CopilotNexusPaths` static class in Core — centralized path constants for the install layout (`%LOCALAPPDATA%\CopilotNexus\`)
- Nexus CLI commands: `stop`, `install`, `update [--component]`, `publish [--component]`
- PID lock file (`nexus.lock`) — written on `start`, read by `stop`/`status`, cleaned on exit
- `nexus status` now reports lock file PID and staged update status
- `nexus publish` — builds and publishes Nexus and/or App to install directory
- `nexus update` — applies staged updates with stop/copy/restart cycle
- Installation & Operations Guide (`docs/installation-and-operations.md`)
- 18 new tests for `CopilotNexusPaths` (path correctness, case insensitivity, staging isolation)

### Changed

- **Project renamed** from `CopilotFamily` to `CopilotNexus` — all namespaces, paths, docs, and file names updated
- `CopilotFamily.Nexus` backend project renamed to `CopilotNexus.Service` (avoids redundant `CopilotNexus.Nexus`)
- Install path changed from `%LOCALAPPDATA%\CopilotFamily\` to `%LOCALAPPDATA%\CopilotNexus\`
- Solution file renamed to `CopilotNexus.slnx`
- **Hot restart** now launches `CopilotNexus.Updater.exe` instead of `powershell.exe` — no visible shell windows
- App staging detection now uses `CopilotNexusPaths.AppStaging` instead of `dist/staging/`
- `README.md` rewritten to reflect Nexus + Avalonia architecture
- CLI entry point uses `NEXUS_TEST_MODE` env var instead of arg-sniffing
- `nexus winapp start` searches `CopilotNexusPaths.AppInstall` first

### Removed

- `update.ps1` PowerShell updater script — replaced by `CopilotNexus.Updater` console app
- `IsCliCommand` and `IsUserFacingArgs` methods — replaced by env var test mode detection

### Added (prior)

- **Nexus CLI commands** via `System.CommandLine 2.0.3`:
  - `nexus start [--url]` — start the Nexus service
  - `nexus status [--url]` — query a running Nexus instance health
  - `nexus winapp start [--nexus-url] [--test-mode]` — launch the desktop app
- `/health` endpoint on Nexus — returns status, session count, model count, and timestamp
- `NexusHostBuilder` — extracted web host configuration for reuse by CLI and test factory
- 11 new tests for health endpoint (4) and CLI command routing (7)
- **Nexus SignalR client** — Avalonia app now connects to Nexus backend via `NexusSessionManager`
- `NexusSessionProxy` — `ICopilotSessionWrapper` implementation for Nexus-backed sessions (sends via SignalR, receives output callbacks)
- `--nexus-url` startup argument to configure Nexus backend URL (default: `http://localhost:5280`)
- `SessionInfo.FromRemote()` factory method for reconstructing session info from Nexus DTOs
- 23 new unit tests for `NexusSessionProxy` (12 tests) and `NexusSessionManager` (11 tests) with mock HTTP handler
- **CopilotNexus.Service** — standalone ASP.NET Core backend service for Copilot session management
  - **SignalR Hub** (`/hubs/session`) — real-time streaming of session output to connected clients
  - **REST API** (`/api/sessions`, `/api/models`) — CRUD for sessions, model listing, input sending, reconfiguration
  - **Webhook API** (`/api/webhooks/sessions`) — automation endpoints for scripts/CI to create sessions and send messages with optional callback URLs
- Shared DTOs and `ISessionHubClient` contract in `CopilotNexus.Core.Contracts`
- 20 Nexus integration tests (REST API + Webhooks) using `WebApplicationFactory` with mock SDK
- **Model selector ComboBox** in session tab status bar — change models on a live session via dropdown
- **Avalonia UI migration** — replaced WPF with Avalonia 11.3.12 for cross-platform support
- Headless UI testing via `Avalonia.Headless.XUnit` (MIT licensed, no visible windows needed)
- 17 headless UI tests replacing FlaUI-based process tests
- Tabbed interface for managing multiple Copilot SDK sessions simultaneously
- Direct GitHub Copilot SDK integration via `GitHub.Copilot.SDK` NuGet package (JSON-RPC)
- MVVM architecture with `MainWindowViewModel` and `SessionTabViewModel`
- Streaming response support with word-by-word delta accumulation
- Dark theme UI with VS Code-inspired color scheme
- Keyboard shortcuts: Ctrl+T (new tab), Ctrl+W (close tab)
- Serilog structured logging with rolling file sink (`%LOCALAPPDATA%\CopilotNexus\logs\`)
- Global exception handlers for unhandled UI, domain, and task exceptions
- `--test-mode` flag with mock services for development without Copilot CLI
- `--reset-state` flag to clear persisted state on startup (clean slate for testing or troubleshooting)
- `--minimized` flag to start the window minimized (for CI or automated test runs)
- Mock services (`MockCopilotClientService`, `MockCopilotSessionWrapper`) for UI testing
- FlaUI-based UI automation tests (`CopilotNexus.UI.Tests`)
- AutomationIds on all interactive XAML elements
- VS Code tasks for build, run, test, publish-dist, and stage-update workflows
- Architecture documentation (`docs/architecture-overview.md`)
- Testing guide (`docs/testing-guide.md`)
- Status bar showing connection and tab creation progress
- SDK native session persistence — sessions survive app restarts via `ResumeSessionAsync`
- Structured session IDs (`copilot-nexus-{timestamp}-{guid}`) for auditable SDK session tracking
- App state persistence — saves/restores open tabs and SDK session IDs on exit/launch
- `JsonStatePersistenceService` with atomic file writes for crash-safe state saving
- Distribution pipeline — `dotnet publish` to `dist/` folder for fast iteration
- Staging update detection via `StagingUpdateDetectionService` (FileSystemWatcher + timer fallback)
- Update notification bar in UI with "Restart Now" / "Later" buttons
- PowerShell updater script (embedded resource) for hot restart — handles PID wait, file copy, app relaunch
- BDD feature specifications in `docs/bdd/` for distribution, hot restart, and session persistence
- Copilot SDK C# instructions file (`.github/copilot-sdk-csharp.instructions.md`)
- Conventional commit skill (`.github/skills/conventional-commit/SKILL.md`)
- 109 tests across Core (62), App (47), and UI test projects
- Integration tests for dist/staging/updater pipeline including PowerShell script execution
- `--reset-state` startup flag — clears persisted session state for a clean start
- `--minimized` startup flag — starts the main window minimized (reduces visual disruption during UI tests)
- `IStatePersistenceService.ClearAsync()` — programmatic state reset
- UI tests now pass `--reset-state` to prevent state pollution between test runs
- Model selection support — `ListModelsAsync`, `SessionConfiguration.Model`, per-session model ComboBox
- Working directory support — `SessionConfiguration.WorkingDirectory`, per-session folder browser
- Autopilot mode toggle — `SessionConfiguration.IsAutopilot`, per-session interactive/autopilot switching
- Permission handler abstraction — configurable `PermissionRequestHandler` (approve-all vs interactive)
- User input handler — `UserInputHandler` for surfacing `ask_user` tool calls in interactive mode
- `ReconfigureSessionAsync` — disconnect + resume pattern for changing model/workdir on live sessions
- BDD specs for model selection, working directory, and autopilot mode (`docs/bdd/`)
- 149 tests across Core (65), App (47), Nexus (20), and UI (17) test projects

### Fixed

- **Input box pushed off screen** — messages list was not constrained within its Grid row; switched to DockPanel layout with input area docked to bottom so it stays pinned regardless of message count
- App crash when creating a new session — added `OnPermissionRequest = PermissionHandler.ApproveAll` to `SessionConfig`
- Duplicate Copilot responses — SDK fires both streaming deltas and a final `AssistantMessageEvent`; now skips the duplicate full message when deltas were already received
- Tab selection bug after closing a tab — now correctly checks `SelectedTab == tab`
- Button styling in dark mode — buttons had white backgrounds with unreadable text; added global dark button template
- Tab header styling in dark mode — `TabItem` and `TabControl` used WPF default white chrome; added custom dark templates with accent border on selected tab
- Nullable warning CS8604 in `CopilotSessionWrapper` — `msg.Data.Content` can be null, now coalesced to `string.Empty`
- xUnit1031 warnings — converted `Task.Result`/`.Wait()` calls to async/await in test methods
- Updater timeout test — replaced both `$waited -lt 30` and `$waited -ge 30` in PS1 script for correct timeout behavior

### Changed

- **UI framework: WPF → Avalonia 11.3.12** — cross-platform XAML with headless testing support
- Target framework: `net8.0-windows` → `net8.0` (no longer Windows-only)
- Converters return `bool` for `IsVisible` instead of WPF `Visibility` enum
- `WpfUiDispatcher` → `AvaloniaUiDispatcher` (uses `Dispatcher.UIThread.Post`)
- `RelayCommand` — removed `CommandManager.RequerySuggested`, uses manual `RaiseCanExecuteChanged()`
- XAML files: `.xaml` → `.axaml` with Avalonia namespace
- UI tests: FlaUI (process-based) → Avalonia.Headless.XUnit (in-process, no visible windows)
- App.Tests: `net8.0-windows` + `UseWPF` → `net8.0` (pure .NET, no WPF dependency)
- Session persistence now uses SDK native `ResumeSessionAsync` instead of custom message serialization
- `TabState` simplified — removed `Messages` list, added `SdkSessionId` (SDK handles conversation history on disk)
- `CopilotSessionWrapper.SessionId` now uses `session.SessionId` from SDK instead of random GUID
- `CloseSpecificTabAsync` calls `DeleteSessionAsync` to permanently clean up SDK session data
- `ICopilotClientService` expanded with `ResumeSessionAsync`, `DeleteSessionAsync`, and `sessionId` parameter on `CreateSessionAsync`
- `ISessionManager` expanded with `ResumeSessionAsync` and `deleteFromDisk` parameter on `RemoveSessionAsync`
- `SessionManager` generates structured SDK session IDs for traceability
- UI tests capture screenshots to `test/screenshots/` for visual verification and failure diagnostics
- Build discipline rule added to copilot-instructions — always build after changes, fix warnings
- BDD maintenance rule — update BDD specs when new information surfaces (SDK capabilities, user feedback, research)
- Feature development process documented: Research → BDD → Design Review → Implement → Verify
