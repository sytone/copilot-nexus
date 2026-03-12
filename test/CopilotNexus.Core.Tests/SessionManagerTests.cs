namespace CopilotNexus.Core.Tests;

using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using CopilotNexus.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class SessionManagerTests
{
    private readonly Mock<ICopilotClientService> _mockClientService;
    private readonly Mock<ICopilotSessionWrapper> _mockSession;
    private readonly SessionManager _manager;

    public SessionManagerTests()
    {
        _mockClientService = new Mock<ICopilotClientService>();
        _mockSession = new Mock<ICopilotSessionWrapper>();
        _mockSession.SetupGet(s => s.SessionId).Returns("test-sdk-session-id");

        _mockClientService
            .Setup(c => c.CreateSessionAsync(
                It.IsAny<string?>(),
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockSession.Object);

        _mockClientService
            .Setup(c => c.ResumeSessionAsync(
                It.IsAny<string>(),
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockSession.Object);

        _mockClientService
            .Setup(c => c.ListModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ModelInfo>());

        _manager = new SessionManager(_mockClientService.Object, NullLogger<SessionManager>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_StartsClient()
    {
        await _manager.InitializeAsync();

        _mockClientService.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSessionAsync_AddsSessionToList()
    {
        var info = await _manager.CreateSessionAsync("Test");

        Assert.Single(_manager.Sessions);
        Assert.Equal("Test", info.Name);
    }

    [Fact]
    public async Task CreateSessionAsync_SetsStateToRunning()
    {
        var info = await _manager.CreateSessionAsync("Test");

        Assert.Equal(SessionState.Running, info.State);
    }

    [Fact]
    public async Task CreateSessionAsync_RaisesSessionAddedEvent()
    {
        SessionInfo? addedSession = null;
        _manager.SessionAdded += (_, info) => addedSession = info;

        var info = await _manager.CreateSessionAsync("Test");

        Assert.NotNull(addedSession);
        Assert.Equal(info.Id, addedSession.Id);
    }

    [Fact]
    public async Task CreateSessionAsync_StoresSdkSessionId()
    {
        var info = await _manager.CreateSessionAsync("Test", new SessionConfiguration { Model = "gpt-4.1" });

        Assert.Equal("test-sdk-session-id", info.SdkSessionId);
    }

    [Fact]
    public async Task CreateSessionAsync_UsesResolvedDefaultModel_WhenNotSpecified()
    {
        var info = await _manager.CreateSessionAsync("Test");

        Assert.Equal("gpt-4.1", info.Model);
        _mockClientService.Verify(c => c.CreateSessionAsync(
                It.IsAny<string?>(),
                It.Is<SessionConfiguration?>(cfg => cfg != null && cfg.Model == "gpt-4.1"),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResumeSessionAsync_ResumesExistingSession()
    {
        var info = await _manager.ResumeSessionAsync("Test", "my-sdk-id");

        Assert.Single(_manager.Sessions);
        Assert.Equal("Test", info.Name);
        Assert.Equal(SessionState.Running, info.State);
        _mockClientService.Verify(
            c => c.ResumeSessionAsync(
                "my-sdk-id",
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconfigureSessionAsync_PreservesSessionId()
    {
        var info = await _manager.CreateSessionAsync("Test", new SessionConfiguration { Model = "gpt-4.1" });

        var updated = await _manager.ReconfigureSessionAsync(
            info.Id,
            new SessionConfiguration { Model = "claude-opus-4.6", IsAutopilot = true });

        Assert.Equal(info.Id, updated.Id);
        Assert.Equal("claude-opus-4.6", updated.Model);
        Assert.NotNull(_manager.GetSession(info.Id));
        Assert.Single(_manager.Sessions);
    }

    [Fact]
    public async Task RenameSessionAsync_UpdatesName()
    {
        var info = await _manager.CreateSessionAsync("Old Name");

        var updated = await _manager.RenameSessionAsync(info.Id, "New Name");

        Assert.Equal("New Name", updated.Name);
        Assert.Equal("New Name", _manager.Sessions.Single(s => s.Id == info.Id).Name);
    }

    [Fact]
    public async Task SendInputAsync_CallsSessionSend()
    {
        var info = await _manager.CreateSessionAsync("Test");

        await _manager.SendInputAsync(info.Id, "hello");

        _mockSession.Verify(
            s => s.SendAsync("hello", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendInputAsync_ThrowsForUnknownSession()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _manager.SendInputAsync("nonexistent", "hello"));
    }

    [Fact]
    public async Task RemoveSessionAsync_RemovesAndDisposesSession()
    {
        var info = await _manager.CreateSessionAsync("Test");

        await _manager.RemoveSessionAsync(info.Id);

        Assert.Empty(_manager.Sessions);
        _mockSession.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RemoveSessionAsync_DeletesFromDisk_WhenRequested()
    {
        var info = await _manager.CreateSessionAsync("Test");

        await _manager.RemoveSessionAsync(info.Id, deleteFromDisk: true);

        Assert.Empty(_manager.Sessions);
        _mockClientService.Verify(
            c => c.DeleteSessionAsync("test-sdk-session-id", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveSessionAsync_DoesNotDeleteFromDisk_ByDefault()
    {
        var info = await _manager.CreateSessionAsync("Test");

        await _manager.RemoveSessionAsync(info.Id);

        _mockClientService.Verify(
            c => c.DeleteSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveSessionAsync_RaisesSessionRemovedEvent()
    {
        SessionInfo? removedSession = null;
        _manager.SessionRemoved += (_, info) => removedSession = info;

        var info = await _manager.CreateSessionAsync("Test");
        await _manager.RemoveSessionAsync(info.Id);

        Assert.NotNull(removedSession);
        Assert.Equal(info.Id, removedSession.Id);
    }

    [Fact]
    public async Task GetSession_ReturnsWrapperForValidSession()
    {
        var info = await _manager.CreateSessionAsync("Test");

        var session = _manager.GetSession(info.Id);

        Assert.NotNull(session);
    }

    [Fact]
    public void GetSession_ReturnsNullForUnknownSession()
    {
        var session = _manager.GetSession("nonexistent");

        Assert.Null(session);
    }

    [Fact]
    public async Task CreateMultipleSessions_TracksAll()
    {
        await _manager.CreateSessionAsync("Session 1");
        await _manager.CreateSessionAsync("Session 2");
        await _manager.CreateSessionAsync("Session 3");

        Assert.Equal(3, _manager.Sessions.Count);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllSessions()
    {
        var mock1 = new Mock<ICopilotSessionWrapper>();
        mock1.SetupGet(s => s.SessionId).Returns("sdk-1");
        var mock2 = new Mock<ICopilotSessionWrapper>();
        mock2.SetupGet(s => s.SessionId).Returns("sdk-2");
        var mocks = new Queue<Mock<ICopilotSessionWrapper>>(new[] { mock1, mock2 });

        _mockClientService
            .Setup(c => c.CreateSessionAsync(
                It.IsAny<string?>(),
                It.IsAny<SessionConfiguration?>(),
                It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
                It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => mocks.Dequeue().Object);

        await _manager.CreateSessionAsync("Session 1");
        await _manager.CreateSessionAsync("Session 2");

        await _manager.DisposeAsync();

        mock1.Verify(s => s.DisposeAsync(), Times.Once);
        mock2.Verify(s => s.DisposeAsync(), Times.Once);
    }
}
