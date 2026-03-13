namespace CopilotNexus.Core.Versioning;

using System.Text.RegularExpressions;

/// <summary>
/// Minimal SemVer parser/comparer supporting optional pre-release identifiers.
/// Build metadata is ignored for ordering.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<string> _prereleaseIdentifiers;

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }
    public bool IsPrerelease => !string.IsNullOrWhiteSpace(Prerelease);

    public SemanticVersion(int major, int minor, int patch, string? prerelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease.Trim();
        _prereleaseIdentifiers = Prerelease?.Split('.', StringSplitOptions.RemoveEmptyEntries) ?? [];
    }

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = VersionPattern.Match(value.Trim());
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value
            : null;
        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version) || version == null)
            throw new FormatException($"Invalid semantic version: {value}");
        return version;
    }

    public SemanticVersion NextMajor() => new(Major + 1, 0, 0);
    public SemanticVersion NextMinor() => new(Major, Minor + 1, 0);
    public SemanticVersion NextPatch() => new(Major, Minor, Patch + 1);

    public int CompareTo(SemanticVersion? other)
    {
        if (other == null)
            return 1;

        var major = Major.CompareTo(other.Major);
        if (major != 0)
            return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
            return minor;

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0)
            return patch;

        if (!IsPrerelease && !other.IsPrerelease)
            return 0;
        if (!IsPrerelease)
            return 1;
        if (!other.IsPrerelease)
            return -1;

        return ComparePrerelease(_prereleaseIdentifiers, other._prereleaseIdentifiers);
    }

    public override string ToString()
    {
        return IsPrerelease
            ? $"{Major}.{Minor}.{Patch}-{Prerelease}"
            : $"{Major}.{Minor}.{Patch}";
    }

    private static int ComparePrerelease(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var max = Math.Max(left.Count, right.Count);
        for (var i = 0; i < max; i++)
        {
            if (i >= left.Count)
                return -1;
            if (i >= right.Count)
                return 1;

            var leftToken = left[i];
            var rightToken = right[i];
            var leftNumeric = int.TryParse(leftToken, out var leftValue);
            var rightNumeric = int.TryParse(rightToken, out var rightValue);

            if (leftNumeric && rightNumeric)
            {
                var numericCompare = leftValue.CompareTo(rightValue);
                if (numericCompare != 0)
                    return numericCompare;
                continue;
            }

            if (leftNumeric && !rightNumeric)
                return -1;
            if (!leftNumeric && rightNumeric)
                return 1;

            var lexicalCompare = string.Compare(leftToken, rightToken, StringComparison.Ordinal);
            if (lexicalCompare != 0)
                return lexicalCompare;
        }

        return 0;
    }
}
