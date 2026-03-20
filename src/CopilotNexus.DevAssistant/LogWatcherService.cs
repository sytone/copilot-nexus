using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CopilotNexus.DevAssistant;

/// <summary>
/// Parsed log entry with severity, component, and context.
/// </summary>
internal sealed record LogEntry(
    string Component,
    string Severity,
    DateTimeOffset TimestampUtc,
    string Message,
    string FileName,
    int LineNumber,
    List<string> ContextLines);

/// <summary>
/// Watches the CopilotNexus log directory for changes and detects new errors/warnings.
/// </summary>
internal sealed class LogWatcherService : IDisposable
{
    private static readonly Regex TimestampPattern = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s?(?:Z|[+\-]\d{2}:\d{2}))?)",
        RegexOptions.Compiled);

    private static readonly Regex SeverityPattern = new(
        @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s?(?:Z|[+\-]\d{2}:\d{2}))?\s+\[(?<lvl>[A-Z]{3})\]",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> ComponentPatterns = new()
    {
        ["App"] = "copilot-nexus-*.log",
        ["Service"] = "nexus-*.log",
        ["Cli"] = "cli-*.log",
        ["DevAssistant"] = "devassistant-*.log",
    };

    private FileSystemWatcher? _watcher;
    private readonly string _logDirectory;
    private readonly Dictionary<string, long> _filePositions = new();
    private readonly HashSet<string> _knownIssueHashes = new();
    private readonly ILogger<LogWatcherService> _logger;

    public event Action<LogEntry>? ErrorDetected;

    public LogWatcherService(string logDirectory, ILogger<LogWatcherService> logger)
    {
        _logDirectory = logDirectory;
        _logger = logger;
    }

    public void Start()
    {
        Directory.CreateDirectory(_logDirectory);

        // Snapshot current file positions so we only detect NEW entries
        foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
        {
            _filePositions[file] = new FileInfo(file).Length;
        }

        _watcher = new FileSystemWatcher(_logDirectory, "*.log")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnLogFileChanged;
        _watcher.Created += OnLogFileChanged;

        _logger.LogInformation("Log watcher started on {Directory}", _logDirectory);
    }

    public void RegisterExistingIssueHash(string hash)
    {
        _knownIssueHashes.Add(hash);
    }

    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            ProcessFileChanges(e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing log file change: {File}", e.FullPath);
        }
    }

    private void ProcessFileChanges(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var lastPosition = _filePositions.GetValueOrDefault(filePath, 0L);
        var currentSize = new FileInfo(filePath).Length;

        if (currentSize <= lastPosition) return;

        var component = DetermineComponent(filePath);
        var newLines = ReadNewLines(filePath, lastPosition);

        _filePositions[filePath] = currentSize;

        for (int i = 0; i < newLines.Count; i++)
        {
            var line = newLines[i];
            var severity = GetSeverity(line);
            if (severity == null) continue;

            var timestamp = ParseTimestamp(line);
            var contextLines = GetContextLines(newLines, i, contextRadius: 3);
            var issueHash = ComputeIssueHash(component, severity, line);

            if (!_knownIssueHashes.Add(issueHash))
                continue; // duplicate

            var entry = new LogEntry(
                component,
                severity,
                timestamp ?? DateTimeOffset.UtcNow,
                line,
                Path.GetFileName(filePath),
                (int)(lastPosition > 0 ? i + 1 : i + 1), // approximate line number in new content
                contextLines);

            ErrorDetected?.Invoke(entry);
        }
    }

    private static List<string> ReadNewLines(string filePath, long fromPosition)
    {
        var lines = new List<string>();
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(fromPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }
        }
        catch (IOException)
        {
            // File might be locked; skip this pass
        }

        return lines;
    }

    private static string DetermineComponent(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var (component, pattern) in ComponentPatterns)
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase))
                return component;
        }

        return "Unknown";
    }

    internal static string? GetSeverity(string line)
    {
        var match = SeverityPattern.Match(line);
        if (match.Success)
        {
            var level = match.Groups["lvl"].Value;
            return level switch
            {
                "ERR" or "FTL" => "Error",
                "WRN" => "Warning",
                _ => null,
            };
        }

        if (Regex.IsMatch(line, @"(?i)\b(error|exception|failed|fatal|timeout|unhandled)\b"))
            return "Error";

        if (Regex.IsMatch(line, @"(?i)\b(warning|warn)\b"))
            return "Warning";

        return null;
    }

    private static DateTimeOffset? ParseTimestamp(string line)
    {
        var match = TimestampPattern.Match(line);
        if (!match.Success) return null;

        if (DateTimeOffset.TryParse(match.Groups["ts"].Value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dto))
        {
            return dto;
        }

        return null;
    }

    private static List<string> GetContextLines(List<string> lines, int index, int contextRadius)
    {
        var start = Math.Max(0, index - contextRadius);
        var end = Math.Min(lines.Count - 1, index + contextRadius);
        return lines.GetRange(start, end - start + 1);
    }

    internal static string ComputeIssueHash(string component, string severity, string message)
    {
        // Strip timestamp to hash on content only
        var stripped = TimestampPattern.Replace(message, "").Trim();
        var input = $"{component}|{severity}|{stripped}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
