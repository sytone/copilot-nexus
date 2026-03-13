namespace CopilotNexus.Core.Tests;

using Xunit;

public class CopilotNexusPathsTests
{
    private static readonly string ExpectedRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotNexus");

    private static readonly string ExpectedUserConfigRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot-nexus");

    [Fact]
    public void Root_IsUnderLocalAppData()
    {
        Assert.Equal(ExpectedRoot, CopilotNexusPaths.Root);
    }

    [Fact]
    public void NexusInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app", "service"), CopilotNexusPaths.NexusInstall);
    }

    [Fact]
    public void CliInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app", "cli"), CopilotNexusPaths.CliInstall);
    }

    [Fact]
    public void AppInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app", "winapp"), CopilotNexusPaths.AppInstall);
    }

    [Fact]
    public void AppRoot_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app"), CopilotNexusPaths.AppRoot);
    }

    [Fact]
    public void Logs_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "logs"), CopilotNexusPaths.Logs);
    }

    [Fact]
    public void NexusLockFile_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "nexus.lock"), CopilotNexusPaths.NexusLockFile);
    }

    [Fact]
    public void StateFiles_AreUnderStateRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "state", "session-state.json"), CopilotNexusPaths.NexusAppStateFile);
        Assert.Equal(Path.Combine(ExpectedRoot, "state", "session-profiles.json"), CopilotNexusPaths.NexusSessionProfilesFile);
        Assert.Equal(Path.Combine(ExpectedRoot, "state", "publish-version-state.json"), CopilotNexusPaths.PublishVersionStateFile);
    }

    [Fact]
    public void UserConfigRoot_IsUnderUserProfile()
    {
        Assert.Equal(ExpectedUserConfigRoot, CopilotNexusPaths.UserConfigRoot);
    }

    [Fact]
    public void AppStateFile_IsUnderUserConfigRoot()
    {
        Assert.Equal(Path.Combine(ExpectedUserConfigRoot, "session-state.json"), CopilotNexusPaths.AppStateFile);
    }

    [Fact]
    public void NexusSessionProfilesFile_IsUnderStateRoot()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "state", "session-profiles.json"),
            CopilotNexusPaths.NexusSessionProfilesFile);
    }

    [Fact]
    public void CliExe_IsInCliInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "app", "cli", "CopilotNexus.Cli.exe"),
            CopilotNexusPaths.CliExe);
    }

    [Fact]
    public void ServiceExe_IsInNexusInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "app", "service", "CopilotNexus.Service.exe"),
            CopilotNexusPaths.ServiceExe);
    }

    [Fact]
    public void AppExe_IsInAppInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "app", "winapp", "CopilotNexus.App.exe"),
            CopilotNexusPaths.AppExe);
    }

    [Theory]
    [InlineData("nexus")]
    [InlineData("cli")]
    [InlineData("app")]
    public void GetInstallPath_ReturnsCorrectPath(string component)
    {
        var expected = component switch
        {
            "nexus" => Path.Combine(ExpectedRoot, "app", "service"),
            "cli" => Path.Combine(ExpectedRoot, "app", "cli"),
            _ => Path.Combine(ExpectedRoot, "app", "winapp"),
        };
        Assert.Equal(expected, CopilotNexusPaths.GetInstallPath(component));
    }

    [Fact]
    public void GetInstallPath_ThrowsForUnknownComponent()
    {
        Assert.Throws<ArgumentException>(() => CopilotNexusPaths.GetInstallPath("unknown"));
    }

    [Theory]
    [InlineData("NEXUS")]
    [InlineData("Nexus")]
    [InlineData("CLI")]
    [InlineData("Cli")]
    [InlineData("APP")]
    [InlineData("App")]
    public void GetInstallPath_IsCaseInsensitive(string component)
    {
        var install = CopilotNexusPaths.GetInstallPath(component);
        Assert.NotNull(install);
    }

    [Fact]
    public void GetVersionedInstallPath_AppendsVersionFolder()
    {
        var version = "1.2.3-dev.20260101010101";
        Assert.Equal(
            Path.Combine(ExpectedRoot, "app", "service", version),
            CopilotNexusPaths.GetVersionedInstallPath("nexus", version));
    }

    [Fact]
    public void EnsureDirectories_CreatesExpectedDirectories()
    {
        CopilotNexusPaths.EnsureDirectories();
        Assert.True(Directory.Exists(CopilotNexusPaths.Root));
        Assert.True(Directory.Exists(CopilotNexusPaths.AppRoot));
        Assert.True(Directory.Exists(CopilotNexusPaths.NexusInstall));
        Assert.True(Directory.Exists(CopilotNexusPaths.CliInstall));
        Assert.True(Directory.Exists(CopilotNexusPaths.AppInstall));
        Assert.True(Directory.Exists(CopilotNexusPaths.Logs));
        Assert.True(Directory.Exists(CopilotNexusPaths.StateRoot));
        Assert.True(Directory.Exists(CopilotNexusPaths.UserConfigRoot));
    }
}
