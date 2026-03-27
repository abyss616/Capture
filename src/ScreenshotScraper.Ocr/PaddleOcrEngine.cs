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
    private int _workerReady;

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
        if (Interlocked.CompareExchange(ref _workerReady, 1, 1) == 1)
        {
            return;
        }

        await _transport.SelfTestAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _workerReady, 1);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }
}