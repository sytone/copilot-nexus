namespace CopilotNexus.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class SessionTabViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyList<string> SessionModes = ["Normal", "Plan", "Autopilot"];

    private ICopilotSessionWrapper _session;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private SessionMessage? _currentStreamingMessage;
    private SessionMessage? _currentReasoningMessage;
    private string? _currentReasoningCorrelationId;
    private readonly Dictionary<string, SessionMessage> _activityMessagesByCorrelation =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _inputHistory = [];
    private int _inputHistoryIndex = -1;
    private string _historyDraftInput = string.Empty;
    private bool _suppressHistoryReset;
    private string _inputText = string.Empty;
    private string _title;
    private string _editableTitle;
    private string _selectedMode;
    private bool _isInlineRenaming;
    private bool _isRunning;
    private bool _isProcessing;
    private bool _isAutopilot;
    private string? _workingDirectory;
    private string? _gitBranch;
    private ModelInfo? _selectedModel;
    private bool _disposed;

    public SessionInfo Info { get; private set; }
    public ObservableCollection<SessionMessage> Messages { get; } = new();

    /// <summary>Available models for the model selector.</summary>
    public ObservableCollection<ModelInfo> AvailableModels { get; }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value) && !_suppressHistoryReset && _inputHistoryIndex >= 0)
            {
                _inputHistoryIndex = -1;
                _historyDraftInput = string.Empty;
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string EditableTitle
    {
        get => _editableTitle;
        set => SetProperty(ref _editableTitle, value);
    }

    public bool IsInlineRenaming
    {
        get => _isInlineRenaming;
        set => SetProperty(ref _isInlineRenaming, value);
    }

    public IReadOnlyList<string> AvailableModes => SessionModes;

    /// <summary>Session mode shown in the footer: Normal, Plan, or Autopilot.</summary>
    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            var normalized = NormalizeMode(value, _isAutopilot);
            if (!SetProperty(ref _selectedMode, normalized))
                return;

            ApplySelectedMode(normalized);
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    /// <summary>Whether the session is in autopilot mode.</summary>
    public bool IsAutopilot
    {
        get => _isAutopilot;
        set
        {
            if (SetProperty(ref _isAutopilot, value))
            {
                Info.IsAutopilot = value;
                _logger.LogInformation("Tab '{Title}': autopilot mode {Mode}", Title, value ? "enabled" : "disabled");

                if (!value)
                {
                    AppendSystemMessage("Switched to interactive mode — you will be asked before tool calls are executed.");
                }
                else
                {
                    AppendSystemMessage("Switched to autopilot mode — all tool calls will be auto-approved.");
                }

                if (value)
                {
                    _selectedMode = "Autopilot";
                }
                else if (string.Equals(_selectedMode, "Autopilot", StringComparison.Ordinal))
                {
                    _selectedMode = "Normal";
                }

                OnPropertyChanged(nameof(SelectedMode));
            }
        }
    }

    /// <summary>Working directory for the session.</summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set
        {
            if (SetProperty(ref _workingDirectory, value))
            {
                Info.WorkingDirectory = value;
                RefreshWorkingDirectoryMetadata();
            }
        }
    }

    public string WorkingDirectoryDisplay =>
        string.IsNullOrWhiteSpace(_workingDirectory) ? "(default working directory)" : _workingDirectory;

    public string? GitBranchDisplay => string.IsNullOrWhiteSpace(_gitBranch) ? null : _gitBranch;

    /// <summary>Currently selected model for this session.</summary>
    public ModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value) && value != null && Info.Model != value.ModelId)
            {
                RequestReconfigure(model: value.ModelId);
            }

            NotifyModelMetadataChanged();
        }
    }

    public string CurrentModelDisplay => SelectedModel?.Name ?? Info.Model ?? "default";

    public string ReasoningLevelDisplay => ResolveReasoningLevel(SelectedModel);

    public string? ModelCostDisplay => ResolveModelCost(SelectedModel);

    public ICommand SendCommand { get; }
    public ICommand AbortCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RenameSessionCommand { get; }
    public ICommand BrowseWorkingDirectoryCommand { get; }
    public ICommand ToggleAutopilotCommand { get; }

    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised when the session needs to be reconfigured (model change, working directory change).
    /// The MainWindowViewModel handles the disconnect + resume.
    /// </summary>
    public event EventHandler<SessionConfiguration>? ReconfigureRequested;
    public event EventHandler<string>? RenameRequested;

    public SessionTabViewModel(
        SessionInfo info,
        ICopilotSessionWrapper session,
        IUiDispatcher dispatcher,
        ILogger logger,
        ObservableCollection<ModelInfo>? availableModels = null)
    {
        Info = info;
        _session = session;
        _dispatcher = dispatcher;
        _logger = logger;
        _title = info.Name;
        _editableTitle = info.Name;
        _selectedMode = info.IsAutopilot ? "Autopilot" : "Normal";
        _isAutopilot = info.IsAutopilot;
        _workingDirectory = info.WorkingDirectory;
        IsRunning = true;

        AvailableModels = availableModels ?? new ObservableCollection<ModelInfo>();

        // Set selected model from info without triggering reconfigure
        _selectedModel = AvailableModels.FirstOrDefault(m => m.ModelId == info.Model);
        RefreshWorkingDirectoryMetadata();

        SendCommand = new AsyncRelayCommand(SendInputAsync, () => IsRunning && !IsProcessing && !string.IsNullOrWhiteSpace(InputText));
        AbortCommand = new AsyncRelayCommand(AbortAsync, () => IsProcessing);
        ClearCommand = new RelayCommand(ClearMessages);
        RenameSessionCommand = new RelayCommand(RequestRename);
        BrowseWorkingDirectoryCommand = new RelayCommand(BrowseWorkingDirectory);
        ToggleAutopilotCommand = new RelayCommand(() => IsAutopilot = !IsAutopilot);

        _session.OutputReceived += OnOutputReceived;

        _logger.LogInformation("Tab '{Title}' created for session {SessionId}", _title, info.Id);

        var startMsg = $"Session started — model: {info.Model ?? "default"}";
        if (info.WorkingDirectory != null)
            startMsg += $" | path: {info.WorkingDirectory}";
        startMsg += $" | mode: {(info.IsAutopilot ? "autopilot" : "interactive")}";
        AppendMessage(new SessionMessage(MessageRole.System, startMsg, isNexusSystemMessage: true));
    }

    /// <summary>
    /// Reconfigures this tab with a new session after a disconnect + resume.
    /// Called by MainWindowViewModel after ReconfigureSessionAsync succeeds.
    /// </summary>
    public void Reconfigure(SessionInfo newInfo, ICopilotSessionWrapper newSession)
    {
        _session.OutputReceived -= OnOutputReceived;

        Info = newInfo;
        _session = newSession;
        _isAutopilot = newInfo.IsAutopilot;
        _workingDirectory = newInfo.WorkingDirectory;
        _editableTitle = newInfo.Name;
        Title = newInfo.Name;
        IsInlineRenaming = false;

        // Update selected model without triggering reconfigure
        _selectedModel = AvailableModels.FirstOrDefault(m => m.ModelId == newInfo.Model);
        if (_isAutopilot)
        {
            _selectedMode = "Autopilot";
        }
        else if (string.Equals(_selectedMode, "Autopilot", StringComparison.Ordinal))
        {
            _selectedMode = "Normal";
        }

        RefreshWorkingDirectoryMetadata();
        NotifyModelMetadataChanged();
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsAutopilot));
        OnPropertyChanged(nameof(WorkingDirectory));
        OnPropertyChanged(nameof(EditableTitle));

        _session.OutputReceived += OnOutputReceived;

        var msg = $"Session reconfigured — model: {newInfo.Model ?? "default"}";
        if (newInfo.WorkingDirectory != null)
            msg += $" | path: {newInfo.WorkingDirectory}";
        AppendSystemMessage(msg);

        _logger.LogInformation("Tab '{Title}' reconfigured with new session {SessionId}", Title, newInfo.Id);
    }

    public void RestoreMode(string? mode)
    {
        var normalized = NormalizeMode(mode, _isAutopilot);
        if (string.Equals(_selectedMode, normalized, StringComparison.Ordinal))
            return;

        _selectedMode = normalized;
        OnPropertyChanged(nameof(SelectedMode));
    }

    private void RequestReconfigure(string? model = null, string? workDir = null)
    {
        var config = new SessionConfiguration
        {
            Model = model ?? Info.Model,
            WorkingDirectory = workDir ?? Info.WorkingDirectory,
            IsAutopilot = Info.IsAutopilot,
            ProfileId = Info.ProfileId,
            AgentFilePath = Info.AgentFilePath,
            IncludeWellKnownMcpConfigs = Info.IncludeWellKnownMcpConfigs,
            AdditionalMcpConfigPaths = Info.AdditionalMcpConfigPaths.ToList(),
            EnabledMcpServers = Info.EnabledMcpServers.ToList(),
            SkillDirectories = Info.SkillDirectories.ToList(),
        };

        _logger.LogInformation("Tab '{Title}': requesting reconfigure — model={Model}, workDir={WorkDir}",
            Title, config.Model, config.WorkingDirectory);

        ReconfigureRequested?.Invoke(this, config);
    }

    private void RequestRename()
    {
        var candidate = EditableTitle?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || string.Equals(candidate, Title, StringComparison.Ordinal))
        {
            IsInlineRenaming = false;
            EditableTitle = Title;
            return;
        }

        IsInlineRenaming = false;
        RenameRequested?.Invoke(this, candidate);
    }

    public void BeginInlineRename()
    {
        EditableTitle = Title;
        IsInlineRenaming = true;
    }

    public void CommitInlineRename()
    {
        RequestRename();
    }

    public void CancelInlineRename()
    {
        EditableTitle = Title;
        IsInlineRenaming = false;
    }

    public void ApplyRename(string newName)
    {
        var value = newName.Trim();
        Info.Name = value;
        Title = value;
        EditableTitle = value;
        IsInlineRenaming = false;
        _logger.LogInformation("Tab renamed to '{Title}'", value);
    }

    public void AppendSystemMessage(string text)
    {
        _dispatcher.BeginInvoke(() => AppendMessage(new SessionMessage(
            MessageRole.System,
            text,
            isNexusSystemMessage: true)));
    }

    private void BrowseWorkingDirectory()
    {
        // Raise an event for the view to handle folder browsing
        // (Avalonia's file dialogs require a TopLevel reference)
        FolderBrowseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user clicks Browse for working directory. View handles the dialog.</summary>
    public event EventHandler? FolderBrowseRequested;

    /// <summary>Called by the view after the user picks a folder.</summary>
    public void SetWorkingDirectory(string path)
    {
        _logger.LogInformation("Tab '{Title}': working directory set to '{Path}'", Title, path);
        WorkingDirectory = path;
        RequestReconfigure(workDir: path);
    }

    public bool TryNavigateInputHistory(int direction)
    {
        if (_inputHistory.Count == 0 || direction == 0)
            return false;

        if (direction < 0)
        {
            if (_inputHistoryIndex == -1)
            {
                _historyDraftInput = InputText;
                _inputHistoryIndex = _inputHistory.Count - 1;
            }
            else if (_inputHistoryIndex > 0)
            {
                _inputHistoryIndex--;
            }

            SetInputTextFromHistory(_inputHistory[_inputHistoryIndex]);
            return true;
        }

        if (_inputHistoryIndex == -1)
            return false;

        if (_inputHistoryIndex < _inputHistory.Count - 1)
        {
            _inputHistoryIndex++;
            SetInputTextFromHistory(_inputHistory[_inputHistoryIndex]);
        }
        else
        {
            _inputHistoryIndex = -1;
            SetInputTextFromHistory(_historyDraftInput);
            _historyDraftInput = string.Empty;
        }

        return true;
    }

    private async Task SendInputAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var input = InputText;
        RecordInputHistory(input);
        InputText = string.Empty;

        AppendMessage(new SessionMessage(MessageRole.User, input));
        IsProcessing = true;
        _logger.LogDebug("Tab '{Title}': sending input ({Length} chars)", Title, input.Length);

        try
        {
            await _session.SendAsync(input);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Tab '{Title}': request aborted by user", Title);
            AppendMessage(new SessionMessage(
                MessageRole.System,
                "Request aborted.",
                isNexusSystemMessage: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tab '{Title}': send failed", Title);
            AppendMessage(new SessionMessage(
                MessageRole.System,
                $"Error: {ex.Message}",
                isNexusSystemMessage: true));
        }
        finally
        {
            FinalizeStreamingMessage();
            IsProcessing = false;
        }
    }

    private async Task AbortAsync()
    {
        try
        {
            await _session.AbortAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tab '{Title}': abort failed", Title);
            AppendMessage(new SessionMessage(
                MessageRole.System,
                $"Abort error: {ex.Message}",
                isNexusSystemMessage: true));
        }
    }

    private void ClearMessages()
    {
        Messages.Clear();
        _currentStreamingMessage = null;
        _currentReasoningMessage = null;
        _currentReasoningCorrelationId = null;
        _activityMessagesByCorrelation.Clear();
    }

    public void RequestClose()
    {
        _logger.LogInformation("Tab '{Title}': close requested", Title);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOutputReceived(object? sender, SessionOutputEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                HandleOutput(e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tab '{Title}': error handling output event", Title);
            }
        });
    }

    private void HandleOutput(SessionOutputEventArgs e)
    {
        switch (e.Kind)
        {
            case OutputKind.Delta:
                if (_currentStreamingMessage == null)
                {
                    _currentStreamingMessage = new SessionMessage(MessageRole.Assistant, e.Content, isStreaming: true);
                    AppendMessage(_currentStreamingMessage);
                }
                else
                {
                    _currentStreamingMessage.AppendContent(e.Content);
                }
                break;

            case OutputKind.ReasoningDelta:
                HandleReasoningDelta(e.Content, e.CorrelationId);
                break;

            case OutputKind.Reasoning:
                HandleReasoningMessage(e.Content, e.CorrelationId);
                break;

            case OutputKind.Message:
                if (_currentStreamingMessage != null && e.Role == MessageRole.Assistant)
                {
                    _logger.LogDebug("Tab '{Title}': skipping duplicate full message (already streamed)", Title);
                    break;
                }
                if (!string.IsNullOrEmpty(e.Content))
                {
                    AppendMessage(new SessionMessage(e.Role, e.Content));
                }
                break;

            case OutputKind.Activity:
                if (!string.IsNullOrWhiteSpace(e.Content))
                {
                    AppendActivityMessage(e.Content, e.CorrelationId);
                }
                break;

            case OutputKind.Idle:
                FinalizeStreamingMessage();
                break;
        }
    }

    private void FinalizeStreamingMessage()
    {
        if (_currentStreamingMessage != null)
        {
            _currentStreamingMessage.CompleteStreaming();
            _currentStreamingMessage = null;
        }

        if (_currentReasoningMessage != null)
        {
            _currentReasoningMessage.CompleteStreaming();
            _currentReasoningMessage = null;
            _currentReasoningCorrelationId = null;
        }
    }

    private void HandleReasoningDelta(string content, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        if (_currentReasoningMessage == null ||
            (!string.IsNullOrWhiteSpace(correlationId) &&
             !string.Equals(_currentReasoningCorrelationId, correlationId, StringComparison.Ordinal)))
        {
            if (_currentReasoningMessage != null)
            {
                _currentReasoningMessage.CompleteStreaming();
            }

            _currentReasoningMessage = new SessionMessage(
                MessageRole.System,
                "[thinking] ",
                isStreaming: true);
            _currentReasoningCorrelationId = correlationId;
            AppendMessage(_currentReasoningMessage);
        }

        _currentReasoningMessage.AppendContent(content);
    }

    private void HandleReasoningMessage(string content, string? correlationId)
    {
        if (_currentReasoningMessage != null)
        {
            if (!string.IsNullOrWhiteSpace(content) &&
                (string.IsNullOrWhiteSpace(correlationId) ||
                 string.Equals(_currentReasoningCorrelationId, correlationId, StringComparison.Ordinal)))
            {
                _currentReasoningMessage.ReplaceContent($"[thinking] {content}");
            }

            _currentReasoningMessage.CompleteStreaming();
            _currentReasoningMessage = null;
            _currentReasoningCorrelationId = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            AppendMessage(new SessionMessage(MessageRole.System, $"[thinking] {content}"));
        }
    }

    private void AppendActivityMessage(string content, string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId) &&
            _activityMessagesByCorrelation.TryGetValue(correlationId, out var existing) &&
            Messages.Contains(existing))
        {
            existing.ReplaceContent($"[activity] {content}");
            return;
        }

        var message = new SessionMessage(MessageRole.System, $"[activity] {content}");
        AppendMessage(message);
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            _activityMessagesByCorrelation[correlationId] = message;
        }
    }

    private void AppendMessage(SessionMessage message)
    {
        Messages.Add(message);
    }

    private void SetInputTextFromHistory(string value)
    {
        _suppressHistoryReset = true;
        try
        {
            InputText = value;
        }
        finally
        {
            _suppressHistoryReset = false;
        }
    }

    private void RecordInputHistory(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        _inputHistory.Add(input);
        if (_inputHistory.Count > 200)
            _inputHistory.RemoveAt(0);

        _inputHistoryIndex = -1;
        _historyDraftInput = string.Empty;
    }

    private void NotifyModelMetadataChanged()
    {
        OnPropertyChanged(nameof(CurrentModelDisplay));
        OnPropertyChanged(nameof(ReasoningLevelDisplay));
        OnPropertyChanged(nameof(ModelCostDisplay));
    }

    private void RefreshWorkingDirectoryMetadata()
    {
        _gitBranch = ResolveGitBranch(_workingDirectory);
        OnPropertyChanged(nameof(WorkingDirectoryDisplay));
        OnPropertyChanged(nameof(GitBranchDisplay));
    }

    private static string NormalizeMode(string? mode, bool autopilotFallback)
    {
        if (string.Equals(mode, "autopilot", StringComparison.OrdinalIgnoreCase))
            return "Autopilot";
        if (string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase))
            return "Plan";
        if (string.Equals(mode, "normal", StringComparison.OrdinalIgnoreCase))
            return "Normal";

        return autopilotFallback ? "Autopilot" : "Normal";
    }

    private void ApplySelectedMode(string mode)
    {
        var shouldAutopilot = string.Equals(mode, "Autopilot", StringComparison.Ordinal);
        if (_isAutopilot != shouldAutopilot)
        {
            _isAutopilot = shouldAutopilot;
            Info.IsAutopilot = shouldAutopilot;
            OnPropertyChanged(nameof(IsAutopilot));
            RequestReconfigure();
        }

        switch (mode)
        {
            case "Plan":
                AppendSystemMessage("Switched to plan mode — interactive approvals remain enabled.");
                break;
            case "Normal":
                AppendSystemMessage("Switched to normal mode — interactive approvals remain enabled.");
                break;
            default:
                AppendSystemMessage("Switched to autopilot mode — all tool calls will be auto-approved.");
                break;
        }
    }

    private static string ResolveReasoningLevel(ModelInfo? model)
    {
        if (model == null)
            return "standard";

        var explicitLevel = model.Capabilities
            .FirstOrDefault(capability => capability.StartsWith("reasoning:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(explicitLevel))
            return explicitLevel.Split(':', 2)[1].Trim();

        var hasReasoning = model.Capabilities.Any(capability =>
            capability.Contains("reasoning", StringComparison.OrdinalIgnoreCase));
        return hasReasoning ? "supported" : "standard";
    }

    private static string? ResolveModelCost(ModelInfo? model)
    {
        if (model == null)
            return null;

        var costCapability = model.Capabilities
            .FirstOrDefault(capability => capability.StartsWith("cost:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(costCapability))
            return null;

        var parts = costCapability.Split(':', 2);
        return parts.Length == 2 ? parts[1].Trim() : null;
    }

    private static string? ResolveGitBranch(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return null;

        try
        {
            var gitDirectory = FindGitDirectory(workingDirectory);
            if (string.IsNullOrWhiteSpace(gitDirectory))
                return null;

            var headPath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headPath))
                return null;

            var head = File.ReadAllText(headPath).Trim();
            const string refPrefix = "ref: ";
            if (head.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var reference = head[refPrefix.Length..].Trim();
                var segments = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length > 0 ? segments[^1] : reference;
            }

            return string.IsNullOrWhiteSpace(head) ? null : "detached";
        }
        catch
        {
            return null;
        }
    }

    private static string? FindGitDirectory(string workingDirectory)
    {
        var current = new DirectoryInfo(workingDirectory);
        while (current != null)
        {
            var dotGitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(dotGitPath))
                return dotGitPath;

            if (File.Exists(dotGitPath))
            {
                var pointer = File.ReadAllText(dotGitPath).Trim();
                const string gitDirPrefix = "gitdir:";
                if (!pointer.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
                    return null;

                var relativeGitDir = pointer[gitDirPrefix.Length..].Trim();
                return Path.IsPathRooted(relativeGitDir)
                    ? relativeGitDir
                    : Path.GetFullPath(Path.Combine(current.FullName, relativeGitDir));
            }

            current = current.Parent;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Tab '{Title}': disposing", Title);
        _session.OutputReceived -= OnOutputReceived;
        IsRunning = false;

        GC.SuppressFinalize(this);
    }
}
