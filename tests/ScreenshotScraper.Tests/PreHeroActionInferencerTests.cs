using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class PreHeroActionInferencerTests
{
    [Fact]
    public void Infer_IncludesOnlyConfidentFoldsBeforeHeroAndSequentialNumbers()
    {
        var players = SixMaxPositionMapper.AssignPositions(
        [
            new SnapshotPlayer { Seat = 1, Name = "Hero", IsHero = true },
            new SnapshotPlayer { Seat = 2, Name = "Button", Dealer = true },
            new SnapshotPlayer { Seat = 3, Name = "SmallBlind", Bet = "0.50" },
            new SnapshotPlayer { Seat = 4, Name = "BigBlind", Bet = "1" },
            new SnapshotPlayer { Seat = 5, Name = "Utg", AppearsFolded = true },
            new SnapshotPlayer { Seat = 6, Name = "Hijack", AppearsFolded = false }
        ]);

        var inferencer = new PreHeroActionInferencer();
        var (round0Actions, round1Actions) = inferencer.Infer(players);

        Assert.Collection(
            round0Actions,
            action =>
            {
                Assert.Equal(1, action.No);
                Assert.Equal("SmallBlind", action.Player);
                Assert.Equal(1, action.Type);
                Assert.Equal("0.50", action.Sum);
            },
            action =>
            {
                Assert.Equal(2, action.No);
                Assert.Equal("BigBlind", action.Player);
                Assert.Equal(2, action.Type);
                Assert.Equal("1", action.Sum);
            });

        var fold = Assert.Single(round1Actions);
        Assert.Equal(1, fold.No);
        Assert.Equal("Utg", fold.Player);
        Assert.Equal(0, fold.Type);
        Assert.Equal(string.Empty, fold.Sum);
    }
}
