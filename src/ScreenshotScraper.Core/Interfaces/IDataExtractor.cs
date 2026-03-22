using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IDataExtractor
{
    Task<ExtractionResult> ExtractAsync(CapturedImage image, CancellationToken cancellationToken = default);
}
