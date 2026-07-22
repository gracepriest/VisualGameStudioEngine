using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.ProjectSystem.Serialization;
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
        // FACTORY registration (not by-type): BuildService's ProjectSerializer ctor arg is
        // not itself container-registered, so a by-type AddSingleton<IBuildService,
        // BuildService>() would let MS DI silently fall back to the 1-arg convenience ctor,
        // leaving the per-backend C++ toolchain overrides reader null and the whole feature
        // a no-op that unit tests (which construct BuildService directly) would never catch.
        services.AddSingleton<IBuildService>(sp => new BuildService(
            sp.GetRequiredService<IOutputService>(),
            new ProjectSerializer(),
            sp.GetRequiredService<CppToolchainOverrides>()));
        // The IDE's language servers: one LanguageService per LanguageServerDescriptor, routed to
        // by file extension. Registered as the singleton that OWNS them — disposing the container
        // on shutdown disposes the registry, which disposes each service, which kills its server
        // process. A registry that built services lazily outside the container would never be
        // disposed and would orphan a server child process on every exit.
        //
        // Every consumer routes through ILanguageServiceRegistry (Task 7): there is deliberately NO
        // `ILanguageService` singleton to inject directly. A second registration would spawn a
        // second `dotnet --lsp` child process that nothing routes to, starts, stops or disposes —
        // orphaned for the life of the IDE, with nothing failing to say so.
        //
        // The roster is machine-dependent BY DESIGN: BasicLang ships with the IDE and is always
        // registered; clangd does not, so it is registered only when one was actually found
        // (cpp.clangd.path override, else PATH). Registering a clangd that does not exist would
        // spawn nothing and leave the registry claiming to own .cpp — every C++ IntelliSense
        // request would route to a server that can never answer, which is strictly worse than
        // routing to nothing. GetFor(".cpp") returning null IS the degraded mode, and callers
        // already handle it (Task 7); the user is told via the status bar.
        services.AddSingleton<ILanguageServiceRegistry>(sp =>
        {
            var outputService = sp.GetRequiredService<IOutputService>();
            var settingsService = sp.GetRequiredService<ISettingsService>();

            var languageServices = new List<ILanguageService>
            {
                new LanguageService(outputService, settingsService)
            };

            // Resolved ONCE, here, at container build: the descriptor is pure identity and must
            // hold an already-resolved path (D1). A clangd installed mid-session is picked up on
            // the next IDE start — acquiring it live is Phase 3b's job.
            var clangdPath = ClangdLocator.Locate(settingsService);
            if (clangdPath != null)
            {
                languageServices.Add(new LanguageService(
                    outputService, settingsService, LanguageServerDescriptor.Clangd(clangdPath)));
            }

            return new LanguageServiceRegistry(languageServices);
        });
        // The clangd acquisition pipeline (download → verify → stage → swap into ~/.vgs/tools).
        // A factory-created singleton so the CONTAINER disposes it on shutdown — it owns a
        // FileDownloader (HttpClient) — and so DI never has to guess at its all-optional test-seam
        // ctor parameters. Consumed by MainWindowViewModel, which wraps it in a ClangdDownloadFlow
        // built over its own toast/progress methods (the flow itself is deliberately NOT in DI:
        // its sinks are VM methods, and a service→VM dependency would be a cycle).
        services.AddSingleton(sp => new ClangdInstaller());
        // The lldb-dap acquisition pipeline — the clangd installer's structural sibling (Task 12),
        // registered for the same reasons: the container disposes its FileDownloader on shutdown,
        // and DI never guesses at the all-optional test-seam ctor. Consumed by MainWindowViewModel
        // via an LldbDapDownloadFlow over its own toast/progress methods. Its release pins are
        // PLACEHOLDERS until the self-hosted zip ships (runbook, Task 13) — the flow's
        // IsReleasePinned gate keeps the installer from ever fetching the placeholder URL.
        services.AddSingleton(sp => new LldbDapInstaller());
        // Gives clangd its obj/gen headers + obj/compile_commands.json on project open, before any
        // build has produced them (Task 10). Singleton because its whole job is to coalesce and
        // serialize a multi-second, non-incremental emission across requests — per-instance state
        // that a transient registration would throw away, letting two emissions race into obj/gen.
        // Constructed explicitly rather than by type: the class also exposes a public emitter-seam
        // constructor for tests, and naming the production one here keeps DI from having to choose.
        // Threaded through CppToolchainOverrides (Task 7) so a project pinned to a possibly
        // off-PATH compiler override gets a compile_commands.json that names it, matching what
        // BuildService's own override-aware resolver would compile with.
        services.AddSingleton<IIntelliSenseEmissionService>(sp =>
            new IntelliSenseEmissionService(
                sp.GetRequiredService<IOutputService>(),
                sp.GetRequiredService<CppToolchainOverrides>()));
        // Saving a .bas/.mod/.cls file under the current project re-runs that emission after a
        // 1.5s trailing-edge debounce (Task 10, Phase 3b), so clangd tracks edits between builds;
        // the coordinator also watches the open project's .blproj through IFileWatcherService
        // (Task 11), so an EXTERNAL project-file edit wakes the same debounced regen path.
        // Registered by concrete type — NOTHING injects it, so App.OnFrameworkInitializationCompleted
        // resolves it eagerly (beside GitAutoFetchService): a lazily-resolved singleton nobody
        // injects is never constructed and therefore never subscribes to FileSavedEvent. Disposed
        // with the container. The factory names the production ctor: the class also exposes a
        // public quiet-period seam ctor for tests, and naming this one keeps DI from choosing.
        services.AddSingleton(sp => new RegenOnSaveCoordinator(
            sp.GetRequiredService<IIntelliSenseEmissionService>(),
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<IBuildService>(),
            sp.GetRequiredService<IEventAggregator>(),
            sp.GetRequiredService<IFileWatcherService>()));
        // The debug-adapter roster. Deliberately NOT the LSP registry's shape: this
        // registry owns no processes (DAP sessions are per-launch, owned by the debug
        // service), so there is no dispose lifecycle and no install-gated registration.
        services.AddSingleton<IDebugAdapterRegistry>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var registry = new DebugAdapterRegistry();
            // Built-ins register through the SAME public door an extension would use (spec §2.3).
            // Launch commands resolve at SESSION START (descriptor contract) — an lldb-dap
            // installed mid-session is found on the next F5, no IDE restart (contrast clangd, D1).
            registry.Register(DebugAdapterDescriptor.BasicLangManaged(DebugService.ResolveCompilerPath));
            registry.Register(DebugAdapterDescriptor.LldbDap(() => LldbDapLocator.Locate(settingsService)));
            return registry;
        });
        services.AddSingleton<IDebugService>(sp => new DebugService(
            sp.GetRequiredService<IOutputService>(),
            sp.GetRequiredService<IDebugAdapterRegistry>()));
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
        services.AddSingleton<ICppToolchainProbe, CppToolchainProbeService>();
        services.AddSingleton<CppToolchainOverrides>();
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
