namespace CopilotNexus.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using CopilotNexus.App.Services;
using CopilotNexus.App.Utilities;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Versioning;
using Microsoft.Extensions.Logging;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ISessionProfileService _profileService;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private SessionTabViewModel? _selectedTab;
    private SessionProfile? _selectedProfile;
    private int _sessionCounter;
    private bool _disposed;
    private string? _statusText;
    private bool _isUpdateAvailable;
    private bool _isProfileEditorVisible;
    private string _updateNotificationText = "A new version is available.";
    private ModelInfo? _selectedModel;

    public ObservableCollection<SessionTabViewModel> Tabs { get; } = new();
    public ObservableCollection<SessionProfile> Profiles { get; } = new();

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

    public SessionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplySelectedProfileDefaults(value);
            }
        }
    }

    public bool IsProfileEditorVisible
    {
        get => _isProfileEditorVisible;
        set
        {
            if (SetProperty(ref _isProfileEditorVisible, value))
                OnPropertyChanged(nameof(IsSessionEditorVisible));
        }
    }

    public bool IsSessionEditorVisible => !IsProfileEditorVisible;

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
    public ICommand ShowSessionsViewCommand { get; }
    public ICommand ShowProfilesViewCommand { get; }
    public ICommand NewProfileCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }

    public MainWindowViewModel(
        ISessionManager sessionManager,
        IUiDispatcher dispatcher,
        ILogger<MainWindowViewModel> logger,
        ISessionProfileService? profileService = null)
    {
        _sessionManager = sessionManager;
        _profileService = profileService ?? new InMemorySessionProfileService();
        _dispatcher = dispatcher;
        _logger = logger;
        NewTabCommand = new AsyncRelayCommand(CreateNewTabAsync);
        CloseTabCommand = new AsyncRelayCommand(CloseTabAsync);
        RestartNowCommand = new RelayCommand(OnRestartNow);
        DismissUpdateCommand = new RelayCommand(OnDismissUpdate);
        ShowSessionsViewCommand = new RelayCommand(() => IsProfileEditorVisible = false);
        ShowProfilesViewCommand = new RelayCommand(() => IsProfileEditorVisible = true);
        NewProfileCommand = new RelayCommand(CreateNewProfile);
        SaveProfileCommand = new AsyncRelayCommand(SaveSelectedProfileAsync);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync);
    }

    public void ShowUpdateNotification(string? currentVersion = null, string? availableVersion = null)
    {
        if (!ShouldShowUpdateNotification(currentVersion, availableVersion))
        {
            _logger.LogInformation(
                "Ignoring update notification because available version is not newer (current={CurrentVersion}, available={AvailableVersion})",
                currentVersion,
                availableVersion);
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            UpdateNotificationText = BuildUpdateNotificationText(currentVersion, availableVersion);
            IsUpdateAvailable = true;
        });
    }

    internal static bool ShouldShowUpdateNotification(string? currentVersion, string? availableVersion)
    {
        var hasCurrent = !string.IsNullOrWhiteSpace(currentVersion);
        var hasAvailable = !string.IsNullOrWhiteSpace(availableVersion);
        if (!hasCurrent || !hasAvailable)
            return true;

        if (!SemanticVersion.TryParse(currentVersion, out var current) || current is null)
            return true;
        if (!SemanticVersion.TryParse(availableVersion, out var available) || available is null)
            return true;

        return available.CompareTo(current) > 0;
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
        StatusText = "Connecting to agent runtime…";
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

        try
        {
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load session profiles; using fallback default profile.");
            Profiles.Clear();
            var fallback = new SessionProfile
            {
                Id = "default",
                Name = "Default",
                Description = "Fallback profile",
                IsAutopilot = true,
                IncludeWellKnownMcpConfigs = true,
            };
            Profiles.Add(fallback);
            SelectedProfile = fallback;
        }

        StatusText = null;
        _logger.LogInformation("Application initialized successfully");
    }

    private async Task LoadProfilesAsync()
    {
        var previousSelectedId = SelectedProfile?.Id;
        var profiles = await _profileService.ListAsync();

        Profiles.Clear();
        foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add(profile);
        }

        if (Profiles.Count == 0)
        {
            var defaultProfile = await _profileService.SaveAsync(new SessionProfile
            {
                Name = "Default",
                Description = "Default Nexus profile",
                IsAutopilot = true,
                IncludeWellKnownMcpConfigs = true,
            });
            Profiles.Add(defaultProfile);
        }

        SelectedProfile = previousSelectedId != null
            ? Profiles.FirstOrDefault(profile => string.Equals(profile.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
            : SelectedProfile;
        SelectedProfile ??= Profiles.FirstOrDefault();

        foreach (var tab in Tabs)
        {
            tab.RefreshSelectedProfile();
        }
    }

    private void ApplySelectedProfileDefaults(SessionProfile? profile)
    {
        if (profile == null)
            return;

        if (!string.IsNullOrWhiteSpace(profile.Model))
        {
            var matched = AvailableModels.FirstOrDefault(model =>
                string.Equals(model.ModelId, profile.Model, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
                SelectedModel = matched;
        }
    }

    private void CreateNewProfile()
    {
        var profile = new SessionProfile
        {
            Name = "New Profile",
            IsAutopilot = true,
            IncludeWellKnownMcpConfigs = true,
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        IsProfileEditorVisible = true;
    }

    private async Task SaveSelectedProfileAsync()
    {
        if (SelectedProfile == null)
            return;

        var saved = await _profileService.SaveAsync(SelectedProfile);
        await LoadProfilesAsync();
        SelectedProfile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation("Saved profile '{Name}' ({Id})", saved.Name, saved.Id);
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfile == null || string.IsNullOrWhiteSpace(SelectedProfile.Id))
            return;

        var profileId = SelectedProfile.Id;
        await _profileService.DeleteAsync(profileId);
        await LoadProfilesAsync();
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
                    IsAutopilot = string.Equals(
                        NormalizeSessionMode(tabState.SessionMode, tabState.IsAutopilot),
                        "Autopilot",
                        StringComparison.Ordinal),
                    ProfileId = tabState.ProfileId,
                    AgentFilePath = tabState.AgentFilePath,
                    IncludeWellKnownMcpConfigs = tabState.IncludeWellKnownMcpConfigs,
                    AdditionalMcpConfigPaths = tabState.AdditionalMcpConfigPaths ?? new List<string>(),
                    EnabledMcpServers = tabState.EnabledMcpServers ?? new List<string>(),
                    SkillDirectories = tabState.SkillDirectories ?? new List<string>(),
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
                var history = Array.Empty<SessionOutputEventArgs>();
                if (string.Equals(systemMessage, "Session resumed", StringComparison.Ordinal))
                {
                    try
                    {
                        history = (await session.GetHistoryAsync())
                            .Where(item => item.Kind != OutputKind.Idle)
                            .Where(item => !string.IsNullOrWhiteSpace(item.Content))
                            .ToArray();
                        _logger.LogInformation("Loaded {Count} history messages for resumed tab '{Name}'", history.Length, tabState.Name);
                    }
                    catch (Exception historyEx)
                    {
                        _logger.LogWarning(historyEx, "Failed to load history for resumed tab '{Name}'", tabState.Name);
                    }
                }
                var nexusSystemMessages = (tabState.NexusSystemMessages ?? [])
                    .Where(msg => msg.Role == MessageRole.System)
                    .Where(msg => !string.IsNullOrWhiteSpace(msg.Content))
                    .OrderBy(msg => msg.Timestamp)
                    .ToArray();

                var tabViewModel = new SessionTabViewModel(
                    sessionInfo,
                    session,
                    _dispatcher,
                    _logger,
                    AvailableModels,
                    Profiles);
                tabViewModel.CloseRequested += (_, _) => _ = CloseSpecificTabAsync(tabViewModel);
                tabViewModel.ReconfigureRequested += OnTabReconfigureRequested;
                tabViewModel.RenameRequested += OnTabRenameRequested;
                tabViewModel.RestoreMode(NormalizeSessionMode(tabState.SessionMode, tabState.IsAutopilot));

                tabViewModel.Messages.Clear();
                foreach (var item in history)
                {
                    tabViewModel.Messages.Add(new SessionMessage(item.Role, FormatRestoredOutputContent(item)));
                }
                foreach (var item in nexusSystemMessages)
                {
                    tabViewModel.Messages.Add(new SessionMessage(
                        item.Role,
                        item.Content,
                        isNexusSystemMessage: true)
                    {
                        Timestamp = item.Timestamp == default ? DateTime.UtcNow : item.Timestamp,
                    });
                }
                tabViewModel.Messages.Add(new SessionMessage(
                    MessageRole.System,
                    systemMessage,
                    isNexusSystemMessage: true));

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
    /// Stores tab metadata and Nexus-generated system messages.
    /// The SDK handles conversation history.
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
            var nexusSystemMessages = tab.Messages
                .Where(message => message.IsNexusSystemMessage)
                .Where(message => message.Role == MessageRole.System)
                .Where(message => !string.IsNullOrWhiteSpace(message.Content))
                .Select(message => new MessageState
                {
                    Role = message.Role,
                    Content = message.Content,
                    Timestamp = message.Timestamp,
                })
                .ToList();

            state.Tabs.Add(new TabState
            {
                Name = tab.Title,
                Model = tab.Info.Model,
                SdkSessionId = tab.Info.SdkSessionId,
                WorkingDirectory = tab.Info.WorkingDirectory,
                IsAutopilot = tab.Info.IsAutopilot,
                SessionMode = tab.SelectedMode,
                ProfileId = tab.Info.ProfileId,
                AgentFilePath = tab.Info.AgentFilePath,
                IncludeWellKnownMcpConfigs = tab.Info.IncludeWellKnownMcpConfigs,
                AdditionalMcpConfigPaths = tab.Info.AdditionalMcpConfigPaths.ToList(),
                EnabledMcpServers = tab.Info.EnabledMcpServers.ToList(),
                SkillDirectories = tab.Info.SkillDirectories.ToList(),
                NexusSystemMessages = nexusSystemMessages,
            });
        }

        _logger.LogInformation("State captured ({Count} tabs)", state.Tabs.Count);
        return state;
    }

    public async Task CreateNewTabAsync()
    {
        _sessionCounter++;
        var name = $"Session {_sessionCounter}";
        var profile = SelectedProfile;

        _logger.LogInformation("Creating new tab '{Name}'", name);
        StatusText = $"Creating {name}…";

        try
        {
            var profileModel = string.IsNullOrWhiteSpace(profile?.Model) ? null : profile!.Model;
            var resolvedModel = profileModel ?? SelectedModel?.ModelId;

            var config = new SessionConfiguration
            {
                Model = resolvedModel,
                IsAutopilot = profile?.IsAutopilot ?? true,
                WorkingDirectory = profile?.WorkingDirectory,
                ProfileId = profile?.Id,
                AgentFilePath = profile?.AgentFilePath,
                IncludeWellKnownMcpConfigs = profile?.IncludeWellKnownMcpConfigs ?? true,
                AdditionalMcpConfigPaths = DelimitedListParser.Parse(profile?.AdditionalMcpConfigPaths),
                EnabledMcpServers = DelimitedListParser.Parse(profile?.EnabledMcpServers),
                SkillDirectories = DelimitedListParser.Parse(profile?.AdditionalSkillDirectories),
            };

            var sessionInfo = await _sessionManager.CreateSessionAsync(name, config);
            var session = _sessionManager.GetSession(sessionInfo.Id)!;

            var tabViewModel = new SessionTabViewModel(
                sessionInfo,
                session,
                _dispatcher,
                _logger,
                AvailableModels,
                Profiles);
            tabViewModel.CloseRequested += (_, _) => _ = CloseSpecificTabAsync(tabViewModel);
            tabViewModel.ReconfigureRequested += OnTabReconfigureRequested;
            tabViewModel.RenameRequested += OnTabRenameRequested;

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

    private async void OnTabRenameRequested(object? sender, string newName)
    {
        if (sender is not SessionTabViewModel tab)
            return;

        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(tab.Title, newName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var updated = await _sessionManager.RenameSessionAsync(tab.Info.Id, newName);
            tab.ApplyRename(updated.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename tab '{Title}'", tab.Title);
            tab.AppendSystemMessage($"Rename failed: {ex.Message}");
        }
    }

    private static string NormalizeSessionMode(string? mode, bool isAutopilot)
    {
        if (string.Equals(mode, "autopilot", StringComparison.OrdinalIgnoreCase))
            return "Autopilot";
        if (string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase))
            return "Plan";
        if (string.Equals(mode, "normal", StringComparison.OrdinalIgnoreCase))
            return "Normal";

        return isAutopilot ? "Autopilot" : "Normal";
    }

    private static string FormatRestoredOutputContent(SessionOutputEventArgs item)
    {
        return item.Kind switch
        {
            OutputKind.Activity => $"[activity] {item.Content}",
            OutputKind.Reasoning or OutputKind.ReasoningDelta => $"[thinking] {item.Content}",
            _ => item.Content,
        };
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
        tab.RenameRequested -= OnTabRenameRequested;
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
            tab.RenameRequested -= OnTabRenameRequested;
            tab.Dispose();
        }

        Tabs.Clear();

        GC.SuppressFinalize(this);
    }
}
