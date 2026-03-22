namespace ScreenshotScraper.Core.Models;

/// <summary>
/// Diagnostic information about a discovered top-level window candidate.
/// </summary>
public sealed class WindowInfo
{
    public nint Handle { get; init; }

    public string Title { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public int Left { get; init; }

    public int Top { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool IsVisible { get; init; }

    public bool IsMinimized { get; init; }

    public bool IsForeground { get; init; }

    public bool IsOwnedWindow { get; init; }

    public bool IsToolWindow { get; init; }

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public int Area => Width > 0 && Height > 0 ? Width * Height : 0;
}
