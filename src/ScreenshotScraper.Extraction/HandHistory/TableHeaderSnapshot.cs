using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class TableHeaderSnapshot
{
    public string? GameCode { get; init; }

    public DateTime? StartDate { get; init; }

    public ExtractedField? GameCodeField { get; init; }
}
