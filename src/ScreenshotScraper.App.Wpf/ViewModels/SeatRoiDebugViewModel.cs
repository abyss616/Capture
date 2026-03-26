using System.Windows.Media.Imaging;

namespace ScreenshotScraper.App.Wpf.ViewModels;

public sealed class SeatRoiDebugViewModel
{
    public required int Seat { get; init; }

    public required string OccupancyLabel { get; init; }

    public required BitmapImage? FullRoiImage { get; init; }

    public required BitmapImage? NameRoiImage { get; init; }

    public required BitmapImage? StackRoiImage { get; init; }

    public required BitmapImage? BetRoiImage { get; init; }

    public required string NameBounds { get; init; }

    public required string StackBounds { get; init; }

    public required string BetBounds { get; init; }
}
