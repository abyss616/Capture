using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class PartialHandHistorySnapshot
{
    public string? GameCode { get; init; }

    public DateTime? StartDate { get; init; }

    public List<SnapshotPlayer> Players { get; init; } = [];

    public List<SnapshotAction> Round0Actions { get; init; } = [];

    public List<SnapshotPocketCards> Round1PocketCards { get; init; } = [];

    public List<SnapshotAction> Round1ObservedActions { get; init; } = [];

    public ExtractedField? GameCodeField { get; init; }

    public ExtractedField? HeroNameField { get; init; }

    public ExtractedField? DealerSeatField { get; init; }

    public ExtractedField? HeroPositionField { get; init; }

    public string SeatLocalOcrDiagnostics { get; init; } = string.Empty;

    public IReadOnlyList<SeatDebugArtifact> SeatLocalOcrDebugArtifacts { get; init; } = [];
}
