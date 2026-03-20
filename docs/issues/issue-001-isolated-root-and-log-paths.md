# Issue 001: Loop runs polluted default install root

## Symptoms

- Automated publish/start/restart stress runs always used `%LOCALAPPDATA%\CopilotNexus`.
- It was not possible to run lifecycle validation in an isolated folder.
- App and service logs were hard-coded to `%LOCALAPPDATA%\CopilotNexus\logs`, making loop diagnostics mix with normal usage logs.

## Root cause

- `CopilotNexusPaths.Root` had no override mechanism and always resolved from `SpecialFolder.LocalApplicationData`.
- App and service logging paths bypassed `CopilotNexusPaths` and used direct `LocalApplicationData` path construction.

## Fix

- Added `COPILOT_NEXUS_ROOT` support in `CopilotNexusPaths` via `RootOverrideEnvironmentVariable`.
- `CopilotNexusPaths.Root` now resolves to the override path (when set) and otherwise preserves default behavior.
- Updated App logging (`App.axaml.cs`) to use `CopilotNexusPaths.Logs`.
- Updated Service logging (`NexusHostBuilder.cs`) to use `CopilotNexusPaths.Logs`.

## Expected result

- Stress loops can target a separate install root without touching the user's default install.
- App/service/CLI artifacts and logs are co-located under the same overridden root for clean diagnostics.
