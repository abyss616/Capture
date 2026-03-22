namespace ScreenshotScraper.Core.Models;

public sealed class XmlBuildResult
{
    public string XmlContent { get; init; } = string.Empty;

    public bool Success { get; init; }

    public List<string> Errors { get; init; } = [];
}
