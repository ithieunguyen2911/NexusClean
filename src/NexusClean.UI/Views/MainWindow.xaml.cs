using System.Windows;
using NexusClean.UI.ViewModels;

namespace NexusClean.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainDashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
