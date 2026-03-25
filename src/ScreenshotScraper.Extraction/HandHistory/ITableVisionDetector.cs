using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface ITableVisionDetector
{
    TableDetectionResult Detect(CapturedImage image, IReadOnlyList<SnapshotPlayer> players);
}
