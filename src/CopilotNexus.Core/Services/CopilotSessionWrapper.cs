namespace CopilotNexus.Core.Services;

using GitHub.Copilot.SDK;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class CopilotSessionWrapper : ICopilotSessionWrapper
{
    private readonly CopilotSession _session;
    private readonly ILogger _logger;
    private readonly IDisposable _subscription;
    private bool _disposed;

    public string SessionId { get; }
    public bool IsActive => !_disposed;

    public event EventHandler<SessionOutputEventArgs>? OutputReceived;

    public CopilotSessionWrapper(CopilotSession session, ILogger logger)
    {
        _session = session;
        _logger = logger;
        SessionId = session.SessionId;

        _subscription = _session.On(HandleSessionEvent);
        _logger.LogDebug("Session wrapper {SessionId} initialized", SessionId);
    }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Session {SessionId}: sending prompt ({Length} chars)", SessionId, prompt.Length);
        try
        {
            await _session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, timeout: null, cancellationToken);
            _logger.LogDebug("Session {SessionId}: send completed", SessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Session {SessionId}: send failed", SessionId);
            throw;
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Session {SessionId}: aborting", SessionId);
        await _session.AbortAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionOutputEventArgs>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var events = await _session.GetMessagesAsync(cancellationToken);
        var history = new List<SessionOutputEventArgs>();

        foreach (var evt in events)
        {
            var mapped = MapSessionEvent(evt, includeStreamingEvents: false);
            if (mapped != null)
                history.Add(mapped);
        }

        _logger.LogDebug("Session {SessionId}: loaded {Count} history outputs", SessionId, history.Count);
        return history;
    }

    private void HandleSessionEvent(SessionEvent evt)
    {
        try
        {
            var mapped = MapSessionEvent(evt, includeStreamingEvents: true);
            if (mapped == null)
                return;

            RaiseOutput(mapped.Content, mapped.Role, mapped.Kind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId}: error handling event {EventType}",
                SessionId, evt.GetType().Name);
        }
    }

    private SessionOutputEventArgs? MapSessionEvent(SessionEvent evt, bool includeStreamingEvents)
    {
        switch (evt)
        {
            case UserMessageEvent user when !includeStreamingEvents && !string.IsNullOrWhiteSpace(user.Data?.Content):
                return new SessionOutputEventArgs(SessionId, user.Data.Content, MessageRole.User, OutputKind.Message);

            case AssistantMessageDeltaEvent delta when includeStreamingEvents &&
                                                       !string.IsNullOrWhiteSpace(delta.Data?.DeltaContent):
                return new SessionOutputEventArgs(SessionId, delta.Data.DeltaContent, MessageRole.Assistant, OutputKind.Delta);

            case AssistantMessageEvent assistant when !string.IsNullOrWhiteSpace(assistant.Data?.Content):
                return new SessionOutputEventArgs(SessionId, assistant.Data.Content, MessageRole.Assistant, OutputKind.Message);

            case AssistantReasoningDeltaEvent reasoningDelta when includeStreamingEvents &&
                                                                 !string.IsNullOrWhiteSpace(reasoningDelta.Data?.DeltaContent):
                return new SessionOutputEventArgs(
                    SessionId,
                    reasoningDelta.Data.DeltaContent,
                    MessageRole.System,
                    OutputKind.ReasoningDelta,
                    reasoningDelta.Data.ReasoningId);

            case AssistantReasoningEvent reasoning when !string.IsNullOrWhiteSpace(reasoning.Data?.Content):
                return new SessionOutputEventArgs(
                    SessionId,
                    reasoning.Data.Content,
                    MessageRole.System,
                    OutputKind.Reasoning,
                    reasoning.Data.ReasoningId);

            case SystemMessageEvent system when !string.IsNullOrWhiteSpace(system.Data?.Content):
                return new SessionOutputEventArgs(SessionId, system.Data.Content, MessageRole.System, OutputKind.Message);

            case AssistantIntentEvent intent when !string.IsNullOrWhiteSpace(intent.Data?.Intent):
                return BuildActivity($"Intent: {intent.Data.Intent}");

            case SessionInfoEvent info when !string.IsNullOrWhiteSpace(info.Data?.Message):
                return BuildActivity(info.Data.Message);

            case SessionWarningEvent warning when !string.IsNullOrWhiteSpace(warning.Data?.Message):
                return BuildActivity($"Warning: {warning.Data.Message}");

            case SessionErrorEvent error when !string.IsNullOrWhiteSpace(error.Data?.Message):
                return BuildActivity($"Error: {error.Data.Message}");

            case ToolExecutionStartEvent toolStart:
            {
                var toolName = toolStart.Data?.ToolName;
                if (string.IsNullOrWhiteSpace(toolName))
                    toolName = toolStart.Data?.McpToolName;
                var message = string.IsNullOrWhiteSpace(toolName)
                    ? "Tool execution started"
                    : $"Tool started: {toolName}";
                return BuildActivity(message, toolStart.Data?.ToolCallId);
            }

            case ToolExecutionProgressEvent toolProgress when !string.IsNullOrWhiteSpace(toolProgress.Data?.ProgressMessage):
                return BuildActivity($"Tool progress: {toolProgress.Data.ProgressMessage}", toolProgress.Data.ToolCallId);

            case ToolExecutionPartialResultEvent partial when !string.IsNullOrWhiteSpace(partial.Data?.PartialOutput):
                return BuildActivity($"Tool partial result: {partial.Data.PartialOutput}", partial.Data.ToolCallId);

            case ToolExecutionCompleteEvent toolComplete:
            {
                var status = toolComplete.Data?.Success == true ? "completed" : "failed";
                var detail = toolComplete.Data?.Error?.Message;
                var message = detail is { Length: > 0 }
                    ? $"Tool {status}: {detail}"
                    : $"Tool {status}";
                return BuildActivity(message, toolComplete.Data?.ToolCallId);
            }

            case SkillInvokedEvent skill when !string.IsNullOrWhiteSpace(skill.Data?.Name):
                return BuildActivity($"Skill invoked: {skill.Data.Name}");

            case SubagentStartedEvent subagentStarted:
            {
                var name = subagentStarted.Data?.AgentDisplayName ?? subagentStarted.Data?.AgentName;
                return !string.IsNullOrWhiteSpace(name)
                    ? BuildActivity($"Subagent started: {name}", subagentStarted.Data?.ToolCallId)
                    : BuildActivity("Subagent started", subagentStarted.Data?.ToolCallId);
            }

            case SubagentCompletedEvent subagentCompleted:
            {
                var name = subagentCompleted.Data?.AgentDisplayName ?? subagentCompleted.Data?.AgentName;
                return !string.IsNullOrWhiteSpace(name)
                    ? BuildActivity($"Subagent completed: {name}", subagentCompleted.Data?.ToolCallId)
                    : BuildActivity("Subagent completed", subagentCompleted.Data?.ToolCallId);
            }

            case SubagentFailedEvent subagentFailed:
            {
                var name = subagentFailed.Data?.AgentDisplayName ?? subagentFailed.Data?.AgentName;
                var errorMessage = subagentFailed.Data?.Error;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(errorMessage))
                    return BuildActivity($"Subagent failed: {name} ({errorMessage})", subagentFailed.Data?.ToolCallId);
                if (!string.IsNullOrWhiteSpace(name))
                    return BuildActivity($"Subagent failed: {name}", subagentFailed.Data?.ToolCallId);
                if (!string.IsNullOrWhiteSpace(errorMessage))
                    return BuildActivity($"Subagent failed: {errorMessage}", subagentFailed.Data?.ToolCallId);
                return BuildActivity("Subagent failed", subagentFailed.Data?.ToolCallId);
            }

            case CommandQueuedEvent queued when !string.IsNullOrWhiteSpace(queued.Data?.Command):
                return BuildActivity($"Command queued: {queued.Data.Command}", queued.Data.RequestId);

            case CommandCompletedEvent completed:
                return BuildActivity("Command completed", completed.Data?.RequestId);

            case SessionIdleEvent when includeStreamingEvents:
                return new SessionOutputEventArgs(SessionId, string.Empty, MessageRole.System, OutputKind.Idle);

            default:
                return null;
        }
    }

    private SessionOutputEventArgs BuildActivity(string content, string? correlationId = null)
    {
        return new SessionOutputEventArgs(SessionId, content, MessageRole.System, OutputKind.Activity, correlationId);
    }

    private void RaiseOutput(string content, MessageRole role, OutputKind kind)
    {
        OutputReceived?.Invoke(this, new SessionOutputEventArgs(SessionId, content, role, kind));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Session {SessionId}: disposing", SessionId);
        _subscription.Dispose();

        try
        {
            await _session.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session {SessionId}: error during disposal", SessionId);
        }

        GC.SuppressFinalize(this);
    }
}
