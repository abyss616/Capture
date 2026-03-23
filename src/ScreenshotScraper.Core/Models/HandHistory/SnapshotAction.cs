namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class SnapshotAction
{
    public int No { get; init; }

    public string Player { get; init; } = string.Empty;

    public int Type { get; init; }

    public string Sum { get; init; } = string.Empty;
}
