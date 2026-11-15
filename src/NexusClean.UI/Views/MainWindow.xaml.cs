using System.Windows;
using System.Windows.Input;
using NexusClean.UI.ViewModels;

namespace NexusClean.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainDashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainDashboardViewModel vm && vm.Modal.IsOpen)
        {
            vm.Modal.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
