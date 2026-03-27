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
using System.Windows;
using System;

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
        var backendRaw = Environment.GetEnvironmentVariable("OCR_BACKEND");
        var backend = string.Equals(backendRaw, "windows", StringComparison.OrdinalIgnoreCase)
            ? OcrBackend.Windows
            : OcrBackend.Paddle;

        var timeoutRaw = Environment.GetEnvironmentVariable("PADDLE_TIMEOUT_MS");
        _ = int.TryParse(timeoutRaw, out var timeoutMs);
        if (timeoutMs <= 0)
        {
            timeoutMs = 15000;
        }

        var keepWarmRaw = Environment.GetEnvironmentVariable("PADDLE_KEEP_WARM");
        var keepWarm = !string.Equals(keepWarmRaw, "false", StringComparison.OrdinalIgnoreCase);

        return new OcrEngineOptions
        {
            Backend = backend,
            Paddle = new PaddleOcrOptions
            {
                PythonExecutablePath = Environment.GetEnvironmentVariable("PADDLE_PYTHON") ?? "python",
                WorkerScriptPath = Environment.GetEnvironmentVariable("PADDLE_WORKER_SCRIPT") ?? Path.Combine("tools", "paddle_ocr_worker.py"),
                Language = Environment.GetEnvironmentVariable("PADDLE_LANGUAGE") ?? "en",
                TimeoutMilliseconds = timeoutMs,
                KeepWorkerWarm = keepWarm
            }
        };
    }
}
