# Installation & Operations Guide

This guide covers install, publish, rollback, and runtime operations for Copilot Nexus.

## Prerequisites

- .NET 8 SDK or later
- Windows 10/11
- Pi CLI installed and authenticated (`pi --version`)
- Repository cloned locally

## Install layout

Everything installs under `%LOCALAPPDATA%\CopilotNexus\`:

```
%LOCALAPPDATA%\CopilotNexus\
├── app\
│   ├── cli\
│   │   ├── CopilotNexus.Cli.exe          (shim)
│   │   └── <version>\CopilotNexus.Cli.exe
│   ├── service\
│   │   ├── CopilotNexus.Service.exe      (shim)
│   │   └── <version>\CopilotNexus.Service.exe
│   └── winapp\
│       ├── CopilotNexus.App.exe          (shim)
│       ├── CopilotNexus.Updater.exe
│       └── <version>\...
├── logs\
├── state\
│   ├── session-state.json
│   ├── session-profiles.json
│   └── publish-version-state.json
└── nexus.lock
```

Each shim launches the newest versioned payload by default.

## Build

```powershell
dotnet build CopilotNexus.slnx
# or
nexus build
```

## First-time install

```powershell
dotnet run --project src/CopilotNexus.Cli -- install
```

This installs/refreshes shims and publishes versioned payloads.

Set alias:

```powershell
Set-Alias nexus "$env:LOCALAPPDATA\CopilotNexus\app\cli\CopilotNexus.Cli.exe"
```

## Publish new versions

```powershell
# publish nexus + app payloads
nexus publish

# publish one component
nexus publish --component nexus
nexus publish --component app
nexus publish --component cli
```

`publish` writes side-by-side versions; it does not copy files over running binaries.

Development publishes use `X.Y.Z-dev.YYYYMMDDHHMMSS`.
Release-mode publishes (tag/release env) use plain `X.Y.Z`.

## Service lifecycle

```powershell
nexus start --url http://localhost:5280
nexus restart --url http://localhost:5280
nexus status
nexus stop
```

`nexus start` validates readiness (`/health`, `/api/models`, session create/delete probe) before reporting success.

## Launch desktop app

```powershell
nexus winapp start
nexus winapp start --nexus-url http://localhost:5280
nexus winapp start --test-mode
```

## Rollback and cleanup

Shim flags are supported by all shims:

```powershell
# run previous CLI payload for this invocation
nexus --previous

# keep latest 5 CLI payload versions, delete older ones
nexus --cleanup 5
```

Service and app shims can be invoked directly:

```powershell
& "$env:LOCALAPPDATA\CopilotNexus\app\service\CopilotNexus.Service.exe" --previous --urls http://localhost:5280
& "$env:LOCALAPPDATA\CopilotNexus\app\winapp\CopilotNexus.App.exe" --cleanup 3
```

## Command reference

| Command | Description |
|---|---|
| `nexus start [--url URL] [--agent pi\|copilot-sdk]` | Start Nexus service |
| `nexus restart [--url URL] [--agent pi\|copilot-sdk]` | Restart Nexus service |
| `nexus stop` | Stop Nexus service |
| `nexus status [--url URL]` | Show service/runtime status |
| `nexus version` | Show shim and latest payload versions |
| `nexus build [-c CONFIG]` | Build solution |
| `nexus install` | Install/refresh shims + publish payloads |
| `nexus publish [--component C]` | Publish versioned payloads (`nexus`, `app`, `cli`, `both`) |
| `nexus winapp start [...]` | Launch desktop app |
| `nexus dev watch [--url URL]` | Start DevAssistant (log watcher + HTTP action server on port 5290) |
| `nexus dev rebuild` | Trigger solution rebuild via DevAssistant |
| `nexus dev publish` | Trigger component publish via DevAssistant |
| `nexus dev restart` | Trigger service restart via DevAssistant |
| `nexus dev republish` | Rebuild, publish, and restart in sequence via DevAssistant |
| `nexus dev status` | Show DevAssistant status (uptime, open issues) |
| `nexus dev issues` | List open issues detected by DevAssistant |

## Development Assistant

The DevAssistant is a long-running CLI tool that provides:

1. **Log watching** — monitors `%LOCALAPPDATA%\CopilotNexus\logs\` for new errors and warnings
2. **Issue creation** — auto-generates issue markdown files in `docs/issues/` with error details and suggested fix prompts
3. **HTTP action API** — exposes `http://localhost:5290` for triggering build, publish, and restart actions

### Quick start

```powershell
# Start the watcher (runs until Ctrl+C)
nexus dev watch

# In another terminal, trigger actions:
nexus dev republish     # rebuild → publish → restart
nexus dev status        # check watcher status
nexus dev issues        # list detected issues
```

### HTTP API reference

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/status` | DevAssistant status (uptime, open issues count) |
| `GET` | `/api/issues` | List all detected issues |
| `POST` | `/api/issues/{fileName}/resolve` | Mark an issue as resolved |
| `POST` | `/api/actions/rebuild` | Run `dotnet build` on the solution |
| `POST` | `/api/actions/publish` | Publish all components |
| `POST` | `/api/actions/restart` | Stop and restart the Nexus service |
| `POST` | `/api/actions/republish` | Rebuild + publish + restart in sequence |

### Running directly (without shim)

```powershell
dotnet run --project src/CopilotNexus.DevAssistant -- watch
```

## Troubleshooting

### Service won't start

```powershell
nexus status
Get-Content "$env:LOCALAPPDATA\CopilotNexus\nexus.lock"
```

Check logs in `%LOCALAPPDATA%\CopilotNexus\logs\`.

### Select runtime adapter

```powershell
nexus start --agent pi
nexus start --agent copilot-sdk
```

### Validate runtime prerequisites

```powershell
pi --version
$env:NEXUS_PI_EXECUTABLE = "C:\path\to\pi.cmd"
nexus start --agent pi

# Copilot SDK runtime (requires GitHub Copilot auth in your environment)
nexus start --agent copilot-sdk
```

### Review only new warnings/errors since last checkpoint

```powershell
pwsh -File .\scripts\Review-NexusLogs.ps1
```
