using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class FixedLayoutSeatSnapshotExtractorTests
{
    [Fact]
    public void Extract_UsesSeatRegionsSoBottomCenterSeatRemainsHeroWhenOcrOrderIsShuffled()
    {
        var extractor = new FixedLayoutSeatSnapshotExtractor();
        var rawText = """
            [Seat 4] DealerGuy 112 BB dealer
            [Seat 1] HeroBottom 97 BB
            [Seat 6] Cutoff 130 BB
            [Seat 3] SmallBlind 98.50 BB 0.50 BB
            [Seat 5] BigBlind 223.50 BB 1 BB
            [Seat 2] UnderGun 101 BB FOLD
            """;

        var players = extractor.Extract(new CapturedImage(), rawText);
        var hero = Assert.Single(players.Where(player => player.IsHero));

        Assert.Equal(1, hero.Seat);
        Assert.Equal("HeroBottom", hero.Name);
        Assert.Equal("97", hero.Chips);
        Assert.Contains(players, player => player.Seat == 4 && player.Dealer);
    }
}
