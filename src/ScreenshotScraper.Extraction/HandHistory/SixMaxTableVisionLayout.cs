using System.Drawing;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class SixMaxTableVisionLayout
{
    public const int ReferenceWidth = 1020;
    public const int ReferenceHeight = 717;

    private readonly IReadOnlyDictionary<int, SeatVisionRoi> _seatRois;

    public SixMaxTableVisionLayout()
    {
        // Seat ROI coordinates are calibrated for the 1020x717 capture reference. They assume a fixed six-max table
        // camera angle where seat number 1 is hero (bottom-center) and seats increase clockwise.
        _seatRois = new Dictionary<int, SeatVisionRoi>
        {
            [1] = new(1, new Rectangle(250, 470, 110, 90), new Rectangle(430, 510, 170, 95), new Rectangle(430, 510, 170, 36), new Rectangle(430, 546, 170, 30), new Rectangle(430, 576, 170, 30)),
            [2] = new(2, new Rectangle(170, 440, 110, 90), new Rectangle(140, 500, 170, 95), new Rectangle(140, 500, 170, 36), new Rectangle(140, 536, 170, 30), new Rectangle(140, 566, 170, 30)),
            [3] = new(3, new Rectangle(170, 170, 110, 90), new Rectangle(60, 190, 170, 95), new Rectangle(60, 190, 170, 36), new Rectangle(60, 226, 170, 30), new Rectangle(60, 256, 170, 30)),
            [4] = new(4, new Rectangle(430, 150, 110, 90), new Rectangle(420, 80, 180, 95), new Rectangle(420, 80, 180, 36), new Rectangle(420, 116, 180, 30), new Rectangle(420, 146, 180, 30)),
            [5] = new(5, new Rectangle(810, 170, 110, 90), new Rectangle(820, 190, 170, 95), new Rectangle(820, 190, 170, 36), new Rectangle(820, 226, 170, 30), new Rectangle(820, 256, 170, 30)),
            [6] = new(6, new Rectangle(820, 440, 110, 90), new Rectangle(740, 500, 170, 95), new Rectangle(740, 500, 170, 36), new Rectangle(740, 536, 170, 30), new Rectangle(740, 566, 170, 30))
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
