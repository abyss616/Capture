using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

/// <summary>
/// Stable seat numbering for the visible 6-max layout:
/// 1=bottom-center(hero), 2=bottom-left, 3=top-left, 4=top-center, 5=top-right, 6=bottom-right.
/// Clockwise table order follows the same numeric order.
/// </summary>
public static class SixMaxPositionMapper
{
    private static readonly string[] PositionOrder = ["BTN", "SB", "BB", "UTG", "HJ", "CO"];
    private static readonly string[] PreflopActionOrder = ["UTG", "HJ", "CO", "BTN", "SB", "BB"];

    public static IReadOnlyList<SnapshotPlayer> AssignPositions(IReadOnlyList<SnapshotPlayer> players)
    {
        var dealerSeat = players.FirstOrDefault(player => player.Dealer)?.Seat;
        if (dealerSeat is null)
        {
            return players;
        }

        var mappedPlayers = new List<SnapshotPlayer>(players.Count);
        foreach (var player in players)
        {
            var offset = GetClockwiseDistance(dealerSeat.Value, player.Seat);
            var position = offset >= 0 && offset < PositionOrder.Length ? PositionOrder[offset] : string.Empty;
            mappedPlayers.Add(Clone(player, position));
        }

        return mappedPlayers;
    }

    public static IReadOnlyList<SnapshotPlayer> OrderPreflopActors(IReadOnlyList<SnapshotPlayer> players)
    {
        return players
            .OrderBy(player => Array.IndexOf(PreflopActionOrder, player.Position ?? string.Empty))
            .ThenBy(player => player.Seat)
            .ToList();
    }

    private static int GetClockwiseDistance(int dealerSeat, int targetSeat)
    {
        return (targetSeat - dealerSeat + 6) % 6;
    }

    private static SnapshotPlayer Clone(SnapshotPlayer player, string position)
    {
        return new SnapshotPlayer
        {
            Seat = player.Seat,
            Name = player.Name,
            Chips = player.Chips,
            Dealer = player.Dealer,
            Bet = player.Bet,
            Win = player.Win,
            Muck = player.Muck,
            Cashout = player.Cashout,
            CashoutFee = player.CashoutFee,
            RakeAmount = player.RakeAmount,
            Position = position,
            IsHero = player.IsHero,
            AppearsFolded = player.AppearsFolded,
            HasVisibleCards = player.HasVisibleCards
        };
    }
}
