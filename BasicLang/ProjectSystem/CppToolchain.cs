using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Discovery and invocation of a native C++ toolchain so the Cpp backend
    /// can produce a runnable executable instead of stopping at source.
    ///
    /// Probe order: clang++ on PATH, g++ on PATH, then MSVC located via
    /// vswhere + vcvars64.bat (the same chain the backend's E2E test uses).
    /// Shared by the CLI (`BasicLang.exe build`) and the IDE's BuildService so
    /// the two entry points cannot drift.
    /// </summary>
    public sealed class CppToolchain
    {
        private const int CompileTimeoutMs = 240000;

        public string DisplayName { get; }

        private readonly string _executable;   // clang++ / g++ / cmd.exe (MSVC)
        private readonly string _vcvarsPath;   // set for MSVC only

        private CppToolchain(string displayName, string executable, string vcvarsPath = null)
        {
            DisplayName = displayName;
            _executable = executable;
            _vcvarsPath = vcvarsPath;
        }

        /// <summary>Probe for an available toolchain; null when none is installed.</summary>
        public static CppToolchain Find()
        {
            foreach (var exe in new[] { "clang++", "g++" })
            {
                try
                {
                    using var probe = Process.Start(new ProcessStartInfo(exe, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    probe.WaitForExit(10000);
                    if (probe.ExitCode == 0)
                        return new CppToolchain(exe, exe);
                }
                catch
                {
                    // not on PATH
                }
            }

            try
            {
                var vswhere = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (File.Exists(vswhere))
                {
                    using var p = Process.Start(new ProcessStartInfo(vswhere,
                        "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    var installPath = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(15000);
                    if (!string.IsNullOrEmpty(installPath))
                    {
                        var vcvars = Path.Combine(installPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
                        if (File.Exists(vcvars))
                            return new CppToolchain("MSVC (cl.exe)", "cmd.exe", vcvars);
                    }
                }
            }
            catch
            {
                // vswhere probe failed
            }

            return null;
        }

        /// <summary>
        /// Compile a generated .cpp to an executable. When
        /// <paramref name="engineLibPath"/> is set (a path to
        /// VisualGameStudioEngine.lib) it is linked in so Framework_* imports
        /// resolve. Returns success plus the combined compiler output.
        /// </summary>
        public (bool Success, string Output) CompileToExecutable(
            string cppFilePath, string exePath, string engineLibPath = null, string workingDirectory = null)
        {
            string arguments;
            if (_vcvarsPath != null)
            {
                // MSVC: vcvars64 sets up INCLUDE/LIB/PATH, then cl compiles and
                // links in one step. /EHsc for exceptions, C++20 for coroutines.
                var lib = engineLibPath != null ? $" \"{engineLibPath}\"" : string.Empty;
                arguments = "/s /c \"\"" + _vcvarsPath + "\" >nul && cl /nologo /std:c++20 /EHsc \"" +
                            cppFilePath + "\"" + lib + " /Fe:\"" + exePath + "\"\"";
            }
            else
            {
                // clang++/g++ accept an MSVC import library as a linker input file.
                var lib = engineLibPath != null ? $" \"{engineLibPath}\"" : string.Empty;
                arguments = $"-std=c++20 \"{cppFilePath}\"{lib} -o \"{exePath}\"";
            }

            var psi = new ProcessStartInfo(_executable, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            try
            {
                using var proc = Process.Start(psi);
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(CompileTimeoutMs))
                {
                    try { proc.Kill(); } catch { /* already gone */ }
                    return (false, $"C++ compiler timed out after {CompileTimeoutMs / 1000}s.");
                }

                var output = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(stdout)) output.AppendLine(stdout.Trim());
                if (!string.IsNullOrWhiteSpace(stderr)) output.AppendLine(stderr.Trim());
                return (proc.ExitCode == 0 && File.Exists(exePath), output.ToString().Trim());
            }
            catch (Exception ex)
            {
                return (false, $"Failed to invoke {DisplayName}: {ex.Message}");
            }
        }
    }
}
