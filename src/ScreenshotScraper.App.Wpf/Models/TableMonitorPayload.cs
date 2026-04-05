using System.Text.Json.Serialization;

namespace ScreenshotScraper.App.Wpf.Models;

public sealed class TableMonitorPayload
{
    [JsonPropertyName("byte64")]
    public string[] Byte64 { get; init; } = [];

    [JsonPropertyName("newGame")]
    public bool NewGame { get; init; }
}
