using Microsoft.Extensions.DependencyInjection;
using ScreenshotScraper.App.Wpf.ViewModels;
using ScreenshotScraper.Capture;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Services;
using ScreenshotScraper.Core.Services.HandHistory;
using ScreenshotScraper.Extraction;
using ScreenshotScraper.Extraction.HandHistory;
using ScreenshotScraper.Imaging;
using ScreenshotScraper.Ocr;
using ScreenshotScraper.Xml;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ScreenshotScraper.App.Wpf;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var ocrEngine = _serviceProvider.GetRequiredService<IOcrEngine>();
        if (ocrEngine is PaddleOcrEngine paddleOcrEngine)
        {
            paddleOcrEngine.StartWorkerWarmupInBackground();
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new PokerWindowCaptureOptions());
        services.AddSingleton<IWindowLocator, WindowLocator>();
        services.AddSingleton<IScreenshotService, PokerTableScreenshotService>();
        services.AddSingleton<IImagePreprocessor, ImagePreprocessor>();
        services.AddSingleton(BuildOcrOptions());
        services.AddSingleton<IOcrEngine>(provider =>
        {
            var options = provider.GetRequiredService<OcrEngineOptions>();
            return OcrEngineFactory.Create(options);
        });
        services.AddSingleton<ITableHeaderExtractor, OcrTableHeaderExtractor>();
        services.AddSingleton<ISeatSnapshotExtractor, FixedLayoutSeatSnapshotExtractor>();
        services.AddSingleton<ICardExtractor, OcrHeroCardExtractor>();
        services.AddSingleton<ITableVisionDetector, OpenCvTableVisionDetector>();
        services.AddSingleton<IPreHeroActionInferencer, PreHeroActionInferencer>();
        services.AddSingleton<IPreHeroScreenshotParser, PreHeroScreenshotParser>();
        services.AddSingleton<IDataExtractor, DataExtractor>();
        services.AddSingleton<IXmlBuilder, XmlBuilder>();
        services.AddSingleton<IProcessingWorkflowService, ProcessingWorkflowService>();
        services.AddSingleton<IPreHeroHandHistoryXmlWorkflow, PreHeroHandHistoryXmlWorkflow>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private static OcrEngineOptions BuildOcrOptions()
    {
        var timeoutRaw = Environment.GetEnvironmentVariable("PADDLE_TIMEOUT_MS");
        _ = int.TryParse(timeoutRaw, out var timeoutMs);
        if (timeoutMs <= 0)
        {
            timeoutMs = 15000;
        }

        var startupTimeoutRaw = Environment.GetEnvironmentVariable("PADDLE_STARTUP_TIMEOUT_MS");
        _ = int.TryParse(startupTimeoutRaw, out var startupTimeoutMs);
        if (startupTimeoutMs <= 0)
        {
            startupTimeoutMs = 90000;
        }

        var keepWarmRaw = Environment.GetEnvironmentVariable("PADDLE_KEEP_WARM");
        var keepWarm = !string.Equals(keepWarmRaw, "false", StringComparison.OrdinalIgnoreCase);
        if (!keepWarm)
        {
            Debug.WriteLine("[PaddleOCR] KeepWorkerWarm=false was requested, but the app enforces warm worker lifetime for session performance.");
        }

        return new OcrEngineOptions
        {
            Paddle = new PaddleOcrOptions
            {
                PythonExecutablePath =
                    Environment.GetEnvironmentVariable("PADDLE_PYTHON")
                    ?? @"C:\Users\amd\AppData\Local\Programs\Python\Python311\python.exe",

                WorkerScriptPath =
                    Environment.GetEnvironmentVariable("PADDLE_WORKER_SCRIPT")
                    ?? Path.Combine(AppContext.BaseDirectory, "tools", "paddle_ocr_worker.py"),

                Language = Environment.GetEnvironmentVariable("PADDLE_LANGUAGE") ?? "en",
                TimeoutMilliseconds = timeoutMs,
                StartupTimeoutMilliseconds = startupTimeoutMs,
                KeepWorkerWarm = true,
                EnableHighPerformanceInference = string.Equals(Environment.GetEnvironmentVariable("PADDLE_ENABLE_HPI"), "true", StringComparison.OrdinalIgnoreCase),
                UseTensorRt = string.Equals(Environment.GetEnvironmentVariable("PADDLE_USE_TENSORRT"), "true", StringComparison.OrdinalIgnoreCase),
                Precision = Environment.GetEnvironmentVariable("PADDLE_PRECISION") ?? "fp32"
            }
        };
    }
}
