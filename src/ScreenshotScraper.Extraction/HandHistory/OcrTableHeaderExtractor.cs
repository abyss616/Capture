using System.Globalization;
using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class OcrTableHeaderExtractor : ITableHeaderExtractor
{
    public TableHeaderSnapshot Extract(CapturedImage image, string rawText)
    {
        var text = string.Join(Environment.NewLine, image.WindowTitle ?? string.Empty, rawText ?? string.Empty);
        var gameCode = GameCodeRegex().Match(text).Groups["code"].Value;

        DateTime? startDate = null;
        var timestamp = TimestampRegex().Match(text);
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
            GameCode = string.IsNullOrWhiteSpace(gameCode) ? null : gameCode,
            StartDate = startDate
        };
    }

    [GeneratedRegex(@"ID\s*[:#]\s*(?<code>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GameCodeRegex();

    [GeneratedRegex(@"(?<date>\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2})", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();
}
