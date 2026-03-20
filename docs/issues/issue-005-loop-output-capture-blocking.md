# Issue 005: Loop automation could block on captured launcher output

## Symptoms

- `scripts/Exercise-VersionedLifecycleLoop.ps1` could appear hung during `start`, `restart`, or app launch stages.
- The run log would pause even though payload processes had started.
- End-to-end loop completion was inconsistent across iterations.

## Root cause

- The loop wrapper always captured child command output (`2>&1`) regardless of command behavior.
- Commands that spawn long-lived child processes can keep inherited output handles open.
- Capturing those streams can delay control flow and look like a deadlock from the script side.

## Fix

- Added a `-CaptureOutput` switch to the command invocation helpers.
- Disabled output capture for lifecycle steps that launch or stop long-lived processes (`start`, `restart`, `stop`, `winapp start`).
- Kept capture enabled for bounded commands where output inspection is useful (`install`, `publish`).
- Added `Push-Location`/`Pop-Location` around loop execution to keep source-CLI invocation path resolution stable.

## Expected result

- Multi-step loop execution progresses without output-handle stalls.
- Script retains useful diagnostics for bounded commands while avoiding hangs on launcher commands.
- Repeated publish/restart passes complete consistently in isolated test roots.
