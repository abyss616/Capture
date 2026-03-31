namespace ScreenshotScraper.Core.Models.HandHistory;

public sealed class PlayingCard
{
    public string Rank { get; init; } = string.Empty;

    public string Suit { get; init; } = string.Empty;

    public bool IsComplete => !string.IsNullOrWhiteSpace(Rank) && !string.IsNullOrWhiteSpace(Suit);

    public override string ToString() => $"{Rank}{Suit}";
}

public sealed class Cards
{
    public List<PlayingCard> Items { get; init; } = [];

    public bool IsComplete => Items.Count == 2 && Items.All(card => card.IsComplete);

    public override string ToString()
    {
        if (Items.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(' ', Items.Select(card => card.ToString()));
    }
}
