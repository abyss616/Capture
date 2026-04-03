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
    private static readonly Lazy<Dictionary<string, Bitmap>> SuitTemplates = new(LoadSuitTemplates);

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

                var suitRecognition = await RecognizeSuitAsync(suitRoiRaw, image, i).ConfigureAwait(false);
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
        var x = (int)Math.Round(cardWidth * 0.05);
        var y = (int)Math.Round(cardHeight * 0.1);
        var w = Math.Max(4, (int)Math.Round(cardWidth * 0.40));
        var h = Math.Max(4, (int)Math.Round(cardHeight * 0.5));

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

    public static Bitmap PreprocessSuitImage(Bitmap suitRoi)
    {
        // Keep suit preprocessing minimal and shape-preserving for template matching.
        var processed = new Bitmap(suitRoi.Width, suitRoi.Height, PixelFormat.Format24bppRgb);
        const byte threshold = 215;
        for (var y = 0; y < suitRoi.Height; y++)
        {
            for (var x = 0; x < suitRoi.Width; x++)
            {
                var px = suitRoi.GetPixel(x, y);
                var lum = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                processed.SetPixel(x, y, lum < threshold ? Color.Black : Color.White);
            }
        }

        return processed;
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

    private Task<SuitRecognitionResult> RecognizeSuitAsync(Bitmap rawSuitRoi, CapturedImage source, int cardIndex)
    {
        var debugDirectory = EnsureDebugDirectory(source.CapturedAtUtc == default ? DateTime.UtcNow : source.CapturedAtUtc);
        var colorFamily = DetectSuitColorFamily(rawSuitRoi);
        using var suitMask = BuildSuitForegroundMask(rawSuitRoi);
        SaveBitmap(suitMask, Path.Combine(debugDirectory, $"suit_{cardIndex}_mask.png"));
        using var componentCrop = ExtractSuitComponentCrop(rawSuitRoi, out var selectedBounds, out var usedFallback);
        SaveBitmap(componentCrop, Path.Combine(debugDirectory, $"suit_{cardIndex}_component.png"));
        using var resizedSuit = ResizeSuitForMatch(componentCrop, 64, 64);
        SaveBitmap(resizedSuit, Path.Combine(debugDirectory, $"suit_{cardIndex}_resized_padded.png"));
        Debug.WriteLine($"[HeroSuitTemplate] card={cardIndex}; selected_bounds={selectedBounds}; fallback={usedFallback}");

        var templateMatch = RecognizeSuitFromTemplates(resizedSuit, colorFamily);
        SaveSuitClassificationDebugArtifact(debugDirectory, cardIndex, templateMatch);
        Debug.WriteLine($"[HeroSuitTemplate] card={cardIndex}; color_family={templateMatch.ColorFamily}; candidates={string.Join(",", templateMatch.CandidateSuits)}");
        Debug.WriteLine($"[HeroSuitTemplate] card={cardIndex}; scores={string.Join(", ", templateMatch.Scores.OrderByDescending(x => x.Value).Select(x => $"{x.Key}:{x.Value:0.000}"))}");

        if (templateMatch.NormalizedSuit is not null)
        {
            Debug.WriteLine($"[HeroSuitTemplate] card={cardIndex}; suit={templateMatch.NormalizedSuit}; conf={templateMatch.Confidence:0.000}");
            return Task.FromResult(SuitRecognitionResult.Succeeded(templateMatch.NormalizedSuit, templateMatch.Confidence, "template"));
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

    private sealed record SuitShapeRecognition(string? NormalizedSuit, double Confidence, Dictionary<string, double> Scores, string ColorFamily, IReadOnlyList<string> CandidateSuits);

    private static SuitShapeRecognition RecognizeSuitFromTemplates(Bitmap preprocessedSuit, SuitColorFamily colorFamily)
    {
        var candidates = colorFamily switch
        {
            SuitColorFamily.Black => new[] { "s", "c" },
            _ => new[] { "h", "d" }
        };
        var scores = ScoreSuitTemplates(preprocessedSuit, candidates);
        var ordered = scores.OrderByDescending(kvp => kvp.Value).ToList();
        if (ordered.Count == 0)
        {
            return new SuitShapeRecognition(null, 0d, scores, colorFamily.ToString().ToLowerInvariant(), candidates);
        }

        var top = ordered[0];
        return new SuitShapeRecognition(top.Key, top.Value, scores, colorFamily.ToString().ToLowerInvariant(), candidates);
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

    private static Bitmap BuildSuitForegroundMask(Bitmap suitRoi)
    {
        var mask = new Bitmap(suitRoi.Width, suitRoi.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < suitRoi.Height; y++)
        {
            for (var x = 0; x < suitRoi.Width; x++)
            {
                var foreground = IsSuitForeground(suitRoi.GetPixel(x, y));
                mask.SetPixel(x, y, foreground ? Color.Black : Color.White);
            }
        }

        return mask;
    }

    private static bool IsSuitForeground(Color c)
    {
        var luminance = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
        var maxChannel = Math.Max(c.R, Math.Max(c.G, c.B));
        var minChannel = Math.Min(c.R, Math.Min(c.G, c.B));
        var channelSpread = maxChannel - minChannel;
        var redDominant = c.R >= c.G + 16 && c.R >= c.B + 16;
        var darkPixel = luminance <= 210;
        var chromaticPixel = channelSpread >= 24;
        var lightNeutralBackground = luminance >= 232 && channelSpread <= 18;

        if (lightNeutralBackground)
        {
            return false;
        }

        return darkPixel || chromaticPixel || redDominant;
    }

    private static Bitmap ExtractSuitComponentCrop(Bitmap suitRoi, out Rectangle selectedBounds, out bool usedFallback)
    {
        var mask = new bool[suitRoi.Width, suitRoi.Height];
        for (var y = 0; y < suitRoi.Height; y++)
        {
            for (var x = 0; x < suitRoi.Width; x++)
            {
                mask[x, y] = IsSuitForeground(suitRoi.GetPixel(x, y));
            }
        }

        var components = ExtractConnectedComponents(mask, suitRoi.Width, suitRoi.Height);
        var best = SelectBestSuitComponent(components, suitRoi.Width, suitRoi.Height);

        if (best is null)
        {
            usedFallback = true;
            selectedBounds = GetFallbackSuitBounds(suitRoi.Width, suitRoi.Height);
        }
        else
        {
            usedFallback = false;
            var bestValue = best.Value;
            selectedBounds = InflateWithin(bestValue.Bounds, suitRoi.Width, suitRoi.Height, Math.Max(1, Math.Min(3, Math.Min(suitRoi.Width, suitRoi.Height) / 10)));
        }

        return suitRoi.Clone(selectedBounds, suitRoi.PixelFormat);
    }

    private static List<SuitComponent> ExtractConnectedComponents(bool[,] mask, int width, int height)
    {
        var visited = new bool[width, height];
        var components = new List<SuitComponent>();
        var neighbors = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mask[x, y] || visited[x, y])
                {
                    continue;
                }

                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                var minX = x;
                var minY = y;
                var maxX = x;
                var maxY = y;
                var area = 0;
                var touchesTop = false;
                var touchesLeft = false;
                var touchesRight = false;
                var touchesBottom = false;

                while (queue.Count > 0)
                {
                    var point = queue.Dequeue();
                    area++;
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                    touchesTop |= point.Y == 0;
                    touchesLeft |= point.X == 0;
                    touchesRight |= point.X == width - 1;
                    touchesBottom |= point.Y == height - 1;

                    foreach (var (dx, dy) in neighbors)
                    {
                        var nx = point.X + dx;
                        var ny = point.Y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        if (visited[nx, ny] || !mask[nx, ny])
                        {
                            continue;
                        }

                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                components.Add(new SuitComponent(
                    Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1),
                    area,
                    touchesTop,
                    touchesBottom,
                    touchesLeft,
                    touchesRight));
            }
        }

        return components;
    }

    private static SuitComponent? SelectBestSuitComponent(IEnumerable<SuitComponent> components, int width, int height)
    {
        var minArea = Math.Max(8, (width * height) / 140);
        var rankBoundaryY = (int)Math.Round(height * 0.20);
        var leftMiddleLimit = (int)Math.Round(width * 0.75);
        var lowerHalfY = (int)Math.Round(height * 0.40);
        var lowerLeftRegionWidth = (int)Math.Round(width * 0.45);

        var candidates = new List<(SuitComponent Component, double Score)>();
        foreach (var component in components)
        {
            if (component.Area < minArea)
            {
                continue;
            }

            if (component.Bounds.Top < rankBoundaryY)
            {
                continue;
            }

            if (component.Bounds.Left > leftMiddleLimit)
            {
                continue;
            }

            var centerX = component.Bounds.Left + (component.Bounds.Width / 2.0);
            var centerY = component.Bounds.Top + (component.Bounds.Height / 2.0);
            double score = component.Area;
            score += centerY * 1.6;
            score += component.Bounds.Top * 0.8;
            if (centerX <= lowerLeftRegionWidth && centerY >= lowerHalfY)
            {
                score += component.Area * 0.7;
            }

            if (component.TouchesTop)
            {
                score -= component.Area * 0.5;
            }

            if (component.TouchesLeft || component.TouchesRight)
            {
                score -= component.Area * 0.2;
            }

            candidates.Add((component, score));
        }

        return candidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Component.Area)
            .Select(x => x.Component)
            .FirstOrDefault();
    }

    private static Rectangle GetFallbackSuitBounds(int width, int height)
    {
        var x = 0;
        var y = (int)Math.Round(height * 0.35);
        var w = Math.Max(2, (int)Math.Round(width * 0.60));
        var h = Math.Max(2, (int)Math.Round(height * 0.60));
        var fallback = new Rectangle(x, y, w, h);
        return Rectangle.Intersect(fallback, new Rectangle(0, 0, width, height));
    }

    private static Rectangle InflateWithin(Rectangle bounds, int width, int height, int padding)
    {
        var left = Math.Max(0, bounds.Left - padding);
        var top = Math.Max(0, bounds.Top - padding);
        var right = Math.Min(width, bounds.Right + padding);
        var bottom = Math.Min(height, bounds.Bottom + padding);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Bitmap ResizeSuitForMatch(Bitmap suit, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var scale = Math.Min(width / (double)suit.Width, height / (double)suit.Height);
        var scaledWidth = Math.Max(1, (int)Math.Round(suit.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(suit.Height * scale));
        var offsetX = (width - scaledWidth) / 2;
        var offsetY = (height - scaledHeight) / 2;
        graphics.DrawImage(suit, offsetX, offsetY, scaledWidth, scaledHeight);
        return resized;
    }

    private static SuitColorFamily DetectSuitColorFamily(Bitmap suitRoi)
    {
        var foregroundPixels = 0;
        var redDominantPixels = 0;

        for (var y = 0; y < suitRoi.Height; y++)
        {
            for (var x = 0; x < suitRoi.Width; x++)
            {
                var px = suitRoi.GetPixel(x, y);
                var lum = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                if (lum > 245)
                {
                    continue;
                }

                foregroundPixels++;
                if (px.R > px.G + 12 && px.R > px.B + 12)
                {
                    redDominantPixels++;
                }
            }
        }

        if (foregroundPixels == 0)
        {
            return SuitColorFamily.Black;
        }

        var redRatio = redDominantPixels / (double)foregroundPixels;
        return redRatio >= 0.12d ? SuitColorFamily.Red : SuitColorFamily.Black;
    }

    private static Dictionary<string, double> ScoreSuitTemplates(Bitmap normalizedSuit, IEnumerable<string> candidateSuits)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var suit in candidateSuits)
        {
            if (!SuitTemplates.Value.TryGetValue(suit, out var template))
            {
                continue;
            }

            var totalDifference = 0d;
            var pixels = normalizedSuit.Width * normalizedSuit.Height;
            for (var y = 0; y < normalizedSuit.Height; y++)
            {
                for (var x = 0; x < normalizedSuit.Width; x++)
                {
                    var input = normalizedSuit.GetPixel(x, y).R;
                    var target = template.GetPixel(x, y).R;
                    totalDifference += Math.Abs(input - target);
                }
            }

            var averageDifference = totalDifference / pixels;
            scores[suit] = Math.Clamp(1d - (averageDifference / 255d), 0d, 1d);
        }

        return scores;
    }

    private static Dictionary<string, Bitmap> LoadSuitTemplates()
    {
        var templateDirectory = GetSuitTemplateDirectory("normalized");
        if (!Directory.Exists(templateDirectory))
        {
            Directory.CreateDirectory(templateDirectory);
        }

        GenerateSuitTemplatesFromRawGlyphs(templateDirectory);

        var templates = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        foreach (var suit in ValidSuits)
        {
            var templatePath = Path.Combine(templateDirectory, $"{suit}.png");
            if (!File.Exists(templatePath))
            {
                continue;
            }

            templates[suit] = new Bitmap(templatePath);
        }

        return templates;
    }

    private static void GenerateSuitTemplatesFromRawGlyphs(string templateDirectory)
    {
        var rawDirectory = GetSuitTemplateDirectory("raw");
        if (!Directory.Exists(rawDirectory))
        {
            return;
        }

        var generationDebugDirectory = Path.Combine(
            EnsureDebugDirectory(DateTime.UtcNow),
            "suit_template_generation");
        Directory.CreateDirectory(generationDebugDirectory);

        foreach (var suit in ValidSuits)
        {
            var sourcePath = ResolveTemplateSourcePath(rawDirectory, suit);
            if (sourcePath is null)
            {
                continue;
            }

            using var rawGlyph = new Bitmap(sourcePath);
            SaveBitmap(rawGlyph, Path.Combine(generationDebugDirectory, $"{suit}_raw.png"));

            using var preprocessed = PreprocessSuitImage(rawGlyph);
            SaveBitmap(preprocessed, Path.Combine(generationDebugDirectory, $"{suit}_preprocessed.png"));

            using var trimmed = TrimSuitWhitespace(preprocessed);
            SaveBitmap(trimmed, Path.Combine(generationDebugDirectory, $"{suit}_trimmed.png"));

            using var normalized = ResizeSuitForMatch(trimmed, 64, 64);
            SaveBitmap(normalized, Path.Combine(generationDebugDirectory, $"{suit}_normalized.png"));
            SaveBitmap(normalized, Path.Combine(templateDirectory, $"{suit}.png"));
        }
    }

    private static string? ResolveTemplateSourcePath(string rawDirectory, string suit)
    {
        var candidates = new[] { ".png", ".bmp", ".jpg", ".jpeg", ".webp" }
            .Select(extension => Path.Combine(rawDirectory, $"{suit}{extension}"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetSuitTemplateDirectory(string subdirectory)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "SuitTemplates", subdirectory);
    }

    private static void SaveSuitClassificationDebugArtifact(string debugDirectory, int cardIndex, SuitShapeRecognition recognition)
    {
        var path = Path.Combine(debugDirectory, $"suit_{cardIndex}_classification.txt");
        var builder = new StringBuilder();
        builder.AppendLine($"color_family={recognition.ColorFamily}");
        builder.AppendLine($"candidates={string.Join(",", recognition.CandidateSuits)}");
        builder.AppendLine($"top_suit={recognition.NormalizedSuit ?? "n/a"}");
        builder.AppendLine($"confidence={recognition.Confidence:0.000}");
        foreach (var pair in recognition.Scores.OrderByDescending(x => x.Value))
        {
            builder.AppendLine($"{pair.Key}={pair.Value:0.000}");
        }

        File.WriteAllText(path, builder.ToString());
    }

    private enum SuitColorFamily
    {
        Black = 1,
        Red = 2
    }

    private readonly record struct SuitComponent(
        Rectangle Bounds,
        int Area,
        bool TouchesTop,
        bool TouchesBottom,
        bool TouchesLeft,
        bool TouchesRight);
}
