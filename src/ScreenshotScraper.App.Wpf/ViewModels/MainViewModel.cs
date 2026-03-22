using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
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
    private string _previewStatus = "No screenshot captured yet. Placeholder preview will appear when image bytes are available.";
    private string _xmlContent = "Ready.";
    private string _statusMessage = "Use Capture to create placeholder data, then Build XML to run the workflow.";
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

    public async Task CaptureAsync(CancellationToken cancellationToken = default)
    {
        _capturedImage = await _screenshotService.CaptureAsync(cancellationToken).ConfigureAwait(true);
        SetPreview(_capturedImage);

        ExtractedFields.Clear();
        XmlContent = "Capture complete. Build XML to run extraction and XML generation.";
        StatusMessage = _capturedImage.SourceDescription ?? "Capture completed.";
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
        }

        if (workflowResult.ExtractionResult is not null)
        {
            ApplyExtractionResult(workflowResult.ExtractionResult);
        }

        XmlContent = workflowResult.XmlBuildResult?.XmlContent
            ?? string.Join(Environment.NewLine, workflowResult.Errors);

        StatusMessage = workflowResult.Success
            ? "Workflow completed with placeholder services."
            : string.Join(Environment.NewLine, workflowResult.Errors.DefaultIfEmpty("Workflow completed with placeholder warnings."));
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
        if (capturedImage.ImageBytes is { Length: > 0 })
        {
            using var stream = new MemoryStream(capturedImage.ImageBytes);
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            PreviewImage = bitmapImage;
            PreviewStatus = $"Captured {capturedImage.Width}x{capturedImage.Height} image from {capturedImage.SourceDescription}.";
            return;
        }

        PreviewImage = null;
        PreviewStatus = capturedImage.SourceDescription ?? "No preview image bytes available yet.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
