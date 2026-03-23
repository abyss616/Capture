using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface ICardExtractor
{
    string ExtractHeroCards(CapturedImage image, string rawText);
}
