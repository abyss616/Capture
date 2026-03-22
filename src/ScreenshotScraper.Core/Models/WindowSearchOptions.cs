namespace ScreenshotScraper.Core.Models;

/// <summary>
/// Generic search and filtering rules for locating a top-level window.
/// </summary>
public sealed class WindowSearchOptions
{
    public string ProcessName { get; init; } = string.Empty;

    public string? WindowTitleContains { get; init; }

    public bool RequireVisible { get; init; } = true;

    public bool RequireNotMinimized { get; init; } = true;

    public bool RequireForegroundWindow { get; init; }

    public bool ExcludeOwnedWindows { get; init; } = true;

    public bool ExcludeToolWindows { get; init; } = true;

    public IReadOnlyList<string> ExcludedTitleContains { get; init; } = [];

    public int MinimumWidth { get; init; } = 200;

    public int MinimumHeight { get; init; } = 150;
}
