namespace CopilotNexus.Core.Services;

using System.Text.Json;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// JSON-backed runtime agent configuration service.
/// </summary>
public sealed class JsonRuntimeAgentConfigService : IRuntimeAgentConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<JsonRuntimeAgentConfigService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonRuntimeAgentConfigService(ILogger<JsonRuntimeAgentConfigService> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? CopilotNexusPaths.RuntimeAgentConfigFile;
    }

    public async Task<RuntimeAgentType> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RecoverTempFileIfNeeded();
            if (!File.Exists(_filePath))
                return RuntimeAgentType.Pi;

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var config = JsonSerializer.Deserialize<RuntimeAgentConfig>(json, JsonOptions);
            if (RuntimeAgentTypeExtensions.TryParse(config?.Agent, out var runtimeAgent))
                return runtimeAgent;

            _logger.LogWarning(
                "Invalid runtime agent value '{Agent}' in {Path}; defaulting to pi",
                config?.Agent ?? "(null)",
                _filePath);
            return RuntimeAgentType.Pi;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid runtime agent config JSON at {Path}; defaulting to pi", _filePath);
            return RuntimeAgentType.Pi;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(RuntimeAgentType runtimeAgent, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            var tmpPath = _filePath + ".tmp";
            var payload = new RuntimeAgentConfig { Agent = runtimeAgent.ToConfigValue() };
            var json = JsonSerializer.Serialize(payload, JsonOptions);

            await File.WriteAllTextAsync(tmpPath, json, cancellationToken);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RecoverTempFileIfNeeded()
    {
        var tmpPath = _filePath + ".tmp";
        if (File.Exists(_filePath) || !File.Exists(tmpPath))
            return;

        try
        {
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recover runtime config from temp file at {Path}", tmpPath);
        }
    }
}
