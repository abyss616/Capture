using System.Globalization;
using System.Text;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

#if WINDOWS
using System.Runtime.Versioning;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
#endif

namespace ScreenshotScraper.Ocr;

/// <summary>
/// Windows-friendly OCR engine for poker table screenshots.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
#if WINDOWS
    private readonly Lazy<ConfiguredWindowsOcrEngine> _configuredEngine;

    public WindowsOcrEngine()
        : this(new Lazy<ConfiguredWindowsOcrEngine>(CreateConfiguredEngine, LazyThreadSafetyMode.ExecutionAndPublication))
    {
    }

    internal WindowsOcrEngine(Func<ConfiguredWindowsOcrEngine> engineFactory)
        : this(new Lazy<ConfiguredWindowsOcrEngine>(engineFactory, LazyThreadSafetyMode.ExecutionAndPublication))
    {
    }

    private WindowsOcrEngine(Lazy<ConfiguredWindowsOcrEngine> configuredEngine)
    {
        _configuredEngine = configuredEngine;
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    public async Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(image);

        if (image.ImageBytes.Length == 0)
        {
            throw new InvalidOperationException("OCR cannot run because the captured screenshot did not contain any image bytes.");
        }

        var configuredEngine = _configuredEngine.Value;

        try
        {
            using var softwareBitmap = await LoadSoftwareBitmapAsync(image.ImageBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var ocrResult = await configuredEngine.Engine.RecognizeAsync(softwareBitmap);
            cancellationToken.ThrowIfCancellationRequested();

            return NormalizeOcrText(ocrResult.Text);
        }
        catch (Exception exception) when (exception is not OcrEngineUnavailableException)
        {
            throw new InvalidOperationException(
                $"Windows OCR failed while reading the screenshot using recognizer language '{configuredEngine.LanguageTag}'.",
                exception);
        }
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    private static async Task<SoftwareBitmap> LoadSoftwareBitmapAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var decodedBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        cancellationToken.ThrowIfCancellationRequested();

        return SoftwareBitmap.Convert(decodedBitmap, BitmapPixelFormat.Gray8);
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    private static ConfiguredWindowsOcrEngine CreateConfiguredEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is not null)
        {
            return new ConfiguredWindowsOcrEngine(engine, DescribeConfiguredLanguages(GlobalizationPreferences.Languages));
        }

        var fallbackLanguage = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
        if (fallbackLanguage is not null)
        {
            engine = OcrEngine.TryCreateFromLanguage(fallbackLanguage);
            if (engine is not null)
            {
                return new ConfiguredWindowsOcrEngine(engine, fallbackLanguage.LanguageTag);
            }
        }

        throw new OcrEngineUnavailableException(
            "Windows OCR is not ready. Install at least one Windows OCR language pack in Settings > Time & language > Language & region, then restart the app.");
    }

    private static string NormalizeOcrText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawText.Length);
        using var reader = new StringReader(rawText);

        while (reader.ReadLine() is { } line)
        {
            var normalizedLine = line.Normalize(NormalizationForm.FormKC).Trim();
            if (normalizedLine.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(normalizedLine);
        }

        return builder.ToString();
    }

    private static string DescribeConfiguredLanguages(IReadOnlyList<string> languages)
    {
        var filtered = languages
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return filtered.Length == 0
            ? CultureInfo.CurrentUICulture.Name
            : string.Join(", ", filtered);
    }

    internal sealed record ConfiguredWindowsOcrEngine(OcrEngine Engine, string LanguageTag);
#else
    public Task<string> ReadTextAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new OcrEngineUnavailableException(
            "Windows OCR is only available from the Windows-targeted build of ScreenshotScraper. Run the WPF app on Windows 10/11 with an installed OCR language pack.");
    }
#endif
}
