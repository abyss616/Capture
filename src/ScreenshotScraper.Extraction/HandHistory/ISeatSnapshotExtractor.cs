using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface ISeatSnapshotExtractor
{
    IReadOnlyList<SnapshotPlayer> Extract(CapturedImage image, string rawText);
}
