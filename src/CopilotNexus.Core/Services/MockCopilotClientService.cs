namespace CopilotFamily.Core.Services;

using CopilotFamily.Core.Interfaces;
using CopilotFamily.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Mock client service for UI testing. Simulates a successful connection
/// and creates mock sessions that echo responses with streaming.
/// </summary>
public class MockCopilotClientService : ICopilotClientService
{
    private readonly ILogger _logger;
    private bool _started;

    public bool IsConnected => _started;

    public MockCopilotClientService(ILogger logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Copilot client started");
        _started = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Listing available models");
        IReadOnlyList<ModelInfo> models = new List<ModelInfo>
        {
            new() { ModelId = "gpt-4.1", Name = "GPT-4.1", Capabilities = new List<string> { "streaming", "reasoning" } },
            new() { ModelId = "gpt-5", Name = "GPT-5", Capabilities = new List<string> { "streaming", "reasoning" } },
            new() { ModelId = "claude-sonnet-4.5", Name = "Claude Sonnet 4.5", Capabilities = new List<string> { "streaming" } },
            new() { ModelId = "gpt-5.2-codex", Name = "GPT-5.2 Codex", Capabilities = new List<string> { "streaming", "reasoning" } },
        };
        return Task.FromResult(models);
    }

    public Task<ICopilotSessionWrapper> CreateSessionAsync(
        string? sessionId = null,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        if (!_started)
            throw new InvalidOperationException("Client has not been started.");

        config ??= new SessionConfiguration();
        _logger.LogInformation("[MOCK] Creating session {SessionId} with model {Model}, autopilot={Autopilot}, workDir={WorkDir}",
            sessionId ?? "(auto)", config.Model ?? "default", config.IsAutopilot, config.WorkingDirectory ?? "(default)");
        ICopilotSessionWrapper wrapper = new MockCopilotSessionWrapper(sessionId, config.Model, _logger);
        return Task.FromResult(wrapper);
    }

    public Task<ICopilotSessionWrapper> ResumeSessionAsync(
        string sessionId,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        if (!_started)
            throw new InvalidOperationException("Client has not been started.");

        config ??= new SessionConfiguration();
        _logger.LogInformation("[MOCK] Resuming session {SessionId}, model={Model}", sessionId, config.Model ?? "(unchanged)");
        ICopilotSessionWrapper wrapper = new MockCopilotSessionWrapper(sessionId, config.Model, _logger);
        return Task.FromResult(wrapper);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Deleting session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Copilot client stopped");
        _started = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _started = false;
        return ValueTask.CompletedTask;
    }
}
