namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class SnapshotPlayer
{
    public int Seat { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Chips { get; init; }

    public bool Dealer { get; init; }

    public string? Bet { get; init; }

    public string? Win { get; init; }

    public string? Muck { get; init; }

    public string? Cashout { get; init; }

    public string? CashoutFee { get; init; }

    public string? RakeAmount { get; init; }

    public string? Position { get; init; }

    public bool IsHero { get; init; }

    public bool AppearsFolded { get; init; }

    public bool HasVisibleCards { get; init; }
}
