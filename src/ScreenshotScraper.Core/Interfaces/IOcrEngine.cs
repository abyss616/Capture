using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IOcrEngine
{
    Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default);
}
