namespace CopilotNexus.Core.Tests;

using CopilotNexus.Core.Models;
using Xunit;

public class SessionInfoTests
{
    [Fact]
    public void Constructor_GeneratesUniqueId()
    {
        var info1 = new SessionInfo("Session 1");
        var info2 = new SessionInfo("Session 2");

        Assert.NotEqual(info1.Id, info2.Id);
    }

    [Fact]
    public void Constructor_SetsName()
    {
        var info = new SessionInfo("Test Session", "gpt-4.1");

        Assert.Equal("Test Session", info.Name);
        Assert.Equal("gpt-4.1", info.Model);
    }

    [Fact]
    public void Constructor_DefaultStateIsNotStarted()
    {
        var info = new SessionInfo("Test");
        Assert.Equal(SessionState.NotStarted, info.State);
    }

    [Fact]
    public void Constructor_SetsCreatedAt()
    {
        var before = DateTime.UtcNow;
        var info = new SessionInfo("Test");
        var after = DateTime.UtcNow;

        Assert.InRange(info.CreatedAt, before, after);
    }

    [Fact]
    public void Id_IsEightCharacters()
    {
        var info = new SessionInfo("Test");
        Assert.Equal(8, info.Id.Length);
    }

    [Fact]
    public void Name_CanBeUpdated()
    {
        var info = new SessionInfo("Original");
        info.Name = "Updated";
        Assert.Equal("Updated", info.Name);
    }

    [Fact]
    public void State_CanBeUpdated()
    {
        var info = new SessionInfo("Test");
        info.State = SessionState.Running;
        Assert.Equal(SessionState.Running, info.State);
    }

    [Fact]
    public void Model_DefaultsToNull()
    {
        var info = new SessionInfo("Test");
        Assert.Null(info.Model);
    }
}
