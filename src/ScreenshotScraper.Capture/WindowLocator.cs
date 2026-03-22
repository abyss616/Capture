using System.Diagnostics;
using System.Text;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Capture;

public sealed class WindowLocator : IWindowLocator
{
    public IReadOnlyList<WindowInfo> ListWindows(WindowSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var foregroundWindow = Win32NativeMethods.GetForegroundWindow();
        var windows = new List<WindowInfo>();

        Win32NativeMethods.EnumWindows((handle, _) =>
        {
            windows.Add(CreateWindowInfo(handle, foregroundWindow));
            return true;
        }, nint.Zero);

        return windows
            .Where(window => WindowCandidateSelector.IsProcessMatch(window.ProcessName, options.ProcessName))
            .OrderByDescending(window => window.Area)
            .ThenBy(window => window.Handle)
            .ToList();
    }

    public WindowCaptureTarget FindBestMatch(WindowSearchOptions options)
    {
        var windows = ListWindows(options);
        return WindowCandidateSelector.SelectBestCandidate(windows, options);
    }

    private static WindowInfo CreateWindowInfo(nint handle, nint foregroundWindow)
    {
        Win32NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        var processName = string.Empty;
        try
        {
            if (processId != 0)
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
        }
        catch
        {
            processName = string.Empty;
        }

        var title = GetWindowTitle(handle);
        var isVisible = Win32NativeMethods.IsWindowVisible(handle);
        var isMinimized = Win32NativeMethods.IsIconic(handle);
        var hasRect = Win32NativeMethods.GetWindowRect(handle, out var rect);
        var owner = Win32NativeMethods.GetWindow(handle, Win32NativeMethods.GwOwner);
        var exStyle = Win32NativeMethods.GetWindowLongPtr(handle, Win32NativeMethods.GwlExstyle).ToInt64();

        return new WindowInfo
        {
            Handle = handle,
            Title = title,
            ProcessId = (int)processId,
            ProcessName = processName,
            Left = hasRect ? rect.Left : 0,
            Top = hasRect ? rect.Top : 0,
            Width = hasRect ? Math.Max(0, rect.Width) : 0,
            Height = hasRect ? Math.Max(0, rect.Height) : 0,
            IsVisible = isVisible,
            IsMinimized = isMinimized,
            IsForeground = handle == foregroundWindow,
            IsOwnedWindow = owner != nint.Zero,
            IsToolWindow = (exStyle & Win32NativeMethods.WsExToolwindow) == Win32NativeMethods.WsExToolwindow
        };
    }

    private static string GetWindowTitle(nint handle)
    {
        var length = Win32NativeMethods.GetWindowTextLengthW(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = Win32NativeMethods.GetWindowTextW(handle, builder, builder.Capacity);
        return builder.ToString();
    }
}
