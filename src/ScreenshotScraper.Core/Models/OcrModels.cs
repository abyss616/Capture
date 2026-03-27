namespace ScreenshotScraper.Core.Models;

/// <summary>
/// Minimal OCR request metadata that lets parsers describe the ROI/use-case without changing extraction flow.
/// </summary>
public sealed record OcrRequest(
    string RoiType = "generic",
    string Variant = "default",
    bool PreferRecognitionOnly = true,
    string? Language = null);

public sealed record OcrLineResult(string Text, double? Confidence = null);

public sealed record OcrResult(
    string Text,
    string Backend,
    double? Confidence = null,
    string? RawPayload = null,
    IReadOnlyList<OcrLineResult>? Lines = null,
    long? ElapsedMilliseconds = null);
