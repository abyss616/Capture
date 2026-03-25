namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class OpenCvTableVisionDetectorOptions
{
    public double DealerTemplateThreshold { get; init; } = 0.58;

    public double DealerMarginThreshold { get; init; } = 0.05;

    public double OccupancyStdDevThreshold { get; init; } = 28;

    public double OccupancyEdgeRatioThreshold { get; init; } = 0.04;

    public bool EnableDebugArtifacts { get; init; }

    public string DebugOutputDirectory { get; init; } = Path.Combine("debug", "output");
}
