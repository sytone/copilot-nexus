using CopilotNexus.Updater;

// CopilotNexus.Updater — Cross-platform update shim
// Waits for the host app to exit, copies staged files, relaunches.
//
// Usage: CopilotNexus.Updater --app-pid <PID> --install-path <path>
//        --staging-path <path> --app-exe <path> [--timeout <seconds>]

var options = ParseArgs(args);
if (options is null)
{
    Console.Error.WriteLine(
        "Usage: CopilotNexus.Updater --app-pid <PID> --install-path <path> " +
        "--staging-path <path> --app-exe <path> [--timeout <seconds>]");
    return 1;
}

// Log to console and to a log file
var logDir = Path.Combine(Path.GetDirectoryName(options.InstallPath)!, "logs");
Directory.CreateDirectory(logDir);
var logFile = Path.Combine(logDir, "update.log");

void Log(string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
    Console.WriteLine(line);
    try { File.AppendAllText(logFile, line + Environment.NewLine); }
    catch { /* best effort */ }
}

var service = new UpdaterService(Log);
return await service.RunAsync(options);

static UpdaterOptions? ParseArgs(string[] args)
{
    var opts = new UpdaterOptions();
    bool hasPid = false, hasInstall = false, hasStaging = false, hasExe = false;

    for (int i = 0; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--app-pid":
                if (int.TryParse(args[++i], out var pid)) { opts.AppPid = pid; hasPid = true; }
                break;
            case "--install-path":
                opts.InstallPath = args[++i]; hasInstall = true;
                break;
            case "--staging-path":
                opts.StagingPath = args[++i]; hasStaging = true;
                break;
            case "--app-exe":
                opts.AppExe = args[++i]; hasExe = true;
                break;
            case "--timeout":
                if (int.TryParse(args[++i], out var t)) opts.WaitTimeoutSeconds = t;
                break;
        }
    }

    return hasPid && hasInstall && hasStaging && hasExe ? opts : null;
}
