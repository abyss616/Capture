using ScreenshotScraper.Core.Interfaces;

namespace ScreenshotScraper.Ocr;

public static class OcrEngineFactory
{
    public static IOcrEngine Create(OcrEngineOptions options)
    {
        if (options.Backend != OcrBackend.Paddle)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Backend), options.Backend, "Only PaddleOCR is supported.");
        }

        return new PaddleOcrEngine(options.Paddle);
    }
}
