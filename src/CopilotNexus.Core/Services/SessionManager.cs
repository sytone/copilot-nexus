namespace CopilotNexus.Core.Services;

using System.Collections.Concurrent;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class SessionManager : ISessionManager
{
    private const string FallbackModelId = "gpt-4.1";
    private readonly IAgentClientService _clientService;
    private readonly ILogger<SessionManager> _logger;
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, ICopilotSessionWrapper> _wrappers = new();
    private List<ModelInfo> _availableModels = new();
    private bool _disposed;

    public IReadOnlyList<SessionInfo> Sessions => _sessions.Values.ToList().AsReadOnly();
    public IReadOnlyList<ModelInfo> AvailableModels => _availableModels.AsReadOnly();

    public event EventHandler<SessionInfo>? SessionAdded;
    public event EventHandler<SessionInfo>? SessionRemoved;

    public SessionManager(IAgentClientService clientService, ILogger<SessionManager> logger)
    {
        _clientService = clientService;
        _logger = logger;
    }

    /// <summary>
    /// Backward-compatible constructor used by tests and legacy call sites.
    /// If multiple services are supplied, the first service is used.
    /// </summary>
    public SessionManager(IEnumerable<IAgentClientService> clientServices, ILogger<SessionManager> logger)
        : this(
            clientServices.FirstOrDefault()
                ?? throw new InvalidOperationException("At least one agent client service must be registered."),
            logger)
    {
    }

    /// <summary>
    /// Backward-compatible constructor used by existing tests and call sites.
    /// </summary>
    public SessionManager(ICopilotClientService clientService, ILogger<SessionManager> logger)
        : this((IAgentClientService)clientService, logger)
    {
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing session manager...");

        try
        {
            await _clientService.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Agent runtime is unavailable (e.g. pi/copilot CLI not installed or not in PATH).
            // Log clearly and continue — the service still starts in degraded mode so the app
            // can connect. Session creation will fail at request time with a meaningful error.
            _logger.LogWarning(
                ex,
                "Agent runtime could not be started; service running in degraded mode. " +
                "Install the agent runtime (pi/copilot CLI) and restart to enable sessions.");
            _availableModels = new List<ModelInfo>();
            _logger.LogInformation("Session manager initialized (degraded — no runtime)");
            return;
        }

        try
        {
            var models = await _clientService.ListModelsAsync(cancellationToken);
            _availableModels = models.ToList();
            _logger.LogInformation("Cached {Count} available models", _availableModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list available models during initialization");
            _availableModels = new List<ModelInfo>();
        }

        _logger.LogInformation("Session manager initialized");
    }

    public async Task<SessionInfo> CreateSessionAsync(
        string name,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new SessionConfiguration();
        var resolvedModel = ResolveModel(config.Model);
        var effectiveConfig = new SessionConfiguration
        {
            Model = resolvedModel,
            WorkingDirectory = config.WorkingDirectory,
            IsAutopilot = config.IsAutopilot,
            ProfileId = config.ProfileId,
            AgentFilePath = config.AgentFilePath,
            IncludeWellKnownMcpConfigs = config.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = (config.AdditionalMcpConfigPaths ?? []).ToList(),
            EnabledMcpServers = (config.EnabledMcpServers ?? []).ToList(),
            SkillDirectories = (config.SkillDirectories ?? []).ToList(),
        };

        // Generate a structured SDK session ID for persistence.
        var sdkSessionId = $"copilot-nexus-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{Guid.NewGuid().ToString("N")[..6]}";

        _logger.LogInformation(
            "Creating session '{Name}' with model {Model}, autopilot={Autopilot}, workDir={WorkDir}, SDK ID {SdkId}",
            name,
            resolvedModel,
            effectiveConfig.IsAutopilot,
            effectiveConfig.WorkingDirectory ?? "(default)",
            sdkSessionId);

        var wrapper = await _clientService.CreateSessionAsync(
            sdkSessionId,
            effectiveConfig,
            permissionHandler,
            userInputHandler,
            cancellationToken);
        var info = new SessionInfo(name, resolvedModel, wrapper.SessionId)
        {
            State = SessionState.Running,
            WorkingDirectory = effectiveConfig.WorkingDirectory,
            IsAutopilot = effectiveConfig.IsAutopilot,
            ProfileId = effectiveConfig.ProfileId,
            AgentFilePath = effectiveConfig.AgentFilePath,
            IncludeWellKnownMcpConfigs = effectiveConfig.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = effectiveConfig.AdditionalMcpConfigPaths.ToList(),
            EnabledMcpServers = effectiveConfig.EnabledMcpServers.ToList(),
            SkillDirectories = effectiveConfig.SkillDirectories.ToList(),
        };

        _sessions[info.Id] = info;
        _wrappers[info.Id] = wrapper;

        _logger.LogInformation(
            "Session '{Name}' created with ID {SessionId}, SDK ID {SdkId}",
            name,
            info.Id,
            wrapper.SessionId);
        SessionAdded?.Invoke(this, info);
        return info;
    }

    public async Task<SessionInfo> ResumeSessionAsync(
        string name,
        string sdkSessionId,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new SessionConfiguration();
        var resolvedModel = ResolveModel(config.Model);
        var effectiveConfig = new SessionConfiguration
        {
            Model = resolvedModel,
            WorkingDirectory = config.WorkingDirectory,
            IsAutopilot = config.IsAutopilot,
            ProfileId = config.ProfileId,
            AgentFilePath = config.AgentFilePath,
            IncludeWellKnownMcpConfigs = config.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = (config.AdditionalMcpConfigPaths ?? []).ToList(),
            EnabledMcpServers = (config.EnabledMcpServers ?? []).ToList(),
            SkillDirectories = (config.SkillDirectories ?? []).ToList(),
        };

        _logger.LogInformation(
            "Resuming session '{Name}' with SDK ID {SdkId}, model={Model}, autopilot={Autopilot}",
            name,
            sdkSessionId,
            resolvedModel,
            effectiveConfig.IsAutopilot);

        ICopilotSessionWrapper wrapper;
        try
        {
            wrapper = await _clientService.ResumeSessionAsync(
                sdkSessionId,
                effectiveConfig,
                permissionHandler,
                userInputHandler,
                cancellationToken);
        }
        catch (Exception ex) when (IsSessionNotFoundException(ex))
        {
            _logger.LogWarning(
                ex,
                "Session {SdkSessionId} was not found. Creating a fresh session with the same SDK ID.",
                sdkSessionId);
            wrapper = await _clientService.CreateSessionAsync(
                sdkSessionId,
                effectiveConfig,
                permissionHandler,
                userInputHandler,
                cancellationToken);
        }

        var info = new SessionInfo(name, resolvedModel, wrapper.SessionId)
        {
            State = SessionState.Running,
            WorkingDirectory = effectiveConfig.WorkingDirectory,
            IsAutopilot = effectiveConfig.IsAutopilot,
            ProfileId = effectiveConfig.ProfileId,
            AgentFilePath = effectiveConfig.AgentFilePath,
            IncludeWellKnownMcpConfigs = effectiveConfig.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = effectiveConfig.AdditionalMcpConfigPaths.ToList(),
            EnabledMcpServers = effectiveConfig.EnabledMcpServers.ToList(),
            SkillDirectories = effectiveConfig.SkillDirectories.ToList(),
        };

        _sessions[info.Id] = info;
        _wrappers[info.Id] = wrapper;

        _logger.LogInformation(
            "Session '{Name}' resumed with ID {SessionId}, SDK ID {SdkId}",
            name,
            info.Id,
            wrapper.SessionId);
        SessionAdded?.Invoke(this, info);
        return info;
    }

    public async Task<SessionInfo> ReconfigureSessionAsync(
        string sessionId,
        SessionConfiguration config,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var existingInfo))
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        _logger.LogInformation("Reconfiguring session '{Name}' — disconnect + resume with new config", existingInfo.Name);
        var resolvedModel = config.Model ?? existingInfo.Model ?? ResolveModel(null);

        // Get the SDK session ID before disposing.
        var sdkSessionId = existingInfo.SdkSessionId;
        var name = existingInfo.Name;

        // Disconnect the existing session (preserves state on disk).
        if (_wrappers.TryRemove(sessionId, out var existingWrapper))
        {
            try
            {
                await existingWrapper.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting session '{Name}' during reconfigure", name);
            }
        }

        _sessions.TryRemove(sessionId, out _);

        // Resume with new configuration.
        var wrapper = await _clientService.ResumeSessionAsync(sdkSessionId, config, permissionHandler, userInputHandler, cancellationToken);
        var newInfo = SessionInfo.FromRemote(
            sessionId,
            name,
            resolvedModel,
            wrapper.SessionId,
            config.IsAutopilot,
            config.WorkingDirectory,
            config.ProfileId,
            config.AgentFilePath,
            config.IncludeWellKnownMcpConfigs,
            config.AdditionalMcpConfigPaths ?? [],
            config.EnabledMcpServers ?? [],
            config.SkillDirectories ?? []);

        _sessions[newInfo.Id] = newInfo;
        _wrappers[newInfo.Id] = wrapper;

        _logger.LogInformation("Session '{Name}' reconfigured with ID {SessionId}", name, newInfo.Id);
        return newInfo;
    }

    public Task<SessionInfo> RenameSessionAsync(string sessionId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Session name cannot be empty.", nameof(name));

        if (!_sessions.TryGetValue(sessionId, out var info))
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        info.Name = name.Trim();
        _logger.LogInformation("Session {SessionId} renamed to '{Name}'", sessionId, info.Name);
        return Task.FromResult(info);
    }

    public async Task SendInputAsync(string sessionId, string input, CancellationToken cancellationToken = default)
    {
        var wrapper = GetSessionOrThrow(sessionId);
        _logger.LogDebug("Sending input to session {SessionId}", sessionId);
        await wrapper.SendAsync(input, cancellationToken);
    }

    public async Task RemoveSessionAsync(string sessionId, bool deleteFromDisk = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing session {SessionId} (deleteFromDisk={Delete})", sessionId, deleteFromDisk);

        if (_wrappers.TryRemove(sessionId, out var wrapper))
        {
            var sdkSessionId = wrapper.SessionId;

            try
            {
                await wrapper.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing session {SessionId}", sessionId);
            }

            if (deleteFromDisk)
            {
                await _clientService.DeleteSessionAsync(sdkSessionId, cancellationToken);
            }
        }

        if (_sessions.TryRemove(sessionId, out var info))
        {
            info.State = SessionState.Stopped;
            SessionRemoved?.Invoke(this, info);
        }
    }

    public ICopilotSessionWrapper? GetSession(string sessionId)
    {
        _wrappers.TryGetValue(sessionId, out var wrapper);
        return wrapper;
    }

    private string ResolveModel(string? requestedModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
            return requestedModel;

        return _availableModels.FirstOrDefault(m => m.ModelId == FallbackModelId)?.ModelId
            ?? _availableModels.FirstOrDefault()?.ModelId
            ?? FallbackModelId;
    }

    private ICopilotSessionWrapper GetSessionOrThrow(string sessionId)
    {
        if (!_wrappers.TryGetValue(sessionId, out var wrapper))
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        return wrapper;
    }

    private static bool IsSessionNotFoundException(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && current.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger.LogInformation("Disposing session manager ({Count} sessions)", _wrappers.Count);

        foreach (var (id, wrapper) in _wrappers)
        {
            try
            {
                await wrapper.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing session {SessionId} during shutdown", id);
            }
        }

        _wrappers.Clear();
        _sessions.Clear();

        try
        {
            await _clientService.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping runtime service during shutdown");
        }

        GC.SuppressFinalize(this);
    }
}
