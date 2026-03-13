namespace CopilotNexus.App.Utilities;

internal static class DelimitedListParser
{
    public static List<string> Parse(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return [];

        return rawValue
            .Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
