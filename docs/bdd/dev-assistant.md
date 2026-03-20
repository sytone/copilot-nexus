# Feature: Development Assistant

The Development Assistant (`CopilotNexus.DevAssistant`) is a long-running CLI tool
that watches runtime logs, detects errors and warnings, generates issue files for
AI-assisted triage, and exposes a local HTTP API so developers (or AI agents) can
trigger rebuild, publish, and restart actions without relying on VS Code tasks.

## Background

```gherkin
Given the log directory is "%LOCALAPPDATA%\CopilotNexus\logs\"
And the issue output directory is "docs/issues/" relative to the repository root
And the DevAssistant HTTP endpoint listens on "http://localhost:5290"
And log files follow the naming patterns:
  | Component | Pattern               |
  | App       | copilot-nexus-*.log   |
  | Service   | nexus-*.log           |
  | Cli       | cli-*.log             |
  | DevAssist | devassistant-*.log    |
```

## Scenario: Start the DevAssistant in watch mode

```gherkin
When the user runs "nexus dev watch"
Then the DevAssistant starts a FileSystemWatcher on the log directory
And the DevAssistant starts an HTTP listener on http://localhost:5290
And a startup banner is printed showing the watch directory and API URL
And the process runs until Ctrl+C is pressed
```

## Scenario: Detect new errors in a log file

```gherkin
Given the DevAssistant is running in watch mode
When a log file is modified and contains a new line matching severity "Error"
  | Example line                                                                  |
  | 2026-03-20T10:15:00Z [ERR] Unhandled exception in SessionHub.SendInput       |
  | 2026-03-20T10:15:01Z [FTL] Application startup failed                        |
Then the DevAssistant extracts the error details (timestamp, component, message, stack trace)
And creates an issue file at "docs/issues/YYYYMMDD-HHMMSS-<slug>.md"
And the issue file contains:
  | Section          | Content                                              |
  | Title            | Short summary derived from the error message          |
  | Component        | The source component (App, Service, Cli)              |
  | Severity         | Error or Warning                                      |
  | Timestamp        | UTC timestamp of the log entry                        |
  | Log Excerpt      | The relevant log lines (error + surrounding context)  |
  | Suggested Prompt | A pre-written prompt an AI agent can use to fix it    |
  | Status           | "open"                                                |
And a console message summarises the new issue file path
```

## Scenario: Detect new warnings in a log file

```gherkin
Given the DevAssistant is running in watch mode
When a log file is modified and contains a new line matching severity "Warning"
Then the DevAssistant creates an issue file with severity "Warning"
And the issue file follows the same format as error issues
```

## Scenario: Avoid duplicate issues for the same error

```gherkin
Given the DevAssistant has already created an issue for error "Unhandled exception in SessionHub.SendInput"
When the same error line appears again in the log
Then no new issue file is created
And the existing issue file is not modified
```

## Scenario: Trigger rebuild via HTTP API

```gherkin
When a POST request is sent to "http://localhost:5290/api/actions/rebuild"
Then the DevAssistant runs "dotnet build CopilotNexus.slnx" from the repository root
And streams the build output to the console
And returns HTTP 200 with a JSON body containing:
  | Field     | Value                        |
  | action    | "rebuild"                    |
  | success   | true or false                |
  | output    | last 50 lines of build output|
```

## Scenario: Trigger publish via HTTP API

```gherkin
When a POST request is sent to "http://localhost:5290/api/actions/publish"
Then the DevAssistant runs the publish sequence for all components:
  | Step | Command                                                                                      |
  | 1    | dotnet publish src/CopilotNexus.Service -c Release -o <versioned-path> --self-contained false |
  | 2    | dotnet publish src/CopilotNexus.App -c Release -o <versioned-path> --self-contained false     |
  | 3    | dotnet publish src/CopilotNexus.Cli -c Release -o <versioned-path> --self-contained false     |
And returns HTTP 200 with a JSON summary of publish results
```

## Scenario: Trigger restart via HTTP API

```gherkin
When a POST request is sent to "http://localhost:5290/api/actions/restart"
Then the DevAssistant stops the running Nexus service (reads nexus.lock for PID)
And starts the Nexus service using the latest published version
And returns HTTP 200 with the service status
```

## Scenario: Trigger republish (rebuild + publish + restart) via HTTP API

```gherkin
When a POST request is sent to "http://localhost:5290/api/actions/republish"
Then the DevAssistant executes the following sequence:
  | Step | Action   |
  | 1    | rebuild  |
  | 2    | publish  |
  | 3    | restart  |
And each step's result is included in the response
And if any step fails, subsequent steps are skipped
And the response indicates which step failed
```

## Scenario: Query DevAssistant status

```gherkin
When a GET request is sent to "http://localhost:5290/api/status"
Then the response includes:
  | Field              | Value                                    |
  | watching           | true                                     |
  | logDirectory       | the watched log directory path            |
  | issueDirectory     | the issue output directory path           |
  | openIssues         | count of issue files with status "open"   |
  | uptime             | how long the DevAssistant has been running|
  | lastErrorDetected  | timestamp of the most recent error found  |
```

## Scenario: List open issues via HTTP API

```gherkin
When a GET request is sent to "http://localhost:5290/api/issues"
Then the response contains a JSON array of open issue summaries:
  | Field     | Value                              |
  | fileName  | the issue markdown file name       |
  | title     | the issue title                    |
  | severity  | "Error" or "Warning"               |
  | component | "App", "Service", "Cli", etc.      |
  | timestamp | when the error was detected         |
  | status    | "open" or "resolved"               |
```

## Scenario: Resolve an issue via HTTP API

```gherkin
When a POST request is sent to "http://localhost:5290/api/issues/<fileName>/resolve"
Then the issue file's Status field is updated from "open" to "resolved"
And the response confirms the resolution
```

## Scenario: CLI shorthand for actions

```gherkin
When the user runs "nexus dev rebuild"
Then the CLI sends POST http://localhost:5290/api/actions/rebuild
And prints the response to the console

When the user runs "nexus dev publish"
Then the CLI sends POST http://localhost:5290/api/actions/publish

When the user runs "nexus dev restart"
Then the CLI sends POST http://localhost:5290/api/actions/restart

When the user runs "nexus dev republish"
Then the CLI sends POST http://localhost:5290/api/actions/republish

When the user runs "nexus dev status"
Then the CLI sends GET http://localhost:5290/api/status

When the user runs "nexus dev issues"
Then the CLI sends GET http://localhost:5290/api/issues
```

## Research Notes

- Log file watching uses `FileSystemWatcher` on the log directory, filtering for `*.log` files
- The existing `Review-NexusLogs.ps1` script provides severity detection patterns: `[ERR]`, `[WRN]`, `[FTL]` markers and keyword matching
- Issue file deduplication uses a hash of the error message + component to avoid re-creating issues for repeated errors
- The HTTP API uses ASP.NET Core minimal APIs (consistent with the Service project pattern)
- Build/publish commands reuse the same `dotnet` CLI invocations as `CliCommands.cs`
- The DevAssistant writes its own logs to `devassistant-YYYYMMDD.log` in the shared log directory
