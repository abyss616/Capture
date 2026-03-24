using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class HeuristicDealerButtonExtractorTests
{
    [Fact]
    public void DetectDealerSeat_PrefersImageDealerButtonOverOcrText()
    {
        var extractor = new HeuristicDealerButtonExtractor();
        using var bitmap = BuildTableImageWithDealerButton(seat: 2, width: 1020, height: 717);
        var image = new CapturedImage
        {
            ImageBytes = ToPngBytes(bitmap),
            Width = bitmap.Width,
            Height = bitmap.Height
        };

        var field = extractor.DetectDealerSeat(
            image,
            rawText: "[Seat 4] DealerGuy 112 BB dealer",
            players: BuildPlayers());

        Assert.True(field.IsValid);
        Assert.Equal("2", field.ParsedValue);
        Assert.Contains("Per-seat ROI scores", field.Reason);
        Assert.Contains("Chosen seat=2", field.Reason);
    }

    [Fact]
    public void DetectDealerSeat_FallsBackToOcrWhenImageBytesAreMissing()
    {
        var extractor = new HeuristicDealerButtonExtractor();

        var field = extractor.DetectDealerSeat(
            new CapturedImage(),
            rawText: "[Seat 6] DealerGuy 111 BB dealer",
            players: BuildPlayers());

        Assert.True(field.IsValid);
        Assert.Equal("6", field.ParsedValue);
        Assert.Contains("Falling back to OCR/text heuristics", field.Reason);
    }

    private static IReadOnlyList<SnapshotPlayer> BuildPlayers()
    {
        return
        [
            new SnapshotPlayer { Seat = 1, Name = "HeroBottom", IsHero = true },
            new SnapshotPlayer { Seat = 2, Name = "P2" },
            new SnapshotPlayer { Seat = 3, Name = "P3" },
            new SnapshotPlayer { Seat = 4, Name = "P4" },
            new SnapshotPlayer { Seat = 5, Name = "P5" },
            new SnapshotPlayer { Seat = 6, Name = "P6" }
        ];
    }

    private static Bitmap BuildTableImageWithDealerButton(int seat, int width, int height)
    {
        var anchors = new Dictionary<int, PointF>
        {
            [1] = new PointF(0.60f, 0.75f),
            [2] = new PointF(0.33f, 0.72f),
            [3] = new PointF(0.22f, 0.38f),
            [4] = new PointF(0.56f, 0.24f),
            [5] = new PointF(0.86f, 0.38f),
            [6] = new PointF(0.87f, 0.72f)
        };

        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(18, 56, 31));

        var center = anchors[seat];
        var radius = (int)Math.Round(Math.Min(width, height) * 0.02);
        var centerX = (int)Math.Round(center.X * width);
        var centerY = (int)Math.Round(center.Y * height);
        var bounds = new Rectangle(centerX - radius, centerY - radius, radius * 2, radius * 2);
        using var yellowBrush = new SolidBrush(Color.FromArgb(252, 220, 34));
        graphics.FillEllipse(yellowBrush, bounds);
        using var blackPen = new Pen(Color.FromArgb(22, 22, 22), Math.Max(2, radius / 5f));
        using var font = new Font("Arial", Math.Max(8, radius * 0.8f), FontStyle.Bold);
        graphics.DrawString("D", font, Brushes.Black, bounds.Left + radius * 0.4f, bounds.Top + radius * 0.1f);
        graphics.DrawEllipse(blackPen, bounds);
        return bitmap;
    }

    private static byte[] ToPngBytes(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
