using System.Diagnostics;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Shared helper for the native C++ runtime tests: probes for a real C++ compiler and
/// compiles-and-runs a self-contained C++ program, capturing stdout.
///
/// This mirrors <c>CppBackendTests.FindCppCompiler</c> but uses executable-producing flags
/// (rather than syntax-only) so tests can assert on runtime behavior, not just that the code
/// compiles.
/// </summary>
public static class CppCompile
{
    /// <summary>Captured result of running a child process to completion (or timeout).</summary>
    private readonly record struct ProcResult(bool Exited, int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// Run <paramref name="proc"/> to completion, capturing both streams without deadlocking.
    ///
    /// stderr is drained on a background task while we block reading stdout, so a child that
    /// fills one pipe buffer (~4 KB, easy for a verbose compiler error dump) while we read the
    /// other can never wedge both sides. If the process does not exit within
    /// <paramref name="timeoutMs"/> it is killed (whole tree) and <see cref="ProcResult.Exited"/>
    /// is <c>false</c>.
    /// </summary>
    private static ProcResult RunToCompletion(Process proc, int timeoutMs)
    {
        var errTask = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEnd();
        bool exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            // Give the async stderr read a bounded chance to unblock after the kill.
            try { errTask.Wait(2000); } catch { /* ignore */ }
            var partialErr = errTask.IsCompletedSuccessfully ? errTask.Result : string.Empty;
            return new ProcResult(false, -1, stdout, partialErr);
        }
        var stderr = errTask.GetAwaiter().GetResult();
        return new ProcResult(true, proc.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Probe for a C++ compiler capable of building an executable: clang++/g++ on PATH,
    /// then MSVC via vswhere + vcvars64.bat. Returns (executable, args template) where the
    /// args template has <c>{0}</c> = source path and <c>{1}</c> = output exe path, or
    /// <c>null</c> when no compiler is found.
    /// </summary>
    public static (string exe, string argsTemplate)? FindRunCompiler()
    {
        foreach (var (exe, args) in new[]
        {
            ("clang++", "-std=c++20 \"{0}\" -o \"{1}\""),
            ("g++", "-std=c++20 \"{0}\" -o \"{1}\"")
        })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(exe, "--version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                var r = RunToCompletion(probe!, 10000);
                if (r.Exited && r.ExitCode == 0) return (exe, args);
            }
            catch { /* not on PATH */ }
        }

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(vswhere,
                    "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                var r = RunToCompletion(p!, 15000);
                var installPath = r.StdOut.Trim();
                if (r.Exited && !string.IsNullOrEmpty(installPath))
                {
                    var vcvars = Path.Combine(installPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(vcvars))
                        return ("cmd.exe",
                            "/s /c \"\"" + vcvars + "\" >nul && cl /nologo /std:c++20 /EHsc /Fe:\"{1}\" \"{0}\"\"");
                }
            }
            catch { /* vswhere probe failed */ }
        }

        return null;
    }

    /// <summary>
    /// Write <paramref name="cppSource"/> to a temp <c>.cpp</c>, compile it to a temp exe
    /// with the given compiler (asserting exit code 0, with compiler stdout+stderr in the
    /// failure message), run the exe (asserting exit code 0), and return its captured stdout.
    /// A compile or run that exceeds its timeout is killed and reported as a clear timeout
    /// failure. Temp files are cleaned up in a <c>finally</c>.
    /// </summary>
    public static string CompileAndRun(string cppSource, (string exe, string argsTemplate) compiler) =>
        CompileAndRun(cppSource, compiler, extraFiles: null);

    /// <summary>
    /// Same as <see cref="CompileAndRun(string,ValueTuple{string,string})"/>, but first writes
    /// each (fileName -> content) in <paramref name="extraFiles"/> into the temp compile dir so
    /// the generated C++ can <c>#include "header.h"</c> a sibling header (the compiler runs with
    /// that dir as its working directory, so quote-includes resolve against it).
    /// </summary>
    public static string CompileAndRun(
        string cppSource,
        (string exe, string argsTemplate) compiler,
        IEnumerable<KeyValuePair<string, string>>? extraFiles)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "blcpp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var srcPath = Path.Combine(tmpDir, "prog.cpp");
        var exePath = Path.Combine(tmpDir, "prog" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        try
        {
            if (extraFiles != null)
                foreach (var kv in extraFiles)
                    File.WriteAllText(Path.Combine(tmpDir, kv.Key), kv.Value);

            File.WriteAllText(srcPath, cppSource);

            return CompileAndRunCore(tmpDir, srcPath, exePath, compiler,
                                     $"--- source ---\n{cppSource}");
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Multi-file variant of <see cref="CompileAndRun(string,ValueTuple{string,string})"/> for
    /// split emission: writes every (fileName -> content) in <paramref name="files"/> into a
    /// fresh temp dir, compiles the named <paramref name="translationUnits"/> together on ONE
    /// command line into a single executable, runs it (asserting exit code 0), and returns its
    /// captured stdout.
    ///
    /// The TU names are joined with <c>" "</c> before substitution, which relies on the args
    /// template quoting <c>{0}</c> (both the clang/g++ and MSVC templates from
    /// <see cref="FindRunCompiler"/> do) so the expansion becomes <c>"a.cpp" "b.cpp"</c>. Bare
    /// file names resolve because the compiler runs with the temp dir as working directory —
    /// which also makes quote-includes among the generated files work.
    /// </summary>
    public static string CompileAndRunFiles(
        IReadOnlyDictionary<string, string> files,
        IEnumerable<string> translationUnits,
        (string exe, string argsTemplate) compiler)
    {
        var tuList = translationUnits.ToList();
        Assert.That(tuList, Is.Not.Empty, "no translation units to compile");
        foreach (var tu in tuList)
            Assert.That(files.ContainsKey(tu), Is.True, $"translation unit '{tu}' has no file content");

        var tmpDir = Path.Combine(Path.GetTempPath(), "bl-splitc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var exePath = Path.Combine(tmpDir, "prog" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        try
        {
            foreach (var kv in files)
                File.WriteAllText(Path.Combine(tmpDir, kv.Key), kv.Value);

            // Failure context: every non-runtime file (the runtime header is big and stable;
            // errors there would still name it by file:line in the compiler output).
            var context = string.Join("\n", files
                .Where(kv => !kv.Key.Equals("BasicLangRuntime.g.h", StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"--- {kv.Key} ---\n{kv.Value}"));

            return CompileAndRunCore(tmpDir, string.Join("\" \"", tuList), exePath, compiler, context);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Shared compile+run tail: formats the compiler args template with
    /// (<paramref name="sourcesForTemplate"/>, <paramref name="exePath"/>), compiles in
    /// <paramref name="tmpDir"/> (asserting exit 0 with compiler output plus
    /// <paramref name="failureContext"/> in the message), runs the produced exe (asserting
    /// exit 0), and returns its stdout.
    /// </summary>
    private static string CompileAndRunCore(
        string tmpDir,
        string sourcesForTemplate,
        string exePath,
        (string exe, string argsTemplate) compiler,
        string failureContext)
    {
        // Compile.
        var (compilerExe, argsTemplate) = compiler;
        var compileArgs = string.Format(argsTemplate, sourcesForTemplate, exePath);
        var compilePsi = new ProcessStartInfo(compilerExe, compileArgs)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tmpDir
        };
        using (var cproc = Process.Start(compilePsi))
        {
            var c = RunToCompletion(cproc!, 120000);
            Assert.That(c.Exited, Is.True,
                $"C++ compiler timed out after 120000 ms:\n{c.StdOut}\n{c.StdErr}\n{failureContext}");
            Assert.That(c.ExitCode, Is.EqualTo(0),
                $"C++ compilation failed (exit {c.ExitCode}):\n{c.StdOut}\n{c.StdErr}\n{failureContext}");
        }

        Assert.That(File.Exists(exePath), Is.True, $"compiler produced no executable at {exePath}");

        // Run.
        var runPsi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tmpDir
        };
        using (var rproc = Process.Start(runPsi))
        {
            var r = RunToCompletion(rproc!, 30000);
            Assert.That(r.Exited, Is.True,
                $"compiled program timed out after 30000 ms:\n{r.StdOut}\n{r.StdErr}");
            Assert.That(r.ExitCode, Is.EqualTo(0),
                $"compiled program exited with {r.ExitCode}:\n{r.StdOut}\n{r.StdErr}");
            return r.StdOut;
        }
    }
}
