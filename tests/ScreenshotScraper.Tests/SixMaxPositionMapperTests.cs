using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class SixMaxPositionMapperTests
{
    [Fact]
    public void AssignPositions_MapsClockwiseFromDealer()
    {
        var players = new List<SnapshotPlayer>
        {
            new() { Seat = 1, Name = "Hero" },
            new() { Seat = 2, Name = "Button", Dealer = true },
            new() { Seat = 3, Name = "SmallBlind" },
            new() { Seat = 4, Name = "BigBlind" },
            new() { Seat = 5, Name = "Utg" },
            new() { Seat = 6, Name = "Hijack" }
        };

        var mapped = SixMaxPositionMapper.AssignPositions(players).ToDictionary(player => player.Seat, player => player.Position);

        Assert.Equal("CO", mapped[1]);
        Assert.Equal("BTN", mapped[2]);
        Assert.Equal("SB", mapped[3]);
        Assert.Equal("BB", mapped[4]);
        Assert.Equal("UTG", mapped[5]);
        Assert.Equal("HJ", mapped[6]);
    }
}
