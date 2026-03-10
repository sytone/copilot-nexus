namespace CopilotFamily.App;

using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using Serilog;

public partial class App : Application
{
    private static Microsoft.Extensions.Logging.ILogger? _appLogger;

    public static string[] StartupArgs { get; set; } = Array.Empty<string>();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotFamily", "logs");

    public static bool IsTestMode { get; internal set; }

    /// <summary>When true, persisted session state is deleted on startup (clean slate).</summary>
    public static bool ResetState { get; internal set; }

    /// <summary>When true, the main window starts minimized (useful for UI test runs).</summary>
    public static bool StartMinimized { get; internal set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        IsTestMode = StartupArgs.Contains("--test-mode");
        ResetState = StartupArgs.Contains("--reset-state");
        StartMinimized = StartupArgs.Contains("--minimized");

        ConfigureLogging();

        _appLogger!.LogInformation("=== Copilot Family starting (TestMode={TestMode}, ResetState={ResetState}, Minimized={Minimized}) ===",
            IsTestMode, ResetState, StartMinimized);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureLogging()
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(LogDirectory, "copilot-family-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug(
                outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var factory = new LoggerFactory().AddSerilog(Log.Logger);
        _appLogger = factory.CreateLogger("CopilotFamily.App");

        // Store factory for MainWindow to access
        LoggerFactoryInstance = factory;
    }

    public static ILoggerFactory? LoggerFactoryInstance { get; internal set; }
}
