namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class PreHeroHandHistoryXmlWorkflowResult
{
    public CapturedImage? PreparedImage { get; init; }

    public ExtractionResult? ExtractionResult { get; init; }

    public XmlBuildResult? XmlBuildResult { get; init; }

    public bool Success { get; init; }

    public List<string> Errors { get; init; } = [];
}
