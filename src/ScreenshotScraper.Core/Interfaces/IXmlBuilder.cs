using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IXmlBuilder
{
    Task<XmlBuildResult> BuildAsync(ExtractionResult extractionResult, CancellationToken cancellationToken = default);
}
