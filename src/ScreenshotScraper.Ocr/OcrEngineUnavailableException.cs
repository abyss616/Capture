namespace ScreenshotScraper.Ocr;

/// <summary>
/// Raised when the application is configured without a concrete OCR backend.
/// </summary>
public sealed class OcrEngineUnavailableException : InvalidOperationException
{
    public OcrEngineUnavailableException()
        : base("No OCR engine implementation is configured. Register a concrete IOcrEngine adapter to enable poker screenshot extraction.")
    {
    }

    public OcrEngineUnavailableException(string message)
        : base(message)
    {
    }

    public OcrEngineUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
