namespace CopilotFamily.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Serializable state for the entire application — saved to disk on exit
/// and restored on startup.
/// </summary>
public class AppState
{
    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;

    /// <summary>All open tabs at the time of save.</summary>
    public List<TabState> Tabs { get; set; } = new();

    /// <summary>Index of the selected tab (0-based), or -1 if none.</summary>
    public int SelectedTabIndex { get; set; } = -1;

    /// <summary>
    /// The session counter value so new tabs continue numbering
    /// from where the user left off.
    /// </summary>
    public int SessionCounter { get; set; }
}

/// <summary>
/// Serializable state for a single tab. The SDK handles conversation
/// history persistence — we only store lightweight UI metadata.
/// </summary>
public class TabState
{
    public string Name { get; set; } = string.Empty;
    public string? Model { get; set; }

    /// <summary>
    /// The SDK-managed session ID used with ResumeSessionAsync.
    /// </summary>
    public string SdkSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Working directory for the session's file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Whether the session was in autopilot mode.
    /// </summary>
    public bool IsAutopilot { get; set; } = true;
}

/// <summary>
/// Serializable state for a single chat message.
/// Retained for backward compatibility with existing state files.
/// </summary>
public class MessageState
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageRole Role { get; set; }

    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
