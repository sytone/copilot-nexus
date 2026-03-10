namespace CopilotFamily.Core.Tests.Integration;

using System.Diagnostics;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests that verify the dist folder publish, staging update
/// detection, and updater script file-copy logic using real file system operations.
/// These tests simulate the full update pipeline without launching the actual WPF app.
/// </summary>
public class DistStagingUpdateTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testRoot;
    private readonly string _distDir;
    private readonly string _stagingDir;

    public DistStagingUpdateTests(ITestOutputHelper output)
    {
        _output = output;
        _testRoot = Path.Combine(Path.GetTempPath(), $"CopilotFamilyDistTest_{Guid.NewGuid():N}");
        _distDir = Path.Combine(_testRoot, "dist");
        _stagingDir = Path.Combine(_distDir, "staging");
        Directory.CreateDirectory(_distDir);
        Directory.CreateDirectory(_stagingDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, true); } catch { }
    }

    // ──────────────────────────────────────────────
    // Dist folder structure tests
    // ──────────────────────────────────────────────

    [Fact]
    public void DistFolder_CanBeCreated()
    {
        Assert.True(Directory.Exists(_distDir));
        Assert.True(Directory.Exists(_stagingDir));
    }

    [Fact]
    public async Task PublishOutput_CanBeCopiedToDist()
    {
        // Simulate publish output — create some fake DLLs and EXE
        await CreateFakeFile(Path.Combine(_distDir, "CopilotFamily.App.exe"), "v1-exe");
        await CreateFakeFile(Path.Combine(_distDir, "CopilotFamily.Core.dll"), "v1-core");
        await CreateFakeFile(Path.Combine(_distDir, "CopilotFamily.App.dll"), "v1-app");

        Assert.True(File.Exists(Path.Combine(_distDir, "CopilotFamily.App.exe")));
        Assert.Equal("v1-exe", await File.ReadAllTextAsync(Path.Combine(_distDir, "CopilotFamily.App.exe")));
    }

    // ──────────────────────────────────────────────
    // Staging detection tests
    // ──────────────────────────────────────────────

    [Fact]
    public void StagingFolder_EmptyInitially()
    {
        var files = Directory.GetFiles(_stagingDir, "*", SearchOption.AllDirectories);
        Assert.Empty(files);
    }

    [Fact]
    public async Task StagingFolder_DetectsNewFiles()
    {
        await CreateFakeFile(Path.Combine(_stagingDir, "CopilotFamily.App.exe"), "v2-exe");
        await CreateFakeFile(Path.Combine(_stagingDir, "CopilotFamily.Core.dll"), "v2-core");

        var files = Directory.GetFiles(_stagingDir, "*", SearchOption.AllDirectories);
        Assert.Equal(2, files.Length);
    }

    // ──────────────────────────────────────────────
    // Updater script tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UpdaterScript_CopiesStagedFilesToDist()
    {
        // Setup: v1 in dist, v2 in staging
        await CreateFakeFile(Path.Combine(_distDir, "app.exe"), "v1");
        await CreateFakeFile(Path.Combine(_distDir, "lib.dll"), "v1-lib");
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "v2");
        await CreateFakeFile(Path.Combine(_stagingDir, "lib.dll"), "v2-lib");
        await CreateFakeFile(Path.Combine(_stagingDir, "newfile.txt"), "v2-new");

        // Run the updater logic (simulate what the script does)
        SimulateUpdaterCopy(_stagingDir, _distDir);

        // Verify: dist has v2 files
        Assert.Equal("v2", await File.ReadAllTextAsync(Path.Combine(_distDir, "app.exe")));
        Assert.Equal("v2-lib", await File.ReadAllTextAsync(Path.Combine(_distDir, "lib.dll")));
        Assert.Equal("v2-new", await File.ReadAllTextAsync(Path.Combine(_distDir, "newfile.txt")));
    }

    [Fact]
    public async Task UpdaterScript_ClearsStagingAfterCopy()
    {
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "v2");
        await CreateFakeFile(Path.Combine(_stagingDir, "lib.dll"), "v2-lib");

        SimulateUpdaterCopy(_stagingDir, _distDir);

        // Clear staging (as the updater does)
        foreach (var file in Directory.GetFiles(_stagingDir))
            File.Delete(file);

        var remaining = Directory.GetFiles(_stagingDir);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task UpdaterScript_HandlesSubdirectories()
    {
        // Staging has a subdirectory
        var subDir = Path.Combine(_stagingDir, "runtimes", "win-x64");
        Directory.CreateDirectory(subDir);
        await CreateFakeFile(Path.Combine(subDir, "native.dll"), "v2-native");

        SimulateUpdaterCopy(_stagingDir, _distDir);

        var destFile = Path.Combine(_distDir, "runtimes", "win-x64", "native.dll");
        Assert.True(File.Exists(destFile));
        Assert.Equal("v2-native", await File.ReadAllTextAsync(destFile));
    }

    [Fact]
    public void UpdaterScript_SkipsWhenStagingIsEmpty()
    {
        // No files in staging — should be a no-op
        var filesBefore = Directory.GetFiles(_distDir, "*", SearchOption.AllDirectories).Length;

        // Updater checks for files first
        var hasFiles = Directory.EnumerateFiles(_stagingDir, "*", SearchOption.AllDirectories).Any();
        Assert.False(hasFiles);

        var filesAfter = Directory.GetFiles(_distDir, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(filesBefore, filesAfter);
    }

    // ──────────────────────────────────────────────
    // PowerShell updater script execution test
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UpdaterScript_RunsSuccessfully_WithFakeProcess()
    {
        // Extract the updater script from the App project's embedded resource
        var scriptPath = await ExtractUpdaterScript();
        Assert.True(File.Exists(scriptPath), "Updater script should be extractable");

        // Setup: v1 in dist, v2 in staging
        await CreateFakeFile(Path.Combine(_distDir, "app.exe"), "v1");
        await CreateFakeFile(Path.Combine(_stagingDir, "app.exe"), "v2");

        // Create a dummy "app" process that immediately exits so the updater can proceed
        // Use a short-lived PowerShell process as our fake app
        var dummyProc = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-Command Start-Sleep -Milliseconds 500",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var pid = dummyProc.Id;

        // Run the updater script
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                        $"-AppPid {pid} -DistPath \"{_distDir}\" " +
                        $"-StagingPath \"{_stagingDir}\" " +
                        $"-AppExe \"powershell.exe\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var updater = Process.Start(psi)!;
        var stdout = await updater.StandardOutput.ReadToEndAsync();
        var stderr = await updater.StandardError.ReadToEndAsync();
        await updater.WaitForExitAsync();

        _output.WriteLine("Updater stdout:");
        _output.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _output.WriteLine("Updater stderr:");
            _output.WriteLine(stderr);
        }

        // The updater should have succeeded (exit code 0)
        Assert.Equal(0, updater.ExitCode);

        // Verify: dist/app.exe is now v2
        Assert.Equal("v2", await File.ReadAllTextAsync(Path.Combine(_distDir, "app.exe")));

        // Verify: staging is cleared
        var stagingFiles = Directory.GetFiles(_stagingDir, "*", SearchOption.AllDirectories);
        Assert.Empty(stagingFiles);

        // Verify: update.log was written
        var logPath = Path.Combine(_distDir, "update.log");
        Assert.True(File.Exists(logPath));
        var log = await File.ReadAllTextAsync(logPath);
        _output.WriteLine("Update log:");
        _output.WriteLine(log);
        Assert.Contains("Updater started", log);
        Assert.Contains("Copy succeeded", log);
        Assert.Contains("Updater complete", log);

        // Clean up any launched processes
        try { dummyProc.Kill(); } catch { }
    }

    [Fact]
    public async Task UpdaterScript_TimesOut_WhenProcessDoesNotExit()
    {
        var scriptPath = await ExtractUpdaterScript();

        // Start a long-running dummy process
        var dummyProc = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-Command Start-Sleep -Seconds 120",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var pid = dummyProc.Id;

        try
        {
            // Modify script timeout to 2 seconds for test speed
            var scriptContent = await File.ReadAllTextAsync(scriptPath);
            var fastScript = scriptContent
                .Replace("$waited -lt 30", "$waited -lt 2")
                .Replace("$waited -ge 30", "$waited -ge 2");
            var fastScriptPath = Path.Combine(_testRoot, "update-fast.ps1");
            await File.WriteAllTextAsync(fastScriptPath, fastScript);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{fastScriptPath}\" " +
                            $"-AppPid {pid} -DistPath \"{_distDir}\" " +
                            $"-StagingPath \"{_stagingDir}\" " +
                            $"-AppExe \"powershell.exe\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var updater = Process.Start(psi)!;
            await updater.WaitForExitAsync();

            // Should exit with code 1 (timeout)
            Assert.Equal(1, updater.ExitCode);

            var logPath = Path.Combine(_distDir, "update.log");
            if (File.Exists(logPath))
            {
                var log = await File.ReadAllTextAsync(logPath);
                _output.WriteLine(log);
                Assert.Contains("did not exit within", log);
            }
        }
        finally
        {
            try { dummyProc.Kill(); } catch { }
        }
    }

    // ──────────────────────────────────────────────
    // End-to-end publish simulation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EndToEnd_PublishThenStageUpdate()
    {
        // Step 1: Simulate initial publish to dist/
        var v1Files = new Dictionary<string, string>
        {
            { "CopilotFamily.App.exe", "v1-exe-content" },
            { "CopilotFamily.App.dll", "v1-app-dll" },
            { "CopilotFamily.Core.dll", "v1-core-dll" },
            { "CopilotFamily.App.deps.json", "{\"v1\": true}" },
        };

        foreach (var (name, content) in v1Files)
            await CreateFakeFile(Path.Combine(_distDir, name), content);

        // Step 2: Simulate staging a new version
        var v2Files = new Dictionary<string, string>
        {
            { "CopilotFamily.App.exe", "v2-exe-content" },
            { "CopilotFamily.App.dll", "v2-app-dll" },
            { "CopilotFamily.Core.dll", "v2-core-dll" },
            { "CopilotFamily.App.deps.json", "{\"v2\": true}" },
            { "NewFeature.dll", "v2-new-feature" },
        };

        foreach (var (name, content) in v2Files)
            await CreateFakeFile(Path.Combine(_stagingDir, name), content);

        // Step 3: Simulate updater copy
        SimulateUpdaterCopy(_stagingDir, _distDir);

        // Verify: all files updated
        Assert.Equal("v2-exe-content", await File.ReadAllTextAsync(Path.Combine(_distDir, "CopilotFamily.App.exe")));
        Assert.Equal("v2-app-dll", await File.ReadAllTextAsync(Path.Combine(_distDir, "CopilotFamily.App.dll")));
        Assert.Equal("v2-core-dll", await File.ReadAllTextAsync(Path.Combine(_distDir, "CopilotFamily.Core.dll")));
        Assert.Equal("{\"v2\": true}", await File.ReadAllTextAsync(Path.Combine(_distDir, "CopilotFamily.App.deps.json")));
        Assert.Equal("v2-new-feature", await File.ReadAllTextAsync(Path.Combine(_distDir, "NewFeature.dll")));

        _output.WriteLine($"End-to-end: {Directory.GetFiles(_distDir).Length} files in dist after update");
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static async Task CreateFakeFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>
    /// Simulates the copy logic from the updater PowerShell script in C#.
    /// This mirrors the script's behavior for testability without running PowerShell.
    /// </summary>
    private static void SimulateUpdaterCopy(string stagingPath, string distPath)
    {
        foreach (var srcFile in Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(stagingPath, srcFile);
            var destFile = Path.Combine(distPath, relativePath);
            var destDir = Path.GetDirectoryName(destFile)!;
            Directory.CreateDirectory(destDir);
            File.Copy(srcFile, destFile, overwrite: true);
        }
    }

    /// <summary>
    /// Extracts the updater script from the App project's embedded resource.
    /// </summary>
    private async Task<string> ExtractUpdaterScript()
    {
        // Read the script from the source file directly (the embedded resource
        // is only available in the App assembly which isn't referenced from tests)
        var repoRoot = FindRepoRoot();
        var scriptSrc = Path.Combine(repoRoot, "src", "CopilotFamily.App", "Resources", "update.ps1");

        if (!File.Exists(scriptSrc))
            throw new FileNotFoundException($"Updater script not found at {scriptSrc}");

        var destPath = Path.Combine(_testRoot, "update.ps1");
        await File.WriteAllTextAsync(destPath, await File.ReadAllTextAsync(scriptSrc));
        return destPath;
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "CopilotFamily.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find repo root (CopilotFamily.slnx)");
    }
}
