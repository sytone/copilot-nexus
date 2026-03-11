namespace CopilotNexus.Core.Tests;

using Xunit;

public class CopilotNexusPathsTests
{
    private static readonly string ExpectedRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotNexus");

    [Fact]
    public void Root_IsUnderLocalAppData()
    {
        Assert.Equal(ExpectedRoot, CopilotNexusPaths.Root);
    }

    [Fact]
    public void NexusInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "nexus"), CopilotNexusPaths.NexusInstall);
    }

    [Fact]
    public void CliInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "cli"), CopilotNexusPaths.CliInstall);
    }

    [Fact]
    public void AppInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app"), CopilotNexusPaths.AppInstall);
    }

    [Fact]
    public void StagingRoot_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "staging"), CopilotNexusPaths.StagingRoot);
    }

    [Fact]
    public void NexusStaging_IsUnderStagingRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "staging", "nexus"), CopilotNexusPaths.NexusStaging);
    }

    [Fact]
    public void AppStaging_IsUnderStagingRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "staging", "app"), CopilotNexusPaths.AppStaging);
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
    public void AppStateFile_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app-state.json"), CopilotNexusPaths.AppStateFile);
    }

    [Fact]
    public void CliExe_IsInCliInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "cli", "CopilotNexus.Cli.exe"),
            CopilotNexusPaths.CliExe);
    }

    [Fact]
    public void ServiceExe_IsInNexusInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "nexus", "CopilotNexus.Service.exe"),
            CopilotNexusPaths.ServiceExe);
    }

    [Fact]
    public void AppExe_IsInAppInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "app", "CopilotNexus.App.exe"),
            CopilotNexusPaths.AppExe);
    }

    [Theory]
    [InlineData("nexus")]
    [InlineData("app")]
    public void GetStagingPath_ReturnsCorrectPath(string component)
    {
        var expected = Path.Combine(ExpectedRoot, "staging", component);
        Assert.Equal(expected, CopilotNexusPaths.GetStagingPath(component));
    }

    [Theory]
    [InlineData("nexus")]
    [InlineData("app")]
    public void GetInstallPath_ReturnsCorrectPath(string component)
    {
        var expected = Path.Combine(ExpectedRoot, component);
        Assert.Equal(expected, CopilotNexusPaths.GetInstallPath(component));
    }

    [Fact]
    public void GetStagingPath_ThrowsForUnknownComponent()
    {
        Assert.Throws<ArgumentException>(() => CopilotNexusPaths.GetStagingPath("unknown"));
    }

    [Fact]
    public void GetInstallPath_ThrowsForUnknownComponent()
    {
        Assert.Throws<ArgumentException>(() => CopilotNexusPaths.GetInstallPath("unknown"));
    }

    [Theory]
    [InlineData("NEXUS")]
    [InlineData("Nexus")]
    [InlineData("APP")]
    [InlineData("App")]
    public void GetPaths_AreCaseInsensitive(string component)
    {
        // Should not throw
        var staging = CopilotNexusPaths.GetStagingPath(component);
        var install = CopilotNexusPaths.GetInstallPath(component);

        Assert.NotNull(staging);
        Assert.NotNull(install);
    }

    [Fact]
    public void EnsureDirectories_CreatesAllDirectories()
    {
        // This actually creates directories on disk — it's safe because
        // they go to %LOCALAPPDATA% which is per-user
        CopilotNexusPaths.EnsureDirectories();

        Assert.True(Directory.Exists(CopilotNexusPaths.Root));
        Assert.True(Directory.Exists(CopilotNexusPaths.NexusInstall));
        Assert.True(Directory.Exists(CopilotNexusPaths.CliInstall));
        Assert.True(Directory.Exists(CopilotNexusPaths.AppInstall));
        Assert.True(Directory.Exists(CopilotNexusPaths.StagingRoot));
        Assert.True(Directory.Exists(CopilotNexusPaths.NexusStaging));
        Assert.True(Directory.Exists(CopilotNexusPaths.AppStaging));
        Assert.True(Directory.Exists(CopilotNexusPaths.Logs));
    }

    [Fact]
    public void StagingPaths_AreNotInsideInstallPaths()
    {
        // Key requirement: staging is NOT inside install dirs
        Assert.DoesNotContain(CopilotNexusPaths.NexusInstall, CopilotNexusPaths.NexusStaging.AsSpan());
        Assert.DoesNotContain(CopilotNexusPaths.CliInstall, CopilotNexusPaths.NexusStaging.AsSpan());
        Assert.DoesNotContain(CopilotNexusPaths.AppInstall, CopilotNexusPaths.AppStaging.AsSpan());

        // Staging IS under the shared staging root
        Assert.StartsWith(CopilotNexusPaths.StagingRoot, CopilotNexusPaths.NexusStaging);
        Assert.StartsWith(CopilotNexusPaths.StagingRoot, CopilotNexusPaths.AppStaging);
    }
}
