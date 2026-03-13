# Feature: Versioned shim launchers for CLI, Service, and Win App

Nexus should launch components through stable shim executables that always resolve the correct versioned payload directory, so update copying is no longer required.

## Research Notes

- Current install/update flow copies files into fixed `cli/`, `nexus/`, and `app/` directories and relies on `nexus update`.
- Current publish stamps a dev informational version (`0.1.0-dev.<timestamp>`) but does not retain multiple side-by-side executable versions per component.
- Service lifecycle currently depends on a lock file and direct executable path lookup.

## Background

```gherkin
Given each component has a stable shim executable at:
  | Component | Shim path under %LOCALAPPDATA%\CopilotNexus\app\ |
  | CLI       | cli\CopilotNexus.Cli.exe                         |
  | Service   | service\CopilotNexus.Service.exe                 |
  | Win App   | winapp\CopilotNexus.App.exe                      |
And each shim folder contains versioned subdirectories (for example "1.4.2" or "1.4.3-dev.20260313164500")
And component binaries are published into those versioned subdirectories
And one unified version sequence is used across CLI, Service, and Win App publishes
```

## Scenario: Shim launches latest compatible version by default

```gherkin
Given a component shim folder contains versions:
  | 1.4.1 |
  | 1.4.2-dev.20260312 |
  | 1.4.2 |
When the shim is executed without arguments
Then it resolves the highest version according to SemVer precedence
And it launches the component executable from that version directory
And it forwards all remaining user arguments to the launched executable
```

## Scenario: Shim supports rollback to previous version

```gherkin
Given a component shim folder contains at least two valid versions
When the shim is executed with "--previous"
Then it resolves the immediate predecessor of the default selected version
And it launches that predecessor version
And it returns a non-zero exit code with a clear error when no predecessor exists
```

## Scenario: Shim supports cleanup retention policy

```gherkin
Given a component shim folder contains many versions
When the shim is executed with "--cleanup 5"
Then it keeps the 5 most recent versions by SemVer ordering
And it deletes all older version directories
And it never deletes the currently running shim binary
And it returns a non-zero exit code when cleanup count is less than 1
```

## Scenario: Publish writes side-by-side versioned outputs

```gherkin
Given the user runs "nexus publish"
When publish completes
Then CLI, Service, and Win App outputs are written to component-specific versioned subdirectories
And existing older versions remain on disk
And no file copy update phase is required to activate the new version
```

## Scenario: Version derivation for local iteration vs GitHub release

```gherkin
Given commit history includes conventional commits since the last published version
When publish runs in local/dev mode
Then the version bump is inferred from commits (breaking=major, feat=minor, otherwise=patch)
And the output version is "<semver>-dev.<utc timestamp>"
When publish runs for GitHub release/tag mode
Then the same inferred bump is applied without dev suffix
And the output version is plain SemVer "<major>.<minor>.<patch>"
```

## Scenario: Service start/stop uses shim-targeted runtime metadata

```gherkin
Given "nexus start" uses the service shim path
When the service shim launches a versioned service payload
Then lock/metadata records both process ID and resolved service version path
And "nexus stop" terminates the correct process even if newer versions are published later
```

## Scenario: Update command is removed

```gherkin
Given the shim architecture is active
When a user runs "nexus update"
Then the CLI reports the command is not recognized
And it exits with a non-zero status
And docs guide users to publish and relaunch via shims instead
```
