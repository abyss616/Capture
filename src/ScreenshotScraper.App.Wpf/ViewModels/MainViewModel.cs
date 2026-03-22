using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media.Imaging;
using ScreenshotScraper.App.Wpf.Helpers;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.App.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IProcessingWorkflowService _processingWorkflowService;
    private readonly IScreenshotService _screenshotService;
    private readonly IDataExtractor _dataExtractor;
    private readonly IXmlBuilder _xmlBuilder;

    private BitmapImage? _previewImage;
    private string _previewStatus = "No screenshot captured yet.";
    private string _xmlContent = "Ready.";
    private string _statusMessage = "Capture a visible PokerClient table window to preview it.";
    private string _captureMetadata = "No capture metadata available yet.";
    private CapturedImage? _capturedImage;

    public MainViewModel(
        IProcessingWorkflowService processingWorkflowService,
        IScreenshotService screenshotService,
        IDataExtractor dataExtractor,
        IXmlBuilder xmlBuilder)
    {
        _processingWorkflowService = processingWorkflowService;
        _screenshotService = screenshotService;
        _dataExtractor = dataExtractor;
        _xmlBuilder = xmlBuilder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ExtractedField> ExtractedFields { get; } = [];

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
            XmlContent = "Capture complete. Build XML remains available for later pipeline testing.";
            StatusMessage = $"Captured {_capturedImage.WindowTitle ?? "window"} at {_capturedImage.CapturedAtUtc:O}.";
        }
        catch (Exception exception)
        {
            PreviewImage = null;
            PreviewStatus = "Capture failed.";
            CaptureMetadata = exception.Message;
            StatusMessage = $"Capture failed: {exception.Message}";
        }
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

        var workflowResult = await _processingWorkflowService.RunAsync(cancellationToken).ConfigureAwait(true);
        ApplyWorkflowResult(workflowResult);

        if (workflowResult.XmlBuildResult is null)
        {
            var extractionResult = await _dataExtractor.ExtractAsync(_capturedImage, cancellationToken).ConfigureAwait(true);
            var xmlBuildResult = await _xmlBuilder.BuildAsync(extractionResult, cancellationToken).ConfigureAwait(true);

            ApplyExtractionResult(extractionResult);
            XmlContent = xmlBuildResult.XmlContent;
            StatusMessage = "XML built using fallback services.";
        }
    }

    private void ApplyWorkflowResult(ProcessingWorkflowResult workflowResult)
    {
        _capturedImage = workflowResult.CapturedImage ?? _capturedImage;

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
            ?? string.Join(Environment.NewLine, workflowResult.Errors);

        StatusMessage = workflowResult.Success
            ? "Workflow completed."
            : string.Join(Environment.NewLine, workflowResult.Errors.DefaultIfEmpty("Workflow completed with warnings."));
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
