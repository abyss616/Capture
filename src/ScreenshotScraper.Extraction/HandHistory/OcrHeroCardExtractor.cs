using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenshotScraper.Extraction.HandHistory;

/// <summary>
/// Rank-only OCR extractor for hero hole cards.
/// The upstream parser already provides a hero-card crop containing exactly two upright cards.
/// This component detects card bounds, isolates top-left rank glyph ROIs, preprocesses them,
/// and runs PaddleOCR on each ROI separately (recognition-only behavior by tight cropping).
/// </summary>
public sealed class OcrHeroCardExtractor : ICardExtractor
{
    public enum HeroCardItemKind
    {
        Rank = 0,
        Suit = 1
    }

    public readonly record struct HeroCardItem(int CardIndex, HeroCardItemKind Kind, Rectangle Bounds);

    private static readonly HashSet<string> ValidRanks = ["A", "K", "Q", "J", "T", "9", "8", "7", "6", "5", "4", "3", "2"];

    private readonly IOcrEngine _ocrEngine;

    public OcrHeroCardExtractor(IOcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
    }

    public async Task<string> ExtractHeroCardsAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        var extraction = await TryExtractFromImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (extraction.Success)
        {
            return extraction.Ranks;
        }

        Debug.WriteLine($"[HeroRankOCR] Rank-only extraction failed: {extraction.Diagnostics}");
        return string.Empty;
    }

    private async Task<HeroRankExtractionResult> TryExtractFromImageAsync(CapturedImage image, CancellationToken cancellationToken)
    {
        if (image.ImageBytes.Length == 0)
        {
            return HeroRankExtractionResult.Failed("Hero crop image is empty.");
        }

        try
        {
            using var memory = new MemoryStream(image.ImageBytes);
            using var bitmap = new Bitmap(memory);

            var debugDirectory = EnsureDebugDirectory(image.CapturedAtUtc);
            SaveBitmap(bitmap, Path.Combine(debugDirectory, "hero_crop.png"));

            var cardBounds = FindDetectedCardBounds(bitmap);
            if (cardBounds.Count != 2)
            {
                return HeroRankExtractionResult.Failed($"Expected 2 card bounds, found {cardBounds.Count}.");
            }
            var cardItems = BuildCardItems(cardBounds);

            var recognizedRanks = new List<string>(2);
            for (var i = 0; i < cardBounds.Count; i++)
            {
                var cardRect = cardBounds[i];
                using var cardCrop = bitmap.Clone(cardRect, bitmap.PixelFormat);
                SaveBitmap(cardCrop, Path.Combine(debugDirectory, $"card_{i}_{(i == 0 ? "left" : "right")}.png"));

                var rankRoiRect = cardItems
                    .First(item => item.CardIndex == i && item.Kind == HeroCardItemKind.Rank)
                    .Bounds;

                var rankInCardRect = new Rectangle(
                    rankRoiRect.Left - cardRect.Left,
                    rankRoiRect.Top - cardRect.Top,
                    rankRoiRect.Width,
                    rankRoiRect.Height);

                var suitRoiRect = cardItems
                    .First(item => item.CardIndex == i && item.Kind == HeroCardItemKind.Suit)
                    .Bounds;
                var suitInCardRect = new Rectangle(
                    suitRoiRect.Left - cardRect.Left,
                    suitRoiRect.Top - cardRect.Top,
                    suitRoiRect.Width,
                    suitRoiRect.Height);

                using var rankRoiRaw = cardCrop.Clone(rankInCardRect, cardCrop.PixelFormat);
                SaveBitmap(rankRoiRaw, Path.Combine(debugDirectory, $"rank_{i}_raw.png"));
                using var suitRoiRaw = cardCrop.Clone(suitInCardRect, cardCrop.PixelFormat);
                SaveBitmap(suitRoiRaw, Path.Combine(debugDirectory, $"suit_{i}_raw.png"));

                using var preprocessed = PreprocessRankImage(rankRoiRaw);
                SaveBitmap(preprocessed, Path.Combine(debugDirectory, $"rank_{i}_preprocessed.png"));

                var recognition = await RecognizeRankAsync(preprocessed, image, i, cancellationToken).ConfigureAwait(false);
                if (!recognition.Success)
                {
                    return HeroRankExtractionResult.Failed($"Card {i}: {recognition.Diagnostics}");
                }

                recognizedRanks.Add(recognition.NormalizedRank!);
            }

            return HeroRankExtractionResult.Succeeded(string.Join(' ', recognizedRanks));
        }
        catch (Exception ex)
        {
            return HeroRankExtractionResult.Failed($"Unhandled exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds two likely card rectangles by scanning for bright card-face columns, then row extents.
    /// Prefers simple robust heuristics over expensive contour pipelines.
    /// </summary>
    public static List<Rectangle> FindDetectedCardBounds(Bitmap heroCrop)
    {
        var bounds = new Rectangle(0, 0, heroCrop.Width, heroCrop.Height);
        var brightThreshold = 190;
        var minBrightRowsPerColumn = Math.Max(4, heroCrop.Height / 10);
        var minCardWidth = Math.Max(18, heroCrop.Width / 10);

        var brightByColumn = new int[heroCrop.Width];
        for (var x = 0; x < heroCrop.Width; x++)
        {
            var count = 0;
            for (var y = 0; y < heroCrop.Height; y++)
            {
                var px = heroCrop.GetPixel(x, y);
                var luminance = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                if (luminance >= brightThreshold)
                {
                    count++;
                }
            }

            brightByColumn[x] = count;
        }

        var segments = new List<(int Left, int Right)>();
        var segmentStart = -1;
        for (var x = 0; x < brightByColumn.Length; x++)
        {
            var isCardLike = brightByColumn[x] >= minBrightRowsPerColumn;
            if (isCardLike && segmentStart < 0)
            {
                segmentStart = x;
            }

            if (!isCardLike && segmentStart >= 0)
            {
                var end = x - 1;
                if (end - segmentStart + 1 >= minCardWidth)
                {
                    segments.Add((segmentStart, end));
                }

                segmentStart = -1;
            }
        }

        if (segmentStart >= 0)
        {
            var end = brightByColumn.Length - 1;
            if (end - segmentStart + 1 >= minCardWidth)
            {
                segments.Add((segmentStart, end));
            }
        }

        var rectangles = new List<Rectangle>();
        foreach (var segment in segments)
        {
            var top = heroCrop.Height - 1;
            var bottom = 0;
            for (var y = 0; y < heroCrop.Height; y++)
            {
                var brightInRow = 0;
                for (var x = segment.Left; x <= segment.Right; x++)
                {
                    var px = heroCrop.GetPixel(x, y);
                    var luminance = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                    if (luminance >= brightThreshold)
                    {
                        brightInRow++;
                    }
                }

                if (brightInRow > (segment.Right - segment.Left + 1) / 4)
                {
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            if (bottom <= top)
            {
                continue;
            }

            var rect = Rectangle.FromLTRB(segment.Left, top, segment.Right + 1, bottom + 1);
            rect.Inflate(2, 2);
            rect = Rectangle.Intersect(rect, bounds);
            if (rect.Width > 0 && rect.Height > 0)
            {
                rectangles.Add(rect);
            }
        }

        if (rectangles.Count < 2)
        {
            // Fallback: split hero crop into two vertical halves.
            var halfWidth = heroCrop.Width / 2;
            rectangles =
            [
                new Rectangle(0, 0, halfWidth, heroCrop.Height),
                new Rectangle(halfWidth, 0, heroCrop.Width - halfWidth, heroCrop.Height)
            ];
        }

        return rectangles
            .OrderBy(r => r.Left)
            .Take(2)
            .ToList();
    }

    /// <summary>
    /// Builds semantic hero-card items from detected card rectangles.
    /// Returns (left rank, left suit, right rank, right suit) when two cards are available.
    /// </summary>
    public static List<HeroCardItem> FindCardItems(Bitmap heroCrop)
    {
        var cards = FindDetectedCardBounds(heroCrop);
        return BuildCardItems(cards);
    }

    private static List<HeroCardItem> BuildCardItems(IReadOnlyList<Rectangle> cards)
    {
        var items = new List<HeroCardItem>(cards.Count * 2);
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var rankLocal = CropRankRegion(card.Width, card.Height);
            var suitLocal = CropSuitRegion(card.Width, card.Height);

            items.Add(new HeroCardItem(i, HeroCardItemKind.Rank, Translate(rankLocal, card.Left, card.Top)));
            items.Add(new HeroCardItem(i, HeroCardItemKind.Suit, Translate(suitLocal, card.Left, card.Top)));
        }

        return items.OrderBy(item => item.Bounds.Left).ThenBy(item => item.Kind).ToList();
    }

    [Obsolete("Use FindDetectedCardBounds for card-level bounds or FindCardItems for semantic rank/suit items.")]
    public static List<Rectangle> FindCardBounds(Bitmap heroCrop) => FindDetectedCardBounds(heroCrop);

    /// <summary>
    /// Crops rank glyph from top-left card corner.
    /// Recommended starting ratios:
    /// - x=6%, y=5%: skip rounded edge + border.
    /// - w=32%, h=36%: capture full rank (including upper serif/strokes) without suit pip below.
    /// </summary>
    public static Rectangle CropRankRegion(int cardWidth, int cardHeight)
    {
        var x = (int)Math.Round(cardWidth * 0.06);
        var y = (int)Math.Round(cardHeight * 0.05);
        var w = Math.Max(4, (int)Math.Round(cardWidth * 0.32));
        var h = Math.Max(4, (int)Math.Round(cardHeight * 0.36));

        return Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, cardWidth, cardHeight));
    }

    /// <summary>
    /// Crops suit glyph from the same corner stack under the rank.
    /// </summary>
    public static Rectangle CropSuitRegion(int cardWidth, int cardHeight)
    {
        var x = (int)Math.Round(cardWidth * 0.10);
        var y = (int)Math.Round(cardHeight * 0.42);
        var w = Math.Max(4, (int)Math.Round(cardWidth * 0.24));
        var h = Math.Max(4, (int)Math.Round(cardHeight * 0.25));

        return Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, cardWidth, cardHeight));
    }

    private static Rectangle Translate(Rectangle rect, int offsetX, int offsetY)
        => new(rect.Left + offsetX, rect.Top + offsetY, rect.Width, rect.Height);

    public static Bitmap PreprocessRankImage(Bitmap rankRoi)
    {
        // 4x upscale to make tiny glyphs legible for OCR recognizer.
        var upscaled = new Bitmap(rankRoi.Width * 4, rankRoi.Height * 4);
        using (var g = Graphics.FromImage(upscaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rankRoi, 0, 0, upscaled.Width, upscaled.Height);
        }

        var grayscale = new Bitmap(upscaled.Width, upscaled.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < upscaled.Height; y++)
        {
            for (var x = 0; x < upscaled.Width; x++)
            {
                var px = upscaled.GetPixel(x, y);
                var lum = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                var color = Color.FromArgb(lum, lum, lum);
                grayscale.SetPixel(x, y, color);
            }
        }

        upscaled.Dispose();

        // Otsu threshold for adaptive binarization.
        var threshold = ComputeOtsuThreshold(grayscale);
        for (var y = 0; y < grayscale.Height; y++)
        {
            for (var x = 0; x < grayscale.Width; x++)
            {
                var value = grayscale.GetPixel(x, y).R;
                grayscale.SetPixel(x, y, value >= threshold ? Color.White : Color.Black);
            }
        }

        return grayscale;
    }

    private async Task<RankRecognitionResult> RecognizeRankAsync(Bitmap preprocessedRankRoi, CapturedImage source, int cardIndex, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        preprocessedRankRoi.Save(stream, ImageFormat.Png);
        SaveHeroCardOcrInputArtifact(stream, source.CapturedAtUtc, cardIndex);

        var roiImage = new CapturedImage
        {
            ImageBytes = stream.ToArray(),
            Width = preprocessedRankRoi.Width,
            Height = preprocessedRankRoi.Height,
            CapturedAtUtc = source.CapturedAtUtc,
            SourceDescription = source.SourceDescription,
            WindowTitle = source.WindowTitle,
            ProcessName = source.ProcessName
        };

        var ocr = await _ocrEngine.ReadAsync(roiImage, new OcrRequest("hero_rank", "rank_roi", PreferRecognitionOnly: true), cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeRank(ocr.Text, preprocessedRankRoi, ocr.Confidence);

        Debug.WriteLine($"[HeroRankOCR] card={cardIndex}; raw='{Sanitize(ocr.Text)}'; conf={ocr.Confidence?.ToString("0.000") ?? "n/a"}; normalized='{normalized ?? string.Empty}'");

        if (normalized is null)
        {
            return RankRecognitionResult.Failed($"No valid rank from OCR raw='{Sanitize(ocr.Text)}' conf={ocr.Confidence?.ToString("0.000") ?? "n/a"}.");
        }

        return RankRecognitionResult.Succeeded(normalized);
    }

    public static string? NormalizeRank(string? rawText, Bitmap processedBitmap, double? confidence)
    {
        var sanitized = Sanitize(rawText).ToUpperInvariant();
        if (sanitized.Length == 0)
        {
            return null;
        }

        // Common OCR aliases for Ten.
        if (sanitized is "10" or "1O" or "IO" or "L0")
        {
            return "T";
        }

        // Keep only first token for rank-like OCR clutter.
        var candidate = sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? sanitized;
        if (candidate.Length > 1 && candidate != "10")
        {
            candidate = candidate[..1];
        }

        if (candidate == "0")
        {
            // Heuristic gate: only map 0->Q when confidence is reasonable and glyph has interior hole.
            if ((confidence ?? 0d) >= 0.65d && LooksLikeQ(processedBitmap))
            {
                return "Q";
            }

            return null;
        }

        if (candidate == "1")
        {
            // '1' is usually misread Ten leading digit in this UI.
            return "T";
        }

        if (candidate == "O")
        {
            return (confidence ?? 0d) >= 0.75d && LooksLikeQ(processedBitmap)
                ? "Q"
                : null;
        }

        return ValidRanks.Contains(candidate) ? candidate : null;
    }

    private static bool LooksLikeQ(Bitmap processedBitmap)
    {
        // Very light heuristic: Q should be loop-like and not too sparse.
        var black = 0;
        var centerBlack = 0;
        var cx0 = processedBitmap.Width / 4;
        var cx1 = processedBitmap.Width * 3 / 4;
        var cy0 = processedBitmap.Height / 4;
        var cy1 = processedBitmap.Height * 3 / 4;

        for (var y = 0; y < processedBitmap.Height; y++)
        {
            for (var x = 0; x < processedBitmap.Width; x++)
            {
                var isBlack = processedBitmap.GetPixel(x, y).R < 128;
                if (!isBlack)
                {
                    continue;
                }

                black++;
                if (x >= cx0 && x <= cx1 && y >= cy0 && y <= cy1)
                {
                    centerBlack++;
                }
            }
        }

        if (black == 0)
        {
            return false;
        }

        var centerRatio = centerBlack / (double)black;
        return centerRatio is > 0.08 and < 0.45;
    }

    private static int ComputeOtsuThreshold(Bitmap grayscale)
    {
        var histogram = new int[256];
        for (var y = 0; y < grayscale.Height; y++)
        {
            for (var x = 0; x < grayscale.Width; x++)
            {
                histogram[grayscale.GetPixel(x, y).R]++;
            }
        }

        var total = grayscale.Width * grayscale.Height;
        var sum = 0.0;
        for (var i = 0; i < 256; i++)
        {
            sum += i * histogram[i];
        }

        var sumBackground = 0.0;
        var backgroundWeight = 0;
        var bestVariance = -1.0;
        var threshold = 128;

        for (var t = 0; t < 256; t++)
        {
            backgroundWeight += histogram[t];
            if (backgroundWeight == 0)
            {
                continue;
            }

            var foregroundWeight = total - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            sumBackground += t * histogram[t];
            var meanBackground = sumBackground / backgroundWeight;
            var meanForeground = (sum - sumBackground) / foregroundWeight;
            var betweenVariance = backgroundWeight * foregroundWeight * Math.Pow(meanBackground - meanForeground, 2);

            if (betweenVariance > bestVariance)
            {
                bestVariance = betweenVariance;
                threshold = t;
            }
        }

        return threshold;
    }

    private static string EnsureDebugDirectory(DateTime capturedAtUtc)
    {
        var dir = Path.Combine("debug", "output", capturedAtUtc.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SaveHeroCardOcrInputArtifact(MemoryStream stream, DateTime capturedAtUtc, int cardIndex)
    {
        var debugDirectory = EnsureDebugDirectory(capturedAtUtc == default ? DateTime.UtcNow : capturedAtUtc);
        var artifactPath = Path.Combine(debugDirectory, $"rank_{cardIndex}_preprocessed.png");
        if (File.Exists(artifactPath))
        {
            File.Delete(artifactPath);
        }

        File.WriteAllBytes(artifactPath, stream.ToArray());
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
        bitmap.Save(path, ImageFormat.Png);
    }

    private static string Sanitize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private sealed record HeroRankExtractionResult(bool Success, string Ranks, string Diagnostics)
    {
        public static HeroRankExtractionResult Succeeded(string ranks) => new(true, ranks, string.Empty);
        public static HeroRankExtractionResult Failed(string diagnostics) => new(false, string.Empty, diagnostics);
    }

    private sealed record RankRecognitionResult(bool Success, string? NormalizedRank, string Diagnostics)
    {
        public static RankRecognitionResult Succeeded(string rank) => new(true, rank, string.Empty);
        public static RankRecognitionResult Failed(string diagnostics) => new(false, null, diagnostics);
    }
}
