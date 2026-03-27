namespace ScreenshotScraper.Ocr;

public enum OcrBackend
{
    Windows,
    Paddle
}

public sealed class OcrEngineOptions
{
    public OcrBackend Backend { get; set; } = OcrBackend.Paddle;

    public PaddleOcrOptions Paddle { get; set; } = new();
}

public sealed class PaddleOcrOptions
{
    public string PythonExecutablePath { get; set; } = "python";

    public string WorkerScriptPath { get; set; } = Path.Combine("tools", "paddle_ocr_worker.py");

    public string Language { get; set; } = "en";

    public int TimeoutMilliseconds { get; set; } = 15000;

    public bool KeepWorkerWarm { get; set; } = true;
}
