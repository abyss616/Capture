using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction;

/// <summary>
/// Placeholder extraction service. Future implementations can combine OCR text and image analysis.
/// </summary>
public sealed class DataExtractor : IDataExtractor
{
    private readonly IOcrEngine _ocrEngine;

    public DataExtractor(IOcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
    }

    public async Task<ExtractionResult> ExtractAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawText = await _ocrEngine.ReadTextAsync(image, cancellationToken).ConfigureAwait(false);

        return new ExtractionResult
        {
            Success = false,
            Fields =
            [
                new ExtractedField
                {
                    Name = "DocumentType",
                    RawText = rawText,
                    ParsedValue = null,
                    IsValid = false,
                    Error = "Document type extraction is not implemented yet."
                },
                new ExtractedField
                {
                    Name = "ReferenceNumber",
                    RawText = rawText,
                    ParsedValue = null,
                    IsValid = false,
                    Error = "Reference number extraction is not implemented yet."
                },
                new ExtractedField
                {
                    Name = "Amount",
                    RawText = rawText,
                    ParsedValue = null,
                    IsValid = false,
                    Error = "Amount extraction is not implemented yet."
                }
            ],
            Errors =
            [
                "Placeholder extraction result generated. Real extraction rules are still pending."
            ]
        };
    }
}
