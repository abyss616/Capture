using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Services;

/// <summary>
/// Coordinates capture, preprocessing, extraction, and XML generation outside of the UI layer.
/// </summary>
public sealed class ProcessingWorkflowService : IProcessingWorkflowService
{
    private readonly IScreenshotService _screenshotService;
    private readonly IImagePreprocessor _imagePreprocessor;
    private readonly IDataExtractor _dataExtractor;
    private readonly IXmlBuilder _xmlBuilder;

    public ProcessingWorkflowService(
        IScreenshotService screenshotService,
        IImagePreprocessor imagePreprocessor,
        IDataExtractor dataExtractor,
        IXmlBuilder xmlBuilder)
    {
        _screenshotService = screenshotService;
        _imagePreprocessor = imagePreprocessor;
        _dataExtractor = dataExtractor;
        _xmlBuilder = xmlBuilder;
    }

    public async Task<ProcessingWorkflowResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        try
        {
            var capturedImage = await _screenshotService.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var preparedImage = await _imagePreprocessor.PrepareAsync(capturedImage, cancellationToken).ConfigureAwait(false);
            var extractionResult = await _dataExtractor.ExtractAsync(preparedImage, cancellationToken).ConfigureAwait(false);
            var xmlBuildResult = await _xmlBuilder.BuildAsync(extractionResult, cancellationToken).ConfigureAwait(false);

            errors.AddRange(extractionResult.Errors);
            errors.AddRange(xmlBuildResult.Errors);

            return new ProcessingWorkflowResult
            {
                CapturedImage = preparedImage,
                ExtractionResult = extractionResult,
                XmlBuildResult = xmlBuildResult,
                Success = extractionResult.Success && xmlBuildResult.Success && errors.Count == 0,
                Errors = errors
            };
        }
        catch (Exception exception)
        {
            errors.Add($"Processing workflow failed: {exception.Message}");

            return new ProcessingWorkflowResult
            {
                Success = false,
                Errors = errors
            };
        }
    }
}
