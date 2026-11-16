using System.Windows;
using QuickConvert.Core.Tools;
using Wpf.Ui.Controls;

namespace QuickConvert.UI.Views;

public partial class MissingToolsWindow : FluentWindow
{
    public MissingToolsWindow(IEnumerable<MissingToolInfo> missingTools)
    {
        InitializeComponent();
        MissingToolsItemsControl.ItemsSource = missingTools;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
