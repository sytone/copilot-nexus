# Feature: Pi RPC runtime as the default Nexus interface

Nexus should use Pi RPC as its runtime interface by default and avoid exposing adapter-selection complexity in public app/service contracts.

## Research Notes

- Pi supports process integration via JSON RPC over stdin/stdout (`pi --mode rpc`).
- Pi RPC protocol includes prompt, abort, get_state, get_messages, and model discovery commands.
- Pi can broker execution against multiple providers, including GitHub Copilot-backed paths.

## Background

```gherkin
Given Nexus is configured to run sessions through Pi RPC
And public DTOs do not expose adapter-selection fields
And profile/tab/session payloads carry model, path, mode, MCP, and agent settings
```

## Scenarios

### Scenario: Create session uses Pi runtime contract

```gherkin
Given a client sends CreateSessionRequest without any adapter field
When Nexus creates a new session
Then the session is created via PiRpcClientService
And SessionInfoDto excludes adapter metadata
```

### Scenario: Model listing is runtime-simple

```gherkin
Given Nexus has initialized Pi runtime model metadata
When a client calls GET /api/models
Then Nexus returns model information without adapter query requirements
```

### Scenario: Reconfigure keeps session identity

```gherkin
Given an existing Pi-backed session
When ConfigureSessionRequest updates model or path
Then Nexus reconnects/reconfigures through the same Pi runtime path
And the session ID remains stable for the client tab
```

### Scenario: Resume gracefully handles missing persisted runtime state

```gherkin
Given a client asks to resume a session by sdkSessionId
And runtime reports "Session not found"
When Nexus handles the resume
Then Nexus creates a fresh session with the same sdkSessionId
And the request succeeds instead of returning a server error
```
