using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BasicLang.Compiler.ProjectSystem
{
    public sealed class CppProjectBuildResult
    {
        public bool Success { get; set; }
        public string ExecutablePath { get; set; }    // set for OutputType=Exe on success
        public string OutputPath { get; set; }        // output dir
        public List<CppDiagnostic> Diagnostics { get; } = new List<CppDiagnostic>();
        public List<string> Messages { get; } = new List<string>();   // progress lines for CLI/IDE output
        public string RawToolchainOutput { get; set; } = "";
    }

    /// <summary>
    /// Builds a Language=Cpp project: gathers translation units, resolves
    /// includes/libs, emits compile_commands.json, drives CppToolchain, parses
    /// diagnostics, deploys engine DLLs. The ONLY native-project build path —
    /// shared verbatim by the CLI (Program.cs) and the IDE (BuildService) so the
    /// two entry points cannot drift.
    /// </summary>
    public static class CppProjectBuilder
    {
        public static CppProjectBuildResult Build(ProjectFile project, string configuration)
        {
            var result = new CppProjectBuildResult();
            var projectDir = Path.GetDirectoryName(project.FilePath) ?? ".";
            var outputName = project.AssemblyName ?? project.ProjectName ?? "Program";
            var isExe = !string.Equals(project.OutputType, "Library", StringComparison.OrdinalIgnoreCase);

            // Native projects use bin/<config> in BOTH entry points (no TFM — it
            // is meaningless for native output).
            var outputDir = Path.Combine(projectDir, "bin", configuration);
            Directory.CreateDirectory(outputDir);
            result.OutputPath = outputDir;

            // ---- Phase 1 guard: no BasicLang sources in a C++ project ----
            var strayBas = project.GetSourceFiles()
                .Where(f => !ProjectFile.IsInBuildOutputDir(projectDir, f))
                .Where(f => new[] { ".bas", ".bl", ".basic", ".mod", ".cls", ".class" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (strayBas.Count > 0)
            {
                Fail(result, "BL6008",
                    "BasicLang sources in a C++ project are not supported yet (mixed projects arrive in a later phase): "
                    + string.Join(", ", strayBas.Select(Path.GetFileName)), project.FilePath);
                return result;
            }

            // ---- Translation units ----
            var tus = project.GetCppTranslationUnits().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tus.Count == 0)
            {
                // Explicit <Compile> items get a message about THOSE items; only
                // the no-items case actually ran the directory glob.
                var message = project.SourceFiles.Count > 0
                    ? "No C++ translation units among the project's <Compile> items (listed items may be missing on disk or headers-only)."
                    : "No C++ source files found (looked for " + string.Join("/", ProjectFile.CppTranslationUnitExtensions)
                      + " under " + projectDir + ", excluding bin/ and obj/).";
                Fail(result, "BL6007", message, project.FilePath);
                return result;
            }

            // ---- Toolchain (hard requirement for Language=Cpp) ----
            var toolchain = CppToolchain.Find();
            if (toolchain == null)
            {
                Fail(result, "BL6005",
                    "No C++ toolchain found. Probed: clang++ (PATH), g++ (PATH), MSVC (vswhere). "
                    + "Install LLVM/clang (https://releases.llvm.org), MinGW-w64, or Visual Studio Build Tools.",
                    project.FilePath);
                return result;
            }
            result.Messages.Add($"Compiling C++ with {toolchain.DisplayName}...");

            // ---- Request ----
            var request = new CppCompileRequest
            {
                OutputPath = Path.Combine(outputDir, outputName + (isExe ? ".exe"
                    : toolchain.Kind == CppToolchainKind.Msvc ? ".lib" : ".a")),
                LinkExecutable = isExe,
                CppStandard = project.CppStandard,
                WorkingDirectory = outputDir,
                DebugSymbols = true,
                Optimize = false,
            };
            if (project.Configurations.TryGetValue(configuration, out var config))
            {
                request.Optimize = config.OptimizationsEnabled;
                request.DebugSymbols = config.DebugSymbols;
            }
            request.SourceFiles.AddRange(tus);

            request.IncludeDirs.Add(projectDir);
            foreach (var inc in project.IncludeDirs)
                request.IncludeDirs.Add(Path.IsPathRooted(inc) ? inc : Path.Combine(projectDir, inc));
            request.Defines.AddRange(project.Defines);

            // ---- Native libs (engine lib resolves via EngineDeployment) ----
            string engineLib = null;
            foreach (var lib in project.NativeLibs)
            {
                var local = Path.IsPathRooted(lib) ? lib : Path.Combine(projectDir, lib);
                if (File.Exists(local))
                {
                    request.Libraries.Add(local);
                    // A vendored engine import lib must still deploy its native
                    // DLLs next to the exe (LocateNativeDlls looks in the lib's
                    // dir first, then the compiler's base dir) — otherwise the
                    // built game dies at startup with a missing-DLL error and
                    // no build-time hint.
                    if (string.Equals(Path.GetFileName(lib), EngineDeployment.EngineImportLibName,
                            StringComparison.OrdinalIgnoreCase))
                        engineLib = local;
                    continue;
                }

                if (string.Equals(Path.GetFileName(lib), EngineDeployment.EngineImportLibName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    engineLib = EngineDeployment.LocateImportLib();
                    if (engineLib != null)
                    {
                        request.Libraries.Add(engineLib);
                        result.Messages.Add($"Engine import library: {engineLib}");
                        continue;
                    }
                }
                Fail(result, "BL6009", $"Native library not found: {lib}", project.FilePath);
                return result;
            }

            // ---- compile_commands.json (advisory — never fails the build) ----
            try
            {
                var ccPath = CompileCommandsWriter.Write(projectDir, toolchain.Kind, toolchain.DriverName, request);
                result.Messages.Add($"Compilation database: {ccPath}");
            }
            catch (Exception ex)
            {
                result.Messages.Add($"warning: could not write compilation database: {ex.Message}");
            }

            // ---- Compile ----
            var (ok, output) = toolchain.Compile(request);
            ApplyCompileOutcome(result, ok, output, outputDir, project.FilePath);
            if (!result.Success)
                return result;

            if (isExe) result.ExecutablePath = request.OutputPath;
            result.Messages.Add($"Output: {request.OutputPath}");

            // ---- Engine DLL deploy ----
            if (engineLib != null)
            {
                foreach (var dll in EngineDeployment.LocateNativeDlls(engineLib))
                {
                    // A locked DLL (previous game run still open) must never
                    // discard a successful build result — warn and continue,
                    // mirroring the CLI's existing deploy precedent.
                    try
                    {
                        var dest = Path.Combine(outputDir, Path.GetFileName(dll));
                        File.Copy(dll, dest, overwrite: true);
                        result.Messages.Add($"Deployed {Path.GetFileName(dll)}");
                    }
                    catch (Exception ex)
                    {
                        result.Messages.Add($"warning: could not deploy {Path.GetFileName(dll)}: {ex.Message}");
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Applies a toolchain compile outcome to the result: parses diagnostics
        /// and — load-bearing — falls back to BL6006 with the raw output when a
        /// FAILED compile parses zero errors (gcc/ld/lld grammars the parser
        /// doesn't know must never fail silently). Internal for direct testing.
        /// </summary>
        internal static void ApplyCompileOutcome(CppProjectBuildResult result, bool ok,
            string output, string workingDirectory, string projectFilePath)
        {
            result.RawToolchainOutput = output;
            result.Diagnostics.AddRange(CppDiagnosticsParser.Parse(output, workingDirectory));

            if (!ok)
            {
                if (!result.Diagnostics.Any(d => !d.IsWarning))
                {
                    Fail(result, "BL6006", "C++ compilation failed: " + output, projectFilePath);
                }
                result.Success = false;
                return;
            }
            result.Success = true;
        }

        private static void Fail(CppProjectBuildResult result, string code, string message, string filePath)
        {
            result.Success = false;
            result.Diagnostics.Add(new CppDiagnostic
            { FilePath = filePath, Line = 0, Column = 0, IsWarning = false, Code = code, Message = message });
        }
    }
}
