using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class HeuristicDealerButtonExtractor : IDealerButtonExtractor
{
    public int? DetectDealerSeat(CapturedImage image, string rawText, IReadOnlyList<SnapshotPlayer> players)
    {
        var text = rawText ?? string.Empty;
        var marker = DealerSeatRegex().Match(text);
        if (marker.Success && int.TryParse(marker.Groups["seat"].Value, out var seat) && seat is >= 1 and <= 6)
        {
            return seat;
        }

        var indexedPlayer = players
            .Select(player => new { player.Seat, player.Name })
            .FirstOrDefault(player => !string.IsNullOrWhiteSpace(player.Name) && DealerNameRegex(player.Name).IsMatch(text));
        if (indexedPlayer is not null)
        {
            return indexedPlayer.Seat;
        }

        return players.FirstOrDefault(player => player.Dealer)?.Seat;
    }

    [GeneratedRegex(@"DEALER\s*SEAT\s*(?<seat>[1-6])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DealerSeatRegex();

    private static Regex DealerNameRegex(string playerName)
    {
        return new Regex($@"\b{Regex.Escape(playerName)}\b.*\bdealer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
