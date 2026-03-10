# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).

## [Unreleased]

### Added

- **CopilotFamily.Nexus** — standalone ASP.NET Core backend service for Copilot session management
  - **SignalR Hub** (`/hubs/session`) — real-time streaming of session output to connected clients
  - **REST API** (`/api/sessions`, `/api/models`) — CRUD for sessions, model listing, input sending, reconfiguration
  - **Webhook API** (`/api/webhooks/sessions`) — automation endpoints for scripts/CI to create sessions and send messages with optional callback URLs
- Shared DTOs and `ISessionHubClient` contract in `CopilotFamily.Core.Contracts`
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
- Serilog structured logging with rolling file sink (`%LOCALAPPDATA%\CopilotFamily\logs\`)
- Global exception handlers for unhandled UI, domain, and task exceptions
- `--test-mode` flag with mock services for development without Copilot CLI
- `--reset-state` flag to clear persisted state on startup (clean slate for testing or troubleshooting)
- `--minimized` flag to start the window minimized (for CI or automated test runs)
- Mock services (`MockCopilotClientService`, `MockCopilotSessionWrapper`) for UI testing
- FlaUI-based UI automation tests (`CopilotFamily.UI.Tests`)
- AutomationIds on all interactive XAML elements
- VS Code tasks for build, run, test, publish-dist, and stage-update workflows
- Architecture documentation (`docs/architecture-overview.md`)
- Testing guide (`docs/testing-guide.md`)
- Status bar showing connection and tab creation progress
- SDK native session persistence — sessions survive app restarts via `ResumeSessionAsync`
- Structured session IDs (`copilot-family-{timestamp}-{guid}`) for auditable SDK session tracking
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
