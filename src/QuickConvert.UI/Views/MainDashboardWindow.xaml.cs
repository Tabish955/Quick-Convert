using System.Windows;
using QuickConvert.UI.ViewModels;
using Wpf.Ui.Controls;

namespace QuickConvert.UI.Views;

public partial class MainDashboardWindow : FluentWindow
{
    public MainDashboardViewModel ViewModel { get; }

    public MainDashboardWindow(MainDashboardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                ViewModel.AddFilePaths(files);
            }
        }
    }
}
