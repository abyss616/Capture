using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

/// <summary>
/// Maps occupied seats into dealer-relative six-max order.
/// Position indexes are numeric only:
/// dealer=4, small blind=5, big blind=6, then preceding seats=3,2,1.
/// </summary>
public static class SixMaxPositionMapper
{
    public static IReadOnlyList<SnapshotPlayer> OrderPreflopActors(IReadOnlyList<SnapshotPlayer> players)
    {
        var dealerSeat = players.FirstOrDefault(player => player.Dealer)?.Seat;
        if (!dealerSeat.HasValue)
        {
            return players.OrderBy(player => player.Seat).ToList();
        }

        return players
            .OrderBy(player => GetPreflopActionRank(dealerSeat.Value, player.Seat))
            .ThenBy(player => player.Seat)
            .ToList();
    }

    public static IReadOnlyList<SnapshotPlayer> OrderDealerFirst(IReadOnlyList<SnapshotPlayer> players, int? dealerSeat)
    {
        if (!dealerSeat.HasValue)
        {
            return players.OrderBy(player => player.Seat).ToList();
        }

        return players
            .OrderBy(player => GetClockwiseDistance(dealerSeat.Value, player.Seat))
            .ThenBy(player => player.Seat)
            .ToList();
    }

    private static int GetPreflopActionRank(int dealerSeat, int seat)
    {
        var clockwiseDistance = GetClockwiseDistance(dealerSeat, seat);
        return clockwiseDistance switch
        {
            3 => 0, // Position 1
            4 => 1, // Position 2
            5 => 2, // Position 3
            0 => 3, // Position 4 (dealer)
            1 => 4, // Position 5 (small blind)
            2 => 5, // Position 6 (big blind)
            _ => int.MaxValue
        };
    }

    private static int GetClockwiseDistance(int dealerSeat, int targetSeat)
    {
        return (targetSeat - dealerSeat + 6) % 6;
    }
}
