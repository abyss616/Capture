using OpenCvSharp;

namespace ScreenshotScraper.Extraction.HandHistory;

public static class SeatLocalOcrPreprocessor
{
    public const double NameScale = 3.0;
    public const double NumericScale = 4.0;

    public static byte[] PreprocessNameRoi(byte[] sourcePngBytes)
    {
        using var source = Cv2.ImDecode(sourcePngBytes, ImreadModes.Color);
        if (source.Empty())
        {
            return [];
        }

        using var upscaled = new Mat();
        Cv2.Resize(source, upscaled, new Size(), NameScale, NameScale, InterpolationFlags.Cubic);

        using var hsv = new Mat();
        Cv2.CvtColor(upscaled, hsv, ColorConversionCodes.BGR2HSV);
        using var value = new Mat();
        Cv2.ExtractChannel(hsv, value, 2);

        using var normalized = new Mat();
        Cv2.Normalize(value, normalized, 0, 255, NormTypes.MinMax);

        using var threshold = new Mat();
        Cv2.AdaptiveThreshold(normalized, threshold, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 31, 7);

        using var denoised = new Mat();
        Cv2.MedianBlur(threshold, denoised, 3);

        Cv2.ImEncode(".png", denoised, out var encoded);
        return encoded;
    }

    public static byte[] PreprocessNumericRoi(byte[] sourcePngBytes)
    {
        using var source = Cv2.ImDecode(sourcePngBytes, ImreadModes.Color);
        if (source.Empty())
        {
            return [];
        }

        using var upscaled = new Mat();
        Cv2.Resize(source, upscaled, new Size(), NumericScale, NumericScale, InterpolationFlags.Cubic);

        using var gray = new Mat();
        Cv2.CvtColor(upscaled, gray, ColorConversionCodes.BGR2GRAY);

        using var normalized = new Mat();
        Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);

        using var threshold = new Mat();
        Cv2.Threshold(normalized, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        using var opened = new Mat();
        Cv2.MorphologyEx(threshold, opened, MorphTypes.Open, kernel);

        Cv2.ImEncode(".png", opened, out var encoded);
        return encoded;
    }
}
