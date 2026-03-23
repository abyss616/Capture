using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Core.Interfaces.HandHistory;

public interface IPreHeroScreenshotParser
{
    Task<PartialHandHistorySnapshot> ParseAsync(CapturedImage image, CancellationToken cancellationToken = default);
}
