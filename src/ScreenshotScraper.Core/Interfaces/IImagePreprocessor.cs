using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IImagePreprocessor
{
    Task<CapturedImage> PrepareAsync(CapturedImage image, CancellationToken cancellationToken = default);
}
