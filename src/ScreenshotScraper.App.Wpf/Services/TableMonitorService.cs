using ScreenshotScraper.App.Wpf.Models;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models.HandHistory;
using ScreenshotScraper.Extraction.HandHistory;

namespace ScreenshotScraper.App.Wpf.Services;

public sealed class TableMonitorService
{
    private readonly IScreenshotService _screenshotService;
    private readonly ActionCornerDetector _actionCornerDetector;
    private readonly ITableVisionDetector _tableVisionDetector;
    private readonly DummyTableEventClient _tableEventClient;

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int? _lastDealerSeat;

    public TableMonitorService(
        IScreenshotService screenshotService,
        ActionCornerDetector actionCornerDetector,
        ITableVisionDetector tableVisionDetector,
        DummyTableEventClient tableEventClient)
    {
        _screenshotService = screenshotService;
        _actionCornerDetector = actionCornerDetector;
        _tableVisionDetector = tableVisionDetector;
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

            var hasCheckOrFold = await _actionCornerDetector.HasCheckOrFoldAsync(capture, cancellationToken).ConfigureAwait(false);
            if (!hasCheckOrFold)
            {
                return new TableMonitorTickEventArgs(
                    "No CHECK/FOLD text in the right-bottom action area. Skipped.",
                    null,
                    null,
                    null);
            }

            var detection = _tableVisionDetector.Detect(capture, []);
            var currentDealerSeat = detection.DealerSeat;
            var newGame = _lastDealerSeat.HasValue
                && currentDealerSeat.HasValue
                && _lastDealerSeat.Value != currentDealerSeat.Value;

            if (currentDealerSeat.HasValue)
            {
                _lastDealerSeat = currentDealerSeat.Value;
            }

            var payload = new TableMonitorPayload
            {
                Byte64 = [Convert.ToBase64String(capture.ImageBytes)],
                NewGame = newGame
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
