using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using BasicLang.VisualStudio.LanguageService;
using Task = System.Threading.Tasks.Task;

namespace BasicLang.VisualStudio.Commands;

/// <summary>
/// Command identifiers.
/// </summary>
public static class CommandIds
{
    public const int Build = 0x0100;
    public const int Run = 0x0101;
    public const int ChangeBackend = 0x0102;
    public const int RestartServer = 0x0103;
    public const int GoToDefinition = 0x0104;
    public const int FindReferences = 0x0105;
}

/// <summary>
/// Command handlers for BasicLang menu commands.
/// </summary>
public static class CommandHandlers
{
    private static AsyncPackage? _package;
    private static OleMenuCommandService? _commandService;

    /// <summary>
    /// Initializes the command handlers.
    /// </summary>
    public static async Task InitializeAsync(AsyncPackage package)
    {
        _package = package;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        _commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (_commandService == null)
        {
            System.Diagnostics.Debug.WriteLine("Failed to get menu command service");
            return;
        }

        // Register commands
        RegisterCommand(CommandIds.Build, ExecuteBuildAsync);
        RegisterCommand(CommandIds.Run, ExecuteRunAsync);
        RegisterCommand(CommandIds.ChangeBackend, ExecuteChangeBackendAsync);
        RegisterCommand(CommandIds.RestartServer, ExecuteRestartServerAsync);
        RegisterCommand(CommandIds.GoToDefinition, ExecuteGoToDefinitionAsync);
        RegisterCommand(CommandIds.FindReferences, ExecuteFindReferencesAsync);

        System.Diagnostics.Debug.WriteLine("BasicLang commands registered");
    }

    /// <summary>
    /// Registers a command with the given handler.
    /// </summary>
    private static void RegisterCommand(int commandId, EventHandler handler)
    {
        var commandID = new CommandID(Guids.CommandSet, commandId);
        var menuCommand = new OleMenuCommand(handler, commandID);
        menuCommand.BeforeQueryStatus += OnBeforeQueryStatus;
        _commandService!.AddCommand(menuCommand);
    }

    /// <summary>
    /// Handles BeforeQueryStatus to enable/disable commands based on context.
    /// </summary>
    private static void OnBeforeQueryStatus(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (sender is OleMenuCommand command)
        {
            // Enable commands when a BasicLang file is active
            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var activeDoc = dte?.ActiveDocument;

            bool isBasicLangFile = activeDoc != null &&
                (activeDoc.Name.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                 activeDoc.Name.EndsWith(".bl", StringComparison.OrdinalIgnoreCase) ||
                 activeDoc.Name.EndsWith(".blproj", StringComparison.OrdinalIgnoreCase));

            command.Visible = true;
            command.Enabled = isBasicLangFile;
        }
    }

    /// <summary>
    /// Executes the Build command.
    /// </summary>
    private static async void ExecuteBuildAsync(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        var activeDocument = dte?.ActiveDocument;

        if (activeDocument == null)
        {
            ShowMessage("No active document", "Please open a BasicLang file first.");
            return;
        }

        var projectPath = activeDocument.FullName;

        // Get output window
        var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow != null)
        {
            var guidPane = Microsoft.VisualStudio.VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;
            outputWindow.CreatePane(ref guidPane, "Build", 1, 1);
            outputWindow.GetPane(ref guidPane, out var pane);
            pane?.Activate();
            pane?.OutputStringThreadSafe($"Building BasicLang project: {projectPath}\n");

            // Find project file
            var projectDir = Path.GetDirectoryName(projectPath);
            var projectFiles = Directory.GetFiles(projectDir!, "*.blproj");

            if (projectFiles.Length > 0)
            {
                var blprojPath = projectFiles[0];
                pane?.OutputStringThreadSafe($"Found project file: {blprojPath}\n");

                // Execute build
                var basicLangPath = FindBasicLangCompiler();
                if (!string.IsNullOrEmpty(basicLangPath))
                {
                    pane?.OutputStringThreadSafe($"Using compiler: {basicLangPath}\n");

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = basicLangPath,
                        Arguments = $"build \"{blprojPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = projectDir
                    };

                    var process = System.Diagnostics.Process.Start(startInfo);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        var errors = await process.StandardError.ReadToEndAsync();
                        process.WaitForExit();

                        pane?.OutputStringThreadSafe(output);
                        if (!string.IsNullOrEmpty(errors))
                        {
                            pane?.OutputStringThreadSafe($"Errors:\n{errors}\n");
                        }

                        pane?.OutputStringThreadSafe($"\nBuild completed with exit code: {process.ExitCode}\n");
                    }
                }
                else
                {
                    pane?.OutputStringThreadSafe("Error: BasicLang compiler not found.\n");
                }
            }
            else
            {
                pane?.OutputStringThreadSafe("No .blproj file found in the current directory.\n");
            }
        }
    }

    /// <summary>
    /// Executes the Run command.
    /// </summary>
    private static async void ExecuteRunAsync(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        var activeDocument = dte?.ActiveDocument;

        if (activeDocument == null)
        {
            ShowMessage("No active document", "Please open a BasicLang file first.");
            return;
        }

        // Build first, then run
        ExecuteBuildAsync(sender, e);

        // After build, try to run
        var projectDir = Path.GetDirectoryName(activeDocument.FullName);
        var outputDir = Path.Combine(projectDir!, "bin", "Debug");

        var exeFiles = Directory.Exists(outputDir)
            ? Directory.GetFiles(outputDir, "*.exe")
            : Array.Empty<string>();

        if (exeFiles.Length > 0)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exeFiles[0],
                WorkingDirectory = outputDir,
                UseShellExecute = true
            });
        }
        else
        {
            ShowMessage("Run Failed", "No executable found. Please build the project first.");
        }
    }

    /// <summary>
    /// Executes the Change Backend command.
    /// </summary>
    private static async void ExecuteChangeBackendAsync(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // Show backend selection dialog
        var backends = new[] { "CSharp", "MSIL", "LLVM", "CPlusPlus" };
        var currentBackend = "CSharp"; // Would read from project

        // For now, just show a message - would show a proper dialog
        ShowMessage("Change Backend",
            $"Available backends:\n" +
            $"- CSharp (C# transpilation, recommended)\n" +
            $"- MSIL (Direct IL generation)\n" +
            $"- LLVM (Native code via LLVM)\n" +
            $"- CPlusPlus (C++ transpilation)\n\n" +
            $"Current: {currentBackend}\n\n" +
            $"Edit the <Backend> element in your .blproj file to change.");
    }

    /// <summary>
    /// Executes the Restart Language Server command.
    /// </summary>
    private static async void ExecuteRestartServerAsync(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // Find the language client and restart it
        var componentModel = Package.GetGlobalService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel))
            as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;

        if (componentModel != null)
        {
            try
            {
                var languageClient = componentModel.GetService<BasicLangLanguageClient>();
                if (languageClient != null)
                {
                    await languageClient.RestartServerAsync();
                    ShowMessage("Language Server", "BasicLang language server restarted.");
                }
                else
                {
                    ShowMessage("Language Server", "Language server not found. It will start automatically when you open a BasicLang file.");
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Error", $"Failed to restart language server: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Executes the Go to Definition command.
    /// </summary>
    private static async void ExecuteGoToDefinitionAsync(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // This would be handled by the LSP client
        // For now, just trigger the standard VS Go to Definition
        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        dte?.ExecuteCommand("Edit.GoToDefinition");
    }

    /// <summary>
    /// Executes the Find All References command.
    /// </summary>
    private static async void ExecuteFindReferencesAsync(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // This would be handled by the LSP client
        // For now, just trigger the standard VS Find All References
        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        dte?.ExecuteCommand("Edit.FindAllReferences");
    }

    /// <summary>
    /// Shows a message box.
    /// </summary>
    private static void ShowMessage(string title, string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        VsShellUtilities.ShowMessageBox(
            _package!,
            message,
            title,
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    /// <summary>
    /// Finds the BasicLang compiler.
    /// </summary>
    private static string? FindBasicLangCompiler()
    {
        var possiblePaths = new[]
        {
            Path.Combine(Path.GetDirectoryName(typeof(CommandHandlers).Assembly.Location) ?? "", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BasicLang", "BasicLang.exe"),
            "BasicLang.exe"
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                if (File.Exists(path))
                    return path;
            }
            catch { }
        }

        return null;
    }
}
