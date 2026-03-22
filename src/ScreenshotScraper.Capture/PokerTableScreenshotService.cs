using System.Drawing;
using System.Drawing.Imaging;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Capture;

/// <summary>
/// Captures the currently visible poker table window and returns PNG bytes plus detailed window metadata.
/// </summary>
public sealed class PokerTableScreenshotService : IScreenshotService
{
    private const string CaptureMethodName = "Graphics.CopyFromScreen";

    private readonly IWindowLocator _windowLocator;
    private readonly PokerWindowCaptureOptions _options;

    public PokerTableScreenshotService(IWindowLocator windowLocator, PokerWindowCaptureOptions options)
    {
        _windowLocator = windowLocator;
        _options = options;
    }

    public Task<CapturedImage> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = _windowLocator.FindBestMatch(_options.ToSearchOptions());
        var window = target.Window;

        if (window.Width <= 0 || window.Height <= 0)
        {
            throw new WindowCaptureException($"Window '{window.Title}' has invalid bounds {window.Width}x{window.Height}.");
        }

        try
        {
            using var bitmap = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(window.Left, window.Top, 0, 0, new Size(window.Width, window.Height), CopyPixelOperation.SourceCopy);

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);

            return Task.FromResult(new CapturedImage
            {
                ImageBytes = stream.ToArray(),
                Width = bitmap.Width,
                Height = bitmap.Height,
                CapturedAtUtc = DateTime.UtcNow,
                SourceDescription = "PokerClient visible top-level window",
                WindowTitle = window.Title,
                ProcessName = window.ProcessName,
                WindowLeft = window.Left,
                WindowTop = window.Top,
                WindowWidth = window.Width,
                WindowHeight = window.Height,
                IsVisible = window.IsVisible,
                IsForegroundWindow = window.IsForeground,
                WindowHandle = window.Handle,
                CaptureMethod = CaptureMethodName,
                MonitorDeviceName = TryGetMonitorDeviceName(window.Handle)
            });
        }
        catch (Exception exception)
        {
            throw new WindowCaptureException(
                $"Failed to capture window '{window.Title}' ({window.Handle}) using {CaptureMethodName}.",
                exception);
        }
    }

    private static string? TryGetMonitorDeviceName(nint windowHandle)
    {
        var monitor = Win32NativeMethods.MonitorFromWindow(windowHandle, Win32NativeMethods.MonitorDefaulttonearest);
        if (monitor == nint.Zero)
        {
            return null;
        }

        var monitorInfo = new Win32NativeMethods.MonitorInfoEx
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32NativeMethods.MonitorInfoEx>()
        };

        return Win32NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)
            ? monitorInfo.SzDevice
            : null;
    }
}
