using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class DataExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsPokerSpecificFields()
    {
        var extractor = CreateExtractor(
            """
            Table Alpha ID: 12127348780 22-03-2026 17:06:28
            [Seat 6] DealerGuy 238.50 BB dealer
            [Seat 1] Hero 97 BB
            [Seat 2] SmallBlind 98.50 BB 0.50 BB
            [Seat 3] BigBlind 223.50 BB 1 BB
            [Seat 4] Utg 101 BB FOLD
            [Seat 5] Hijack 145 BB
            Q♠ K♣
            """);

        var image = new CapturedImage
        {
            ImageBytes = [],
            Width = 100,
            Height = 80,
            SourceDescription = "Unit test image",
            WindowTitle = "Poker Table ID: 12127348780"
        };

        var result = await extractor.ExtractAsync(image);

        Assert.True(result.Success);
        Assert.Collection(
            result.Fields.Select(field => field.Name),
            field => Assert.Equal("GameCode", field),
            field => Assert.Equal("StartDate", field),
            field => Assert.Equal("PlayerCount", field),
            field => Assert.Equal("HeroName", field),
            field => Assert.Equal("HeroPocketCards", field),
            field => Assert.Equal("HeroPosition", field),
            field => Assert.Equal("DealerSeat", field),
            field => Assert.Equal("ObservedPreHeroActions", field));
        Assert.DoesNotContain(result.Fields, field => field.Name is "DocumentType" or "ReferenceNumber" or "Amount");
        Assert.Equal("12127348780", result.Fields.Single(field => field.Name == "GameCode").ParsedValue);
        Assert.Equal("Hero", result.Fields.Single(field => field.Name == "HeroName").ParsedValue);
        Assert.Equal("SQ CK", result.Fields.Single(field => field.Name == "HeroPocketCards").ParsedValue);
        Assert.Equal("6", result.Fields.Single(field => field.Name == "PlayerCount").ParsedValue);
        Assert.Equal("CO", result.Fields.Single(field => field.Name == "HeroPosition").ParsedValue);
        Assert.Equal("6", result.Fields.Single(field => field.Name == "DealerSeat").ParsedValue);
        Assert.True(result.Fields.Single(field => field.Name == "GameCode").Confidence > 0.9);
    }

    private static DataExtractor CreateExtractor(string rawText)
    {
        return new DataExtractor(
            new PreHeroScreenshotParser(
                new StubOcrEngine(rawText),
                new OcrTableHeaderExtractor(),
                new FixedLayoutSeatSnapshotExtractor(),
                new OcrHeroCardExtractor(),
                new HeuristicDealerButtonExtractor(),
                new PreHeroActionInferencer()));
    }

    private sealed class StubOcrEngine(string rawText) : IOcrEngine
    {
        public Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(rawText);
        }
    }
}
