# Copilot Nexus — GitHub Copilot Instructions

## Project Overview

Copilot Nexus is a cross-platform desktop application (Avalonia 11.3.12 / .NET 8) that provides a tabbed
interface for managing multiple GitHub Copilot SDK sessions simultaneously. The app follows
a client–service split: the Avalonia desktop app is a thin client that communicates with
**CopilotNexus.Service**, an ASP.NET Core backend service that owns all SDK interactions.
**CopilotNexus.Cli** provides the `nexus` command-line interface for managing the service.
Nexus exposes sessions via SignalR (real-time streaming) and REST (CRUD), enabling future
clients (web UI, CLI, webhook automation) to share the same backend. In test mode, the app
bypasses Nexus and uses local mock services directly.

## Solution Structure

```
src/CopilotNexus.Core         — Core business logic, SDK abstractions, shared DTOs/contracts
src/CopilotNexus.App          — Avalonia 11 desktop application (MVVM, thin SignalR client)
src/CopilotNexus.Cli          — CLI console app — `nexus` commands (start, stop, build, publish, update)
src/CopilotNexus.Service      — ASP.NET Core web host — SignalR hub, REST API, webhooks (~40-line Program.cs)
test/CopilotNexus.Core.Tests  — Unit tests for Core (xUnit + Moq, 65 tests)
test/CopilotNexus.App.Tests   — Unit tests for ViewModels and converters (xUnit + Moq, 47 tests)
test/CopilotNexus.Service.Tests — Integration tests for Nexus (WebApplicationFactory, 20 tests)
test/CopilotNexus.UI.Tests    — Headless UI tests (Avalonia.Headless.XUnit, 17 tests)
docs/                          — Project documentation
.github/                       — GitHub configuration and Copilot instructions
```

## Key Design Patterns

- **MVVM** (Model-View-ViewModel) for the Avalonia application
- **Interface-based abstractions** for testability (`ICopilotClientService`, `ICopilotSessionWrapper`, `ISessionManager`)
- **Event-driven streaming** — SDK session events (deltas, messages, idle) flow through `SessionOutputEventArgs`
- **`IUiDispatcher` abstraction** — decouples ViewModels from Avalonia's `Dispatcher` for pure unit testing

## Key Abstractions

| Abstraction              | Purpose                                                                    |
| ------------------------ | -------------------------------------------------------------------------- |
| `ICopilotClientService`  | Wraps single `CopilotClient` (one per app, JSON-RPC to CLI)                |
| `ICopilotSessionWrapper` | Wraps `CopilotSession` (one per tab, streaming events)                     |
| `ISessionManager`        | Manages lifecycle of multiple sessions                                     |
| `IUiDispatcher`          | Abstracts UI thread dispatching for testability                            |
| `NexusSessionManager`    | `ISessionManager` implementation via SignalR + REST (production mode)      |
| `NexusSessionProxy`      | `ICopilotSessionWrapper` for Nexus-backed sessions (receives SignalR events) |
| `SessionHub`             | SignalR hub in Nexus — `JoinSession`, `LeaveSession`, `SendInput`, `AbortSession` |
| `SessionTabViewModel`    | Manages UI state for a single tab, handles streaming deltas                |
| `MainWindowViewModel`    | Manages the collection of tabs and client lifecycle                        |
| `SessionMessage`         | Observable message model with streaming support (`INotifyPropertyChanged`) |
| `SessionInfo`            | Metadata for a session (id, name, model, state); `FromRemote()` factory for DTO reconstruction |
| `Dtos.cs`                | Shared DTOs: `SessionInfoDto`, `SessionOutputDto`, `ModelInfoDto`, `CreateSessionRequest` |
| `ISessionHubClient`      | SignalR client interface — methods the hub can invoke on connected clients  |

## Building

```bash
dotnet build CopilotNexus.slnx
```

## Testing

```bash
# All tests
dotnet test CopilotNexus.slnx

# Core only
dotnet test test/CopilotNexus.Core.Tests/

# App (ViewModels + converters) only
dotnet test test/CopilotNexus.App.Tests/
```

## Code Style

- Use C# 12 features (file-scoped namespaces, primary constructors where appropriate)
- Follow MVVM pattern strictly for all UI-related code
- All business logic belongs in `CopilotNexus.Core`, never in the App project
- Use interfaces for all services to enable testing and swappability
- Use async/await for all I/O operations
- Keep ViewModels free of direct UI framework dependencies (use `IUiDispatcher`)

## Build Discipline

- **Always run `dotnet build CopilotNexus.slnx` after making changes** and fix all warnings and errors before considering work complete
- Treat warnings as errors — do not leave warnings unresolved
- Run `dotnet test CopilotNexus.slnx` after any code change to verify tests still pass
- If a change breaks the build or tests, fix it immediately before moving on

## Documentation Guidelines

- All documentation lives in the `docs/` folder
- File names use **lowercase kebab-case** (e.g., `architecture-overview.md`, `getting-started.md`)
- Update documentation when making code changes that affect public interfaces or behavior
- The `README.md` at the root is the entry point; link to `docs/` for deeper content

## Development Cycle

Every change — feature, bug fix, or refactor — follows this cycle. The cycle is sequential: **do not skip or reorder steps**. Each step has a clear exit gate before proceeding.

```
┌─────────────────────────────────────────────────────────┐
│                    DEVELOPMENT CYCLE                     │
│                                                          │
│  1. RESEARCH ──────► 2. SPECIFY (BDD) ──────► 3. REVIEW │
│       │                     │                      │     │
│       │  Exit: findings     │  Exit: scenarios     │     │
│       │  documented         │  written             │     │
│       │                     │                      │     │
│       │                     ▼                      │     │
│       │              2b. MAINTAIN BDD ◄────────────┘     │
│       │              (ongoing — update                   │
│       │               when new info                      │
│       │               surfaces)          Exit: design    │
│       │                                  validated       │
│       │                                      │           │
│       │    ┌─────────────────────────────────┘           │
│       │    ▼                                             │
│       │  4. COMMIT CHECKPOINT (pre-implementation)       │
│       │    │                                             │
│       │    ▼                                             │
│       │  5. IMPLEMENT ──────► 6. BUILD & TEST            │
│       │                            │                     │
│       │                  ┌─── fail ┤ pass ───┐           │
│       │                  │         │         │           │
│       │                  ▼         │         ▼           │
│       │             Fix & loop     │   7. COMMIT         │
│       │                            │         │           │
│       │                            │         ▼           │
│       │                            │   8. SUMMARY        │
│       │                            │         │           │
│       │                            │         ▼           │
│       │                            │   Present to user   │
│       └────────────────────────────┘                     │
└─────────────────────────────────────────────────────────┘
```

### Step 1 — Research

Before designing or coding, understand the problem space:

- **Consult the Copilot SDK repo** — check [github/copilot-sdk](https://github.com/github/copilot-sdk) docs for native SDK capabilities before building custom solutions. The SDK may already support features like session persistence, tool registration, or lifecycle management.
- **Investigate best practices** — search for how similar problems are solved in the ecosystem (e.g., Avalonia patterns, SDK usage, desktop app update mechanisms)
- **Review existing code** — understand how the current codebase handles related concerns to maintain consistency
- **Identify constraints** — note platform limitations, SDK quirks, or dependency requirements discovered during research
- **Document findings** — record key research results in the relevant `docs/` file or as comments in the BDD spec

**Exit gate:** Research findings documented. You can articulate *what* the SDK/platform supports and *how* the current codebase handles related concerns.

### Step 2 — BDD Specification

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

**Exit gate:** BDD spec file created with ≥1 happy-path and ≥1 error/edge-case scenario.

### Step 2b — BDD Maintenance (ongoing)

BDD specs are living documents — update them whenever new information surfaces:

- **New SDK capabilities** — if you discover the SDK supports a feature natively (e.g., session persistence), revise the spec to use the SDK capability instead of a custom workaround
- **User steering comments** — when the user provides feedback, corrections, or new requirements, review all affected BDD specs and update scenarios accordingly
- **Research findings** — if research reveals a better approach, update the Research Notes section and revise scenarios that depend on outdated assumptions
- **Bug discoveries** — add new scenarios to cover the failure mode that was found
- **Always check** `docs/bdd/` for specs that reference the area being changed

### Step 3 — Design Review

After writing the BDD specs, **stop and review** the design before implementing:

- Re-read each scenario — does it fully describe the expected behaviour?
- Look for **missing scenarios** — what happens on failure, timeout, invalid input, concurrent access?
- Check for **conflicts** with existing features — does this change break any current behaviour?
- Validate the **technical approach** — is the proposed architecture testable, maintainable, and consistent with existing patterns?
- Consider **performance and resource implications** — file I/O, memory, thread safety
- If the design has issues, revise the BDD specs before proceeding

**Exit gate:** Each scenario is complete, no conflicts found, technical approach validated. If issues found, loop back to Step 2.

### Step 4 — Commit Checkpoint

Before touching any code, **commit any uncommitted work**. This creates a rollback point.

```bash
git add -A && git commit -m "chore: checkpoint before <feature-name>"
```

**Exit gate:** `git status` shows a clean working tree.

### Step 5 — Implementation

Only after the design review passes:

1. Define interfaces in `Core/Interfaces/`
2. Implement services in `Core/Services/`
3. Create ViewModels in `App/ViewModels/`
4. Create Views (AXAML) in `App/Views/`
5. Add unit tests in `test/CopilotNexus.Core.Tests/` and `test/CopilotNexus.App.Tests/`
6. Add headless UI tests in `test/CopilotNexus.UI.Tests/` if the feature has visual elements
7. Update documentation in `docs/` if the feature changes architecture or public API
8. Update `CHANGELOG.md` under the `[Unreleased]` section (see changelog rules below)

### Step 6 — Build & Test

Run the full verification suite:

```bash
dotnet build CopilotNexus.slnx        # must be 0 errors, 0 warnings
dotnet test CopilotNexus.slnx         # all tests must pass
```

- Walk through each BDD scenario and confirm it is satisfied by the implementation
- If any scenario is not covered, add tests or fix the implementation
- **If build/tests fail:** fix and re-run. Do not proceed until green.

**Exit gate:** Build succeeds with 0 warnings, all tests pass, all BDD scenarios satisfied.

### Step 7 — Commit

Commit the completed work using Conventional Commits format:

```bash
git add -A
git commit -m "feat(scope): description

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Step 8 — Dev Cycle Summary

Produce a **metrics summary** and present it to the user. This is the final output of every dev cycle.

| Metric | How to collect |
|---|---|
| **Tests** | Added / Updated / Removed count, plus total pass/fail/skip (`dotnet test`) |
| **Code coverage** | Run `dotnet test --collect:"XPlat Code Coverage"` and report line/branch % (if coverage tooling is configured) |
| **Files** | Added / Modified / Removed (`git diff --stat HEAD~1`) |
| **Lines** | Insertions / Deletions (`git diff --shortstat HEAD~1`) |
| **Build** | Errors / Warnings count |

Example output:

```
📊 Dev Cycle Summary
─────────────────────
Tests:     +5 added, 2 updated, 0 removed │ 134 passed, 0 failed
Coverage:  82.3% line, 71.1% branch
Files:     3 added, 7 modified, 1 removed
Lines:     +210 inserted, −45 deleted
Build:     0 errors, 0 warnings
```

- Use `git diff --stat HEAD~1` and `git diff --shortstat HEAD~1` for file/line metrics
- If code coverage tooling is not yet configured, report "not configured" rather than omitting

### Step 9 — Publish & Stage

After committing, **always publish to the staging folder** so the running application can detect the update and offer a restart.

```bash
# Publish a self-contained build to the staging folder inside dist
dotnet publish src/CopilotNexus.App/CopilotNexus.App.csproj -c Release -o dist/staging --self-contained false --nologo -v q
```

- The running application monitors `dist/staging/` via `StagingUpdateDetectionService`
- When staging files land, the app shows an "Update Available — Restart Now / Later" bar
- On restart, the updater script copies staging → dist root and relaunches
- **This step is mandatory** — the user expects to see the update notification after every dev cycle

**Exit gate:** `dist/staging/` contains the freshly published build. User sees the update notification in the running app.

## Git Workflow

This project uses git for version control. Follow these rules:

- **Commit after every logical set of changes** — each feature, bug fix, or refactor gets its own commit
- **Use Conventional Commits** format (see below)
- **Never leave uncommitted work** at the end of a dev cycle
- **Commit before destructive operations** — if about to delete files, refactor heavily, or change architecture, commit first so rollback is easy
- **Always include the Co-authored-by trailer:**
  ```
  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
  ```
- Use `git --no-pager` for all git commands to avoid interactive pagers

### Commit Cadence

| Situation | Action |
|---|---|
| Feature complete + tests pass | Commit |
| About to delete/rename files | Commit current state first |
| About to refactor | Commit current state first |
| Bug fix verified | Commit |
| Documentation update | Commit (can batch with related code change) |
| End of session | Commit any uncommitted work |

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

- Web client — browser-based UI connecting to Nexus via SignalR + REST
- CLI client — terminal-based session management against the Nexus API
- Webhook automation — CI/CD pipelines triggering sessions via `POST /api/webhooks/sessions/{id}/message`
- Multi-user session sharing — multiple clients collaborating on the same session in real time
- Custom copilot CLI path configuration via settings
- Theme customization (light/dark/custom)
- Session output search and filtering
- Drag-and-drop tab reordering
- Split-pane view for comparing sessions side-by-side
- Tool registration — expose custom C# functions to the Copilot agent
- Keyboard shortcut customization
- Export session transcripts
