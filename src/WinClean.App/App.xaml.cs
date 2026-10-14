using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WinClean.App.ViewModels;
using WinClean.Core.Cleaning;
using WinClean.Core.Logging;
using WinClean.Core.Paths;
using WinClean.Core.Rules;
using WinClean.Core.Scanning;
using WinClean.Core.Special;
using ILogger = Serilog.ILogger;

namespace WinClean.App;

/// <summary>
/// Composition root: builds the dependency container and shows the shell.
/// All business logic lives in WinClean.Core; this class only wires things together.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IServiceCollection services = new ServiceCollection();

        ILogger logger = WinCleanLogger.CreateFileLogger();
        services.AddSingleton(logger);
        services.AddSingleton(PathValidator.CreateDefault());
        services.AddSingleton<IRecycleBinEstimator, ShellRecycleBinEstimator>();
        services.AddSingleton<IRecycleBinEmptier, ShellRecycleBinEmptier>();
        services.AddSingleton<IRuleLoader, RuleLoader>();
        services.AddSingleton<IScanner, Scanner>();
        services.AddSingleton<ICleaner, Cleaner>();
        services.AddSingleton<CleanerViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();

        MainWindow = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainViewModel>(),
        };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            if (_services.GetRequiredService<ILogger>() is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _services.Dispose();
        }

        base.OnExit(e);
    }
}
