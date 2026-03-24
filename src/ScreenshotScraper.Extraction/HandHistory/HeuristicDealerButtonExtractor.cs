using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed partial class HeuristicDealerButtonExtractor : IDealerButtonExtractor
{
    private static readonly IReadOnlyDictionary<int, PointF> SeatAnchors = new Dictionary<int, PointF>
    {
        [1] = new PointF(0.60f, 0.75f), // bottom-center(hero): button drawn to the player's right side
        [2] = new PointF(0.33f, 0.72f), // bottom-left
        [3] = new PointF(0.22f, 0.38f), // top-left
        [4] = new PointF(0.56f, 0.24f), // top-center
        [5] = new PointF(0.86f, 0.38f), // top-right
        [6] = new PointF(0.87f, 0.72f)  // bottom-right
    };

    public ExtractedField DetectDealerSeat(CapturedImage image, string rawText, IReadOnlyList<SnapshotPlayer> players)
    {
        var imageResult = TryDetectDealerFromImage(image);
        if (imageResult.Field is not null)
        {
            return imageResult.Field;
        }

        var fallbackField = DetectDealerFromText(rawText, players);
        return new ExtractedField
        {
            Name = fallbackField.Name,
            RawText = fallbackField.RawText,
            ParsedValue = fallbackField.ParsedValue,
            IsValid = fallbackField.IsValid,
            Error = fallbackField.Error,
            Confidence = fallbackField.Confidence,
            Reason = string.IsNullOrWhiteSpace(imageResult.FailureReason)
                ? fallbackField.Reason
                : $"{imageResult.FailureReason} Falling back to OCR/text heuristics. {fallbackField.Reason}"
        };
    }

    private static ImageDetectionResult TryDetectDealerFromImage(CapturedImage image)
    {
        if (image.ImageBytes.Length == 0)
        {
            return new ImageDetectionResult(null, "Image-based dealer detection skipped: screenshot bytes are empty.");
        }

        using var memoryStream = new MemoryStream(image.ImageBytes);
        using var bitmap = new Bitmap(memoryStream);
        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0)
        {
            return new ImageDetectionResult(null, "Image-based dealer detection skipped: screenshot dimensions are invalid.");
        }

        var roiRadius = Math.Max(10, (int)Math.Round(Math.Min(width, height) * 0.05));
        var minComponentArea = Math.Max(45, (int)Math.Round(roiRadius * roiRadius * 0.2));
        var seatScores = new List<SeatButtonScore>(SeatAnchors.Count);
        foreach (var anchor in SeatAnchors.OrderBy(pair => pair.Key))
        {
            var centerX = (int)Math.Round(anchor.Value.X * width);
            var centerY = (int)Math.Round(anchor.Value.Y * height);
            var roi = Rectangle.FromLTRB(
                Math.Max(0, centerX - roiRadius),
                Math.Max(0, centerY - roiRadius),
                Math.Min(width, centerX + roiRadius),
                Math.Min(height, centerY + roiRadius));

            var score = ScoreSeatRoi(bitmap, anchor.Key, roi, minComponentArea);
            seatScores.Add(score);
        }

        var best = seatScores.OrderByDescending(score => score.TotalScore).First();
        var secondBest = seatScores.Where(score => score.Seat != best.Seat).OrderByDescending(score => score.TotalScore).First();
        var debugOutput = BuildDebugOutput(seatScores);
        var seatChoiceIsConfident = best.TotalScore >= 0.45 && (best.TotalScore - secondBest.TotalScore) >= 0.08;
        if (!seatChoiceIsConfident)
        {
            return new ImageDetectionResult(null, $"Image-based dealer detection failed confidence gate. {debugOutput}");
        }

        return new ImageDetectionResult(
            new ExtractedField
            {
                Name = "DealerSeat",
                RawText = debugOutput,
                ParsedValue = best.Seat.ToString(CultureInfo.InvariantCulture),
                IsValid = true,
                Error = null,
                Confidence = Math.Min(0.99, Math.Max(0.55, best.TotalScore)),
                Reason = $"Detected yellow circular dealer token in seat {best.Seat} ROI. {debugOutput}"
            },
            null);
    }

    private static string BuildDebugOutput(IEnumerable<SeatButtonScore> seatScores)
    {
        var scoreSummary = string.Join(
            "; ",
            seatScores.OrderBy(score => score.Seat).Select(score =>
                $"S{score.Seat}:score={score.TotalScore:0.000},yellow={score.YellowRatio:0.000},circle={score.Circularity:0.000},darkCenter={score.DarkCenterRatio:0.000}"));
        var chosen = seatScores.OrderByDescending(score => score.TotalScore).First();
        return $"Per-seat ROI scores => {scoreSummary}. Chosen seat={chosen.Seat}.";
    }

    private static SeatButtonScore ScoreSeatRoi(Bitmap bitmap, int seat, Rectangle roi, int minComponentArea)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new SeatButtonScore(seat, 0, 0, 0, 0);
        }

        var yellowMask = new bool[roi.Width, roi.Height];
        var yellowPixels = 0;
        for (var y = 0; y < roi.Height; y++)
        {
            for (var x = 0; x < roi.Width; x++)
            {
                var pixel = bitmap.GetPixel(roi.Left + x, roi.Top + y);
                var isYellow = IsDealerYellow(pixel);
                yellowMask[x, y] = isYellow;
                if (isYellow)
                {
                    yellowPixels++;
                }
            }
        }

        var totalPixels = roi.Width * roi.Height;
        var yellowRatio = totalPixels > 0 ? (double)yellowPixels / totalPixels : 0;
        if (yellowPixels < minComponentArea)
        {
            return new SeatButtonScore(seat, yellowRatio, 0, 0, yellowRatio * 0.5);
        }

        var bestComponent = FindBestYellowComponent(yellowMask, minComponentArea);
        if (bestComponent is null)
        {
            return new SeatButtonScore(seat, yellowRatio, 0, 0, yellowRatio * 0.55);
        }

        var darkCenterRatio = MeasureDarkCenterRatio(bitmap, roi, bestComponent.Value.Bounds);
        var totalScore = (yellowRatio * 0.4) + (bestComponent.Value.Circularity * 0.45) + (darkCenterRatio * 0.15);
        return new SeatButtonScore(seat, yellowRatio, bestComponent.Value.Circularity, darkCenterRatio, totalScore);
    }

    private static ConnectedComponent? FindBestYellowComponent(bool[,] yellowMask, int minComponentArea)
    {
        var width = yellowMask.GetLength(0);
        var height = yellowMask.GetLength(1);
        var visited = new bool[width, height];
        ConnectedComponent? best = null;
        var queue = new Queue<(int X, int Y)>();
        var directions = new (int dX, int dY)[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!yellowMask[x, y] || visited[x, y])
                {
                    continue;
                }

                queue.Clear();
                queue.Enqueue((x, y));
                visited[x, y] = true;
                var area = 0;
                var perimeter = 0;
                var minX = x;
                var maxX = x;
                var minY = y;
                var maxY = y;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    area++;
                    minX = Math.Min(minX, current.X);
                    maxX = Math.Max(maxX, current.X);
                    minY = Math.Min(minY, current.Y);
                    maxY = Math.Max(maxY, current.Y);

                    foreach (var direction in directions)
                    {
                        var nextX = current.X + direction.dX;
                        var nextY = current.Y + direction.dY;
                        if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height || !yellowMask[nextX, nextY])
                        {
                            perimeter++;
                            continue;
                        }

                        if (visited[nextX, nextY])
                        {
                            continue;
                        }

                        visited[nextX, nextY] = true;
                        queue.Enqueue((nextX, nextY));
                    }
                }

                if (area < minComponentArea || perimeter <= 0)
                {
                    continue;
                }

                var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                var aspectRatio = bounds.Height > 0 ? (double)bounds.Width / bounds.Height : 0;
                var normalizedAspect = 1.0 - Math.Min(1.0, Math.Abs(aspectRatio - 1.0));
                var circularity = (4 * Math.PI * area) / (perimeter * perimeter);
                var adjustedCircularity = circularity * (0.8 + (0.2 * normalizedAspect));

                if (best is null || adjustedCircularity > best.Value.Circularity)
                {
                    best = new ConnectedComponent(bounds, adjustedCircularity);
                }
            }
        }

        return best;
    }

    private static double MeasureDarkCenterRatio(Bitmap bitmap, Rectangle roi, Rectangle bounds)
    {
        var centerBounds = Rectangle.FromLTRB(
            roi.Left + bounds.Left + (int)(bounds.Width * 0.3),
            roi.Top + bounds.Top + (int)(bounds.Height * 0.3),
            roi.Left + bounds.Left + (int)(bounds.Width * 0.7),
            roi.Top + bounds.Top + (int)(bounds.Height * 0.7));

        if (centerBounds.Width <= 0 || centerBounds.Height <= 0)
        {
            return 0;
        }

        var darkPixels = 0;
        var total = centerBounds.Width * centerBounds.Height;
        for (var y = centerBounds.Top; y < centerBounds.Bottom; y++)
        {
            for (var x = centerBounds.Left; x < centerBounds.Right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var brightness = (pixel.R + pixel.G + pixel.B) / (3.0 * 255.0);
                if (brightness < 0.35)
                {
                    darkPixels++;
                }
            }
        }

        return total > 0 ? (double)darkPixels / total : 0;
    }

    private static bool IsDealerYellow(Color color)
    {
        var (hue, saturation, value) = ToHsv(color);
        return hue is >= 38 and <= 72 && saturation >= 0.4 && value >= 0.45;
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;

        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        double hue;
        if (delta == 0)
        {
            hue = 0;
        }
        else if (Math.Abs(max - red) < double.Epsilon)
        {
            hue = 60 * (((green - blue) / delta) % 6);
        }
        else if (Math.Abs(max - green) < double.Epsilon)
        {
            hue = 60 * (((blue - red) / delta) + 2);
        }
        else
        {
            hue = 60 * (((red - green) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static ExtractedField DetectDealerFromText(string rawText, IReadOnlyList<SnapshotPlayer> players)
    {
        var text = rawText ?? string.Empty;
        var marker = DealerSeatRegex().Match(text);
        if (marker.Success && int.TryParse(marker.Groups["seat"].Value, out var markerSeat) && markerSeat is >= 1 and <= 6)
        {
            return BuildDealerField(text, markerSeat, 1.0, "Matched an explicit dealer-seat marker in OCR text.");
        }

        var regionDealer = players.FirstOrDefault(player => player.Dealer && !string.IsNullOrWhiteSpace(player.Name));
        if (regionDealer is not null)
        {
            return BuildDealerField(text, regionDealer.Seat, 0.95, $"Matched dealer text inside seat {regionDealer.Seat}'s OCR region.");
        }

        var indexedPlayer = players
            .Select(player => new { player.Seat, player.Name })
            .FirstOrDefault(player => !string.IsNullOrWhiteSpace(player.Name) && DealerNameRegex(player.Name!).IsMatch(text));
        if (indexedPlayer is not null)
        {
            return BuildDealerField(text, indexedPlayer.Seat, 0.7, $"Matched the player name '{indexedPlayer.Name}' near a dealer keyword.");
        }

        return new ExtractedField
        {
            Name = "DealerSeat",
            RawText = text,
            ParsedValue = null,
            IsValid = false,
            Error = "Dealer button was not confidently detected.",
            Confidence = 0,
            Reason = "No explicit dealer marker or reliable seat-local dealer text was found."
        };
    }

    private static ExtractedField BuildDealerField(string rawText, int seat, double confidence, string reason)
    {
        return new ExtractedField
        {
            Name = "DealerSeat",
            RawText = rawText,
            ParsedValue = seat.ToString(),
            IsValid = true,
            Error = null,
            Confidence = confidence,
            Reason = reason
        };
    }

    [GeneratedRegex(@"DEALER\s*SEAT\s*(?<seat>[1-6])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DealerSeatRegex();

    private static Regex DealerNameRegex(string playerName)
    {
        return new Regex($@"\b{Regex.Escape(playerName)}\b.*\bdealer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private readonly record struct ConnectedComponent(Rectangle Bounds, double Circularity);

    private readonly record struct SeatButtonScore(int Seat, double YellowRatio, double Circularity, double DarkCenterRatio, double TotalScore);

    private readonly record struct ImageDetectionResult(ExtractedField? Field, string? FailureReason);
}
