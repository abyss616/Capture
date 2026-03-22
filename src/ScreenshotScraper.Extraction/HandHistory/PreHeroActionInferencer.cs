using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class PreHeroActionInferencer : IPreHeroActionInferencer
{
    public (IReadOnlyList<SnapshotAction> Round0Actions, IReadOnlyList<SnapshotAction> Round1Actions) Infer(IReadOnlyList<SnapshotPlayer> players)
    {
        var round0Actions = new List<SnapshotAction>();
        var round1Actions = new List<SnapshotAction>();

        var numberedRound0 = 1;
        foreach (var blind in players.Where(player => player.Position is "SB" or "BB" && !string.IsNullOrWhiteSpace(player.Bet)))
        {
            round0Actions.Add(new SnapshotAction
            {
                No = numberedRound0++,
                Player = blind.Name,
                Type = blind.Position == "SB" ? 1 : 2,
                Sum = blind.Bet ?? string.Empty
            });
        }

        var hero = players.FirstOrDefault(player => player.IsHero);
        if (hero is null || string.IsNullOrWhiteSpace(hero.Position))
        {
            return (round0Actions, round1Actions);
        }

        var actionNumber = 1;
        foreach (var player in SixMaxPositionMapper.OrderPreflopActors(players).Where(player => !string.IsNullOrWhiteSpace(player.Position)))
        {
            if (player.Seat == hero.Seat)
            {
                break;
            }

            if (!player.AppearsFolded)
            {
                continue;
            }

            round1Actions.Add(new SnapshotAction
            {
                No = actionNumber++,
                Player = player.Name,
                Type = 0,
                Sum = string.Empty
            });
        }

        return (round0Actions, round1Actions);
    }
}
