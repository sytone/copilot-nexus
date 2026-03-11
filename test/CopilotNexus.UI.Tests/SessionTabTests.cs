namespace CopilotFamily.UI.Tests;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CopilotFamily.App;
using CopilotFamily.App.ViewModels;
using CopilotFamily.App.Views;
using CopilotFamily.Core.Models;
using Xunit;

/// <summary>
/// Headless UI tests for session tab interactions.
/// Uses ViewModel-level assertions (proper MVVM testing) rather than
/// control name lookup across name scopes.
/// </summary>
public class SessionTabTests
{
    private static async Task<(MainWindow window, MainWindowViewModel vm)> CreateWindowWithTabAsync()
    {
        App.StartupArgs = new[] { "--test-mode", "--reset-state" };
        App.IsTestMode = true;
        App.ResetState = true;
        var window = new MainWindow();
        window.Show();
        await Task.Delay(500);

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.CreateNewTabAsync();
        await Task.Delay(300);

        return (window, vm);
    }

    [AvaloniaFact]
    public async Task Tab_HasCorrectViewModel()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.NotNull(tab);
        Assert.IsType<SessionTabViewModel>(tab);
    }

    [AvaloniaFact]
    public async Task Tab_SendCommandExists()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.NotNull(tab.SendCommand);
    }

    [AvaloniaFact]
    public async Task Tab_AbortCommandExists()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.NotNull(tab.AbortCommand);
    }

    [AvaloniaFact]
    public async Task Tab_ClearCommandExists()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.NotNull(tab.ClearCommand);
    }

    [AvaloniaFact]
    public async Task Tab_HasInputTextProperty()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        tab.InputText = "test";
        Assert.Equal("test", tab.InputText);
    }

    [AvaloniaFact]
    public async Task Tab_HasMessagesCollection()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.NotNull(tab.Messages);
    }

    [AvaloniaFact]
    public async Task Tab_ShowsSystemStartMessage()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.Single(tab.Messages);
        Assert.Equal(MessageRole.System, tab.Messages[0].Role);
        Assert.Contains("Session started", tab.Messages[0].Content);
    }

    [AvaloniaFact]
    public async Task ClearButton_ClearsMessages()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        Assert.NotEmpty(tab.Messages);

        tab.ClearCommand.Execute(null);

        Assert.Empty(tab.Messages);
    }

    [AvaloniaFact]
    public async Task SendCommand_AddsUserMessage()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var tab = vm.Tabs[0];
        tab.InputText = "Hello Copilot";
        tab.SendCommand.Execute(null);
        await Task.Delay(2000);

        Assert.True(tab.Messages.Count >= 2, $"Expected at least 2 messages, got {tab.Messages.Count}");
        Assert.Contains(tab.Messages, m => m.Role == MessageRole.User && m.Content == "Hello Copilot");
    }

    [AvaloniaFact]
    public async Task SessionTabView_RendersInTabControl()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        // Verify the tab control shows content
        var tabControl = window.FindControl<TabControl>("SessionTabControl");
        Assert.NotNull(tabControl);
        Assert.Equal(1, tabControl.ItemCount);
    }
}
