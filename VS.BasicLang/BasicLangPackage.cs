using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace VS.BasicLang;

/// <summary>
/// BasicLang Visual Studio Package
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideLanguageService(typeof(BasicLangLanguageService), BasicLangConstants.LanguageName, 0,
    CodeSense = true,
    RequestStockColors = false,
    EnableCommenting = true,
    EnableFormatSelection = true,
    EnableLineNumbers = true,
    DefaultToInsertSpaces = true,
    ShowCompletion = true,
    ShowSmartIndent = true,
    ShowDropDownOptions = true)]
[ProvideLanguageExtension(typeof(BasicLangLanguageService), ".bl")]
[ProvideLanguageExtension(typeof(BasicLangLanguageService), ".bas")]
[ProvideLanguageExtension(typeof(BasicLangLanguageService), ".blproj")]
public sealed class BasicLangPackage : AsyncPackage
{
    public const string PackageGuidString = "95a8f3e1-1234-4567-8901-abcdef123456";

    private BasicLangLanguageService? _languageService;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        // Switch to main thread for UI operations
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Initialize the language service
        _languageService = new BasicLangLanguageService();

        // Register commands
        await BasicLangCommands.InitializeAsync(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _languageService?.Dispose();
        }
        base.Dispose(disposing);
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
