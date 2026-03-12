namespace CopilotNexus.App.Tests.ViewModels;

using CopilotNexus.App.Tests.Helpers;
using CopilotNexus.App.ViewModels;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class MainWindowViewModelTests : IDisposable
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<ICopilotSessionWrapper> _mockSession;
    private readonly SynchronousUiDispatcher _dispatcher;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _mockSession = new Mock<ICopilotSessionWrapper>();
        _mockSession
            .Setup(s => s.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SessionOutputEventArgs>());
        _mockSessionManager = new Mock<ISessionManager>();
        _mockSessionManager.SetupGet(m => m.AvailableModels)
            .Returns(Array.Empty<ModelInfo>());
        _dispatcher = new SynchronousUiDispatcher();
        _viewModel = new MainWindowViewModel(_mockSessionManager.Object, _dispatcher, NullLogger<MainWindowViewModel>.Instance);
    }

    [Fact]
    public void Constructor_TabsIsEmpty()
    {
        Assert.Empty(_viewModel.Tabs);
    }

    [Fact]
    public void Constructor_SelectedTabIsNull()
    {
        Assert.Null(_viewModel.SelectedTab);
    }

    [Fact]
    public async Task InitializeAsync_CallsSessionManagerInitialize()
    {
        await _viewModel.InitializeAsync();

        _mockSessionManager.Verify(m => m.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateNewTabAsync_AddsTab()
    {
        SetupSessionCreation("Session 1");

        await _viewModel.CreateNewTabAsync();

        Assert.Single(_viewModel.Tabs);
    }

    [Fact]
    public async Task CreateNewTabAsync_SetsSelectedTab()
    {
        SetupSessionCreation("Session 1");

        await _viewModel.CreateNewTabAsync();

        Assert.NotNull(_viewModel.SelectedTab);
        Assert.Equal("Session 1", _viewModel.SelectedTab.Title);
    }

    [Fact]
    public async Task CreateNewTabAsync_IncrementsSessionNames()
    {
        SetupSessionCreation("Session 1");
        await _viewModel.CreateNewTabAsync();

        SetupSessionCreation("Session 2");
        await _viewModel.CreateNewTabAsync();

        Assert.Equal(2, _viewModel.Tabs.Count);
        Assert.Equal("Session 1", _viewModel.Tabs[0].Title);
        Assert.Equal("Session 2", _viewModel.Tabs[1].Title);
    }

    [Fact]
    public async Task CreateNewTabAsync_CallsCreateSessionOnManager()
    {
        SetupSessionCreation("Session 1");

        await _viewModel.CreateNewTabAsync();

        _mockSessionManager.Verify(
            m => m.CreateSessionAsync(
                "Session 1",
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CloseTabCommand_WhenTabSelected_RemovesTab()
    {
        SetupSessionCreation("Session 1");
        await _viewModel.CreateNewTabAsync();
        Assert.Single(_viewModel.Tabs);

        _viewModel.CloseTabCommand.Execute(null);
        await Task.Delay(100);

        Assert.Empty(_viewModel.Tabs);
    }

    [Fact]
    public async Task CloseTabCommand_WhenNoTabs_DoesNothing()
    {
        _viewModel.CloseTabCommand.Execute(null);
        await Task.Delay(50);

        Assert.Empty(_viewModel.Tabs);
    }

    [Fact]
    public async Task CloseTab_SelectsLastTab()
    {
        SetupSessionCreation("Session 1");
        await _viewModel.CreateNewTabAsync();
        var firstTab = _viewModel.Tabs[0];

        SetupSessionCreation("Session 2");
        await _viewModel.CreateNewTabAsync();

        _viewModel.SelectedTab = firstTab;
        _viewModel.CloseTabCommand.Execute(null);
        await Task.Delay(100);

        Assert.Single(_viewModel.Tabs);
        Assert.Equal("Session 2", _viewModel.SelectedTab?.Title);
    }

    [Fact]
    public async Task CloseTab_CallsRemoveSessionOnManager()
    {
        SetupSessionCreation("Session 1");
        await _viewModel.CreateNewTabAsync();

        _viewModel.CloseTabCommand.Execute(null);
        await Task.Delay(100);

        _mockSessionManager.Verify(
            m => m.RemoveSessionAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void SelectedTab_PropertyChanged_NotRaisedWhenSame()
    {
        var changedProps = new List<string>();
        _viewModel.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        _viewModel.SelectedTab = null; // already null

        Assert.DoesNotContain("SelectedTab", changedProps);
    }

    [Fact]
    public void ShowUpdateNotification_WithVersionTransition_SetsBannerText()
    {
        _viewModel.ShowUpdateNotification("1.0.0", "1.1.0");

        Assert.True(_viewModel.IsUpdateAvailable);
        Assert.Equal("Update available: 1.0.0 → 1.1.0.", _viewModel.UpdateNotificationText);
    }

    [Fact]
    public void ShowUpdateNotification_WithoutVersions_UsesDefaultText()
    {
        _viewModel.ShowUpdateNotification();

        Assert.True(_viewModel.IsUpdateAvailable);
        Assert.Equal("A new version is available.", _viewModel.UpdateNotificationText);
    }

    [Fact]
    public async Task RestoreStateAsync_WhenSessionResumed_LoadsHistoryMessages()
    {
        var resumedSession = SessionInfo.FromRemote(
            id: "tab-1",
            name: "Session 1",
            model: "gpt-4.1",
            sdkSessionId: "sdk-123");

        _mockSessionManager
            .Setup(m => m.ResumeSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumedSession);

        _mockSessionManager
            .Setup(m => m.GetSession("tab-1"))
            .Returns(_mockSession.Object);

        _mockSession
            .Setup(s => s.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionOutputEventArgs>
            {
                new("tab-1", "System context", MessageRole.System, OutputKind.Message),
                new("tab-1", "How does this work?", MessageRole.User, OutputKind.Message),
                new("tab-1", "Here is the explanation.", MessageRole.Assistant, OutputKind.Message),
            });

        var state = new AppState
        {
            SessionCounter = 1,
            SelectedTabIndex = 0,
            Tabs =
            {
                new TabState
                {
                    Name = "Session 1",
                    SdkSessionId = "sdk-123",
                    Model = "gpt-4.1",
                    IsAutopilot = true,
                },
            },
        };

        await _viewModel.RestoreStateAsync(state);

        var tab = Assert.Single(_viewModel.Tabs);
        Assert.Equal(4, tab.Messages.Count);
        Assert.Equal("System context", tab.Messages[0].Content);
        Assert.Equal("How does this work?", tab.Messages[1].Content);
        Assert.Equal("Here is the explanation.", tab.Messages[2].Content);
        Assert.Equal(MessageRole.System, tab.Messages[3].Role);
        Assert.Equal("Session resumed", tab.Messages[3].Content);
    }

    [Fact]
    public async Task RestoreStateAsync_LoadsPersistedNexusSystemMessages()
    {
        var resumedSession = SessionInfo.FromRemote(
            id: "tab-1",
            name: "Session 1",
            model: "gpt-4.1",
            sdkSessionId: "sdk-123");

        _mockSessionManager
            .Setup(m => m.ResumeSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumedSession);

        _mockSessionManager
            .Setup(m => m.GetSession("tab-1"))
            .Returns(_mockSession.Object);

        _mockSession
            .Setup(s => s.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SessionOutputEventArgs("tab-1", "Assistant history", MessageRole.Assistant, OutputKind.Message),
            });

        var state = new AppState
        {
            SessionCounter = 1,
            SelectedTabIndex = 0,
            Tabs =
            {
                new TabState
                {
                    Name = "Session 1",
                    SdkSessionId = "sdk-123",
                    Model = "gpt-4.1",
                    IsAutopilot = true,
                    NexusSystemMessages =
                    {
                        new MessageState
                        {
                            Role = MessageRole.System,
                            Content = "Nexus-side note",
                            Timestamp = DateTime.UtcNow.AddMinutes(-5),
                        },
                    },
                },
            },
        };

        await _viewModel.RestoreStateAsync(state);

        var tab = Assert.Single(_viewModel.Tabs);
        Assert.Equal(3, tab.Messages.Count);
        Assert.Equal("Assistant history", tab.Messages[0].Content);
        Assert.Equal("Nexus-side note", tab.Messages[1].Content);
        Assert.True(tab.Messages[1].IsNexusSystemMessage);
        Assert.Equal("Session resumed", tab.Messages[2].Content);
        Assert.True(tab.Messages[2].IsNexusSystemMessage);
    }

    [Fact]
    public async Task CaptureState_PersistsNexusSystemMessages()
    {
        SetupSessionCreation("Session 1");
        await _viewModel.CreateNewTabAsync();
        var tab = Assert.Single(_viewModel.Tabs);
        tab.AppendSystemMessage("Nexus runtime message");

        var state = _viewModel.CaptureState();

        var tabState = Assert.Single(state.Tabs);
        Assert.Contains(tabState.NexusSystemMessages, m => m.Content == "Nexus runtime message");
    }

    private void SetupSessionCreation(string name)
    {
        var sessionInfo = new SessionInfo(name);

        _mockSessionManager
            .Setup(m => m.CreateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionInfo);

        _mockSessionManager
            .Setup(m => m.GetSession(sessionInfo.Id))
            .Returns(_mockSession.Object);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
    }
}
