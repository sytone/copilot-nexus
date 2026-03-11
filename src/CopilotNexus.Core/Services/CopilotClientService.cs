namespace CopilotNexus.Core.Services;

using GitHub.Copilot.SDK;
using CopilotNexus.Core.Interfaces;
using Microsoft.Extensions.Logging;
using CoreModels = CopilotNexus.Core.Models;

public class CopilotClientService : ICopilotClientService
{
    private readonly ILogger<CopilotClientService> _logger;
    private CopilotClient? _client;
    private bool _disposed;

    public bool IsConnected => _client != null;

    public CopilotClientService(ILogger<CopilotClientService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null)
            throw new InvalidOperationException("Client is already started.");

        _logger.LogInformation("Starting Copilot client...");
        try
        {
            _client = new CopilotClient();
            _logger.LogInformation("Copilot client started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Copilot client");
            throw;
        }
    }

    public async Task<IReadOnlyList<CoreModels.ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Client has not been started. Call StartAsync first.");

        _logger.LogInformation("Listing available models...");

        try
        {
            var sdkModels = await _client.ListModelsAsync(cancellationToken);
            var models = sdkModels.Select(m =>
            {
                var capabilities = new List<string>();
                if (m.Capabilities?.Supports is { } supports)
                {
                    if (supports.Vision) capabilities.Add("vision");
                    if (supports.ReasoningEffort) capabilities.Add("reasoning");
                }
                return new CoreModels.ModelInfo
                {
                    ModelId = m.Id,
                    Name = m.Name,
                    Capabilities = capabilities,
                };
            }).ToList();

            _logger.LogInformation("Found {Count} available models", models.Count);
            return models.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list models, returning empty list");
            return Array.Empty<CoreModels.ModelInfo>();
        }
    }

    public async Task<ICopilotSessionWrapper> CreateSessionAsync(
        string? sessionId = null,
        CoreModels.SessionConfiguration? config = null,
        Func<CoreModels.ToolPermissionRequest, Task<CoreModels.PermissionDecision>>? permissionHandler = null,
        Func<CoreModels.AgentUserInputRequest, Task<CoreModels.AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Client has not been started. Call StartAsync first.");

        config ??= new CoreModels.SessionConfiguration();
        var resolvedModel = config.Model ?? "gpt-4.1";
        var isAutopilot = config.IsAutopilot;

        _logger.LogInformation("Creating session {SessionId} with model {Model}, autopilot={Autopilot}, workDir={WorkDir}",
            sessionId ?? "(auto)", resolvedModel, isAutopilot, config.WorkingDirectory ?? "(default)");

        try
        {
            var sessionConfig = new SessionConfig
            {
                Model = resolvedModel,
                Streaming = true,
                OnPermissionRequest = BuildPermissionHandler(isAutopilot, permissionHandler),
            };

            if (sessionId != null)
                sessionConfig.SessionId = sessionId;

            if (config.WorkingDirectory != null)
                sessionConfig.WorkingDirectory = config.WorkingDirectory;

            if (!isAutopilot && userInputHandler != null)
            {
                sessionConfig.OnUserInputRequest = BuildUserInputHandler(userInputHandler);
            }

            var session = await _client.CreateSessionAsync(sessionConfig);
            var wrapper = new CopilotSessionWrapper(session, _logger);
            _logger.LogInformation("Session {SessionId} created successfully", wrapper.SessionId);
            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session with model {Model}", resolvedModel);
            throw;
        }
    }

    public async Task<ICopilotSessionWrapper> ResumeSessionAsync(
        string sessionId,
        CoreModels.SessionConfiguration? config = null,
        Func<CoreModels.ToolPermissionRequest, Task<CoreModels.PermissionDecision>>? permissionHandler = null,
        Func<CoreModels.AgentUserInputRequest, Task<CoreModels.AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Client has not been started. Call StartAsync first.");

        config ??= new CoreModels.SessionConfiguration();
        var isAutopilot = config.IsAutopilot;

        _logger.LogInformation("Resuming session {SessionId}, model={Model}, autopilot={Autopilot}, workDir={WorkDir}",
            sessionId, config.Model ?? "(unchanged)", isAutopilot, config.WorkingDirectory ?? "(unchanged)");

        try
        {
            var resumeConfig = new ResumeSessionConfig
            {
                OnPermissionRequest = BuildPermissionHandler(isAutopilot, permissionHandler),
            };

            if (config.Model != null)
                resumeConfig.Model = config.Model;

            if (config.WorkingDirectory != null)
                resumeConfig.WorkingDirectory = config.WorkingDirectory;

            if (!isAutopilot && userInputHandler != null)
            {
                resumeConfig.OnUserInputRequest = BuildUserInputHandler(userInputHandler);
            }

            var session = await _client.ResumeSessionAsync(sessionId, resumeConfig);
            var wrapper = new CopilotSessionWrapper(session, _logger);
            _logger.LogInformation("Session {SessionId} resumed successfully", wrapper.SessionId);
            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
            throw;
        }
    }

    private static PermissionRequestHandler BuildPermissionHandler(
        bool isAutopilot,
        Func<CoreModels.ToolPermissionRequest, Task<CoreModels.PermissionDecision>>? externalHandler)
    {
        if (isAutopilot || externalHandler == null)
            return PermissionHandler.ApproveAll;

        return async (req, inv) =>
        {
            var toolRequest = new CoreModels.ToolPermissionRequest
            {
                ToolName = req.Kind ?? "unknown",
                Details = req.ToolCallId ?? string.Empty,
            };

            var decision = await externalHandler(toolRequest);

            return decision switch
            {
                CoreModels.PermissionDecision.Approved or CoreModels.PermissionDecision.ApproveAll =>
                    new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved },
                _ => new PermissionRequestResult { Kind = PermissionRequestResultKind.DeniedInteractivelyByUser },
            };
        };
    }

    private static UserInputHandler BuildUserInputHandler(
        Func<CoreModels.AgentUserInputRequest, Task<CoreModels.AgentUserInputResponse>> externalHandler)
    {
        return async (req, inv) =>
        {
            var agentRequest = new CoreModels.AgentUserInputRequest
            {
                Question = req.Question ?? string.Empty,
                Choices = req.Choices?.ToList(),
                AllowFreeform = req.AllowFreeform ?? true,
            };

            var response = await externalHandler(agentRequest);

            return new UserInputResponse
            {
                Answer = response.Answer,
                WasFreeform = response.WasFreeform,
            };
        };
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Client has not been started. Call StartAsync first.");

        _logger.LogInformation("Deleting session {SessionId}", sessionId);

        try
        {
            await _client.DeleteSessionAsync(sessionId);
            _logger.LogInformation("Session {SessionId} deleted", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete session {SessionId}", sessionId);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null)
        {
            _logger.LogInformation("Stopping Copilot client...");
            try
            {
                await _client.StopAsync();
                _client = null;
                _logger.LogInformation("Copilot client stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Copilot client");
                _client = null;
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_client != null)
        {
            try { await StopAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during client disposal");
            }

            _client?.Dispose();
            _client = null;
        }

        GC.SuppressFinalize(this);
    }
}
