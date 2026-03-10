# Copilot Family — GitHub Copilot Instructions

## Project Overview

Copilot Family is a Windows desktop application (WPF / .NET 8) that provides a tabbed
interface for managing multiple GitHub Copilot SDK sessions simultaneously. Each tab
runs an independent Copilot session via the `GitHub.Copilot.SDK` NuGet package,
communicating with the Copilot CLI over JSON-RPC — not by wrapping a CLI process.

## Solution Structure

```
src/CopilotFamily.Core         — Core business logic, SDK abstractions, models, services
src/CopilotFamily.App          — WPF desktop application with MVVM architecture
test/CopilotFamily.Core.Tests  — Unit tests for Core (xUnit + Moq)
test/CopilotFamily.App.Tests   — Unit tests for ViewModels and converters (xUnit + Moq)
docs/                          — Project documentation
.github/                       — GitHub configuration and Copilot instructions
```

## Key Design Patterns

- **MVVM** (Model-View-ViewModel) for the WPF application
- **Interface-based abstractions** for testability (`ICopilotClientService`, `ICopilotSessionWrapper`, `ISessionManager`)
- **Event-driven streaming** — SDK session events (deltas, messages, idle) flow through `SessionOutputEventArgs`
- **`IUiDispatcher` abstraction** — decouples ViewModels from WPF's `Dispatcher` for pure unit testing

## Key Abstractions

| Abstraction              | Purpose                                                                    |
| ------------------------ | -------------------------------------------------------------------------- |
| `ICopilotClientService`  | Wraps single `CopilotClient` (one per app, JSON-RPC to CLI)                |
| `ICopilotSessionWrapper` | Wraps `CopilotSession` (one per tab, streaming events)                     |
| `ISessionManager`        | Manages lifecycle of multiple sessions                                     |
| `IUiDispatcher`          | Abstracts UI thread dispatching for testability                            |
| `SessionTabViewModel`    | Manages UI state for a single tab, handles streaming deltas                |
| `MainWindowViewModel`    | Manages the collection of tabs and client lifecycle                        |
| `SessionMessage`         | Observable message model with streaming support (`INotifyPropertyChanged`) |
| `SessionInfo`            | Metadata for a session (id, name, model, state)                            |

## Building

```bash
dotnet build CopilotFamily.slnx
```

## Testing

```bash
# All tests
dotnet test CopilotFamily.slnx

# Core only
dotnet test test/CopilotFamily.Core.Tests/

# App (ViewModels + converters) only
dotnet test test/CopilotFamily.App.Tests/
```

## Code Style

- Use C# 12 features (file-scoped namespaces, primary constructors where appropriate)
- Follow MVVM pattern strictly for all UI-related code
- All business logic belongs in `CopilotFamily.Core`, never in the App project
- Use interfaces for all services to enable testing and swappability
- Use async/await for all I/O operations
- Keep ViewModels free of direct UI framework dependencies (use `IUiDispatcher`)

## Build Discipline

- **Always run `dotnet build CopilotFamily.slnx` after making changes** and fix all warnings and errors before considering work complete
- Treat warnings as errors — do not leave warnings unresolved
- Run `dotnet test CopilotFamily.slnx` after any code change to verify tests still pass
- If a change breaks the build or tests, fix it immediately before moving on

## Documentation Guidelines

- All documentation lives in the `docs/` folder
- File names use **lowercase kebab-case** (e.g., `architecture-overview.md`, `getting-started.md`)
- Update documentation when making code changes that affect public interfaces or behavior
- The `README.md` at the root is the entry point; link to `docs/` for deeper content

## Feature Development Process

Follow this process for every new feature, bug fix, or significant change:

### 1. Research

Before designing or coding, spend time understanding the problem space:

- **Consult the Copilot SDK repo** — check [github/copilot-sdk](https://github.com/github/copilot-sdk) docs for native SDK capabilities before building custom solutions. The SDK may already support features like session persistence, tool registration, or lifecycle management.
- **Investigate best practices** — search for how similar problems are solved in the ecosystem (e.g., WPF patterns, SDK usage, desktop app update mechanisms)
- **Review existing code** — understand how the current codebase handles related concerns to maintain consistency
- **Identify constraints** — note platform limitations, SDK quirks, or dependency requirements discovered during research
- **Document findings** — record key research results in the relevant `docs/` file or as comments in the BDD spec

### 2. BDD Specification

Write Gherkin-style feature specs **before** writing any implementation code:

- Create feature specs in `docs/bdd/` using lowercase kebab-case filenames (e.g., `session-persistence.md`)
- Each spec file starts with a **Feature** description explaining the user value
- Include a **Background** section for shared context
- Write **Scenarios** covering:
  - The primary happy path
  - Edge cases and error handling
  - Boundary conditions
  - Interactions with existing features
- Use concrete examples with realistic data, not abstract placeholders

### 2b. BDD Maintenance

BDD specs are living documents — update them whenever new information surfaces:

- **New SDK capabilities** — if you discover the SDK supports a feature natively (e.g., session persistence), revise the spec to use the SDK capability instead of a custom workaround
- **User steering comments** — when the user provides feedback, corrections, or new requirements, review all affected BDD specs and update scenarios accordingly
- **Research findings** — if research reveals a better approach, update the Research Notes section and revise scenarios that depend on outdated assumptions
- **Bug discoveries** — add new scenarios to cover the failure mode that was found
- **Always check** `docs/bdd/` for specs that reference the area being changed

### 3. Design Review

After writing the BDD specs, **stop and review** the design before implementing:

- Re-read each scenario — does it fully describe the expected behaviour?
- Look for **missing scenarios** — what happens on failure, timeout, invalid input, concurrent access?
- Check for **conflicts** with existing features — does this change break any current behaviour?
- Validate the **technical approach** — is the proposed architecture testable, maintainable, and consistent with existing patterns?
- Consider **performance and resource implications** — file I/O, memory, thread safety
- If the design has issues, revise the BDD specs before proceeding

### 4. Implementation

Only after the BDD specs pass review:

1. Define interfaces in `Core/Interfaces/`
2. Implement services in `Core/Services/`
3. Create ViewModels in `App/ViewModels/`
4. Create Views (XAML) in `App/Views/`
5. Add unit tests in both `test/CopilotFamily.Core.Tests/` and `test/CopilotFamily.App.Tests/`
6. Update documentation in `docs/` if the feature changes architecture or public API
7. Update `CHANGELOG.md` under the `[Unreleased]` section (see changelog rules below)

### 5. Verification

After implementation:

- Run `dotnet build CopilotFamily.slnx` — fix all warnings and errors
- Run `dotnet test CopilotFamily.slnx` — all tests must pass
- Walk through each BDD scenario and confirm it is satisfied by the implementation
- If any scenario is not covered, add tests or fix the implementation

## Commits and Changelog

- **Conventional Commits** are required. See `.github/skills/conventional-commit/SKILL.md` for the full specification.
- Format: `type(scope): description` (e.g., `feat(app): add model selector`, `fix(core): handle null delta`)
- Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`
- Scopes: `core`, `app`, `ui`, `tests`, `docs`, `ci`
- **Always update `CHANGELOG.md`** in the project root when making a `feat`, `fix`, or breaking change:
  - Add entries under the `[Unreleased]` section
  - Group by: `Added`, `Fixed`, `Changed`, `Removed`
  - Each entry is a single-line bullet describing the change
- When fixing a bug, include the root cause in the changelog entry
- When adding a feature, describe the user-facing behaviour

## Future Enhancement Areas

- Session persistence and restoration across app restarts
- Model selection per session (dropdown in tab bar)
- Custom copilot CLI path configuration via settings
- Theme customization (light/dark/custom)
- Session output search and filtering
- Drag-and-drop tab reordering
- Split-pane view for comparing sessions side-by-side
- Tool registration — expose custom C# functions to the Copilot agent
- Keyboard shortcut customization
- Export session transcripts
