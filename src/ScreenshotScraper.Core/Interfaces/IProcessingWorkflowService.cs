using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Core.Interfaces;

public interface IProcessingWorkflowService
{
    Task<ProcessingWorkflowResult> RunAsync(CancellationToken cancellationToken = default);
}
