using ScreenshotScraper.Capture;
using ScreenshotScraper.Core.Models;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class WindowCandidateSelectorTests
{
    [Fact]
    public void FindBestMatch_PrefersHighestTitleMatchThenLargestArea()
    {
        var options = new WindowSearchOptions
        {
            ProcessName = "PokerClient",
            WindowTitleContains = "Table"
        };

        var windows = new[]
        {
            CreateWindow(handle: 2, title: "Poker Table 1", width: 1200, height: 800),
            CreateWindow(handle: 1, title: "Lobby", width: 1800, height: 1000),
            CreateWindow(handle: 3, title: "Table", width: 900, height: 700)
        };

        var selected = WindowCandidateSelector.SelectBestCandidate(windows, options);

        Assert.Equal((nint)3, selected.Window.Handle);
        Assert.True(selected.TitleMatchScore > 0);
    }

    [Fact]
    public void IsCandidate_RejectsOwnedOrToolWindowsWhenConfigured()
    {
        var options = new WindowSearchOptions
        {
            ProcessName = "PokerClient",
            ExcludeOwnedWindows = true,
            ExcludeToolWindows = true
        };

        var ownedWindow = CreateWindow(handle: 11, title: "Call", isOwnedWindow: true);
        var toolWindow = CreateWindow(handle: 12, title: "Raise", isToolWindow: true);
        var tableWindow = CreateWindow(handle: 13, title: "Table 1");

        Assert.False(WindowCandidateSelector.IsCandidate(ownedWindow, options));
        Assert.False(WindowCandidateSelector.IsCandidate(toolWindow, options));
        Assert.True(WindowCandidateSelector.IsCandidate(tableWindow, options));
    }

    [Theory]
    [InlineData("PokerClient.exe", "PokerClient")]
    [InlineData("PokerClient", "PokerClient.exe")]
    [InlineData("pokerclient", "PokerClient")]
    public void IsProcessMatch_NormalizesExeSuffix(string actualProcessName, string requiredProcessName)
    {
        Assert.True(WindowCandidateSelector.IsProcessMatch(actualProcessName, requiredProcessName));
    }

    [Theory]
    [InlineData(null, "Practice Table", true)]
    [InlineData("", "Practice Table", true)]
    [InlineData("table", "Practice Table", true)]
    [InlineData("lobby", "Practice Table", false)]
    public void TitleMatching_UsesCaseInsensitiveContains(string? filter, string title, bool expected)
    {
        Assert.Equal(expected, WindowTitleMatcher.IsMatch(title, filter));
    }

    [Fact]
    public void SelectBestCandidate_ThrowsHelpfulExceptionWhenTitleFilterEliminatesMatches()
    {
        var options = new WindowSearchOptions
        {
            ProcessName = "PokerClient",
            WindowTitleContains = "Table"
        };

        var windows = new[]
        {
            CreateWindow(handle: 21, title: "Lobby"),
            CreateWindow(handle: 22, title: "Cashier")
        };

        var exception = Assert.Throws<WindowNotFoundException>(() => WindowCandidateSelector.SelectBestCandidate(windows, options));

        Assert.Contains("Title filter 'Table' excluded all PokerClient windows.", exception.Message);
    }

    private static WindowInfo CreateWindow(
        nint handle,
        string title,
        int width = 1000,
        int height = 700,
        bool isVisible = true,
        bool isMinimized = false,
        bool isForeground = false,
        bool isOwnedWindow = false,
        bool isToolWindow = false)
    {
        return new WindowInfo
        {
            Handle = handle,
            Title = title,
            ProcessId = 100,
            ProcessName = "PokerClient",
            Width = width,
            Height = height,
            IsVisible = isVisible,
            IsMinimized = isMinimized,
            IsForeground = isForeground,
            IsOwnedWindow = isOwnedWindow,
            IsToolWindow = isToolWindow
        };
    }
}
