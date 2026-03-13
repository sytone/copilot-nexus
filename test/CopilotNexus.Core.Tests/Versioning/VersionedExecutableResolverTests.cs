namespace CopilotNexus.Core.Tests.Versioning;

using CopilotNexus.Core.Versioning;
using Xunit;

public class VersionedExecutableResolverTests : IDisposable
{
    private readonly string _componentRoot = Path.Combine(
        Path.GetTempPath(),
        "copilot-nexus-resolver-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ListAvailableExecutables_OrdersBySemanticVersionDescending()
    {
        CreatePayload("1.0.0");
        CreatePayload("1.2.0-dev.1");
        CreatePayload("1.2.0");
        CreatePayload("not-a-version");

        var entries = VersionedExecutableResolver.ListAvailableExecutables(_componentRoot, "CopilotNexus.Service.exe");
        var ordered = entries.Select(x => x.Version.ToString()).ToArray();

        Assert.Equal(new[] { "1.2.0", "1.2.0-dev.1", "1.0.0" }, ordered);
    }

    [Fact]
    public void ResolveExecutable_Previous_ReturnsSecondNewest()
    {
        CreatePayload("2.0.0");
        CreatePayload("1.9.0");

        var resolved = VersionedExecutableResolver.ResolveExecutable(
            _componentRoot,
            "CopilotNexus.Service.exe",
            previous: true);

        Assert.Equal("1.9.0", resolved.Version.ToString());
    }

    [Fact]
    public void CleanupOldVersions_RemovesAnythingBeyondRetentionCount()
    {
        CreatePayload("1.0.0");
        CreatePayload("1.1.0");
        CreatePayload("1.2.0");

        var deleted = VersionedExecutableResolver.CleanupOldVersions(
            _componentRoot,
            "CopilotNexus.Service.exe",
            keepCount: 2);

        Assert.Single(deleted);
        Assert.Contains(Path.Combine(_componentRoot, "1.0.0"), deleted);
        Assert.False(Directory.Exists(Path.Combine(_componentRoot, "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(_componentRoot, "1.1.0")));
        Assert.True(Directory.Exists(Path.Combine(_componentRoot, "1.2.0")));
    }

    [Fact]
    public void ResolveExecutable_PreviousThrowsWhenOnlyOneVersionExists()
    {
        CreatePayload("1.0.0");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            VersionedExecutableResolver.ResolveExecutable(_componentRoot, "CopilotNexus.Service.exe", previous: true));

        Assert.Contains("No previous version is available", ex.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_componentRoot))
            Directory.Delete(_componentRoot, recursive: true);
    }

    private void CreatePayload(string versionFolder)
    {
        var folder = Path.Combine(_componentRoot, versionFolder);
        Directory.CreateDirectory(folder);
        if (SemanticVersion.TryParse(versionFolder, out _))
        {
            File.WriteAllText(Path.Combine(folder, "CopilotNexus.Service.exe"), "shim");
        }
    }
}
