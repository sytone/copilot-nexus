# Copilot Nexus

Copilot Nexus is a Windows desktop app (Avalonia/.NET 8) plus a backend Nexus service for managing multiple Pi runtime sessions with real-time streaming output.

## Architecture

- `CopilotNexus.Service` (ASP.NET Core): session orchestration, REST, SignalR
- `CopilotNexus.App` (Avalonia): tabbed desktop client
- `CopilotNexus.Cli` (`nexus`): install/publish/start/stop/status tooling
- `CopilotNexus.Shim`: generic version-resolving launcher used by CLI/Service/App

## Key capabilities

- Multi-session tabbed workflow
- Pi RPC runtime integration (`pi --mode rpc`)
- Session history/state persistence via Nexus-owned state
- Session profiles and rename support
- Versioned side-by-side publishes with shim-based latest selection

## Quick start

```powershell
dotnet build CopilotNexus.slnx
dotnet run --project src/CopilotNexus.Cli -- install
Set-Alias nexus "$env:LOCALAPPDATA\CopilotNexus\app\cli\CopilotNexus.Cli.exe"
nexus start
nexus winapp start
```

## Publish flow

```powershell
nexus publish                  # publish nexus + app payloads
nexus publish --component cli # publish only CLI payload
```

Publish is side-by-side under `%LOCALAPPDATA%\CopilotNexus\app\<component>\<version>\...`.
No separate `nexus update` copy step is used.

## Testing

```powershell
dotnet test CopilotNexus.slnx
```

## Docs

- [Installation & Operations](docs/installation-and-operations.md)
- [Architecture Overview](docs/architecture-overview.md)
- [API Contracts](docs/api-contracts.md)
- [Testing Guide](docs/testing-guide.md)
- [Configuration Management](docs/configuration-management.md)
