namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class PartialHandHistorySnapshot
{
    public string? GameCode { get; init; }

    public DateTime? StartDate { get; init; }

    public List<SnapshotPlayer> Players { get; init; } = [];

    public List<SnapshotAction> Round0Actions { get; init; } = [];

    public List<SnapshotPocketCards> Round1PocketCards { get; init; } = [];

    public List<SnapshotAction> Round1ObservedActions { get; init; } = [];
}
