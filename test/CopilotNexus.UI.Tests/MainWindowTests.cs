namespace CopilotNexus.UI.Tests;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CopilotNexus.App;
using CopilotNexus.App.Services;
using CopilotNexus.App.ViewModels;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Headless UI tests for MainWindow.
/// Runs in-process without any visible windows — no FlaUI, no process launch.
/// </summary>
public class MainWindowTests
{
    private static MainWindow CreateTestWindow()
    {
        // Ensure test mode is set
        App.StartupArgs = new[] { "--test-mode", "--reset-state" };
        return new MainWindow();
    }

    [AvaloniaFact]
    public void MainWindow_Creates_Successfully()
    {
        var window = CreateTestWindow();
        Assert.NotNull(window);
        Assert.Equal("Copilot Nexus", window.Title);
    }

    [AvaloniaFact]
    public void MainWindow_HasDataContext()
    {
        var window = CreateTestWindow();
        Assert.NotNull(window.DataContext);
        Assert.IsType<MainWindowViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public void NewSessionButton_Exists()
    {
        var window = CreateTestWindow();
        window.Show();

        var button = window.FindControl<Button>("NewSessionButton");
        Assert.NotNull(button);
        Assert.True(button.IsEnabled);
    }

    [AvaloniaFact]
    public async Task NewSessionButton_Click_CreatesTab()
    {
        var window = CreateTestWindow();
        window.Show();

        // Wait for initialization
        await Task.Delay(500);

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.CreateNewTabAsync();

        Assert.Single(vm.Tabs);
    }

    [AvaloniaFact]
    public async Task CreateMultipleTabs_AllTracked()
    {
        var window = CreateTestWindow();
        window.Show();

        await Task.Delay(500);

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.CreateNewTabAsync();
        await vm.CreateNewTabAsync();

        Assert.Equal(2, vm.Tabs.Count);
    }

    [AvaloniaFact]
    public async Task CloseTab_RemovesFromCollection()
    {
        var window = CreateTestWindow();
        window.Show();

        await Task.Delay(500);

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.CreateNewTabAsync();
        Assert.Single(vm.Tabs);

        // Close via command
        vm.CloseTabCommand.Execute(null);
        await Task.Delay(300);

        Assert.Empty(vm.Tabs);
    }

    [AvaloniaFact]
    public void SessionTabControl_Exists()
    {
        var window = CreateTestWindow();
        window.Show();

        var tabControl = window.FindControl<TabControl>("SessionTabControl");
        Assert.NotNull(tabControl);
    }
}
