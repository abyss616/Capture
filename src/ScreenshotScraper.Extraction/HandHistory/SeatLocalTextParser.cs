using System.Text.RegularExpressions;

namespace ScreenshotScraper.Extraction.HandHistory;

public static partial class SeatLocalTextParser
{
    public static string ParseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Replace("\r", " ").Replace("\n", " ");
        foreach (Match match in NameTokenRegex().Matches(cleaned))
        {
            var token = match.Value.Trim();
            if (IgnoredNameTokenRegex().IsMatch(token))
            {
                continue;
            }

            if (token.Length >= 3)
            {
                return token;
            }
        }

        return string.Empty;
    }

    public static string ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace('O', '0').Replace('o', '0').Replace(',', '.');
        var match = NumberRegex().Match(normalized);
        return match.Success ? match.Groups["num"].Value : string.Empty;
    }

    [GeneratedRegex(@"[A-Za-z0-9_]{3,}", RegexOptions.Compiled)]
    private static partial Regex NameTokenRegex();

    [GeneratedRegex(@"^(TIME|BANK|POT|MAX|CALL|FOLD|RAISE|EXIT)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IgnoredNameTokenRegex();

    [GeneratedRegex(@"(?<num>\d+(?:[\.,]\d+)?)\s*(?:BB)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NumberRegex();
}
