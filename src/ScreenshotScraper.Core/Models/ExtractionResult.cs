namespace ScreenshotScraper.Core.Models;

public sealed class ExtractionResult
{
    public List<ExtractedField> Fields { get; init; } = [];

    public bool Success { get; init; }

    public List<string> Errors { get; init; } = [];
}
