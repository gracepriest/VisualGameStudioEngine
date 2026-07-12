using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace BasicLang.Compiler.ProjectSystem
{
    public enum CppToolchainKind { ClangLike, Msvc }

    /// <summary>Inputs for a multi-TU native compile (exe or static library).</summary>
    public sealed class CppCompileRequest
    {
        public List<string> SourceFiles { get; } = new List<string>();
        public string OutputPath { get; set; }               // .exe, or .lib/.a for libraries
        public bool LinkExecutable { get; set; } = true;     // false = static library
        public List<string> IncludeDirs { get; } = new List<string>();
        public List<string> Defines { get; } = new List<string>();
        public List<string> Libraries { get; } = new List<string>();
        public string CppStandard { get; set; } = "c++20";
        public string WorkingDirectory { get; set; }
        public bool DebugSymbols { get; set; }
        public bool Optimize { get; set; }
    }

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

        public CppToolchainKind Kind => _vcvarsPath != null ? CppToolchainKind.Msvc : CppToolchainKind.ClangLike;

        /// <summary>Compiler driver name for compile_commands.json ("clang++", "g++", "cl").</summary>
        public string DriverName => _vcvarsPath != null ? "cl" : _executable;

        /// <summary>
        /// Ordered compile-flag tokens for one invocation. This is the single
        /// source of truth for flag spelling: <see cref="BuildCompileCommandArguments"/>
        /// (compile_commands.json argv) and <see cref="Compile"/> (real shell
        /// command lines) both consume it verbatim, so the two cannot drift.
        /// </summary>
        private static List<string> FlagsFor(CppToolchainKind kind, CppCompileRequest request)
        {
            var flags = new List<string>();
            if (kind == CppToolchainKind.Msvc)
            {
                flags.Add("/nologo");
                flags.Add("/std:" + request.CppStandard);
                flags.Add("/EHsc");
                flags.Add(request.Optimize ? "/O2" : "/Od");
                if (request.DebugSymbols) flags.Add("/Zi");
                foreach (var inc in request.IncludeDirs) flags.Add("/I" + inc);
                foreach (var def in request.Defines) flags.Add("/D" + def);
            }
            else
            {
                flags.Add("-std=" + request.CppStandard);
                flags.Add(request.Optimize ? "-O2" : "-O0");
                if (request.DebugSymbols) flags.Add("-g");
                foreach (var inc in request.IncludeDirs) flags.Add("-I" + inc);
                foreach (var def in request.Defines) flags.Add("-D" + def);
            }
            return flags;
        }

        /// <summary>Quote a whole flag token for a shell command line when it
        /// contains a space — e.g. an include dir with spaces becomes the single
        /// quoted token "/IC:\path with spaces".</summary>
        private static string QuoteToken(string token)
            => token.Contains(' ') ? "\"" + token + "\"" : token;

        /// <summary>Shell-string form of <see cref="FlagsFor"/> used by <see cref="Compile"/>.</summary>
        private static string JoinFlags(CppToolchainKind kind, CppCompileRequest request)
            => string.Join(" ", FlagsFor(kind, request).Select(QuoteToken));

        /// <summary>
        /// Per-TU compile command (argv, driver first) for compile_commands.json
        /// emission. Flags come from <see cref="FlagsFor"/> — the same source
        /// <see cref="Compile"/> consumes — so emitted commands match the real
        /// compile exactly. Static + kind-keyed so it is unit-testable without
        /// an installed toolchain.
        /// </summary>
        public static List<string> BuildCompileCommandArguments(
            CppToolchainKind kind, string driver, CppCompileRequest request, string sourceFile)
        {
            var args = new List<string> { driver };
            args.AddRange(FlagsFor(kind, request));
            args.Add(sourceFile);
            return args;
        }

        /// <summary>cl /c and g++ -c drop basename-derived .obj/.o files into a
        /// single directory, so two TUs sharing a base name would silently
        /// overwrite each other's objects. Returns the first colliding base name
        /// (OrdinalIgnoreCase), or null when all are distinct.</summary>
        internal static string FindDuplicateBasename(IEnumerable<string> sourceFiles)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in sourceFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!seen.Add(name)) return name;
            }
            return null;
        }

        /// <summary>
        /// Compile a whole project in one toolchain invocation (all TUs on one
        /// command line; Phase 1 has no incremental builds). Executables compile
        /// and link in one step; libraries compile to objects then archive
        /// (llvm-ar/ar for clang/g++, lib.exe inside the vcvars environment).
        /// Known limitation: very large projects could exceed cmd.exe's 8191-char
        /// limit on the MSVC path — acceptable for Phase 1, response files later.
        /// </summary>
        public (bool Success, string Output) Compile(CppCompileRequest request)
        {
            var duplicate = FindDuplicateBasename(request.SourceFiles);
            if (duplicate != null)
                return (false, "error: duplicate source file base names would overwrite object files: " + duplicate);

            var quotedSources = string.Join(" ", request.SourceFiles.Select(s => "\"" + s + "\""));
            var libs = string.Join(" ", request.Libraries.Select(l => "\"" + l + "\""));
            string arguments;

            if (_vcvarsPath != null)
            {
                var flags = JoinFlags(CppToolchainKind.Msvc, request);
                if (request.LinkExecutable)
                {
                    arguments = "/s /c \"\"" + _vcvarsPath + "\" >nul && cl " + flags + " "
                              + quotedSources + (libs.Length > 0 ? " " + libs : "")
                              + " /Fe:\"" + request.OutputPath + "\"\"";
                }
                else
                {
                    // cl /c into the working dir, then lib.exe archives the .obj files.
                    var objs = string.Join(" ", request.SourceFiles.Select(s =>
                        "\"" + Path.GetFileNameWithoutExtension(s) + ".obj\""));
                    arguments = "/s /c \"\"" + _vcvarsPath + "\" >nul && cl /c " + flags + " "
                              + quotedSources + " && lib /nologo /OUT:\"" + request.OutputPath + "\" " + objs + "\"";
                }
                return RunProcess(_executable, arguments, request.WorkingDirectory, request.OutputPath);
            }

            var gnuFlags = JoinFlags(CppToolchainKind.ClangLike, request);

            if (request.LinkExecutable)
            {
                arguments = gnuFlags + " " + quotedSources
                          + (libs.Length > 0 ? " " + libs : "")
                          + " -o \"" + request.OutputPath + "\"";
                return RunProcess(_executable, arguments, request.WorkingDirectory, request.OutputPath);
            }

            // Library: compile to objects, then archive.
            var compile = RunProcess(_executable, gnuFlags + " -c " + quotedSources,
                request.WorkingDirectory, expectedOutput: null);
            if (!compile.Success) return compile;

            var objNames = string.Join(" ", request.SourceFiles.Select(s =>
                "\"" + Path.GetFileNameWithoutExtension(s) + ".o\""));
            var archiver = FindArchiver();
            if (archiver == null)
                return (false, compile.Output + "\nerror: no archiver (llvm-ar/ar) found on PATH for static library output");
            var archive = RunProcess(archiver, "rcs \"" + request.OutputPath + "\" " + objNames,
                request.WorkingDirectory, request.OutputPath);
            return (archive.Success, compile.Output + archive.Output);
        }

        private static string FindArchiver()
        {
            foreach (var exe in new[] { "llvm-ar", "ar" })
            {
                try
                {
                    using var probe = Process.Start(new ProcessStartInfo(exe, "--version")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                    probe.WaitForExit(10000);
                    if (probe.ExitCode == 0) return exe;
                }
                catch { }
            }
            return null;
        }

        private (bool Success, string Output) RunProcess(
            string executable, string arguments, string workingDirectory, string expectedOutput)
        {
            // NOTE: unlike the legacy CompileToExecutable body, this helper drains
            // stdout/stderr via async reads — compilers overflow the ~4KB pipe
            // buffer with error dumps and deadlock naive sync ReadToEnd() code.
            // Success = exit 0 (+ output file exists when expected).
            var psi = new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;

            try
            {
                using var proc = Process.Start(psi);
                var stdOutTask = proc.StandardOutput.ReadToEndAsync();
                var stdErrTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(CompileTimeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return (false, "error: C++ compile timed out after " + (CompileTimeoutMs / 1000) + "s");
                }
                var output = (stdOutTask.Result + "\n" + stdErrTask.Result).Trim();
                var ok = proc.ExitCode == 0 && (expectedOutput == null || File.Exists(expectedOutput));
                return (ok, output);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to invoke {executable}: {ex.Message}");
            }
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
