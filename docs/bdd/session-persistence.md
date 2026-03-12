# Feature: Session Persistence and Restoration

The application saves its state on exit (or before a hot restart) and restores
the exact same tabs, conversation history, and Copilot sessions on the next
launch. The user returns to exactly where they left off.

## Research Notes

- **Copilot SDK native session persistence**: The SDK supports session persistence
  natively. By providing a custom `SessionId` in `SessionConfig`, the SDK
  automatically saves conversation history, tool call results, and planning state
  to `~/.copilot/session-state/{sessionId}/`. Sessions can be resumed later with
  `client.ResumeSessionAsync(sessionId, config)`, which restores the full AI
  context (not just UI history). See:
  https://github.com/github/copilot-sdk/blob/main/docs/features/session-persistence.md

- **Session ID strategy**: Use structured IDs for auditability:
  `copilot-nexus-{counter}-{timestamp}` (e.g., `copilot-nexus-3-1706932800`).
  These are deterministic, unique, and easy to clean up.

- **Disconnect vs Dispose**: On app exit, call `session.Disconnect()` (not
  `DisposeAsync()`) to release in-memory resources while preserving session data
  on disk. `DisposeAsync()` also keeps data on disk but `Disconnect()` is the
  idiomatic choice for "pause and resume later".

- **ResumeSessionConfig**: When resuming, must re-provide `OnPermissionRequest`
  handler and can optionally change `Model`, `Streaming`, etc. API keys (BYOK)
  are NOT persisted and must be re-provided.

- **What the SDK persists**: Full conversation history, tool call results, agent
  planning state (`plan.md`), session artifacts. API keys are NOT persisted.

- **What the app must persist**: Tab metadata (name, model, SDK session ID,
  tab order, selected tab index, session counter), plus Nexus-generated
  system messages that are outside SDK conversation history.

- **State file format**: JSON via `System.Text.Json`. Include a schema version
  field for forward compatibility.

- **Concurrent write safety**: Use atomic write (write to `.tmp`, then rename)
  to prevent corruption from crashes during save.

- **SDK idle timeout**: The CLI has a 30-minute idle timeout. If the app is
  closed for longer than 30 minutes, the SDK session state may still be on disk
  but the CLI process won't be running. `ResumeSessionAsync` handles this.

- **ListSessionsAsync**: Can be used to verify which sessions are still
  available before attempting to resume.

- **DeleteSessionAsync**: Should be called when the user explicitly closes a
  tab (removes the session from disk permanently).

## Background

```gherkin
Given app state is stored by Nexus at "%LOCALAPPDATA%/CopilotNexus/state/session-state.json"
And the state file contains tab metadata: name, model, SDK session ID, and tab order
And SDK session data is stored at "~/.copilot/session-state/{sessionId}/"
And the state file includes a schema version for forward compatibility
```

## Scenarios

### Scenario: Sessions are created with stable IDs

```gherkin
Given the application is running
When the user creates a new session tab
Then the SDK session is created with a structured SessionId (e.g., "copilot-nexus-1-1706932800")
And the SessionId is stored in the tab metadata
And the SDK automatically persists conversation state to disk
```

### Scenario: State is saved on normal exit

```gherkin
Given the application is running
And "Session 1" is open with SDK session ID "copilot-nexus-1-1706932800"
And "Session 2" is open with SDK session ID "copilot-nexus-2-1706932801"
And "Session 1" is the selected tab
When the user closes the application
Then each SDK session is disconnected (not disposed) to preserve state on disk
And a state file is written to "%LOCALAPPDATA%/CopilotNexus/state/session-state.json"
And the state file contains tab metadata for 2 tabs (name, model, SDK session ID)
And the selected tab index is 0
And the session counter is preserved
And the state file is written atomically (write to .tmp then rename)
```

### Scenario: State is saved before hot restart

```gherkin
Given the application is running with open tabs
When the user accepts a hot restart
Then each SDK session is disconnected to preserve state on disk
And the state file is saved before the application exits
```

### Scenario: Sessions are fully resumed on startup

```gherkin
Given a state file exists with 2 tabs and their SDK session IDs
When the application starts
Then 2 tabs are created with the saved names
And each tab uses ResumeSessionAsync with the saved SDK session ID
And the full conversation history is loaded from the SDK and displayed in the tab
And Nexus-generated system messages are restored from app state and displayed in the tab
And the previously selected tab is re-selected
And the session counter resumes from the saved value
And a system message is appended: "Session resumed"
```

### Scenario: Resume failure falls back to new session

```gherkin
Given a state file exists with a tab referencing SDK session ID "copilot-nexus-old-123"
And the SDK session data has been deleted or expired
When the application starts and tries to resume
Then ResumeSessionAsync fails for that session
And a new session is created instead
And a system message is appended: "Previous session could not be resumed — started fresh"
And the tab retains its saved name and model
```

### Scenario: Tab close permanently deletes SDK session

```gherkin
Given the application is running
And "Session 1" is open with SDK session ID "copilot-nexus-1-1706932800"
When the user closes the "Session 1" tab
Then DeleteSessionAsync is called with "copilot-nexus-1-1706932800"
And the SDK session data is permanently removed from disk
And the session cannot be resumed later
```

### Scenario: First launch with no state file

```gherkin
Given no state file exists
When the application starts
Then no tabs are created
And the welcome screen is shown
And no errors are logged
```

### Scenario: Corrupted state file is handled gracefully

```gherkin
Given a state file exists but contains invalid JSON
When the application starts
Then the corrupted state file is backed up with a ".bak" extension
And no tabs are created
And the welcome screen is shown
And a warning is logged: "Failed to restore session state"
```

### Scenario: State survives across multiple restarts

```gherkin
Given the application has been restarted 3 times
And tabs were modified between each restart
Then the state file reflects the most recent tab configuration
And SDK sessions retain full conversation history across all restarts
And no duplicate tabs are created
```
