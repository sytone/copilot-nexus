namespace CopilotFamily.App;

using System.Diagnostics;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CopilotFamily.App.Services;
using CopilotFamily.App.ViewModels;
using CopilotFamily.Core.Interfaces;
using CopilotFamily.Core.Services;
using Microsoft.Extensions.Logging;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ISessionManager _sessionManager;
    private readonly IStatePersistenceService _stateService;
    private readonly ILogger _logger;
    private StagingUpdateDetectionService? _updateService;
    private DispatcherTimer? _redisplayTimer;

    /// <summary>Default Nexus URL — can be overridden via --nexus-url argument.</summary>
    private const string DefaultNexusUrl = "http://localhost:5280";

    public MainWindow()
    {
        InitializeComponent();

        var factory = App.LoggerFactoryInstance!;
        _logger = factory.CreateLogger<MainWindow>();
        var dispatcher = new AvaloniaUiDispatcher();

        if (App.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }

        if (App.IsTestMode)
        {
            _logger.LogInformation("Running in TEST MODE with mock services");
            var mockClient = new MockCopilotClientService(factory.CreateLogger<MockCopilotClientService>());
            _sessionManager = new SessionManager(mockClient, factory.CreateLogger<SessionManager>());
        }
        else
        {
            var nexusUrl = GetNexusUrl();
            _logger.LogInformation("Connecting to Nexus at {Url}", nexusUrl);
            _sessionManager = new NexusSessionManager(nexusUrl, _logger);
        }

        _stateService = new JsonStatePersistenceService(factory.CreateLogger<JsonStatePersistenceService>());
        _viewModel = new MainWindowViewModel(_sessionManager, dispatcher, factory.CreateLogger<MainWindowViewModel>());
        _viewModel.RestartRequested += OnRestartRequested;
        _viewModel.UpdateDismissed += OnUpdateDismissed;
        DataContext = _viewModel;

        Opened += OnWindowOpened;
    }

    private static string GetNexusUrl()
    {
        var args = App.StartupArgs;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--nexus-url")
                return args[i + 1];
        }
        return DefaultNexusUrl;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            if (App.ResetState)
            {
                _logger.LogInformation("--reset-state: clearing persisted session state");
                await _stateService.ClearAsync();
            }

            await _viewModel.InitializeAsync();

            if (!App.ResetState)
            {
                var savedState = await _stateService.LoadAsync();
                if (savedState != null && savedState.Tabs.Count > 0)
                {
                    await _viewModel.RestoreStateAsync(savedState);
                }
            }

            _logger.LogInformation("MainWindow initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize — Copilot CLI may not be available");
        }

        StartUpdateDetection();
    }

    private void StartUpdateDetection()
    {
        var stagingDir = CopilotFamily.Core.CopilotFamilyPaths.AppStaging;

        _updateService = new StagingUpdateDetectionService(stagingDir, _logger);
        _updateService.UpdateAvailable += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => _viewModel.ShowUpdateNotification());
        };
        _updateService.StartWatching();
    }

    private async void OnRestartRequested(object? sender, EventArgs e)
    {
        _logger.LogInformation("Hot restart initiated");

        try
        {
            var state = _viewModel.CaptureState();
            await _stateService.SaveAsync(state);

            var updaterPath = ExtractUpdaterScript();
            var appExePath = Environment.ProcessPath!;
            var installDir = CopilotFamily.Core.CopilotFamilyPaths.AppInstall;
            var stagingDir = CopilotFamily.Core.CopilotFamilyPaths.AppStaging;
            var pid = Environment.ProcessId;

            _logger.LogInformation("Launching updater: PID={Pid}, Install={Install}, Staging={Staging}", pid, installDir, stagingDir);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{updaterPath}\" -AppPid {pid} -InstallPath \"{installDir}\" -StagingPath \"{stagingDir}\" -AppExe \"{appExePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
            {
                lifetime.Shutdown();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot restart failed");
        }
    }

    private void OnUpdateDismissed(object? sender, EventArgs e)
    {
        _updateService?.ResetNotification();

        _redisplayTimer?.Stop();
        _redisplayTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _redisplayTimer.Tick += (_, _) =>
        {
            _redisplayTimer.Stop();
            _updateService?.ResetNotification();
        };
        _redisplayTimer.Start();
    }

    private static string ExtractUpdaterScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("update.ps1", StringComparison.OrdinalIgnoreCase));

        var tempDir = Path.Combine(Path.GetTempPath(), "CopilotFamily-Updater");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, "update.ps1");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var fs = File.Create(tempPath);
        stream.CopyTo(fs);

        return tempPath;
    }

    private void CloseTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is SessionTabViewModel tab)
        {
            tab.RequestClose();
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        _logger.LogInformation("MainWindow closing — saving state");

        _updateService?.Dispose();
        _redisplayTimer?.Stop();

        try
        {
            var state = _viewModel.CaptureState();
            await _stateService.SaveAsync(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state on exit");
        }

        _viewModel.Dispose();
        await _sessionManager.DisposeAsync();
        base.OnClosed(e);
    }
}
