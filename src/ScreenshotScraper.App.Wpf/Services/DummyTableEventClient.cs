using System.Net.Http;
using System.Text;
using System.Text.Json;
using ScreenshotScraper.App.Wpf.Models;

namespace ScreenshotScraper.App.Wpf.Services;

public sealed class DummyTableEventClient
{
    private static readonly Uri DummyEndpoint = new("https://example.invalid/api/table-snapshot");

    private readonly HttpClient _httpClient;

    public DummyTableEventClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> SendAsync(TableMonitorPayload payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(DummyEndpoint, content, cancellationToken).ConfigureAwait(false);
            return $"POST {(int)response.StatusCode} {response.ReasonPhrase} -> {DummyEndpoint}";
        }
        catch (Exception exception)
        {
            return $"Dummy endpoint call failed ({DummyEndpoint}): {exception.Message}";
        }
    }
}
