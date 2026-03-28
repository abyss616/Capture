using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Extraction.HandHistory;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class OpenCvTableVisionDetectorTests
{
    [Fact]
    public void SixMaxLayout_MovesSeat1NameAndStackRoisDownToTimeBankArea()
    {
        var layout = new SixMaxTableVisionLayout();

        var seat1 = Assert.Single(layout.GetSeatRois(SixMaxTableVisionLayout.ReferenceWidth, SixMaxTableVisionLayout.ReferenceHeight).Where(seat => seat.Seat == 1));

        Assert.Equal(new Rectangle(432, 547, 165, 30), seat1.NameRoi);
        Assert.Equal(new Rectangle(432, 579, 165, 27), seat1.StackRoi);
    }

    [Fact]
    public void Detect_ReturnsCorrectDealerSeat_OnKnownLayout()
    {
        var detector = new OpenCvTableVisionDetector();
        using var bitmap = BuildTableImage([1, 2, 3, 4], dealerSeat: 2);
        var image = ToCapturedImage(bitmap);

        var result = detector.Detect(image, BuildPlayers());

        Assert.Equal(2, result.DealerSeat);
        Assert.True(result.DealerConfidence > 0.42);
    }

    [Fact]
    public void Detect_ReturnsOccupiedSeats_OnKnownLayout()
    {
        var detector = new OpenCvTableVisionDetector();
        using var bitmap = BuildTableImage([1, 3, 5], dealerSeat: 5);

        var result = detector.Detect(ToCapturedImage(bitmap), BuildPlayers());

        Assert.Equal([1, 3, 5], result.OccupiedSeats.OrderBy(seat => seat).ToList());
    }


    [Fact]
    public void Detect_DoesNotAssignDealerToEmptySeat()
    {
        var detector = new OpenCvTableVisionDetector();
        using var bitmap = BuildTableImage([1, 2, 3, 4, 5], dealerSeat: 6);

        var result = detector.Detect(ToCapturedImage(bitmap), BuildPlayers());

        Assert.Null(result.DealerSeat);
        Assert.False(result.DealerDetected);
    }

    [Fact]
    public void Detect_ExposesSeatSnapshotsForAllSeats()
    {
        var detector = new OpenCvTableVisionDetector();
        using var bitmap = BuildTableImage([1, 3, 4, 6], dealerSeat: 3);

        var result = detector.Detect(ToCapturedImage(bitmap), BuildPlayers());

        Assert.Equal(6, result.SeatSnapshots.Count);
        Assert.Equal([1, 3, 4, 6], result.SeatSnapshots.Where(snapshot => snapshot.IsOccupied).Select(snapshot => snapshot.SeatNumber).OrderBy(seat => seat).ToList());
        Assert.All(result.SeatSnapshots, snapshot => Assert.Equal(snapshot.DealerScore >= 0.42, snapshot.DealerThresholdPassed));
    }
    [Fact]
    public void Detect_DoesNotGuessDealer_WhenCompositeRankingIsTooWeak()
    {
        var detector = new OpenCvTableVisionDetector(
            new SixMaxTableVisionLayout(),
            new OpenCvTableVisionDetectorOptions
            {
                DealerSeatMinimumScore = 0.99,
                DealerMarginThreshold = 0.1
            });

        using var bitmap = BuildTableImage([1, 2, 4], dealerSeat: 2);
        var result = detector.Detect(ToCapturedImage(bitmap), BuildPlayers());

        Assert.Null(result.DealerSeat);
    }

    private static Bitmap BuildTableImage(IReadOnlyList<int> occupiedSeats, int dealerSeat)
    {
        var layout = new SixMaxTableVisionLayout();
        var bitmap = new Bitmap(SixMaxTableVisionLayout.ReferenceWidth, SixMaxTableVisionLayout.ReferenceHeight, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(24, 66, 44));

        foreach (var seatRoi in layout.GetSeatRois(bitmap.Width, bitmap.Height))
        {
            using var brush = new SolidBrush(Color.FromArgb(15, 24, 36));
            graphics.FillRectangle(brush, seatRoi.OccupancyRoi);

            if (occupiedSeats.Contains(seatRoi.Seat))
            {
                using var panelBrush = new LinearGradientBrush(seatRoi.OccupancyRoi, Color.FromArgb(18, 34, 54), Color.FromArgb(46, 78, 122), LinearGradientMode.Horizontal);
                graphics.FillRectangle(panelBrush, seatRoi.OccupancyRoi);
                using var textPen = new Pen(Color.FromArgb(220, 220, 230), 2);
                graphics.DrawLine(textPen, seatRoi.OccupancyRoi.Left + 8, seatRoi.OccupancyRoi.Top + 8, seatRoi.OccupancyRoi.Right - 8, seatRoi.OccupancyRoi.Bottom - 8);
                graphics.DrawLine(textPen, seatRoi.OccupancyRoi.Right - 8, seatRoi.OccupancyRoi.Top + 8, seatRoi.OccupancyRoi.Left + 8, seatRoi.OccupancyRoi.Bottom - 8);
            }

            if (seatRoi.Seat == dealerSeat)
            {
                DrawDealerButton(graphics, seatRoi.DealerButtonSearchRoi);
            }
        }

        return bitmap;
    }

    private static void DrawDealerButton(Graphics graphics, Rectangle roi)
    {
        var radius = Math.Min(roi.Width, roi.Height) / 4;
        var center = new Point(roi.Left + roi.Width / 2, roi.Top + roi.Height / 2);
        var button = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using var fill = new SolidBrush(Color.FromArgb(251, 224, 44));
        using var border = new Pen(Color.FromArgb(45, 45, 45), 2);
        graphics.FillEllipse(fill, button);
        graphics.DrawEllipse(border, button);
        using var font = new Font("Arial", Math.Max(10, radius * 0.9f), FontStyle.Bold);
        graphics.DrawString("D", font, Brushes.Black, button.Left + radius * 0.35f, button.Top + radius * 0.08f);
    }

    private static CapturedImage ToCapturedImage(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new CapturedImage
        {
            ImageBytes = stream.ToArray(),
            Width = bitmap.Width,
            Height = bitmap.Height,
            CapturedAtUtc = DateTime.UtcNow
        };
    }

    private static IReadOnlyList<SnapshotPlayer> BuildPlayers()
    {
        return
        [
            new SnapshotPlayer { Seat = 1, Name = "Hero" },
            new SnapshotPlayer { Seat = 2, Name = "P2" },
            new SnapshotPlayer { Seat = 3, Name = "P3" },
            new SnapshotPlayer { Seat = 4, Name = "P4" },
            new SnapshotPlayer { Seat = 5, Name = "P5" },
            new SnapshotPlayer { Seat = 6, Name = "P6" }
        ];
    }
}
