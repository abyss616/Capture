using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Capture;

/// <summary>
/// Placeholder screenshot implementation. Future versions can support active window,
/// full screen, and selected region capture modes.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    public Task<CapturedImage> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Placeholder image bytes for startup/demo flow until real screen capture is implemented.
        var placeholderBytes = Array.Empty<byte>();

        return Task.FromResult(new CapturedImage
        {
            ImageBytes = placeholderBytes,
            Width = 0,
            Height = 0,
            CapturedAtUtc = DateTime.UtcNow,
            SourceDescription = "Placeholder capture - real screen capture not implemented yet."
        });
    }
}
