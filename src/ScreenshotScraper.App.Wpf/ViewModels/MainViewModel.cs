using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using ScreenshotScraper.Extraction.HandHistory;
using ScreenshotScraper.App.Wpf.Helpers;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.App.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IScreenshotService _screenshotService;
    private readonly IPreHeroHandHistoryXmlWorkflow _preHeroHandHistoryXmlWorkflow;

    private BitmapImage? _previewImage;
    private string _previewStatus = "No screenshot captured yet.";
    private string _xmlContent = "Ready.";
    private string _statusMessage = "Capture a visible PokerClient table window to preview it.";
    private string _captureMetadata = "No capture metadata available yet.";
    private CapturedImage? _capturedImage;
    private string _seatRoiStatus = "Seat ROI debug is shown after a capture or screenshot upload.";

    public MainViewModel(
        IScreenshotService screenshotService,
        IPreHeroHandHistoryXmlWorkflow preHeroHandHistoryXmlWorkflow)
    {
        _screenshotService = screenshotService;
        _preHeroHandHistoryXmlWorkflow = preHeroHandHistoryXmlWorkflow;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ExtractedField> ExtractedFields { get; } = [];

    public ObservableCollection<SeatRoiDebugViewModel> SeatRoiDebugItems { get; } = [];

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set
        {
            _previewImage = value;
            OnPropertyChanged();
        }
    }

    public string PreviewStatus
    {
        get => _previewStatus;
        private set
        {
            _previewStatus = value;
            OnPropertyChanged();
        }
    }

    public string XmlContent
    {
        get => _xmlContent;
        private set
        {
            _xmlContent = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }


    public string SeatRoiStatus
    {
        get => _seatRoiStatus;
        private set
        {
            _seatRoiStatus = value;
            OnPropertyChanged();
        }
    }

    public string CaptureMetadata
    {
        get => _captureMetadata;
        private set
        {
            _captureMetadata = value;
            OnPropertyChanged();
        }
    }

    public async Task CaptureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _capturedImage = await _screenshotService.CaptureAsync(cancellationToken).ConfigureAwait(true);
            SetPreview(_capturedImage);
            CaptureMetadata = BuildMetadataSummary(_capturedImage);

            ExtractedFields.Clear();
            XmlContent = "Capture complete. Generate XML to parse the current screenshot up to hero's first action.";
            StatusMessage = $"Captured {_capturedImage.WindowTitle ?? "window"} at {_capturedImage.CapturedAtUtc:O}.";
            BuildSeatRoiDebugArtifacts(_capturedImage);
        }
        catch (Exception exception)
        {
            PreviewImage = null;
            PreviewStatus = "Capture failed.";
            CaptureMetadata = exception.Message;
            StatusMessage = $"Capture failed: {exception.Message}";
        }
    }

    public Task LoadScreenshotAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A screenshot file path is required.", nameof(filePath));
        }

        var imageBytes = File.ReadAllBytes(filePath);
        var bitmap = BuildBitmap(imageBytes);

        _capturedImage = new CapturedImage
        {
            ImageBytes = imageBytes,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            CapturedAtUtc = DateTime.UtcNow,
            SourceDescription = Path.GetFileName(filePath),
            WindowTitle = "Uploaded screenshot",
            ProcessName = "Local file",
            WindowWidth = bitmap.PixelWidth,
            WindowHeight = bitmap.PixelHeight,
            IsVisible = true,
            IsForegroundWindow = false,
            CaptureMethod = "File upload",
            MonitorDeviceName = "N/A"
        };

        SetPreview(_capturedImage);
        CaptureMetadata = BuildMetadataSummary(_capturedImage);
        ExtractedFields.Clear();
        XmlContent = "Screenshot uploaded. Generate XML to parse the current screenshot up to hero's first action.";
        StatusMessage = $"Loaded screenshot '{Path.GetFileName(filePath)}' ({bitmap.PixelWidth}x{bitmap.PixelHeight}).";
        BuildSeatRoiDebugArtifacts(_capturedImage);

        return Task.CompletedTask;
    }

    public async Task BuildXmlAsync(CancellationToken cancellationToken = default)
    {
        if (_capturedImage is null)
        {
            await CaptureAsync(cancellationToken).ConfigureAwait(true);
        }

        if (_capturedImage is null)
        {
            StatusMessage = "Capture did not return an image.";
            return;
        }

        var workflowResult = await _preHeroHandHistoryXmlWorkflow.RunAsync(_capturedImage, cancellationToken).ConfigureAwait(true);
        ApplyWorkflowResult(workflowResult);
    }

    private void ApplyWorkflowResult(PreHeroHandHistoryXmlWorkflowResult workflowResult)
    {
        _capturedImage = workflowResult.PreparedImage ?? _capturedImage;

        if (_capturedImage is not null)
        {
            SetPreview(_capturedImage);
            CaptureMetadata = BuildMetadataSummary(_capturedImage);
        }

        if (workflowResult.ExtractionResult is not null)
        {
            ApplyExtractionResult(workflowResult.ExtractionResult);
        }

        XmlContent = workflowResult.XmlBuildResult?.XmlContent
            ?? string.Join(Environment.NewLine, workflowResult.Errors.DefaultIfEmpty("XML generation did not return content."));

        StatusMessage = workflowResult.Success
            ? "Pre-hero XML generated from the current screenshot."
            : string.Join(Environment.NewLine, workflowResult.Errors.DefaultIfEmpty("Workflow completed with warnings."));

        TryLoadSeatOcrSummary(_capturedImage);
    }

    private void ApplyExtractionResult(ExtractionResult extractionResult)
    {
        ExtractedFields.Clear();

        foreach (var field in extractionResult.Fields)
        {
            ExtractedFields.Add(field);
        }
    }

    private void SetPreview(CapturedImage capturedImage)
    {
        PreviewImage = BitmapImageFactory.Create(capturedImage.ImageBytes);

        PreviewStatus = PreviewImage is null
            ? capturedImage.SourceDescription ?? "No preview image bytes available."
            : $"Captured {capturedImage.Width}x{capturedImage.Height} image from {capturedImage.SourceDescription}.";
    }

    private static string BuildMetadataSummary(CapturedImage capturedImage)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Window Title: {capturedImage.WindowTitle ?? "(none)"}");
        builder.AppendLine($"Process Name: {capturedImage.ProcessName ?? "(unknown)"}");
        builder.AppendLine($"Captured At (UTC): {capturedImage.CapturedAtUtc:O}");
        builder.AppendLine($"Image Size: {capturedImage.Width} x {capturedImage.Height}");
        builder.AppendLine($"Window Bounds: Left={capturedImage.WindowLeft}, Top={capturedImage.WindowTop}, Width={capturedImage.WindowWidth}, Height={capturedImage.WindowHeight}");
        builder.AppendLine($"Foreground: {capturedImage.IsForegroundWindow}");
        builder.AppendLine($"Visible: {capturedImage.IsVisible}");
        builder.AppendLine($"Handle: 0x{capturedImage.WindowHandle.ToInt64():X}");
        builder.AppendLine($"Capture Method: {capturedImage.CaptureMethod ?? "(unknown)"}");
        builder.AppendLine($"Monitor: {capturedImage.MonitorDeviceName ?? "(unknown)"}");
        builder.Append($"Source: {capturedImage.SourceDescription ?? "(unknown)"}");
        return builder.ToString();
    }

    private static BitmapSource BuildBitmap(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames.FirstOrDefault()
            ?? throw new InvalidOperationException("Unable to read screenshot file bytes.");
    }



    private void BuildSeatRoiDebugArtifacts(CapturedImage capturedImage)
    {
        SeatRoiDebugItems.Clear();

        var layout = new SixMaxTableVisionLayout();
        var rois = layout.GetSeatRois(capturedImage.Width, capturedImage.Height);


        foreach (var seat in rois)
        {
            var fullBounds = BuildSeatBounds(seat);
            SeatRoiDebugItems.Add(new SeatRoiDebugViewModel
            {
                Seat = seat.Seat,
                OccupancyLabel = $"Seat {seat.Seat}",
                FullRoiImage = ToBitmap(CropRoi(capturedImage.ImageBytes, fullBounds)),
                NameRoiImage = ToBitmap(CropRoi(capturedImage.ImageBytes, seat.NameRoi)),
                StackRoiImage = ToBitmap(CropRoi(capturedImage.ImageBytes, seat.StackRoi)),
                BetRoiImage = ToBitmap(CropRoi(capturedImage.ImageBytes, seat.BetRoi)),
                NameBounds = FormatBounds(seat.NameRoi),
                StackBounds = FormatBounds(seat.StackRoi),
                BetBounds = FormatBounds(seat.BetRoi)
            });
        }

        SeatRoiStatus = $"Showing {SeatRoiDebugItems.Count} seat ROI panels (full + name/stack/bet crops).";
    }

    private void TryLoadSeatOcrSummary(CapturedImage? capturedImage)
    {
        if (capturedImage is null)
        {
            return;
        }

        var timestamp = (capturedImage.CapturedAtUtc == default ? DateTime.UtcNow : capturedImage.CapturedAtUtc).ToString("yyyyMMdd_HHmmssfff");
        var summaryPath = Path.Combine("debug", "output", timestamp, "seat_ocr_summary.txt");
        if (File.Exists(summaryPath))
        {
            SeatRoiStatus = File.ReadAllText(summaryPath);
        }
    }

    private static BitmapImage? ToBitmap(byte[] bytes)
    {
        return bytes.Length == 0 ? null : BitmapImageFactory.Create(bytes);
    }

    private static byte[] CropRoi(byte[] sourceBytes, System.Drawing.Rectangle roi)
    {
        using var source = Cv2.ImDecode(sourceBytes, ImreadModes.Color);
        if (source.Empty())
        {
            return [];
        }

        var bounded = new Rect(roi.X, roi.Y, roi.Width, roi.Height).Intersect(new Rect(0, 0, source.Width, source.Height));
        if (bounded.Width <= 0 || bounded.Height <= 0)
        {
            return [];
        }

        using var crop = new Mat(source, bounded);
        Cv2.ImEncode(".png", crop, out var encoded);
        return encoded;
    }

    private static System.Drawing.Rectangle BuildSeatBounds(SeatVisionRoi seat)
    {
        var left = new[] { seat.OccupancyRoi.Left, seat.NameRoi.Left, seat.StackRoi.Left, seat.BetRoi.Left }.Min();
        var top = new[] { seat.OccupancyRoi.Top, seat.NameRoi.Top, seat.StackRoi.Top, seat.BetRoi.Top }.Min();
        var right = new[] { seat.OccupancyRoi.Right, seat.NameRoi.Right, seat.StackRoi.Right, seat.BetRoi.Right }.Max();
        var bottom = new[] { seat.OccupancyRoi.Bottom, seat.NameRoi.Bottom, seat.StackRoi.Bottom, seat.BetRoi.Bottom }.Max();
        return System.Drawing.Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static string FormatBounds(System.Drawing.Rectangle roi)
    {
        return $"x={roi.X}, y={roi.Y}, w={roi.Width}, h={roi.Height}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
