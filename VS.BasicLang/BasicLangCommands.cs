using System.ComponentModel.Design;
using System.Diagnostics;
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
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte?.ActiveDocument == null)
            {
                ShowMessage("No active document", "Please open a BasicLang file first.");
                return;
            }

            var projectDir = Path.GetDirectoryName(dte.ActiveDocument.FullName)!;
            await BuildProjectAsync(projectDir);
        });
    }

    private static void ExecuteRun(object sender, EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte?.ActiveDocument == null)
            {
                ShowMessage("No active document", "Please open a BasicLang file first.");
                return;
            }

            var projectDir = Path.GetDirectoryName(dte.ActiveDocument.FullName)!;

            bool succeeded = await BuildProjectAsync(projectDir);
            if (!succeeded)
            {
                ShowMessage("Run Failed", "Build failed. See the Build output window for details.");
                return;
            }

            var outputDir = Path.Combine(projectDir, "bin", "Debug");
            var exeFiles = Directory.Exists(outputDir)
                ? Directory.GetFiles(outputDir, "*.exe")
                : Array.Empty<string>();

            if (exeFiles.Length > 0)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exeFiles[0],
                    WorkingDirectory = outputDir,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowMessage("Run Failed", "No executable found in bin\\Debug after build.");
            }
        });
    }

    /// <summary>
    /// Invokes the BasicLang compiler on the project in <paramref name="projectDir"/>.
    /// Returns true if the build succeeded (exit code 0).
    /// </summary>
    private static async Task<bool> BuildProjectAsync(string projectDir)
    {
        var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
        var guidPane = Microsoft.VisualStudio.VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;
        IVsOutputWindowPane? pane = null;
        if (outputWindow != null)
        {
            outputWindow.CreatePane(ref guidPane, "Build", 1, 1);
            outputWindow.GetPane(ref guidPane, out pane);
        }
        pane?.Activate();

        var projectFiles = Directory.GetFiles(projectDir, "*.blproj");
        if (projectFiles.Length == 0)
        {
            pane?.OutputStringThreadSafe("No .blproj file found in the current directory.\n");
            return false;
        }

        var blprojPath = projectFiles[0];
        pane?.OutputStringThreadSafe($"Building: {blprojPath}\n");

        var compilerPath = FindBasicLangCompiler();
        if (string.IsNullOrEmpty(compilerPath))
        {
            pane?.OutputStringThreadSafe("Error: BasicLang compiler not found.\n");
            return false;
        }

        pane?.OutputStringThreadSafe($"Using compiler: {compilerPath}\n");

        var startInfo = new ProcessStartInfo
        {
            FileName = compilerPath,
            Arguments = $"build \"{blprojPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = projectDir
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            pane?.OutputStringThreadSafe("Error: Failed to start compiler process.\n");
            return false;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var errors = await process.StandardError.ReadToEndAsync();
        await Task.Run(() => process.WaitForExit());

        if (!string.IsNullOrEmpty(output))
            pane?.OutputStringThreadSafe(output);
        if (!string.IsNullOrEmpty(errors))
            pane?.OutputStringThreadSafe($"Errors:\n{errors}\n");

        pane?.OutputStringThreadSafe($"\nBuild {(process.ExitCode == 0 ? "succeeded" : "FAILED")} (exit code {process.ExitCode})\n");
        return process.ExitCode == 0;
    }

    private static void ExecuteRestartServer(object sender, EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var componentModel = Package.GetGlobalService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel))
                as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;

            if (componentModel == null)
            {
                ShowMessage("Error", "Could not access MEF component model.");
                return;
            }

            try
            {
                var languageService = componentModel.GetService<BasicLangLanguageService>();
                if (languageService != null)
                {
                    await languageService.RestartServerAsync();
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
        });
    }

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

    private static string? FindBasicLangCompiler()
    {
        var possiblePaths = new[]
        {
            Path.Combine(Path.GetDirectoryName(typeof(BasicLangCommands).Assembly.Location) ?? "", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BasicLang", "BasicLang.exe"),
        };

        foreach (var path in possiblePaths)
        {
            try { if (File.Exists(path)) return path; }
            catch { }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var full = Path.Combine(dir, "BasicLang.exe");
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }

        return null;
    }
}
