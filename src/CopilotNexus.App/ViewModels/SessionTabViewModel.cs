namespace CopilotNexus.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class SessionTabViewModel : ViewModelBase, IDisposable
{
    private ICopilotSessionWrapper _session;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private SessionMessage? _currentStreamingMessage;
    private string _inputText = string.Empty;
    private string _title;
    private bool _isRunning;
    private bool _isProcessing;
    private bool _isAutopilot;
    private string? _workingDirectory;
    private ModelInfo? _selectedModel;
    private bool _disposed;

    public SessionInfo Info { get; private set; }
    public ObservableCollection<SessionMessage> Messages { get; } = new();

    /// <summary>Available models for the model selector.</summary>
    public ObservableCollection<ModelInfo> AvailableModels { get; }

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
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
            }
        }
    }

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
        }
    }

    public ICommand SendCommand { get; }
    public ICommand AbortCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand BrowseWorkingDirectoryCommand { get; }
    public ICommand ToggleAutopilotCommand { get; }

    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised when the session needs to be reconfigured (model change, working directory change).
    /// The MainWindowViewModel handles the disconnect + resume.
    /// </summary>
    public event EventHandler<SessionConfiguration>? ReconfigureRequested;

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
        _isAutopilot = info.IsAutopilot;
        _workingDirectory = info.WorkingDirectory;
        IsRunning = true;

        AvailableModels = availableModels ?? new ObservableCollection<ModelInfo>();

        // Set selected model from info without triggering reconfigure
        _selectedModel = AvailableModels.FirstOrDefault(m => m.ModelId == info.Model);

        SendCommand = new AsyncRelayCommand(SendInputAsync, () => IsRunning && !IsProcessing && !string.IsNullOrWhiteSpace(InputText));
        AbortCommand = new AsyncRelayCommand(AbortAsync, () => IsProcessing);
        ClearCommand = new RelayCommand(ClearMessages);
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

        // Update selected model without triggering reconfigure
        _selectedModel = AvailableModels.FirstOrDefault(m => m.ModelId == newInfo.Model);
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(IsAutopilot));
        OnPropertyChanged(nameof(WorkingDirectory));

        _session.OutputReceived += OnOutputReceived;

        var msg = $"Session reconfigured — model: {newInfo.Model ?? "default"}";
        if (newInfo.WorkingDirectory != null)
            msg += $" | path: {newInfo.WorkingDirectory}";
        AppendSystemMessage(msg);

        _logger.LogInformation("Tab '{Title}' reconfigured with new session {SessionId}", Title, newInfo.Id);
    }

    private void RequestReconfigure(string? model = null, string? workDir = null)
    {
        var config = new SessionConfiguration
        {
            Model = model ?? Info.Model,
            WorkingDirectory = workDir ?? Info.WorkingDirectory,
            IsAutopilot = Info.IsAutopilot,
        };

        _logger.LogInformation("Tab '{Title}': requesting reconfigure — model={Model}, workDir={WorkDir}",
            Title, config.Model, config.WorkingDirectory);

        ReconfigureRequested?.Invoke(this, config);
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

    private async Task SendInputAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var input = InputText;
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

            case OutputKind.Message:
                if (_currentStreamingMessage != null)
                {
                    _logger.LogDebug("Tab '{Title}': skipping duplicate full message (already streamed)", Title);
                    break;
                }
                if (!string.IsNullOrEmpty(e.Content))
                {
                    AppendMessage(new SessionMessage(e.Role, e.Content));
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
    }

    private void AppendMessage(SessionMessage message)
    {
        Messages.Add(message);
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
