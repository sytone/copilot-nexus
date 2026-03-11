namespace CopilotNexus.Core.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public enum MessageRole
{
    User,
    Assistant,
    System
}

public class SessionMessage : INotifyPropertyChanged
{
    public MessageRole Role { get; }
    public DateTime Timestamp { get; init; }

    private string _content;
    public string Content
    {
        get => _content;
        private set { _content = value; OnPropertyChanged(); }
    }

    private bool _isStreaming;
    public bool IsStreaming
    {
        get => _isStreaming;
        private set { _isStreaming = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionMessage(MessageRole role, string content, bool isStreaming = false)
    {
        Role = role;
        _content = content;
        Timestamp = DateTime.UtcNow;
        _isStreaming = isStreaming;
    }

    public void AppendContent(string delta)
    {
        Content += delta;
    }

    public void CompleteStreaming()
    {
        IsStreaming = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
