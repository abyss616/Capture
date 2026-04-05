namespace ScreenshotScraper.App.Wpf.Models;

public sealed class ManualSnapshotPayload
{
    public string Byte64 { get; init; } = string.Empty;

    public bool NewGame { get; init; }
}