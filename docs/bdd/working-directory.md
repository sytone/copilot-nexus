# Feature: Working Directory

Allows users to set the working directory for each Copilot session, giving the agent
the correct file system context for the project being worked on.

## Background

The Copilot SDK's `SessionConfig.WorkingDirectory` controls the base path for all
file operations in a session. Each session can have a different working directory,
enabling tabs pointed at different repositories or projects. The working directory
is persisted with the session state and can be changed on resume.

## Scenario: Set working directory when creating a new session

```gherkin
Given the user clicks "New Session"
When the new session dialog is displayed
Then it includes a working directory field with a folder browser button
And the default working directory is the current user's home directory or last used path
When the user selects a folder (e.g., "C:\repos\my-project")
And creates the session
Then the session is created with SessionConfig.WorkingDirectory = "C:\repos\my-project"
And the agent has file access scoped to that directory
```

## Scenario: Display working directory in the session tab

```gherkin
Given a session is open with working directory "C:\repos\my-project"
Then the tab toolbar or status area shows the working directory path
And the user can see which project each tab is pointed at
```

## Scenario: Change working directory on an existing session

```gherkin
Given a session is open with working directory "C:\repos\old-project"
When the user clicks the folder browser and selects "C:\repos\new-project"
Then the current session is disconnected (preserving state on disk)
And the session is resumed via ResumeSessionAsync with WorkingDirectory = "C:\repos\new-project"
And the conversation history is preserved
And the agent now operates in the new directory context
And a system message indicates the working directory was changed
```

## Scenario: Working directory is persisted across restarts

```gherkin
Given a session is open with working directory "C:\repos\my-project"
When the application exits and saves state
Then the TabState includes the working directory path
When the application restarts and restores state
Then the session is resumed with the same working directory
```

## Scenario: Invalid working directory is handled gracefully

```gherkin
Given a session was saved with working directory "C:\repos\deleted-project"
When the application restarts and the directory no longer exists
Then the session is created with the default working directory
And the user is notified that the original directory was not found
```

## SDK API Reference

- `SessionConfig.WorkingDirectory` → set on `CreateSessionAsync`
- `ResumeSessionConfig.WorkingDirectory` → set on `ResumeSessionAsync`
- Each session's working directory is isolated from other sessions
- The CLI process itself has `CopilotClientOptions.Cwd` (separate from per-session)
