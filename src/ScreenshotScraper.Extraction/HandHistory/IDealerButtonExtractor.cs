using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

internal interface IDealerButtonExtractor
{
    int? DetectDealerSeat(CapturedImage image, string rawText, IReadOnlyList<SnapshotPlayer> players);
}
