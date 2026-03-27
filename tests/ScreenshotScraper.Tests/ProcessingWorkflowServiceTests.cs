using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Services;
using ScreenshotScraper.Extraction;
using ScreenshotScraper.Extraction.HandHistory;
using ScreenshotScraper.Imaging;
using ScreenshotScraper.Xml;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class ProcessingWorkflowServiceTests
{
    [Fact]
    public async Task RunAsync_BuildsPokerXmlFromWorkflow()
    {
        var service = new ProcessingWorkflowService(
            new StubScreenshotService(),
            new ImagePreprocessor(),
            new DataExtractor(
                new PreHeroScreenshotParser(
                    new StubOcrEngine(
                        """
                        Practice Table ID: 12127348780 22-03-2026 17:06:28
                        Hero 97 BB
                        Button 238.50 BB dealer
                        SmallBlind 98.50 BB 0.50 BB
                        BigBlind 223.50 BB 1 BB
                        Utg 101 BB FOLD
                        Hijack 145 BB
                        Q♠ K♣
                        """),
                    new OcrTableHeaderExtractor(),
                    new FixedLayoutSeatSnapshotExtractor(),
                    new OcrHeroCardExtractor(),
                    new OpenCvTableVisionDetector(),
                    new PreHeroActionInferencer())),
            new XmlBuilder());

        var result = await service.RunAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.CapturedImage);
        Assert.NotNull(result.ExtractionResult);
        Assert.NotNull(result.XmlBuildResult);
        Assert.Contains("gamecode=\"12127348780\"", result.XmlBuildResult!.XmlContent);
        Assert.Contains("type=\"Pocket\"", result.XmlBuildResult.XmlContent);
        Assert.DoesNotContain("DocumentType", result.XmlBuildResult.XmlContent);
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

    private sealed class StubOcrEngine(string rawText) : IOcrEngine
    {
        public Task<OcrResult> ReadAsync(CapturedImage image, OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new OcrResult(rawText, "test"));
        }
    }
}
