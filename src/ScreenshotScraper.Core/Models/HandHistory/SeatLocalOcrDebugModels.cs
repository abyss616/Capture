using System.Drawing;

namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class SeatOcrVariantDebugArtifact
{
    public required int SeatNumber { get; init; }

    public required string FieldType { get; init; }

    public required Rectangle RawRoiRect { get; init; }

    public required string VariantName { get; init; }

    public required string OcrInputImagePath { get; init; }

    public required string OcrBackend { get; init; }

    public required string OcrRawText { get; init; }

    public double? Confidence { get; init; }

    public bool Selected { get; init; }

    public string? ParsedValue { get; init; }

    public string? RejectionReason { get; init; }
}

public sealed class SeatFieldOcrDebugResult
{
    public required int SeatNumber { get; init; }

    public required string FieldType { get; init; }

    public required Rectangle RawRoiRect { get; init; }

    public required string RawRoiImagePath { get; init; }

    public required string SelectedVariantName { get; init; }

    public required string SelectedOcrInputImagePath { get; init; }

    public required string ParsedValue { get; init; }

    public required string ParseRejectionReason { get; init; }

    public required IReadOnlyList<SeatOcrVariantDebugArtifact> Variants { get; init; }
}

public sealed class SeatDebugArtifact
{
    public required int SeatNumber { get; init; }

    public required Rectangle SeatFullRect { get; init; }

    public required string SeatFullImagePath { get; init; }

    public required IReadOnlyList<SeatFieldOcrDebugResult> Fields { get; init; }
}
