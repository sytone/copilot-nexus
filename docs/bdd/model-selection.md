# Feature: Model Selection

Allows users to choose and change the LLM model for each Copilot session tab.

## Background

The Copilot SDK supports multiple models (e.g., gpt-5, claude-sonnet-4.5, gpt-4.1).
The SDK provides `client.ListModelsAsync()` to discover available models at runtime
and `SessionConfig.Model` / `ResumeSessionConfig.Model` to set/change the model.

## Scenario: List available models on startup

```gherkin
Given the Copilot client is connected
When the application initializes
Then it calls ListModelsAsync to retrieve available models
And caches the model list for the lifetime of the client connection
And the model list is available to all session creation workflows
```

## Scenario: Select model when creating a new session

```gherkin
Given the model list has been loaded
When the user clicks "New Session"
Then a model selector (ComboBox) is displayed with all available models
And the default model is pre-selected
When the user selects a model and confirms
Then a new session is created with SessionConfig.Model set to the selected model
And the tab header or toolbar shows the active model name
```

## Scenario: Change model on an existing session

```gherkin
Given a session is open with model "gpt-4.1"
When the user selects a different model "claude-sonnet-4.5" from the model selector
Then the current session is disconnected (preserving state on disk)
And the session is resumed via ResumeSessionAsync with Model = "claude-sonnet-4.5"
And the conversation history is preserved
And the tab shows the updated model name
And a system message indicates the model was changed
```

## Scenario: Model change fails gracefully

```gherkin
Given a session is open with model "gpt-4.1"
When the user changes the model to "claude-sonnet-4.5"
And the ResumeSessionAsync call fails
Then the app falls back to creating a fresh session with the new model
And the user is notified that conversation history could not be preserved
And the tab remains functional with the new model
```

## Scenario: Model is persisted across restarts

```gherkin
Given a session is open with model "claude-sonnet-4.5"
When the application exits and saves state
Then the TabState includes the model name
When the application restarts and restores state
Then the session is resumed with the same model
```

## SDK API Reference

- `client.ListModelsAsync()` → list of model objects with `ModelId`, `Name`, `Capabilities`
- `SessionConfig.Model` → set on `CreateSessionAsync`
- `ResumeSessionConfig.Model` → set on `ResumeSessionAsync` (can differ from original)
- `SessionConfig.ReasoningEffort` → optional ("low", "medium", "high", "xhigh")
