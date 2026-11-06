using System.Windows;
using QuickConvert.Core.Models;
using QuickConvert.UI.ViewModels;
using Wpf.Ui.Controls;

namespace QuickConvert.UI.Views;

public partial class QuickPopupWindow : FluentWindow
{
    public QuickPopupViewModel ViewModel { get; }

    public QuickPopupWindow(QuickPopupViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.RequestClose += () => Dispatcher.Invoke(Close);
    }

    /// <summary>
    /// Repositions the window smoothly adjacent to the user's cursor position.
    /// </summary>
    public void PositionNearCursor(ScreenPoint pt)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        double targetLeft = pt.X + 20;
        double targetTop = pt.Y + 20;

        if (targetLeft + Width > screenWidth)
        {
            targetLeft = screenWidth - Width - 30;
        }

        if (targetTop + Height > screenHeight)
        {
            targetTop = screenHeight - Height - 50;
        }

        if (targetLeft < 20) targetLeft = 20;
        if (targetTop < 20) targetTop = 20;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = targetLeft;
        Top = targetTop;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                ViewModel.LoadFiles(files);
            }
        }
    }
}
