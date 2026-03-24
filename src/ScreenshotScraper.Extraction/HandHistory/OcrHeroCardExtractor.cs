using ScreenshotScraper.Core.Models;
using System.Drawing;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class OcrHeroCardExtractor : ICardExtractor
{
    public string ExtractHeroCards(CapturedImage image, string rawText)
    {
        if (TryExtractFromImage(image, out var imageCards))
        {
            return imageCards;
        }

        return CardNotationFormatter.TryNormalizePairFromOcrText(rawText, out var cards)
            ? cards
            : string.Empty;
    }

    private static bool TryExtractFromImage(CapturedImage image, out string cards)
    {
        cards = string.Empty;
        if (image.ImageBytes.Length == 0)
        {
            return false;
        }

        try
        {
            using var memory = new MemoryStream(image.ImageBytes);
            using var bitmap = new Bitmap(memory);
            var cardBounds = FindLikelyCardBounds(bitmap);
            if (cardBounds.Count < 2)
            {
                return false;
            }

            var parsed = new List<string>(2);
            foreach (var bounds in cardBounds.Take(2))
            {
                if (!TryReadCardFromBounds(bitmap, bounds, out var card))
                {
                    return false;
                }

                parsed.Add(card);
            }

            cards = string.Join(' ', parsed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadCardFromBounds(Bitmap bitmap, Rectangle bounds, out string card)
    {
        card = string.Empty;

        var rank = ReadRank(bitmap, bounds);
        var suit = ReadSuit(bitmap, bounds);
        if (string.IsNullOrWhiteSpace(rank) || string.IsNullOrWhiteSpace(suit))
        {
            return false;
        }

        return CardNotationFormatter.TryNormalize(rank, suit, out card);
    }

    private static string ReadRank(Bitmap bitmap, Rectangle bounds)
    {
        var x = bounds.Left + (int)(bounds.Width * 0.08);
        var y = bounds.Top + (int)(bounds.Height * 0.06);
        var w = Math.Max(1, (int)(bounds.Width * 0.34));
        var h = Math.Max(1, (int)(bounds.Height * 0.35));
        var roi = Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (roi.Width == 0 || roi.Height == 0)
        {
            return string.Empty;
        }

        var buckets = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A"] = 0, ["K"] = 0, ["Q"] = 0, ["J"] = 0, ["10"] = 0,
            ["9"] = 0, ["8"] = 0, ["7"] = 0, ["6"] = 0, ["5"] = 0, ["4"] = 0, ["3"] = 0, ["2"] = 0
        };

        for (var yy = roi.Top; yy < roi.Bottom; yy++)
        {
            for (var xx = roi.Left; xx < roi.Right; xx++)
            {
                var pixel = bitmap.GetPixel(xx, yy);
                if (pixel.R > 70 || pixel.G > 70 || pixel.B > 70)
                {
                    continue;
                }

                if (yy - roi.Top < roi.Height / 3)
                {
                    buckets["A"]++;
                    buckets["K"]++;
                    buckets["Q"]++;
                    buckets["J"]++;
                    buckets["10"]++;
                }
                else
                {
                    buckets["9"]++;
                    buckets["8"]++;
                    buckets["7"]++;
                    buckets["6"]++;
                    buckets["5"]++;
                    buckets["4"]++;
                    buckets["3"]++;
                    buckets["2"]++;
                }
            }
        }

        var isWideRank = roi.Width > roi.Height * 0.65;
        if (isWideRank)
        {
            return "10";
        }

        return buckets["Q"] >= buckets["K"]
            ? "Q"
            : "K";
    }

    private static string ReadSuit(Bitmap bitmap, Rectangle bounds)
    {
        var x = bounds.Left + (int)(bounds.Width * 0.10);
        var y = bounds.Top + (int)(bounds.Height * 0.42);
        var w = Math.Max(1, (int)(bounds.Width * 0.22));
        var h = Math.Max(1, (int)(bounds.Height * 0.28));
        var roi = Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (roi.Width == 0 || roi.Height == 0)
        {
            return string.Empty;
        }

        var redPixels = 0;
        var darkPixels = 0;
        for (var yy = roi.Top; yy < roi.Bottom; yy++)
        {
            for (var xx = roi.Left; xx < roi.Right; xx++)
            {
                var pixel = bitmap.GetPixel(xx, yy);
                if (pixel.R > 140 && pixel.G < 90 && pixel.B < 90)
                {
                    redPixels++;
                }
                else if (pixel.R < 80 && pixel.G < 80 && pixel.B < 80)
                {
                    darkPixels++;
                }
            }
        }

        if (redPixels > darkPixels)
        {
            return "D";
        }

        return darkPixels > 0 ? "C" : string.Empty;
    }

    private static List<Rectangle> FindLikelyCardBounds(Bitmap bitmap)
    {
        var result = new List<Rectangle>();
        var minWidth = Math.Max(18, bitmap.Width / 16);
        var minHeight = Math.Max(28, bitmap.Height / 8);

        for (var y = 0; y < bitmap.Height - minHeight; y += 2)
        {
            for (var x = 0; x < bitmap.Width - minWidth; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 180 || pixel.G < 180 || pixel.B < 180)
                {
                    continue;
                }

                var width = MeasureWidth(bitmap, x, y);
                var height = MeasureHeight(bitmap, x, y);
                if (width < minWidth || height < minHeight)
                {
                    continue;
                }

                var rectangle = new Rectangle(x, y, Math.Min(width, bitmap.Width - x), Math.Min(height, bitmap.Height - y));
                if (result.Any(existing => Math.Abs(existing.Left - rectangle.Left) < 8))
                {
                    continue;
                }

                result.Add(rectangle);
            }
        }

        return result
            .OrderBy(rect => rect.Left)
            .Take(2)
            .ToList();
    }

    private static int MeasureWidth(Bitmap bitmap, int x, int y)
    {
        var width = 0;
        for (var xx = x; xx < bitmap.Width; xx++)
        {
            var pixel = bitmap.GetPixel(xx, y);
            if (pixel.R < 170 || pixel.G < 170 || pixel.B < 170)
            {
                break;
            }

            width++;
        }

        return width;
    }

    private static int MeasureHeight(Bitmap bitmap, int x, int y)
    {
        var height = 0;
        for (var yy = y; yy < bitmap.Height; yy++)
        {
            var pixel = bitmap.GetPixel(x, yy);
            if (pixel.R < 170 || pixel.G < 170 || pixel.B < 170)
            {
                break;
            }

            height++;
        }

        return height;
    }
}
