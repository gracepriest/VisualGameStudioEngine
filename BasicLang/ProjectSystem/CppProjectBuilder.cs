using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

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

            var objGenDir = Path.Combine(projectDir, "obj", "gen");

            // ---- 1. Partition sources ----
            // BasicLang sources are transpiled to C++; C++ translation units compile
            // directly. Headers and anything else are ignored (reachable via the
            // include path). Build-output dirs (bin/obj) are never sources.
            var blSources = project.GetSourceFiles()
                .Where(f => !ProjectFile.IsInBuildOutputDir(projectDir, f))
                .Where(f => ProjectFile.BasicLangSourceExtensions.Contains(
                    Path.GetExtension(f).ToLowerInvariant()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var userTus = project.GetCppTranslationUnits()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ---- 2. Nothing to build ----
            if (blSources.Count == 0 && userTus.Count == 0)
            {
                // Explicit <Compile> items get a message about THOSE items; only the
                // no-items case actually ran the directory glob.
                var message = project.SourceFiles.Count > 0
                    ? "No C++ translation units or BasicLang sources among the project's <Compile> items (listed items may be missing on disk or headers-only)."
                    : "Project contains no C++ translation units and no BasicLang sources (looked under "
                      + projectDir + ", excluding bin/ and obj/).";
                Fail(result, "BL6007", message, project.FilePath);
                return result;
            }

            // ---- 3. Transpile stage (BasicLang -> C++) ----
            CompilationResult compilation = null;
            List<IRModule> unitIRs = null;
            var basicLangMainCount = 0;
            if (blSources.Count > 0)
            {
                var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
                compilation = compiler.CompileProjectFiles(blSources);
                if (!compilation.Success)
                {
                    // On failure result.Units is empty; errors are attributed per-unit
                    // through the registry (mirrors BuildService.MapCompilerDiagnostics).
                    MapTranspileDiagnostics(result, compiler, compilation, project.FilePath);
                    result.Success = false;
                    return result;
                }
                unitIRs = compilation.Units.Select(u => u.IR).Where(ir => ir != null).ToList();
                // Count Main from the PER-UNIT IRs, never CombinedIR: the combiner is
                // first-wins on duplicate cross-file Sub Main, so CombinedIR has at most
                // one (Task 3 probe pins this). Registry.Modules is failure-safe.
                basicLangMainCount = CountBasicLangMains(compiler.Registry.Modules.Select(u => u.IR));
            }

            // ---- 4. Entry-point rule (pre-link, so 0/2-main cases give clickable diags) ----
            var cppMains = userTus
                .Select(t => (file: Path.GetFileName(t), count: NativeEntryPoints.CountCppMains(TryReadAllText(t))))
                .ToList();
            var entryDiags = NativeEntryPoints.Apply(isExe, basicLangMainCount, cppMains);
            if (entryDiags.Count > 0)
            {
                result.Diagnostics.AddRange(entryDiags);
                result.Success = false;
                return result;
            }
            // BasicLang owns the single entry point iff it has the one Main; if the sole
            // entry is a user C++ main, basicLangMainCount == 0 so emitMain is false and
            // the C++ side provides main().
            var emitMain = isExe && basicLangMainCount == 1;

            // ---- 5. Generate split C++ from the transpiled IR ----
            CppSplitResult split = null;
            var generatedTus = new List<string>();
            if (blSources.Count > 0)
            {
                // Stale generated files from renamed/removed modules would otherwise still
                // compile — clean before regenerating (OrdinalIgnoreCase-safe on Windows).
                CleanGeneratedDir(objGenDir);
                Directory.CreateDirectory(objGenDir);

                // GenerateSplit's precondition: projectName must be filename-safe (it is
                // used verbatim in <Project>.g.h etc). The builder owns that guarantee.
                var safeProject = SanitizeProjectName(outputName);
                try
                {
                    split = new CppCodeGenerator().GenerateSplit(
                        compilation.CombinedIR, safeProject, unitIRs, emitMain);
                }
                catch (CppCapabilityException ex)
                {
                    Fail(result, "BL6001", ex.Message, project.FilePath);
                    return result;
                }
                catch (ArgumentException ex)
                {
                    // Reserved-name collision (a module named like a generated file) — the
                    // exception message names the offending module.
                    Fail(result, "BL6001", ex.Message, project.FilePath);
                    return result;
                }

                foreach (var kv in split.Files)
                    File.WriteAllText(Path.Combine(objGenDir, kv.Key), kv.Value);
                generatedTus = split.TranslationUnitFileNames
                    .Select(n => Path.Combine(objGenDir, n))
                    .ToList();
            }

            // ---- 6. Toolchain (hard requirement for ALL native projects, D6) ----
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

            // ---- 7. Request ----
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
            // User C++ TUs plus the generated per-module .g.cpp files (both absolute).
            // The .g suffix keeps generated basenames from colliding with user files.
            request.SourceFiles.AddRange(userTus);
            request.SourceFiles.AddRange(generatedTus);

            // projectDir first (so user quote-includes and #CppInclude "helper.h" resolve),
            // then obj/gen (so user C++ can #include the generated shim headers), then items.
            request.IncludeDirs.Add(projectDir);
            if (blSources.Count > 0)
                request.IncludeDirs.Add(objGenDir);
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

            // ---- Engine framework auto-link ----
            // A mixed/BasicLang game that calls Framework_* engine functions but declares
            // no explicit <NativeLib> still needs the engine import library linked (and its
            // DLLs deployed). Only kicks in when codegen actually emitted framework calls.
            if (split != null && split.UsesFramework && engineLib == null)
            {
                engineLib = EngineDeployment.LocateImportLib();
                if (engineLib != null)
                {
                    request.Libraries.Add(engineLib);
                    result.Messages.Add($"Engine import library: {engineLib}");
                }
                else
                {
                    Fail(result, "BL6009",
                        $"Engine framework used but {EngineDeployment.EngineImportLibName} not found: "
                        + "the project calls Framework_* engine functions but the engine import library "
                        + "could not be located. Build the native engine (x64) or add a <NativeLib> reference.",
                        project.FilePath);
                    return result;
                }
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

        /// <summary>
        /// Maps a failed transpile's errors into build diagnostics with the best file
        /// attribution available (loosely modeled on BuildService.MapCompilerDiagnostics,
        /// a different assembly's consumer): per-unit errors carry the unit's file path;
        /// the SemanticError type has no path of its own. Each DISTINCT source error
        /// yields exactly one diagnostic — never a second copy pinned to the .blproj.
        ///
        /// The subtlety: FinalizeResult -> WithInlineLocation REPLACES every located
        /// (Line &gt; 0) AllErrors entry with a fresh, message-prefixed copy ("Error at
        /// line L, column C: &lt;original&gt;") while leaving unit.Errors holding the
        /// ORIGINAL object. So the rewritten AllErrors copy is no longer reference-equal
        /// to the already-attributed per-unit error; a reference-only dedup would leak it
        /// through as a spurious .blproj orphan with a double-prefixed message. Orphan
        /// detection therefore also matches by VALUE (same line+column, and the AllErrors
        /// message ends with the attributed per-unit message).
        /// </summary>
        private static void MapTranspileDiagnostics(CppProjectBuildResult result,
            BasicCompiler compiler, CompilationResult compilation, string projectFilePath)
        {
            // Registry.Modules holds units even when compilation aborted before
            // CompilationResult.Units was populated (e.g. parse errors).
            var units = compiler.Registry.Modules.ToList();

            // Per-unit errors carry the unit's file path — the clean, non-prefixed
            // message straight off unit.Errors. Reference-dedup so a single error object
            // shared across units is attributed once.
            var attributed = new HashSet<SemanticError>(ReferenceEqualityComparer.Instance);
            var attributedList = new List<SemanticError>();
            foreach (var unit in units)
                foreach (var error in unit.Errors)
                    if (attributed.Add(error))
                    {
                        AddTranspileDiagnostic(result, error, unit.FilePath);
                        attributedList.Add(error);
                    }

            // A genuine orphan is an AllErrors entry that reached no unit (parse /
            // infrastructure errors) — neither reference-attributed NOR a value-duplicate
            // of an attributed per-unit error (WithInlineLocation copy).
            var orphans = compilation.AllErrors
                .Where(e => !attributed.Contains(e) && !IsDuplicateOfAttributed(e, attributedList))
                .ToList();

            string fallbackPath = null;
            var unattributedFailedUnits = units
                .Where(u => u.Status == CompilationStatus.Error && u.Errors.Count == 0)
                .ToList();
            if (unattributedFailedUnits.Count == 1)
                fallbackPath = unattributedFailedUnits[0].FilePath;

            foreach (var error in orphans)
                AddTranspileDiagnostic(result, error, fallbackPath ?? projectFilePath);
        }

        // True when <paramref name="candidate"/> (an AllErrors entry) is the
        // WithInlineLocation rewrite of an already-attributed per-unit error: same
        // location, and the candidate's message is the per-unit message with only a
        // "<Severity> at line L, column C: " prefix prepended (so it ends with it).
        private static bool IsDuplicateOfAttributed(SemanticError candidate, List<SemanticError> attributed)
        {
            var cm = candidate.Message ?? string.Empty;
            foreach (var a in attributed)
            {
                if (a.Line != candidate.Line || a.Column != candidate.Column)
                    continue;
                var am = a.Message ?? string.Empty;
                // EndsWith subsumes exact equality; require a non-empty per-unit message
                // so an empty message can't match every candidate at the same location.
                if (am.Length > 0 && cm.EndsWith(am, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void AddTranspileDiagnostic(CppProjectBuildResult result, SemanticError error, string filePath)
        {
            result.Diagnostics.Add(new CppDiagnostic
            {
                FilePath = filePath,
                Line = error.Line,
                Column = error.Column,
                IsWarning = error.Severity != ErrorSeverity.Error,
                Code = string.IsNullOrEmpty(error.ErrorCode) ? "BL3001" : error.ErrorCode,
                Message = error.Message,
            });
        }

        /// <summary>
        /// Counts standalone BasicLang <c>Main</c> functions (case-insensitive) across
        /// the per-unit IRs. Standalone = not external, not a lambda, and not a class
        /// method (class-method implementations also live in module.Functions — the same
        /// distinction CppCodeGenerator.IsClassMethod makes when picking free functions).
        /// Counting per-unit (rather than from the combined IR) is deliberate: the IR
        /// combiner keeps only the first Sub Main across files, so the combined module
        /// undercounts a duplicate-entry error.
        /// </summary>
        private static int CountBasicLangMains(IEnumerable<IRModule> unitModules)
        {
            var count = 0;
            foreach (var module in unitModules)
            {
                if (module?.Functions == null) continue;
                foreach (var fn in module.Functions)
                {
                    if (fn == null || fn.IsExternal || fn.IsLambda) continue;
                    if (!string.Equals(fn.Name, "Main", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsClassMethod(fn, module)) continue;
                    count++;
                }
            }
            return count;
        }

        // Local copy of CppCodeGenerator.IsClassMethod (that one is private): a function
        // is a class member iff it is some class's method/constructor/accessor impl.
        private static bool IsClassMethod(IRFunction function, IRModule module)
        {
            if (module.Classes == null) return false;
            foreach (var irClass in module.Classes.Values)
            {
                if (irClass.Methods != null && irClass.Methods.Any(m => m.Implementation == function)) return true;
                if (irClass.Constructors != null && irClass.Constructors.Any(c => c.Implementation == function)) return true;
                if (irClass.Properties != null && irClass.Properties.Any(p => p.Getter == function || p.Setter == function)) return true;
            }
            return false;
        }

        // Filename-safe token for GenerateSplit's precondition: keep [A-Za-z0-9_],
        // replace everything else with '_', fall back to "Program" when empty.
        private static string SanitizeProjectName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Program";
            var chars = name.Select(ch =>
                (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') || ch == '_' ? ch : '_').ToArray();
            var safe = new string(chars);
            return string.IsNullOrEmpty(safe) ? "Program" : safe;
        }

        // Delete stale generated files so a renamed/removed module's old .g.cpp/.g.h can
        // never be picked up by a later compile. Best-effort (a locked file is skipped).
        private static void CleanGeneratedDir(string objGenDir)
        {
            if (!Directory.Exists(objGenDir)) return;
            foreach (var file in Directory.EnumerateFiles(objGenDir)
                .Where(f => f.EndsWith(".g.cpp", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".g.h", StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }

        // A translation unit that vanished between discovery and read contributes no
        // entry-point candidates; the real compile then surfaces the missing-file error.
        private static string TryReadAllText(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return string.Empty; }
        }
    }
}
