using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Ocr;

/// <summary>
/// Transparent fallback used when no concrete OCR provider package is available in the repository.
/// </summary>
public sealed class UnavailableOcrEngine : IOcrEngine
{
    public Task<OcrResult> ReadAsync(CapturedImage image, OcrRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new OcrEngineUnavailableException();
    }
}
