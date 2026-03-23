using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Ocr;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class UnavailableOcrEngineTests
{
    [Fact]
    public async Task ReadTextAsync_ThrowsMeaningfulException()
    {
        var engine = new UnavailableOcrEngine();

        var exception = await Assert.ThrowsAsync<OcrEngineUnavailableException>(() => engine.ReadTextAsync(new CapturedImage()));

        Assert.Contains("No OCR engine implementation is configured", exception.Message);
    }
}
