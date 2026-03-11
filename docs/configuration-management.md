# Configuration Management

This document explains where Copilot Nexus configuration comes from, how values are
applied, and what is persisted per user.

## Configuration Sources

Configuration is currently managed through three main sources:

1. Command-line arguments
2. Local user files under `%USERPROFILE%\\.copilot-nexus`
3. Environment variables (limited use)

## Runtime Arguments

### Desktop app (`CopilotNexus.App`)

`src/CopilotNexus.App/Program.cs` passes startup arguments into `App.StartupArgs`.
`src/CopilotNexus.App/App.axaml.cs` and `src/CopilotNexus.App/MainWindow.axaml.cs`
consume these values.

Supported app arguments:

- `--nexus-url <url>`: Backend URL to connect to (default: `http://localhost:5280`)
- `--test-mode`: Use mock services instead of Nexus backend
- `--reset-state`: Clear persisted UI/session metadata on startup
- `--minimized`: Start the window minimized

### CLI (`CopilotNexus.Cli`)

`src/CopilotNexus.Cli/Program.cs` defines command-level options.

Examples:

- `nexus start --url <url>`
- `nexus status --url <url>`
- `nexus build -c <Debug|Release>`
- `nexus publish --component <nexus|app|both>`
- `nexus update --component <nexus|app|both>`
- `nexus winapp start --nexus-url <url> --test-mode`

## User-Specific Storage

User-specific configuration/state is stored under `%USERPROFILE%\\.copilot-nexus\\`.

Install/runtime binaries remain under `%LOCALAPPDATA%\CopilotNexus\`.

Primary paths are centralized in `src/CopilotNexus.Core/CopilotNexusPaths.cs`:

- `UserConfigRoot`: `%USERPROFILE%\\.copilot-nexus`
- `AppStateFile`: `%USERPROFILE%\\.copilot-nexus\\session-state.json`
- `Root`: `%LOCALAPPDATA%\CopilotNexus`
- `CliInstall`: `%LOCALAPPDATA%\CopilotNexus\cli`
- `NexusInstall`: `%LOCALAPPDATA%\CopilotNexus\nexus`
- `AppInstall`: `%LOCALAPPDATA%\CopilotNexus\app`
- `StagingRoot`: `%LOCALAPPDATA%\CopilotNexus\staging`
- `Logs`: `%LOCALAPPDATA%\CopilotNexus\logs`
- `NexusLockFile`: `%LOCALAPPDATA%\CopilotNexus\nexus.lock`

## Session Information Persistence

Session metadata used by the desktop app is persisted by
`src/CopilotNexus.Core/Services/JsonStatePersistenceService.cs`.

Persisted file:

- `%USERPROFILE%\\.copilot-nexus\\session-state.json`

Stored schema (`src/CopilotNexus.Core/Models/AppState.cs`):

- `AppState.Version`
- `AppState.SelectedTabIndex`
- `AppState.SessionCounter`
- `AppState.Tabs[]` containing:
  - `Name`
  - `Model`
  - `SdkSessionId`
  - `WorkingDirectory`
  - `IsAutopilot`

Important notes:

- Conversation history is not persisted by this file; SDK-managed session data is used
  when restoring via `SdkSessionId`.
- Corrupt JSON is backed up to `session-state.json.bak` and then reset.

## Settings vs State

There is currently no separate strongly-typed "user settings" file (for example,
theme or preferences) outside of persisted app/session state.

In practice, user-specific behavior is represented by:

- Startup flags (for example `--test-mode`, `--nexus-url`)
- Persisted `session-state.json` metadata
- Install/update layout and lock/log files under `%LOCALAPPDATA%\CopilotNexus`

## Environment Variables

Current usage is minimal:

- `NEXUS_TEST_MODE=1`: Used by the Service host to suppress lock-file handling during
  integration tests (`src/CopilotNexus.Service/Program.cs`).

## Restore and Save Flow

1. `MainWindow` initializes session manager.
2. If `--reset-state` is set, persisted state is cleared.
3. App loads `session-state.json` (if present).
4. `MainWindowViewModel.RestoreStateAsync` resumes sessions by `SdkSessionId`.
5. On app close (and hot restart), current metadata is captured and saved.

## Related Files

- `src/CopilotNexus.App/App.axaml.cs`
- `src/CopilotNexus.App/MainWindow.axaml.cs`
- `src/CopilotNexus.App/ViewModels/MainWindowViewModel.cs`
- `src/CopilotNexus.Core/Models/AppState.cs`
- `src/CopilotNexus.Core/Services/JsonStatePersistenceService.cs`
- `src/CopilotNexus.Core/CopilotNexusPaths.cs`
- `src/CopilotNexus.Cli/Program.cs`
- `src/CopilotNexus.Service/Program.cs`