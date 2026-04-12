using ScreenshotScraper.App.Wpf.Models;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Extraction.HandHistory;

namespace ScreenshotScraper.App.Wpf.Services;

public sealed class TableMonitorService
{
    private readonly IScreenshotService _screenshotService;
    private readonly ActionCornerDetector _actionCornerDetector;
    private readonly ITableVisionDetector _tableVisionDetector;
    private readonly IPreHeroScreenshotParser _preHeroScreenshotParser;
    private readonly DummyTableEventClient _tableEventClient;

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int? _lastDealerSeat;
    private string? _lastProcessedStateKey;

    public TableMonitorService(
        IScreenshotService screenshotService,
        ActionCornerDetector actionCornerDetector,
        ITableVisionDetector tableVisionDetector,
        IPreHeroScreenshotParser preHeroScreenshotParser,
        DummyTableEventClient tableEventClient)
    {
        _screenshotService = screenshotService;
        _actionCornerDetector = actionCornerDetector;
        _tableVisionDetector = tableVisionDetector;
        _preHeroScreenshotParser = preHeroScreenshotParser;
        _tableEventClient = tableEventClient;
    }

    public bool IsRunning => _runTask is { IsCompleted: false };

    public event EventHandler<TableMonitorTickEventArgs>? TickCompleted;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _runCts = new CancellationTokenSource();
        _runTask = RunAsync(_runCts.Token);
    }

    public async Task StopAsync()
    {
        if (_runCts is null)
        {
            return;
        }

        _runCts.Cancel();

        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runCts.Dispose();
        _runCts = null;
        _runTask = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var args = await ExecuteTickAsync(cancellationToken).ConfigureAwait(false);
            TickCompleted?.Invoke(this, args);
        }
    }

    private async Task<TableMonitorTickEventArgs> ExecuteTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var capture = await _screenshotService.CaptureAsync(cancellationToken).ConfigureAwait(false);

            var hasCall = await _actionCornerDetector.HasCallAsync(capture, cancellationToken).ConfigureAwait(false);
            if (!hasCall)
            {
                return new TableMonitorTickEventArgs(
                    "No CALL text in the right-bottom action area. Skipped.",
                    null,
                    null,
                    null);
            }

            var detection = _tableVisionDetector.Detect(capture, []);
            var currentDealerSeat = detection.DealerSeat;
            var newGame = _lastDealerSeat.HasValue
                && currentDealerSeat.HasValue
                && _lastDealerSeat.Value != currentDealerSeat.Value;

            if (newGame)
            {
                _lastProcessedStateKey = null;
            }

            if (currentDealerSeat.HasValue)
            {
                _lastDealerSeat = currentDealerSeat.Value;
            }

            var snapshot = await TryParseSnapshotAsync(capture, cancellationToken).ConfigureAwait(false);
            var stateKey = SemanticHandStateKeyBuilder.Build(detection, snapshot);
            if (string.Equals(stateKey, _lastProcessedStateKey, StringComparison.Ordinal))
            {
                return new TableMonitorTickEventArgs(
                    "Same semantic hand state is still active. Skipped duplicate.",
                    currentDealerSeat,
                    newGame,
                    null);
            }

            _lastProcessedStateKey = stateKey;

            var payload = new TableMonitorPayload
            {
                Byte64 = [Convert.ToBase64String(capture.ImageBytes)],
                NewGame = newGame,
                StateKey = stateKey
            };

            var endpointResult = await _tableEventClient.SendAsync(payload, cancellationToken).ConfigureAwait(false);

            return new TableMonitorTickEventArgs(
                endpointResult,
                currentDealerSeat,
                newGame,
                payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new TableMonitorTickEventArgs(
                $"Monitor tick failed: {exception.Message}",
                null,
                null,
                null);
        }
    }

    private async Task<PartialHandHistorySnapshot?> TryParseSnapshotAsync(CapturedImage capture, CancellationToken cancellationToken)
    {
        try
        {
            return await _preHeroScreenshotParser.ParseAsync(capture, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class TableMonitorTickEventArgs : EventArgs
{
    public TableMonitorTickEventArgs(
        string message,
        int? dealerSeat,
        bool? newGame,
        TableMonitorPayload? payload)
    {
        Message = message;
        DealerSeat = dealerSeat;
        NewGame = newGame;
        Payload = payload;
    }

    public string Message { get; }

    public int? DealerSeat { get; }

    public bool? NewGame { get; }

    public TableMonitorPayload? Payload { get; }
}
