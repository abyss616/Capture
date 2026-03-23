using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface IDealerButtonExtractor
{
    ExtractedField DetectDealerSeat(CapturedImage image, string rawText, IReadOnlyList<SnapshotPlayer> players);
}
