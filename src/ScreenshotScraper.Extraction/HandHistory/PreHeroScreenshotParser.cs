using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class PreHeroScreenshotParser : IPreHeroScreenshotParser
{
    private readonly IOcrEngine _ocrEngine;
    private readonly ITableHeaderExtractor _tableHeaderExtractor;
    private readonly ISeatSnapshotExtractor _seatSnapshotExtractor;
    private readonly ICardExtractor _cardExtractor;
    private readonly IDealerButtonExtractor _dealerButtonExtractor;
    private readonly IPreHeroActionInferencer _preHeroActionInferencer;

    public PreHeroScreenshotParser(IOcrEngine ocrEngine)
        : this(
            ocrEngine,
            new OcrTableHeaderExtractor(),
            new FixedLayoutSeatSnapshotExtractor(),
            new OcrHeroCardExtractor(),
            new HeuristicDealerButtonExtractor(),
            new PreHeroActionInferencer())
    {
    }

    internal PreHeroScreenshotParser(
        IOcrEngine ocrEngine,
        ITableHeaderExtractor tableHeaderExtractor,
        ISeatSnapshotExtractor seatSnapshotExtractor,
        ICardExtractor cardExtractor,
        IDealerButtonExtractor dealerButtonExtractor,
        IPreHeroActionInferencer preHeroActionInferencer)
    {
        _ocrEngine = ocrEngine;
        _tableHeaderExtractor = tableHeaderExtractor;
        _seatSnapshotExtractor = seatSnapshotExtractor;
        _cardExtractor = cardExtractor;
        _dealerButtonExtractor = dealerButtonExtractor;
        _preHeroActionInferencer = preHeroActionInferencer;
    }

    public async Task<PartialHandHistorySnapshot> ParseAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawText = await _ocrEngine.ReadTextAsync(image, cancellationToken).ConfigureAwait(false);
        var header = _tableHeaderExtractor.Extract(image, rawText);
        var basePlayers = _seatSnapshotExtractor.Extract(image, rawText).ToList();
        var heroCards = _cardExtractor.ExtractHeroCards(image, rawText);
        var dealerSeat = _dealerButtonExtractor.DetectDealerSeat(image, rawText, basePlayers);

        var players = ApplyDealerAndHeroCards(basePlayers, dealerSeat, heroCards);
        players = SixMaxPositionMapper.AssignPositions(players).ToList();
        var actions = _preHeroActionInferencer.Infer(players);

        return new PartialHandHistorySnapshot
        {
            GameCode = header.GameCode,
            StartDate = header.StartDate,
            Players = players,
            Round0Actions = actions.Round0Actions.ToList(),
            Round1PocketCards = BuildPocketCards(players, heroCards),
            Round1ObservedActions = actions.Round1Actions.ToList()
        };
    }

    private static List<SnapshotPocketCards> BuildPocketCards(IReadOnlyList<SnapshotPlayer> players, string heroCards)
    {
        return players
            .Select(player => new SnapshotPocketCards
            {
                Player = player.Name,
                Cards = player.IsHero
                    ? heroCards
                    : "X X"
            })
            .ToList();
    }

    private static List<SnapshotPlayer> ApplyDealerAndHeroCards(IReadOnlyList<SnapshotPlayer> players, int? dealerSeat, string heroCards)
    {
        return players
            .Select(player => new SnapshotPlayer
            {
                Seat = player.Seat,
                Name = player.Name,
                Chips = player.Chips,
                Dealer = dealerSeat.HasValue && player.Seat == dealerSeat.Value,
                Bet = player.Bet,
                Win = player.Win,
                Muck = player.Muck,
                Cashout = player.Cashout,
                CashoutFee = player.CashoutFee,
                RakeAmount = player.RakeAmount,
                Position = player.Position,
                IsHero = player.IsHero,
                AppearsFolded = player.AppearsFolded,
                HasVisibleCards = player.IsHero && !string.IsNullOrWhiteSpace(heroCards)
            })
            .ToList();
    }
}
