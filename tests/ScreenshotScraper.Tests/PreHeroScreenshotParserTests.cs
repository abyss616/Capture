using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class PreHeroScreenshotParserTests
{
    [Fact]
    public async Task ParseAsync_AssignsHeroPositionWhenDealerIsDetectedFromSeatRegion()
    {
        var parser = CreateParser(
            """
            [Seat 6] DealerGuy 120 BB dealer
            [Seat 1] HeroBottom 97 BB
            [Seat 2] SmallBlind 80 BB 0.5 BB
            [Seat 3] BigBlind 100 BB 1 BB
            [Seat 4] UnderGun 101 BB FOLD
            [Seat 5] Hijack 145 BB
            Q♠ K♣
            """);

        var snapshot = await parser.ParseAsync(new CapturedImage { WindowTitle = "Gerekek 817752987 | NL Hold'em | €0.01/€0.02" });
        var hero = Assert.Single(snapshot.Players.Where(player => player.IsHero));

        Assert.Equal("HeroBottom", hero.Name);
        Assert.Equal("CO", hero.Position);
        Assert.True(snapshot.HeroPositionField?.IsValid);
        Assert.Equal("CO", snapshot.HeroPositionField?.ParsedValue);
        Assert.Equal("6", snapshot.DealerSeatField?.ParsedValue);
    }

    [Fact]
    public async Task ParseAsync_ReturnsLowConfidenceHeroPositionWhenDealerIsMissing()
    {
        var parser = CreateParser(
            """
            [Seat 1] HeroBottom 97 BB
            [Seat 2] PlayerTwo 80 BB 0.5 BB
            [Seat 3] PlayerThree 100 BB 1 BB
            [Seat 4] PlayerFour 101 BB FOLD
            [Seat 5] PlayerFive 145 BB
            [Seat 6] PlayerSix 111 BB
            Q♠ K♣
            """);

        var snapshot = await parser.ParseAsync(new CapturedImage { WindowTitle = "Table Alpha" });
        var hero = Assert.Single(snapshot.Players.Where(player => player.IsHero));

        Assert.Equal("HeroBottom", hero.Name);
        Assert.False(snapshot.DealerSeatField?.IsValid);
        Assert.False(snapshot.HeroPositionField?.IsValid);
        Assert.Contains("dealer detection", snapshot.HeroPositionField?.Reason);
    }

    private static PreHeroScreenshotParser CreateParser(string rawText)
    {
        return new PreHeroScreenshotParser(
            new StubOcrEngine(rawText),
            new OcrTableHeaderExtractor(),
            new FixedLayoutSeatSnapshotExtractor(),
            new OcrHeroCardExtractor(),
            new HeuristicDealerButtonExtractor(),
            new PreHeroActionInferencer());
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
