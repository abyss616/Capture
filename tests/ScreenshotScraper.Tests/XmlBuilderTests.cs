using System.Xml.Linq;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Xml;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class XmlBuilderTests
{
    [Fact]
    public async Task BuildAsync_ReturnsPokerSpecificHandHistoryShape()
    {
        var builder = new XmlBuilder();
        var extractionResult = new ExtractionResult
        {
            Success = true,
            Snapshot = new PartialHandHistorySnapshot
            {
                GameCode = "12127348780",
                StartDate = new DateTime(2026, 3, 22, 17, 6, 28),
                Players =
                [
                    new SnapshotPlayer { Seat = 1, Name = "Hero", Chips = "97", Dealer = false, Bet = string.Empty, Position = "CO", IsHero = true, HasVisibleCards = true, AppearsFolded = false },
                    new SnapshotPlayer { Seat = 2, Name = "Button", Chips = "238.50", Dealer = true, Bet = string.Empty, Position = "BTN", IsHero = false, HasVisibleCards = false, AppearsFolded = false },
                    new SnapshotPlayer { Seat = 3, Name = "SmallBlind", Chips = "98.50", Dealer = false, Bet = "0.50", Position = "SB", IsHero = false, HasVisibleCards = false, AppearsFolded = false },
                    new SnapshotPlayer { Seat = 4, Name = "BigBlind", Chips = "223.50", Dealer = false, Bet = "1", Position = "BB", IsHero = false, HasVisibleCards = false, AppearsFolded = false }
                ],
                Round0Actions =
                [
                    new SnapshotAction { No = 1, Player = "SmallBlind", Type = 1, Sum = "0.50" },
                    new SnapshotAction { No = 2, Player = "BigBlind", Type = 2, Sum = "1" }
                ],
                Round1PocketCards =
                [
                    new SnapshotPocketCards { Player = "Hero", Cards = "SQ CK" },
                    new SnapshotPocketCards { Player = "Button", Cards = "X X" },
                    new SnapshotPocketCards { Player = "SmallBlind", Cards = "X X" },
                    new SnapshotPocketCards { Player = "BigBlind", Cards = "X X" }
                ],
                Round1ObservedActions =
                [
                    new SnapshotAction { No = 1, Player = "UTG", Type = 0, Sum = string.Empty }
                ]
            }
        };

        var result = await builder.BuildAsync(extractionResult);
        var document = XDocument.Parse(result.XmlContent);

        Assert.True(result.Success);
        Assert.Equal("game", document.Root?.Name.LocalName);
        Assert.NotNull(document.Root?.Element("general"));
        Assert.NotNull(document.Root?.Element("general")?.Element("players"));
        Assert.Equal(2, document.Root?.Elements("round").Count());
        Assert.Equal("0", document.Root?.Elements("round").First().Attribute("no")?.Value);
        Assert.Equal("1", document.Root?.Elements("round").Skip(1).First().Attribute("no")?.Value);
        Assert.Equal("1", document.Root?.Element("general")?.Element("players")?.Elements("player").First().Attribute("hero")?.Value);
        Assert.Equal("CO", document.Root?.Element("general")?.Element("players")?.Elements("player").First().Attribute("position")?.Value);
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute => attribute.Name.LocalName is "cashout" or "cashout_fee" or "rakeamount" or "win" or "muck");
        Assert.Empty(document.Descendants().Where(element => element.Name.LocalName is "pendingAction" or "heroDecision" or "metadata" or "confidence" or "snapshot"));
    }
}
