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
                probe!.WaitForExit(10000);
                if (probe.ExitCode == 0) return (exe, args);
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
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                var installPath = p!.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(15000);
                if (!string.IsNullOrEmpty(installPath))
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
    /// Temp files are cleaned up in a <c>finally</c>.
    /// </summary>
    public static string CompileAndRun(string cppSource, (string exe, string argsTemplate) compiler)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "blcpp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var srcPath = Path.Combine(tmpDir, "prog.cpp");
        var exePath = Path.Combine(tmpDir, "prog" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        try
        {
            File.WriteAllText(srcPath, cppSource);

            // Compile.
            var (compilerExe, argsTemplate) = compiler;
            var compileArgs = string.Format(argsTemplate, srcPath, exePath);
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
                var cstdout = cproc!.StandardOutput.ReadToEnd();
                var cstderr = cproc.StandardError.ReadToEnd();
                cproc.WaitForExit(120000);
                Assert.That(cproc.ExitCode, Is.EqualTo(0),
                    $"C++ compilation failed (exit {cproc.ExitCode}):\n{cstdout}\n{cstderr}\n--- source ---\n{cppSource}");
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
                var rstdout = rproc!.StandardOutput.ReadToEnd();
                var rstderr = rproc.StandardError.ReadToEnd();
                rproc.WaitForExit(30000);
                Assert.That(rproc.ExitCode, Is.EqualTo(0),
                    $"compiled program exited with {rproc.ExitCode}:\n{rstdout}\n{rstderr}");
                return rstdout;
            }
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
