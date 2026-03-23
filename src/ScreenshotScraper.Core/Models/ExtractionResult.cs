using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Core.Models;

public sealed class ExtractionResult
{
    public List<ExtractedField> Fields { get; init; } = [];

    public PartialHandHistorySnapshot? Snapshot { get; init; }

    public bool Success { get; init; }

    public List<string> Errors { get; init; } = [];
}
