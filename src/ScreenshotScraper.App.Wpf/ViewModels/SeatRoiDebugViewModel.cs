using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace ScreenshotScraper.App.Wpf.ViewModels;

public sealed class SeatRoiDebugViewModel
{
    public required int Seat { get; init; }

    public required string OccupancyLabel { get; init; }

    public required BitmapImage? FullRoiImage { get; init; }

    public required ObservableCollection<SeatFieldDebugViewModel> Fields { get; init; }
}

public sealed class SeatFieldDebugViewModel
{
    public required string FieldType { get; init; }

    public required BitmapImage? RawRoiImage { get; init; }

    public required BitmapImage? SelectedOcrInputImage { get; init; }

    public required string Bounds { get; init; }

    public required string SelectedVariant { get; init; }

    public required string ParsedValue { get; init; }

    public required string ParseRejectionReason { get; init; }

    public required ObservableCollection<SeatFieldVariantDebugViewModel> Variants { get; init; }
}

public sealed class SeatFieldVariantDebugViewModel
{
    public required string VariantName { get; init; }

    public required BitmapImage? Image { get; init; }

    public required bool Selected { get; init; }

    public required string OcrRawText { get; init; }

    public required string ConfidenceText { get; init; }

    public required string Backend { get; init; }

    public required string RejectionReason { get; init; }
}

public sealed class SeatOcrInputDebugViewModel
{
    public required int Seat { get; init; }

    public required string SeatLabel { get; init; }

    public required BitmapImage? NameImage { get; init; }

    public required BitmapImage? StackImage { get; init; }

    public required BitmapImage? BetImage { get; init; }
}
