using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

internal interface ITableHeaderExtractor
{
    TableHeaderSnapshot Extract(CapturedImage image, string rawText);
}
