using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction;

/// <summary>
/// Parses a single table screenshot into a conservative pre-hero hand-history snapshot.
/// </summary>
public sealed class DataExtractor : IDataExtractor
{
    private readonly IPreHeroScreenshotParser _preHeroScreenshotParser;

    public DataExtractor(IPreHeroScreenshotParser preHeroScreenshotParser)
    {
        _preHeroScreenshotParser = preHeroScreenshotParser;
    }

    public async Task<ExtractionResult> ExtractAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await _preHeroScreenshotParser.ParseAsync(image, cancellationToken).ConfigureAwait(false);
        var errors = new List<string>();
        var occupiedPlayers = snapshot.Players.Where(player => !string.IsNullOrWhiteSpace(player.Name)).ToList();
        var hero = occupiedPlayers.FirstOrDefault(player => player.IsHero);

        if (occupiedPlayers.Count == 0)
        {
            errors.Add("No seats were identified on the screenshot.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.GameCode))
        {
            errors.Add("Game code was not confidently extracted.");
        }

        if (hero is null)
        {
            errors.Add("Hero seat was not identified on the screenshot.");
        }

        if (!(snapshot.DealerSeatField?.IsValid ?? false))
        {
            errors.Add(snapshot.DealerSeatField?.Reason ?? "Dealer button was not confidently detected.");
        }

        if (!(snapshot.HeroPositionField?.IsValid ?? false))
        {
            errors.Add(snapshot.HeroPositionField?.Reason ?? "Hero position was not confidently inferred.");
        }

        return new ExtractionResult
        {
            Success = occupiedPlayers.Count > 0 && hero is not null,
            Snapshot = snapshot,
            Fields = BuildFields(snapshot),
            Errors = errors.Distinct(StringComparer.Ordinal).ToList()
        };
    }

    private static List<ExtractedField> BuildFields(PartialHandHistorySnapshot snapshot)
    {
        var heroCards = snapshot.Round1PocketCards.FirstOrDefault(cards => snapshot.Players.Any(player => player.IsHero && player.Name == cards.Player));

        return
        [
            snapshot.GameCodeField ?? BuildSimpleField("GameCode", snapshot.GameCode, !string.IsNullOrWhiteSpace(snapshot.GameCode), "Game code not found."),
            new ExtractedField
            {
                Name = "StartDate",
                RawText = snapshot.StartDate?.ToString("O"),
                ParsedValue = snapshot.StartDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                IsValid = snapshot.StartDate.HasValue,
                Error = snapshot.StartDate.HasValue ? null : "Start date not confidently parsed.",
                Confidence = snapshot.StartDate.HasValue ? 0.9 : 0,
                Reason = snapshot.StartDate.HasValue ? "Matched a supported timestamp pattern in the title/OCR text." : "No supported timestamp pattern was found."
            },
            new ExtractedField
            {
                Name = "PlayerCount",
                RawText = snapshot.Players.Count.ToString(),
                ParsedValue = snapshot.Players.Count.ToString(),
                IsValid = snapshot.Players.Count > 0,
                Error = snapshot.Players.Count > 0 ? null : "No occupied seats extracted.",
                Confidence = snapshot.Players.Count > 0 ? 0.85 : 0,
                Reason = snapshot.Players.Count > 0 ? "Counted occupied seat regions/entries in the 6-max layout." : "No occupied seats were extracted from the layout."
            },
            snapshot.HeroNameField ?? BuildSimpleField("HeroName", null, false, "Hero name not confidently extracted."),
            new ExtractedField
            {
                Name = "HeroPocketCards",
                RawText = heroCards?.Cards,
                ParsedValue = heroCards?.Cards,
                IsValid = !string.IsNullOrWhiteSpace(heroCards?.Cards),
                Error = string.IsNullOrWhiteSpace(heroCards?.Cards) ? "Hero pocket cards not confidently extracted." : null,
                Confidence = string.IsNullOrWhiteSpace(heroCards?.Cards) ? 0 : 0.9,
                Reason = string.IsNullOrWhiteSpace(heroCards?.Cards) ? "No hero pocket-card pattern was found in the hero card region." : "Detected hero pocket cards from hero-card region OCR."
            },
            snapshot.HeroPositionField ?? BuildSimpleField("HeroPosition", null, false, "Hero position not confidently inferred."),
            snapshot.DealerSeatField ?? BuildSimpleField("DealerSeat", null, false, "Dealer button was not confidently detected."),
            new ExtractedField
            {
                Name = "ObservedPreHeroActions",
                RawText = snapshot.Round1ObservedActions.Count.ToString(),
                ParsedValue = string.Join(", ", snapshot.Round1ObservedActions.Select(action => action.Player).Where(player => !string.IsNullOrWhiteSpace(player))),
                IsValid = true,
                Error = null,
                Confidence = snapshot.Round1ObservedActions.Count > 0 ? 0.8 : 0.6,
                Reason = "Observed folded players before the hero according to the inferred 6-max action order."
            }
        ];
    }

    private static ExtractedField BuildSimpleField(string name, string? value, bool isValid, string error)
    {
        return new ExtractedField
        {
            Name = name,
            RawText = value,
            ParsedValue = value,
            IsValid = isValid,
            Error = isValid ? null : error,
            Confidence = isValid ? 1.0 : 0,
            Reason = isValid ? $"{name} was extracted successfully." : error
        };
    }
}
