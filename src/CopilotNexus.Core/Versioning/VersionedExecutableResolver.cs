namespace CopilotNexus.Core.Versioning;

public sealed record VersionedExecutable(
    string VersionDirectory,
    string ExecutablePath,
    SemanticVersion Version);

public static class VersionedExecutableResolver
{
    public static IReadOnlyList<VersionedExecutable> ListAvailableExecutables(string componentRoot, string executableName)
    {
        if (string.IsNullOrWhiteSpace(componentRoot))
            throw new ArgumentException("Component root is required.", nameof(componentRoot));
        if (string.IsNullOrWhiteSpace(executableName))
            throw new ArgumentException("Executable name is required.", nameof(executableName));
        if (!Directory.Exists(componentRoot))
            return [];

        var resolved = new List<VersionedExecutable>();
        foreach (var directory in Directory.EnumerateDirectories(componentRoot))
        {
            var folderName = Path.GetFileName(directory);
            if (!SemanticVersion.TryParse(folderName, out var version) || version == null)
                continue;

            var executablePath = Path.Combine(directory, executableName);
            if (!File.Exists(executablePath))
                continue;

            resolved.Add(new VersionedExecutable(directory, executablePath, version));
        }

        return resolved
            .OrderByDescending(entry => entry.Version)
            .ThenByDescending(entry => entry.VersionDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static VersionedExecutable ResolveExecutable(
        string componentRoot,
        string executableName,
        bool previous = false)
    {
        var candidates = ListAvailableExecutables(componentRoot, executableName);
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No versioned payloads found for '{executableName}' under '{componentRoot}'.");

        var index = previous ? 1 : 0;
        if (index >= candidates.Count)
        {
            throw new InvalidOperationException(
                $"No previous version is available for '{executableName}' under '{componentRoot}'.");
        }

        return candidates[index];
    }

    public static IReadOnlyList<string> CleanupOldVersions(
        string componentRoot,
        string executableName,
        int keepCount)
    {
        if (keepCount < 1)
            throw new ArgumentOutOfRangeException(nameof(keepCount), "keepCount must be at least 1.");

        var candidates = ListAvailableExecutables(componentRoot, executableName);
        if (candidates.Count <= keepCount)
            return [];

        var stale = candidates.Skip(keepCount).ToList();
        var deleted = new List<string>(stale.Count);
        foreach (var item in stale)
        {
            Directory.Delete(item.VersionDirectory, recursive: true);
            deleted.Add(item.VersionDirectory);
        }

        return deleted;
    }
}
