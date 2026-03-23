# Issue 006: Service crashes on startup when Pi runtime is unavailable

## Symptoms

- `nexus start` fails with "Service did not become healthy ... within 30 seconds".
- Service process exits immediately (exit code -532462766).
- Service log stops at `Initializing session manager...` with no further output.
- App shows "Connecting to agent runtime" indefinitely because the service never binds its port.

## Root cause

`SessionManager.InitializeAsync` called `_clientService.StartAsync()` without catching failure.
`PiRpcClientService.StartAsync` validates the Pi executable via `pi.cmd --version`; when Pi is
not installed or not in PATH the validation throws `InvalidOperationException` with "did not
respond to '--version' within 5 seconds". This exception was unhandled and propagated to
`Program.cs`, crashing the process before ASP.NET Core ever bound the HTTP port.

## Fix

Wrapped `_clientService.StartAsync()` in `SessionManager.InitializeAsync` with a `try/catch`
that logs a clear warning and returns early, leaving `_availableModels` empty. The service
continues to start normally in "degraded mode" — health, REST, and SignalR endpoints all
bind and respond. Session creation will fail at request time with a descriptive error rather
than taking down the whole host.

Pi is an **optional** runtime. The service must start regardless of whether Pi (or any other
agent runtime) is installed.

## Expected result

- `nexus start` succeeds even when Pi is not installed.
- `/health` returns `200 OK` immediately; `models` count reflects 0 when no runtime is available.
- App connects and shows connected state; users see no available models until a runtime is configured.
- Log clearly shows: `Agent runtime could not be started; service running in degraded mode.`
