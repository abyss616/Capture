using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class OcrHeroCardExtractor : ICardExtractor
{
    public string ExtractHeroCards(CapturedImage image, string rawText)
    {
        return CardNotationFormatter.TryNormalizePairFromOcrText(rawText, out var cards)
            ? cards
            : string.Empty;
    }
}
