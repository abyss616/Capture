using System;
using OpenCvSharp;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class SeatLocalOcrUtilitiesTests
{
    [Theory]
    [InlineData("238.50 BB", "238.50")]
    [InlineData("O.50 BB", "0.50")]
    [InlineData("1 BB", "1")]
    public void ParseNumber_ExtractsNormalizedNumericToken(string raw, string expected)
    {
        Assert.Equal(expected, SeatLocalTextParser.ParseNumber(raw));
    }

    [Theory]
    [InlineData("jkl102", "jkl102")]
    [InlineData("TIME BANK 11", string.Empty)]
    [InlineData("Wulverate 223.50", "Wulverate")]
    public void ParseName_ExtractsSeatLocalNameToken(string raw, string expected)
    {
        Assert.Equal(expected, SeatLocalTextParser.ParseName(raw));
    }

    [Fact]
    public void BuildVariantsForNumeric_ReturnsSourceLikeAndFallbackVariants()
    {
        var bytes = BuildPngWithText("238.50 BB");
        var variants = SeatLocalOcrPreprocessor.BuildVariantsForNumeric(bytes);

        Assert.Contains(variants, variant => variant.VariantName == "raw");
        Assert.Contains(variants, variant => variant.VariantName.StartsWith("source_enhanced_x", StringComparison.Ordinal));
        Assert.Contains(variants, variant => variant.VariantName.StartsWith("threshold_fallback_x", StringComparison.Ordinal));
        Assert.All(variants, variant => Assert.NotEmpty(variant.ImageBytes));
    }

    private static byte[] BuildPngWithText(string text)
    {
        using var mat = new Mat(new Size(220, 50), MatType.CV_8UC3, Scalar.Black);
        Cv2.PutText(mat, text, new Point(8, 32), HersheyFonts.HersheySimplex, 0.8, Scalar.White, 2);
        Cv2.ImEncode(".png", mat, out var encoded);
        return encoded;
    }
}
