namespace ScreenshotScraper.Capture;

internal static class WindowTitleMatcher
{
    public static bool IsMatch(string? title, string? titleFilter)
    {
        if (string.IsNullOrWhiteSpace(titleFilter))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Contains(titleFilter, StringComparison.OrdinalIgnoreCase);
    }

    public static int GetMatchScore(string? title, string? titleFilter)
    {
        if (string.IsNullOrWhiteSpace(titleFilter))
        {
            return string.IsNullOrWhiteSpace(title) ? 0 : 1;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return 0;
        }

        var index = title.IndexOf(titleFilter, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        if (title.Length == titleFilter.Length)
        {
            return 400;
        }

        if (index == 0)
        {
            return 300;
        }

        return 200;
    }
}
