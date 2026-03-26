using System.Diagnostics;
using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class FixedLayoutSeatSnapshotExtractor : ISeatSnapshotExtractor
{
    public IReadOnlyList<SnapshotPlayer> Extract(CapturedImage image, string rawText)
    {
        var seatTemplates = CreateSeatTemplates().ToDictionary(player => player.Seat);
        var seatBlocks = ParseSeatBlocks(rawText);

        if (seatBlocks.Count == 0)
        {
            Debug.WriteLine("[SeatExtract] No seat-local OCR blocks were detected; skipping global order fallback.");
            return [];
        }

        var extractedSeats = new List<SnapshotPlayer>();
        foreach (var (seatNumber, seatText) in seatBlocks.OrderBy(pair => pair.Key))
        {
            if (!seatTemplates.TryGetValue(seatNumber, out var template))
            {
                continue;
            }

            var snapshot = ExtractSeatSnapshotForSeat(template, seatText);
            if (IsOccupiedSeat(snapshot))
            {
                extractedSeats.Add(snapshot);
            }
        }

        return extractedSeats;
    }

    private static SnapshotPlayer ExtractSeatSnapshotForSeat(SnapshotPlayer seatTemplate, string seatText)
    {
        var entry = SeatEntryRegex().Match(seatText);
        var rawName = entry.Success ? entry.Groups["name"].Value.Trim() : ExtractBestNameCandidate(seatText);
        var chips = entry.Success ? NormalizeNumber(entry.Groups["chips"].Value) : string.Empty;
        var bet = entry.Success ? NormalizeNumber(entry.Groups["bet"].Value) : ExtractBetCandidate(seatText);
        var parsedName = IsReliableSeatName(rawName) ? rawName : string.Empty;
        var failureReason = string.Empty;

        if (!string.IsNullOrWhiteSpace(rawName) && parsedName.Length == 0)
        {
            failureReason = $"Rejected suspicious name '{rawName}'.";
        }

        var snapshot = new SnapshotPlayer
        {
            Seat = seatTemplate.Seat,
            IsHero = seatTemplate.IsHero,
            Dealer = DealerRegex().IsMatch(seatText),
            Name = parsedName,
            Chips = chips,
            Bet = bet,
            Win = string.Empty,
            Muck = string.Empty,
            Cashout = string.Empty,
            CashoutFee = string.Empty,
            RakeAmount = string.Empty,
            Position = seatTemplate.Position,
            AppearsFolded = FoldRegex().IsMatch(seatText),
            HasVisibleCards = seatTemplate.HasVisibleCards
        };

        Debug.WriteLine($"[SeatExtract] seat={seatTemplate.Seat}; roi=n/a;text='{SanitizeForLog(seatText)}'; rawName='{rawName}'; parsedName='{snapshot.Name}'; chips='{snapshot.Chips}'; bet='{snapshot.Bet}'; folded={snapshot.AppearsFolded}; failure='{failureReason}'");
        return snapshot;
    }

    private static bool IsOccupiedSeat(SnapshotPlayer player)
    {
        return !string.IsNullOrWhiteSpace(player.Name)
            || !string.IsNullOrWhiteSpace(player.Chips)
            || !string.IsNullOrWhiteSpace(player.Bet)
            || player.IsHero;
    }

    private static bool IsReliableSeatName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        var alphanumeric = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
        if (alphanumeric.Length < 3)
        {
            return false;
        }

        if (alphanumeric.All(char.IsDigit))
        {
            return alphanumeric.Length >= 5;
        }

        return true;
    }

    private static Dictionary<int, string> ParseSeatBlocks(string? rawText)
    {
        var result = new Dictionary<int, List<string>>();
        var currentSeat = 0;

        foreach (var rawLine in (rawText ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var marker = SeatHeaderRegex().Match(line);
            var seatValue = marker.Groups["seat"].Success ? marker.Groups["seat"].Value : marker.Groups["seat2"].Value;
            if (marker.Success && int.TryParse(seatValue, out var seat) && seat is >= 1 and <= 6)
            {
                currentSeat = seat;
                if (!result.TryGetValue(seat, out var lines))
                {
                    lines = [];
                    result[seat] = lines;
                }

                var trailing = marker.Groups["content"].Value.Trim();
                if (trailing.Length > 0)
                {
                    lines.Add(trailing);
                }

                continue;
            }

            if (currentSeat == 0)
            {
                continue;
            }

            result[currentSeat].Add(line);
        }

        return result.ToDictionary(pair => pair.Key, pair => string.Join(Environment.NewLine, pair.Value));
    }

    private static string ExtractBestNameCandidate(string seatText)
    {
        foreach (var line in seatText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = NameOnlyRegex().Match(line.Trim());
            if (candidate.Success)
            {
                return candidate.Groups["name"].Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string ExtractBetCandidate(string seatText)
    {
        var betMatch = BetOnlyRegex().Match(seatText);
        return betMatch.Success ? NormalizeNumber(betMatch.Groups["bet"].Value) : string.Empty;
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

    private static string SanitizeForLog(string value)
    {
        return value.Replace(Environment.NewLine, " | ").Trim();
    }

    [GeneratedRegex(@"(?<name>[A-Za-z0-9_]{3,})\s+(?<chips>\d+(?:[\.,]\d+)?)\s*BB(?:\s+(?<bet>\d+(?:[\.,]\d+)?)\s*BB)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SeatEntryRegex();

    [GeneratedRegex(@"^\s*(?:\[\s*seat\s*(?<seat>[1-6])\s*\]|seat\s*(?<seat2>[1-6])\s*:?)\s*(?<content>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SeatHeaderRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_]{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NameOnlyRegex();

    [GeneratedRegex(@"(?<bet>\d+(?:[\.,]\d+)?)\s*BB", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BetOnlyRegex();

    [GeneratedRegex(@"\bdealer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DealerRegex();

    [GeneratedRegex(@"\b(FOLD|MUCK|SIT OUT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FoldRegex();
}
