namespace CopilotNexus.Core.Services;

using System.Text.Json;
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

            var mcpServers = ResolveMcpServers(config);
            if (mcpServers.Count > 0)
                sessionConfig.McpServers = mcpServers;

            var customAgents = ResolveCustomAgents(config);
            if (customAgents.Count > 0)
                sessionConfig.CustomAgents = customAgents;

            var skillDirectories = ResolveSkillDirectories(config);
            if (skillDirectories.Count > 0)
                sessionConfig.SkillDirectories = skillDirectories;

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

            var mcpServers = ResolveMcpServers(config);
            if (mcpServers.Count > 0)
                resumeConfig.McpServers = mcpServers;

            var customAgents = ResolveCustomAgents(config);
            if (customAgents.Count > 0)
                resumeConfig.CustomAgents = customAgents;

            var skillDirectories = ResolveSkillDirectories(config);
            if (skillDirectories.Count > 0)
                resumeConfig.SkillDirectories = skillDirectories;

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

    private Dictionary<string, object> ResolveMcpServers(CoreModels.SessionConfiguration config)
    {
        var configPaths = new List<string>();
        if (config.IncludeWellKnownMcpConfigs)
            configPaths.AddRange(GetWellKnownMcpConfigPaths(config.WorkingDirectory));

        configPaths.AddRange((config.AdditionalMcpConfigPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path, config.WorkingDirectory)));

        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in configPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!document.RootElement.TryGetProperty("mcpServers", out var mcpServersElement) ||
                    mcpServersElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var server in mcpServersElement.EnumerateObject())
                {
                    if (TryParseMcpServer(server.Value, out var parsedServer))
                    {
                        merged[server.Name] = parsedServer;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse MCP config at {Path}", path);
            }
        }

        if ((config.EnabledMcpServers ?? []).Count == 0)
            return merged;

        var allowed = (config.EnabledMcpServers ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
            return merged;

        return merged
            .Where(kvp => allowed.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private List<CustomAgentConfig> ResolveCustomAgents(CoreModels.SessionConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.AgentFilePath))
            return [];

        var agentPath = NormalizePath(config.AgentFilePath, config.WorkingDirectory);
        if (!File.Exists(agentPath))
        {
            _logger.LogWarning("Custom agent file not found: {Path}", agentPath);
            return [];
        }

        try
        {
            var prompt = File.ReadAllText(agentPath);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.LogWarning("Custom agent file is empty: {Path}", agentPath);
                return [];
            }

            var name = Path.GetFileNameWithoutExtension(agentPath).Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "custom-agent";

            return
            [
                new CustomAgentConfig
                {
                    Name = name,
                    DisplayName = name,
                    Description = $"Loaded from {agentPath}",
                    Prompt = prompt,
                    Infer = true,
                },
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load custom agent from {Path}", agentPath);
            return [];
        }
    }

    private static List<string> ResolveSkillDirectories(CoreModels.SessionConfiguration config)
    {
        var skillDirectories = new List<string>();

        skillDirectories.AddRange((config.SkillDirectories ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path, config.WorkingDirectory)));

        if (!string.IsNullOrWhiteSpace(config.WorkingDirectory))
        {
            var repoSkills = Path.Combine(config.WorkingDirectory, ".github", "skills");
            if (Directory.Exists(repoSkills))
                skillDirectories.Add(repoSkills);
        }

        return skillDirectories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> GetWellKnownMcpConfigPaths(string? workingDirectory)
    {
        var copilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (string.IsNullOrWhiteSpace(copilotHome))
        {
            copilotHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".copilot");
        }

        yield return Path.Combine(copilotHome, "mcp-config.json");
        yield return Path.Combine(copilotHome, "mcp.json");

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            yield return Path.Combine(workingDirectory, ".vscode", "mcp.json");
            yield return Path.Combine(workingDirectory, ".copilot", "mcp-config.json");
            yield return Path.Combine(workingDirectory, ".copilot", "mcp.json");
        }
    }

    private static string NormalizePath(string path, string? workingDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;

        return string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDirectory, path));
    }

    private static bool TryParseMcpServer(JsonElement element, out object server)
    {
        server = null!;

        if (!element.TryGetProperty("type", out var typeElement))
            return false;

        var type = typeElement.GetString()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(type))
            return false;

        var timeout = element.TryGetProperty("timeout", out var timeoutElement) &&
                      timeoutElement.ValueKind == JsonValueKind.Number &&
                      timeoutElement.TryGetInt32(out var parsedTimeout)
            ? parsedTimeout
            : (int?)null;

        var tools = ParseStringList(element, "tools");
        if (tools.Count == 0)
            tools.Add("*");

        if (type is "local" or "stdio")
        {
            if (!element.TryGetProperty("command", out var commandElement))
                return false;

            var command = commandElement.GetString();
            if (string.IsNullOrWhiteSpace(command))
                return false;

            server = new McpLocalServerConfig
            {
                Type = type,
                Command = command,
                Args = ParseStringList(element, "args"),
                Env = ParseStringMap(element, "env"),
                Cwd = ParseOptionalString(element, "cwd"),
                Timeout = timeout,
                Tools = tools,
            };
            return true;
        }

        if (type is "http" or "sse")
        {
            if (!element.TryGetProperty("url", out var urlElement))
                return false;

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
                return false;

            server = new McpRemoteServerConfig
            {
                Type = type,
                Url = url,
                Headers = ParseStringMap(element, "headers"),
                Timeout = timeout,
                Tools = tools,
            };
            return true;
        }

        return false;
    }

    private static string? ParseOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static List<string> ParseStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return [];

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString();
            return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }

        if (value.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                items.Add(item.GetString()!);
        }
        return items;
    }

    private static Dictionary<string, string> ParseStringMap(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return [];

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    map[property.Name] = property.Value.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    map[property.Name] = property.Value.ToString();
                    break;
            }
        }
        return map;
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
