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

        if (snapshot.Players.Count == 0)
        {
            errors.Add("No seats were identified on the screenshot.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.GameCode))
        {
            errors.Add("Game code was not confidently extracted.");
        }

        return new ExtractionResult
        {
            Success = snapshot.Players.Count > 0,
            Snapshot = snapshot,
            Fields = BuildFields(snapshot),
            Errors = errors
        };
    }

    private static List<ExtractedField> BuildFields(PartialHandHistorySnapshot snapshot)
    {
        var heroCards = snapshot.Round1PocketCards.FirstOrDefault(cards => snapshot.Players.Any(player => player.IsHero && player.Name == cards.Player));

        return
        [
            new ExtractedField
            {
                Name = "GameCode",
                RawText = snapshot.GameCode,
                ParsedValue = snapshot.GameCode,
                IsValid = !string.IsNullOrWhiteSpace(snapshot.GameCode),
                Error = string.IsNullOrWhiteSpace(snapshot.GameCode) ? "Game code not found." : null
            },
            new ExtractedField
            {
                Name = "StartDate",
                RawText = snapshot.StartDate?.ToString("O"),
                ParsedValue = snapshot.StartDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                IsValid = snapshot.StartDate.HasValue,
                Error = snapshot.StartDate.HasValue ? null : "Start date not confidently parsed."
            },
            new ExtractedField
            {
                Name = "PlayerCount",
                RawText = snapshot.Players.Count.ToString(),
                ParsedValue = snapshot.Players.Count.ToString(),
                IsValid = snapshot.Players.Count > 0,
                Error = snapshot.Players.Count > 0 ? null : "No occupied seats extracted."
            },
            new ExtractedField
            {
                Name = "HeroPocketCards",
                RawText = heroCards?.Cards,
                ParsedValue = heroCards?.Cards,
                IsValid = !string.IsNullOrWhiteSpace(heroCards?.Cards),
                Error = string.IsNullOrWhiteSpace(heroCards?.Cards) ? "Hero pocket cards not confidently extracted." : null
            },
            new ExtractedField
            {
                Name = "ObservedPreHeroFolds",
                RawText = snapshot.Round1ObservedActions.Count.ToString(),
                ParsedValue = string.Join(", ", snapshot.Round1ObservedActions.Select(action => action.Player).Where(player => !string.IsNullOrWhiteSpace(player))),
                IsValid = true,
                Error = null
            }
        ];
    }
}
