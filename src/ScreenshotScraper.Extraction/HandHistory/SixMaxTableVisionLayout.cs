using System.Drawing;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class SixMaxTableVisionLayout
{
    public const int ReferenceWidth = 1020;
    public const int ReferenceHeight = 717;

    private readonly IReadOnlyDictionary<int, SeatVisionRoi> _seatRois;

    public SixMaxTableVisionLayout()
    {
        // Calibrated for Betsafe 6-max table at 1020x717. Seat 1 is hero (bottom-center), seats increase clockwise.
        // Per-seat offsets are intentional because each seat panel has slightly different perspective and HUD overlap.
        _seatRois = new Dictionary<int, SeatVisionRoi>
        {
            [1] = new(1, new Rectangle(255, 474, 120, 94), new Rectangle(425, 505, 185, 104), NameRoi: new Rectangle(432, 565, 165, 30), StackRoi: new Rectangle(432, 590, 165, 27), new Rectangle(432, 583, 165, 24)),
            [2] = new(2, new Rectangle(165, 438, 120, 94), new Rectangle(124, 500, 192, 104), NameRoi: new Rectangle(134, 510, 170, 30), StackRoi: new Rectangle(130, 535, 170, 27), new Rectangle(134, 579, 170, 24)),
            [3] = new(3, new Rectangle(158, 162, 120, 94), new Rectangle(56, 188, 182, 104), NameRoi: new Rectangle(60, 212, 165, 30), StackRoi: new Rectangle(50, 233, 165, 27), new Rectangle(66, 268, 165, 24)),
            [4] = new(4, new Rectangle(428, 142, 120, 94), new Rectangle(414, 76, 192, 104), NameRoi: new Rectangle(426, 125, 170, 30), StackRoi: new Rectangle(426, 145, 170, 27), new Rectangle(426, 157, 170, 24)),
            [5] = new(5, new Rectangle(818, 162, 120, 94), new Rectangle(806, 188, 188, 104), NameRoi: new Rectangle(820, 210, 164, 30), StackRoi: new Rectangle(820, 232, 164, 27), new Rectangle(820, 268, 164, 24)),
            [6] = new(6, new Rectangle(812, 438, 120, 94), new Rectangle(730, 500, 196, 104), NameRoi: new Rectangle(748, 510, 164, 30), StackRoi: new Rectangle(735, 535, 164, 27), new Rectangle(748, 579, 164, 24))
        };
    }

    public IReadOnlyList<SeatVisionRoi> GetSeatRois(int width, int height)
    {
        var scaleX = width / (double)ReferenceWidth;
        var scaleY = height / (double)ReferenceHeight;

        return _seatRois.Values
            .OrderBy(seat => seat.Seat)
            .Select(seat => new SeatVisionRoi(
                seat.Seat,
                ScaleAndClamp(seat.DealerButtonSearchRoi, width, height, scaleX, scaleY),
                ScaleAndClamp(seat.OccupancyRoi, width, height, scaleX, scaleY),
                ScaleAndClamp(seat.NameRoi, width, height, scaleX, scaleY),
                ScaleAndClamp(seat.StackRoi, width, height, scaleX, scaleY),
                ScaleAndClamp(seat.BetRoi, width, height, scaleX, scaleY)))
            .ToList();
    }

    private static Rectangle ScaleAndClamp(Rectangle source, int maxWidth, int maxHeight, double scaleX, double scaleY)
    {
        var scaled = new Rectangle(
            x: (int)Math.Round(source.X * scaleX),
            y: (int)Math.Round(source.Y * scaleY),
            width: Math.Max(1, (int)Math.Round(source.Width * scaleX)),
            height: Math.Max(1, (int)Math.Round(source.Height * scaleY)));

        var boundary = new Rectangle(0, 0, maxWidth, maxHeight);
        scaled.Intersect(boundary);
        return scaled;
    }
}

public sealed record SeatVisionRoi(
    int Seat,
    Rectangle DealerButtonSearchRoi,
    Rectangle OccupancyRoi,
    Rectangle NameRoi,
    Rectangle StackRoi,
    Rectangle BetRoi);
