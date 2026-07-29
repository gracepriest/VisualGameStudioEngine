using System.Diagnostics;
using System.Text;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// Result of a shim publish attempt. On failure (non-zero exit, or the expected DLL missing)
    /// this carries <see cref="Success"/> = false with the diagnostic output rather than
    /// throwing, so callers such as AotDiagnosticMapper can map the failure to a build error
    /// instead of catching an exception.
    /// </summary>
    internal sealed record NetShimPublishResult(bool Success, string DllPath, string Output, int ExitCode);

    /// <summary>
    /// Publishes a shim project with Native AOT (NativeLib=Shared) via `dotnet publish` and
    /// returns the resulting native DLL path. This is the product implementation of the recipe
    /// proven in P0 (spec §8.1) and hardened against the VS-Installer PATH probe failure
    /// (spec §10.5).
    /// </summary>
    internal static class NetShimPublisher
    {
        /// <summary>
        /// Builds the `dotnet` argument list for publishing <paramref name="csprojPath"/> as a
        /// Native AOT shared native library into <paramref name="outputDir"/> for
        /// <paramref name="runtimeIdentifier"/>. This is the P0-proven recipe (spec §8.1) —
        /// changing it changes what ships; update the spec too if this is intentional.
        /// </summary>
        internal static string[] BuildPublishArguments(string csprojPath, string outputDir, string runtimeIdentifier) =>
            new[]
            {
                "publish", csprojPath,
                "-c", "Release",
                "-r", runtimeIdentifier,
                "-p:PublishAot=true",
                "-p:NativeLib=Shared",
                "-o", outputDir,
            };

        /// <summary>
        /// The ILCompiler targets locate MSVC via findvcvarsall.bat -> VS's VsDevCmd.bat, which
        /// pushd's into the VS Installer directory and invokes a bare `vswhere.exe`, relying on
        /// cmd resolving executables from the current directory. Under a shell that sets
        /// NoDefaultCurrentDirectoryInExePath (hardened environments do), that probe fails and
        /// writes "'vswhere.exe' is not recognized" to stderr — which Exec's ConsoleToMSBuild
        /// captures and the targets Split('#') into the linker path, corrupting CppLinker.
        /// Appending the Installer dir to the child PATH lets the probe resolve via PATH instead.
        /// </summary>
        internal static void HardenChildPath(IDictionary<string, string?> env, string vsInstallerDir, bool installerExists)
        {
            if (installerExists)
                env["PATH"] = $"{env["PATH"]};{vsInstallerDir}";
        }

        /// <summary>
        /// Publishes <paramref name="csprojPath"/> as a Native AOT shared native library into
        /// <paramref name="outputDir"/> for <paramref name="runtimeIdentifier"/>, running with
        /// <paramref name="workingDirectory"/> as the child process's working directory. Returns
        /// a result rather than throwing for a non-zero exit or a missing output DLL; a timeout
        /// still throws <see cref="TimeoutException"/> since a hung publish is not a diagnosable
        /// build error.
        /// </summary>
        internal static NetShimPublishResult Publish(
            string csprojPath, string outputDir, string workingDirectory, string runtimeIdentifier = "win-x64")
        {
            if (!File.Exists(csprojPath))
                throw new FileNotFoundException($"Shim project not found: {csprojPath}");

            Directory.CreateDirectory(outputDir);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in BuildPublishArguments(csprojPath, outputDir, runtimeIdentifier))
                psi.ArgumentList.Add(arg);

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
            HardenChildPath(psi.Environment, vsInstaller, Directory.Exists(vsInstaller));

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
                throw new TimeoutException($"dotnet publish of {Path.GetFileName(csprojPath)} timed out after 10 minutes.\n{Snapshot(output)}");
            }
            proc.WaitForExit(); // drain async output handlers

            var text = Snapshot(output);
            if (proc.ExitCode != 0)
                return new NetShimPublishResult(false, DllPath: string.Empty, text, proc.ExitCode);

            var dllName = Path.GetFileNameWithoutExtension(csprojPath) + ".dll";
            var dll = Path.Combine(outputDir, dllName);
            if (!File.Exists(dll))
                return new NetShimPublishResult(false, DllPath: string.Empty, text, proc.ExitCode);

            return new NetShimPublishResult(true, dll, text, proc.ExitCode);
        }

        private static string Snapshot(StringBuilder output) { lock (output) return output.ToString(); }
    }
}
