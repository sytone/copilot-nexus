# Copilot Family

A Windows desktop application (WPF / .NET 8) that provides a tabbed interface for
managing multiple GitHub Copilot SDK sessions simultaneously.

## Why?

Running multiple Copilot CLI sessions in separate terminal windows is hard to manage.
Copilot Family gives you a single window with tabs — each tab is an independent
Copilot SDK session with real-time streaming output, powered by the
[GitHub Copilot SDK](https://github.com/github/copilot-sdk).

## Features

- **Tabbed interface** — Create, switch between, and close Copilot sessions
- **Direct SDK integration** — Uses `GitHub.Copilot.SDK` via JSON-RPC, not process wrapping
- **Real-time streaming** — Responses stream word-by-word as they're generated
- **Dark terminal theme** — Comfortable for extended use
- **Session lifecycle** — Active sessions with abort support per tab
- **Status indicators** — Green dot for running sessions, grey for stopped
- **Keyboard shortcuts** — `Ctrl+T` new tab, `Ctrl+W` close tab, `Enter` send input
- **Auto-scroll** — Output auto-scrolls to the latest message

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows (WPF)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) installed and authenticated

## Quick Start

```bash
# Build
dotnet build CopilotFamily.slnx

# Run
dotnet run --project src/CopilotFamily.App

# Test
dotnet test CopilotFamily.slnx
```

## Architecture

The app uses the **MVVM pattern** with the GitHub Copilot SDK for direct API integration.
A single `CopilotClient` (JSON-RPC connection to the CLI) is shared across tabs, while
each tab owns an independent `CopilotSession` with its own conversation and streaming events.

```
src/
├── CopilotFamily.Core/           # Business logic (SDK abstractions, no UI deps)
│   ├── Interfaces/                # ICopilotClientService, ICopilotSessionWrapper,
│   │                              # ISessionManager, IUiDispatcher
│   ├── Models/                    # SessionMessage, SessionInfo, SessionState, OutputKind
│   ├── Services/                  # CopilotClientService, CopilotSessionWrapper,
│   │                              # SessionManager
│   └── Events/                    # SessionOutputEventArgs
├── CopilotFamily.App/             # WPF application (MVVM)
│   ├── ViewModels/                # MainWindowViewModel, SessionTabViewModel
│   ├── Views/                     # SessionTabView
│   ├── Converters/                # Value converters for XAML bindings
│   └── Services/                  # WpfUiDispatcher
test/
├── CopilotFamily.Core.Tests/     # Core unit tests (xUnit + Moq)
└── CopilotFamily.App.Tests/      # ViewModel + converter tests (xUnit + Moq)
docs/                              # Project documentation
```

See [docs/architecture-overview.md](docs/architecture-overview.md) for detailed design.

## Documentation

All documentation lives in the [`docs/`](docs/) folder using lowercase kebab-case file names.

- [Architecture overview](docs/architecture-overview.md)
