# Feature: Runtime adapter selection with transparent API contracts

Nexus should support both Pi RPC and GitHub Copilot SDK runtimes behind the same
public service contracts. Runtime selection is an operational concern (CLI/service startup),
not a client API concern.

## Research Notes

- `IAgentClientService` is the runtime abstraction used by `SessionManager`.
- Pi runtime implementation: `PiRpcClientService`.
- Copilot SDK implementation: `CopilotClientService`.
- Public DTOs and request payloads intentionally avoid adapter-specific fields.

## Background

```gherkin
Given Nexus supports runtime adapters "pi" and "copilot-sdk"
And Pi is the default runtime when no explicit selection is persisted or provided
And public DTOs do not expose adapter-selection fields
And profile/tab/session payloads carry model, path, mode, MCP, and agent settings
```

## Scenarios

### Scenario: Default startup uses Pi runtime

```gherkin
Given no runtime override is provided
And no persisted runtime selection exists
When Nexus service starts
Then Nexus registers PiRpcClientService as IAgentClientService
And clients continue using the same REST and SignalR contracts
```

### Scenario: CLI startup override selects Copilot SDK runtime

```gherkin
Given the user runs "nexus start --agent copilot-sdk"
When Nexus service starts
Then Nexus registers CopilotClientService as IAgentClientService
And the runtime selection is persisted for future starts
And clients continue using unchanged API payloads
```

### Scenario: Model listing stays adapter-agnostic

```gherkin
Given Nexus has initialized the active runtime adapter
When a client calls GET /api/models
Then Nexus returns model information from the active runtime
And the response shape remains ModelInfoDto[]
```

### Scenario: Session lifecycle contracts are stable across runtimes

```gherkin
Given a client sends CreateSessionRequest without any adapter field
When Nexus creates, configures, sends input to, and deletes a session
Then the request/response contract is unchanged for the client
And SessionInfoDto excludes adapter metadata
```

### Scenario: Resume gracefully handles missing runtime state

```gherkin
Given a client asks to resume a session by sdkSessionId
And the active runtime reports "Session not found"
When Nexus handles the resume
Then Nexus creates a fresh session with the same sdkSessionId
And the request succeeds instead of returning a server error
```
