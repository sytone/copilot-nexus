# Feature: Pi model and agent-profile selection

Users can choose both runtime model and reusable agent configuration (session profile) from the Nexus-backed desktop UI.

## Background

Nexus runs Pi in RPC mode and exposes model/profile controls through its REST contracts:

- `GET /api/models` for runtime-discoverable Pi models.
- `GET /api/session-profiles` for reusable profile defaults.
- `PUT /api/sessions/{id}/configure` for applying model/profile-derived configuration to active sessions.

## Scenario: Models are discovered from Pi runtime

```gherkin
Given Nexus can start the Pi executable
When a client requests GET /api/models
Then Nexus queries Pi RPC get_available_models
And returns model IDs that can be used directly for session create/configure
And each model entry includes user-visible name and capabilities metadata
```

## Scenario: Change model on an active session

```gherkin
Given a session is open with model "openai/gpt-5"
When the user picks "anthropic/claude-sonnet-4-5-20250929" in the session footer model selector
Then the app sends a configure request for that session with the selected model
And Nexus reconfigures the session while preserving the tab session identity
And the footer metadata reflects the updated model
```

## Scenario: Apply a profile as agent configuration for an active session

```gherkin
Given a profile "analysis" exists with model, agent file path, MCP server filters, and skill directories
And a session is currently open
When the user selects profile "analysis" from the footer agent selector
Then the app sends configure with profile-derived runtime fields
And Nexus reconfigures the session with those values
And session info stores the selected profileId for persistence and restore
```

## Scenario: Profile selection is restored in the UI

```gherkin
Given a session was last saved with profileId "analysis"
When the app restores state and loads session profiles
Then the tab footer profile selector resolves to the matching profile entry
And users can continue switching model/profile without manual ID entry
```
