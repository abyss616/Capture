using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface ITableHeaderExtractor
{
    TableHeaderSnapshot Extract(CapturedImage image, string rawText);
}
