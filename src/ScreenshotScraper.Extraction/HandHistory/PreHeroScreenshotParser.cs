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

    public PreHeroScreenshotParser(
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
        var dealerSeatField = _dealerButtonExtractor.DetectDealerSeat(image, rawText, basePlayers);
        var dealerSeat = int.TryParse(dealerSeatField.ParsedValue, out var parsedDealerSeat) ? parsedDealerSeat : (int?)null;

        var players = ApplyDealerAndHeroCards(basePlayers, dealerSeat, heroCards);
        players = SixMaxPositionMapper.AssignPositions(players).ToList();
        var hero = players.FirstOrDefault(player => player.IsHero);
        var heroNameField = BuildHeroNameField(hero);
        var heroPositionField = BuildHeroPositionField(hero, dealerSeatField);
        var actions = _preHeroActionInferencer.Infer(players);

        return new PartialHandHistorySnapshot
        {
            GameCode = header.GameCode,
            StartDate = header.StartDate,
            Players = players,
            Round0Actions = actions.Round0Actions.ToList(),
            Round1PocketCards = BuildPocketCards(players, heroCards),
            Round1ObservedActions = actions.Round1Actions.ToList(),
            GameCodeField = header.GameCodeField,
            HeroNameField = heroNameField,
            DealerSeatField = dealerSeatField,
            HeroPositionField = heroPositionField
        };
    }

    private static ExtractedField BuildHeroNameField(SnapshotPlayer? hero)
    {
        return new ExtractedField
        {
            Name = "HeroName",
            RawText = hero?.Name,
            ParsedValue = hero?.Name,
            IsValid = !string.IsNullOrWhiteSpace(hero?.Name),
            Error = string.IsNullOrWhiteSpace(hero?.Name) ? "Hero name not confidently extracted." : null,
            Confidence = string.IsNullOrWhiteSpace(hero?.Name) ? 0 : 1.0,
            Reason = string.IsNullOrWhiteSpace(hero?.Name)
                ? "The bottom-center hero seat did not yield a usable player name."
                : "Hero name was read from the bottom-center seat region (seat 1)."
        };
    }

    private static ExtractedField BuildHeroPositionField(SnapshotPlayer? hero, ExtractedField dealerSeatField)
    {
        if (hero is not null && !string.IsNullOrWhiteSpace(hero.Position))
        {
            return new ExtractedField
            {
                Name = "HeroPosition",
                RawText = hero.Position,
                ParsedValue = hero.Position,
                IsValid = true,
                Error = null,
                Confidence = dealerSeatField.IsValid ? Math.Min(1.0, 0.55 + dealerSeatField.Confidence * 0.45) : 0.2,
                Reason = $"Hero position was inferred from fixed 6-max seat indices using dealer seat {dealerSeatField.ParsedValue}."
            };
        }

        return new ExtractedField
        {
            Name = "HeroPosition",
            RawText = hero?.Position,
            ParsedValue = hero?.Position,
            IsValid = false,
            Error = "Hero position not confidently inferred.",
            Confidence = 0,
            Reason = dealerSeatField.IsValid
                ? "Hero seat was identified, but no position could be mapped from the 6-max layout."
                : $"Hero seat was identified, but dealer detection was low-confidence or missing: {dealerSeatField.Reason}"
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
