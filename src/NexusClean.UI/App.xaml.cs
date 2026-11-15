using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexusClean.Infrastructure;
using NexusClean.UI.ViewModels;
using NexusClean.UI.Views;

namespace NexusClean.UI;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    public static void ApplyTheme(AppThemeMode mode)
    {
        if (Current?.Resources.MergedDictionaries is not { Count: > 0 } dicts)
        {
            return;
        }

        var sourceUri = mode == AppThemeMode.Dark
            ? new Uri("Themes/Theme.Dark.xaml", UriKind.Relative)
            : new Uri("Themes/Theme.Light.xaml", UriKind.Relative);

        dicts[0] = new ResourceDictionary { Source = sourceUri };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddNexusCleanCore();
        services.AddSingleton<MainDashboardViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
