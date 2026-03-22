using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

internal interface ISeatSnapshotExtractor
{
    IReadOnlyList<SnapshotPlayer> Extract(CapturedImage image, string rawText);
}
