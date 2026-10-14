using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WinClean.App.Services;
using WinClean.App.ViewModels;
using WinClean.App.Views;
using WinClean.Core.Cleaning;
using WinClean.Core.Logging;
using WinClean.Core.Paths;
using WinClean.Core.Rules;
using WinClean.Core.Scanning;
using WinClean.Core.Special;
using WinClean.Core.Storage;
using ILogger = Serilog.ILogger;

namespace WinClean.App;

/// <summary>
/// Composition root: builds the dependency container, applies the persisted theme, wires global
/// exception handling, and shows the shell. All business logic lives in WinClean.Core.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Global error handling: log everything, keep the app alive when possible.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomainException;

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
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IHistoryStore, HistoryStore>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<CleanerViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();
        ServiceProvider servicesProvider = _services;

        // Apply the persisted theme before any window is created.
        AppSettings settings = servicesProvider.GetRequiredService<ISettingsStore>().Load();
        servicesProvider.GetRequiredService<IThemeService>()
            .Apply(ThemeService.ParseThemeName(settings.Theme));

        MainWindow = new MainWindow
        {
            DataContext = servicesProvider.GetRequiredService<MainViewModel>(),
        };
        MainWindow.Show();

        // After an elevation relaunch, start the scan the user asked for.
        if (e.Args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
        {
            MainWindow.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () => servicesProvider.GetRequiredService<MainViewModel>().Cleaner.ScanCommand.Execute(null));
        }
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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.GetService<ILogger>()?.Error(e.Exception, "Unhandled exception on the UI thread");
        ShowErrorWindow("Something went wrong", e.Exception.Message, e.Exception.ToString());
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _services?.GetService<ILogger>()?.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private void OnUnhandledDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception? exception = e.ExceptionObject as Exception;
        _services?.GetService<ILogger>()?.Fatal(exception, "Unhandled exception, process will terminate");
    }

    private void ShowErrorWindow(string heading, string message, string details)
    {
        try
        {
            ErrorWindow window = new()
            {
                Owner = MainWindow,
            };
            window.SetError(heading, message, details);
            window.ShowDialog();
        }
        catch
        {
            // The error handler itself must never crash the app.
        }
    }
}
