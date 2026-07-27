using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Publishes the BlnetTestShim project with Native AOT (NativeLib=Shared) exactly once per
/// test run and hands out the resulting native DLL path. Multiple fixtures share the single
/// publish via <see cref="PublishOnce"/>; the captured publish output is exposed for
/// warning-scan assertions.
/// </summary>
public static class BlnetShimPublisher
{
    private static readonly Lazy<(string DllPath, string Output)> Published =
        new(PublishCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Publishes on first call (cached for the rest of the run); returns the native DLL path.</summary>
    public static string PublishOnce() => Published.Value.DllPath;

    /// <summary>Combined stdout+stderr of the publish. Forces the publish if it has not run yet.</summary>
    public static string PublishOutput => Published.Value.Output;

    private static (string DllPath, string Output) PublishCore()
    {
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, "VisualGameStudio.Tests", "TestAssets", "BlnetTestShim", "BlnetTestShim.csproj");
        if (!File.Exists(csproj))
            throw new FileNotFoundException($"Shim project not found: {csproj}");

        // Per-run scratch dir: the Lazy dedupes within this test host; the pid keeps
        // concurrent test runs from clobbering each other's publish output.
        var scratch = Path.Combine(Path.GetTempPath(), $"blnet_shim_{Environment.ProcessId}");
        Directory.CreateDirectory(scratch);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add("win-x64");
        psi.ArgumentList.Add("-p:PublishAot=true");
        psi.ArgumentList.Add("-p:NativeLib=Shared");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(scratch);

        // The ILCompiler targets locate MSVC via findvcvarsall.bat -> VS's VsDevCmd.bat, which
        // pushd's into the VS Installer directory and invokes a bare `vswhere.exe`, relying on
        // cmd resolving executables from the current directory. Under a shell that sets
        // NoDefaultCurrentDirectoryInExePath (hardened environments do), that probe fails and
        // writes "'vswhere.exe' is not recognized" to stderr — which Exec's ConsoleToMSBuild
        // captures and the targets Split('#') into the linker path, corrupting CppLinker.
        // Appending the Installer dir to the child PATH lets the probe resolve via PATH instead.
        var vsInstaller = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer");
        if (Directory.Exists(vsInstaller))
            psi.Environment["PATH"] = $"{psi.Environment["PATH"]};{vsInstaller}";

        var output = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // First publish downloads ILCompiler packages then runs the native linker — allow 10 minutes.
        if (!proc.WaitForExit(600_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException($"dotnet publish of BlnetTestShim timed out after 10 minutes.\n{Snapshot(output)}");
        }
        proc.WaitForExit(); // drain async output handlers

        var text = Snapshot(output);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"dotnet publish of BlnetTestShim failed (exit {proc.ExitCode}).\n{text}");

        var dll = Path.Combine(scratch, "BlnetTestShim.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException($"Publish succeeded but the native library is missing: {dll}\n{text}");
        return (dll, text);
    }

    private static string Snapshot(StringBuilder output) { lock (output) return output.ToString(); }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "VisualGameStudioEngine.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException(
            $"VisualGameStudioEngine.sln not found in any parent of {TestContext.CurrentContext.TestDirectory}");
    }
}
