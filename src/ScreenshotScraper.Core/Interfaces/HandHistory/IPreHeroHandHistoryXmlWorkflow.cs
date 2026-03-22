using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Core.Interfaces.HandHistory;

public interface IPreHeroHandHistoryXmlWorkflow
{
    Task<PreHeroHandHistoryXmlWorkflowResult> RunAsync(CapturedImage capturedImage, CancellationToken cancellationToken = default);
}
