namespace CopilotNexus.Core.Services;

using System.Collections.Concurrent;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class SessionManager : ISessionManager
{
    private const string FallbackModelId = "gpt-4.1";
    private readonly ICopilotClientService _clientService;
    private readonly ILogger<SessionManager> _logger;
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, ICopilotSessionWrapper> _wrappers = new();
    private List<ModelInfo> _availableModels = new();
    private bool _disposed;

    public IReadOnlyList<SessionInfo> Sessions => _sessions.Values.ToList().AsReadOnly();
    public IReadOnlyList<ModelInfo> AvailableModels => _availableModels.AsReadOnly();

    public event EventHandler<SessionInfo>? SessionAdded;
    public event EventHandler<SessionInfo>? SessionRemoved;

    public SessionManager(ICopilotClientService clientService, ILogger<SessionManager> logger)
    {
        _clientService = clientService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing session manager...");
        await _clientService.StartAsync(cancellationToken);

        // Cache available models
        try
        {
            var models = await _clientService.ListModelsAsync(cancellationToken);
            _availableModels = models.ToList();
            _logger.LogInformation("Cached {Count} available models", _availableModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list models during initialization");
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

        // Generate a structured SDK session ID for persistence
        var sdkSessionId = $"copilot-nexus-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{Guid.NewGuid().ToString("N")[..6]}";

        _logger.LogInformation("Creating session '{Name}' with model {Model}, autopilot={Autopilot}, workDir={WorkDir}, SDK ID {SdkId}",
            name, resolvedModel, effectiveConfig.IsAutopilot, effectiveConfig.WorkingDirectory ?? "(default)", sdkSessionId);

        var wrapper = await _clientService.CreateSessionAsync(sdkSessionId, effectiveConfig, permissionHandler, userInputHandler, cancellationToken);
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

        _logger.LogInformation("Session '{Name}' created with ID {SessionId}, SDK ID {SdkId}", name, info.Id, wrapper.SessionId);
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

        _logger.LogInformation("Resuming session '{Name}' with SDK ID {SdkId}, model={Model}, autopilot={Autopilot}",
            name, sdkSessionId, config.Model ?? "(unchanged)", config.IsAutopilot);

        var wrapper = await _clientService.ResumeSessionAsync(sdkSessionId, config, permissionHandler, userInputHandler, cancellationToken);
        var info = new SessionInfo(name, config.Model, wrapper.SessionId)
        {
            State = SessionState.Running,
            WorkingDirectory = config.WorkingDirectory,
            IsAutopilot = config.IsAutopilot,
            ProfileId = config.ProfileId,
            AgentFilePath = config.AgentFilePath,
            IncludeWellKnownMcpConfigs = config.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = (config.AdditionalMcpConfigPaths ?? []).ToList(),
            EnabledMcpServers = (config.EnabledMcpServers ?? []).ToList(),
            SkillDirectories = (config.SkillDirectories ?? []).ToList(),
        };

        _sessions[info.Id] = info;
        _wrappers[info.Id] = wrapper;

        _logger.LogInformation("Session '{Name}' resumed with ID {SessionId}, SDK ID {SdkId}", name, info.Id, wrapper.SessionId);
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

        // Get the SDK session ID before disposing
        var sdkSessionId = existingInfo.SdkSessionId;
        var name = existingInfo.Name;

        // Disconnect the existing session (preserves state on disk)
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

        // Resume with new configuration
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
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

        GC.SuppressFinalize(this);
    }
}
