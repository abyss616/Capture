using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Services;
using ScreenshotScraper.Extraction;
using ScreenshotScraper.Extraction.HandHistory;
using ScreenshotScraper.Imaging;
using ScreenshotScraper.Ocr;
using ScreenshotScraper.Xml;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class ProcessingWorkflowServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsResultWithoutThrowing()
    {
        var service = new ProcessingWorkflowService(
            new StubScreenshotService(),
            new ImagePreprocessor(),
            new DataExtractor(new PreHeroScreenshotParser(new DummyOcrEngine())),
            new XmlBuilder());

        var result = await service.RunAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.CapturedImage);
        Assert.NotNull(result.ExtractionResult);
        Assert.NotNull(result.XmlBuildResult);
    }

    private sealed class StubScreenshotService : IScreenshotService
    {
        public Task<CapturedImage> CaptureAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CapturedImage
            {
                ImageBytes = [],
                Width = 640,
                Height = 480,
                SourceDescription = "Test capture",
                ProcessName = "PokerClient",
                WindowTitle = "Practice Table"
            });
        }
    }
}
