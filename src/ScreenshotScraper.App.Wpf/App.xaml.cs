using System.Windows;
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
        services.AddSingleton<IOcrEngine, DummyOcrEngine>();
        services.AddSingleton<IPreHeroScreenshotParser, PreHeroScreenshotParser>();
        services.AddSingleton<IDataExtractor, DataExtractor>();
        services.AddSingleton<IXmlBuilder, XmlBuilder>();
        services.AddSingleton<IProcessingWorkflowService, ProcessingWorkflowService>();
        services.AddSingleton<IPreHeroHandHistoryXmlWorkflow, PreHeroHandHistoryXmlWorkflow>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
