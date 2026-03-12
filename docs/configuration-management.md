# Configuration Management

This document explains where Copilot Nexus configuration comes from, how values are
applied, and what is persisted per user.

## Configuration Sources

Configuration is currently managed through three main sources:

1. Command-line arguments
2. Nexus-owned persisted state under `%LOCALAPPDATA%\\CopilotNexus\\state`
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
- `nexus publish --component <nexus|app|cli|both>`
- `nexus update --component <nexus|app|cli|both>`
- `nexus winapp start --nexus-url <url> --test-mode`

## User-Specific Storage

Install/runtime binaries remain under `%LOCALAPPDATA%\CopilotNexus\`.

Primary paths are centralized in `src/CopilotNexus.Core/CopilotNexusPaths.cs`:

- `StateRoot`: `%LOCALAPPDATA%\\CopilotNexus\\state`
- `NexusAppStateFile`: `%LOCALAPPDATA%\\CopilotNexus\\state\\session-state.json`
- `NexusSessionProfilesFile`: `%LOCALAPPDATA%\\CopilotNexus\\state\\session-profiles.json`
- `UserConfigRoot`: `%USERPROFILE%\\.copilot-nexus` (test-mode fallback)
- `AppStateFile`: `%USERPROFILE%\\.copilot-nexus\\session-state.json` (test-mode fallback)
- `Root`: `%LOCALAPPDATA%\CopilotNexus`
- `CliInstall`: `%LOCALAPPDATA%\CopilotNexus\cli`
- `NexusInstall`: `%LOCALAPPDATA%\CopilotNexus\nexus`
- `AppInstall`: `%LOCALAPPDATA%\CopilotNexus\app`
- `StagingRoot`: `%LOCALAPPDATA%\CopilotNexus\staging`
- `Logs`: `%LOCALAPPDATA%\CopilotNexus\logs`
- `NexusLockFile`: `%LOCALAPPDATA%\CopilotNexus\nexus.lock`

## Session Information Persistence

Session metadata used by the desktop app is persisted by Nexus via
`/api/app-state`, backed by `JsonStatePersistenceService` on the service host.

Persisted file:

- `%LOCALAPPDATA%\\CopilotNexus\\state\\session-state.json`

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
- Orphaned temporary writes (`.tmp`) are recovered on next load.

## Settings vs State

There is currently no separate strongly-typed "user settings" file (for example,
theme or preferences) outside of persisted app/session state.

In practice, user-specific behavior is represented by:

- Startup flags (for example `--test-mode`, `--nexus-url`)
- Nexus-managed persisted `session-state.json` metadata
- Install/update layout and lock/log files under `%LOCALAPPDATA%\CopilotNexus`

## Environment Variables

Current usage is minimal:

- `NEXUS_TEST_MODE=1`: Used by the Service host to suppress lock-file handling during
  integration tests (`src/CopilotNexus.Service/Program.cs`).

## Restore and Save Flow

1. `MainWindow` initializes session manager.
2. If `--reset-state` is set, persisted state is cleared.
3. App loads state from Nexus (`GET /api/app-state`).
4. `MainWindowViewModel.RestoreStateAsync` resumes sessions by `SdkSessionId`.
5. On app close (and hot restart), current metadata is captured and saved via Nexus (`PUT /api/app-state`).

## Session Profile Persistence

Session profiles are Nexus-owned and shared across clients via:

- `GET /api/session-profiles`
- `POST /api/session-profiles`
- `PUT /api/session-profiles/{id}`
- `DELETE /api/session-profiles/{id}`

Persisted file:

- `%LOCALAPPDATA%\\CopilotNexus\\state\\session-profiles.json`

Profiles store defaults for model, mode, working directory, optional custom agent file,
and MCP behavior (well-known discovery + additional config files + enabled server filter).

## Related Files

- `src/CopilotNexus.App/App.axaml.cs`
- `src/CopilotNexus.App/MainWindow.axaml.cs`
- `src/CopilotNexus.App/ViewModels/MainWindowViewModel.cs`
- `src/CopilotNexus.Core/Models/AppState.cs`
- `src/CopilotNexus.Core/Services/JsonStatePersistenceService.cs`
- `src/CopilotNexus.Core/CopilotNexusPaths.cs`
- `src/CopilotNexus.Cli/Program.cs`
- `src/CopilotNexus.Service/Program.cs`
