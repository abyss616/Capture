using System.IO;
using System.Windows.Media.Imaging;

namespace ScreenshotScraper.App.Wpf.Helpers;

internal static class BitmapImageFactory
{
    public static BitmapImage? Create(byte[]? imageBytes)
    {
        if (imageBytes is not { Length: > 0 })
        {
            return null;
        }

        using var stream = new MemoryStream(imageBytes);
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }
}
