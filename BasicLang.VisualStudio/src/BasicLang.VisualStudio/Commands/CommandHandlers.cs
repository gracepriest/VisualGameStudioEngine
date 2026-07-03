using System.ComponentModel.Design;
using System.Text.RegularExpressions;
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
    /// Matches compiler error diagnostics in build output, e.g. "Error: ...",
    /// "error CS1002: ...", "Error at line 5: ...", "Compilation failed with 3 error(s)".
    /// Needed because BasicLang.exe may exit 0 even when compilation fails.
    /// </summary>
    private static readonly Regex ErrorDiagnosticPattern = new(
        @"(^|[\s:(\[])error\s*([A-Za-z]*\d+)?\s*:|(^|\s)error\s+at\s+line\s|\bcompilation\s+failed\b|\bbuild\s+failed\b|\brestore\s+failed\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

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

            command.Visible = isBasicLangFile;
            command.Enabled = isBasicLangFile;
        }
    }

    /// <summary>
    /// Reports an unexpected failure of an async void command handler. Must never throw:
    /// an unhandled exception in an async void handler crashes devenv.exe.
    /// </summary>
    private static async Task ReportHandlerErrorAsync(string commandName, Exception ex)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ActivityLog.TryLogError(nameof(CommandHandlers), $"BasicLang '{commandName}' command failed: {ex}");
            ShowMessage($"BasicLang {commandName}", $"The command failed: {ex.Message}");
        }
        catch
        {
            // Never let error reporting itself take down VS.
        }
    }

    /// <summary>
    /// Finds the .blproj for the given start directory, walking up parent directories
    /// until one is found. Stops at the repository/solution root (a directory containing
    /// a .sln or .git entry) or the drive root.
    /// </summary>
    private static string? FindProjectFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            var projectFiles = dir.GetFiles("*.blproj");
            if (projectFiles.Length > 0)
                return projectFiles[0].FullName;

            // Don't search above the solution/repo root
            bool isRoot = dir.GetFiles("*.sln").Length > 0 ||
                          Directory.Exists(Path.Combine(dir.FullName, ".git"));
            if (isRoot)
                break;

            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Reads the &lt;TargetFramework&gt; element from a .blproj file, if present.
    /// </summary>
    private static string? ReadProjectTargetFramework(string blprojPath)
    {
        try
        {
            var content = File.ReadAllText(blprojPath);
            var match = Regex.Match(content, @"<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a compiler options backend to the compiler's --target argument value.
    /// </summary>
    private static string GetBackendTargetName(Options.CompilerBackend backend) => backend switch
    {
        Options.CompilerBackend.MSIL => "msil",
        Options.CompilerBackend.LLVM => "llvm",
        Options.CompilerBackend.CPlusPlus => "cpp",
        _ => "csharp",
    };

    /// <summary>
    /// Returns true if the compiler output contains error diagnostics. BasicLang.exe
    /// historically exits 0 even on compile errors, so the exit code alone is not enough.
    /// </summary>
    private static bool ContainsErrorDiagnostics(string output)
    {
        return !string.IsNullOrEmpty(output) && ErrorDiagnosticPattern.IsMatch(output);
    }

    /// <summary>
    /// Finds the built executable for a project. The compiler outputs to
    /// bin\&lt;Configuration&gt;\&lt;TargetFramework&gt;, so probe the target framework
    /// subdirectory (read from the .blproj), then bin\&lt;Configuration&gt; itself,
    /// then any other subdirectories.
    /// </summary>
    private static string? FindBuiltExecutable(string projectDir, string configuration, string? targetFramework)
    {
        var configDir = Path.Combine(projectDir, "bin", configuration);
        if (!Directory.Exists(configDir))
            return null;

        var searchDirs = new List<string>();
        if (!string.IsNullOrEmpty(targetFramework))
            searchDirs.Add(Path.Combine(configDir, targetFramework));
        searchDirs.Add(configDir);
        searchDirs.AddRange(Directory.GetDirectories(configDir));

        foreach (var dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir))
                continue;

            var exeFiles = Directory.GetFiles(dir, "*.exe");
            if (exeFiles.Length > 0)
                return exeFiles[0];
        }
        return null;
    }

    /// <summary>
    /// Executes the Build command.
    /// </summary>
    private static async void ExecuteBuildAsync(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var activeDocument = dte?.ActiveDocument;

            if (activeDocument == null)
            {
                ShowMessage("No active document", "Please open a BasicLang file first.");
                return;
            }

            var startDir = Path.GetDirectoryName(activeDocument.FullName)!;
            var blprojPath = FindProjectFile(startDir);
            if (blprojPath == null)
            {
                ShowMessage("Build", "No .blproj file found in the current directory or any parent directory.");
                return;
            }

            await BuildProjectAsync(blprojPath);
        }
        catch (Exception ex)
        {
            await ReportHandlerErrorAsync("Build", ex);
        }
    }

    /// <summary>
    /// Runs the BasicLang compiler on the given .blproj and returns true if the build
    /// succeeded. Success requires exit code 0 AND no error diagnostics in the output
    /// (BasicLang.exe may exit 0 even when compilation fails).
    /// </summary>
    private static async Task<bool> BuildProjectAsync(string blprojPath)
    {
        // Read compiler options on the main thread before any await
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var compilerOptions = BasicLangPackage.Instance?.CompilerOptions;
        var backendTarget = GetBackendTargetName(compilerOptions?.DefaultBackend ?? Options.CompilerBackend.CSharp);
        var additionalArgs = compilerOptions?.AdditionalCompilerArgs?.Trim() ?? "";

        var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
        var guidPane = Microsoft.VisualStudio.VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;
        IVsOutputWindowPane? pane = null;
        if (outputWindow != null)
        {
            outputWindow.CreatePane(ref guidPane, "Build", 1, 1);
            outputWindow.GetPane(ref guidPane, out pane);
        }
        pane?.Activate();

        var projectDir = Path.GetDirectoryName(blprojPath)!;
        pane?.OutputStringThreadSafe($"Building: {blprojPath}\n");

        var basicLangPath = FindBasicLangCompiler();
        if (string.IsNullOrEmpty(basicLangPath))
        {
            pane?.OutputStringThreadSafe("Error: BasicLang compiler not found.\n");
            return false;
        }

        pane?.OutputStringThreadSafe($"Using compiler: {basicLangPath}\n");

        var arguments = $"build \"{blprojPath}\" --target={backendTarget}";
        if (!string.IsNullOrEmpty(additionalArgs))
            arguments += $" {additionalArgs}";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = basicLangPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = projectDir
        };

        var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            pane?.OutputStringThreadSafe("Error: Failed to start compiler process.\n");
            return false;
        }

        // Drain both pipes concurrently to avoid a deadlock if one fills while
        // the other is being read sequentially
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outTask, errTask);
        await Task.Run(() => process.WaitForExit());
        var output = outTask.Result;
        var errors = errTask.Result;

        if (!string.IsNullOrEmpty(output))
            pane?.OutputStringThreadSafe(output);
        if (!string.IsNullOrEmpty(errors))
            pane?.OutputStringThreadSafe($"Errors:\n{errors}\n");

        // The exit code alone isn't reliable — also scan the output for error diagnostics
        bool succeeded = process.ExitCode == 0 &&
                         !ContainsErrorDiagnostics(output) &&
                         !ContainsErrorDiagnostics(errors);

        pane?.OutputStringThreadSafe($"\nBuild {(succeeded ? "succeeded" : "FAILED")} (exit code {process.ExitCode})\n");
        return succeeded;
    }

    /// <summary>
    /// Executes the Run command.
    /// </summary>
    private static async void ExecuteRunAsync(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var activeDocument = dte?.ActiveDocument;

            if (activeDocument == null)
            {
                ShowMessage("No active document", "Please open a BasicLang file first.");
                return;
            }

            var startDir = Path.GetDirectoryName(activeDocument.FullName)!;
            var blprojPath = FindProjectFile(startDir);
            if (blprojPath == null)
            {
                ShowMessage("Run", "No .blproj file found in the current directory or any parent directory.");
                return;
            }

            // Build first and wait for it to finish before launching
            bool buildSucceeded = await BuildProjectAsync(blprojPath);
            if (!buildSucceeded)
            {
                ShowMessage("Run Failed", "Build failed. See the Build output window for details.");
                return;
            }

            // The compiler outputs to bin\Debug\{TargetFramework}, so probe subdirectories too
            var projectDir = Path.GetDirectoryName(blprojPath)!;
            var targetFramework = ReadProjectTargetFramework(blprojPath);
            var exePath = FindBuiltExecutable(projectDir, "Debug", targetFramework);

            if (exePath != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowMessage("Run Failed", "No executable found under bin\\Debug after build.");
            }
        }
        catch (Exception ex)
        {
            await ReportHandlerErrorAsync("Run", ex);
        }
    }

    /// <summary>
    /// Executes the Change Backend command.
    /// </summary>
    private static async void ExecuteChangeBackendAsync(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var currentBackend = (BasicLangPackage.Instance?.CompilerOptions?.DefaultBackend
                ?? Options.CompilerBackend.CSharp).ToString();

            // For now, just show a message - would show a proper dialog
            ShowMessage("Change Backend",
                $"Available backends:\n" +
                $"- CSharp (C# transpilation, recommended)\n" +
                $"- MSIL (Direct IL generation)\n" +
                $"- LLVM (Native code via LLVM)\n" +
                $"- CPlusPlus (C++ transpilation)\n\n" +
                $"Current: {currentBackend}\n\n" +
                $"Change it in Tools > Options > BasicLang > Compiler, or edit the <Backend> element in your .blproj file.");
        }
        catch (Exception ex)
        {
            await ReportHandlerErrorAsync("Change Backend", ex);
        }
    }

    /// <summary>
    /// Executes the Restart Language Server command.
    /// </summary>
    private static async void ExecuteRestartServerAsync(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // The language client is a MEF part exported as ILanguageClient, so it must be
            // resolved through the component model's extensions, not GetService
            var componentModel = Package.GetGlobalService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel))
                as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;

            if (componentModel == null)
            {
                ShowMessage("Language Server", "Component model service is unavailable.");
                return;
            }

            var languageClient = componentModel
                .GetExtensions<Microsoft.VisualStudio.LanguageServer.Client.ILanguageClient>()
                .OfType<BasicLangLanguageClient>()
                .FirstOrDefault();

            if (languageClient != null)
            {
                await languageClient.RestartServerAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowMessage("Language Server", "BasicLang language server restarted.");
            }
            else
            {
                ShowMessage("Language Server", "Language server not found. It will start automatically when you open a BasicLang file.");
            }
        }
        catch (Exception ex)
        {
            await ReportHandlerErrorAsync("Restart Server", ex);
        }
    }

    /// <summary>
    /// Executes the Go to Definition command.
    /// </summary>
    private static async void ExecuteGoToDefinitionAsync(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // This would be handled by the LSP client
            // For now, just trigger the standard VS Go to Definition
            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            dte?.ExecuteCommand("Edit.GoToDefinition");
        }
        catch (Exception ex)
        {
            await ReportHandlerErrorAsync("Go To Definition", ex);
        }
    }

    /// <summary>
    /// Executes the Find All References command.
    /// </summary>
    private static async void ExecuteFindReferencesAsync(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // This would be handled by the LSP client
            // For now, just trigger the standard VS Find All References
            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            dte?.ExecuteCommand("Edit.FindAllReferences");
        }
        catch (Exception ex)
        {
            await ReportHandlerErrorAsync("Find References", ex);
        }
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
        return BasicLangExeLocator.FindBasicLangExe();
    }
}
