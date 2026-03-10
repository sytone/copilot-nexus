namespace CopilotFamily.Core.Tests;

using Xunit;

public class CopilotFamilyPathsTests
{
    private static readonly string ExpectedRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotFamily");

    [Fact]
    public void Root_IsUnderLocalAppData()
    {
        Assert.Equal(ExpectedRoot, CopilotFamilyPaths.Root);
    }

    [Fact]
    public void NexusInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "nexus"), CopilotFamilyPaths.NexusInstall);
    }

    [Fact]
    public void AppInstall_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app"), CopilotFamilyPaths.AppInstall);
    }

    [Fact]
    public void StagingRoot_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "staging"), CopilotFamilyPaths.StagingRoot);
    }

    [Fact]
    public void NexusStaging_IsUnderStagingRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "staging", "nexus"), CopilotFamilyPaths.NexusStaging);
    }

    [Fact]
    public void AppStaging_IsUnderStagingRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "staging", "app"), CopilotFamilyPaths.AppStaging);
    }

    [Fact]
    public void Logs_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "logs"), CopilotFamilyPaths.Logs);
    }

    [Fact]
    public void NexusLockFile_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "nexus.lock"), CopilotFamilyPaths.NexusLockFile);
    }

    [Fact]
    public void AppStateFile_IsUnderRoot()
    {
        Assert.Equal(Path.Combine(ExpectedRoot, "app-state.json"), CopilotFamilyPaths.AppStateFile);
    }

    [Fact]
    public void NexusExe_IsInNexusInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "nexus", "CopilotFamily.Nexus.exe"),
            CopilotFamilyPaths.NexusExe);
    }

    [Fact]
    public void AppExe_IsInAppInstall()
    {
        Assert.Equal(
            Path.Combine(ExpectedRoot, "app", "CopilotFamily.App.exe"),
            CopilotFamilyPaths.AppExe);
    }

    [Theory]
    [InlineData("nexus")]
    [InlineData("app")]
    public void GetStagingPath_ReturnsCorrectPath(string component)
    {
        var expected = Path.Combine(ExpectedRoot, "staging", component);
        Assert.Equal(expected, CopilotFamilyPaths.GetStagingPath(component));
    }

    [Theory]
    [InlineData("nexus")]
    [InlineData("app")]
    public void GetInstallPath_ReturnsCorrectPath(string component)
    {
        var expected = Path.Combine(ExpectedRoot, component);
        Assert.Equal(expected, CopilotFamilyPaths.GetInstallPath(component));
    }

    [Fact]
    public void GetStagingPath_ThrowsForUnknownComponent()
    {
        Assert.Throws<ArgumentException>(() => CopilotFamilyPaths.GetStagingPath("unknown"));
    }

    [Fact]
    public void GetInstallPath_ThrowsForUnknownComponent()
    {
        Assert.Throws<ArgumentException>(() => CopilotFamilyPaths.GetInstallPath("unknown"));
    }

    [Theory]
    [InlineData("NEXUS")]
    [InlineData("Nexus")]
    [InlineData("APP")]
    [InlineData("App")]
    public void GetPaths_AreCaseInsensitive(string component)
    {
        // Should not throw
        var staging = CopilotFamilyPaths.GetStagingPath(component);
        var install = CopilotFamilyPaths.GetInstallPath(component);

        Assert.NotNull(staging);
        Assert.NotNull(install);
    }

    [Fact]
    public void EnsureDirectories_CreatesAllDirectories()
    {
        // This actually creates directories on disk — it's safe because
        // they go to %LOCALAPPDATA% which is per-user
        CopilotFamilyPaths.EnsureDirectories();

        Assert.True(Directory.Exists(CopilotFamilyPaths.Root));
        Assert.True(Directory.Exists(CopilotFamilyPaths.NexusInstall));
        Assert.True(Directory.Exists(CopilotFamilyPaths.AppInstall));
        Assert.True(Directory.Exists(CopilotFamilyPaths.StagingRoot));
        Assert.True(Directory.Exists(CopilotFamilyPaths.NexusStaging));
        Assert.True(Directory.Exists(CopilotFamilyPaths.AppStaging));
        Assert.True(Directory.Exists(CopilotFamilyPaths.Logs));
    }

    [Fact]
    public void StagingPaths_AreNotInsideInstallPaths()
    {
        // Key requirement: staging is NOT inside install dirs
        Assert.DoesNotContain(CopilotFamilyPaths.NexusInstall, CopilotFamilyPaths.NexusStaging.AsSpan());
        Assert.DoesNotContain(CopilotFamilyPaths.AppInstall, CopilotFamilyPaths.AppStaging.AsSpan());

        // Staging IS under the shared staging root
        Assert.StartsWith(CopilotFamilyPaths.StagingRoot, CopilotFamilyPaths.NexusStaging);
        Assert.StartsWith(CopilotFamilyPaths.StagingRoot, CopilotFamilyPaths.AppStaging);
    }
}
