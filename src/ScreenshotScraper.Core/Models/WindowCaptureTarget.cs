namespace ScreenshotScraper.Core.Models;

/// <summary>
/// Represents the chosen window target for a capture operation.
/// </summary>
public sealed class WindowCaptureTarget
{
    public required WindowInfo Window { get; init; }

    public int TitleMatchScore { get; init; }

    public string? SelectionReason { get; init; }
}
