namespace CopilotNexus.Core.Tests.Versioning;

using CopilotNexus.Core.Versioning;
using Xunit;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-dev.20260101010101")]
    [InlineData("10.20.30-alpha.1")]
    public void TryParse_AcceptsValidSemVer(string input)
    {
        var parsed = SemanticVersion.TryParse(input, out var version);
        Assert.True(parsed);
        Assert.NotNull(version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3.4")]
    public void TryParse_RejectsInvalidSemVer(string input)
    {
        Assert.False(SemanticVersion.TryParse(input, out _));
    }

    [Fact]
    public void CompareTo_ReleaseSortsAfterPrerelease()
    {
        var prerelease = SemanticVersion.Parse("1.2.3-dev.1");
        var release = SemanticVersion.Parse("1.2.3");

        Assert.True(release.CompareTo(prerelease) > 0);
    }

    [Fact]
    public void CompareTo_PrereleaseNumericOrdering_IsSupported()
    {
        var lower = SemanticVersion.Parse("1.2.3-dev.2");
        var higher = SemanticVersion.Parse("1.2.3-dev.10");

        Assert.True(higher.CompareTo(lower) > 0);
    }

    [Fact]
    public void NextVersionHelpers_ReturnExpectedValues()
    {
        var version = SemanticVersion.Parse("1.2.3");

        Assert.Equal("2.0.0", version.NextMajor().ToString());
        Assert.Equal("1.3.0", version.NextMinor().ToString());
        Assert.Equal("1.2.4", version.NextPatch().ToString());
    }
}
