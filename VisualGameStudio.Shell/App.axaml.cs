using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.Configuration;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.Views;

namespace VisualGameStudio.Shell;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window? MainWindow { get; private set; }

    private static readonly string CrashLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "vgs_crash.log");

    private static void LogCrash(string prefix, Exception? ex)
    {
        try
        {
            var msg = $"[{DateTime.Now:HH:mm:ss}] [{prefix}] {ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}\n";
            if (ex?.InnerException != null)
                msg += $"  [INNER] {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n  {ex.InnerException.StackTrace}\n";
            System.IO.File.AppendAllText(CrashLogPath, msg);
        }
        catch { }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Global exception handlers for crash diagnostics
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogCrash("UNHANDLED", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TASK", e.Exception);
            e.SetObserved();
        };

        // Avalonia UI thread exceptions
        RxApp_DefaultExceptionHandler();

        // Setup dependency injection
        var services = new ServiceCollection();
        services.ConfigureServices();
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
            MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = MainWindow;

            // Auto-load TestProject for debugging
            desktop.MainWindow.Opened += async (s, e) =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var projectService = Services.GetRequiredService<IProjectService>();
                    var testProjectPath = @"C:\Users\melvi\Documents\TestProject\TestProject.blproj";
                    if (System.IO.File.Exists(testProjectPath))
                    {
                        try
                        {
                            await projectService.OpenProjectAsync(testProjectPath);

                            // Auto-open Program.bas and set a breakpoint for testing
                            var programBasPath = @"C:\Users\melvi\Documents\TestProject\Program.bas";
                            if (System.IO.File.Exists(programBasPath))
                            {
                                // Set breakpoints for testing variable inspection
                                mainViewModel.Breakpoints.AddBreakpoint(programBasPath, 14); // Dim counter
                                mainViewModel.Breakpoints.AddBreakpoint(programBasPath, 19); // Inside loop - PrintLine with i
                                mainViewModel.Breakpoints.AddBreakpoint(programBasPath, 31); // After function call
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to auto-load project: {ex.Message}");
                        }
                    }
                }, DispatcherPriority.Background);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RxApp_DefaultExceptionHandler()
    {
        // No-op: Avalonia scheduler doesn't expose a public catch handler
    }
}
