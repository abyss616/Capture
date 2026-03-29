using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface ICardExtractor
{
    Task<string> ExtractHeroCardsAsync(CapturedImage image, CancellationToken cancellationToken = default);
}
