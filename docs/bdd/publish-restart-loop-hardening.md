# Feature: Reliable repeated publish and restart cycles via shims

Repeated publish/restart cycles should work without file lock failures or startup/load regressions, even when run as an automated stress loop in an isolated test install root.

## Research Notes

- `nexus publish` writes side-by-side versioned payloads and refreshes stable shim executables.
- CLI service lifecycle commands (`start`, `restart`, `stop`) rely on `nexus.lock` and health validation probes.
- Current logs show historical startup and publish errors; loop tooling should surface these deterministically.

## Background

```gherkin
Given Copilot Nexus uses shim launchers for CLI, service, and app
And payloads are published into versioned folders per component
And loop automation can run against an isolated Copilot Nexus root
```

## Scenario: Loop runs in isolated install root

```gherkin
Given a dedicated test root path outside the default user install root
When the automation script runs install, publish, and restart commands
Then install artifacts, lock files, state files, and logs are created under that test root
And the default user install root remains unchanged by the loop run
```

## Scenario: Repeated publish and restart stays lock-free

```gherkin
Given service and app were launched via shims
When the loop performs publish -> restart -> publish -> restart in sequence
Then each publish writes a new versioned payload folder
And shims continue launching the latest payload without manual file copy steps
And no command output reports locked executable or access denied failures
```

## Scenario: Load and startup failures are surfaced by diagnostics

```gherkin
Given the loop collects command output and runtime logs each cycle
When a startup or initialization error occurs
Then the script marks the cycle as failed
And it reports the exact failing command and log lines
And the failure output includes a clear signature for issue documentation
```

## Scenario: Successful cycle has explicit pass criteria

```gherkin
When a full cycle completes
Then build/install/publish commands return exit code 0
And service status check succeeds after each restart
And app launch command returns success after each restart
And no lock or load failure signatures are found in cycle logs
```
