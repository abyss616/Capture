namespace ScreenshotScraper.Core.Models;

/// <summary>
/// Represents an image captured from the desktop while keeping the core model free of WPF-specific types.
/// </summary>
public sealed class CapturedImage
{
    public byte[]? ImageBytes { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    public string? SourceDescription { get; init; }
}
