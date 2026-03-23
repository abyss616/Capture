using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public interface IPreHeroActionInferencer
{
    (IReadOnlyList<SnapshotAction> Round0Actions, IReadOnlyList<SnapshotAction> Round1Actions) Infer(IReadOnlyList<SnapshotPlayer> players);
}
