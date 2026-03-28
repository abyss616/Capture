using System.Xml.Linq;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Xml;

/// <summary>
/// Builds conservative hand-history XML using the original game/general/round schema shape.
/// </summary>
public sealed class XmlBuilder : IXmlBuilder
{
    public Task<XmlBuildResult> BuildAsync(ExtractionResult extractionResult, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (extractionResult.Snapshot is null)
        {
            return Task.FromResult(new XmlBuildResult
            {
                Success = false,
                Errors = ["Hand-history snapshot was not available for XML generation."],
                XmlContent = string.Empty
            });
        }

        var document = new XDocument(BuildGameElement(extractionResult.Snapshot));

        return Task.FromResult(new XmlBuildResult
        {
            XmlContent = document.ToString(),
            Success = true,
            Errors = []
        });
    }

    private static XElement BuildGameElement(PartialHandHistorySnapshot snapshot)
    {
        var orderedPlayers = OrderPlayersForXml(snapshot.Players);

        return new XElement(
            "game",
            new XAttribute("gamecode", snapshot.GameCode ?? string.Empty),
            new XElement(
                "general",
                new XElement("startdate", snapshot.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
                new XElement(
                    "players",
                    orderedPlayers.Select(BuildPlayerElement))),
            new XElement(
                "round",
                new XAttribute("no", "0"),
                snapshot.Round0Actions.Select(BuildActionElement)),
            new XElement(
                "round",
                new XAttribute("no", "1"),
                snapshot.Round1PocketCards.Select(BuildPocketCardElement),
                snapshot.Round1ObservedActions.Select(BuildActionElement)));
    }

    private static IReadOnlyList<SnapshotPlayer> OrderPlayersForXml(IReadOnlyList<SnapshotPlayer> players)
    {
        if (players.Count == 0)
        {
            return players;
        }

        var dealerSeat = players.FirstOrDefault(player => player.Dealer)?.Seat;
        if (!dealerSeat.HasValue)
        {
            return players;
        }

        return players
            .OrderBy(player => (player.Seat - dealerSeat.Value + 6) % 6)
            .ThenBy(player => player.Seat)
            .ToList();
    }

    private static XElement BuildPlayerElement(SnapshotPlayer player)
    {
        return new XElement(
            "player",
            new XAttribute("seat", player.Seat),
            new XAttribute("name", player.Name),
            new XAttribute("chips", player.Chips ?? string.Empty),
            new XAttribute("dealer", player.Dealer ? "1" : "0"),
            new XAttribute("bet", player.Bet ?? string.Empty),
            new XAttribute("position", player.Position ?? string.Empty),
            new XAttribute("hero", player.IsHero ? "1" : "0"),
            new XAttribute("visiblecards", player.HasVisibleCards ? "1" : "0"),
            new XAttribute("folded", player.AppearsFolded ? "1" : "0"));
    }

    private static XElement BuildActionElement(SnapshotAction action)
    {
        return new XElement(
            "action",
            new XAttribute("no", action.No),
            new XAttribute("player", action.Player),
            new XAttribute("type", action.Type),
            new XAttribute("sum", action.Sum));
    }

    private static XElement BuildPocketCardElement(SnapshotPocketCards pocketCards)
    {
        return new XElement(
            "cards",
            new XAttribute("type", "Pocket"),
            new XAttribute("player", pocketCards.Player),
            pocketCards.Cards);
    }
}
