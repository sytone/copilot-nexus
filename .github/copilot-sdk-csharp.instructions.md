---
applyTo: '**.cs, **.csproj'
description: 'This file provides guidance on building C# applications using GitHub Copilot SDK.'
name: 'GitHub Copilot SDK C# Instructions'
---

## Core Principles

- The SDK is in technical preview and may have breaking changes
- Requires .NET 10.0 or later
- Requires GitHub Copilot CLI installed and in PATH
- Uses async/await patterns throughout
- Implements IAsyncDisposable for resource cleanup

## Installation

Always install via NuGet:
```bash
dotnet add package GitHub.Copilot.SDK
```

## Client Initialization

### Basic Client Setup

```csharp
await using var client = new CopilotClient();
await client.StartAsync();
```

### Client Configuration Options

When creating a CopilotClient, use `CopilotClientOptions`:

- `CliPath` - Path to CLI executable (default: "copilot" from PATH)
- `CliArgs` - Extra arguments prepended before SDK-managed flags
- `CliUrl` - URL of existing CLI server (e.g., "localhost:8080"). When provided, client won't spawn a process
- `Port` - Server port (default: 0 for random)
- `UseStdio` - Use stdio transport instead of TCP (default: true)
- `LogLevel` - Log level (default: "info")
- `AutoStart` - Auto-start server (default: true)
- `AutoRestart` - Auto-restart on crash (default: true)
- `Cwd` - Working directory for the CLI process
- `Environment` - Environment variables for the CLI process
- `Logger` - ILogger instance for SDK logging

### Manual Server Control

For explicit control:
```csharp
var client = new CopilotClient(new CopilotClientOptions { AutoStart = false });
await client.StartAsync();
// Use client...
await client.StopAsync();
```

Use `ForceStopAsync()` when `StopAsync()` takes too long.

## Session Management

### Creating Sessions

Use `SessionConfig` for configuration:

```csharp
await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Streaming = true,
    Tools = [...],
    SystemMessage = new SystemMessageConfig { ... },
    AvailableTools = ["tool1", "tool2"],
    ExcludedTools = ["tool3"],
    Provider = new ProviderConfig { ... }
});
```

### Session Config Options

- `SessionId` - Custom session ID for persistence/resumption
- `Model` - Model name ("gpt-5", "claude-sonnet-4.5", etc.)
- `Tools` - Custom tools exposed to the CLI
- `SystemMessage` - System message customization
- `AvailableTools` - Allowlist of tool names
- `ExcludedTools` - Blocklist of tool names
- `Provider` - Custom API provider configuration (BYOK)
- `Streaming` - Enable streaming response chunks (default: false)

### Resuming Sessions

The SDK supports native session persistence. Provide your own `SessionId` when creating
a session, and later resume it with full conversation history intact:

```csharp
// Create with a meaningful ID
var session = await client.CreateSessionAsync(new SessionConfig
{
    SessionId = "user-123-task-456",
    Model = "gpt-5.2-codex",
});

// Later, resume from where you left off
var session = await client.ResumeSessionAsync("user-123-task-456", new ResumeSessionConfig
{
    OnPermissionRequest = (req, inv) =>
        Task.FromResult(new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved }),
});
```

Session state is saved to `~/.copilot/session-state/{sessionId}/` and includes:
- Conversation history (full message thread)
- Tool call results (cached for context)
- Agent planning state (`plan.md`)
- Session artifacts (in `files/` directory)
- API keys are NOT persisted (security)

### Session Operations

- `session.SessionId` - Get session identifier
- `session.SendAsync(new MessageOptions { Prompt = "...", Attachments = [...] })` - Send message
- `session.AbortAsync()` - Abort current processing
- `session.GetMessagesAsync()` - Get all events/messages
- `await session.DisposeAsync()` - Clean up resources

### Session Lifecycle

- `session.Disconnect()` - Releases in-memory resources but preserves session data on disk
- `client.DeleteSessionAsync(sessionId)` - Permanently removes all session data from disk
- `client.ListSessionsAsync()` - Lists all persisted sessions

## Event Handling

### Event Subscription Pattern

ALWAYS use TaskCompletionSource for waiting on session events:

```csharp
var done = new TaskCompletionSource();

session.On(evt =>
{
    if (evt is AssistantMessageEvent msg)
    {
        Console.WriteLine(msg.Data.Content);
    }
    else if (evt is SessionIdleEvent)
    {
        done.SetResult();
    }
});

await session.SendAsync(new MessageOptions { Prompt = "..." });
await done.Task;
```

### Unsubscribing from Events

The `On()` method returns an IDisposable:

```csharp
var subscription = session.On(evt => { /* handler */ });
// Later...
subscription.Dispose();
```

### Event Types

Use pattern matching or switch expressions for event handling:

```csharp
session.On(evt =>
{
    switch (evt)
    {
        case UserMessageEvent userMsg:
            break;
        case AssistantMessageEvent assistantMsg:
            Console.WriteLine(assistantMsg.Data.Content);
            break;
        case AssistantMessageDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;
        case ToolExecutionStartEvent toolStart:
            break;
        case ToolExecutionCompleteEvent toolComplete:
            break;
        case SessionStartEvent start:
            break;
        case SessionIdleEvent idle:
            break;
        case SessionErrorEvent error:
            Console.WriteLine($"Error: {error.Data.Message}");
            break;
    }
});
```

## Streaming Responses

### Enabling Streaming

Set `Streaming = true` in SessionConfig:

```csharp
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Streaming = true
});
```

### Handling Streaming Events

Handle both delta events (incremental) and final events:

```csharp
session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;
        case AssistantMessageEvent msg:
            Console.WriteLine(msg.Data.Content);
            break;
        case SessionIdleEvent:
            done.SetResult();
            break;
    }
});
```

Note: Final events (`AssistantMessageEvent`) are ALWAYS sent regardless of streaming setting.

## Custom Tools

### Defining Tools with AIFunctionFactory

```csharp
using Microsoft.Extensions.AI;
using System.ComponentModel;

var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Tools = [
        AIFunctionFactory.Create(
            async ([Description("Issue ID")] string id) => {
                var issue = await FetchIssueAsync(id);
                return issue;
            },
            "lookup_issue",
            "Fetch issue details from tracker"),
    ]
});
```

## System Message Customization

### Append Mode (Default - Preserves Guardrails)

```csharp
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = "Additional instructions here"
    }
});
```

## Best Practices

1. **Always use `await using`** for CopilotClient and CopilotSession
2. **Use TaskCompletionSource** to wait for SessionIdleEvent
3. **Handle SessionErrorEvent** for robust error handling
4. **Use pattern matching** (switch expressions) for event handling
5. **Enable streaming** for better UX in interactive scenarios
6. **Use AIFunctionFactory** for type-safe tool definitions
7. **Dispose event subscriptions** when no longer needed
8. **Use SystemMessageMode.Append** to preserve safety guardrails
9. **Provide descriptive tool names and descriptions** for better model understanding
10. **Handle both delta and final events** when streaming is enabled
11. **Use custom SessionId** for resumable sessions
12. **Use Disconnect()** instead of Dispose() when you want to preserve session state
