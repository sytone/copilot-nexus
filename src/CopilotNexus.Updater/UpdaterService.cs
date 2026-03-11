namespace CopilotNexus.Updater;

using System.Diagnostics;

/// <summary>
/// Cross-platform updater logic. Waits for the host process to exit, copies staged
/// files to the install directory, clears the staging folder, and relaunches the app.
/// All logic is in this testable class; Program.cs is a thin entry point.
/// </summary>
public class UpdaterService
{
    private readonly Action<string> _log;

    public UpdaterService(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Runs the full update cycle: wait → copy → clear → relaunch.
    /// Returns 0 on success, non-zero on failure.
    /// </summary>
    public async Task<int> RunAsync(UpdaterOptions options, CancellationToken ct = default)
    {
        _log("Updater started.");

        var waitResult = await WaitForProcessExitAsync(options.AppPid, options.WaitTimeoutSeconds, ct);
        if (!waitResult)
        {
            _log($"ERROR: Process {options.AppPid} did not exit within {options.WaitTimeoutSeconds} seconds. Aborting update.");
            return 1;
        }

        // Small delay for file handles to release
        await Task.Delay(500, ct);

        var copyResult = await CopyStagingAsync(options.StagingPath, options.InstallPath, ct);
        if (!copyResult)
            return 2;

        ClearStaging(options.StagingPath);

        if (!string.IsNullOrWhiteSpace(options.AppExe))
        {
            Relaunch(options.AppExe);
        }

        _log("Updater complete.");
        return 0;
    }

    /// <summary>
    /// Waits for a process to exit within the given timeout.
    /// Returns true if the process exited (or was already gone), false on timeout.
    /// </summary>
    internal async Task<bool> WaitForProcessExitAsync(int pid, int timeoutSeconds, CancellationToken ct)
    {
        _log($"Waiting for PID {pid} to exit (timeout: {timeoutSeconds}s)...");

        for (int waited = 0; waited < timeoutSeconds; waited++)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                if (proc.HasExited)
                {
                    _log("Process exited.");
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Process no longer exists
                _log("Process exited (not found).");
                return true;
            }
            catch (InvalidOperationException)
            {
                _log("Process exited (invalid).");
                return true;
            }

            await Task.Delay(1000, ct);
        }

        return false;
    }

    /// <summary>
    /// Copies all files from staging to the install directory with retry logic.
    /// Returns true on success, false on failure.
    /// </summary>
    internal async Task<bool> CopyStagingAsync(string stagingPath, string installPath, CancellationToken ct)
    {
        if (!Directory.Exists(stagingPath))
        {
            _log("WARNING: Staging folder does not exist. Skipping copy.");
            return true;
        }

        var files = Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            _log("WARNING: Staging folder is empty. Skipping copy.");
            return true;
        }

        _log($"Copying {files.Length} files from staging to install directory...");

        const int maxRetries = 10;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Updater cannot replace its own binaries while running.
                CopyDirectory(stagingPath, installPath, sourceFile => !IsUpdaterArtifact(sourceFile));
                _log($"Copy succeeded on attempt {attempt}.");
                return true;
            }
            catch (Exception ex)
            {
                _log($"Copy attempt {attempt} failed: {ex.Message}");
                if (attempt < maxRetries)
                    await Task.Delay(1000, ct);
            }
        }

        _log($"ERROR: Failed to copy staging files after {maxRetries} attempts.");
        return false;
    }

    /// <summary>Clears all files and subdirectories in the staging folder.</summary>
    internal void ClearStaging(string stagingPath)
    {
        if (!Directory.Exists(stagingPath))
            return;

        try
        {
            foreach (var file in Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories))
                File.Delete(file);

            foreach (var dir in Directory.GetDirectories(stagingPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir);
            }

            _log("Staging folder cleared.");
        }
        catch (Exception ex)
        {
            _log($"WARNING: Could not fully clear staging: {ex.Message}");
        }
    }

    /// <summary>Relaunches the application executable.</summary>
    internal void Relaunch(string appExe)
    {
        _log($"Launching {appExe}...");
        Process.Start(new ProcessStartInfo
        {
            FileName = appExe,
            UseShellExecute = true,
        });
    }

    /// <summary>Recursively copies all files from source to destination.</summary>
    internal static void CopyDirectory(string sourceDir, string destDir, Func<string, bool>? shouldCopyFile = null)
    {
        foreach (var srcFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (shouldCopyFile != null && !shouldCopyFile(srcFile))
                continue;

            var relativePath = Path.GetRelativePath(sourceDir, srcFile);
            var destFile = Path.Combine(destDir, relativePath);
            var destFileDir = Path.GetDirectoryName(destFile)!;
            Directory.CreateDirectory(destFileDir);
            File.Copy(srcFile, destFile, overwrite: true);
        }
    }

    private static bool IsUpdaterArtifact(string sourceFile)
    {
        var fileName = Path.GetFileName(sourceFile);
        return fileName.StartsWith("CopilotNexus.Updater.", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("CopilotNexus.Updater.exe", StringComparison.OrdinalIgnoreCase);
    }
}
