# Copilot Nexus

A Windows desktop application (Avalonia 11 / .NET 8) with a backend service for
managing multiple GitHub Copilot SDK sessions simultaneously.

## Why?

Running multiple Copilot CLI sessions in separate terminal windows is hard to manage.
Copilot Nexus gives you a single window with tabs — each tab is an independent
Copilot SDK session with real-time streaming output, powered by the
[GitHub Copilot SDK](https://github.com/github/copilot-sdk).

## Architecture

The application is split into two processes:

- **Nexus** — ASP.NET Core backend service that owns all Copilot SDK sessions,
  exposed via SignalR (real-time streaming) and REST API
- **App** — Avalonia desktop application (thin SignalR client) that renders session
  output in a tabbed interface

```
┌─────────────────────────┐     ┌────────────────────────────────┐
│  Desktop App (Avalonia)  │◄──►│  Nexus (ASP.NET Core)          │
│  SignalR client          │    │  SignalR hub + REST API         │
│  Renders session tabs    │    │  Manages SDK sessions           │
└─────────────────────────┘     │  Webhook support               │
                                └────────────────────────────────┘
```

## Features

- **Tabbed interface** — Create, switch between, and close Copilot sessions
- **Direct SDK integration** — Uses `GitHub.Copilot.SDK` via JSON-RPC
- **Real-time streaming** — Responses stream word-by-word as they're generated
- **Model selection** — Change AI model per session from the UI
- **Dark terminal theme** — Comfortable for extended use
- **Session persistence** — Resume sessions after restart
- **Auto-update detection** — Staged updates with in-app notification
- **CLI management** — Install, start, stop, update via `nexus` commands

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10/11
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) installed and authenticated

## Quick Start

```powershell
# Build
dotnet build CopilotNexus.slnx

# Install to %LOCALAPPDATA%\CopilotNexus\
dotnet run --project src/CopilotNexus.Service -- install

# Start the Nexus service
dotnet run --project src/CopilotNexus.Service -- start

# In a second terminal — launch the desktop app
dotnet run --project src/CopilotNexus.App

# Or use the Nexus CLI to launch it
dotnet run --project src/CopilotNexus.Service -- winapp start
```

See [Installation & Operations Guide](docs/installation-and-operations.md) for full
setup, update, and troubleshooting instructions.

## Testing

```powershell
dotnet test CopilotNexus.slnx
```

## Solution Structure

```
src/
├── CopilotNexus.Core/       Core business logic, SDK abstractions, shared contracts
├── CopilotNexus.App/        Avalonia desktop application (MVVM, thin SignalR client)
└── CopilotNexus.Service/      ASP.NET Core backend — SignalR hub, REST API, CLI commands

test/
├── CopilotNexus.Core.Tests/    Unit tests for Core (xUnit + Moq)
├── CopilotNexus.App.Tests/     ViewModel and converter tests (xUnit + Moq)
├── CopilotNexus.Service.Tests/   Integration tests for Nexus (WebApplicationFactory)
└── CopilotNexus.UI.Tests/      Headless UI tests (Avalonia.Headless.XUnit)

docs/                             Project documentation
```

## Documentation

- [Installation & Operations Guide](docs/installation-and-operations.md) — Install, run, update, troubleshoot
- [Architecture Overview](docs/architecture-overview.md) — Detailed design and patterns
- [Testing Guide](docs/testing-guide.md) — Test structure and conventions
- [Configuration Management](docs/configuration-management.md) — Runtime args, persisted state, and user-local paths
