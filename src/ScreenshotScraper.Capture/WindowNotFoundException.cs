namespace ScreenshotScraper.Capture;

public sealed class WindowNotFoundException : WindowCaptureException
{
    public WindowNotFoundException(string message)
        : base(message)
    {
    }
}
