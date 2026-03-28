namespace ScreenshotScraper.Ocr;

public sealed class OcrEngineOptions
{
    public PaddleOcrOptions Paddle { get; set; } = new();
}

public sealed class PaddleOcrOptions
{
    public string PythonExecutablePath { get; set; } = "python";

    public string WorkerScriptPath { get; set; } = Path.Combine("tools", "paddle_ocr_worker.py");

    public string Language { get; set; } = "en";

    public int TimeoutMilliseconds { get; set; } = 15000;

    public int StartupTimeoutMilliseconds { get; set; } = 90000;

    public bool KeepWorkerWarm { get; set; } = true;

    public bool EnableHighPerformanceInference { get; set; }

    public bool UseTensorRt { get; set; }

    public string Precision { get; set; } = "fp32";
}
