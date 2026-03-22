using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Ocr;

/// <summary>
/// Dummy OCR engine used until a real OCR provider is integrated.
/// </summary>
public sealed class DummyOcrEngine : IOcrEngine
{
    public Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("OCR_NOT_IMPLEMENTED");
    }
}
