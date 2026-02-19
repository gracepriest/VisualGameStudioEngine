using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VS.BasicLang;

/// <summary>
/// BasicLang Visual Studio Package
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class BasicLangPackage : AsyncPackage
{
    public const string PackageGuidString = "95a8f3e1-1234-4567-8901-abcdef123456";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        // Switch to main thread for UI operations
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Register commands
        await BasicLangCommands.InitializeAsync(this);
    }
}

/// <summary>
/// Constants for BasicLang
/// </summary>
public static class BasicLangConstants
{
    public const string LanguageName = "BasicLang";
    public const string ContentTypeName = "basiclang";
    public const string FileExtension = ".bl";
    public const string FileExtension2 = ".bas";
    public const string ProjectExtension = ".blproj";
}
