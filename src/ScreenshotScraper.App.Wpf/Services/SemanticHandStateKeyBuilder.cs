using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.App.Wpf.Services;

public static class SemanticHandStateKeyBuilder
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Build(TableDetectionResult detection, PartialHandHistorySnapshot? snapshot)
    {
        var canonical = BuildCanonicalState(detection, snapshot);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static string BuildCanonicalState(TableDetectionResult detection, PartialHandHistorySnapshot? snapshot)
    {
        var sections = new List<string>
        {
            $"DEALER_SEAT={Normalize(detection.DealerSeat?.ToString(CultureInfo.InvariantCulture))}",
            $"OCCUPIED_SEATS={string.Join(',', detection.OccupiedSeats.Distinct().OrderBy(seat => seat).Select(seat => seat.ToString(CultureInfo.InvariantCulture)))}"
        };

        var playersBySeat = snapshot?.Players
            .GroupBy(player => player.Seat)
            .ToDictionary(group => group.Key, group => group.First())
            ?? new Dictionary<int, SnapshotPlayer>();

        var seatNumbers = detection.SeatSnapshots.Select(seat => seat.SeatNumber)
            .Concat(playersBySeat.Keys)
            .Distinct()
            .OrderBy(seat => seat)
            .ToList();

        foreach (var seat in seatNumbers)
        {
            var detectionSeat = detection.SeatSnapshots.FirstOrDefault(item => item.SeatNumber == seat);
            playersBySeat.TryGetValue(seat, out var player);

            sections.Add(
                $"SEAT={seat};NAME={Normalize(player?.Name)};OCC={ToFlag(detectionSeat?.IsOccupied)};FOLDED={ToFlag(player?.AppearsFolded)};BET={Normalize(player?.Bet)};VISIBLE={ToFlag(player?.HasVisibleCards)};HERO={ToFlag(player?.IsHero)}");
        }

        if (snapshot is not null)
        {
            var hero = snapshot.Players.FirstOrDefault(player => player.IsHero);
            sections.Add($"HERO_SEAT={Normalize(hero?.Seat.ToString(CultureInfo.InvariantCulture))}");

            var heroCards = snapshot.Round1PocketCards
                .FirstOrDefault(cards => string.Equals(cards.Player, hero?.Name, StringComparison.Ordinal))?.Cards;
            sections.Add($"HERO_HOLE_CARDS={Normalize(heroCards)}");

            sections.Add($"ROUND0_ACTIONS={SerializeActions(snapshot.Round0Actions)}");
            sections.Add($"ROUND1_OBSERVED_ACTIONS={SerializeActions(snapshot.Round1ObservedActions)}");
        }

        return string.Join('|', sections);
    }

    private static string SerializeActions(IReadOnlyList<SnapshotAction> actions)
    {
        if (actions.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", actions
            .OrderBy(action => action.No)
            .ThenBy(action => action.Player, StringComparer.Ordinal)
            .Select(action =>
                $"{action.No}:{Normalize(action.Player)}:{action.Type}:{Normalize(action.Sum)}:{ToFlag(action.Discard)}:{ToFlag(action.Dealt)}"));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var compact = WhitespaceRegex.Replace(trimmed, " ");
        return compact.ToUpperInvariant();
    }

    private static string ToFlag(bool? value)
    {
        return value == true ? "1" : "0";
    }
}
