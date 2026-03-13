# Runtime Adapter Abstraction Plan (Pi RPC + GitHub Copilot SDK)

## Purpose

Define how Nexus can support **interchangeable runtime backends** (Pi RPC and GitHub Copilot SDK) selected via CLI, while keeping app clients and public API contracts stable.

## Summary Decision

- Keep the existing runtime abstraction (`IAgentClientService`) as the single service contract.
- Add runtime selection at service startup (`pi` or `copilot-sdk`) through Nexus CLI.
- Persist the selected runtime in Nexus-owned state so restarts keep behavior.
- Preserve REST/SignalR/webhook request/response contracts as-is.

No breaking API change is required for clients.

## Current-State Findings

### What is already abstraction-friendly

- `IAgentClientService` already defines runtime-agnostic lifecycle/session operations:
  - `StartAsync`, `StopAsync`, `ListModelsAsync`, `CreateSessionAsync`, `ResumeSessionAsync`, `DeleteSessionAsync`
  - File: `src/CopilotNexus.Core/Interfaces/IAgentClientService.cs`
- `SessionManager` depends on `IAgentClientService`, not a concrete runtime.
  - File: `src/CopilotNexus.Core/Services/SessionManager.cs`
- Service controllers and SignalR hub depend on `ISessionManager` and DTOs only.
  - Files:
    - `src/CopilotNexus.Service/Controllers/SessionsController.cs`
    - `src/CopilotNexus.Service/Controllers/ModelsController.cs`
    - `src/CopilotNexus.Service/Hubs/SessionHub.cs`
- DTO contracts are runtime-neutral and contain no adapter selection fields.
  - File: `src/CopilotNexus.Core/Contracts/Dtos.cs`

### What is currently hardcoded

- Service DI always registers Pi runtime:
  - `IAgentClientService -> PiRpcClientService`
  - File: `src/CopilotNexus.Service/NexusHostBuilder.cs`
- CLI `start` only accepts `--url`; no runtime selection switch.
  - Files:
    - `src/CopilotNexus.Cli/Program.cs`
    - `src/CopilotNexus.Cli/CliCommands.cs`
- Docs are Pi-specific in several places.
  - Files:
    - `docs/api-contracts.md`
    - `docs/architecture-overview.md`
    - `docs/bdd/pi-rpc-adapter.md`

### Existing runtime implementations

- Pi runtime implementation:
  - `src/CopilotNexus.Core/Services/PiRpcClientService.cs`
- Copilot SDK implementation:
  - `src/CopilotNexus.Core/Services/CopilotClientService.cs`
- Both satisfy the shared runtime interface, so they can be selected at bootstrap.

## Target Architecture

## 1) Runtime selection model

Introduce a runtime enum/string setting:

- `pi` (default)
- `copilot-sdk`

Selection precedence:

1. CLI explicit flag for current start (`nexus start --agent ...`)
2. Persisted service setting (state file)
3. Default fallback: `pi`

## 2) Service bootstrap selection

Refactor Nexus host builder registration so runtime adapter is chosen from config:

- If `agent=pi` → register `PiRpcClientService`
- If `agent=copilot-sdk` → register `CopilotClientService`

All downstream dependencies remain unchanged because they already depend on `IAgentClientService` and `ISessionManager`.

## 3) CLI control surface

Add CLI runtime choice:

- `nexus start --agent pi`
- `nexus start --agent copilot-sdk`

Optional helper command (additive):

- `nexus runtime get`
- `nexus runtime set --agent <...>`

## 4) Persisted runtime config

Add Nexus-owned config file under state root (example):

- `%LOCALAPPDATA%\CopilotNexus\state\runtime-config.json`

Example schema:

```json
{
  "agent": "pi"
}
```

Use defensive read/write:

- Missing file → default to `pi`
- Invalid JSON/value → log warning, default to `pi`
- Atomic write pattern (tmp + replace), consistent with existing state services

## API Impact Assessment

### Required API changes

- **None required** for compatibility.

Reason: REST/SignalR/webhook contracts already operate at session/model/message level and do not expose runtime adapter identity.

### Optional additive API changes (non-breaking)

If observability is desired:

- Add `agent` field to `/health` response
- Optionally add a new endpoint:
  - `GET /api/runtime` returning current runtime selection

These are additive and safe for existing clients that ignore unknown JSON fields.

## Detailed Implementation Plan

## Phase 1 — Runtime config model + service

Add:

- `RuntimeAgentType` enum or validated string constants
- `RuntimeConfig` model
- `IRuntimeConfigService` + `JsonRuntimeConfigService`
- Path constant in `CopilotNexusPaths` for runtime config file

Projects:

- `CopilotNexus.Core`

## Phase 2 — Service DI runtime selection

Update service bootstrap:

- Read runtime config + startup override
- Register matching `IAgentClientService` implementation in `NexusHostBuilder`

Projects:

- `CopilotNexus.Service`

## Phase 3 — CLI runtime selection

Update CLI:

- Extend `start` command with `--agent` option
- Pass selection to service startup path (args/env/config write)
- Persist successful selection

Projects:

- `CopilotNexus.Cli`

## Phase 4 — API and docs alignment

Keep current API contracts unchanged; optionally add runtime observability.

Update docs that currently describe Pi as the only runtime:

- `docs/api-contracts.md`
- `docs/architecture-overview.md`
- `docs/configuration-management.md`
- `docs/bdd/pi-rpc-adapter.md` (or replace with a runtime-selection BDD spec)

## Phase 5 — Test coverage

Add/update tests:

- Service host selection tests (`pi` vs `copilot-sdk`)
- CLI parsing tests for `--agent`
- Integration tests verifying API behavior is identical across runtimes
- Optional `/health` runtime field assertions if added

## Risks and Mitigations

1. Runtime-specific startup failures
   - Mitigation: preserve existing start validation (`/health`, `/api/models`, create/delete probe)
2. Config corruption
   - Mitigation: fail-safe default to `pi`
3. Session resume semantics differ by runtime
   - Mitigation: keep existing resume fallback behavior in `SessionManager`
4. Client coupling to Pi-specific assumptions
   - Mitigation: maintain DTO/API neutrality and avoid adapter fields in session contracts

## Compatibility Statement

With this plan:

- App clients remain unchanged.
- REST/SignalR/webhook payloads remain unchanged.
- Runtime selection is operational/configuration concern handled by CLI + service bootstrap.

## Recommended Default

- Keep `pi` as default runtime for backward compatibility.
- Introduce `copilot-sdk` as an opt-in runtime selection.
- Treat runtime metadata in API as optional observability, not required contract surface.
