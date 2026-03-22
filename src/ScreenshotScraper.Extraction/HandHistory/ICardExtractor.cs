using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

internal interface ICardExtractor
{
    string ExtractHeroCards(CapturedImage image, string rawText);
}
