namespace CopilotNexus.Core.Tests.Integration;

using System.Diagnostics;
using CopilotNexus.Updater;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests for the updater service. Tests run against the real file system
/// but do NOT spawn any external processes (PowerShell or otherwise).
/// </summary>
public class UpdaterServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testRoot;
    private readonly string _installDir;
    private readonly string _stagingDir;
    private readonly List<string> _logMessages = [];

    public UpdaterServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _testRoot = Path.Combine(Path.GetTempPath(), $"CopilotNexusUpdaterTest_{Guid.NewGuid():N}");
        _installDir = Path.Combine(_testRoot, "app");
        _stagingDir = Path.Combine(_testRoot, "staging", "app");
        Directory.CreateDirectory(_installDir);
        Directory.CreateDirectory(_stagingDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, true); } catch { }
    }

    private UpdaterService CreateService() => new(msg =>
    {
        _logMessages.Add(msg);
        _output.WriteLine(msg);
    });

    // ──────────────────────────────────────────────
    // CopyDirectory tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CopyStagingAsync_CopiesFilesToInstallDir()
    {
        await CreateFakeFile(Path.Combine(_installDir, "app.exe"), "v1");
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "v2");
        await CreateFakeFile(Path.Combine(_stagingDir, "newlib.dll"), "v2-new");

        var svc = CreateService();
        var result = await svc.CopyStagingAsync(_stagingDir, _installDir, CancellationToken.None);

        Assert.True(result);
        Assert.Equal("v2", await File.ReadAllTextAsync(Path.Combine(_installDir, "app.exe")));
        Assert.Equal("v2-new", await File.ReadAllTextAsync(Path.Combine(_installDir, "newlib.dll")));
    }

    [Fact]
    public async Task CopyStagingAsync_HandlesSubdirectories()
    {
        var subDir = Path.Combine(_stagingDir, "runtimes", "win-x64");
        Directory.CreateDirectory(subDir);
        await CreateFakeFile(Path.Combine(subDir, "native.dll"), "v2-native");

        var svc = CreateService();
        var result = await svc.CopyStagingAsync(_stagingDir, _installDir, CancellationToken.None);

        Assert.True(result);
        var destFile = Path.Combine(_installDir, "runtimes", "win-x64", "native.dll");
        Assert.True(File.Exists(destFile));
        Assert.Equal("v2-native", await File.ReadAllTextAsync(destFile));
    }

    [Fact]
    public async Task CopyStagingAsync_SkipsWhenStagingEmpty()
    {
        var svc = CreateService();
        var result = await svc.CopyStagingAsync(_stagingDir, _installDir, CancellationToken.None);

        Assert.True(result);
        Assert.Contains(_logMessages, m => m.Contains("empty"));
    }

    [Fact]
    public async Task CopyStagingAsync_SkipsWhenStagingDoesNotExist()
    {
        var svc = CreateService();
        var result = await svc.CopyStagingAsync(
            Path.Combine(_testRoot, "nonexistent"), _installDir, CancellationToken.None);

        Assert.True(result);
        Assert.Contains(_logMessages, m => m.Contains("does not exist"));
    }

    // ──────────────────────────────────────────────
    // ClearStaging tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ClearStaging_RemovesAllFiles()
    {
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "v2");
        await CreateFakeFile(Path.Combine(_stagingDir, "lib.dll"), "v2-lib");

        var svc = CreateService();
        svc.ClearStaging(_stagingDir);

        var remaining = Directory.GetFiles(_stagingDir, "*", SearchOption.AllDirectories);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task ClearStaging_RemovesSubdirectories()
    {
        var subDir = Path.Combine(_stagingDir, "sub", "deep");
        Directory.CreateDirectory(subDir);
        await CreateFakeFile(Path.Combine(subDir, "file.txt"), "data");

        var svc = CreateService();
        svc.ClearStaging(_stagingDir);

        var remaining = Directory.GetFiles(_stagingDir, "*", SearchOption.AllDirectories);
        Assert.Empty(remaining);
    }

    [Fact]
    public void ClearStaging_NoOpForNonexistentDirectory()
    {
        var svc = CreateService();
        svc.ClearStaging(Path.Combine(_testRoot, "nonexistent"));
        // Should not throw
    }

    // ──────────────────────────────────────────────
    // WaitForProcessExit tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task WaitForProcessExitAsync_ReturnsTrueWhenProcessAlreadyGone()
    {
        // Use a PID that almost certainly doesn't exist
        var svc = CreateService();
        var result = await svc.WaitForProcessExitAsync(99999, 2, CancellationToken.None);

        Assert.True(result);
        Assert.Contains(_logMessages, m => m.Contains("not found"));
    }

    [Fact]
    public async Task WaitForProcessExitAsync_ReturnsFalseOnTimeout()
    {
        // Use current process PID — it won't exit during the test
        var svc = CreateService();
        var result = await svc.WaitForProcessExitAsync(
            Environment.ProcessId, 1, CancellationToken.None);

        Assert.False(result);
    }

    // ──────────────────────────────────────────────
    // End-to-end RunAsync tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_FullCycle_CopiesAndClears()
    {
        // Setup: v1 installed, v2 staged
        await CreateFakeFile(Path.Combine(_installDir, "app.exe"), "v1-exe");
        await CreateFakeFile(Path.Combine(_installDir, "lib.dll"), "v1-lib");
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "v2-exe");
        await CreateFakeFile(Path.Combine(_stagingDir, "lib.dll"), "v2-lib");
        await CreateFakeFile(Path.Combine(_stagingDir, "newfile.txt"), "v2-new");

        var svc = CreateService();
        var options = new UpdaterOptions
        {
            AppPid = 99999, // non-existent — immediately passes wait
            InstallPath = _installDir,
            StagingPath = _stagingDir,
            AppExe = "", // skip relaunch in test
            WaitTimeoutSeconds = 2,
        };

        var exitCode = await svc.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("v2-exe", await File.ReadAllTextAsync(Path.Combine(_installDir, "app.exe")));
        Assert.Equal("v2-lib", await File.ReadAllTextAsync(Path.Combine(_installDir, "lib.dll")));
        Assert.Equal("v2-new", await File.ReadAllTextAsync(Path.Combine(_installDir, "newfile.txt")));

        var stagingFiles = Directory.GetFiles(_stagingDir, "*", SearchOption.AllDirectories);
        Assert.Empty(stagingFiles);

        Assert.Contains(_logMessages, m => m.Contains("Updater complete"));
    }

    [Fact]
    public async Task RunAsync_ReturnsOne_WhenProcessDoesNotExit()
    {
        var svc = CreateService();
        var options = new UpdaterOptions
        {
            AppPid = Environment.ProcessId, // current process — won't exit
            InstallPath = _installDir,
            StagingPath = _stagingDir,
            AppExe = "",
            WaitTimeoutSeconds = 1, // fast timeout
        };

        var exitCode = await svc.RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Contains(_logMessages, m => m.Contains("did not exit"));
    }

    [Fact]
    public async Task RunAsync_SkipsRelaunch_WhenAppExeIsEmpty()
    {
        await CreateFakeFile(Path.Combine(_stagingDir, "file.txt"), "data");

        var svc = CreateService();
        var options = new UpdaterOptions
        {
            AppPid = 99999,
            InstallPath = _installDir,
            StagingPath = _stagingDir,
            AppExe = "",
            WaitTimeoutSeconds = 2,
        };

        var exitCode = await svc.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(_logMessages, m => m.Contains("Launching"));
    }

    // ──────────────────────────────────────────────
    // CopyDirectory static method tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CopyDirectory_OverwritesExistingFiles()
    {
        await CreateFakeFile(Path.Combine(_installDir, "app.exe"), "old");
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "new");

        UpdaterService.CopyDirectory(_stagingDir, _installDir);

        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(_installDir, "app.exe")));
    }

    [Fact]
    public async Task CopyDirectory_CreatesNestedDirectories()
    {
        var nested = Path.Combine(_stagingDir, "a", "b", "c");
        Directory.CreateDirectory(nested);
        await CreateFakeFile(Path.Combine(nested, "deep.txt"), "deep-content");

        UpdaterService.CopyDirectory(_stagingDir, _installDir);

        var dest = Path.Combine(_installDir, "a", "b", "c", "deep.txt");
        Assert.True(File.Exists(dest));
        Assert.Equal("deep-content", await File.ReadAllTextAsync(dest));
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static async Task CreateFakeFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }
}
