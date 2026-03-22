namespace ScreenshotScraper.Core.Models;

public sealed class ProcessingWorkflowResult
{
    public CapturedImage? CapturedImage { get; init; }

    public ExtractionResult? ExtractionResult { get; init; }

    public XmlBuildResult? XmlBuildResult { get; init; }

    public bool Success { get; init; }

    public List<string> Errors { get; init; } = [];
}
