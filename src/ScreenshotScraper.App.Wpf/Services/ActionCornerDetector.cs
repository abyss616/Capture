using System.Text.RegularExpressions;
using OpenCvSharp;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.App.Wpf.Services;

public sealed class ActionCornerDetector
{
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IOcrEngine _ocrEngine;

    public ActionCornerDetector(IOcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
    }

    public async Task<bool> HasCheckOrFoldAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        var roiImage = CropRightBottomActionArea(image);
        if (roiImage.ImageBytes.Length == 0)
        {
            return false;
        }

        var ocr = await _ocrEngine.ReadAsync(
            roiImage,
            new OcrRequest("action_buttons", "raw"),
            cancellationToken).ConfigureAwait(false);

        var normalized = Normalize(ocr.Text);
        return normalized.Contains("CALL", StringComparison.Ordinal);
           
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = SpaceRegex.Replace(value, string.Empty);
        return compact.ToUpperInvariant();
    }

    private static CapturedImage CropRightBottomActionArea(CapturedImage image)
    {
        if (image.ImageBytes.Length == 0)
        {
            return new CapturedImage();
        }

        using var source = Cv2.ImDecode(image.ImageBytes, ImreadModes.Color);
        if (source.Empty())
        {
            return new CapturedImage();
        }

        var crop = new Rect(
            (int)Math.Round(source.Width * 0.60),
            (int)Math.Round(source.Height * 0.84),
             Math.Max(1, (int)Math.Round(source.Width * 0.39)),
            Math.Max(1, (int)Math.Round(source.Height * 0.15)));

        crop = crop.Intersect(new Rect(0, 0, source.Width, source.Height));
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return new CapturedImage();
        }

        using var cropped = new Mat(source, crop);
        Cv2.ImEncode(".png", cropped, out var encoded);

        return new CapturedImage
        {
            ImageBytes = encoded,
            Width = crop.Width,
            Height = crop.Height,
            CapturedAtUtc = image.CapturedAtUtc,
            SourceDescription = $"{image.SourceDescription}|ActionButtons",
            WindowTitle = image.WindowTitle
        };
    }
}
