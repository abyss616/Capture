using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

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
    private static readonly HashSet<string> ValidSuits = ["h", "s", "d", "c"];
    private static readonly Lazy<Dictionary<string, bool[,]>> SuitTemplates = new(BuildSuitTemplates);

    private readonly IOcrEngine _ocrEngine;

    public OcrHeroCardExtractor(IOcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
    }

    public async Task<Cards?> ExtractHeroCardsAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        var extraction = await TryExtractFromImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (extraction.Success)
        {
            return extraction.Cards;
        }

        Debug.WriteLine($"[HeroRankOCR] Rank-only extraction failed: {extraction.Diagnostics}");
        return null;
    }

    private async Task<HeroCardsExtractionResult> TryExtractFromImageAsync(CapturedImage image, CancellationToken cancellationToken)
    {
        if (image.ImageBytes.Length == 0)
        {
            return HeroCardsExtractionResult.Failed("Hero crop image is empty.");
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
                return HeroCardsExtractionResult.Failed($"Expected 2 card bounds, found {cardBounds.Count}.");
            }
            var cardItems = BuildCardItems(cardBounds);

            var recognizedCards = new List<PlayingCard>(2);
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
                using var preprocessedSuit = PreprocessSuitImage(suitRoiRaw);
                SaveBitmap(preprocessedSuit, Path.Combine(debugDirectory, $"suit_{i}_preprocessed.png"));

                var recognition = await RecognizeRankAsync(preprocessed, image, i, cancellationToken).ConfigureAwait(false);
                if (!recognition.Success)
                {
                    return HeroCardsExtractionResult.Failed($"Card {i} rank: {recognition.Diagnostics}");
                }

                var suitRecognition = await RecognizeSuitAsync(preprocessedSuit, image, i).ConfigureAwait(false);
                if (!suitRecognition.Success)
                {
                    return HeroCardsExtractionResult.Failed($"Card {i} suit: {suitRecognition.Diagnostics}");
                }

                recognizedCards.Add(new PlayingCard
                {
                    Rank = recognition.NormalizedRank!,
                    Suit = suitRecognition.NormalizedSuit!
                });
            }

            return HeroCardsExtractionResult.Succeeded(new Cards { Items = recognizedCards });
        }
        catch (Exception ex)
        {
            return HeroCardsExtractionResult.Failed($"Unhandled exception: {ex.Message}");
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
        var w = Math.Max(4, (int)Math.Round(cardWidth * 0.40));
        var h = Math.Max(4, (int)Math.Round(cardHeight * 0.44));

        return Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, cardWidth, cardHeight));
    }

    /// <summary>
    /// Crops suit glyph from the same corner stack under the rank.
    /// </summary>
    public static Rectangle CropSuitRegion(int cardWidth, int cardHeight)
    {
        var x = (int)Math.Round(cardWidth * 0.06);
        var y = (int)Math.Round(cardHeight * 0.50);
        var w = Math.Max(4, (int)Math.Round(cardWidth * 0.32));
        var h = Math.Max(4, (int)Math.Round(cardHeight * 0.40));

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

    public static Bitmap PreprocessSuitImage(Bitmap suitRoi) => PreprocessRankImage(suitRoi);

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

    private Task<SuitRecognitionResult> RecognizeSuitAsync(Bitmap preprocessedSuitRoi, CapturedImage source, int cardIndex)
    {
        var debugDirectory = EnsureDebugDirectory(source.CapturedAtUtc == default ? DateTime.UtcNow : source.CapturedAtUtc);
        using var trimmedSuit = TrimSuitWhitespace(preprocessedSuitRoi);
        SaveBitmap(trimmedSuit, Path.Combine(debugDirectory, $"suit_{cardIndex}_trimmed.png"));
        using var resizedSuit = ResizeSuitForMatch(trimmedSuit, 64, 64);
        SaveBitmap(resizedSuit, Path.Combine(debugDirectory, $"suit_{cardIndex}_resized.png"));

        var templateMatch = RecognizeSuitFromTemplates(resizedSuit);
        SaveSuitClassificationDebugArtifact(debugDirectory, cardIndex, templateMatch);

        if (templateMatch.Confidence >= 0.55d && templateMatch.NormalizedSuit is not null)
        {
            Debug.WriteLine($"[HeroSuitTemplate] card={cardIndex}; suit={templateMatch.NormalizedSuit}; conf={templateMatch.Confidence:0.000}");
            return Task.FromResult(SuitRecognitionResult.Succeeded(templateMatch.NormalizedSuit, templateMatch.Confidence, "template"));
        }
        if (templateMatch.NormalizedSuit is not null)
        {
            Debug.WriteLine($"[HeroSuitTemplate] card={cardIndex}; low-confidence suit={templateMatch.NormalizedSuit}; conf={templateMatch.Confidence:0.000}");
            return Task.FromResult(SuitRecognitionResult.Succeeded(templateMatch.NormalizedSuit, templateMatch.Confidence, "template_low_confidence"));
        }

        return Task.FromResult(SuitRecognitionResult.Failed($"No valid suit from template classifier. conf={templateMatch.Confidence:0.000}."));
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

    public static string? NormalizeSuit(string? rawText)
    {
        var sanitized = Sanitize(rawText).ToLowerInvariant();
        if (sanitized.Length == 0)
        {
            return null;
        }

        var candidate = sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? sanitized;
        var mapped = candidate switch
        {
            "♠" or "s" or "spade" or "spades" => "s",
            "♥" or "h" or "heart" or "hearts" => "h",
            "♦" or "d" or "diamond" or "diamonds" => "d",
            "♣" or "c" or "club" or "clubs" => "c",
            _ when candidate.Contains('s') => "s",
            _ when candidate.Contains('h') => "h",
            _ when candidate.Contains('d') => "d",
            _ when candidate.Contains('c') => "c",
            _ => null
        };

        return mapped is not null && ValidSuits.Contains(mapped) ? mapped : null;
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

    private static void SaveHeroCardOcrInputArtifact(MemoryStream stream, DateTime capturedAtUtc, int cardIndex, string kind = "rank")
    {
        var debugDirectory = EnsureDebugDirectory(capturedAtUtc == default ? DateTime.UtcNow : capturedAtUtc);
        var artifactPath = Path.Combine(debugDirectory, $"{kind}_{cardIndex}_preprocessed.png");
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

    private sealed record HeroCardsExtractionResult(bool Success, Cards? Cards, string Diagnostics)
    {
        public static HeroCardsExtractionResult Succeeded(Cards cards) => new(true, cards, string.Empty);
        public static HeroCardsExtractionResult Failed(string diagnostics) => new(false, null, diagnostics);
    }

    private sealed record RankRecognitionResult(bool Success, string? NormalizedRank, string Diagnostics)
    {
        public static RankRecognitionResult Succeeded(string rank) => new(true, rank, string.Empty);
        public static RankRecognitionResult Failed(string diagnostics) => new(false, null, diagnostics);
    }

    private sealed record SuitRecognitionResult(bool Success, string? NormalizedSuit, string Diagnostics)
    {
        public static SuitRecognitionResult Succeeded(string suit, double confidence, string source)
            => new(true, suit, $"source={source}; confidence={confidence:0.000}");
        public static SuitRecognitionResult Failed(string diagnostics) => new(false, null, diagnostics);
    }

    private sealed record SuitShapeRecognition(string? NormalizedSuit, double Confidence, Dictionary<string, double> Scores);

    private static SuitShapeRecognition RecognizeSuitFromTemplates(Bitmap preprocessedSuit)
    {
        var mask = BitmapToBinaryMask(preprocessedSuit);
        var scores = ScoreSuitTemplates(mask, ["s", "c", "h", "d"]);
        var ordered = scores.OrderByDescending(kvp => kvp.Value).ToList();
        if (ordered.Count == 0)
        {
            return new SuitShapeRecognition(null, 0d, scores);
        }

        var top = ordered[0];
        var second = ordered.Count > 1 ? ordered[1].Value : 0d;
        var margin = Math.Max(0d, top.Value - second);
        var confidence = Math.Clamp((top.Value * 0.75d) + (margin * 0.25d), 0d, 1d);
        return new SuitShapeRecognition(top.Key, confidence, scores);
    }

    private static Bitmap TrimSuitWhitespace(Bitmap suitRoi, byte backgroundThreshold = 245, int padding = 1)
    {
        var minX = suitRoi.Width;
        var minY = suitRoi.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < suitRoi.Height; y++)
        {
            for (var x = 0; x < suitRoi.Width; x++)
            {
                var px = suitRoi.GetPixel(x, y);
                var lum = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                if (lum >= backgroundThreshold)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return (Bitmap)suitRoi.Clone();
        }

        var left = Math.Max(0, minX - padding);
        var top = Math.Max(0, minY - padding);
        var right = Math.Min(suitRoi.Width - 1, maxX + padding);
        var bottom = Math.Min(suitRoi.Height - 1, maxY + padding);
        var crop = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        return suitRoi.Clone(crop, suitRoi.PixelFormat);
    }

    private static Bitmap ResizeSuitForMatch(Bitmap suit, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(suit, 0, 0, width, height);
        return resized;
    }

    private static bool[,] BitmapToBinaryMask(Bitmap bitmap, byte threshold = 200)
    {
        var mask = new bool[bitmap.Width, bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var px = bitmap.GetPixel(x, y);
                var lum = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                mask[x, y] = lum < threshold;
            }
        }

        return mask;
    }

    private static Dictionary<string, double> ScoreSuitTemplates(bool[,] normalizedMask, IEnumerable<string> candidateSuits)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var suit in candidateSuits)
        {
            if (!SuitTemplates.Value.TryGetValue(suit, out var template))
            {
                continue;
            }

            var intersection = 0;
            var union = 0;
            for (var y = 0; y < normalizedMask.GetLength(1); y++)
            {
                for (var x = 0; x < normalizedMask.GetLength(0); x++)
                {
                    var m = normalizedMask[x, y];
                    var t = template[x, y];
                    if (m && t)
                    {
                        intersection++;
                    }

                    if (m || t)
                    {
                        union++;
                    }
                }
            }

            scores[suit] = union == 0 ? 0d : intersection / (double)union;
        }

        return scores;
    }

    private static Dictionary<string, bool[,]> BuildSuitTemplates()
    {
        return new Dictionary<string, bool[,]>(StringComparer.OrdinalIgnoreCase)
        {
            ["d"] = BuildTemplate(canvas =>
            {
                var points = new[]
                {
                    new PointF(32, 7),
                    new PointF(56, 32),
                    new PointF(32, 57),
                    new PointF(8, 32)
                };
                canvas.FillPolygon(Brushes.Black, points);
            }),
            ["h"] = BuildTemplate(canvas =>
            {
                canvas.FillEllipse(Brushes.Black, 10, 10, 22, 22);
                canvas.FillEllipse(Brushes.Black, 32, 10, 22, 22);
                canvas.FillPolygon(Brushes.Black, [new PointF(6, 25), new PointF(58, 25), new PointF(32, 57)]);
            }),
            ["s"] = BuildTemplate(canvas =>
            {
                canvas.FillEllipse(Brushes.Black, 10, 24, 22, 22);
                canvas.FillEllipse(Brushes.Black, 32, 24, 22, 22);
                canvas.FillPolygon(Brushes.Black, [new PointF(6, 39), new PointF(58, 39), new PointF(32, 8)]);
                canvas.FillRectangle(Brushes.Black, 27, 43, 10, 16);
                canvas.FillEllipse(Brushes.Black, 22, 54, 20, 7);
            }),
            ["c"] = BuildTemplate(canvas =>
            {
                canvas.FillEllipse(Brushes.Black, 21, 7, 22, 22);
                canvas.FillEllipse(Brushes.Black, 8, 24, 22, 22);
                canvas.FillEllipse(Brushes.Black, 34, 24, 22, 22);
                canvas.FillRectangle(Brushes.Black, 27, 39, 10, 18);
                canvas.FillEllipse(Brushes.Black, 22, 53, 20, 8);
            })
        };
    }

    private static bool[,] BuildTemplate(Action<Graphics> draw)
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.Clear(Color.White);
            draw(graphics);
        }

        var mask = new bool[64, 64];
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                mask[x, y] = bitmap.GetPixel(x, y).R < 128;
            }
        }

        return mask;
    }

    private static void SaveSuitClassificationDebugArtifact(string debugDirectory, int cardIndex, SuitShapeRecognition recognition)
    {
        var path = Path.Combine(debugDirectory, $"suit_{cardIndex}_classification.txt");
        var builder = new StringBuilder();
        builder.AppendLine($"top_suit={recognition.NormalizedSuit ?? "n/a"}");
        builder.AppendLine($"confidence={recognition.Confidence:0.000}");
        foreach (var pair in recognition.Scores.OrderByDescending(x => x.Value))
        {
            builder.AppendLine($"{pair.Key}={pair.Value:0.000}");
        }

        File.WriteAllText(path, builder.ToString());
    }
}
