using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using System.Diagnostics;

namespace ScreenshotScraper.Ocr;

/// <summary>
/// PaddleOCR adapter for full-table and tiny table-scene ROI text extraction.
/// Keeps parser architecture unchanged by staying behind IOcrEngine.
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private readonly IPaddleOcrTransport _transport;
    private readonly PaddleOcrOptions _options;
    private readonly object _startupSync = new();
    private Task? _startupTask;

    private static readonly byte[] WarmupImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8//8/AwMDAwMDAwMABDsCA3x6k2kAAAAASUVORK5CYII=");

    public PaddleOcrEngine(PaddleOcrOptions options)
        : this(options, new PaddleOcrStdioTransport(options))
    {
    }

    internal PaddleOcrEngine(PaddleOcrOptions options, IPaddleOcrTransport transport)
    {
        _options = options;
        _transport = transport;
    }

    public async Task<OcrResult> ReadAsync(CapturedImage image, OcrRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (image.ImageBytes.Length == 0)
        {
            return new OcrResult(string.Empty, Backend: "paddle", Confidence: null, RawPayload: "{}", Lines: [], ElapsedMilliseconds: 0);
        }

        await EnsureWorkerReadyAsync(cancellationToken).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var requestJson = PaddleOcrProtocol.BuildRequest(
            image.ImageBytes,
            request.RoiType,
            request.Variant,
            request.Language ?? _options.Language);

        var responseJson = await _transport.InvokeAsync(requestJson, cancellationToken).ConfigureAwait(false);
        var response = PaddleOcrProtocol.ParseResponse(responseJson);

        sw.Stop();

        return new OcrResult(
            response.Text,
            Backend: "paddle",
            Confidence: response.Confidence,
            RawPayload: response.RawJson,
            Lines: response.Lines.Select(line => new OcrLineResult(line.Text, line.Confidence)).ToArray(),
            ElapsedMilliseconds: sw.ElapsedMilliseconds);
    }


    public async Task EnsureWorkerReadyAsync(CancellationToken cancellationToken = default)
    {
        Task startupTask;
        lock (_startupSync)
        {
            _startupTask ??= StartupAndWarmupCoreAsync();
            startupTask = _startupTask;
        }

        await startupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StartupAndWarmupCoreAsync()
    {
        var startupStopwatch = Stopwatch.StartNew();
        Debug.WriteLine("[PaddleOCR] Startup requested.");

        await _transport.EnsureProcessStartedAsync(CancellationToken.None).ConfigureAwait(false);

        Debug.WriteLine("[PaddleOCR] Warmup begin.");
        var warmupRequest = PaddleOcrProtocol.BuildRequest(WarmupImageBytes, "warmup", "startup", _options.Language);
        await _transport.InvokeStartupAsync(warmupRequest, CancellationToken.None).ConfigureAwait(false);

        startupStopwatch.Stop();
        Debug.WriteLine($"[PaddleOCR] Warmup complete. Worker ready in {startupStopwatch.ElapsedMilliseconds} ms.");
    }

    public void StartWorkerWarmupInBackground()
    {
        var startupTask = EnsureWorkerReadyAsync();
        _ = startupTask.ContinueWith(
            task => Debug.WriteLine($"[PaddleOCR] Background warmup failed: {task.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }
}
