using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class CardNotationFormatterTests
{
    [Theory]
    [InlineData("Q", "♠", "SQ")]
    [InlineData("K", "♣", "CK")]
    [InlineData("10", "♦", "D10")]
    public void TryNormalize_UsesSuitFirstNotation(string rank, string suit, string expected)
    {
        var success = CardNotationFormatter.TryNormalize(rank, suit, out var card);

        Assert.True(success);
        Assert.Equal(expected, card);
    }
}
