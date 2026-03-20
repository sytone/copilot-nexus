using System.Diagnostics;
using CopilotNexus.Shim.Versioning;

var parseResult = ParseArgs(args);
if (parseResult.ShowHelp)
{
    PrintHelp();
    return 0;
}

if (!string.IsNullOrWhiteSpace(parseResult.Error))
{
    Console.Error.WriteLine(parseResult.Error);
    PrintHelp();
    return 1;
}

var processPath = Environment.ProcessPath;
if (string.IsNullOrWhiteSpace(processPath))
{
    Console.Error.WriteLine("Unable to determine shim process path.");
    return 1;
}

var executableName = Path.GetFileName(processPath);
var componentRoot = Path.GetDirectoryName(processPath);
if (string.IsNullOrWhiteSpace(componentRoot))
{
    Console.Error.WriteLine("Unable to determine shim component root directory.");
    return 1;
}

try
{
    if (parseResult.CleanupCount.HasValue)
    {
        var deleted = VersionedExecutableResolver.CleanupOldVersions(
            componentRoot,
            executableName,
            parseResult.CleanupCount.Value);
        Console.WriteLine($"Deleted {deleted.Count} old version folder(s).");
        return 0;
    }

    var resolved = VersionedExecutableResolver.ResolveExecutable(
        componentRoot,
        executableName,
        previous: parseResult.UsePrevious);

    if (parseResult.ResolveOnly)
    {
        Console.WriteLine(resolved.ExecutablePath);
        return 0;
    }

    var currentProcessPath = Path.GetFullPath(processPath);
    if (string.Equals(currentProcessPath, Path.GetFullPath(resolved.ExecutablePath), StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Resolved executable points to shim path. Check versioned payload directories.");
        return 1;
    }

    var launchWorkingDirectory = Directory.Exists(Environment.CurrentDirectory)
        ? Environment.CurrentDirectory
        : Path.GetDirectoryName(resolved.ExecutablePath)!;

    var psi = new ProcessStartInfo
    {
        FileName = resolved.ExecutablePath,
        WorkingDirectory = launchWorkingDirectory,
        UseShellExecute = false,
    };

    foreach (var forwardedArg in parseResult.ForwardedArgs)
    {
        psi.ArgumentList.Add(forwardedArg);
    }

    psi.Environment["COPILOT_NEXUS_SHIM_PATH"] = currentProcessPath;

    using var child = Process.Start(psi);
    if (child == null)
    {
        Console.Error.WriteLine($"Failed to launch '{resolved.ExecutablePath}'.");
        return 1;
    }

    // Shim exits immediately — child inherits the console and runs independently.
    // This ensures the shim executable is never locked during the child's lifetime.
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static ParsedArguments ParseArgs(string[] inputArgs)
{
    var result = new ParsedArguments();
    if (!inputArgs.Any(IsShimSpecificArgument))
    {
        foreach (var arg in inputArgs)
        {
            result.ForwardedArgs.Add(arg);
        }

        return result;
    }

    for (var index = 0; index < inputArgs.Length; index++)
    {
        var arg = inputArgs[index];
        if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
        {
            result.ShowHelp = true;
            return result;
        }

        if (string.Equals(arg, "--previous", StringComparison.OrdinalIgnoreCase))
        {
            result.UsePrevious = true;
            continue;
        }

        if (string.Equals(arg, "--resolve-path", StringComparison.OrdinalIgnoreCase))
        {
            result.ResolveOnly = true;
            continue;
        }

        if (string.Equals(arg, "--cleanup", StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= inputArgs.Length)
            {
                result.Error = "Missing value for --cleanup.";
                return result;
            }

            var value = inputArgs[++index];
            if (!int.TryParse(value, out var keepCount) || keepCount < 1)
            {
                result.Error = "--cleanup requires an integer >= 1.";
                return result;
            }

            result.CleanupCount = keepCount;
            continue;
        }

        result.ForwardedArgs.Add(arg);
    }

    if (result.CleanupCount.HasValue && (result.UsePrevious || result.ResolveOnly || result.ForwardedArgs.Count > 0))
    {
        result.Error = "--cleanup cannot be combined with launch/resolve arguments.";
    }

    if (result.ResolveOnly && result.ForwardedArgs.Count > 0)
    {
        result.Error = "--resolve-path cannot be combined with forwarded target arguments.";
    }

    return result;
}

static bool IsShimSpecificArgument(string arg)
{
    return string.Equals(arg, "--previous", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "--resolve-path", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "--cleanup", StringComparison.OrdinalIgnoreCase);
}

static void PrintHelp()
{
    Console.WriteLine("CopilotNexus versioned launcher shim");
    Console.WriteLine("Usage:");
    Console.WriteLine("  <shim> [--previous] [target args...]");
    Console.WriteLine("  <shim> --resolve-path [--previous]");
    Console.WriteLine("  <shim> --cleanup <count>");
}

file sealed class ParsedArguments
{
    public bool ShowHelp { get; set; }
    public bool UsePrevious { get; set; }
    public bool ResolveOnly { get; set; }
    public int? CleanupCount { get; set; }
    public List<string> ForwardedArgs { get; } = [];
    public string? Error { get; set; }
}
