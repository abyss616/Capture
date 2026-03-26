using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace ScreenshotScraper.Extraction.HandHistory;

internal sealed class DealerButtonSeatDetector
{
    private static readonly double[] TemplateScaleFactors = [0.9, 1.0, 1.1];

    private readonly OpenCvTableVisionDetectorOptions _options;
    private readonly IReadOnlyList<Mat> _dealerTemplates;

    public DealerButtonSeatDetector(OpenCvTableVisionDetectorOptions options, IReadOnlyList<Mat> dealerTemplates)
    {
        _options = options;
        _dealerTemplates = dealerTemplates;
    }

    public DealerSeatScore ScoreSeat(Mat frame, SeatVisionRoi seatRoi, bool includeDebugArtifacts)
    {
        if (seatRoi.DealerButtonSearchRoi.Width <= 0 || seatRoi.DealerButtonSearchRoi.Height <= 0)
        {
            return DealerSeatScore.Empty(seatRoi.Seat, "Dealer ROI is empty.");
        }

        var roiRect = new Rect(
            seatRoi.DealerButtonSearchRoi.X,
            seatRoi.DealerButtonSearchRoi.Y,
            seatRoi.DealerButtonSearchRoi.Width,
            seatRoi.DealerButtonSearchRoi.Height);

        using var roi = new Mat(frame, roiRect);
        using var hsv = new Mat();
        Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);

        using var yellowMask = new Mat();
        Cv2.InRange(
            hsv,
            new Scalar(_options.DealerYellowHueMin, _options.DealerYellowSaturationMin, _options.DealerYellowValueMin),
            new Scalar(_options.DealerYellowHueMax, 255, 255),
            yellowMask);

        using var morphKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(yellowMask, yellowMask, MorphTypes.Open, morphKernel);
        Cv2.MorphologyEx(yellowMask, yellowMask, MorphTypes.Close, morphKernel);

        var yellowRatio = Cv2.CountNonZero(yellowMask) / (double)(yellowMask.Width * yellowMask.Height);
        var yellowRatioScore = Clamp01(yellowRatio / 0.12);

        Cv2.FindContours(yellowMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var roiCenter = new Point2d(roi.Width / 2.0, roi.Height / 2.0);
        var maxDistance = Math.Max(1.0, Math.Sqrt((roi.Width * roi.Width) + (roi.Height * roi.Height)) / 2.0);

        var bestContourScore = 0.0;
        var bestCircularity = 0.0;
        var bestDistanceScore = 0.0;
        var bestAreaRatio = 0.0;
        var bestContourIndex = -1;

        for (var i = 0; i < contours.Length; i++)
        {
            var area = Cv2.ContourArea(contours[i]);
            if (area <= 0)
            {
                continue;
            }

            var areaRatio = area / (roi.Width * roi.Height);
            if (areaRatio < _options.DealerMinContourAreaRatio || areaRatio > _options.DealerMaxContourAreaRatio)
            {
                continue;
            }

            var perimeter = Cv2.ArcLength(contours[i], true);
            if (perimeter <= 0)
            {
                continue;
            }

            var circularity = Math.Min(1.0, (4.0 * Math.PI * area) / (perimeter * perimeter));
            var moments = Cv2.Moments(contours[i]);
            if (Math.Abs(moments.M00) < double.Epsilon)
            {
                continue;
            }

            var center = new Point2d(moments.M10 / moments.M00, moments.M01 / moments.M00);
            var distance = Math.Sqrt(Math.Pow(center.X - roiCenter.X, 2) + Math.Pow(center.Y - roiCenter.Y, 2));
            var distanceScore = Clamp01(1.0 - (distance / maxDistance));

            var areaScore = Clamp01((areaRatio - _options.DealerMinContourAreaRatio) / (_options.DealerMaxContourAreaRatio - _options.DealerMinContourAreaRatio));
            var contourQuality = (0.55 * circularity) + (0.35 * distanceScore) + (0.10 * areaScore);

            if (contourQuality > bestContourScore)
            {
                bestContourScore = contourQuality;
                bestCircularity = circularity;
                bestDistanceScore = distanceScore;
                bestAreaRatio = areaRatio;
                bestContourIndex = i;
            }
        }

        var templateScore = ScoreTemplate(roi);

        var compositeScore = Clamp01(
            (_options.DealerYellowWeight * yellowRatioScore)
            + (_options.DealerCircularityWeight * bestContourScore)
            + (_options.DealerTemplateWeight * templateScore));

        DealerSeatDebugArtifacts? artifacts = null;
        if (includeDebugArtifacts)
        {
            artifacts = BuildDebugArtifacts(roi, yellowMask, contours, bestContourIndex, compositeScore, yellowRatioScore, bestContourScore, templateScore);
        }

        var colorShapeEvidence = Clamp01((0.6 * yellowRatioScore) + (0.4 * bestContourScore));
        var notes = $"yellow={yellowRatio:0.000}/{yellowRatioScore:0.000}, contour={bestContourScore:0.000}, circularity={bestCircularity:0.000}, distance={bestDistanceScore:0.000}, area={bestAreaRatio:0.000}, template={templateScore:0.000}, composite={compositeScore:0.000}";

        return new DealerSeatScore(
            seatRoi.Seat,
            compositeScore,
            yellowRatio,
            yellowRatioScore,
            bestContourScore,
            bestCircularity,
            bestDistanceScore,
            templateScore,
            colorShapeEvidence,
            notes,
            artifacts);
    }

    private double ScoreTemplate(Mat roi)
    {
        if (_dealerTemplates.Count == 0)
        {
            return 0;
        }

        using var roiGray = new Mat();
        Cv2.CvtColor(roi, roiGray, ColorConversionCodes.BGR2GRAY);

        var best = 0.0;

        foreach (var template in _dealerTemplates)
        {
            foreach (var scale in TemplateScaleFactors)
            {
                using var scaledTemplate = ResizeTemplate(template, scale);
                if (scaledTemplate.Empty() || roiGray.Width < scaledTemplate.Width || roiGray.Height < scaledTemplate.Height)
                {
                    continue;
                }

                using var result = new Mat();
                Cv2.MatchTemplate(roiGray, scaledTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
                best = Math.Max(best, maxVal);
            }
        }

        return Clamp01(best);
    }

    private static Mat ResizeTemplate(Mat template, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.001)
        {
            return template.Clone();
        }

        var width = Math.Max(1, (int)Math.Round(template.Width * scale));
        var height = Math.Max(1, (int)Math.Round(template.Height * scale));
        var resized = new Mat();
        Cv2.Resize(template, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);
        return resized;
    }

    private static DealerSeatDebugArtifacts BuildDebugArtifacts(
        Mat roi,
        Mat yellowMask,
        Point[][] contours,
        int bestContourIndex,
        double compositeScore,
        double yellowScore,
        double contourScore,
        double templateScore)
    {
        var raw = roi.Clone();
        var mask = yellowMask.Clone();

        var contourView = roi.Clone();
        Cv2.DrawContours(contourView, contours, -1, Scalar.Cyan, 2);
        if (bestContourIndex >= 0)
        {
            Cv2.DrawContours(contourView, contours, bestContourIndex, Scalar.Lime, 3);
        }

        var overlay = roi.Clone();
        Cv2.PutText(overlay, $"score={compositeScore:0.000}", new Point(5, 16), HersheyFonts.HersheySimplex, 0.45, Scalar.White, 1);
        Cv2.PutText(overlay, $"y={yellowScore:0.000} c={contourScore:0.000} t={templateScore:0.000}", new Point(5, 32), HersheyFonts.HersheySimplex, 0.40, Scalar.White, 1);

        return new DealerSeatDebugArtifacts(raw, mask, contourView, overlay);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

internal sealed record DealerSeatScore(
    int Seat,
    double CompositeScore,
    double YellowPixelRatio,
    double YellowRatioScore,
    double ContourScore,
    double Circularity,
    double DistanceScore,
    double TemplateScore,
    double ColorShapeEvidence,
    string Notes,
    DealerSeatDebugArtifacts? DebugArtifacts)
{
    public static DealerSeatScore Empty(int seat, string notes) =>
        new(seat, 0, 0, 0, 0, 0, 0, 0, 0, notes, null);
}

internal sealed class DealerSeatDebugArtifacts : IDisposable
{
    public DealerSeatDebugArtifacts(Mat rawRoi, Mat yellowMask, Mat contourView, Mat overlay)
    {
        RawRoi = rawRoi;
        YellowMask = yellowMask;
        ContourView = contourView;
        Overlay = overlay;
    }

    public Mat RawRoi { get; }

    public Mat YellowMask { get; }

    public Mat ContourView { get; }

    public Mat Overlay { get; }

    public void Dispose()
    {
        RawRoi.Dispose();
        YellowMask.Dispose();
        ContourView.Dispose();
        Overlay.Dispose();
    }
}
