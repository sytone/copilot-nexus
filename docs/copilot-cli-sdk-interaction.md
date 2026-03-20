# Copilot CLI SDK Interaction (Reverse-Engineered Notes)

This document captures practical findings from inspecting the npm-distributed GitHub Copilot CLI package and mapping it to Copilot Nexus SDK usage.

## Scope

- Package inspected: `@github/copilot` (observed version: `1.0.5`)
- Source repository: `https://github.com/github/copilot-cli`
- Focus: process launch behavior, transport, RPC surface, and how `CopilotNexus` uses the SDK

## Package Layout and Entrypoints

From the published package:

- `bin.copilot` points to `npm-loader.js`
- `index.js` and `app.js` are bundled runtime entrypoints
- `copilot-sdk/` is shipped in-package (JS + `.d.ts`)
- Platform optional deps exist (for example `@github/copilot-win32-x64`)

Notable behavior in `npm-loader.js`:

- Tries to resolve a platform package (`@github/copilot-<platform>-<arch>`) and execute it directly
- Falls back to loading local `index.js` if platform package path cannot be resolved
- Enforces Node.js 24+ for JS fallback path

## SDK Defaults and Spawn Behavior

In bundled `copilot-sdk` (`index.js` + `types.d.ts`), `CopilotClient` defaults are:

- `cliPath`: bundled CLI path resolved from `@github/copilot/sdk` location
- `useStdio`: `true` unless `cliUrl` is provided
- `autoStart`: `true`
- `autoRestart`: `true`
- `logLevel`: `"debug"` default in observed bundle
- `cwd`: current process working directory

When spawning a CLI process, SDK builds args including:

- `--headless`
- `--no-auto-update`
- `--log-level <level>`
- transport switch:
  - `--stdio` (default), or
  - `--port <n>` (TCP mode)
- auth-related options when configured:
  - `--auth-token-env COPILOT_SDK_AUTH_TOKEN`
  - `--no-auto-login` when logged-in user auth is disabled

## Transport Model

SDK transport is JSON-RPC (via `vscode-jsonrpc`).

- Stdio mode:
  - `StreamMessageReader(cliProcess.stdout)`
  - `StreamMessageWriter(cliProcess.stdin)`
- TCP mode:
  - Creates a socket and wraps it with stream reader/writer
- External server mode:
  - Uses `cliUrl` and does not spawn a local CLI process

## Observed RPC Methods

Extracted from the bundled SDK request calls:

- `ping`
- `status.get`, `auth.getStatus`
- `models.list`, `tools.list`, `account.getQuota`
- Session lifecycle:
  - `session.create`, `session.resume`, `session.delete`, `session.destroy`
  - `session.list`, `session.getLastId`
  - `session.send`, `session.abort`, `session.getMessages`
- Session controls:
  - `session.model.getCurrent`, `session.model.switchTo`
  - `session.mode.get`, `session.mode.set`
  - `session.setForeground`, `session.getForeground`
- Session workspace/plan:
  - `session.plan.read`, `session.plan.update`, `session.plan.delete`
  - `session.workspace.listFiles`, `session.workspace.readFile`, `session.workspace.createFile`
- Agent/fleet/compaction:
  - `session.agent.list`, `session.agent.getCurrent`, `session.agent.select`, `session.agent.deselect`
  - `session.fleet.start`
  - `session.compaction.compact`
- Tool/permission callback plumbing:
  - `session.tools.handlePendingToolCall`
  - `session.permissions.handlePendingPermissionRequest`

## Mapping to Copilot Nexus

### Where Nexus integrates

- `src/CopilotNexus.Core/Services/CopilotClientService.cs`
- `src/CopilotNexus.Core/Services/CopilotSessionWrapper.cs`
- `src/CopilotNexus.Core/Services/SessionManager.cs`

### Current integration shape

- Nexus creates one SDK client (`new CopilotClient()`) and reuses it
- Session creation/resume goes through:
  - `CreateSessionAsync(SessionConfig)`
  - `ResumeSessionAsync(ResumeSessionConfig)`
- Permission handling:
  - autopilot => `PermissionHandler.ApproveAll`
  - interactive => custom permission callback mapping
- User input:
  - `OnUserInputRequest` is wired in non-autopilot mode when handler provided
- Event mapping:
  - SDK events are converted into Nexus `SessionOutputEventArgs` in `CopilotSessionWrapper`

### What this implies

- Nexus currently uses SDK defaults for process/transport (stdio + auto-start)
- It does not currently pass explicit `CopilotClientOptions` (e.g., custom `CliPath`, `CliUrl`, `UseStdio`, `Port`)
- Most behavior customization is done at session config level (model, autopilot, tools, MCP/custom agents)

## Practical Debugging Tips

- Confirm transport mode assumptions when diagnosing startup issues (stdio vs TCP)
- If CLI startup fails, inspect stderr output and resolved `cliPath`
- Use `status.get` / `ping` style calls (through SDK methods) to validate connection health before session operations
- Keep in mind package-level launch indirection (`npm-loader.js` -> platform binary or JS fallback)

## Reproduce the Inspection

Example workflow used:

```powershell
npm pack @github/copilot --silent
tar -xzf <tarball>
node -e "const p=require('./package/package.json'); console.log(p)"
```

Then inspect:

- `package/npm-loader.js`
- `package/index.js`
- `package/copilot-sdk/index.js`
- `package/copilot-sdk/*.d.ts`

## Notes

These findings are based on reverse inspection of distributed artifacts and can change between versions. Treat this doc as implementation notes, not a stable API contract.
