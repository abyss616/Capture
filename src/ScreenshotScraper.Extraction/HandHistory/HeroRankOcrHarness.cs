using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Ocr;
using System.Drawing;

namespace ScreenshotScraper.Extraction.HandHistory;

/// <summary>
/// Lightweight harness for manually validating rank-only hero OCR against a sample hero-crop image.
/// </summary>
public static class HeroRankOcrHarness
{
    public static async Task<int> RunAsync(
        string sampleHeroCropPath,
        string workerScriptPath,
        string pythonExecutable = "python",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sampleHeroCropPath))
        {
            Console.Error.WriteLine($"Sample image not found: {sampleHeroCropPath}");
            return 2;
        }

        var bytes = await File.ReadAllBytesAsync(sampleHeroCropPath, cancellationToken).ConfigureAwait(false);
        using var bitmap = new Bitmap(new MemoryStream(bytes));

        var image = new CapturedImage
        {
            ImageBytes = bytes,
            Width = bitmap.Width,
            Height = bitmap.Height,
            CapturedAtUtc = DateTime.UtcNow,
            SourceDescription = sampleHeroCropPath
        };

        var options = new OcrEngineOptions
        {
            Paddle = new PaddleOcrOptions
            {
                PythonExecutablePath = pythonExecutable,
                WorkerScriptPath = workerScriptPath,
                Language = "en"
            }
        };

        using var engine = new PaddleOcrEngine(options.Paddle);
        var extractor = new OcrHeroCardExtractor(engine);

        var ranks = await extractor.ExtractHeroCardsAsync(image, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Input: {sampleHeroCropPath}");
        Console.WriteLine($"Detected ranks: {ranks}");
        return string.IsNullOrWhiteSpace(ranks) ? 1 : 0;
    }
}
