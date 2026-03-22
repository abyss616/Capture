namespace ScreenshotScraper.Core.Models;

/// <summary>
/// Represents a captured image and the metadata needed by downstream scraping stages.
/// </summary>
public sealed class CapturedImage
{
    public byte[] ImageBytes { get; init; } = [];

    public int Width { get; init; }

    public int Height { get; init; }

    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    public string? SourceDescription { get; init; }

    public string? WindowTitle { get; init; }

    public string? ProcessName { get; init; }

    public int WindowLeft { get; init; }

    public int WindowTop { get; init; }

    public int WindowWidth { get; init; }

    public int WindowHeight { get; init; }

    public bool IsVisible { get; init; }

    public bool IsForegroundWindow { get; init; }

    public nint WindowHandle { get; init; }

    public string? CaptureMethod { get; init; }

    public string? MonitorDeviceName { get; init; }
}
