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
        var seatSnapshots = new List<SeatSnapshot>(seatRois.Count);
        var occupiedSeats = new List<int>();

        var debugFrame = _options.EnableDebugArtifacts ? frame.Clone() : null;

        foreach (var seat in seatRois)
        {
            var dealerScore = ScoreDealerSeat(frame, seat.DealerButtonSearchRoi);
            var occupancy = ScoreOccupancy(frame, seat.OccupancyRoi);
            var isOccupied = occupancy.IsOccupied;

            if (isOccupied)
            {
                occupiedSeats.Add(seat.Seat);
            }

            diagnostics[seat.Seat] = new SeatDetectionDiagnostics
            {
                DealerScore = dealerScore,
                OccupancyScore = occupancy.Score,
                IsOccupied = isOccupied,
                DealerThresholdPassed = dealerScore >= _options.DealerTemplateThreshold,
                Notes = occupancy.Notes
            };

            seatSnapshots.Add(new SeatSnapshot
            {
                SeatNumber = seat.Seat,
                IsOccupied = isOccupied,
                DealerScore = dealerScore,
                OccupancyScore = occupancy.Score,
                DealerThresholdPassed = dealerScore >= _options.DealerTemplateThreshold,
                Diagnostics = occupancy.Notes
            });

            if (debugFrame is not null)
            {
                DrawSeatDebug(debugFrame, seat, seat.Seat, dealerScore, occupancy.Score, isOccupied);
                SaveSeatCrop(frame, seat, image.CapturedAtUtc);
            }
        }

        var occupiedSeatSnapshots = seatSnapshots.Where(snapshot => snapshot.IsOccupied).OrderByDescending(snapshot => snapshot.DealerScore).ToList();
        var bestOccupied = occupiedSeatSnapshots.FirstOrDefault();
        var secondOccupiedScore = occupiedSeatSnapshots.Skip(1).Select(snapshot => snapshot.DealerScore).DefaultIfEmpty(0).Max();

        var dealerSeat = bestOccupied is not null
            && bestOccupied.DealerThresholdPassed
            && (bestOccupied.DealerScore - secondOccupiedScore) >= _options.DealerMarginThreshold
            ? bestOccupied.SeatNumber
            : (int?)null;

        if (debugFrame is not null)
        {
            DrawSummary(debugFrame, seatRois, seatSnapshots, dealerSeat);
            SaveDebugFrame(debugFrame, image.CapturedAtUtc);
            SaveSeatSummary(image.CapturedAtUtc, seatSnapshots, dealerSeat);
            debugFrame.Dispose();
        }

        return new TableDetectionResult
        {
            DealerSeat = dealerSeat,
            DealerDetected = dealerSeat.HasValue,
            DealerConfidence = dealerSeat.HasValue ? bestOccupied?.DealerScore ?? 0 : 0,
            OccupiedSeats = occupiedSeats.OrderBy(seat => seat).ToList(),
            SeatSnapshots = seatSnapshots.OrderBy(snapshot => snapshot.SeatNumber).ToList(),
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
        var variability = stddev[0];

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
        var candidateDirectories = new[]
        {
            Path.Combine(basePath, "Assets"),
            basePath,
            Path.Combine(Directory.GetCurrentDirectory(), "src", "ScreenshotScraper.Extraction", "Assets")
        };

        foreach (var directory in candidateDirectories.Distinct())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "dealer_button_template*.*", SearchOption.TopDirectoryOnly))
            {
                var mat = Cv2.ImRead(file, ImreadModes.Grayscale);
                if (!mat.Empty())
                {
                    templates.Add(mat);
                }
            }
        }

        return templates;
    }

    private void DrawSeatDebug(Mat frame, SeatVisionRoi seat, int seatNo, double dealerScore, double occupancyScore, bool occupied)
    {
        Cv2.Rectangle(frame, ToRect(seat.DealerButtonSearchRoi), Scalar.Orange, 2);
        Cv2.Rectangle(frame, ToRect(seat.OccupancyRoi), Scalar.Cyan, 2);
        Cv2.PutText(frame, $"S{seatNo} D:{dealerScore:0.00} O:{occupancyScore:0.00} {(occupied ? "occ" : "empty")}", new Point(seat.OccupancyRoi.X, Math.Max(15, seat.OccupancyRoi.Y - 4)), HersheyFonts.HersheySimplex, 0.4, Scalar.White, 1);
    }

    private void DrawSummary(Mat frame, IReadOnlyList<SeatVisionRoi> seatRois, IReadOnlyList<SeatSnapshot> seatSnapshots, int? dealerSeat)
    {
        foreach (var seat in seatRois)
        {
            var occupancyCenter = new Point(
                seat.OccupancyRoi.Left + seat.OccupancyRoi.Width / 2,
                seat.OccupancyRoi.Top + seat.OccupancyRoi.Height / 2);
            Cv2.PutText(frame, $"Seat {seat.Seat}", occupancyCenter, HersheyFonts.HersheySimplex, 0.45, Scalar.Yellow, 1);
        }

        if (!dealerSeat.HasValue)
        {
            return;
        }

        var winner = seatRois.First(seat => seat.Seat == dealerSeat.Value).DealerButtonSearchRoi;
        Cv2.Rectangle(frame, ToRect(winner), Scalar.Red, 3);

        var winningSnapshot = seatSnapshots.First(snapshot => snapshot.SeatNumber == dealerSeat.Value);
        Cv2.PutText(
            frame,
            $"Dealer seat {dealerSeat.Value} ({winningSnapshot.DealerScore:0.000})",
            new Point(20, 24),
            HersheyFonts.HersheySimplex,
            0.6,
            Scalar.Red,
            2);
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

    private void SaveSeatSummary(DateTime capturedAt, IReadOnlyList<SeatSnapshot> seatSnapshots, int? dealerSeat)
    {
        var directory = EnsureDebugDirectory(capturedAt);
        var lines = new List<string>
        {
            $"DealerSeat={(dealerSeat.HasValue ? dealerSeat.Value : "none")}",
            $"DealerTemplateThreshold={_options.DealerTemplateThreshold:0.000}",
            $"DealerMarginThreshold={_options.DealerMarginThreshold:0.000}",
            $"OccupancyStdDevThreshold={_options.OccupancyStdDevThreshold:0.000}",
            $"OccupancyEdgeRatioThreshold={_options.OccupancyEdgeRatioThreshold:0.000}"
        };

        lines.AddRange(seatSnapshots
            .OrderBy(snapshot => snapshot.SeatNumber)
            .Select(snapshot =>
                $"Seat {snapshot.SeatNumber}: occupied={snapshot.IsOccupied}, occScore={snapshot.OccupancyScore:0.000}, dealerScore={snapshot.DealerScore:0.000}, dealerPass={snapshot.DealerThresholdPassed}, notes={snapshot.Diagnostics}"));

        File.WriteAllLines(Path.Combine(directory, "seat_summary.txt"), lines);
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
