using OpenCvSharp;
using ScreenshotScraper.App.Wpf.Models;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Extraction.HandHistory;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScreenshotScraper.App.Wpf.Services;

public sealed class ManualSnapshotService
{
    private readonly IScreenshotService _screenshotService;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITableVisionDetector _tableVisionDetector;
    private readonly HttpClient _httpClient = new();
    private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";
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
            Byte64 = Convert.ToBase64String(capture.ImageBytes),
            NewGame = newGame
        };

        var actionMix = await PostPayloadAsync(payload, cancellationToken).ConfigureAwait(false);

        return new ManualSnapshotResult(capture, "Snapshot sent.", actionMix, currentDealerSeat, true);
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

    private async Task<PokerActionMix> PostPayloadAsync(ManualSnapshotPayload payload, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAPI_AI_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Environment variable OPENAPI_AI_KEY is missing.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var requestBody = new
        {
            model = "gpt-5.4",
            instructions = """
You are a poker decision engine for No-Limit Hold'em.

Analyze the screenshot and return only JSON with:
- check_percent
- call_percent
- fold_percent
- bet_percent

Rules:
- integers only
- each value 0 to 100
- total must equal 100
- use 0 for impossible actions
- no explanation
- if bet_percent, how many big blinds should bet
""",
            input = new object[]
            {
        new
        {
            role = "user",
            content = new object[]
            {
                new
                {
                    type = "input_text",
                    text = $"newGame={payload.NewGame}. Return JSON."
                },
                new
                {
                    type = "input_image",
                    image_url = $"data:image/png;base64,{payload.Byte64}"
                }
            }
        }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "poker_action_mix",
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            check_percent = new { type = "integer", minimum = 0, maximum = 100 },
                            call_percent = new { type = "integer", minimum = 0, maximum = 100 },
                            fold_percent = new { type = "integer", minimum = 0, maximum = 100 },
                            bet_percent = new { type = "integer", minimum = 0, maximum = 100 }
                        },
                        required = new[]
                        {
                    "check_percent",
                    "call_percent",
                    "fold_percent",
                    "bet_percent"
                }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI request failed. Status={(int)response.StatusCode} {response.StatusCode}\n" +
                $"Request JSON:\n{json}\n\n" +
                $"Response body:\n{body}");
        }

        return ParseActionMix(body);
    }

    private static PokerActionMix ParseActionMix(string responseBody)
    {
        using var responseDocument = JsonDocument.Parse(responseBody);
        if (!responseDocument.RootElement.TryGetProperty("output", out var outputElement)
            || outputElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OpenAI response does not include an output array.");
        }

        foreach (var outputItem in outputElement.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentElement.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("text", out var textElement)
                    || textElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var jsonText = textElement.GetString();
                if (string.IsNullOrWhiteSpace(jsonText))
                {
                    continue;
                }

                var actionMix = JsonSerializer.Deserialize<PokerActionMix>(jsonText);
                if (actionMix is not null)
                {
                    return actionMix;
                }
            }
        }

        throw new InvalidOperationException("OpenAI response did not contain a valid poker action mix JSON object.");
    }
}

public sealed record ManualSnapshotResult(
    CapturedImage Capture,
    string StatusMessage,
    PokerActionMix? Payload,
    int? DealerSeat,
    bool PostAttempted);
