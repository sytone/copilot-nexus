# API Contracts

This document describes the public Nexus service contracts used by the desktop app and future clients.

Nexus is Pi-based by default and uses Pi coding agent RPC (`pi --mode rpc`) as its runtime interface.

## REST API

Base URL: `http://localhost:5280` (default)

### Sessions

- `GET /api/sessions` — list active sessions.
- `GET /api/sessions/{id}` — get one session.
- `GET /api/sessions/{id}/history` — get persisted output history (`SessionOutputDto[]`).
- `POST /api/sessions` — create or resume a session.
- `PUT /api/sessions/{id}/configure` — reconfigure model/mode/path/profile/agent settings.
- `PUT /api/sessions/{id}/name` — rename a session.
- `POST /api/sessions/{id}/input` — send input text.
- `DELETE /api/sessions/{id}` — close and delete the session from disk.

`POST /api/sessions` request shape (`CreateSessionRequest`):

- `name` (optional)
- `sdkSessionId` (optional; when provided, Nexus attempts resume)
- `model`
- `workingDirectory`
- `isAutopilot`
- `profileId`
- `agentFilePath`
- `includeWellKnownMcpConfigs`
- `additionalMcpConfigPaths[]`
- `enabledMcpServers[]`
- `skillDirectories[]`
- `initialMessage` (optional)

If resume is requested and the runtime reports `Session not found`, Nexus automatically creates a fresh session using the same `sdkSessionId`.

Common action flows:

- **Switch model**: `PUT /api/sessions/{id}/configure` with `model`.
- **Apply agent/profile config**: read profile details from `GET /api/session-profiles`, then send `PUT /api/sessions/{id}/configure` with `profileId`, `agentFilePath`, MCP fields, and skill directories.

### Models

- `GET /api/models` — list available Pi runtime models discovered from Pi RPC `get_available_models`.

### Session profiles

- `GET /api/session-profiles`
- `GET /api/session-profiles/{id}`
- `POST /api/session-profiles`
- `PUT /api/session-profiles/{id}`
- `DELETE /api/session-profiles/{id}`

Profiles are Nexus-owned and include reusable defaults like `model`, `workingDirectory`, MCP config behavior, and optional custom agent path.

### App state

- `GET /api/app-state` — load persisted desktop state.
- `PUT /api/app-state` — save desktop state.
- `DELETE /api/app-state` — clear desktop state.

`TabState` includes `profileId`, MCP config settings, and skill directories in addition to `name`, `model`, and `sdkSessionId`.

### Webhooks

- `POST /api/webhooks/sessions` — create session and send message.
- `POST /api/webhooks/sessions/{id}/message` — send message to existing session.
- `POST /api/webhooks/sessions/{id}/abort` — abort in-flight work.

Webhook create requests accept `model`, `workingDirectory`, and `isAutopilot`.

## SignalR hub contract

Hub route: `/hubs/session`

Client-to-server hub methods:

- `JoinSession(sessionId)`
- `LeaveSession(sessionId)`
- `SendInput(sessionId, input)`
- `AbortSession(sessionId)`

Server-to-client callbacks (`ISessionHubClient`):

- `SessionOutput(sessionId, SessionOutputDto output)`
- `SessionStateChanged(sessionId, state)`
- `SessionAdded(SessionInfoDto session)`
- `SessionRemoved(sessionId)`
- `ModelsLoaded(ModelInfoDto[] models)`
- `SessionReconfigured(SessionInfoDto session)`

`SessionOutputDto` includes `correlationId` to support incremental UI updates for activity/reasoning blocks.
