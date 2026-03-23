namespace ScreenshotScraper.Core.Models;

public sealed class ExtractedField
{
    public string Name { get; init; } = string.Empty;

    public string? RawText { get; init; }

    public string? ParsedValue { get; init; }

    public bool IsValid { get; init; }

    public string? Error { get; init; }

    public double Confidence { get; init; }

    public string? Reason { get; init; }
}
