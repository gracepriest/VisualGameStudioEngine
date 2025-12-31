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
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IOutputService, OutputService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IBuildService, BuildService>();
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddSingleton<IDebugService, DebugService>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<IBookmarkService, BookmarkService>();
        services.AddSingleton<IRefactoringService, RefactoringService>();
        services.AddSingleton<ISnippetService, SnippetService>();
        services.AddSingleton<RecentProjectsService>();

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

        // ViewModels (Transient for documents and dialogs)
        services.AddTransient<CodeEditorDocumentViewModel>();
        services.AddTransient<QuickOpenViewModel>();
        services.AddTransient<DiffViewerViewModel>();
        services.AddTransient<RenameDialogViewModel>();
        services.AddTransient<PeekDefinitionViewModel>();

        return services;
    }
}
