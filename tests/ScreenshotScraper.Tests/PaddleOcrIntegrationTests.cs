using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Ocr;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class PaddleOcrIntegrationTests
{
    [Fact]
    public void OcrEngineFactory_CreatesConfiguredBackend()
    {
        var paddle = OcrEngineFactory.Create(new OcrEngineOptions
        {
            Backend = OcrBackend.Paddle,
            Paddle = new PaddleOcrOptions()
        });

        Assert.IsType<PaddleOcrEngine>(paddle);
    }

    [Fact]
    public async Task PaddleOcrEngine_ParsesWorkerResponse()
    {
        var transport = new StubTransport("""{"ok":true,"text":"jkl102","confidence":0.91,"lines":[{"text":"jkl102","confidence":0.91}]}""");
        var engine = new PaddleOcrEngine(new PaddleOcrOptions(), transport);

        var result = await engine.ReadAsync(new CapturedImage { ImageBytes = [1, 2, 3] }, new OcrRequest("name", "raw"));

        Assert.Equal("jkl102", result.Text);
        Assert.Equal("paddle", result.Backend);
        Assert.Equal(0.91, result.Confidence, 3);
        Assert.NotEmpty(result.Lines!);
    }

    [Fact]
    public async Task PaddleOcrEngine_ConvertsTimeoutIntoActionableException()
    {
        var transport = new TimeoutTransport();
        var engine = new PaddleOcrEngine(new PaddleOcrOptions { TimeoutMilliseconds = 10 }, transport);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            engine.ReadAsync(new CapturedImage { ImageBytes = [1] }, new OcrRequest("stack", "preprocessed")));
    }

    private sealed class StubTransport(string response) : IPaddleOcrTransport
    {
        public Task<string> InvokeAsync(string requestJson, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TimeoutTransport : IPaddleOcrTransport
    {
        public Task<string> InvokeAsync(string requestJson, CancellationToken cancellationToken)
        {
            throw new TimeoutException("simulated timeout");
        }

        public void Dispose()
        {
        }
    }
}
