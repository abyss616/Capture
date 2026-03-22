using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction;
using ScreenshotScraper.Ocr;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class DataExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsPlaceholderFields()
    {
        var extractor = new DataExtractor(new DummyOcrEngine());
        var image = new CapturedImage
        {
            ImageBytes = [],
            Width = 100,
            Height = 80,
            SourceDescription = "Unit test image"
        };

        var result = await extractor.ExtractAsync(image);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Fields);
        Assert.Contains(result.Fields, field => field.Name == "DocumentType");
    }
}
