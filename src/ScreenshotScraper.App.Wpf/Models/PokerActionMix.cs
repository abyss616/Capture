using System.Text.Json.Serialization;

namespace ScreenshotScraper.App.Wpf.Models;

public sealed class PokerActionMix
{
    [JsonPropertyName("check_percent")]
    public int CheckPercent { get; set; }

    [JsonPropertyName("call_percent")]
    public int CallPercent { get; set; }

    [JsonPropertyName("fold_percent")]
    public int FoldPercent { get; set; }

    [JsonPropertyName("bet_percent")]
    public int BetPercent { get; set; }

    [JsonPropertyName("bet_size_bb")]
    public decimal BetSizeBb { get; set; }

    [JsonPropertyName("summary_decision")]
    public string SummaryDecision { get; set; } = string.Empty;
}
