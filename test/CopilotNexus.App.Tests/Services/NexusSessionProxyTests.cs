namespace CopilotNexus.App.Tests.Services;

using CopilotNexus.App.Services;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Models;
using Xunit;

public class NexusSessionProxyTests
{
    [Fact]
    public void Constructor_SetsSessionId()
    {
        var proxy = new NexusSessionProxy("session-1");

        Assert.Equal("session-1", proxy.SessionId);
    }

    [Fact]
    public void IsActive_DefaultsToTrue()
    {
        var proxy = new NexusSessionProxy("s1");

        Assert.True(proxy.IsActive);
    }

    [Fact]
    public async Task SendAsync_InvokesSendFunc_WhenTransportSet()
    {
        var proxy = new NexusSessionProxy("s1");
        string? capturedId = null;
        string? capturedInput = null;

        proxy.SetTransport(
            (sid, input) => { capturedId = sid; capturedInput = input; return Task.CompletedTask; },
            _ => Task.CompletedTask);

        await proxy.SendAsync("hello world");

        Assert.Equal("s1", capturedId);
        Assert.Equal("hello world", capturedInput);
    }

    [Fact]
    public async Task SendAsync_DoesNotThrow_WhenNoTransportSet()
    {
        var proxy = new NexusSessionProxy("s1");

        await proxy.SendAsync("test");
    }

    [Fact]
    public async Task AbortAsync_InvokesAbortFunc_WhenTransportSet()
    {
        var proxy = new NexusSessionProxy("s1");
        string? capturedId = null;

        proxy.SetTransport(
            (_, _) => Task.CompletedTask,
            sid => { capturedId = sid; return Task.CompletedTask; });

        await proxy.AbortAsync();

        Assert.Equal("s1", capturedId);
    }

    [Fact]
    public async Task AbortAsync_DoesNotThrow_WhenNoTransportSet()
    {
        var proxy = new NexusSessionProxy("s1");

        await proxy.AbortAsync();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsHistory_WhenTransportSet()
    {
        var proxy = new NexusSessionProxy("s1");
        proxy.SetTransport(
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            (sid, _) => Task.FromResult<IReadOnlyList<SessionOutputEventArgs>>(
                new[]
                {
                    new SessionOutputEventArgs(sid, "prior message", MessageRole.Assistant, OutputKind.Message),
                }));

        var history = await proxy.GetHistoryAsync();

        Assert.Single(history);
        Assert.Equal("prior message", history[0].Content);
    }

    [Fact]
    public async Task GetHistoryAsync_WithoutTransport_ReturnsEmpty()
    {
        var proxy = new NexusSessionProxy("s1");

        var history = await proxy.GetHistoryAsync();

        Assert.Empty(history);
    }

    [Fact]
    public void RaiseOutput_FiresOutputReceivedEvent()
    {
        var proxy = new NexusSessionProxy("s1");
        SessionOutputEventArgs? received = null;

        proxy.OutputReceived += (_, e) => received = e;

        var args = new SessionOutputEventArgs("s1", "test content", MessageRole.Assistant, OutputKind.Message);
        proxy.RaiseOutput(args);

        Assert.NotNull(received);
        Assert.Equal("test content", received!.Content);
        Assert.Equal(MessageRole.Assistant, received.Role);
        Assert.Equal(OutputKind.Message, received.Kind);
    }

    [Fact]
    public void RaiseOutput_NoSubscribers_DoesNotThrow()
    {
        var proxy = new NexusSessionProxy("s1");
        var args = new SessionOutputEventArgs("s1", "content", MessageRole.Assistant, OutputKind.Delta);

        proxy.RaiseOutput(args);
    }

    [Fact]
    public void RaiseOutput_MultipleSubscribers_AllReceiveEvent()
    {
        var proxy = new NexusSessionProxy("s1");
        int callCount = 0;

        proxy.OutputReceived += (_, _) => callCount++;
        proxy.OutputReceived += (_, _) => callCount++;

        proxy.RaiseOutput(new SessionOutputEventArgs("s1", "msg", MessageRole.User, OutputKind.Message));

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task DisposeAsync_SetsIsActiveToFalse()
    {
        var proxy = new NexusSessionProxy("s1");

        Assert.True(proxy.IsActive);

        await proxy.DisposeAsync();

        Assert.False(proxy.IsActive);
    }

    [Fact]
    public async Task SetTransport_OverridesPreviousTransport()
    {
        var proxy = new NexusSessionProxy("s1");
        int firstCallCount = 0;
        int secondCallCount = 0;

        proxy.SetTransport(
            (_, _) => { firstCallCount++; return Task.CompletedTask; },
            _ => Task.CompletedTask);

        proxy.SetTransport(
            (_, _) => { secondCallCount++; return Task.CompletedTask; },
            _ => Task.CompletedTask);

        await proxy.SendAsync("test");

        Assert.Equal(0, firstCallCount);
        Assert.Equal(1, secondCallCount);
    }
}
