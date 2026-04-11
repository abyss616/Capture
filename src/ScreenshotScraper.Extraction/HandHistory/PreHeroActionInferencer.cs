using System.Globalization;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class PreHeroActionInferencer : IPreHeroActionInferencer
{
    private const decimal SmallBlindAmount = 0.5m;
    private const decimal BigBlindAmount = 1.0m;

    public (IReadOnlyList<SnapshotAction> Round0Actions, IReadOnlyList<SnapshotAction> Round1Actions) Infer(IReadOnlyList<SnapshotPlayer> players)
    {
        var (smallBlind, bigBlind) = ResolveBlindPlayersByBetSize(players);
        var round0Actions = BuildRound0Actions(smallBlind, bigBlind);

        var hero = players.FirstOrDefault(player => player.IsHero);
        if (hero is null)
        {
            return (round0Actions, []);
        }

        var round1Actions = InferRound1Actions(players, hero, smallBlind, bigBlind, round0Actions.Count + 1);

        return (round0Actions, round1Actions);
    }

    private static List<SnapshotAction> BuildRound0Actions(SnapshotPlayer? smallBlind, SnapshotPlayer? bigBlind)
    {
        var actions = new List<SnapshotAction>();
        var nextNo = 1;

        if (smallBlind is not null)
        {
            actions.Add(new SnapshotAction
            {
                No = nextNo++,
                Player = smallBlind.Name,
                Type = SnapshotActionType.SmallBlindPost,
                Sum = SmallBlindAmount.ToString(CultureInfo.InvariantCulture),
                Discard = true,
                Dealt = true
            });
        }

        if (bigBlind is not null)
        {
            actions.Add(new SnapshotAction
            {
                No = nextNo++,
                Player = bigBlind.Name,
                Type = SnapshotActionType.BigBlindPost,
                Sum = BigBlindAmount.ToString(CultureInfo.InvariantCulture),
                Discard = true,
                Dealt = true
            });
        }

        return actions;
    }

    private static List<SnapshotAction> InferRound1Actions(
        IReadOnlyList<SnapshotPlayer> players,
        SnapshotPlayer hero,
        SnapshotPlayer? smallBlind,
        SnapshotPlayer? bigBlind,
        int startingActionNo)
    {
        var actions = new List<SnapshotAction>();
        var committedBySeat = players.ToDictionary(player => player.Seat, _ => 0m);

        if (smallBlind is not null)
        {
            committedBySeat[smallBlind.Seat] = SmallBlindAmount;
        }

        if (bigBlind is not null)
        {
            committedBySeat[bigBlind.Seat] = BigBlindAmount;
        }

        var currentPrice = BigBlindAmount;
        var actionNo = startingActionNo;

        foreach (var player in SixMaxPositionMapper.OrderPreflopActors(players))
        {
            if (player.Seat == hero.Seat)
            {
                break;
            }

            var alreadyCommitted = committedBySeat[player.Seat];
            if (player.AppearsFolded)
            {
                actions.Add(new SnapshotAction
                {
                    No = actionNo++,
                    Player = player.Name,
                    Type = SnapshotActionType.Fold,
                    Sum = "0"
                });
                continue;
            }

            var finalCommitted = ParseBetSize(player.Bet);
            if (finalCommitted == alreadyCommitted && alreadyCommitted == currentPrice)
            {
                actions.Add(new SnapshotAction
                {
                    No = actionNo++,
                    Player = player.Name,
                    Type = SnapshotActionType.Check,
                    Sum = "0"
                });
                continue;
            }

            if (finalCommitted > alreadyCommitted && finalCommitted == currentPrice)
            {
                actions.Add(new SnapshotAction
                {
                    No = actionNo++,
                    Player = player.Name,
                    Type = SnapshotActionType.Call,
                    Sum = (finalCommitted - alreadyCommitted).ToString(CultureInfo.InvariantCulture)
                });
                committedBySeat[player.Seat] = finalCommitted;
                continue;
            }

            if (finalCommitted > currentPrice)
            {
                actions.Add(new SnapshotAction
                {
                    No = actionNo++,
                    Player = player.Name,
                    Type = SnapshotActionType.BetRaiseAllIn,
                    Sum = (finalCommitted - alreadyCommitted).ToString(CultureInfo.InvariantCulture)
                });
                committedBySeat[player.Seat] = finalCommitted;
                currentPrice = finalCommitted;
            }
        }

        return actions;
    }

    private static (SnapshotPlayer? SmallBlind, SnapshotPlayer? BigBlind) ResolveBlindPlayersByBetSize(IReadOnlyList<SnapshotPlayer> players)
    {
        var dealer = players.FirstOrDefault(player => player.Dealer);
        if (dealer is not null)
        {
            var clockwiseFromDealer = SixMaxPositionMapper.OrderDealerFirst(players, dealer.Seat);
            if (clockwiseFromDealer.Count >= 3)
            {
                return (clockwiseFromDealer[1], clockwiseFromDealer[2]);
            }
        }

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

    private static decimal ParseBetSize(string? bet)
    {
        var parsed = SeatLocalTextParser.ParseNumber(bet);
        return decimal.TryParse(parsed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }
}
