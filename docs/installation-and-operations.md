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
nexus --previous version

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
| `nexus start [--url URL]` | Start Nexus service |
| `nexus stop` | Stop Nexus service |
| `nexus status [--url URL]` | Show service/runtime status |
| `nexus version` | Show shim and latest payload versions |
| `nexus build [-c CONFIG]` | Build solution |
| `nexus install` | Install/refresh shims + publish payloads |
| `nexus publish [--component C]` | Publish versioned payloads (`nexus`, `app`, `cli`, `both`) |
| `nexus winapp start [...]` | Launch desktop app |

## Troubleshooting

### Service won't start

```powershell
nexus status
Get-Content "$env:LOCALAPPDATA\CopilotNexus\nexus.lock"
```

Check logs in `%LOCALAPPDATA%\CopilotNexus\logs\`.

### Validate Pi runtime

```powershell
pi --version
$env:NEXUS_PI_EXECUTABLE = "C:\path\to\pi.cmd"
nexus start
```

### Review only new warnings/errors since last checkpoint

```powershell
pwsh -File .\scripts\Review-NexusLogs.ps1
```
