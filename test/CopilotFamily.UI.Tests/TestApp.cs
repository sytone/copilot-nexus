namespace CopilotFamily.UI.Tests;

using Avalonia;
using Avalonia.Themes.Fluent;
using CopilotFamily.App;
using Microsoft.Extensions.Logging;

/// <summary>
/// Test application that loads Avalonia styles/resources but uses test-mode services.
/// Runs in-process headlessly — no visible windows.
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        // Apply Fluent theme programmatically (no AXAML needed for test app)
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Set test mode flags
        App.StartupArgs = new[] { "--test-mode", "--reset-state" };
        App.IsTestMode = true;
        App.ResetState = true;

        // Ensure logging is available for MainWindow constructor
        if (App.LoggerFactoryInstance == null)
        {
            App.LoggerFactoryInstance = new LoggerFactory();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
