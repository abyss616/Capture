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
    private readonly DealerButtonSeatDetector _dealerSeatDetector;

    public OpenCvTableVisionDetector()
        : this(new SixMaxTableVisionLayout(), CreateOptionsFromEnvironment())
    {
    }

    public OpenCvTableVisionDetector(SixMaxTableVisionLayout layout, OpenCvTableVisionDetectorOptions options)
    {
        _layout = layout;
        _options = options;
        _dealerTemplates = LoadDealerTemplates();
        _dealerSeatDetector = new DealerButtonSeatDetector(_options, _dealerTemplates);
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
        var dealerScores = new Dictionary<int, DealerSeatScore>(seatRois.Count);

        var debugFrame = _options.EnableDebugArtifacts ? frame.Clone() : null;

        foreach (var seat in seatRois)
        {
            var occupancy = ScoreOccupancy(frame, seat.OccupancyRoi);
            var isOccupied = occupancy.IsOccupied;

            if (isOccupied)
            {
                occupiedSeats.Add(seat.Seat);
            }

            var dealer = _dealerSeatDetector.ScoreSeat(frame, seat, _options.EnableDebugArtifacts);
            dealerScores[seat.Seat] = dealer;

            var passedMinimum = dealer.CompositeScore >= _options.DealerSeatMinimumScore;
            var notes = string.IsNullOrWhiteSpace(occupancy.Notes)
                ? dealer.Notes
                : $"{dealer.Notes}; occ({occupancy.Notes})";

            diagnostics[seat.Seat] = new SeatDetectionDiagnostics
            {
                DealerScore = dealer.CompositeScore,
                OccupancyScore = occupancy.Score,
                IsOccupied = isOccupied,
                DealerThresholdPassed = passedMinimum,
                Notes = notes
            };

            seatSnapshots.Add(new SeatSnapshot
            {
                SeatNumber = seat.Seat,
                IsOccupied = isOccupied,
                DealerScore = dealer.CompositeScore,
                OccupancyScore = occupancy.Score,
                DealerThresholdPassed = passedMinimum,
                Diagnostics = notes
            });

            if (debugFrame is not null)
            {
                DrawSeatDebug(debugFrame, seat, seat.Seat, dealer, occupancy.Score, isOccupied);
                SaveSeatCrop(frame, seat, image.CapturedAtUtc);
                SaveDealerSeatDebugArtifacts(dealer, image.CapturedAtUtc);
            }
        }

        var dealerDecision = ChooseDealerSeat(occupiedSeats, dealerScores);
        var dealerSeat = dealerDecision.DealerSeat;

        if (debugFrame is not null)
        {
            DrawSummary(debugFrame, seatRois, seatSnapshots, dealerSeat);
            SaveDebugFrame(debugFrame, image.CapturedAtUtc);
            SaveSeatSummary(image.CapturedAtUtc, seatSnapshots, dealerSeat, dealerDecision.DecisionNotes);
            debugFrame.Dispose();
        }

        return new TableDetectionResult
        {
            DealerSeat = dealerSeat,
            DealerDetected = dealerSeat.HasValue,
            DealerConfidence = dealerSeat.HasValue ? dealerScores[dealerSeat.Value].CompositeScore : 0,
            OccupiedSeats = occupiedSeats.OrderBy(seat => seat).ToList(),
            SeatSnapshots = seatSnapshots.OrderBy(snapshot => snapshot.SeatNumber).ToList(),
            PerSeatDiagnostics = diagnostics
        };
    }

    private DealerSeatDecision ChooseDealerSeat(IReadOnlyList<int> occupiedSeats, IReadOnlyDictionary<int, DealerSeatScore> scores)
    {
        var occupiedRanked = occupiedSeats
            .Select(seat => scores[seat])
            .OrderByDescending(score => score.CompositeScore)
            .ToList();

        if (occupiedRanked.Count == 0)
        {
            return new DealerSeatDecision(null, "No occupied seats available for dealer selection.");
        }

        var best = occupiedRanked[0];
        var second = occupiedRanked.Skip(1).FirstOrDefault();
        var secondScore = second?.CompositeScore ?? 0;
        var margin = best.CompositeScore - secondScore;

        if (best.CompositeScore >= _options.DealerSeatMinimumScore && margin >= _options.DealerMarginThreshold)
        {
            return new DealerSeatDecision(best.Seat, $"Accepted best occupied seat {best.Seat}: score={best.CompositeScore:0.000}, margin={margin:0.000}.");
        }

        var strongOccupied = occupiedRanked
            .Where(candidate => candidate.ColorShapeEvidence >= _options.StrongYellowCircularEvidenceThreshold)
            .ToList();

        if (strongOccupied.Count == 1)
        {
            return new DealerSeatDecision(strongOccupied[0].Seat, $"Accepted seat {strongOccupied[0].Seat} from unique strong yellow/circle evidence={strongOccupied[0].ColorShapeEvidence:0.000}.");
        }

        var bestUnoccupied = scores.Values
            .Where(score => !occupiedSeats.Contains(score.Seat))
            .OrderByDescending(score => score.CompositeScore)
            .FirstOrDefault();

        if (bestUnoccupied is not null
            && bestUnoccupied.CompositeScore >= _options.OverwhelmingUnoccupiedDealerScore
            && bestUnoccupied.CompositeScore - best.CompositeScore >= (_options.DealerMarginThreshold * 2.0))
        {
            return new DealerSeatDecision(bestUnoccupied.Seat, $"Accepted unoccupied seat {bestUnoccupied.Seat} due to overwhelming evidence {bestUnoccupied.CompositeScore:0.000}.");
        }

        return new DealerSeatDecision(null, $"No confident dealer: best occupied={best.Seat}:{best.CompositeScore:0.000}, second={second?.Seat}:{secondScore:0.000}, margin={margin:0.000}.");
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

    private void DrawSeatDebug(Mat frame, SeatVisionRoi seat, int seatNo, DealerSeatScore dealer, double occupancyScore, bool occupied)
    {
        Cv2.Rectangle(frame, ToRect(seat.DealerButtonSearchRoi), Scalar.Orange, 2);
        Cv2.Rectangle(frame, ToRect(seat.OccupancyRoi), Scalar.Cyan, 2);
        Cv2.PutText(frame, $"S{seatNo} D:{dealer.CompositeScore:0.00} Y:{dealer.YellowRatioScore:0.00} C:{dealer.ContourScore:0.00} T:{dealer.TemplateScore:0.00} O:{occupancyScore:0.00} {(occupied ? "occ" : "empty")}", new Point(seat.OccupancyRoi.X, Math.Max(15, seat.OccupancyRoi.Y - 4)), HersheyFonts.HersheySimplex, 0.4, Scalar.White, 1);
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

    private void SaveDealerSeatDebugArtifacts(DealerSeatScore dealerSeatScore, DateTime capturedAt)
    {
        if (dealerSeatScore.DebugArtifacts is null)
        {
            return;
        }

        var directory = EnsureDebugDirectory(capturedAt);
        using var artifacts = dealerSeatScore.DebugArtifacts;
        Cv2.ImWrite(Path.Combine(directory, $"seat_{dealerSeatScore.Seat}_dealer_raw.png"), artifacts.RawRoi);
        Cv2.ImWrite(Path.Combine(directory, $"seat_{dealerSeatScore.Seat}_dealer_yellow_mask.png"), artifacts.YellowMask);
        Cv2.ImWrite(Path.Combine(directory, $"seat_{dealerSeatScore.Seat}_dealer_contours.png"), artifacts.ContourView);
        Cv2.ImWrite(Path.Combine(directory, $"seat_{dealerSeatScore.Seat}_dealer_overlay.png"), artifacts.Overlay);
    }

    private void SaveDebugFrame(Mat frame, DateTime capturedAt)
    {
        var directory = EnsureDebugDirectory(capturedAt);
        Cv2.ImWrite(Path.Combine(directory, "table_rois.png"), frame);
    }

    private void SaveSeatSummary(DateTime capturedAt, IReadOnlyList<SeatSnapshot> seatSnapshots, int? dealerSeat, string decisionNotes)
    {
        var directory = EnsureDebugDirectory(capturedAt);
        var lines = new List<string>
        {
            $"DealerSeat={(dealerSeat.HasValue ? dealerSeat.Value : "none")}",
            $"DealerSeatMinimumScore={_options.DealerSeatMinimumScore:0.000}",
            $"DealerMarginThreshold={_options.DealerMarginThreshold:0.000}",
            $"StrongYellowCircularEvidenceThreshold={_options.StrongYellowCircularEvidenceThreshold:0.000}",
            $"OverwhelmingUnoccupiedDealerScore={_options.OverwhelmingUnoccupiedDealerScore:0.000}",
            $"Decision={decisionNotes}",
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

    private sealed record DealerSeatDecision(int? DealerSeat, string DecisionNotes);
}
