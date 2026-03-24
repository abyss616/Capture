using System.Windows;
using Microsoft.Win32;
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

    private async void UploadScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select screenshot",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All Files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.LoadScreenshotAsync(dialog.FileName);
        }
    }
}
