using System.Drawing;
using System.Drawing.Imaging;
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
            """,
            "Q♠ K♣");

        var snapshot = await parser.ParseAsync(CreatePngImage());
        var hero = Assert.Single(snapshot.Players.Where(player => player.IsHero));

        Assert.Equal("HeroBottom", hero.Name);
        Assert.Equal("CO", hero.Position);
        Assert.True(snapshot.HeroPositionField?.IsValid);
        Assert.Equal("CO", snapshot.HeroPositionField?.ParsedValue);
        Assert.Equal("6", snapshot.DealerSeatField?.ParsedValue);
        Assert.True(hero.HasVisibleCards);
        Assert.Equal("SQ CK", snapshot.Round1PocketCards.Single(cards => cards.Player == "HeroBottom").Cards);
    }

    [Fact]
    public async Task ParseAsync_UsesGenericHeroNameWhenHeroNameIsMissingOrInvalid()
    {
        var parser = CreateParser(
            """
            [Seat 1] 994 98.50 BB 0.50 BB
            [Seat 2] VillainTwo 100 BB 1 BB
            [Seat 3] VillainThree 110 BB
            [Seat 4] VillainFour 120 BB
            [Seat 5] VillainFive 130 BB
            [Seat 6] VillainSix 140 BB dealer
            """);

        var snapshot = await parser.ParseAsync(CreatePngImage());
        var hero = Assert.Single(snapshot.Players.Where(player => player.IsHero));
        var seatOne = snapshot.Players.Single(player => player.Seat == 1);

        Assert.Equal("GenericHeroName", hero.Name);
        Assert.Equal("GenericHeroName", seatOne.Name);
        Assert.DoesNotContain(snapshot.Players.Where(player => !player.IsHero), player => player.Name == "994");
        Assert.False(hero.HasVisibleCards);
        Assert.Contains("fallback GenericHeroName", snapshot.HeroNameField?.Reason);
    }

    [Fact]
    public async Task ParseAsync_DoesNotSetVisibleCardsWhenHeroCardsAreUnreadable()
    {
        var parser = CreateParser(
            """
            [Seat 1] HeroBottom 97 BB
            [Seat 2] VillainTwo 80 BB 0.5 BB
            [Seat 3] VillainThree 100 BB 1 BB
            [Seat 4] VillainFour 101 BB
            [Seat 5] VillainFive 145 BB
            [Seat 6] VillainSix 111 BB dealer
            """,
            "dealer icon only");

        var snapshot = await parser.ParseAsync(CreatePngImage());
        var hero = Assert.Single(snapshot.Players.Where(player => player.IsHero));

        Assert.Equal("HeroBottom", hero.Name);
        Assert.False(hero.HasVisibleCards);
        Assert.All(snapshot.Round1PocketCards, cards => Assert.Equal("X X", cards.Cards));
    }

    [Fact]
    public async Task ParseAsync_DoesNotAssignHeroWhenHeroSeatCannotBeDetected()
    {
        var parser = CreateParser(
            """
            [Seat 2] VillainTwo 80 BB 0.5 BB
            [Seat 3] VillainThree 100 BB 1 BB
            [Seat 4] VillainFour 101 BB
            [Seat 5] VillainFive 145 BB
            [Seat 6] VillainSix 111 BB dealer
            """,
            "A♠ K♣");

        var snapshot = await parser.ParseAsync(CreatePngImage());

        Assert.DoesNotContain(snapshot.Players, player => player.IsHero);
        Assert.False(snapshot.HeroNameField?.IsValid);
        Assert.All(snapshot.Players, player =>
        {
            Assert.False(player.IsHero);
            Assert.False(player.HasVisibleCards);
        });
    }

    private static PreHeroScreenshotParser CreateParser(string fullText, string? heroCardRegionText = null)
    {
        return new PreHeroScreenshotParser(
            new SequenceOcrEngine(fullText, heroCardRegionText ?? string.Empty),
            new OcrTableHeaderExtractor(),
            new FixedLayoutSeatSnapshotExtractor(),
            new OcrHeroCardExtractor(),
            new HeuristicDealerButtonExtractor(),
            new PreHeroActionInferencer());
    }

    private static CapturedImage CreatePngImage()
    {
        using var bitmap = new Bitmap(200, 200, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.DarkGreen);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);

        return new CapturedImage
        {
            ImageBytes = stream.ToArray(),
            Width = bitmap.Width,
            Height = bitmap.Height,
            WindowTitle = "Gerekek 817752987 | NL Hold'em | €0.01/€0.02"
        };
    }

    private sealed class SequenceOcrEngine(string firstResult, string secondResult) : IOcrEngine
    {
        private int _callCount;

        public Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _callCount++;
            return Task.FromResult(_callCount == 1 ? firstResult : secondResult);
        }
    }
}
