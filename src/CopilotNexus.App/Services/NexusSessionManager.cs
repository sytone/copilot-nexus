namespace CopilotNexus.App.Services;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using CopilotNexus.Core.Contracts;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

/// <summary>
/// ISessionManager implementation that delegates to the Nexus backend via SignalR + REST.
/// The Avalonia app uses this instead of directly managing SDK sessions.
/// </summary>
public class NexusSessionManager : ISessionManager
{
    private readonly string _nexusBaseUrl;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private HubConnection? _hubConnection;

    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, NexusSessionProxy> _proxies = new();
    private readonly List<ModelInfo> _availableModels = new();

    public IReadOnlyList<SessionInfo> Sessions => _sessions.Values.ToList();
    public IReadOnlyList<ModelInfo> AvailableModels => _availableModels;

    public event EventHandler<SessionInfo>? SessionAdded;
    public event EventHandler<SessionInfo>? SessionRemoved;

    public NexusSessionManager(string nexusBaseUrl, ILogger logger)
        : this(nexusBaseUrl, logger, handler: null) { }

    /// <summary>
    /// Constructor for testing — accepts a custom HttpMessageHandler.
    /// </summary>
    internal NexusSessionManager(string nexusBaseUrl, ILogger logger, HttpMessageHandler? handler)
    {
        _nexusBaseUrl = nexusBaseUrl.TrimEnd('/');
        _logger = logger;
        _httpClient = handler != null
            ? new HttpClient(handler) { BaseAddress = new Uri(_nexusBaseUrl) }
            : new HttpClient { BaseAddress = new Uri(_nexusBaseUrl) };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to Nexus at {Url}", _nexusBaseUrl);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_nexusBaseUrl}/hubs/session")
            .WithAutomaticReconnect()
            .Build();

        // Wire up hub client callbacks
        _hubConnection.On<List<ModelInfoDto>>("ModelsLoaded", OnModelsLoaded);
        _hubConnection.On<SessionInfoDto>("SessionAdded", OnSessionAdded);
        _hubConnection.On<string>("SessionRemoved", OnSessionRemoved);
        _hubConnection.On<SessionInfoDto>("SessionReconfigured", OnSessionReconfigured);
        _hubConnection.On<string, SessionOutputDto>("SessionOutput", OnSessionOutput);
        _hubConnection.On<string, string>("SessionStateChanged", OnSessionStateChanged);

        _hubConnection.Reconnecting += _ =>
        {
            _logger.LogWarning("SignalR reconnecting to Nexus...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += async _ =>
        {
            _logger.LogInformation("SignalR reconnected to Nexus");
            await LoadModelsAsync();
        };

        _hubConnection.Closed += ex =>
        {
            _logger.LogWarning(ex, "SignalR connection to Nexus closed");
            return Task.CompletedTask;
        };

        await _hubConnection.StartAsync(cancellationToken);
        _logger.LogInformation("Connected to Nexus via SignalR (state: {State})", _hubConnection.State);

        // Avoid startup race with SignalR callback timing by loading models via REST before returning.
        await LoadModelsAsync(cancellationToken);
    }

    public async Task<SessionInfo> CreateSessionAsync(
        string name,
        SessionConfiguration? config = null,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSessionRequest
        {
            Name = name,
            Model = config?.Model,
            WorkingDirectory = config?.WorkingDirectory,
            IsAutopilot = config?.IsAutopilot ?? true,
            ProfileId = config?.ProfileId,
            AgentFilePath = config?.AgentFilePath,
            IncludeWellKnownMcpConfigs = config?.IncludeWellKnownMcpConfigs ?? true,
            AdditionalMcpConfigPaths = config?.AdditionalMcpConfigPaths ?? [],
            EnabledMcpServers = config?.EnabledMcpServers ?? [],
            SkillDirectories = config?.SkillDirectories ?? [],
        };

        var response = await _httpClient.PostAsJsonAsync("/api/sessions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>(cancellationToken: cancellationToken);
        var info = ToSessionInfo(dto!);

        _sessions[info.Id] = info;

        // Create a proxy session wrapper and join the SignalR group
        var proxy = new NexusSessionProxy(info.Id);
        proxy.SetTransport(
            async (sid, input) => await SendInputAsync(sid, input),
            async (sid) =>
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                    await _hubConnection.InvokeAsync("AbortSession", sid);
            },
            async (sid, ct) => await GetSessionHistoryAsync(sid, ct));
        _proxies[info.Id] = proxy;

        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("JoinSession", info.Id, cancellationToken);
        }

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
        var request = new CreateSessionRequest
        {
            Name = name,
            SdkSessionId = sdkSessionId,
            Model = config?.Model,
            WorkingDirectory = config?.WorkingDirectory,
            IsAutopilot = config?.IsAutopilot ?? true,
            ProfileId = config?.ProfileId,
            AgentFilePath = config?.AgentFilePath,
            IncludeWellKnownMcpConfigs = config?.IncludeWellKnownMcpConfigs ?? true,
            AdditionalMcpConfigPaths = config?.AdditionalMcpConfigPaths ?? [],
            EnabledMcpServers = config?.EnabledMcpServers ?? [],
            SkillDirectories = config?.SkillDirectories ?? [],
        };

        var response = await _httpClient.PostAsJsonAsync("/api/sessions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>(cancellationToken: cancellationToken);
        var info = ToSessionInfo(dto!);

        _sessions[info.Id] = info;

        // Create a proxy session wrapper and join the SignalR group
        var proxy = new NexusSessionProxy(info.Id);
        proxy.SetTransport(
            async (sid, input) => await SendInputAsync(sid, input),
            async (sid) =>
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                    await _hubConnection.InvokeAsync("AbortSession", sid);
            },
            async (sid, ct) => await GetSessionHistoryAsync(sid, ct));
        _proxies[info.Id] = proxy;

        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("JoinSession", info.Id, cancellationToken);
        }

        return info;
    }

    public async Task SendInputAsync(string sessionId, string input, CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("SendInput", sessionId, input, cancellationToken);
        }
        else
        {
            // Fallback to REST
            var request = new SendInputRequest { Input = input };
            var response = await _httpClient.PostAsJsonAsync($"/api/sessions/{sessionId}/input", request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task RemoveSessionAsync(string sessionId, bool deleteFromDisk = false, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/sessions/{sessionId}", cancellationToken);
        response.EnsureSuccessStatusCode();

        _sessions.TryRemove(sessionId, out _);
        _proxies.TryRemove(sessionId, out _);

        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("LeaveSession", sessionId, cancellationToken);
        }
    }

    public ICopilotSessionWrapper? GetSession(string sessionId)
    {
        return _proxies.GetValueOrDefault(sessionId);
    }

    public async Task<SessionInfo> ReconfigureSessionAsync(
        string sessionId,
        SessionConfiguration config,
        Func<ToolPermissionRequest, Task<PermissionDecision>>? permissionHandler = null,
        Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? userInputHandler = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ConfigureSessionRequest
        {
            Model = config.Model,
            WorkingDirectory = config.WorkingDirectory,
            IsAutopilot = config.IsAutopilot,
            ProfileId = config.ProfileId,
            AgentFilePath = config.AgentFilePath,
            IncludeWellKnownMcpConfigs = config.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = config.AdditionalMcpConfigPaths ?? [],
            EnabledMcpServers = config.EnabledMcpServers ?? [],
            SkillDirectories = config.SkillDirectories ?? [],
        };

        var response = await _httpClient.PutAsJsonAsync($"/api/sessions/{sessionId}/configure", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>(cancellationToken: cancellationToken);
        var info = ToSessionInfo(dto!);

        if (info.Id != sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
            _proxies.TryRemove(sessionId, out _);
        }

        _sessions[info.Id] = info;
        if (!_proxies.ContainsKey(info.Id))
        {
            var proxy = new NexusSessionProxy(info.Id);
            proxy.SetTransport(
                async (sid, input) => await SendInputAsync(sid, input),
                async (sid) =>
                {
                    if (_hubConnection?.State == HubConnectionState.Connected)
                        await _hubConnection.InvokeAsync("AbortSession", sid);
                },
                async (sid, ct) => await GetSessionHistoryAsync(sid, ct));
            _proxies[info.Id] = proxy;

            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync("JoinSession", info.Id, cancellationToken);
            }
        }
        return info;
    }

    public async Task<SessionInfo> RenameSessionAsync(string sessionId, string name, CancellationToken cancellationToken = default)
    {
        var request = new RenameSessionRequest { Name = name };
        var response = await _httpClient.PutAsJsonAsync($"/api/sessions/{sessionId}/name", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SessionInfoDto>(cancellationToken: cancellationToken);
        var info = ToSessionInfo(dto!);
        _sessions[info.Id] = info;
        return info;
    }

    internal async Task<IReadOnlyList<SessionOutputEventArgs>> GetSessionHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/sessions/{sessionId}/history", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("No history endpoint data for session {SessionId}", sessionId);
            return Array.Empty<SessionOutputEventArgs>();
        }

        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<SessionOutputDto>>(cancellationToken: cancellationToken)
            ?? new List<SessionOutputDto>();

        var history = new List<SessionOutputEventArgs>(items.Count);
        foreach (var item in items)
        {
            if (!Enum.TryParse<OutputKind>(item.Kind, ignoreCase: true, out var kind) || kind != OutputKind.Message)
                continue;
            if (string.IsNullOrWhiteSpace(item.Content))
                continue;

            var role = Enum.TryParse<MessageRole>(item.Role, ignoreCase: true, out var parsedRole)
                ? parsedRole
                : MessageRole.Assistant;

            history.Add(new SessionOutputEventArgs(sessionId, item.Content, role, OutputKind.Message));
        }

        return history;
    }

    // --- SignalR callbacks ---

    internal async Task LoadModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await _httpClient.GetFromJsonAsync<List<ModelInfoDto>>("/api/models", cancellationToken)
                ?? new List<ModelInfoDto>();
            OnModelsLoaded(models);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load models from Nexus API");
        }
    }

    private void OnModelsLoaded(List<ModelInfoDto> models)
    {
        _availableModels.Clear();
        _availableModels.AddRange(models.Select(ToModelInfo));
        _logger.LogInformation("Model catalog updated with {Count} entries", models.Count);
    }

    private void OnSessionAdded(SessionInfoDto dto)
    {
        var info = ToSessionInfo(dto);
        _sessions[info.Id] = info;

        // Create proxy if we don't have one (session created by another client)
        if (!_proxies.ContainsKey(info.Id))
        {
            var proxy = new NexusSessionProxy(info.Id);
            proxy.SetTransport(
                async (sid, input) => await SendInputAsync(sid, input),
                async (sid) =>
                {
                    if (_hubConnection?.State == HubConnectionState.Connected)
                        await _hubConnection.InvokeAsync("AbortSession", sid);
                },
                async (sid, ct) => await GetSessionHistoryAsync(sid, ct));
            _proxies[info.Id] = proxy;
        }

        SessionAdded?.Invoke(this, info);
    }

    private void OnSessionRemoved(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var info))
        {
            _proxies.TryRemove(sessionId, out _);
            SessionRemoved?.Invoke(this, info);
        }
    }

    private void OnSessionReconfigured(SessionInfoDto dto)
    {
        var info = ToSessionInfo(dto);
        _sessions[info.Id] = info;
    }

    private void OnSessionOutput(string sessionId, SessionOutputDto output)
    {
        if (_proxies.TryGetValue(sessionId, out var proxy))
        {
            var kind = Enum.TryParse<OutputKind>(output.Kind, out var k) ? k : OutputKind.Message;
            var role = Enum.TryParse<MessageRole>(output.Role, out var r) ? r : MessageRole.Assistant;
            proxy.RaiseOutput(new SessionOutputEventArgs(sessionId, output.Content, role, kind));
        }
    }

    private void OnSessionStateChanged(string sessionId, string state)
    {
        if (_sessions.TryGetValue(sessionId, out var info))
        {
            if (Enum.TryParse<SessionState>(state, out var s))
                info.State = s;
        }
    }

    // --- Mapping helpers ---

    private static SessionInfo ToSessionInfo(SessionInfoDto dto)
    {
        var info = SessionInfo.FromRemote(
            dto.Id, dto.Name, dto.Model, dto.SdkSessionId,
            dto.IsAutopilot,
            dto.WorkingDirectory,
            dto.ProfileId,
            dto.AgentFilePath,
            dto.IncludeWellKnownMcpConfigs,
            dto.AdditionalMcpConfigPaths,
            dto.EnabledMcpServers,
            dto.SkillDirectories);
        if (Enum.TryParse<SessionState>(dto.State, out var s))
            info.State = s;
        return info;
    }

    private static ModelInfo ToModelInfo(ModelInfoDto dto) => new()
    {
        ModelId = dto.ModelId,
        Name = dto.Name,
        Capabilities = dto.Capabilities,
    };

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
