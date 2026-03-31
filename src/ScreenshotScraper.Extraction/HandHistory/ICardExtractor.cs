using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface ICardExtractor
{
    Task<Cards?> ExtractHeroCardsAsync(CapturedImage image, CancellationToken cancellationToken = default);
}
