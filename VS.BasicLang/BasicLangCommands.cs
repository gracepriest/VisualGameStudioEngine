using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace VS.BasicLang;

/// <summary>
/// Commands for BasicLang extension
/// </summary>
public static class BasicLangCommands
{
    public const int BuildCommandId = 0x0100;
    public const int RunCommandId = 0x0101;
    public const int RestartServerCommandId = 0x0102;

    public static readonly Guid CommandSet = new("95a8f3e1-1234-4567-8902-abcdef123456");

    private static AsyncPackage? _package;

    public static async Task InitializeAsync(AsyncPackage package)
    {
        _package = package;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService == null) return;

        // Build command
        var buildCommandID = new CommandID(CommandSet, BuildCommandId);
        var buildMenuItem = new MenuCommand(ExecuteBuild, buildCommandID);
        commandService.AddCommand(buildMenuItem);

        // Run command
        var runCommandID = new CommandID(CommandSet, RunCommandId);
        var runMenuItem = new MenuCommand(ExecuteRun, runCommandID);
        commandService.AddCommand(runMenuItem);

        // Restart server command
        var restartCommandID = new CommandID(CommandSet, RestartServerCommandId);
        var restartMenuItem = new MenuCommand(ExecuteRestartServer, restartCommandID);
        commandService.AddCommand(restartMenuItem);
    }

    private static void ExecuteBuild(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        if (dte == null) return;

        var activeDocument = dte.ActiveDocument;
        if (activeDocument == null) return;

        var projectPath = activeDocument.FullName;

        // Show output window
        var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow != null)
        {
            var guidGeneral = Microsoft.VisualStudio.VSConstants.OutputWindowPaneGuid.GeneralPane_guid;
            outputWindow.CreatePane(ref guidGeneral, "BasicLang", 1, 1);
            outputWindow.GetPane(ref guidGeneral, out var pane);
            pane?.OutputString($"Building: {projectPath}\n");
            pane?.Activate();
        }

        // Execute build (would connect to BasicLang compiler)
        // For now, just show a message
        VsShellUtilities.ShowMessageBox(
            _package!,
            $"Build requested for: {projectPath}",
            "BasicLang Build",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static void ExecuteRun(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        if (dte == null) return;

        var activeDocument = dte.ActiveDocument;
        if (activeDocument == null) return;

        var projectPath = activeDocument.FullName;

        VsShellUtilities.ShowMessageBox(
            _package!,
            $"Run requested for: {projectPath}",
            "BasicLang Run",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static void ExecuteRestartServer(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        VsShellUtilities.ShowMessageBox(
            _package!,
            "Language server restart requested. Please close and reopen your BasicLang files.",
            "BasicLang",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
