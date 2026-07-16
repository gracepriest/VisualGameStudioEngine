using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Dock;
using VisualGameStudio.Shell.Services;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Panels;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Shell.Configuration;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // Core Services (Singletons)
        services.AddSingleton<IEventAggregator, EventAggregator>();

        // Project System Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IOutputService, OutputService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ISolutionService, SolutionService>();
        services.AddSingleton<IBuildService, BuildService>();
        // The IDE's language servers: one LanguageService per LanguageServerDescriptor, routed to
        // by file extension. Registered as the singleton that OWNS them — disposing the container
        // on shutdown disposes the registry, which disposes each service, which kills its server
        // process. A registry that built services lazily outside the container would never be
        // disposed and would orphan a server child process on every exit.
        //
        // BasicLang only, for now. clangd needs a resolved executable path (ClangdLocator, Task 11)
        // before a descriptor for it can exist; Task 12 adds it here.
        services.AddSingleton<ILanguageServiceRegistry>(sp => new LanguageServiceRegistry(new[]
        {
            new LanguageService(
                sp.GetRequiredService<IOutputService>(),
                sp.GetRequiredService<ISettingsService>())
        }));
        // ⚠ TEMPORARY SHIM — Task 7 removes this registration.
        // The ~26 call sites that still inject ILanguageService assume one server and gate on
        // `IsBasicLangSourceFile`; Task 7 converts them to route through the registry. Until then
        // they must keep working, and they must reach the SAME instance the registry holds:
        // registering LanguageService a second time here would spawn a second `dotnet --lsp` child
        // process that nothing ever routes to, starts or stops — orphaned for the life of the IDE,
        // with nothing failing to say so. LanguageServiceRegistryTests pins this resolves to one
        // object.
        services.AddSingleton<ILanguageService>(sp =>
            sp.GetRequiredService<ILanguageServiceRegistry>().GetFor("x.bas")
            ?? throw new InvalidOperationException(
                "No BasicLang language server is registered — every existing ILanguageService " +
                "call site would silently do nothing."));
        services.AddSingleton<IDebugService, DebugService>();
        services.AddSingleton<ILaunchConfigurationService, LaunchConfigurationService>();
        services.AddSingleton<IGitService, GitService>();
        // Background periodic `git fetch` (git.autoFetch / git.autoFetchInterval). Resolved eagerly
        // at startup (App.OnFrameworkInitializationCompleted) so its timer arms; disposed with the
        // container on shutdown.
        services.AddSingleton<GitAutoFetchService>();
        services.AddSingleton<IBookmarkService, BookmarkService>();
        services.AddSingleton<IRefactoringService, RefactoringService>();
        services.AddSingleton<ISnippetService, SnippetService>();
        services.AddSingleton<IProjectTemplateService, ProjectTemplateService>();
        services.AddSingleton<IAutoSaveService, AutoSaveService>();
        services.AddSingleton<IHotExitService, HotExitService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddSingleton<IRecentProjectsService, RecentProjectsService>();
        services.AddSingleton<IWorkspaceStateStore, WorkspaceStateStore>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ITaskRunnerService, TaskRunnerService>();
        services.AddSingleton<ITextMateService, TextMateService>();
        services.AddSingleton<IExtensionService>(sp =>
            new ExtensionService(
                sp.GetRequiredService<IOutputService>(),
                sp.GetRequiredService<ITextMateService>(),
                sp.GetRequiredService<ISnippetService>()));
        services.AddSingleton<FileSearchService>();

        // Shell Services
        services.AddSingleton<IDialogService, DialogService>();

        // Dock Factory
        services.AddSingleton<DockFactory>();

        // ViewModels (Singletons for panels)
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SolutionExplorerViewModel>();
        services.AddSingleton<OutputPanelViewModel>();
        services.AddSingleton<ErrorListViewModel>();
        services.AddSingleton<CallStackViewModel>();
        services.AddSingleton<VariablesViewModel>();
        services.AddSingleton<BreakpointsViewModel>();
        services.AddSingleton<FindInFilesViewModel>();
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<WelcomeDocumentViewModel>();
        services.AddSingleton<GitChangesViewModel>();
        services.AddSingleton<GitBranchesViewModel>();
        services.AddSingleton<GitStashViewModel>();
        services.AddSingleton<GitBlameViewModel>();
        services.AddSingleton<WatchViewModel>();
        services.AddSingleton<ImmediateWindowViewModel>();
        services.AddSingleton<DocumentOutlineViewModel>();
        services.AddSingleton<BookmarksViewModel>();
        services.AddSingleton<CallHierarchyViewModel>();
        services.AddSingleton<TypeHierarchyViewModel>();
        services.AddSingleton<ThreadsViewModel>();
        services.AddSingleton<TimelineViewModel>();

        // ViewModels (Transient for documents and dialogs)
        services.AddTransient<CodeEditorDocumentViewModel>();
        services.AddTransient<QuickOpenViewModel>();
        services.AddTransient<DiffViewerViewModel>();
        services.AddTransient<RenameDialogViewModel>();
        services.AddTransient<PeekDefinitionViewModel>();

        return services;
    }
}
