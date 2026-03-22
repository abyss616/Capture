using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Core.Services.HandHistory;

/// <summary>
/// Prepares a captured table image, parses the pre-hero state, and builds hand-history XML.
/// </summary>
public sealed class PreHeroHandHistoryXmlWorkflow : IPreHeroHandHistoryXmlWorkflow
{
    private readonly IImagePreprocessor _imagePreprocessor;
    private readonly IDataExtractor _dataExtractor;
    private readonly IXmlBuilder _xmlBuilder;

    public PreHeroHandHistoryXmlWorkflow(
        IImagePreprocessor imagePreprocessor,
        IDataExtractor dataExtractor,
        IXmlBuilder xmlBuilder)
    {
        _imagePreprocessor = imagePreprocessor;
        _dataExtractor = dataExtractor;
        _xmlBuilder = xmlBuilder;
    }

    public async Task<PreHeroHandHistoryXmlWorkflowResult> RunAsync(CapturedImage capturedImage, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        try
        {
            var preparedImage = await _imagePreprocessor.PrepareAsync(capturedImage, cancellationToken).ConfigureAwait(false);
            var extractionResult = await _dataExtractor.ExtractAsync(preparedImage, cancellationToken).ConfigureAwait(false);
            var xmlBuildResult = await _xmlBuilder.BuildAsync(extractionResult, cancellationToken).ConfigureAwait(false);

            errors.AddRange(extractionResult.Errors);
            errors.AddRange(xmlBuildResult.Errors);

            return new PreHeroHandHistoryXmlWorkflowResult
            {
                PreparedImage = preparedImage,
                ExtractionResult = extractionResult,
                XmlBuildResult = xmlBuildResult,
                Success = extractionResult.Success && xmlBuildResult.Success && errors.Count == 0,
                Errors = errors
            };
        }
        catch (Exception exception)
        {
            errors.Add($"Pre-hero XML workflow failed: {exception.Message}");

            return new PreHeroHandHistoryXmlWorkflowResult
            {
                PreparedImage = capturedImage,
                Success = false,
                Errors = errors
            };
        }
    }
}
