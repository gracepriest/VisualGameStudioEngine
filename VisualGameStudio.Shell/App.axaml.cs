using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
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

        // NOTE: the saved theme is applied later in OnFrameworkInitializationCompleted, right
        // after the DI container is built and ~/.vgs is loaded — the single store lives behind
        // ISettingsService, which does not exist yet at Initialize() time. No window has rendered
        // by then, so the theme is still set before anything is shown.
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

        // Dock 11.3 docking behavior (set before any DockControl exists):
        // - UseFloatingDockAdorner: dock adorners render in a floating transparent window, so the
        //   compass shows reliably over any surface (including floated panels) — the smoother
        //   drag-docking this upgrade was for.
        // - CloseFloatingWindowsOnMainWindowClose: floated panels can't outlive the IDE window.
        global::Dock.Settings.DockSettings.UseFloatingDockAdorner = true;
        global::Dock.Settings.DockSettings.CloseFloatingWindowsOnMainWindowClose = true;

        // Setup dependency injection
        var services = new ServiceCollection();
        services.ConfigureServices();
        Services = services.BuildServiceProvider();

        // Load ~/.vgs/settings.json before anything (e.g. MainWindowViewModel below) reads
        // settings, otherwise every Get() silently falls back to schema defaults.
        LoadUserSettingsAtStartup(Services);

        // Install the global per-window High-Contrast class hook BEFORE any window loads, so every
        // window (main, dialogs, Dock floating HostWindows) is class-stamped the moment it loads —
        // not just those open at theme-Apply time. Must precede window construction below.
        try { ThemeManager.EnsureGlobalWindowClassHook(); }
        catch (Exception ex) { LogCrash("THEME_HOOK", ex); }

        // Re-register imported VS Code themes from their saved file paths BEFORE applying the saved
        // theme, so a workbench.colorTheme that names an imported theme resolves instead of falling
        // back to Dark. Missing files are pruned. Must precede ApplyFromSettings.
        ReloadImportedThemesAtStartup(Services);

        // Apply the saved theme now that the single store is loaded and before any window is
        // constructed. (Moved out of Initialize(), which runs before the DI container exists —
        // reading the retired legacy %APPDATA% file there is exactly what split the two stores.)
        // Guarded like its startup siblings: the migration's Set() fires settings-changed
        // handlers synchronously and Apply touches EditorTheme/extension themes — an uncaught
        // throw here would reach Program.cs's [FATAL] and keep the IDE from launching at all.
        try { ThemeManager.ApplyFromSettings(); }
        catch (Exception ex) { LogCrash("THEME_APPLY", ex); }

        // Arm the background git auto-fetch timer (git.autoFetch / git.autoFetchInterval). Resolving
        // the singleton constructs it and starts its timer; it self-gates on whether a repo is open,
        // and the DI container disposes it on shutdown. Nothing else references it, so it must be
        // resolved explicitly here or it would never be created.
        try { _ = Services.GetRequiredService<GitAutoFetchService>(); }
        catch (Exception ex) { LogCrash("GIT_AUTOFETCH_START", ex); }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
            MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = MainWindow;

            // Stamp the HC class on the main window now (before first render) rather than waiting for
            // the Loaded hook, and so a restart already in High Contrast comes up class-styled — the
            // startup ThemeManager.ApplyFromSettings above ran before this window existed, and its
            // desktop.Windows sweep can't have seen an unshown window.
            ThemeManager.Register(MainWindow);

            // Force a final per-project layout/session save on exit (VS Code's shutdown flush),
            // then dispose the DI container so singleton services run their teardown.
            desktop.ShutdownRequested += (s, e) =>
            {
                try { mainViewModel.FlushWorkspaceStateForShutdown(); }
                catch (Exception ex) { LogCrash("SHUTDOWN_SAVE", ex); }

                // Disposing the container invokes LanguageService.Dispose()/DebugService.Dispose(),
                // which Kill the spawned `BasicLang --lsp` and debug-adapter processes. Without this
                // the language server is orphaned on every exit and copies pile up, locking
                // IDE/BasicLang.dll. Nothing else stops those processes.
                try { (Services as IDisposable)?.Dispose(); }
                catch (Exception ex) { LogCrash("SHUTDOWN_DISPOSE", ex); }
            };

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

    /// <summary>
    /// Resolves the settings service from the DI container and loads ~/.vgs/settings.json from
    /// disk. Extracted as its own method so the startup contract can be exercised directly by
    /// tests without spinning up Avalonia's application lifecycle.
    ///
    /// This runs on the UI thread with the AvaloniaSynchronizationContext installed but the
    /// dispatcher loop not yet pumping, so awaiting the load inline would queue continuations
    /// that can never run and deadlock the launch. Task.Run detaches the whole await chain from
    /// the captured context (the thread-pool has no sync context), making the block-and-wait
    /// safe regardless of the service's internals. Settings errors are logged, never rethrown:
    /// a broken settings store must not prevent the IDE from launching.
    /// </summary>
    public static void LoadUserSettingsAtStartup(IServiceProvider services)
    {
        try
        {
            var settingsService = services.GetRequiredService<ISettingsService>();
            Task.Run(() => settingsService.LoadAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogCrash("SETTINGS_LOAD", ex);
        }
    }

    /// <summary>
    /// Re-registers imported VS Code themes from their saved file paths (workbench.importedThemes)
    /// so a saved theme that names one resolves at startup. Blocks like
    /// <see cref="LoadUserSettingsAtStartup"/> — same pre-dispatcher-loop deadlock hazard, so the
    /// await chain is detached onto the thread pool via Task.Run. Never rethrows: a broken/missing
    /// theme file must not stop the IDE from launching.
    /// </summary>
    public static void ReloadImportedThemesAtStartup(IServiceProvider services)
    {
        try
        {
            var settingsService = services.GetRequiredService<ISettingsService>();
            Task.Run(() => ThemeManager.ReloadImportedThemesAsync(settingsService)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogCrash("IMPORTED_THEMES_RELOAD", ex);
        }
    }
}
