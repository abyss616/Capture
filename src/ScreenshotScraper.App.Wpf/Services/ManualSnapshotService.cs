using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using ScreenshotScraper.App.Wpf.Models;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction.HandHistory;

namespace ScreenshotScraper.App.Wpf.Services;

public sealed class ManualSnapshotService
{
    private static readonly Uri DummyEndpoint = new("https://example.invalid/api/table-snapshot");

    private readonly IScreenshotService _screenshotService;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITableVisionDetector _tableVisionDetector;
    private readonly HttpClient _httpClient = new();

    private int? _lastDealerSeat;

    public ManualSnapshotService(
        IScreenshotService screenshotService,
        IOcrEngine ocrEngine,
        ITableVisionDetector tableVisionDetector)
    {
        _screenshotService = screenshotService;
        _ocrEngine = ocrEngine;
        _tableVisionDetector = tableVisionDetector;
    }

    public Task<ManualSnapshotResult> SendSnapshotAsync(CancellationToken cancellationToken = default)
        => SendSnapshotAsync(forceNewGame: false, cancellationToken);

    public async Task<ManualSnapshotResult> SendSnapshotAsync(bool forceNewGame, CancellationToken cancellationToken = default)
    {
        var capture = await _screenshotService.CaptureAsync(cancellationToken).ConfigureAwait(false);

        var hasAction = await HasCheckOrFoldAsync(capture, cancellationToken).ConfigureAwait(false);
        if (!hasAction)
        {
            return new ManualSnapshotResult(
                capture,
                "CHECK/FOLD not found in the right-bottom corner. Skipped.",
                null,
                null,
                false);
        }

        var detection = _tableVisionDetector.Detect(capture, []);
        var currentDealerSeat = detection.DealerSeat;

        var detectedNewGame = _lastDealerSeat.HasValue
            && currentDealerSeat.HasValue
            && _lastDealerSeat.Value != currentDealerSeat.Value;

        var newGame = forceNewGame || detectedNewGame;

        if (currentDealerSeat.HasValue)
        {
            _lastDealerSeat = currentDealerSeat.Value;
        }

        var payload = new ManualSnapshotPayload
        {
            Byte64 = [Convert.ToBase64String(capture.ImageBytes)],
            NewGame = newGame
        };

        var sent = await PostPayloadAsync(payload, cancellationToken).ConfigureAwait(false);

        var status = sent
            ? $"Snapshot sent. Dealer seat: {(currentDealerSeat?.ToString() ?? "unknown")}. NewGame={newGame}."
            : $"Snapshot post failed. Dealer seat: {(currentDealerSeat?.ToString() ?? "unknown")}. NewGame={newGame}.";

        return new ManualSnapshotResult(capture, status, payload, currentDealerSeat, sent);
    }

    private async Task<bool> HasCheckOrFoldAsync(CapturedImage capture, CancellationToken cancellationToken)
    {
        using var frame = Cv2.ImDecode(capture.ImageBytes, ImreadModes.Color);
        if (frame.Empty())
        {
            return false;
        }

        var x = (int)Math.Round(frame.Width * 0.63);
        var y = (int)Math.Round(frame.Height * 0.82);
        var width = Math.Max(1, (int)Math.Round(frame.Width * 0.35));
        var height = Math.Max(1, (int)Math.Round(frame.Height * 0.16));

        x = Math.Clamp(x, 0, Math.Max(0, frame.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frame.Height - 1));
        width = Math.Clamp(width, 1, frame.Width - x);
        height = Math.Clamp(height, 1, frame.Height - y);

        var roiRect = new Rect(x, y, width, height);
        using var roi = new Mat(frame, roiRect);

        var roiBytes = roi.ToBytes(".png");
        var roiImage = new CapturedImage
        {
            ImageBytes = roiBytes,
            Width = roi.Width,
            Height = roi.Height,
            CapturedAtUtc = capture.CapturedAtUtc,
            SourceDescription = "Action corner ROI",
            WindowTitle = capture.WindowTitle,
            ProcessName = capture.ProcessName,
            WindowLeft = capture.WindowLeft + x,
            WindowTop = capture.WindowTop + y,
            WindowWidth = roi.Width,
            WindowHeight = roi.Height,
            IsVisible = capture.IsVisible,
            IsForegroundWindow = capture.IsForegroundWindow,
            WindowHandle = capture.WindowHandle,
            CaptureMethod = capture.CaptureMethod,
            MonitorDeviceName = capture.MonitorDeviceName
        };

        var text = await _ocrEngine.ReadTextAsync(roiImage, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToUpperInvariant();
        return normalized.Contains("CHECK", StringComparison.Ordinal)
            || normalized.Contains("FOLD", StringComparison.Ordinal);
    }

    private async Task<bool> PostPayloadAsync(ManualSnapshotPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(DummyEndpoint, content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record ManualSnapshotResult(
    CapturedImage Capture,
    string StatusMessage,
    ManualSnapshotPayload? Payload,
    int? DealerSeat,
    bool PostAttempted);
