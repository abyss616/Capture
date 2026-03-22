using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IScreenshotService
{
    Task<CapturedImage> CaptureAsync(CancellationToken cancellationToken = default);
}
