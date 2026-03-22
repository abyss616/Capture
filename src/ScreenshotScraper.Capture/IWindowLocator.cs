using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Capture;

public interface IWindowLocator
{
    IReadOnlyList<WindowInfo> ListWindows(WindowSearchOptions options);

    WindowCaptureTarget FindBestMatch(WindowSearchOptions options);
}
