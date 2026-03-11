namespace CopilotNexus.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private SessionTabViewModel? _selectedTab;
    private int _sessionCounter;
    private bool _disposed;
    private string? _statusText;
    private bool _isUpdateAvailable;
    private string _updateNotificationText = "A new version is available.";
    private ModelInfo? _selectedModel;

    public ObservableCollection<SessionTabViewModel> Tabs { get; } = new();

    /// <summary>Available models (populated after InitializeAsync).</summary>
    public ObservableCollection<ModelInfo> AvailableModels { get; } = new();

    public SessionTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => SetProperty(ref _isUpdateAvailable, value);
    }

    public string UpdateNotificationText
    {
        get => _updateNotificationText;
        private set => SetProperty(ref _updateNotificationText, value);
    }

    /// <summary>Default model for new sessions (user can override per-tab).</summary>
    public ModelInfo? SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value);
    }

    /// <summary>Raised when the user accepts a hot restart. The host (MainWindow) should handle the process lifecycle.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Raised when the user dismisses the update notification.</summary>
    public event EventHandler? UpdateDismissed;

    /// <summary>
    /// Raised when a permission dialog needs to be shown (interactive mode).
    /// The host should display a dialog and set the TaskCompletionSource result.
    /// </summary>
    public event Func<ToolPermissionRequest, Task<PermissionDecision>>? PermissionRequested;

    /// <summary>
    /// Raised when the agent asks for user input (interactive mode).
    /// The host should display a dialog and return the response.
    /// </summary>
    public event Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? UserInputRequested;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand RestartNowCommand { get; }
    public ICommand DismissUpdateCommand { get; }

    public MainWindowViewModel(ISessionManager sessionManager, IUiDispatcher dispatcher, ILogger<MainWindowViewModel> logger)
    {
        _sessionManager = sessionManager;
        _dispatcher = dispatcher;
        _logger = logger;
        NewTabCommand = new AsyncRelayCommand(CreateNewTabAsync);
        CloseTabCommand = new AsyncRelayCommand(CloseTabAsync);
        RestartNowCommand = new RelayCommand(OnRestartNow);
        DismissUpdateCommand = new RelayCommand(OnDismissUpdate);
    }

    public void ShowUpdateNotification(string? currentVersion = null, string? availableVersion = null)
    {
        _dispatcher.BeginInvoke(() =>
        {
            UpdateNotificationText = BuildUpdateNotificationText(currentVersion, availableVersion);
            IsUpdateAvailable = true;
        });
    }

    private static string BuildUpdateNotificationText(string? currentVersion, string? availableVersion)
    {
        var current = string.IsNullOrWhiteSpace(currentVersion) ? null : currentVersion;
        var available = string.IsNullOrWhiteSpace(availableVersion) ? null : availableVersion;

        if (current != null && available != null)
        {
            if (string.Equals(current, available, StringComparison.OrdinalIgnoreCase))
                return $"Update available: version {available}.";

            return $"Update available: {current} → {available}.";
        }

        if (available != null)
            return $"Update available: {available}.";

        return "A new version is available.";
    }

    private void OnRestartNow()
    {
        _logger.LogInformation("User accepted hot restart");
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDismissUpdate()
    {
        _logger.LogInformation("User dismissed update notification");
        IsUpdateAvailable = false;
        UpdateDismissed?.Invoke(this, EventArgs.Empty);
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing application...");
        StatusText = "Connecting to Copilot CLI…";
        await _sessionManager.InitializeAsync();

        // Populate available models
        foreach (var model in _sessionManager.AvailableModels)
        {
            AvailableModels.Add(model);
        }

        // Select default model
        SelectedModel = AvailableModels.FirstOrDefault(m => m.ModelId == "gpt-4.1")
                      ?? AvailableModels.FirstOrDefault();

        _logger.LogInformation("Loaded {Count} models, default: {Model}",
            AvailableModels.Count, SelectedModel?.ModelId ?? "none");

        StatusText = null;
        _logger.LogInformation("Application initialized successfully");
    }

    /// <summary>
    /// Restores tabs by resuming SDK sessions. Falls back to creating
    /// fresh sessions if a resume fails (e.g., session data expired).
    /// </summary>
    public async Task RestoreStateAsync(AppState state)
    {
        _sessionCounter = state.SessionCounter;
        _logger.LogInformation("Restoring {Count} tabs from saved state", state.Tabs.Count);

        foreach (var tabState in state.Tabs)
        {
            StatusText = $"Restoring {tabState.Name}…";

            try
            {
                SessionInfo sessionInfo;
                string systemMessage;

                var config = new SessionConfiguration
                {
                    Model = tabState.Model,
                    WorkingDirectory = tabState.WorkingDirectory,
                    IsAutopilot = tabState.IsAutopilot,
                };

                var permHandler = tabState.IsAutopilot ? null : GetPermissionHandler();
                var inputHandler = tabState.IsAutopilot ? null : GetUserInputHandler();

                try
                {
                    sessionInfo = await _sessionManager.ResumeSessionAsync(
                        tabState.Name, tabState.SdkSessionId, config, permHandler, inputHandler);
                    systemMessage = "Session resumed";
                    _logger.LogInformation("Resumed SDK session {SdkId} for tab '{Name}'",
                        tabState.SdkSessionId, tabState.Name);
                }
                catch (Exception resumeEx)
                {
                    _logger.LogWarning(resumeEx, "Failed to resume SDK session {SdkId}, creating fresh session",
                        tabState.SdkSessionId);
                    sessionInfo = await _sessionManager.CreateSessionAsync(
                        tabState.Name, config, permHandler, inputHandler);
                    systemMessage = "Previous session could not be resumed — started fresh";
                }

                var session = _sessionManager.GetSession(sessionInfo.Id)!;
                var tabViewModel = new SessionTabViewModel(sessionInfo, session, _dispatcher, _logger, AvailableModels);
                tabViewModel.CloseRequested += (_, _) => _ = CloseSpecificTabAsync(tabViewModel);
                tabViewModel.ReconfigureRequested += OnTabReconfigureRequested;

                tabViewModel.Messages.Clear();
                tabViewModel.Messages.Add(new SessionMessage(MessageRole.System, systemMessage));

                Tabs.Add(tabViewModel);
                _logger.LogInformation("Restored tab '{Name}'", tabState.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore tab '{Name}'", tabState.Name);
            }
        }

        if (state.SelectedTabIndex >= 0 && state.SelectedTabIndex < Tabs.Count)
        {
            SelectedTab = Tabs[state.SelectedTabIndex];
        }
        else if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[0];
        }

        StatusText = null;
    }

    /// <summary>
    /// Captures the current application state for persistence.
    /// Only stores tab metadata — the SDK handles conversation history.
    /// </summary>
    public AppState CaptureState()
    {
        var state = new AppState
        {
            SessionCounter = _sessionCounter,
            SelectedTabIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : -1,
        };

        foreach (var tab in Tabs)
        {
            state.Tabs.Add(new TabState
            {
                Name = tab.Title,
                Model = tab.Info.Model,
                SdkSessionId = tab.Info.SdkSessionId,
                WorkingDirectory = tab.Info.WorkingDirectory,
                IsAutopilot = tab.Info.IsAutopilot,
            });
        }

        _logger.LogInformation("State captured ({Count} tabs)", state.Tabs.Count);
        return state;
    }

    public async Task CreateNewTabAsync()
    {
        _sessionCounter++;
        var name = $"Session {_sessionCounter}";

        _logger.LogInformation("Creating new tab '{Name}'", name);
        StatusText = $"Creating {name}…";

        try
        {
            var config = new SessionConfiguration
            {
                Model = SelectedModel?.ModelId,
                IsAutopilot = true,
            };

            var sessionInfo = await _sessionManager.CreateSessionAsync(name, config);
            var session = _sessionManager.GetSession(sessionInfo.Id)!;

            var tabViewModel = new SessionTabViewModel(sessionInfo, session, _dispatcher, _logger, AvailableModels);
            tabViewModel.CloseRequested += (_, _) => _ = CloseSpecificTabAsync(tabViewModel);
            tabViewModel.ReconfigureRequested += OnTabReconfigureRequested;

            Tabs.Add(tabViewModel);
            SelectedTab = tabViewModel;

            _logger.LogInformation("Tab '{Name}' created successfully", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tab '{Name}'", name);

            _dispatcher.BeginInvoke(() =>
            {
                // Error is logged — the view can subscribe to a notification event if needed
                StatusText = $"Failed to create session: {ex.Message}";
            });
        }
        finally
        {
            StatusText = null;
        }
    }

    private async void OnTabReconfigureRequested(object? sender, SessionConfiguration config)
    {
        if (sender is not SessionTabViewModel tab) return;

        _logger.LogInformation("Reconfiguring tab '{Title}' — model={Model}, workDir={WorkDir}, autopilot={Autopilot}",
            tab.Title, config.Model ?? "(unchanged)", config.WorkingDirectory ?? "(unchanged)", config.IsAutopilot);

        StatusText = $"Reconfiguring {tab.Title}…";

        try
        {
            var permHandler = config.IsAutopilot ? null : GetPermissionHandler();
            var inputHandler = config.IsAutopilot ? null : GetUserInputHandler();

            var oldSessionId = tab.Info.Id;
            var newInfo = await _sessionManager.ReconfigureSessionAsync(
                tab.Info.Id, config, permHandler, inputHandler);
            var newSession = _sessionManager.GetSession(newInfo.Id);
            if (newSession == null)
            {
                throw new InvalidOperationException(
                    $"Reconfigured session wrapper not found (oldId={oldSessionId}, newId={newInfo.Id}).");
            }

            // Replace the tab's session
            tab.Reconfigure(newInfo, newSession);

            _logger.LogInformation("Tab '{Title}' reconfigured successfully", tab.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconfigure tab '{Title}'", tab.Title);
            tab.AppendSystemMessage($"Reconfiguration failed: {ex.Message}");
        }
        finally
        {
            StatusText = null;
        }
    }

    private Func<ToolPermissionRequest, Task<PermissionDecision>>? GetPermissionHandler()
    {
        if (PermissionRequested != null)
            return PermissionRequested;
        return null;
    }

    private Func<AgentUserInputRequest, Task<AgentUserInputResponse>>? GetUserInputHandler()
    {
        if (UserInputRequested != null)
            return UserInputRequested;
        return null;
    }

    private async Task CloseTabAsync()
    {
        if (SelectedTab != null)
        {
            await CloseSpecificTabAsync(SelectedTab);
        }
    }

    private async Task CloseSpecificTabAsync(SessionTabViewModel tab)
    {
        _logger.LogInformation("Closing tab '{Title}'", tab.Title);

        try
        {
            await _sessionManager.RemoveSessionAsync(tab.Info.Id, deleteFromDisk: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing session for tab '{Title}'", tab.Title);
        }

        tab.ReconfigureRequested -= OnTabReconfigureRequested;
        Tabs.Remove(tab);

        if (SelectedTab == tab || SelectedTab == null)
        {
            SelectedTab = Tabs.Count > 0 ? Tabs[^1] : null;
        }

        tab.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing MainWindowViewModel ({Count} tabs)", Tabs.Count);

        foreach (var tab in Tabs)
        {
            tab.ReconfigureRequested -= OnTabReconfigureRequested;
            tab.Dispose();
        }

        Tabs.Clear();

        GC.SuppressFinalize(this);
    }
}
