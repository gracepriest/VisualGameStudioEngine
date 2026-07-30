using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;

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
    /// Everything <see cref="CppProjectBuilder.EmitCore"/> produces that a caller
    /// might continue from. <see cref="Completed"/> is the only success signal:
    /// when it is false the core already recorded the failure on the result.
    /// </summary>
    internal sealed class CppEmitOutcome
    {
        /// <summary>True when the core ran all the way through the compile-database
        /// write; false means the caller must stop and return the result as-is.</summary>
        public bool Completed { get; set; }
        public CppCompileRequest Request { get; set; }
        /// <summary>The resolved toolchain, or NULL — legitimately so on the IntelliSense
        /// path, which does not require one (the emitter falls back to the blessed clang++
        /// identity). Non-null whenever <see cref="Completed"/> is true on a BUILD, because
        /// EmitCore hard-fails BL6005/BL6015 first; that is the only reason Build may dereference
        /// it unguarded. The declared type cannot express this, and BasicLang.csproj has
        /// CS8602/CS8604 in NoWarn, so the compiler will not warn a future caller either.</summary>
        public CppToolchain Toolchain { get; set; }
        /// <summary>Resolved engine import library whose DLLs need deploying, or null.
        /// Always null on the IntelliSense path (link-time concern).</summary>
        public string EngineLib { get; set; }
        public string OutputDir { get; set; }
        public bool IsExe { get; set; }
        /// <summary>
        /// The .NET assembly closure resolved in phase 1 (spec §10.1) — the declared
        /// <c>&lt;Reference&gt;</c>/<c>&lt;PackageReference&gt;</c> assemblies, the framework set,
        /// and every reference diagnostic regardless of which path recorded it on
        /// <see cref="CppProjectBuildResult.Diagnostics"/>. Non-null whenever EmitCore got past
        /// phase 1, which is before every other phase; null only if it never ran at all.
        /// Tasks 8, 12, 14 and 15 consume this.
        /// </summary>
        public NetReferenceClosure NetReferences { get; set; }
    }

    /// <summary>
    /// Builds a Language=Cpp project: gathers translation units, resolves
    /// includes/libs, emits compile_commands.json, drives CppToolchain, parses
    /// diagnostics, deploys engine DLLs. The ONLY native-project build path —
    /// shared verbatim by the CLI (Program.cs) and the IDE (BuildService) so the
    /// two entry points cannot drift.
    ///
    /// Everything up to and including the compile_commands.json write lives in
    /// <see cref="EmitCore"/>, which <see cref="IntelliSenseEmitter"/> also drives
    /// (toolchain-free, build gates bypassed). <see cref="Build"/> adds only the
    /// compile / link / deploy tail — there is no second emission implementation.
    /// </summary>
    public static class CppProjectBuilder
    {
        /// <summary>
        /// <paramref name="resolveById"/> / <paramref name="probeAvailability"/> are test
        /// seams for <see cref="CppToolchain.TryFindById"/> /
        /// <see cref="CppToolchain.ProbeAvailability"/> (null = the real ones), threaded
        /// through to <see cref="EmitCore"/>'s toolchain gate the same way
        /// resolveToolchain is. <paramref name="resolveToolchain"/> is the unpinned-project
        /// toolchain resolver (null = <see cref="CppToolchain.Find"/>); the IDE's
        /// BuildService injects an override-aware resolver here, keeping this compiler
        /// layer settings-agnostic.
        /// </summary>
        public static CppProjectBuildResult Build(ProjectFile project, string configuration,
            Func<string, CppToolchain> resolveById = null,
            Func<CppToolchainAvailability> probeAvailability = null,
            Func<CppToolchain> resolveToolchain = null)
        {
            var result = new CppProjectBuildResult();
            var emit = EmitCore(project, configuration, result,
                resolveToolchain ?? CppToolchain.Find, forIntelliSense: false, resolveById, probeAvailability);
            if (!emit.Completed)
                return result;

            var request = emit.Request;

            // ---- Compile ----
            var (ok, output) = emit.Toolchain.Compile(request);
            ApplyCompileOutcome(result, ok, output, emit.OutputDir, project.FilePath);
            if (!result.Success)
                return result;

            if (emit.IsExe) result.ExecutablePath = request.OutputPath;
            result.Messages.Add($"Output: {request.OutputPath}");

            // ---- Engine DLL deploy ----
            if (emit.EngineLib != null)
                foreach (var dll in EngineDeployment.LocateNativeDlls(emit.EngineLib))
                    DeployDll(result, emit.OutputDir, dll);

            // ---- MinGW C++ runtime deploy ----
            // A MinGW-linked exe (g++ / winlibs-clang) dynamically imports libstdc++-6.dll,
            // libgcc_s_seh-1.dll and libwinpthread-1.dll. With the toolchain off PATH the exe
            // cannot resolve them at standalone launch ("libstdc++-6.dll not found"), so copy
            // them next to it. Self-detecting via the compiler's bin dir: a no-op for MSVC-ABI
            // toolchains (none of those DLLs live beside cl/clang there) and for libraries.
            if (emit.IsExe)
                foreach (var dll in CppRuntimeDeployment.LocateRuntimeDlls(emit.Toolchain?.ResolveCompilerDirectory()))
                    DeployDll(result, emit.OutputDir, dll);

            return result;
        }

        // Copy one DLL next to the built exe. A locked DLL (a previous game run still
        // open) must never discard a successful build result — warn and continue,
        // mirroring the CLI's existing deploy precedent.
        private static void DeployDll(CppProjectBuildResult result, string outputDir, string dll)
        {
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

        /// <summary>
        /// The shared emission core: partition sources, transpile, generate and write
        /// obj/gen, resolve the toolchain, assemble the compile request, write
        /// obj/compile_commands.json. Used by <see cref="Build"/> (which continues to
        /// compile/link) and by <see cref="IntelliSenseEmitter"/> (which stops here).
        ///
        /// <paramref name="forIntelliSense"/> is a MODE SELECTOR, not just a gate switch: it
        /// also suppresses build-progress messages, skips creating bin/&lt;config&gt; (opening
        /// a project is not a build), and makes a failed compile-database write fatal rather
        /// than advisory (the database is IntelliSense's whole deliverable, but only a hint
        /// to a build). Grep <c>forIntelliSense</c> for the full set of sites.
        ///
        /// The gates it bypasses are the ones that are BUILD rules rather than IntelliSense
        /// rules — none of them changes a compile flag:
        /// <list type="bullet">
        /// <item>BL6007 no-sources — a project mid-creation still deserves completion.</item>
        /// <item>The entry-point rule — a project with no <c>Sub Main</c> YET would
        /// otherwise get no headers at all, which is the normal mid-edit state.</item>
        /// <item>BL6005 no-toolchain (and BL6015 pinned-toolchain-missing) — the entire
        /// point of the seam.</item>
        /// <item>Native libs (BL6009) and the engine framework auto-link — LINK-time
        /// concerns. <c>request.Libraries</c> never reaches <c>CppToolchain.FlagsFor</c>,
        /// so libraries contribute NOTHING to a compilation database (it carries compile
        /// flags only). Skipping them loses nothing and stops a game project whose engine
        /// .lib does not resolve from being denied IntelliSense entirely.</item>
        /// </list>
        /// A transpile failure is deliberately NOT bypassed: broken source yields no IR at
        /// all, and failing here returns BEFORE the obj/gen clean, so the last good headers
        /// survive for clangd to keep serving. Regen-on-success-only, never wipe on failure.
        ///
        /// <paramref name="resolveToolchain"/> is a factory rather than a value so the probe
        /// stays lazy and keeps its original position (after the obj/gen write) — the
        /// ordering Task 8's test pins.
        /// </summary>
        internal static CppEmitOutcome EmitCore(ProjectFile project, string configuration,
            CppProjectBuildResult result, Func<CppToolchain> resolveToolchain, bool forIntelliSense,
            Func<string, CppToolchain> resolveById = null,
            Func<CppToolchainAvailability> probeAvailability = null)
        {
            var outcome = new CppEmitOutcome();
            var projectDir = Path.GetDirectoryName(project.FilePath) ?? ".";
            var outputName = project.AssemblyName ?? project.ProjectName ?? "Program";
            var isExe = !string.Equals(project.OutputType, "Library", StringComparison.OrdinalIgnoreCase);
            outcome.IsExe = isExe;

            // Native projects use bin/<config> in BOTH entry points (no TFM — it
            // is meaningless for native output). Only a real build creates it:
            // opening a project in the IDE must not litter it with empty output
            // dirs, and nothing on the IntelliSense path writes there (the compile
            // database keys off projectDir, not the working directory).
            var outputDir = Path.Combine(projectDir, "bin", configuration);
            if (!forIntelliSense)
                Directory.CreateDirectory(outputDir);
            // NOTE: reported on BOTH paths, but for IntelliSense this is where output WOULD
            // go — the directory is deliberately not created, so it may not exist.
            result.OutputPath = outputDir;
            outcome.OutputDir = outputDir;

            var objGenDir = Path.Combine(projectDir, "obj", "gen");

            // ---- Resolve .NET references (spec §10.1 phase 1) ----
            // Deliberately unnumbered: the spec's phase 1 is THIS, and its phase 2 is the BL->IR
            // transpile that the "3." below already covers, so any ordinal here would put the
            // file's 1..7 permanently one out of step with the spec's. Position conveys "first".
            // Runs on BOTH paths. Until P2a-1 every reference element was parsed into the project
            // model and then silently discarded on the native path (Program.cs returned before
            // restore, and this builder read no reference item at all), so a typo'd <HintPath>
            // produced no output whatsoever. BL6021 replaces that silence.
            //
            // INERTNESS: a project that declares no <Reference>/<PackageReference>/
            // <ProjectReference> — which is every native project that exists today — produces
            // zero diagnostics, an empty declared set, and touches neither the network nor the
            // project directory here. Nothing in this block requires a toolchain, so it is safe
            // on the toolchain-free IntelliSense path.
            var (packageAssemblies, packageErrors) =
                RestorePackagesForClosure(project, configuration, forIntelliSense);
            var netReferences = NetReferenceResolver.Resolve(
                project, project.FilePath, packageAssemblies, packageErrors);
            outcome.NetReferences = netReferences;

            // Diagnostics reach result ONLY on a build. IntelliSense's OUTPUTS stay byte-for-byte
            // as they were (it does pay for the resolution work itself): it bypasses the
            // BUILD-rule gates (BL6007/BL6005/BL6009) for the same reason — an unresolvable
            // reference must not cost the user their generated headers — and IntelliSenseEmitter
            // derives Success from outcome.Completed, so an error here would otherwise turn a
            // perfectly good header regeneration into an Output-panel failure line on every typo'd
            // HintPath. outcome.NetReferences.Diagnostics is the complete record on both paths.
            // Pinned by IntelliSenseEmitterTests.Emit_WithUnresolvableReference_*.
            if (!forIntelliSense)
            {
                var referenceErrors = 0;
                foreach (var diag in netReferences.Diagnostics)
                {
                    if (diag.IsWarning)
                    {
                        // Hand-constructed rather than routed through Fail: Fail forces
                        // Success = false, and a warning must not. Do NOT "unify" these two.
                        result.Diagnostics.Add(new CppDiagnostic
                        {
                            FilePath = project.FilePath,
                            Line = 0,
                            Column = 0,
                            IsWarning = true,
                            Code = diag.Code,
                            Message = diag.Message
                        });
                        continue;
                    }
                    // Report ALL of them before stopping: one BL6021 per broken reference is far
                    // more useful than the first one alone.
                    Fail(result, diag.Code, diag.Message, project.FilePath);
                    referenceErrors++;
                }
                if (referenceErrors > 0)
                    return outcome;   // Completed stays false — the caller returns the result as-is.
            }

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
            // Bypassed for IntelliSense: an empty project mid-creation gets a degenerate
            // (empty) compile database rather than a hard error.
            if (!forIntelliSense && blSources.Count == 0 && userTus.Count == 0)
            {
                // Explicit <Compile> items get a message about THOSE items; only the
                // no-items case actually ran the directory glob.
                var message = project.SourceFiles.Count > 0
                    ? "No C++ translation units or BasicLang sources among the project's <Compile> items (listed items may be missing on disk or headers-only)."
                    : "Project contains no C++ translation units and no BasicLang sources (looked under "
                      + projectDir + ", excluding bin/ and obj/).";
                Fail(result, "BL6007", message, project.FilePath);
                return outcome;
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
                    return outcome;   // Completed stays false — note this returns BEFORE the
                                      // obj/gen clean, which is what preserves stale headers.
                }
                unitIRs = compilation.Units.Select(u => u.IR).Where(ir => ir != null).ToList();
                // Count Main from the PER-UNIT IRs, never CombinedIR: the combiner is
                // first-wins on duplicate cross-file Sub Main, so CombinedIR has at most
                // one (Task 3 probe pins this). Registry.Modules == compilation.Units here
                // (we already returned on failure); it is used only for failure-safety
                // symmetry with MapTranspileDiagnostics, which reads the registry directly.
                basicLangMainCount = CountBasicLangMains(compiler.Registry.Modules.Select(u => u.IR));
            }

            // ---- 4. Entry-point rule (pre-link, so 0/2-main cases give clickable diags) ----
            // Bypassed for IntelliSense: "no Sub Main yet" is the normal mid-edit state and
            // must not cost the user their headers. Skipping the block also skips reading
            // every user TU off disk, which the rule is the only consumer of.
            if (!forIntelliSense)
            {
                var cppMains = userTus
                    .Select(t => (file: Path.GetFileName(t), count: NativeEntryPoints.CountCppMains(TryReadAllText(t))))
                    .ToList();
                var entryDiags = NativeEntryPoints.Apply(isExe, basicLangMainCount, cppMains);
                if (entryDiags.Count > 0)
                {
                    result.Diagnostics.AddRange(entryDiags);
                    result.Success = false;
                    return outcome;
                }
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
                // GenerateSplit's precondition: projectName must be filename-safe (it is
                // used verbatim in <Project>.g.h etc). The builder owns that guarantee.
                var safeProject = SanitizeProjectName(outputName);

                // ---- 5a. Pure codegen (BL6001) — no file IO happens in here. -------------
                // Generate the full file set into memory BEFORE touching obj/gen. A codegen
                // failure (CppCapabilityException on an unsupported construct, or the
                // reserved-name ArgumentException) must NOT wipe the previously generated
                // headers: IntelliSense (clangd) reads them between builds, so one
                // unsupported construct silently destroying every header is a real
                // regression. split.Files is fully in-memory, so generate -> clean -> write
                // is a safe reorder, not a redesign.
                try
                {
                    // Debug-only #line: keyed off the SAME per-config DebugSymbols bit that
                    // decides /Zi | -g (CppCompileRequest below) so source-mapped line tables
                    // and debug info always travel together. Release output stays
                    // byte-identical (DebugSymbols defaults false there, ProjectFile.cs).
                    // IntelliSense emission stays clean: clangd serves the generated headers
                    // to user .cpp and #line would remap its diagnostics onto .bas lines.
                    bool emitLineDirectives = !forIntelliSense;
                    if (emitLineDirectives
                        && project.Configurations.TryGetValue(configuration, out var cfgForCodegen))
                        emitLineDirectives = cfgForCodegen.DebugSymbols;
                    split = new CppCodeGenerator(new CppCodeGenOptions
                    {
                        EmitLineDirectives = emitLineDirectives
                    }).GenerateSplit(compilation.CombinedIR, safeProject, unitIRs, emitMain);
                }
                catch (CppCapabilityException ex)
                {
                    Fail(result, "BL6001", ex.Message, project.FilePath);
                    return outcome;
                }
                catch (ArgumentException ex)
                {
                    // Reserved-name collision (a module named like a generated file) — the
                    // exception message names the offending module.
                    Fail(result, "BL6001", ex.Message, project.FilePath);
                    return outcome;
                }

                // ---- 5b. File IO (BL6006) — a separate try so an IO fault is never -------
                // mislabeled as a codegen error. ArgumentException belongs to BL6006 HERE
                // (an invalid path out of Path.Combine/WriteAllText), which is exactly why
                // the two stages cannot share one catch list: the same exception type means
                // "codegen rejected a name" above and "the filesystem rejected a path" here.
                // (A mid-loop IO fault at the write step can still leave a partial set —
                // a genuine environment failure, out of scope.)
                try
                {
                    // Stale generated files from renamed/removed modules would otherwise
                    // still compile — clean before writing the fresh set (OrdinalIgnoreCase-
                    // safe on Windows).
                    foreach (var stale in CleanGeneratedDir(objGenDir))
                        result.Messages.Add($"warning: could not delete stale generated file {stale}");
                    Directory.CreateDirectory(objGenDir);
                    foreach (var kv in split.Files)
                        File.WriteAllText(Path.Combine(objGenDir, kv.Key), kv.Value);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                                             || ex is ArgumentException
                                                             || ex is NotSupportedException)
                {
                    Fail(result, "BL6006", "Failed to write generated C++ to obj/gen: " + ex.Message,
                        project.FilePath);
                    return outcome;
                }

                generatedTus = split.TranslationUnitFileNames
                    .Select(n => Path.Combine(objGenDir, n))
                    .ToList();
            }

            // ---- 6. Toolchain (hard requirement for ALL native projects, D6) ----
            // Deliberately after the obj/gen write: the headers must exist even when this
            // gate fires (Task 8's test pins that ordering, Task 9 depends on it).
            //
            // A project may pin one specific toolchain via <CppToolchain> ("llvm" | "gcc" |
            // "msvc"); then ONLY that toolchain satisfies the build — a machine that has
            // some other compiler installed gets BL6015 naming both sides, never a silent
            // substitute. No pin = the machine probe, exactly as before. The pin drives
            // IntelliSense emission through the SAME resolution (so the compile database
            // matches what a build would use), but IntelliSense keeps tolerating a null
            // result the same way it tolerates BL6005's — clang++-identity fallback below.
            var requestedId = project.CppToolchain;
            var toolchain = string.IsNullOrEmpty(requestedId)
                ? resolveToolchain()
                : (resolveById ?? CppToolchain.TryFindById)(requestedId);
            outcome.Toolchain = toolchain;
            if (toolchain == null && !forIntelliSense)
            {
                if (!string.IsNullOrEmpty(requestedId))
                {
                    Fail(result, "BL6015",
                        $"C++ toolchain '{requestedId}' requested by the project is not installed. "
                        + $"Detected: {(probeAvailability ?? CppToolchain.ProbeAvailability)().DetectedList}. "
                        + $"Install {requestedId} or change <CppToolchain> in the project file.",
                        project.FilePath);
                    return outcome;
                }
                Fail(result, "BL6005",
                    "No C++ toolchain found. Probed: clang++ (PATH), g++ (PATH), MSVC (vswhere). "
                    + "Install LLVM/clang (https://releases.llvm.org), MinGW-w64, or Visual Studio Build Tools.",
                    project.FilePath);
                return outcome;
            }

            // The compiler IDENTITY, defaulted before anything dereferences it — the request's
            // OutputPath extension already needs Kind, so a null toolchain would be an NRE
            // here, not a missing flag. A null toolchain only reaches this line on the
            // IntelliSense path (Build hard-fails above); clang++ is the spec's blessed
            // toolchain, so that is the default. When a toolchain IS installed we prefer its
            // own identity.
            //
            // Kind and driver ALWAYS come from the same source. clangd picks its parsing mode
            // from arguments[0], so a "cl" driver carrying GNU flags — or a "clang++" driver
            // carrying /std: — parses silently wrong. Pairing them here is what makes that
            // unrepresentable, including on an MSVC machine, where a real build legitimately
            // emits cl + /std:c++20 and clangd reads it in cl-driver mode.
            var kind = toolchain?.Kind ?? CppToolchainKind.ClangLike;
            var driver = toolchain?.DriverName ?? "clang++";

            if (!forIntelliSense)
                result.Messages.Add($"Compiling C++ with {toolchain.DisplayName}...");

            // ---- 7. Request ----
            var request = new CppCompileRequest
            {
                OutputPath = Path.Combine(outputDir, outputName + (isExe ? ".exe"
                    : kind == CppToolchainKind.Msvc ? ".lib" : ".a")),
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
            // Skipped wholesale for IntelliSense: libraries are LINK inputs and never reach
            // CppToolchain.FlagsFor, so they contribute nothing to compile_commands.json.
            // Running the block would only import its BL6009 failure — which sits BEFORE the
            // compile-database write — and deny a game project with an unresolved engine .lib
            // any IntelliSense at all, for zero benefit.
            string engineLib = null;
            foreach (var lib in forIntelliSense ? Enumerable.Empty<string>() : project.NativeLibs)
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
                return outcome;
            }

            // ---- Engine framework auto-link ----
            // A mixed/BasicLang game that calls Framework_* engine functions but declares
            // no explicit <NativeLib> still needs the engine import library linked (and its
            // DLLs deployed). Only kicks in when codegen actually emitted framework calls.
            // Skipped for IntelliSense for the same reason as the block above (and its
            // BL6009 is precisely this repo's known env .lib gap, on the flagship project
            // type).
            if (!forIntelliSense && split != null && split.UsesFramework && engineLib == null)
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
                    return outcome;
                }
            }
            outcome.EngineLib = engineLib;
            outcome.Request = request;

            // ---- compile_commands.json ----
            // Advisory for a BUILD (a compile that succeeds is a success even if the editor
            // hint could not be written), but it IS the deliverable for IntelliSense — so
            // there, a write failure is a real failure rather than a warning nobody reads.
            try
            {
                var ccPath = CompileCommandsWriter.Write(projectDir, kind, driver, request);
                result.Messages.Add($"Compilation database: {ccPath}");
            }
            catch (Exception ex)
            {
                if (forIntelliSense)
                {
                    Fail(result, "BL6006", "Failed to write compilation database: " + ex.Message,
                        project.FilePath);
                    return outcome;
                }
                result.Messages.Add($"warning: could not write compilation database: {ex.Message}");
            }

            outcome.Completed = true;
            return outcome;
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

        /// <summary>
        /// Lazily created, process-wide. See <see cref="RestorePackagesForClosure"/> for why it is
        /// shared and why it must not be created eagerly.
        /// </summary>
        private static readonly Lazy<PackageManager> SharedPackageManager = new(() => new PackageManager());

        /// <summary>
        /// Produces the <c>&lt;PackageReference&gt;</c> half of the .NET reference closure, so
        /// <see cref="NetReferenceResolver.Resolve"/> can stay a pure synchronous function.
        ///
        /// <para><b>A project with no packages does nothing at all</b> — no PackageManager is
        /// constructed, no <c>obj/</c> is created, no console line is written, no network call is
        /// made. That guard is load-bearing: <see cref="PackageManager.RestoreAsync"/> writes
        /// <c>obj/project.assets.json</c> and prints progress unconditionally, so calling it
        /// blindly would change what every existing native project does on disk and on stdout.</para>
        ///
        /// <para><b>Build path.</b> Blocks on the async restore. Safe because no caller runs on a
        /// UI thread: the IDE wraps <see cref="Build"/> in <c>Task.Run</c>
        /// (BuildService.BuildInternalAsync) and the CLI is a console app with no
        /// SynchronizationContext, so there is no continuation to deadlock against.</para>
        ///
        /// <para><b>IntelliSense path.</b> Cache-only, and never blocks: it reads assemblies out
        /// of the already-restored package folder and makes no network call. Opening a project
        /// must not hang on nuget.org, and a package that has never been restored simply
        /// contributes nothing and reports nothing — the next build restores it and reports for
        /// real. A floating version ("*", a range) cannot be pinned without the network, so it is
        /// skipped here rather than guessed at.</para>
        ///
        /// <para><b>KNOWN GAP, deliberately not fixed here (Task 13's job).</b> The blocking
        /// restore has no timeout beyond <c>HttpClient</c>'s default 100 s per request, no
        /// cancellation (<see cref="Build"/> takes no <c>CancellationToken</c> yet), and its
        /// progress goes to <c>Console</c>, which the GUI Shell discards — so an IDE build of a
        /// package-declaring native project can sit silent for minutes. All three are pre-existing
        /// <see cref="PackageManager"/> properties, but THIS change is what put them on the native
        /// build path. Spec §10.3 / plan Task 13 threads a <c>CancellationToken</c> through
        /// <see cref="Build"/> and is the right home for the fix.</para>
        ///
        /// <para><c>internal</c> rather than private so tests can pin all four behaviors directly
        /// — the same seam-for-testability precedent as <see cref="ApplyCompileOutcome"/>.</para>
        /// </summary>
        internal static (IReadOnlyList<string> Assemblies, IReadOnlyList<string> Errors)
            RestorePackagesForClosure(ProjectFile project, string configuration, bool forIntelliSense)
        {
            if (project.PackageReferences.Count == 0)
                return (Array.Empty<string>(), Array.Empty<string>());

            // One PackageManager per process, created on first USE (so a project that declares no
            // packages never triggers its ctor, which allocates a never-disposed HttpClient and
            // does Directory.CreateDirectory(~/.basiclang/packages)). Sharing it also stops a
            // long-lived IDE from leaking a fresh HttpClient + handler on every debounced
            // IntelliSense emission. GetPackageAssemblies is a pure path walk but is an INSTANCE
            // method reading _globalPackagesFolder, so it cannot be reached without one — and
            // PackageManager is shared with the C# backend, so its shape is not ours to change.
            var manager = SharedPackageManager.Value;

            if (!forIntelliSense)
            {
                var restore = manager.RestoreAsync(project, configuration).GetAwaiter().GetResult();
                return (restore.ResolvedAssemblies, restore.Errors);
            }

            var cached = new List<string>();
            foreach (var package in project.PackageReferences)
            {
                var version = package.Version;
                if (string.IsNullOrEmpty(version) || version == "*"
                    || version.StartsWith("[") || version.StartsWith("("))
                    continue;
                cached.AddRange(manager.GetPackageAssemblies(package.Name, version, project.TargetFramework));
            }
            return (cached, Array.Empty<string>());
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
        /// method — the shared <see cref="CppCodeGenerator.IsClassMethod"/> makes exactly
        /// the same distinction the free-function codegen passes use. Counting per-unit
        /// (rather than from the combined IR) is deliberate: the IR combiner keeps only
        /// the first Sub Main across files, so the combined module undercounts a
        /// duplicate-entry error.
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
                    if (CppCodeGenerator.IsClassMethod(fn, module)) continue;
                    count++;
                }
            }
            return count;
        }

        // Filename-safe token for GenerateSplit's precondition: keep [A-Za-z0-9_],
        // replace everything else with '_', fall back to "Program" when empty. The
        // generated aggregate-header basename (<safe>.g.h) intentionally diverges from the
        // unsanitized exe name — users #include the per-module shim (<Module>.g.h), not the
        // aggregate, so the sanitized project token never surfaces in user-facing code.
        private static string SanitizeProjectName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Program";
            var chars = name.Select(ch =>
                (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') || ch == '_' ? ch : '_').ToArray();
            var safe = new string(chars);
            return string.IsNullOrEmpty(safe) ? "Program" : safe;
        }

        /// <summary>
        /// Deletes stale generated files so a renamed/removed module's old .g.cpp/.g.h can
        /// never be picked up by a later compile, and returns the ones it could NOT delete.
        ///
        /// Deletion stays best-effort ON PURPOSE: any process may hold a transient handle on
        /// a generated file — an indexer, antivirus, a concurrent build, an open editor —
        /// and promoting that to a hard BL6006 would let an unrelated process break the
        /// build. But a skipped file must not be SILENT either: a stale one that survives
        /// goes on to be compiled. The caller surfaces each failure as a warning.
        /// </summary>
        private static List<string> CleanGeneratedDir(string objGenDir)
        {
            var failed = new List<string>();
            if (!Directory.Exists(objGenDir)) return failed;
            foreach (var file in Directory.EnumerateFiles(objGenDir)
                .Where(f => f.EndsWith(".g.cpp", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".g.h", StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(file); }
                catch { failed.Add(file); }
            }
            return failed;
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
