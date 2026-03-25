namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class TableDetectionResult
{
    public int? DealerSeat { get; init; }

    public double DealerConfidence { get; init; }

    public IReadOnlyList<int> OccupiedSeats { get; init; } = [];

    public IReadOnlyDictionary<int, SeatDetectionDiagnostics> PerSeatDiagnostics { get; init; } =
        new Dictionary<int, SeatDetectionDiagnostics>();

    public bool IsDealerConfident => DealerSeat.HasValue;
}

public sealed class SeatDetectionDiagnostics
{
    public double DealerScore { get; init; }

    public double OccupancyScore { get; init; }

    public bool IsOccupied { get; init; }

    public string Notes { get; init; } = string.Empty;
}
