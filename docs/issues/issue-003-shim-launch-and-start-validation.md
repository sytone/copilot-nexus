# Issue 003: CLI shim launch state diverged from service runtime state

## Symptoms

- `nexus start` invoked via the CLI shim could report success even when the service payload failed shortly after launch.
- Start validation could bind to the shim process lifecycle instead of the actual service process lifecycle.
- Lock-file metadata could temporarily point to the launcher PID instead of the long-running service PID.

## Root cause

- The shim always exited immediately with code `0`, including for CLI commands, so caller-side failures were hidden.
- CLI `start` logic assumed `Process.Start(...)` returned the final service process in all cases.
- Shim refresh checks only considered the current payload path and did not consider the active shim path.

## Fix

- Updated `CopilotNexus.Shim` to wait for child exit and propagate child exit code for `CopilotNexus.Cli.exe` launches.
- Kept fire-and-exit behavior for service/app shims so long-lived payloads still detach correctly.
- Updated CLI `start` flow to detect shim-launched service startup, validate readiness without coupling to launcher PID, and rewrite lock info with resolved runtime PID/path.
- Added polling helper to wait for lock-file ownership to converge and added targeted PID stop logic for failed shim-launched starts.
- Updated shim in-use detection to honor the active shim path and avoid self-overwrite during publish.

## Expected result

- CLI commands launched via shim now return real failure codes when child commands fail.
- Service start validation tracks actual runtime readiness and lock metadata reflects the true service process.
- Publish/start/restart loops no longer misreport success due to shim PID indirection.
