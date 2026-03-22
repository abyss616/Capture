using ScreenshotScraper.Capture;
using ScreenshotScraper.Core.Services;
using ScreenshotScraper.Extraction;
using ScreenshotScraper.Imaging;
using ScreenshotScraper.Ocr;
using ScreenshotScraper.Xml;

namespace ScreenshotScraper.Tests;

public sealed class ProcessingWorkflowServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsResultWithoutThrowing()
    {
        var service = new ProcessingWorkflowService(
            new ScreenshotService(),
            new ImagePreprocessor(),
            new DataExtractor(new DummyOcrEngine()),
            new XmlBuilder());

        var result = await service.RunAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.CapturedImage);
        Assert.NotNull(result.ExtractionResult);
        Assert.NotNull(result.XmlBuildResult);
    }
}
