using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Capture;

internal static class WindowCandidateSelector
{
    public static WindowCaptureTarget SelectBestCandidate(IEnumerable<WindowInfo> windows, WindowSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = windows.ToList();
        var filtered = diagnostics.Where(window => IsCandidate(window, options)).ToList();

        if (filtered.Count == 0)
        {
            throw new WindowNotFoundException(BuildFailureMessage(diagnostics, options));
        }

        var best = filtered
            .OrderByDescending(window => WindowTitleMatcher.GetMatchScore(window.Title, options.WindowTitleContains))
            .ThenByDescending(window => window.HasTitle)
            .ThenByDescending(window => window.Area)
            .ThenByDescending(window => window.IsForeground)
            .ThenBy(window => window.Handle)
            .First();

        return new WindowCaptureTarget
        {
            Window = best,
            TitleMatchScore = WindowTitleMatcher.GetMatchScore(best.Title, options.WindowTitleContains),
            SelectionReason = "Ranked by title match, area, foreground status, then handle."
        };
    }

    public static bool IsCandidate(WindowInfo window, WindowSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);

        if (!IsProcessMatch(window.ProcessName, options.ProcessName))
        {
            return false;
        }

        if (options.RequireVisible && !window.IsVisible)
        {
            return false;
        }

        if (options.RequireNotMinimized && window.IsMinimized)
        {
            return false;
        }

        if (options.RequireForegroundWindow && !window.IsForeground)
        {
            return false;
        }

        if (options.ExcludeOwnedWindows && window.IsOwnedWindow)
        {
            return false;
        }

        if (options.ExcludeToolWindows && window.IsToolWindow)
        {
            return false;
        }

        if (window.Width < options.MinimumWidth || window.Height < options.MinimumHeight)
        {
            return false;
        }

        if (!WindowTitleMatcher.IsMatch(window.Title, options.WindowTitleContains))
        {
            return false;
        }

        if (options.ExcludedTitleContains.Any(excluded => WindowTitleMatcher.IsMatch(window.Title, excluded)))
        {
            return false;
        }

        return true;
    }

    internal static bool IsProcessMatch(string? actualProcessName, string? requiredProcessName)
    {
        var normalizedActual = NormalizeProcessName(actualProcessName);
        var normalizedRequired = NormalizeProcessName(requiredProcessName);

        if (string.IsNullOrWhiteSpace(normalizedRequired))
        {
            return true;
        }

        return string.Equals(normalizedActual, normalizedRequired, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }

    private static string BuildFailureMessage(IReadOnlyList<WindowInfo> windows, WindowSearchOptions options)
    {
        var processMatches = windows.Where(window => IsProcessMatch(window.ProcessName, options.ProcessName)).ToList();

        if (processMatches.Count == 0)
        {
            return $"No {NormalizeProcessName(options.ProcessName)} window found.";
        }

        if (options.RequireVisible && processMatches.All(window => !window.IsVisible))
        {
            return $"No visible {NormalizeProcessName(options.ProcessName)} window found.";
        }

        if (options.RequireNotMinimized && processMatches.All(window => window.IsMinimized))
        {
            return $"All matching {NormalizeProcessName(options.ProcessName)} windows are minimized.";
        }

        if (!string.IsNullOrWhiteSpace(options.WindowTitleContains)
            && processMatches.All(window => !WindowTitleMatcher.IsMatch(window.Title, options.WindowTitleContains)))
        {
            return $"Title filter '{options.WindowTitleContains}' excluded all {NormalizeProcessName(options.ProcessName)} windows.";
        }

        if (options.ExcludedTitleContains.Count > 0
            && processMatches.All(window => options.ExcludedTitleContains.Any(excluded => WindowTitleMatcher.IsMatch(window.Title, excluded))))
        {
            return $"Excluded window-title filters removed all {NormalizeProcessName(options.ProcessName)} windows.";
        }

        return $"No visible {NormalizeProcessName(options.ProcessName)} window satisfied the capture filters.";
    }
}
