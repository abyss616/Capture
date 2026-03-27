using ScreenshotScraper.Core.Interfaces;

namespace ScreenshotScraper.Ocr;

public static class OcrEngineFactory
{
    public static IOcrEngine Create(OcrEngineOptions options)
    {
        return options.Backend switch
        {
            OcrBackend.Paddle => new PaddleOcrEngine(options.Paddle),
            OcrBackend.Windows => new WindowsOcrEngine(),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Backend), options.Backend, "Unsupported OCR backend.")
        };
    }
}
