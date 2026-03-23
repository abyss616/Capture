using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class OcrTableHeaderExtractorTests
{
    [Fact]
    public void Extract_PrefersWindowTitleGameCodeForPipeDelimitedTitle()
    {
        var extractor = new OcrTableHeaderExtractor();
        var image = new CapturedImage
        {
            WindowTitle = "Gerekek 817752987 | NL Hold'em | €0.01/€0.02"
        };

        var snapshot = extractor.Extract(image, "ID: 999999\n22-03-2026 17:06:28");

        Assert.Equal("817752987", snapshot.GameCode);
        Assert.NotNull(snapshot.GameCodeField);
        Assert.Equal("817752987", snapshot.GameCodeField?.ParsedValue);
        Assert.Equal(1.0, snapshot.GameCodeField?.Confidence);
        Assert.Contains("window title", snapshot.GameCodeField?.Reason);
    }
}
