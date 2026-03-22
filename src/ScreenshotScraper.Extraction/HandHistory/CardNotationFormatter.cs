using System.Text.RegularExpressions;

namespace ScreenshotScraper.Extraction.HandHistory;

public static partial class CardNotationFormatter
{
    private static readonly Dictionary<string, string> SuitMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S"] = "S",
        ["♠"] = "S",
        ["SPADE"] = "S",
        ["SPADES"] = "S",
        ["H"] = "H",
        ["♥"] = "H",
        ["HEART"] = "H",
        ["HEARTS"] = "H",
        ["D"] = "D",
        ["♦"] = "D",
        ["DIAMOND"] = "D",
        ["DIAMONDS"] = "D",
        ["C"] = "C",
        ["♣"] = "C",
        ["CLUB"] = "C",
        ["CLUBS"] = "C"
    };

    private static readonly Dictionary<string, string> RankMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "A",
        ["ACE"] = "A",
        ["K"] = "K",
        ["KING"] = "K",
        ["Q"] = "Q",
        ["QUEEN"] = "Q",
        ["J"] = "J",
        ["JACK"] = "J",
        ["10"] = "10",
        ["T"] = "10",
        ["TEN"] = "10",
        ["9"] = "9",
        ["8"] = "8",
        ["7"] = "7",
        ["6"] = "6",
        ["5"] = "5",
        ["4"] = "4",
        ["3"] = "3",
        ["2"] = "2",
    };

    public static bool TryNormalize(string rank, string suit, out string card)
    {
        card = string.Empty;

        if (!RankMap.TryGetValue(rank.Trim(), out var normalizedRank) ||
            !SuitMap.TryGetValue(suit.Trim(), out var normalizedSuit))
        {
            return false;
        }

        card = $"{normalizedSuit}{normalizedRank}";
        return true;
    }

    public static bool TryNormalizeToken(string token, out string card)
    {
        card = string.Empty;
        var candidate = token.Trim().ToUpperInvariant().Replace(" ", string.Empty);

        if (candidate.Length < 2)
        {
            return false;
        }

        var first = candidate[..1];
        var rest = candidate[1..];
        if (SuitMap.ContainsKey(first) && RankMap.ContainsKey(rest))
        {
            card = $"{SuitMap[first]}{RankMap[rest]}";
            return true;
        }

        var last = candidate[^1..];
        var leading = candidate[..^1];
        if (RankMap.ContainsKey(leading) && SuitMap.ContainsKey(last))
        {
            card = $"{SuitMap[last]}{RankMap[leading]}";
            return true;
        }

        return false;
    }

    public static bool TryNormalizePairFromOcrText(string rawText, out string cards)
    {
        cards = string.Empty;
        var matches = CardTokenRegex().Matches(rawText ?? string.Empty);
        var normalizedCards = new List<string>();

        foreach (Match match in matches)
        {
            var value = match.Groups["value"].Value;
            if (TryNormalizeToken(value, out var card))
            {
                normalizedCards.Add(card);
            }
            else if (TryNormalize(match.Groups["rank"].Value, match.Groups["suit"].Value, out card))
            {
                normalizedCards.Add(card);
            }

            if (normalizedCards.Count == 2)
            {
                cards = string.Join(' ', normalizedCards);
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?<value>(?:10|[2-9AJQKT])[SHDC])|(?<rank>10|[2-9AJQKT])\s*(?<suit>[♠♥♦♣SHDC])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CardTokenRegex();
}
