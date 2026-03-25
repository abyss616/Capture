namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class TableDetectionResult
{
    public int? DealerSeat { get; init; }

    public bool DealerDetected { get; init; }

    public double DealerConfidence { get; init; }

    public IReadOnlyList<int> OccupiedSeats { get; init; } = [];

    public IReadOnlyList<SeatSnapshot> SeatSnapshots { get; init; } = [];

    public IReadOnlyDictionary<int, SeatDetectionDiagnostics> PerSeatDiagnostics { get; init; } =
        new Dictionary<int, SeatDetectionDiagnostics>();

    public bool IsDealerConfident => DealerDetected && DealerSeat.HasValue;
}

public sealed class SeatSnapshot
{
    public required int SeatNumber { get; init; }

    public required bool IsOccupied { get; init; }

    public required double DealerScore { get; init; }

    public required double OccupancyScore { get; init; }

    public bool DealerThresholdPassed { get; init; }

    public string Diagnostics { get; init; } = string.Empty;
}

public sealed class SeatDetectionDiagnostics
{
    public double DealerScore { get; init; }

    public double OccupancyScore { get; init; }

    public bool IsOccupied { get; init; }

    public bool DealerThresholdPassed { get; init; }

    public string Notes { get; init; } = string.Empty;
}
