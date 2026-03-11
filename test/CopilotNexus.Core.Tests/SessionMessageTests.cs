namespace CopilotFamily.Core.Tests;

using CopilotFamily.Core.Models;
using Xunit;

public class SessionMessageTests
{
    [Fact]
    public void Constructor_SetsRoleAndContent()
    {
        var message = new SessionMessage(MessageRole.User, "Hello");

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("Hello", message.Content);
    }

    [Fact]
    public void Constructor_SetsTimestamp()
    {
        var before = DateTime.UtcNow;
        var message = new SessionMessage(MessageRole.Assistant, "Hi");
        var after = DateTime.UtcNow;

        Assert.InRange(message.Timestamp, before, after);
    }

    [Theory]
    [InlineData(MessageRole.User)]
    [InlineData(MessageRole.Assistant)]
    [InlineData(MessageRole.System)]
    public void Constructor_AcceptsAllRoles(MessageRole role)
    {
        var message = new SessionMessage(role, "test");
        Assert.Equal(role, message.Role);
    }

    [Fact]
    public void Constructor_EmptyContent_IsAllowed()
    {
        var message = new SessionMessage(MessageRole.System, string.Empty);
        Assert.Equal(string.Empty, message.Content);
    }

    [Fact]
    public void Constructor_DefaultIsNotStreaming()
    {
        var message = new SessionMessage(MessageRole.Assistant, "test");
        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void Constructor_CanBeCreatedAsStreaming()
    {
        var message = new SessionMessage(MessageRole.Assistant, "partial", isStreaming: true);
        Assert.True(message.IsStreaming);
    }

    [Fact]
    public void AppendContent_AppendsToExistingContent()
    {
        var message = new SessionMessage(MessageRole.Assistant, "Hello", isStreaming: true);
        message.AppendContent(" World");

        Assert.Equal("Hello World", message.Content);
    }

    [Fact]
    public void AppendContent_RaisesPropertyChanged()
    {
        var message = new SessionMessage(MessageRole.Assistant, "", isStreaming: true);
        var changedProps = new List<string>();
        message.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        message.AppendContent("chunk");

        Assert.Contains("Content", changedProps);
    }

    [Fact]
    public void CompleteStreaming_SetsIsStreamingFalse()
    {
        var message = new SessionMessage(MessageRole.Assistant, "data", isStreaming: true);
        message.CompleteStreaming();

        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void CompleteStreaming_RaisesPropertyChanged()
    {
        var message = new SessionMessage(MessageRole.Assistant, "", isStreaming: true);
        var changedProps = new List<string>();
        message.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        message.CompleteStreaming();

        Assert.Contains("IsStreaming", changedProps);
    }
}
