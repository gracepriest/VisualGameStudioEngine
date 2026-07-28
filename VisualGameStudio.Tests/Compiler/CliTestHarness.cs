using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Shared harness for spawned-process end-to-end tests: locates the real
/// BasicLang.exe deployed next to the tests and drives it (or any produced
/// executable) as a child process. The spawn/kill-tree/timeout/output-drain
/// policy lives here — <see cref="CppProjectCliBuildTests"/> and
/// <see cref="MixedProjectBuildTests"/> drive the CLI through
/// <see cref="RunCli"/>, and <see cref="NativeBclFrontEndTests"/> uses
/// <see cref="RunProcess"/> directly. New spawn code should call
/// <see cref="RunProcess"/> instead of hand-rolling a Process (a few older
/// fixtures still carry small pre-existing local run helpers).
/// </summary>
internal static class CliTestHarness
{
    public static string CliPath()
    {
        var cliPath = Path.Combine(AppContext.BaseDirectory, "BasicLang.exe");
        Assert.That(File.Exists(cliPath), Is.True,
            "BasicLang.exe not deployed next to the tests — project reference output changed?");
        return cliPath;
    }

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunCli(
        string workingDir, params string[] args)
        => Task.Run(() => RunProcess(CliPath(), args, workingDir, timeoutMs: 300_000));

    /// <summary>
    /// Spawns a process with redirected output, a hard timeout, and kill-tree on
    /// hang (BasicLang.exe spawns dotnet build / the C++ toolchain — a timed-out
    /// compile must not leak child processes). Output reads are drained with a
    /// bound after exit: a child that inherited the pipe handles must not hang
    /// the drain forever. Timeout failures include whatever partial output was
    /// captured.
    /// </summary>
    /// <param name="environment">
    /// Extra environment variables for the child (added on top of the inherited
    /// block). Used by <see cref="CompileRunCSharp"/>'s culture forcing — see the
    /// header of <c>BclBackendParityTests</c>.
    /// </param>
    public static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, string[] args, string workingDir, int timeoutMs,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        if (environment != null)
            foreach (var kv in environment) process.StartInfo.Environment[kv.Key] = kv.Value;
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            // Kill the whole tree — otherwise a timed-out compile leaks
            // cl.exe/clang++/dotnet children.
            try { process.Kill(entireProcessTree: true); } catch { }
            BoundedDrain(stdoutTask, stderrTask);
            Assert.Fail($"process timed out after {timeoutMs / 1000}s: {fileName} {string.Join(" ", args)}\n" +
                        $"Partial STDOUT:\n{DrainedOrNote(stdoutTask)}\nPartial STDERR:\n{DrainedOrNote(stderrTask)}");
        }

        // The process exited, but a grandchild that inherited the pipe handles
        // can keep ReadToEndAsync alive indefinitely — bound the drain.
        BoundedDrain(stdoutTask, stderrTask);
        return (process.ExitCode, DrainedOrNote(stdoutTask), DrainedOrNote(stderrTask));
    }

    /// <summary>
    /// The C#-backend end-to-end leg: compiles <paramref name="src"/> through the real
    /// CLI (<c>BasicLang.exe build App.blproj</c> with
    /// <c>&lt;TargetBackend&gt;CSharp&lt;/TargetBackend&gt;</c> — the same binary the suite
    /// deploys next to the tests, so it always carries the compiler changes under test),
    /// runs the produced executable, and returns its stdout with line endings normalized
    /// to '\n' (NOT trimmed — callers that want the old trimmed shape trim themselves).
    /// Callers must be tagged [Category("Integration")].
    ///
    /// Extracted from <c>NativeBclFrontEndTests</c> (which still wraps it) so the
    /// cross-backend parity oracle can share the exact same leg rather than reinventing
    /// it; <paramref name="runEnvironment"/> is the only addition, and it applies to the
    /// RUN only — never to the build.
    /// </summary>
    public static string CompileRunCSharp(
        string src, IReadOnlyDictionary<string, string>? runEnvironment = null)
    {
        if (!DotnetOnPath())
        {
            Assert.Ignore("dotnet SDK not found on PATH — the CLI's C# backend cannot build the generated project.");
        }

        var rootDir = Path.Combine(Path.GetTempPath(), "bl-nativebcl-e2e-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(rootDir, "App");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "Main.bas"), src);
            File.WriteAllText(Path.Combine(projectDir, "App.blproj"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>App</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
  </ItemGroup>
</BasicLangProject>
");

            // Build via the CLI. 120s: a first dotnet build in a fresh temp dir
            // includes restore, which can exceed 60s on a cold cache.
            var (buildExit, buildOut, buildErr) = RunProcess(
                CliPath(),
                new[] { "build", Path.Combine(projectDir, "App.blproj") },
                projectDir,
                timeoutMs: 120_000);
            Assert.That(buildExit, Is.EqualTo(0),
                $"CLI C# build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

            var exes = Directory.GetFiles(projectDir, "App.exe", SearchOption.AllDirectories);
            Assert.That(exes, Is.Not.Empty,
                $"CLI build claimed success but produced no App.exe.\nSTDOUT:\n{buildOut}");

            var (runExit, runOut, runErr) = RunProcess(
                exes[0], Array.Empty<string>(), Path.GetDirectoryName(exes[0])!, timeoutMs: 60_000,
                environment: runEnvironment);
            Assert.That(runExit, Is.EqualTo(0),
                $"compiled program exited nonzero ({runExit}).\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");

            return runOut.Replace("\r\n", "\n");
        }
        finally
        {
            try { Directory.Delete(rootDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    public static bool DotnetOnPath()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Any(p => !string.IsNullOrWhiteSpace(p) &&
            (File.Exists(Path.Combine(p.Trim(), "dotnet.exe")) || File.Exists(Path.Combine(p.Trim(), "dotnet"))));
    }

    private static void BoundedDrain(params Task[] reads)
    {
        // WaitAll rethrows a faulted read as AggregateException — swallow it
        // here; DrainedOrNote reports any non-completed read as a note instead.
        try { Task.WaitAll(reads, 10_000); } catch (AggregateException) { }
    }

    private static string DrainedOrNote(Task<string> read)
        => read.Status == TaskStatus.RanToCompletion
            ? read.Result
            : "<output not fully captured: pipe still open (likely inherited by a surviving child process) or the read faulted>";
}
