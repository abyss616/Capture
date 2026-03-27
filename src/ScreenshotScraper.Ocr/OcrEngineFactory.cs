using ScreenshotScraper.Core.Interfaces;

namespace ScreenshotScraper.Ocr;

public static class OcrEngineFactory
{
    public static IOcrEngine Create(OcrEngineOptions options)
    {
        return new PaddleOcrEngine(options.Paddle);
    }
}
