using System.Globalization;
using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class OcrTableHeaderExtractor : ITableHeaderExtractor
{
    public TableHeaderSnapshot Extract(CapturedImage image, string rawText)
    {
        var title = image.WindowTitle ?? string.Empty;
        var body = rawText ?? string.Empty;
        var gameCodeField = ExtractGameCode(title, body);

        DateTime? startDate = null;
        var timestamp = TimestampRegex().Match(string.Join(Environment.NewLine, title, body));
        if (timestamp.Success)
        {
            var candidate = timestamp.Groups["date"].Value;
            if (DateTime.TryParseExact(candidate, "dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                startDate = parsed;
            }
        }

        return new TableHeaderSnapshot
        {
            GameCode = string.IsNullOrWhiteSpace(gameCodeField.ParsedValue) ? null : gameCodeField.ParsedValue,
            StartDate = startDate,
            GameCodeField = gameCodeField
        };
    }

    private static ExtractedField ExtractGameCode(string title, string ocrText)
    {
        var fromTitle = TryMatchGameCode(title, "window title", 1.0);
        if (fromTitle is not null)
        {
            return fromTitle;
        }

        var fromOcr = TryMatchGameCode(ocrText, "OCR text", 0.65);
        if (fromOcr is not null)
        {
            return fromOcr;
        }

        return new ExtractedField
        {
            Name = "GameCode",
            RawText = string.Join(Environment.NewLine, title, ocrText).Trim(),
            ParsedValue = null,
            IsValid = false,
            Error = "Game code not found.",
            Confidence = 0,
            Reason = "No supported game-code pattern was found in the window title or OCR text."
        };
    }

    private static ExtractedField? TryMatchGameCode(string text, string source, double baseConfidence)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = IdGameCodeRegex().Match(text);
        if (match.Success)
        {
            return BuildGameCodeField(text, match.Groups["code"].Value, source, baseConfidence, "Matched an explicit ID marker.");
        }

        match = PipeDelimitedGameCodeRegex().Match(text);
        if (match.Success)
        {
            return BuildGameCodeField(text, match.Groups["code"].Value, source, Math.Max(0.9, baseConfidence), "Matched a standalone 6+ digit identifier before the title metadata separators.");
        }

        match = LeadingStandaloneGameCodeRegex().Match(text);
        if (match.Success)
        {
            return BuildGameCodeField(text, match.Groups["code"].Value, source, Math.Max(0.75, baseConfidence), "Matched a standalone 6+ digit identifier near the start of the text.");
        }

        return null;
    }

    private static ExtractedField BuildGameCodeField(string rawText, string code, string source, double confidence, string reason)
    {
        return new ExtractedField
        {
            Name = "GameCode",
            RawText = rawText,
            ParsedValue = code,
            IsValid = !string.IsNullOrWhiteSpace(code),
            Error = string.IsNullOrWhiteSpace(code) ? "Game code not found." : null,
            Confidence = confidence,
            Reason = $"Parsed from {source}. {reason}"
        };
    }

    [GeneratedRegex(@"ID\s*[:#]\s*(?<code>\d{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IdGameCodeRegex();

    [GeneratedRegex(@"^(?:[^\r\n|]*?\s)?(?<code>\d{6,})\b(?=\s*\|)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PipeDelimitedGameCodeRegex();

    [GeneratedRegex(@"^.{0,40}?\b(?<code>\d{6,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex LeadingStandaloneGameCodeRegex();

    [GeneratedRegex(@"(?<date>\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2})", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();
}
