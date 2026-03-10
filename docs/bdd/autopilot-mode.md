# Feature: Autopilot Mode

Allows users to toggle between autopilot (autonomous) and interactive modes per session.

## Background

The Copilot CLI supports an autopilot mode where the agent works autonomously through
multi-step tasks without waiting for user approval at each step. In the SDK, this
behavior is achieved through:
- `OnPermissionRequest` handler — auto-approve all tool calls (autopilot) or surface
  permission dialogs to the user (interactive)
- `OnUserInputRequest` handler — auto-respond or suppress in autopilot, show dialogs
  in interactive mode so the agent can ask clarifying questions

Currently the app hardcodes `PermissionHandler.ApproveAll`. This feature makes it
configurable per session.

## Scenario: New session defaults to autopilot mode

```gherkin
Given the user creates a new session
Then the session is created in autopilot mode by default
And all permission requests are auto-approved
And the autopilot indicator is shown in the tab toolbar
```

## Scenario: Toggle from autopilot to interactive mode

```gherkin
Given a session is in autopilot mode
When the user clicks the autopilot toggle button
Then the session switches to interactive mode
And the toggle indicator changes to show "Interactive"
And subsequent permission requests are shown as dialogs in the UI
And the user can approve or deny each tool call individually
```

## Scenario: Permission request in interactive mode

```gherkin
Given a session is in interactive mode
When the agent requests permission to use a tool (e.g., "edit_file")
Then a dialog appears showing:
  - The tool name
  - The tool arguments (file path, content preview, etc.)
  - "Allow" and "Deny" buttons
  - An "Allow All" button to switch to autopilot
When the user clicks "Allow"
Then the tool execution proceeds
And the dialog closes
```

## Scenario: User input request in interactive mode

```gherkin
Given a session is in interactive mode
And the session was created with OnUserInputRequest handler
When the agent uses the ask_user tool with a question
Then a dialog appears showing:
  - The agent's question
  - Optional multiple-choice options
  - A freeform text input (if allowed)
When the user provides an answer
Then the answer is returned to the agent
And the agent continues working with the user's response
```

## Scenario: User input request in autopilot mode

```gherkin
Given a session is in autopilot mode
When the agent uses the ask_user tool with a question
Then the question is displayed as a system message in the chat
And the agent continues without waiting for user input
Or the request is auto-responded with a default answer
```

## Scenario: Toggle from interactive to autopilot mode

```gherkin
Given a session is in interactive mode
When the user clicks the autopilot toggle button
Then the session switches to autopilot mode
And all pending permission requests are auto-approved
And subsequent permissions are auto-approved without dialogs
And the toggle indicator changes to show "Autopilot"
```

## Scenario: Autopilot mode is persisted across restarts

```gherkin
Given a session is in interactive mode
When the application exits and saves state
Then the TabState includes the autopilot mode setting
When the application restarts and restores state
Then the session is restored in the same mode (interactive)
```

## Scenario: Allow All from permission dialog

```gherkin
Given a session is in interactive mode
And a permission dialog is showing
When the user clicks "Allow All"
Then the current permission is approved
And the session switches to autopilot mode
And all subsequent permissions are auto-approved
And the toggle indicator updates to "Autopilot"
```

## SDK API Reference

- `SessionConfig.OnPermissionRequest` → handler returning `PermissionRequestResult`
  - `PermissionRequestResultKind.Approved` — allow the tool call
  - `PermissionRequestResultKind.Denied` — deny the tool call
- `SessionConfig.OnUserInputRequest` → handler for agent questions
  - Returns `UserInputResponse { Answer, WasFreeform }`
- `PermissionHandler.ApproveAll` — built-in handler that approves everything
- CLI equivalent: `--autopilot --yolo --allow-all`
