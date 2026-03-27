using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IOcrEngine
{
    Task<OcrResult> ReadAsync(CapturedImage image, OcrRequest request, CancellationToken cancellationToken = default);

    async Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        var result = await ReadAsync(image, new OcrRequest(), cancellationToken).ConfigureAwait(false);
        return result.Text;
    }
}
