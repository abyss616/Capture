using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class HeuristicDealerButtonExtractor : IDealerButtonExtractor
{
    public ExtractedField DetectDealerSeat(CapturedImage image, string rawText, IReadOnlyList<SnapshotPlayer> players)
    {
        var text = rawText ?? string.Empty;
        var marker = DealerSeatRegex().Match(text);
        if (marker.Success && int.TryParse(marker.Groups["seat"].Value, out var markerSeat) && markerSeat is >= 1 and <= 6)
        {
            return BuildDealerField(text, markerSeat, 1.0, "Matched an explicit dealer-seat marker in OCR text.");
        }

        var regionDealer = players.FirstOrDefault(player => player.Dealer && !string.IsNullOrWhiteSpace(player.Name));
        if (regionDealer is not null)
        {
            return BuildDealerField(text, regionDealer.Seat, 0.95, $"Matched dealer text inside seat {regionDealer.Seat}'s OCR region.");
        }

        var indexedPlayer = players
            .Select(player => new { player.Seat, player.Name })
            .FirstOrDefault(player => !string.IsNullOrWhiteSpace(player.Name) && DealerNameRegex(player.Name!).IsMatch(text));
        if (indexedPlayer is not null)
        {
            return BuildDealerField(text, indexedPlayer.Seat, 0.7, $"Matched the player name '{indexedPlayer.Name}' near a dealer keyword.");
        }

        return new ExtractedField
        {
            Name = "DealerSeat",
            RawText = text,
            ParsedValue = null,
            IsValid = false,
            Error = "Dealer button was not confidently detected.",
            Confidence = 0,
            Reason = "No explicit dealer marker or reliable seat-local dealer text was found."
        };
    }

    private static ExtractedField BuildDealerField(string rawText, int seat, double confidence, string reason)
    {
        return new ExtractedField
        {
            Name = "DealerSeat",
            RawText = rawText,
            ParsedValue = seat.ToString(),
            IsValid = true,
            Error = null,
            Confidence = confidence,
            Reason = reason
        };
    }

    [GeneratedRegex(@"DEALER\s*SEAT\s*(?<seat>[1-6])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DealerSeatRegex();

    private static Regex DealerNameRegex(string playerName)
    {
        return new Regex($@"\b{Regex.Escape(playerName)}\b.*\bdealer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
