using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace BasicLang.VisualStudio;

/// <summary>
/// BasicLang Visual Studio Package - main entry point for the extension.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(Guids.PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string, PackageAutoLoadFlags.BackgroundLoad)]

// Menu commands
[ProvideMenuResource("Menus.ctmenu", 1)]

// Options pages
[ProvideOptionPage(typeof(Options.GeneralOptionsPage), "BasicLang", "General", 0, 0, true)]
[ProvideOptionPage(typeof(Options.CompilerOptionsPage), "BasicLang", "Compiler", 0, 0, true)]

// Project type registration for CPS
[ProvideProjectFactory(
    typeof(ProjectSystem.BasicLangProjectFactory),
    "BasicLang",
    "BasicLang Project Files (*.blproj);*.blproj",
    "blproj",
    "blproj",
    null,
    LanguageVsTemplate = "BasicLang")]

public sealed class BasicLangPackage : AsyncPackage
{
    /// <summary>
    /// Gets the singleton instance of this package.
    /// </summary>
    public static BasicLangPackage? Instance { get; private set; }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        Instance = this;

        await base.InitializeAsync(cancellationToken, progress);

        // Switch to main thread for UI operations
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Each initialization step is guarded separately so a failure in one
        // (e.g. factory registration) doesn't take down the others and surface
        // a "package did not load correctly" gold bar on every VS launch —
        // this package autoloads in NoSolution and all solution contexts.

        // Load the General options page so its persisted settings populate the
        // thread-safe GeneralOptionsPage.Snapshot. Background-thread consumers
        // (the LSP client's InitializationOptions and BasicLangExeLocator) read
        // the snapshot instead of calling GetDialogPage, which requires the UI thread.
        try
        {
            // LoadSettingsFromStorage is idempotent; calling it explicitly guarantees
            // the snapshot reflects persisted settings even if the page constructor
            // already loaded them.
            GeneralOptions?.LoadSettingsFromStorage();
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            ActivityLog.LogError(nameof(BasicLangPackage), $"Failed to load BasicLang general options: {ex}");
        }

        // Register project factory
        try
        {
            RegisterProjectFactory(new ProjectSystem.BasicLangProjectFactory(this));
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            ActivityLog.LogError(nameof(BasicLangPackage), $"Failed to register BasicLang project factory: {ex}");
        }

        // Initialize commands
        try
        {
            await Commands.CommandHandlers.InitializeAsync(this);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            ActivityLog.LogError(nameof(BasicLangPackage), $"Failed to initialize BasicLang commands: {ex}");
        }

        // Log successful initialization
        System.Diagnostics.Debug.WriteLine("BasicLang Visual Studio package initialized successfully");
    }

    /// <summary>
    /// Gets a service from the package.
    /// </summary>
    public async Task<T?> GetServiceAsync<T>() where T : class
    {
        return await GetServiceAsync(typeof(T)) as T;
    }

    /// <summary>
    /// Gets the general options.
    /// </summary>
    public Options.GeneralOptionsPage? GeneralOptions
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetDialogPage(typeof(Options.GeneralOptionsPage)) as Options.GeneralOptionsPage;
        }
    }

    /// <summary>
    /// Gets the compiler options.
    /// </summary>
    public Options.CompilerOptionsPage? CompilerOptions
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetDialogPage(typeof(Options.CompilerOptionsPage)) as Options.CompilerOptionsPage;
        }
    }
}
