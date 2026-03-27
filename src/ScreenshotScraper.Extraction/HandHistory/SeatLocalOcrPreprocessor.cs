using OpenCvSharp;

namespace ScreenshotScraper.Extraction.HandHistory;

public static class SeatLocalOcrPreprocessor
{
    public static SeatLocalOcrPreprocessingSettings DefaultSettings { get; } = new();

    public static IReadOnlyList<SeatLocalOcrVariantImage> BuildVariantsForName(byte[] sourcePngBytes, SeatLocalOcrPreprocessingSettings? settings = null)
        => BuildVariants(sourcePngBytes, settings ?? DefaultSettings, numeric: false);

    public static IReadOnlyList<SeatLocalOcrVariantImage> BuildVariantsForNumeric(byte[] sourcePngBytes, SeatLocalOcrPreprocessingSettings? settings = null)
        => BuildVariants(sourcePngBytes, settings ?? DefaultSettings, numeric: true);

    private static IReadOnlyList<SeatLocalOcrVariantImage> BuildVariants(byte[] sourcePngBytes, SeatLocalOcrPreprocessingSettings settings, bool numeric)
    {
        using var source = Cv2.ImDecode(sourcePngBytes, ImreadModes.Color);
        if (source.Empty())
        {
            return [];
        }

        var variants = new List<SeatLocalOcrVariantImage>();
        AddVariant(variants, "raw", source);

        var scales = numeric ? settings.NumericUpscaleFactors : settings.NameUpscaleFactors;
        foreach (var scale in scales)
        {
            if (scale <= 0)
            {
                continue;
            }

            using var upscaled = new Mat();
            Cv2.Resize(source, upscaled, new Size(), scale, scale, InterpolationFlags.Cubic);
            AddVariant(variants, $"raw_x{scale:0.#}", upscaled);

            using var enhanced = ApplySourcePreservingEnhancement(upscaled, settings);
            AddVariant(variants, $"source_enhanced_x{scale:0.#}", enhanced);

            using var grayNormalized = BuildGrayNormalized(upscaled);
            AddVariant(variants, $"gray_normalized_x{scale:0.#}", grayNormalized);

            if (settings.EnableThresholdFallback)
            {
                using var thresholded = BuildThresholdFallback(grayNormalized);
                AddVariant(variants, $"threshold_fallback_x{scale:0.#}", thresholded);
            }
        }

        return variants
            .GroupBy(variant => variant.VariantName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static Mat ApplySourcePreservingEnhancement(Mat source, SeatLocalOcrPreprocessingSettings settings)
    {
        var output = source.Clone();

        using var normalized = new Mat();
        Cv2.ConvertScaleAbs(output, normalized, alpha: settings.ContrastAlpha, beta: settings.ContrastBeta);

        using var sharpened = new Mat();
        using var kernel = new Mat(3, 3, MatType.CV_32F, new float[]
        {
            0f, -settings.SharpenStrength, 0f,
            -settings.SharpenStrength, 1f + (4f * settings.SharpenStrength), -settings.SharpenStrength,
            0f, -settings.SharpenStrength, 0f
        });
        Cv2.Filter2D(normalized, sharpened, -1, kernel);

        output.Dispose();
        return sharpened.Clone();
    }

    private static Mat BuildGrayNormalized(Mat source)
    {
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);

        var normalized = new Mat();
        Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);
        return normalized;
    }

    private static Mat BuildThresholdFallback(Mat grayNormalized)
    {
        var threshold = new Mat();
        Cv2.Threshold(grayNormalized, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        return threshold;
    }

    private static void AddVariant(List<SeatLocalOcrVariantImage> variants, string variantName, Mat image)
    {
        Cv2.ImEncode(".png", image, out var encoded);
        variants.Add(new SeatLocalOcrVariantImage(variantName, encoded, image.Width, image.Height));
    }
}

public sealed record SeatLocalOcrVariantImage(string VariantName, byte[] ImageBytes, int Width, int Height);

public sealed class SeatLocalOcrPreprocessingSettings
{
    public double[] NameUpscaleFactors { get; init; } = [2.0, 3.0, 4.0];

    public double[] NumericUpscaleFactors { get; init; } = [2.0, 3.0, 4.0];

    public double ContrastAlpha { get; init; } = 1.12;

    public int ContrastBeta { get; init; } = 3;

    public float SharpenStrength { get; init; } = 0.20f;

    public bool EnableThresholdFallback { get; init; } = true;
}
