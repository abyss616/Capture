using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

internal sealed partial class HeuristicDealerButtonExtractor : IDealerButtonExtractor
{
    public int? DetectDealerSeat(CapturedImage image, string rawText, IReadOnlyList<SnapshotPlayer> players)
    {
        var marker = DealerSeatRegex().Match(rawText ?? string.Empty);
        if (marker.Success && int.TryParse(marker.Groups["seat"].Value, out var seat) && seat is >= 1 and <= 6)
        {
            return seat;
        }

        return players.FirstOrDefault(player => player.Dealer)?.Seat;
    }

    [GeneratedRegex(@"DEALER\s*SEAT\s*(?<seat>[1-6])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DealerSeatRegex();
}
