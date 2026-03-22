using System.Windows;
using ScreenshotScraper.App.Wpf.ViewModels;

namespace ScreenshotScraper.App.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.CaptureAsync();
    }

    private async void BuildXmlButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.BuildXmlAsync();
    }
}
