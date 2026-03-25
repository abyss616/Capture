using OpenCvSharp;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class OpenCvTableVisionDetector : ITableVisionDetector
{
    private readonly SixMaxTableVisionLayout _layout;
    private readonly OpenCvTableVisionDetectorOptions _options;
    private readonly IReadOnlyList<Mat> _dealerTemplates;

    public OpenCvTableVisionDetector()
        : this(new SixMaxTableVisionLayout(), CreateOptionsFromEnvironment())
    {
    }

    public OpenCvTableVisionDetector(SixMaxTableVisionLayout layout, OpenCvTableVisionDetectorOptions options)
    {
        _layout = layout;
        _options = options;
        _dealerTemplates = LoadDealerTemplates();
    }

    public TableDetectionResult Detect(CapturedImage image, IReadOnlyList<SnapshotPlayer> players)
    {
        if (image.ImageBytes.Length == 0)
        {
            return new TableDetectionResult();
        }

        using var frame = Cv2.ImDecode(image.ImageBytes, ImreadModes.Color);
        if (frame.Empty())
        {
            return new TableDetectionResult();
        }

        var seatRois = _layout.GetSeatRois(frame.Width, frame.Height);
        var diagnostics = new Dictionary<int, SeatDetectionDiagnostics>();
        var dealerScores = new Dictionary<int, double>();
        var occupiedSeats = new List<int>();

        var debugFrame = _options.EnableDebugArtifacts ? frame.Clone() : null;

        foreach (var seat in seatRois)
        {
            var dealerScore = ScoreDealerSeat(frame, seat.DealerButtonSearchRoi);
            var occupancy = ScoreOccupancy(frame, seat.OccupancyRoi);
            var isOccupied = occupancy.IsOccupied || players.Any(player => player.Seat == seat.Seat && !string.IsNullOrWhiteSpace(player.Name));
            if (isOccupied)
            {
                occupiedSeats.Add(seat.Seat);
            }

            dealerScores[seat.Seat] = dealerScore;
            diagnostics[seat.Seat] = new SeatDetectionDiagnostics
            {
                DealerScore = dealerScore,
                OccupancyScore = occupancy.Score,
                IsOccupied = isOccupied,
                Notes = occupancy.Notes
            };

            if (debugFrame is not null)
            {
                DrawSeatDebug(debugFrame, seat, seat.Seat, dealerScore, occupancy.Score, isOccupied);
                SaveSeatCrop(frame, seat, image.CapturedAtUtc);
            }
        }

        var best = dealerScores.OrderByDescending(pair => pair.Value).First();
        var second = dealerScores.Where(pair => pair.Key != best.Key).DefaultIfEmpty(new KeyValuePair<int, double>(0, 0)).MaxBy(pair => pair.Value);
        var dealerSeat = best.Value >= _options.DealerTemplateThreshold && (best.Value - second.Value) >= _options.DealerMarginThreshold
            ? best.Key
            : (int?)null;

        if (debugFrame is not null)
        {
            if (dealerSeat.HasValue)
            {
                var roi = seatRois.First(seat => seat.Seat == dealerSeat.Value).DealerButtonSearchRoi;
                Cv2.Rectangle(debugFrame, ToRect(roi), Scalar.Red, 3);
            }

            SaveDebugFrame(debugFrame, image.CapturedAtUtc);
            debugFrame.Dispose();
        }

        return new TableDetectionResult
        {
            DealerSeat = dealerSeat,
            DealerConfidence = dealerSeat.HasValue ? best.Value : 0,
            OccupiedSeats = occupiedSeats,
            PerSeatDiagnostics = diagnostics
        };
    }

    private double ScoreDealerSeat(Mat frame, System.Drawing.Rectangle roi)
    {
        if (_dealerTemplates.Count == 0 || roi.Width <= 0 || roi.Height <= 0)
        {
            return 0;
        }

        using var seatMat = new Mat(frame, ToRect(roi));
        using var seatGray = new Mat();
        Cv2.CvtColor(seatMat, seatGray, ColorConversionCodes.BGR2GRAY);

        var best = 0.0;
        foreach (var template in _dealerTemplates)
        {
            if (seatGray.Width < template.Width || seatGray.Height < template.Height)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(seatGray, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
            best = Math.Max(best, maxVal);
        }

        return best;
    }

    private (bool IsOccupied, double Score, string Notes) ScoreOccupancy(Mat frame, System.Drawing.Rectangle roi)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return (false, 0, "Empty ROI.");
        }

        using var seatMat = new Mat(frame, ToRect(roi));
        using var gray = new Mat();
        Cv2.CvtColor(seatMat, gray, ColorConversionCodes.BGR2GRAY);

        Cv2.MeanStdDev(gray, out _, out var stddev);
        var variability = stddev[0, 0];

        using var edges = new Mat();
        Cv2.Canny(gray, edges, 55, 140);
        var edgePixels = Cv2.CountNonZero(edges);
        var edgeRatio = edgePixels / (double)(roi.Width * roi.Height);

        var isOccupied = variability >= _options.OccupancyStdDevThreshold || edgeRatio >= _options.OccupancyEdgeRatioThreshold;
        var score = Math.Min(1.0, (variability / (_options.OccupancyStdDevThreshold * 1.5) + edgeRatio / (_options.OccupancyEdgeRatioThreshold * 2.0)) / 2.0);

        return (isOccupied, score, $"std={variability:0.0},edge={edgeRatio:0.000}");
    }

    private static IReadOnlyList<Mat> LoadDealerTemplates()
    {
        var templates = new List<Mat>();
        var basePath = AppContext.BaseDirectory;
        var candidatePaths = new[]
        {
            Path.Combine(basePath, "Assets", "dealer_button_template.pgm"),
            Path.Combine(basePath, "dealer_button_template.pgm"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "ScreenshotScraper.Extraction", "Assets", "dealer_button_template.pgm")
        };

        foreach (var path in candidatePaths.Distinct())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var mat = Cv2.ImRead(path, ImreadModes.Grayscale);
            if (!mat.Empty())
            {
                templates.Add(mat);
            }
        }

        return templates;
    }

    private void DrawSeatDebug(Mat frame, SeatVisionRoi seat, int seatNo, double dealerScore, double occupancyScore, bool occupied)
    {
        var dealerColor = occupied ? Scalar.LimeGreen : Scalar.Orange;
        Cv2.Rectangle(frame, ToRect(seat.DealerButtonSearchRoi), dealerColor, 2);
        Cv2.Rectangle(frame, ToRect(seat.OccupancyRoi), Scalar.Cyan, 2);
        Cv2.PutText(frame, $"S{seatNo} D:{dealerScore:0.00} O:{occupancyScore:0.00}", new Point(seat.OccupancyRoi.X, Math.Max(15, seat.OccupancyRoi.Y - 4)), HersheyFonts.HersheySimplex, 0.4, Scalar.White, 1);
    }

    private void SaveSeatCrop(Mat frame, SeatVisionRoi seat, DateTime capturedAt)
    {
        var directory = EnsureDebugDirectory(capturedAt);
        using var dealerCrop = new Mat(frame, ToRect(seat.DealerButtonSearchRoi));
        Cv2.ImWrite(Path.Combine(directory, $"seat_{seat.Seat}_dealer.png"), dealerCrop);
        using var occupancyCrop = new Mat(frame, ToRect(seat.OccupancyRoi));
        Cv2.ImWrite(Path.Combine(directory, $"seat_{seat.Seat}_occupancy.png"), occupancyCrop);
    }

    private void SaveDebugFrame(Mat frame, DateTime capturedAt)
    {
        var directory = EnsureDebugDirectory(capturedAt);
        Cv2.ImWrite(Path.Combine(directory, "table_rois.png"), frame);
    }

    private string EnsureDebugDirectory(DateTime capturedAt)
    {
        var timestamp = (capturedAt == default ? DateTime.UtcNow : capturedAt).ToString("yyyyMMdd_HHmmssfff");
        var path = Path.Combine(_options.DebugOutputDirectory, timestamp);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Rect ToRect(System.Drawing.Rectangle rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static OpenCvTableVisionDetectorOptions CreateOptionsFromEnvironment()
    {
        return new OpenCvTableVisionDetectorOptions
        {
            EnableDebugArtifacts = string.Equals(Environment.GetEnvironmentVariable("SCRAPER_DEBUG_VISION"), "1", StringComparison.Ordinal)
        };
    }
}
