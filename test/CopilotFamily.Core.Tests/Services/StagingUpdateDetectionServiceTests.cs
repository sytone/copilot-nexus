namespace CopilotFamily.Core.Tests.Services;

using System.IO;
using CopilotFamily.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class StagingUpdateDetectionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stagingDir;

    public StagingUpdateDetectionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CopilotFamilyTest_{Guid.NewGuid():N}");
        _stagingDir = Path.Combine(_tempDir, "staging");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void StartWatching_CreatesStagingDirectory()
    {
        using var svc = new StagingUpdateDetectionService(_stagingDir,
            NullLogger<StagingUpdateDetectionService>.Instance);
        svc.StartWatching();
        Assert.True(Directory.Exists(_stagingDir));
    }

    [Fact]
    public void IsUpdateStaged_FalseInitially()
    {
        using var svc = new StagingUpdateDetectionService(_stagingDir,
            NullLogger<StagingUpdateDetectionService>.Instance);
        Assert.False(svc.IsUpdateStaged);
    }

    [Fact]
    public async Task DetectsFilesInStaging()
    {
        Directory.CreateDirectory(_stagingDir);
        using var svc = new StagingUpdateDetectionService(_stagingDir,
            NullLogger<StagingUpdateDetectionService>.Instance);

        var eventFired = new TaskCompletionSource<bool>();
        svc.UpdateAvailable += (_, _) => eventFired.TrySetResult(true);
        svc.StartWatching();

        // Drop a file
        await File.WriteAllTextAsync(Path.Combine(_stagingDir, "test.dll"), "data");

        // Wait for the event (FSW or timer should fire within a few seconds)
        var fired = await Task.WhenAny(eventFired.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(eventFired.Task.IsCompleted && await eventFired.Task);
        Assert.True(svc.IsUpdateStaged);
    }

    [Fact]
    public async Task DoesNotFireDuplicateEvents()
    {
        Directory.CreateDirectory(_stagingDir);
        using var svc = new StagingUpdateDetectionService(_stagingDir,
            NullLogger<StagingUpdateDetectionService>.Instance);

        var fireCount = 0;
        svc.UpdateAvailable += (_, _) => Interlocked.Increment(ref fireCount);
        svc.StartWatching();

        // Drop multiple files
        await File.WriteAllTextAsync(Path.Combine(_stagingDir, "a.dll"), "a");
        await File.WriteAllTextAsync(Path.Combine(_stagingDir, "b.dll"), "b");

        // Wait for FSW + timer to process
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task ResetNotification_AllowsRefire()
    {
        Directory.CreateDirectory(_stagingDir);
        using var svc = new StagingUpdateDetectionService(_stagingDir,
            NullLogger<StagingUpdateDetectionService>.Instance);

        var fireCount = 0;
        svc.UpdateAvailable += (_, _) => Interlocked.Increment(ref fireCount);
        svc.StartWatching();

        await File.WriteAllTextAsync(Path.Combine(_stagingDir, "a.dll"), "a");
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, fireCount);

        svc.ResetNotification();

        // Drop another file to re-trigger
        await File.WriteAllTextAsync(Path.Combine(_stagingDir, "c.dll"), "c");
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void StopWatching_Idempotent()
    {
        using var svc = new StagingUpdateDetectionService(_stagingDir,
            NullLogger<StagingUpdateDetectionService>.Instance);
        svc.StartWatching();
        svc.StopWatching();
        svc.StopWatching(); // Should not throw
    }
}
