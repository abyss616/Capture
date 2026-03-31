using System.Drawing;
using ScreenshotScraper.Extraction.HandHistory;

namespace ScreenshotScraper.Extraction.Tests;

public sealed class OcrHeroCardExtractorTests
{
    [Fact]
    public void FindCardItems_TwoDetectedCards_ReturnsFourOrderedSemanticItems()
    {
        using var heroCrop = new Bitmap(140, 80);
        using (var g = Graphics.FromImage(heroCrop))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 8, 6, 48, 68);
            g.FillRectangle(Brushes.White, 82, 6, 48, 68);
        }

        var detectedCards = OcrHeroCardExtractor.FindDetectedCardBounds(heroCrop);
        var items = OcrHeroCardExtractor.FindCardItems(heroCrop);

        Assert.Equal(2, detectedCards.Count);
        Assert.Equal(4, items.Count);

        Assert.Equal((0, OcrHeroCardExtractor.HeroCardItemKind.Rank), (items[0].CardIndex, items[0].Kind));
        Assert.Equal((0, OcrHeroCardExtractor.HeroCardItemKind.Suit), (items[1].CardIndex, items[1].Kind));
        Assert.Equal((1, OcrHeroCardExtractor.HeroCardItemKind.Rank), (items[2].CardIndex, items[2].Kind));
        Assert.Equal((1, OcrHeroCardExtractor.HeroCardItemKind.Suit), (items[3].CardIndex, items[3].Kind));

        Assert.True(items[0].Bounds.Left < items[2].Bounds.Left);

        for (var cardIndex = 0; cardIndex < detectedCards.Count; cardIndex++)
        {
            var cardBounds = detectedCards[cardIndex];
            var rankItem = items.Single(item => item.CardIndex == cardIndex && item.Kind == OcrHeroCardExtractor.HeroCardItemKind.Rank);
            var suitItem = items.Single(item => item.CardIndex == cardIndex && item.Kind == OcrHeroCardExtractor.HeroCardItemKind.Suit);

            Assert.True(cardBounds.Contains(rankItem.Bounds));
            Assert.True(cardBounds.Contains(suitItem.Bounds));
        }
    }
}
