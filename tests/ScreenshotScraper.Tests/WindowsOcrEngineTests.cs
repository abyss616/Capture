using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Ocr;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class WindowsOcrEngineTests
{
    [Fact]
    public async Task ReadTextAsync_OnNonWindowsBuild_ThrowsActionableException()
    {
        var engine = new WindowsOcrEngine();

        var exception = await Assert.ThrowsAsync<OcrEngineUnavailableException>(() => engine.ReadTextAsync(new CapturedImage
        {
            ImageBytes = [1, 2, 3]
        }));

        Assert.Contains("Windows OCR", exception.Message);
    }
}
