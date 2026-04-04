using System.Globalization;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class PreHeroActionInferencer : IPreHeroActionInferencer
{
    public (IReadOnlyList<SnapshotAction> Round0Actions, IReadOnlyList<SnapshotAction> Round1Actions) Infer(IReadOnlyList<SnapshotPlayer> players)
    {
        var round0Actions = new List<SnapshotAction>();
        var round1Actions = new List<SnapshotAction>();

        var playersWithPositions = players.Any(player => !string.IsNullOrWhiteSpace(player.Position))
            ? players
            : SixMaxPositionMapper.AssignPositions(players);

        var (smallBlind, bigBlind) = ResolveBlindPlayersByBetSize(playersWithPositions);

        if (smallBlind is not null)
        {
            round0Actions.Add(new SnapshotAction
            {
                No = 1,
                Player = smallBlind.Name,
                Type = SnapshotActionType.SmallBlindPost,
                Sum = smallBlind.Bet ?? string.Empty
            });
        }

        if (bigBlind is not null)
        {
            round0Actions.Add(new SnapshotAction
            {
                No = 2,
                Player = bigBlind.Name,
                Type = SnapshotActionType.BigBlindPost,
                Sum = bigBlind.Bet ?? string.Empty
            });
        }

        var hero = playersWithPositions.FirstOrDefault(player => player.IsHero);
        if (hero is null || string.IsNullOrWhiteSpace(hero.Position))
        {
            return (round0Actions, round1Actions);
        }

        var currentToMatch = TryParseBet(bigBlind?.Bet);
        var hasAggressionInRound = false;
        var firstActorHandled = false;
        var actionNumber = 1;
        foreach (var player in SixMaxPositionMapper.OrderPreflopActors(playersWithPositions).Where(player => !string.IsNullOrWhiteSpace(player.Position)))
        {
            if (player.Seat == hero.Seat)
            {
                break;
            }

            if (player.AppearsFolded)
            {
                round1Actions.Add(new SnapshotAction
                {
                    No = actionNumber++,
                    Player = player.Name,
                    Type = SnapshotActionType.Fold,
                    Sum = string.Empty,
                    Discard = true,
                    Dealt = true
                });

                firstActorHandled = true;
                continue;
            }

            var playerBet = TryParseBet(player.Bet);
            if (playerBet == currentToMatch)
            {
                var isBigBlind = string.Equals(player.Position, "BB", StringComparison.OrdinalIgnoreCase);
                var isFirstActorAfterBigBlind = !firstActorHandled;
                var actionType = !hasAggressionInRound && (isBigBlind || isFirstActorAfterBigBlind)
                    ? SnapshotActionType.Check
                    : SnapshotActionType.Call;

                round1Actions.Add(new SnapshotAction
                {
                    No = actionNumber++,
                    Player = player.Name,
                    Type = actionType,
                    Sum = player.Bet ?? string.Empty
                });
            }
            else if (playerBet > currentToMatch)
            {
                var actionType = hasAggressionInRound ? SnapshotActionType.Raise : SnapshotActionType.Bet;
                round1Actions.Add(new SnapshotAction
                {
                    No = actionNumber++,
                    Player = player.Name,
                    Type = actionType,
                    Sum = player.Bet ?? string.Empty,
                    Discard = true,
                    Dealt = true
                });

                currentToMatch = playerBet;
                hasAggressionInRound = true;
            }

            firstActorHandled = true;
        }

        return (round0Actions, round1Actions);
    }

    private static (SnapshotPlayer? SmallBlind, SnapshotPlayer? BigBlind) ResolveBlindPlayersByBetSize(IReadOnlyList<SnapshotPlayer> players)
    {
        SnapshotPlayer? smallBlind = null;
        SnapshotPlayer? bigBlind = null;

        foreach (var player in players)
        {
            var parsedBet = SeatLocalTextParser.ParseNumber(player.Bet);
            if (!decimal.TryParse(parsedBet, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var betSize))
            {
                continue;
            }

            if (betSize == 0.5m && smallBlind is null)
            {
                smallBlind = player;
                continue;
            }

            if (betSize == 1m && bigBlind is null)
            {
                bigBlind = player;
            }
        }

        return (smallBlind, bigBlind);
    }

    private static decimal TryParseBet(string? bet)
    {
        var parsedBet = SeatLocalTextParser.ParseNumber(bet);
        return decimal.TryParse(parsedBet, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var betSize)
            ? betSize
            : 0m;
    }
}
