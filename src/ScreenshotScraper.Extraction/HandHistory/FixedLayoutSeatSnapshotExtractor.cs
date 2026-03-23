using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class FixedLayoutSeatSnapshotExtractor : ISeatSnapshotExtractor
{
    public IReadOnlyList<SnapshotPlayer> Extract(CapturedImage image, string rawText)
    {
        var seatTemplates = CreateSeatTemplates();
        var entries = SeatEntryRegex().Matches(rawText ?? string.Empty)
            .Cast<Match>()
            .Where(match => match.Success)
            .Select(match => new
            {
                Name = match.Groups["name"].Value.Trim(),
                Chips = NormalizeNumber(match.Groups["chips"].Value),
                Bet = NormalizeNumber(match.Groups["bet"].Value),
                IsFolded = FoldRegex().IsMatch(match.Value)
            })
            .Take(seatTemplates.Count)
            .ToList();

        var players = new List<SnapshotPlayer>(entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            var seatTemplate = seatTemplates[index];
            var entry = entries[index];
            players.Add(new SnapshotPlayer
            {
                Seat = seatTemplate.Seat,
                IsHero = seatTemplate.IsHero,
                Dealer = seatTemplate.Dealer,
                Name = entry.Name,
                Chips = entry.Chips,
                Bet = entry.Bet,
                Win = string.Empty,
                Muck = string.Empty,
                Cashout = string.Empty,
                CashoutFee = string.Empty,
                RakeAmount = string.Empty,
                Position = seatTemplate.Position,
                AppearsFolded = entry.IsFolded,
                HasVisibleCards = seatTemplate.HasVisibleCards
            });
        }

        return players;
    }

    private static List<SnapshotPlayer> CreateSeatTemplates()
    {
        return
        [
            new SnapshotPlayer { Seat = 1, IsHero = true, Name = string.Empty, Chips = string.Empty, Dealer = false, Bet = string.Empty, Win = string.Empty, Muck = string.Empty, Cashout = string.Empty, CashoutFee = string.Empty, RakeAmount = string.Empty, Position = string.Empty, AppearsFolded = false, HasVisibleCards = true },
            new SnapshotPlayer { Seat = 2, IsHero = false, Name = string.Empty, Chips = string.Empty, Dealer = false, Bet = string.Empty, Win = string.Empty, Muck = string.Empty, Cashout = string.Empty, CashoutFee = string.Empty, RakeAmount = string.Empty, Position = string.Empty, AppearsFolded = false, HasVisibleCards = false },
            new SnapshotPlayer { Seat = 3, IsHero = false, Name = string.Empty, Chips = string.Empty, Dealer = false, Bet = string.Empty, Win = string.Empty, Muck = string.Empty, Cashout = string.Empty, CashoutFee = string.Empty, RakeAmount = string.Empty, Position = string.Empty, AppearsFolded = false, HasVisibleCards = false },
            new SnapshotPlayer { Seat = 4, IsHero = false, Name = string.Empty, Chips = string.Empty, Dealer = false, Bet = string.Empty, Win = string.Empty, Muck = string.Empty, Cashout = string.Empty, CashoutFee = string.Empty, RakeAmount = string.Empty, Position = string.Empty, AppearsFolded = false, HasVisibleCards = false },
            new SnapshotPlayer { Seat = 5, IsHero = false, Name = string.Empty, Chips = string.Empty, Dealer = false, Bet = string.Empty, Win = string.Empty, Muck = string.Empty, Cashout = string.Empty, CashoutFee = string.Empty, RakeAmount = string.Empty, Position = string.Empty, AppearsFolded = false, HasVisibleCards = false },
            new SnapshotPlayer { Seat = 6, IsHero = false, Name = string.Empty, Chips = string.Empty, Dealer = false, Bet = string.Empty, Win = string.Empty, Muck = string.Empty, Cashout = string.Empty, CashoutFee = string.Empty, RakeAmount = string.Empty, Position = string.Empty, AppearsFolded = false, HasVisibleCards = false }
        ];
    }

    private static string NormalizeNumber(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(',', '.').Trim();
    }

    [GeneratedRegex(@"(?<name>[A-Za-z0-9_]{3,})\s+(?<chips>\d+(?:[\.,]\d+)?)\s*BB(?:\s+(?<bet>\d+(?:[\.,]\d+)?)\s*BB)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SeatEntryRegex();

    [GeneratedRegex(@"\b(FOLD|MUCK|SIT OUT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FoldRegex();
}
