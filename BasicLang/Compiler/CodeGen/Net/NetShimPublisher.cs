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
        /// The publish RID. Named rather than left as a bare default so §10.2's cache key and the
        /// publish command cannot disagree about what was built (a key that omitted the real RID
        /// would hit across a cross-compile).
        /// </summary>
        internal const string DefaultRuntimeIdentifier = "win-x64";

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
        /// Looks up "PATH" via <see cref="IDictionary{TKey,TValue}.TryGetValue"/> rather than the
        /// indexer so a dictionary without a PATH entry is a no-op instead of a
        /// <see cref="KeyNotFoundException"/> — <c>ProcessStartInfo.Environment</c> is
        /// case-insensitive on Windows and always has one, but callers/tests may not.
        /// </summary>
        internal static void HardenChildPath(IDictionary<string, string?> env, string vsInstallerDir, bool installerExists)
        {
            if (!installerExists) return;
            if (env.TryGetValue("PATH", out var existing))
                env["PATH"] = $"{existing};{vsInstallerDir}";
        }

        /// <summary>
        /// The expected native DLL path for a shim published from <paramref name="csprojPath"/>
        /// into <paramref name="outputDir"/> — <c>dotnet publish -p:NativeLib=Shared</c> names the
        /// output after the project. Pure so <see cref="BuildResult"/> and callers can share it
        /// without re-deriving the naming rule.
        /// </summary>
        internal static string ExpectedDllPath(string csprojPath, string outputDir) =>
            Path.Combine(outputDir, Path.GetFileNameWithoutExtension(csprojPath) + ".dll");

        /// <summary>
        /// Turns a completed `dotnet publish` invocation's raw outcome into a
        /// <see cref="NetShimPublishResult"/>. Pure (no I/O) so the result-shape logic —
        /// including the two distinct ways a publish can fail — is unit-testable without
        /// spawning a process. <paramref name="dllExists"/> is the caller's
        /// <c>File.Exists(ExpectedDllPath(...))</c> probe, passed in rather than done here.
        /// </summary>
        internal static NetShimPublishResult BuildResult(
            string csprojPath, string outputDir, int exitCode, string output, bool dllExists)
        {
            var dllPath = ExpectedDllPath(csprojPath, outputDir);
            var success = exitCode == 0 && dllExists;
            return new NetShimPublishResult(success, dllPath, output, exitCode);
        }

        /// <summary>
        /// Publishes <paramref name="csprojPath"/> as a Native AOT shared native library into
        /// <paramref name="outputDir"/> for <paramref name="runtimeIdentifier"/>, running with
        /// <paramref name="workingDirectory"/> as the child process's working directory.
        /// <paramref name="csprojPath"/> and <paramref name="outputDir"/> may be relative; they
        /// are resolved against <paramref name="workingDirectory"/> (not this process's own
        /// current directory) so that the host-side <c>File.Exists</c>/<c>Directory.CreateDirectory</c>
        /// probes agree with how the <c>dotnet</c> child — whose cwd IS
        /// <paramref name="workingDirectory"/> — resolves the same arguments.
        /// <para/>
        /// Returns a result (<see cref="NetShimPublishResult.Success"/> = false) rather than
        /// throwing for a non-zero exit or a missing output DLL, and rather than throwing when
        /// <c>dotnet</c> itself cannot be started (not on PATH — a supported failure mode per
        /// spec §10.5, reported as <see cref="NetShimPublishResult.ExitCode"/> <c>-1</c>, which is
        /// this method's own sentinel and never an SDK exit code).
        /// <para/>
        /// <b>What it CAN throw — the list callers must catch, and it is longer than the two
        /// deliberate ones.</b> Deliberate: <see cref="FileNotFoundException"/> when
        /// <paramref name="csprojPath"/> does not exist, and <see cref="TimeoutException"/> when
        /// the publish exceeds 10 minutes (a hung publish is not a diagnosable build error).
        /// <b>Incidental, from the path and directory work done BEFORE the child is spawned:</b>
        /// <see cref="Path.GetFullPath(string,string)"/> on either path can throw
        /// <see cref="ArgumentException"/>, <see cref="NotSupportedException"/> or
        /// <see cref="PathTooLongException"/>, and <see cref="Directory.CreateDirectory(string)"/>
        /// on <paramref name="outputDir"/> can throw <see cref="UnauthorizedAccessException"/>,
        /// <see cref="IOException"/> or <see cref="DirectoryNotFoundException"/> — a read-only
        /// <c>obj/</c>, a path over MAX_PATH, a permission-denied cache directory. A caller that
        /// catches only the two deliberate ones lets those escape as an unhandled exception out of
        /// the compiler, which is precisely the shape the <c>proc.Start()</c> catch below exists to
        /// avoid. <c>CppProjectBuilder.GenerateAndPublishShim</c> catches the whole set.
        /// </summary>
        internal static NetShimPublishResult Publish(
            string csprojPath, string outputDir, string workingDirectory,
            string runtimeIdentifier = DefaultRuntimeIdentifier)
        {
            csprojPath = Path.GetFullPath(csprojPath, workingDirectory);
            outputDir = Path.GetFullPath(outputDir, workingDirectory);

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

            // PATH workaround — see HardenChildPath above (spec §10.5) for the full explanation.
            var vsInstaller = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer");
            HardenChildPath(psi.Environment, vsInstaller, Directory.Exists(vsInstaller));

            var output = new StringBuilder();
            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                // dotnet not on PATH, or otherwise unlaunchable — a supported failure (spec §10.5
                // requires the SDK on PATH), not an exceptional one; surface it the same way a
                // failed publish would be, not as an unhandled exception out of the compiler.
                return new NetShimPublishResult(false, ExpectedDllPath(csprojPath, outputDir), $"Failed to start dotnet: {ex.Message}", ExitCode: -1);
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // First publish downloads ILCompiler packages then runs the native linker — allow 10 minutes.
            if (!proc.WaitForExit(600_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                try { proc.WaitForExit(5_000); } catch { /* best effort */ }
                throw new TimeoutException($"dotnet publish of {Path.GetFileName(csprojPath)} timed out after 10 minutes.\n{Snapshot(output)}");
            }
            // Bounded, not unbounded: this waits for stdout/stderr EOF (not just process exit),
            // which can be delayed by a grandchild still holding the inherited pipe write handle.
            // An unbounded wait here would quietly disarm the 600s hang detector above.
            proc.WaitForExit(30_000);

            var text = Snapshot(output);
            return BuildResult(csprojPath, outputDir, proc.ExitCode, text, File.Exists(ExpectedDllPath(csprojPath, outputDir)));
        }

        private static string Snapshot(StringBuilder output) { lock (output) return output.ToString(); }
    }
}
