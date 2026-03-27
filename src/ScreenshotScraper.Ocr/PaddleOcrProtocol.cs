using System.Text.Json;

namespace ScreenshotScraper.Ocr;

internal sealed record PaddleOcrLine(string Text, double? Confidence);
internal sealed record PaddleOcrResponse(string Text, double? Confidence, string RawJson, IReadOnlyList<PaddleOcrLine> Lines);

internal static class PaddleOcrProtocol
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static string BuildRequest(byte[] imageBytes, string roiType, string variant, string language)
    {
        var payload = new
        {
            image_base64 = Convert.ToBase64String(imageBytes),
            roi_type = roiType,
            variant,
            language
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static PaddleOcrResponse ParseResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException("PaddleOCR worker returned an empty response.");
        }

        var root = JsonDocument.Parse(responseJson).RootElement;
        var ok = root.TryGetProperty("ok", out var okNode) && okNode.GetBoolean();
        if (!ok)
        {
            var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : "Unknown PaddleOCR worker error.";
            throw new InvalidOperationException($"PaddleOCR worker failed: {error}");
        }

        var text = root.TryGetProperty("text", out var textNode) ? (textNode.GetString() ?? string.Empty) : string.Empty;
        double? confidence = null;
        if (root.TryGetProperty("confidence", out var confidenceNode) && confidenceNode.ValueKind == JsonValueKind.Number)
        {
            confidence = confidenceNode.GetDouble();
        }

        var lines = new List<PaddleOcrLine>();
        if (root.TryGetProperty("lines", out var linesNode) && linesNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesNode.EnumerateArray())
            {
                var lineText = line.TryGetProperty("text", out var lineTextNode) ? (lineTextNode.GetString() ?? string.Empty) : string.Empty;
                double? lineConfidence = null;
                if (line.TryGetProperty("confidence", out var lineConfidenceNode) && lineConfidenceNode.ValueKind == JsonValueKind.Number)
                {
                    lineConfidence = lineConfidenceNode.GetDouble();
                }

                lines.Add(new PaddleOcrLine(lineText, lineConfidence));
            }
        }

        return new PaddleOcrResponse(text, confidence, responseJson, lines);
    }
}
