using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Capture;

/// <summary>
/// Capture-specific defaults for locating the poker table window.
/// </summary>
public sealed class PokerWindowCaptureOptions
{
    public string ProcessName { get; init; } = "PokerClient";

    public string? WindowTitleContains { get; init; }

    public bool RequireVisible { get; init; } = true;

    public bool RequireNotMinimized { get; init; } = true;

    public bool RequireForegroundWindow { get; init; }

    public bool ExcludeOwnedWindows { get; init; } = true;

    public bool ExcludeToolWindows { get; init; } = true;

    public IReadOnlyList<string> ExcludedTitleContains { get; init; } = ["lobby"];

    public int MinimumWidth { get; init; } = 200;

    public int MinimumHeight { get; init; } = 150;

    public WindowSearchOptions ToSearchOptions()
    {
        return new WindowSearchOptions
        {
            ProcessName = ProcessName,
            WindowTitleContains = WindowTitleContains,
            RequireVisible = RequireVisible,
            RequireNotMinimized = RequireNotMinimized,
            RequireForegroundWindow = RequireForegroundWindow,
            ExcludeOwnedWindows = ExcludeOwnedWindows,
            ExcludeToolWindows = ExcludeToolWindows,
            ExcludedTitleContains = ExcludedTitleContains,
            MinimumWidth = MinimumWidth,
            MinimumHeight = MinimumHeight
        };
    }
}
