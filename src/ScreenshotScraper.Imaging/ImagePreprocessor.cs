using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Imaging;

/// <summary>
/// Placeholder preprocessing stage. Future image cleanup, cropping, and enhancement logic will live here.
/// </summary>
public sealed class ImagePreprocessor : IImagePreprocessor
{
    public Task<CapturedImage> PrepareAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(image);
    }
}
