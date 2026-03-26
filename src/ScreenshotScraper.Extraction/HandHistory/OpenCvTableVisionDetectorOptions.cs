namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class OpenCvTableVisionDetectorOptions
{
    // Legacy threshold kept for compatibility in diagnostics; the final decision is now relative-ranking based.
    public double DealerTemplateThreshold { get; init; } = 0.58;

    // Minimum margin between best and second-best occupied seat composite score.
    public double DealerMarginThreshold { get; init; } = 0.08;

    // Minimum composite score for a dealer seat to be accepted through ranking.
    public double DealerSeatMinimumScore { get; init; } = 0.42;

    // Minimum score for yellow/circle evidence to count as "strong".
    public double StrongYellowCircularEvidenceThreshold { get; init; } = 0.55;

    // Allow assigning dealer to an unoccupied seat only when this very high score is exceeded.
    public double OverwhelmingUnoccupiedDealerScore { get; init; } = 0.92;

    // HSV yellow segmentation bounds tuned for the current skin.
    public int DealerYellowHueMin { get; init; } = 16;
    public int DealerYellowHueMax { get; init; } = 42;
    public int DealerYellowSaturationMin { get; init; } = 80;
    public int DealerYellowValueMin { get; init; } = 90;

    // Dealer contour quality gates.
    public double DealerMinContourAreaRatio { get; init; } = 0.01;
    public double DealerMaxContourAreaRatio { get; init; } = 0.45;

    // Composite score weights.
    public double DealerYellowWeight { get; init; } = 0.45;
    public double DealerCircularityWeight { get; init; } = 0.35;
    public double DealerTemplateWeight { get; init; } = 0.20;

    public double OccupancyStdDevThreshold { get; init; } = 28;

    public double OccupancyEdgeRatioThreshold { get; init; } = 0.04;

    public bool EnableDebugArtifacts { get; init; }

    public string DebugOutputDirectory { get; init; } = Path.Combine("debug", "output");
}
