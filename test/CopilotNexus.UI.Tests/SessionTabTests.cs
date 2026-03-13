namespace CopilotNexus.UI.Tests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CopilotNexus.App;
using CopilotNexus.App.ViewModels;
using CopilotNexus.App.Views;
using CopilotNexus.Core.Models;
using Xunit;

/// <summary>
/// Headless UI tests for session tab interactions.
/// Uses ViewModel-level assertions (proper MVVM testing) rather than
/// control name lookup across name scopes.
/// </summary>
public class SessionTabTests
{
    private const double BottomTolerance = 8.0;

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

    private static SessionTabView GetSessionView(MainWindow window)
    {
        var sessionView = window.GetVisualDescendants().OfType<SessionTabView>().FirstOrDefault();
        Assert.NotNull(sessionView);
        return sessionView;
    }

    private static ScrollViewer GetMessagesScrollViewer(SessionTabView sessionView)
    {
        var listBox = sessionView.FindControl<ListBox>("MessagesList");
        Assert.NotNull(listBox);

        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scrollViewer);
        return scrollViewer;
    }

    private static bool IsNearBottom(ScrollViewer scrollViewer)
    {
        var maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        return maxOffsetY - scrollViewer.Offset.Y <= BottomTolerance;
    }

    private static async Task EnsureMessagesAreScrollableAsync(SessionTabViewModel tab, ScrollViewer scrollViewer)
    {
        for (var i = 0; i < 160 && scrollViewer.Extent.Height <= scrollViewer.Viewport.Height; i++)
        {
            tab.Messages.Add(new SessionMessage(MessageRole.Assistant, $"history-{i} {new string('x', 80)}"));
            await Task.Delay(10);
        }

        Assert.True(
            scrollViewer.Extent.Height > scrollViewer.Viewport.Height,
            "Expected message list to be scrollable for auto-scroll behavior tests.");
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

    [AvaloniaFact]
    public async Task SessionTabView_UsesBottomInputLayout()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var sessionView = window.GetVisualDescendants().OfType<SessionTabView>().FirstOrDefault();
        Assert.NotNull(sessionView);

        var layoutGrid = sessionView.Content as Grid;
        Assert.NotNull(layoutGrid);
        Assert.Equal(4, layoutGrid.RowDefinitions.Count);
        Assert.True(layoutGrid.RowDefinitions[1].Height.IsStar);

        var inputContainer = sessionView.FindControl<Border>("InputContainer");
        Assert.NotNull(inputContainer);
        Assert.Equal(2, Grid.GetRow(inputContainer));

        var contextBar = sessionView.FindControl<Border>("SessionContextBar");
        Assert.NotNull(contextBar);
        Assert.Equal(3, Grid.GetRow(contextBar));
    }

    [AvaloniaFact]
    public async Task SessionTabView_HasModeAndModelFooterControls()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var sessionView = GetSessionView(window);

        var modeSelector = sessionView.FindControl<ComboBox>("ModeSelector");
        var modelSelector = sessionView.FindControl<ComboBox>("FooterModelSelector");

        Assert.NotNull(modeSelector);
        Assert.NotNull(modelSelector);
    }

    [AvaloniaFact]
    public async Task SessionTabView_DefaultsToLatestMessage()
    {
        var (window, vm) = await CreateWindowWithTabAsync();
        var tab = vm.Tabs[0];
        var sessionView = GetSessionView(window);
        var scrollViewer = GetMessagesScrollViewer(sessionView);

        await EnsureMessagesAreScrollableAsync(tab, scrollViewer);
        await Task.Delay(200);

        Assert.True(IsNearBottom(scrollViewer), "Expected auto-scroll to keep latest message visible.");
    }

    [AvaloniaFact]
    public async Task SessionTabView_UserScrollUpWhileActive_PausesAutoScroll()
    {
        var (window, vm) = await CreateWindowWithTabAsync();
        var tab = vm.Tabs[0];
        var sessionView = GetSessionView(window);
        var scrollViewer = GetMessagesScrollViewer(sessionView);

        await EnsureMessagesAreScrollableAsync(tab, scrollViewer);
        tab.IsProcessing = true;

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
        await Task.Delay(150);

        tab.Messages.Add(new SessionMessage(MessageRole.Assistant, "stream update after manual scroll"));
        await Task.Delay(200);

        Assert.False(IsNearBottom(scrollViewer), "Expected manual scroll-up to pause forced auto-scroll while active.");
    }

    [AvaloniaFact]
    public async Task SessionTabView_Inactivity_ResumesAutoScroll()
    {
        var originalDelay = SessionTabView.AutoScrollResumeDelay;
        SessionTabView.AutoScrollResumeDelay = TimeSpan.FromMilliseconds(300);
        try
        {
            var (window, vm) = await CreateWindowWithTabAsync();
            var tab = vm.Tabs[0];
            var sessionView = GetSessionView(window);
            var scrollViewer = GetMessagesScrollViewer(sessionView);

            await EnsureMessagesAreScrollableAsync(tab, scrollViewer);
            tab.IsProcessing = true;

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
            await Task.Delay(150);

            tab.Messages.Add(new SessionMessage(MessageRole.Assistant, "first streaming update"));
            await Task.Delay(100);
            Assert.False(IsNearBottom(scrollViewer), "Expected auto-scroll to remain paused immediately after manual scroll.");

            await Task.Delay(500);
            tab.Messages.Add(new SessionMessage(MessageRole.Assistant, "update after inactivity timeout"));
            await Task.Delay(250);

            Assert.True(IsNearBottom(scrollViewer), "Expected auto-scroll to resume after inactivity timeout.");
        }
        finally
        {
            SessionTabView.AutoScrollResumeDelay = originalDelay;
        }
    }

    [AvaloniaFact]
    public async Task SessionMessages_HaveCopyContextMenu()
    {
        var (window, vm) = await CreateWindowWithTabAsync();

        var sessionView = GetSessionView(window);

        var messageBorder = sessionView.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.ContextMenu != null);
        Assert.NotNull(messageBorder);
        Assert.NotNull(messageBorder.ContextMenu);

        var copyMenuItem = messageBorder.ContextMenu!.Items
            .OfType<MenuItem>()
            .FirstOrDefault(menuItem => string.Equals(menuItem.Header?.ToString(), "Copy", StringComparison.Ordinal));
        Assert.NotNull(copyMenuItem);
    }
}
