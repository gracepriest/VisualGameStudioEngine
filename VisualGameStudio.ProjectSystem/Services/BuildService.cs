using System.Diagnostics;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using MSBuildText = BasicLang.Compiler.ProjectSystem.MSBuildText;
using CppToolchain = BasicLang.Compiler.ProjectSystem.CppToolchain;

namespace VisualGameStudio.ProjectSystem.Services;

public class BuildService : IBuildService
{
    private readonly IOutputService _outputService;
    private readonly ProjectSerializer _projectSerializer;
    private readonly CppToolchainOverrides _overrides;
    private readonly Func<string, CppToolchain> _pathResolve;
    private readonly Func<CppToolchain> _pathFind;
    private CancellationTokenSource? _buildCts;
    private string _currentConfigurationName = "Debug";

    public bool IsBuilding { get; private set; }

    public BuildConfiguration CurrentConfiguration
    {
        get
        {
            return new BuildConfiguration { Name = _currentConfigurationName };
        }
        set
        {
            _currentConfigurationName = value.Name;
        }
    }

    public event EventHandler<BuildProgressEventArgs>? BuildProgress;
    public event EventHandler<BuildCompletedEventArgs>? BuildCompleted;
    public event EventHandler? BuildStarted;
    public event EventHandler? BuildCancelled;

    public BuildService(IOutputService outputService)
        : this(outputService, new ProjectSerializer())
    {
    }

    /// <summary>Convenience ctor for callers/tests that don't exercise per-backend
    /// compiler overrides: a <see cref="CppToolchainOverrides"/> over a null settings
    /// service resolves every backend to <see cref="OverrideState.None"/>, i.e. today's
    /// plain machine-probe behavior.</summary>
    public BuildService(IOutputService outputService, ProjectSerializer projectSerializer)
        : this(outputService, projectSerializer, new CppToolchainOverrides(null))
    {
    }

    /// <summary>
    /// Primary ctor. MUST be reached via the FACTORY registration in
    /// ServiceConfiguration.cs, not a by-type <c>AddSingleton&lt;IBuildService,
    /// BuildService&gt;()</c> — <see cref="ProjectSerializer"/> is not itself
    /// container-registered, so MS DI would silently fall back to the 1-arg convenience
    /// ctor above and leave <see cref="_overrides"/> a permanently-empty reader.
    /// <paramref name="pathResolve"/>/<paramref name="pathFind"/> are test-only seams over
    /// the machine-PATH probes (<see cref="CppToolchain.TryFindById"/> /
    /// <see cref="CppToolchain.Find"/>) so override-precedence tests can fake "X on PATH"
    /// without a real installed toolchain.
    /// </summary>
    public BuildService(IOutputService outputService, ProjectSerializer projectSerializer,
        CppToolchainOverrides overrides,
        Func<string, CppToolchain>? pathResolve = null,
        Func<CppToolchain>? pathFind = null)
    {
        _outputService = outputService;
        _projectSerializer = projectSerializer;
        _overrides = overrides;
        _pathResolve = pathResolve ?? CppToolchain.TryFindById;
        _pathFind = pathFind ?? CppToolchain.Find;
    }

    public async Task<BuildResult> BuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default)
    {
        return await BuildInternalAsync(project, false, cancellationToken);
    }

    public async Task<BuildResult> BuildSolutionAsync(BasicLangSolution solution, CancellationToken cancellationToken = default)
    {
        if (IsBuilding)
        {
            return new BuildResult
            {
                Success = false,
                Diagnostics = new List<DiagnosticItem>
                {
                    new() { Message = "A build is already in progress", Severity = DiagnosticSeverity.Error }
                }
            };
        }

        var combinedResult = new BuildResult();
        var stopwatch = Stopwatch.StartNew();
        var succeeded = 0;
        var failed = 0;

        try
        {
            IsBuilding = true;
            _buildCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            BuildStarted?.Invoke(this, EventArgs.Empty);
            _outputService.Clear(OutputCategory.Build);

            // Compute build order via topological sort
            List<SolutionProject> buildOrder;
            try
            {
                buildOrder = GetBuildOrder(solution);
            }
            catch (InvalidOperationException ex)
            {
                combinedResult.Success = false;
                combinedResult.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL0010",
                    Message = ex.Message,
                    Severity = DiagnosticSeverity.Error
                });
                _outputService.WriteError(ex.Message, OutputCategory.Build);
                return combinedResult;
            }

            _outputService.WriteLine($"========== Building Solution: {solution.SolutionName} ({buildOrder.Count} project(s)) ==========", OutputCategory.Build);
            _outputService.WriteLine($"Configuration: {_currentConfigurationName}", OutputCategory.Build);
            _outputService.WriteLine("", OutputCategory.Build);

            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Building solution...", 0));

            for (int i = 0; i < buildOrder.Count; i++)
            {
                _buildCts.Token.ThrowIfCancellationRequested();

                var project = buildOrder[i];
                _outputService.WriteLine($"--- Building: {project.Name} ({i + 1} of {buildOrder.Count}) ---", OutputCategory.Build);

                var projectPath = project.GetFullPath(solution.SolutionDirectory);
                var percent = (int)((double)i / buildOrder.Count * 100);
                BuildProgress?.Invoke(this, new BuildProgressEventArgs(
                    $"Building {project.Name}...", percent, projectPath));

                // Load and build the project
                BasicLangProject? loadedProject = null;
                try
                {
                    loadedProject = await _projectSerializer.LoadAsync(projectPath, cancellationToken: _buildCts.Token);
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"Failed to load project {project.Name}: {ex.Message}", OutputCategory.Build);
                }

                if (loadedProject != null)
                {
                    // CompileProjectCoreAsync (not BuildInternalAsync) — BuildInternalAsync's
                    // OWN guard ("if (IsBuilding) return failure") would otherwise short-circuit
                    // every project here, since IsBuilding was already set true for the whole
                    // solution above. The core compiles without touching IsBuilding/_buildCts/
                    // BuildStarted/Clear/BuildCompleted — this loop owns the solution-wide ceremony.
                    var projectResult = await CompileProjectCoreAsync(loadedProject, false, _buildCts.Token);

                    // Merge diagnostics into combined result
                    combinedResult.Diagnostics.AddRange(projectResult.Diagnostics);

                    if (projectResult.Success)
                    {
                        succeeded++;
                        _outputService.WriteLine($"  {project.Name} succeeded.", OutputCategory.Build);
                    }
                    else
                    {
                        failed++;
                        _outputService.WriteError($"  {project.Name} failed.", OutputCategory.Build);
                        break; // Stop on first failure
                    }
                }
                else
                {
                    failed++;
                    combinedResult.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL0011",
                        Message = $"Could not load project: {project.Name} ({projectPath})",
                        Severity = DiagnosticSeverity.Error
                    });
                    break;
                }
            }

            stopwatch.Stop();
            combinedResult.Duration = stopwatch.Elapsed;
            combinedResult.Success = failed == 0;

            _outputService.WriteLine("", OutputCategory.Build);
            _outputService.WriteLine($"========== Build: {succeeded} succeeded, {failed} failed ==========", OutputCategory.Build);
            _outputService.WriteLine($"    {combinedResult.ErrorCount} Error(s), {combinedResult.WarningCount} Warning(s)", OutputCategory.Build);
            _outputService.WriteLine($"    Time: {combinedResult.Duration.TotalSeconds:F2}s", OutputCategory.Build);

            BuildProgress?.Invoke(this, new BuildProgressEventArgs(
                combinedResult.Success ? "Solution build succeeded" : "Solution build failed",
                100));

            BuildCompleted?.Invoke(this, new BuildCompletedEventArgs(combinedResult, combinedResult.Duration));
        }
        catch (OperationCanceledException)
        {
            combinedResult.Success = false;
            combinedResult.Diagnostics.Add(new DiagnosticItem
            {
                Message = "Build was cancelled",
                Severity = DiagnosticSeverity.Warning
            });
            _outputService.WriteLine("Build cancelled by user.", OutputCategory.Build);
        }
        catch (Exception ex)
        {
            combinedResult.Success = false;
            combinedResult.Diagnostics.Add(new DiagnosticItem
            {
                Id = "BL9999",
                Message = $"Solution build failed with exception: {ex.Message}",
                Severity = DiagnosticSeverity.Error
            });
            _outputService.WriteError($"Build error: {ex.Message}", OutputCategory.Build);
        }
        finally
        {
            IsBuilding = false;
            _buildCts?.Dispose();
            _buildCts = null;
        }

        return combinedResult;
    }

    /// <summary>
    /// Computes a topological build order using Kahn's algorithm.
    /// Projects with no dependencies are built first.
    /// </summary>
    private List<SolutionProject> GetBuildOrder(BasicLangSolution solution)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in solution.Projects)
        {
            inDegree[p.Name] = 0;
            adjacency[p.Name] = new List<string>();
        }

        foreach (var p in solution.Projects)
        {
            foreach (var dep in p.ProjectReferences)
            {
                if (adjacency.ContainsKey(dep))
                {
                    adjacency[dep].Add(p.Name);
                    inDegree[p.Name]++;
                }
            }
        }

        var queue = new Queue<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<SolutionProject>();

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            var project = solution.GetProject(name);
            if (project != null)
                result.Add(project);

            foreach (var dependent in adjacency[name])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != solution.Projects.Count)
            throw new InvalidOperationException("Circular dependency detected in project references");

        return result;
    }

    public async Task<BuildResult> RebuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default)
    {
        await CleanAsync(project, cancellationToken);
        return await BuildInternalAsync(project, false, cancellationToken);
    }

    public Task CancelBuildAsync()
    {
        if (IsBuilding && _buildCts != null)
        {
            try { _buildCts.Cancel(); }
            catch (ObjectDisposedException) { }
            BuildCancelled?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    public async Task CleanAsync(BasicLangProject project, CancellationToken cancellationToken = default)
    {
        _outputService.WriteLine("Cleaning project...", OutputCategory.Build);

        foreach (var config in project.Configurations.Values)
        {
            var outputDir = Path.Combine(project.ProjectDirectory, config.OutputPath);
            if (Directory.Exists(outputDir))
            {
                try
                {
                    Directory.Delete(outputDir, true);
                    _outputService.WriteLine($"Deleted: {outputDir}", OutputCategory.Build);
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"Failed to delete {outputDir}: {ex.Message}", OutputCategory.Build);
                }
            }
        }

        _outputService.WriteLine("Clean completed.", OutputCategory.Build);
        await Task.CompletedTask;
    }

    private async Task<BuildResult> BuildInternalAsync(BasicLangProject project, bool rebuild, CancellationToken cancellationToken = default)
    {
        if (IsBuilding)
        {
            return new BuildResult
            {
                Success = false,
                Diagnostics = new List<DiagnosticItem>
                {
                    new() { Message = "A build is already in progress", Severity = DiagnosticSeverity.Error }
                }
            };
        }

        var result = new BuildResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            IsBuilding = true;
            _buildCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            BuildStarted?.Invoke(this, EventArgs.Empty);
            _outputService.Clear(OutputCategory.Build);
            _outputService.WriteLine($"========== Build Started: {project.Name} ==========", OutputCategory.Build);
            _outputService.WriteLine($"Configuration: {_currentConfigurationName}", OutputCategory.Build);
            _outputService.WriteLine($"Backend: {project.TargetBackend}", OutputCategory.Build);
            _outputService.WriteLine("", OutputCategory.Build);

            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Starting build...", 0));

            // The guard-free compile CORE (dispatch + exception mapping) is shared
            // with BuildSolutionAsync's per-project loop — see CompileProjectCoreAsync.
            result = await CompileProjectCoreAsync(project, rebuild, _buildCts.Token);

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            // Log results
            _outputService.WriteLine("", OutputCategory.Build);
            if (result.Success)
            {
                _outputService.WriteLine($"========== Build Succeeded ==========", OutputCategory.Build);
            }
            else
            {
                _outputService.WriteLine($"========== Build Failed ==========", OutputCategory.Build);
            }
            _outputService.WriteLine($"    {result.ErrorCount} Error(s), {result.WarningCount} Warning(s)", OutputCategory.Build);
            _outputService.WriteLine($"    Time: {result.Duration.TotalSeconds:F2}s", OutputCategory.Build);

            BuildProgress?.Invoke(this, new BuildProgressEventArgs(
                result.Success ? "Build succeeded" : "Build failed",
                100));

            BuildCompleted?.Invoke(this, new BuildCompletedEventArgs(result, result.Duration));
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Diagnostics.Add(new DiagnosticItem
            {
                Message = "Build was cancelled",
                Severity = DiagnosticSeverity.Warning
            });
            _outputService.WriteLine("Build cancelled by user.", OutputCategory.Build);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Diagnostics.Add(new DiagnosticItem
            {
                Id = "BL9999",
                Message = $"Build failed with exception: {ex.Message}",
                Severity = DiagnosticSeverity.Error
            });
            _outputService.WriteError($"Build error: {ex.Message}", OutputCategory.Build);
        }
        finally
        {
            IsBuilding = false;
            _buildCts?.Dispose();
            _buildCts = null;
        }

        return result;
    }

    /// <summary>
    /// The guard-free compile CORE: dispatches to the native builder or the managed
    /// compiler engine and maps non-cancellation exceptions to a failed
    /// <see cref="BuildResult"/>. A user-initiated <see cref="OperationCanceledException"/>
    /// is deliberately NOT mapped to a failed result — <see cref="BuildResult"/> has no
    /// Cancelled flag, so doing so would make a deliberate cancel indistinguishable from
    /// a real failure to callers (quiet-build toasts/announcements). It is re-thrown so
    /// the caller's own outer catch (which predates this core and already handles
    /// cancellation quietly, without firing <see cref="BuildCompleted"/>) takes over.
    /// Deliberately contains NONE of the build-SESSION ceremony (<see cref="IsBuilding"/>,
    /// <see cref="_buildCts"/>, <see cref="BuildStarted"/>, output-clearing, the per-build
    /// banner/logging, or <see cref="BuildCompleted"/>) — that ceremony is meaningful once
    /// per build GESTURE, not once per project. <see cref="BuildInternalAsync"/> (single-project
    /// builds) wraps this with that ceremony; <see cref="BuildSolutionAsync"/> calls this
    /// DIRECTLY per project so an N-project solution keeps ONE solution-level ceremony
    /// instead of tripping over BuildInternalAsync's own IsBuilding guard (which
    /// BuildSolutionAsync has already set true for the whole solution).
    /// </summary>
    private async Task<BuildResult> CompileProjectCoreAsync(BasicLangProject project, bool rebuild, CancellationToken cancellationToken)
    {
        var result = new BuildResult();

        try
        {
            // ---------- Native projects: route to the shared native builder ----------
            // Both Language=Cpp (hand-written C++) and a BasicLang project on the
            // C++ backend (TargetBackend=Cpp, transpiled to native) build through
            // the SAME CppProjectBuilder the CLI uses — one native-project path, no
            // IDE-side reimplementation (the CLI mirror is ProjectFile.IsNativeProject).
            if (project.IsNativeBuild)
            {
                // BuildCppProject never throws (exceptions map to a failed result)
                // so execution ALWAYS falls through to the shared finalization
                // in the caller — BuildCompleted is the Error List's only diagnostics feed.
                result = await Task.Run(() => BuildCppProject(project), cancellationToken);
            }
            else
            {
                // Get source files
                var sourceFiles = project.GetSourceFiles().ToList();
                if (!sourceFiles.Any())
                {
                    result.Success = false;
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL0001",
                        Message = "No source files found in project",
                        Severity = DiagnosticSeverity.Error
                    });
                    return result;
                }

                // Delegate to the real BasicLang compiler engine (same as `BasicLang.exe build`)
                result = await CompileWithBasicLangApiAsync(project, sourceFiles, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Do NOT map this to a failed BuildResult: a user-initiated cancel is not
            // a build failure, and this core has no way to tell the caller "cancelled"
            // apart from "failed" (BuildResult carries no Cancelled flag). Re-throwing
            // (mirroring CompileWithBasicLangApiAsync's own catch) lets it escape to
            // BuildInternalAsync's / BuildSolutionAsync's outer catch — both already
            // handle it quietly (Warning diagnostic + "Build cancelled by user.",
            // deliberately WITHOUT firing BuildCompleted) so a deliberate cancel never
            // surfaces as a "Build failed" toast/announcement.
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Diagnostics.Add(new DiagnosticItem
            {
                Id = "BL9999",
                Message = $"Build failed with exception: {ex.Message}",
                Severity = DiagnosticSeverity.Error
            });
            _outputService.WriteError($"Build error: {ex.Message}", OutputCategory.Build);
        }

        return result;
    }

    /// <summary>
    /// Compiles the project with the SAME engine the CLI uses
    /// (<see cref="BasicCompiler.CompileProjectFiles"/>), mirroring
    /// BasicLang/Program.cs's `build` command: restore NuGet packages, compile all
    /// project files as one program (.mod/.cls implicit wrapping, sibling imports,
    /// combined IR, optimization all handled by the compiler), generate
    /// backend-appropriate output, and — for the C# backend — run `dotnet build`
    /// with CLI-parity csproj generation and game-engine deployment.
    /// </summary>
    private async Task<BuildResult> CompileWithBasicLangApiAsync(
        BasicLangProject project,
        List<ProjectItem> sourceFiles,
        CancellationToken cancellationToken)
    {
        var result = new BuildResult();
        var config = project.GetConfiguration(_currentConfigurationName);

        try
        {
            // ---------- Resolve source files ----------
            var absoluteSourcePaths = new List<string>();
            foreach (var sourceFile in sourceFiles)
            {
                var filePath = Path.Combine(project.ProjectDirectory, sourceFile.Include);
                if (!File.Exists(filePath))
                {
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL0002",
                        Message = $"Source file not found: {sourceFile.Include}",
                        FilePath = filePath,
                        Severity = DiagnosticSeverity.Error
                    });
                    _outputService.WriteError($"Source file not found: {sourceFile.Include}", OutputCategory.Build);
                    continue;
                }

                absoluteSourcePaths.Add(filePath);
            }

            if (result.ErrorCount > 0)
            {
                result.Success = false;
                return result;
            }

            // The CLI project model (BasicLang.Compiler.ProjectSystem.ProjectFile)
            // carries data the IDE model does not: PackageReferences, assembly
            // references, UseWindowsForms/UseWPF, TargetFramework. Load it from the
            // same .blproj for parity with `BasicLang.exe build`.
            var cliProject = TryLoadCliProject(project.FilePath);

            // ---------- Phase 1: restore NuGet packages (CLI parity) ----------
            var restoredAssemblies = new List<string>();
            if (cliProject != null && cliProject.PackageReferences.Count > 0)
            {
                _outputService.WriteLine($"Restoring {cliProject.PackageReferences.Count} NuGet package(s)...", OutputCategory.Build);
                BuildProgress?.Invoke(this, new BuildProgressEventArgs("Restoring packages...", 5));

                var packageManager = new BasicLang.Compiler.ProjectSystem.PackageManager();
                var restoreResult = await packageManager.RestoreAsync(cliProject, config.Name);

                foreach (var error in restoreResult.Errors)
                {
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL0020",
                        Message = $"Package restore failed: {error}",
                        FilePath = project.FilePath,
                        Severity = DiagnosticSeverity.Error
                    });
                    _outputService.WriteError($"  Package restore: {error}", OutputCategory.Build);
                }

                if (!restoreResult.Success)
                {
                    result.Success = false;
                    return result;
                }

                foreach (var package in restoreResult.RestoredPackages)
                {
                    _outputService.WriteLine($"  Restored {package.Name} {package.Version}", OutputCategory.Build);
                }

                restoredAssemblies.AddRange(restoreResult.ResolvedAssemblies);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ---------- Phase 2: compile with the real compiler engine ----------
            _outputService.WriteLine($"Compiling {absoluteSourcePaths.Count} file(s)...", OutputCategory.Build);
            foreach (var path in absoluteSourcePaths)
            {
                _outputService.WriteLine($"  Compiling {Path.GetFileName(path)}...", OutputCategory.Build);
            }
            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Parsing and analyzing sources...", 20));

            var backend = GetBackendId(project.TargetBackend);
            var outputDir = Path.Combine(project.ProjectDirectory, config.OutputPath);

            var compilerOptions = new CompilerOptions
            {
                TargetBackend = backend,
                OutputPath = outputDir,
                OptimizeAggressive = config.Optimize
            };

            // Resolved package assemblies join the compiler search paths (CLI parity)
            foreach (var assembly in restoredAssemblies)
            {
                var dir = Path.GetDirectoryName(assembly);
                if (!string.IsNullOrEmpty(dir) && !compilerOptions.SearchPaths.Contains(dir))
                {
                    compilerOptions.SearchPaths.Add(dir);
                }
            }

            // Compile all project files as ONE program: the compiler handles
            // .mod/.cls preprocessing, compile order, implicit sibling imports,
            // combined IR and optimization internally — exactly like the CLI.
            var compiler = new BasicCompiler(compilerOptions);
            var compilation = await Task.Run(() => compiler.CompileProjectFiles(absoluteSourcePaths), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            MapCompilerDiagnostics(compiler, compilation, result);

            if (!compilation.Success || compilation.CombinedIR == null)
            {
                result.Success = false;
                if (result.ErrorCount == 0)
                {
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL9998",
                        Message = "Compilation failed but the compiler reported no diagnostics",
                        FilePath = project.FilePath,
                        Severity = DiagnosticSeverity.Error
                    });
                }
                return result;
            }

            // ---------- Phase 3: backend code generation (CLI parity) ----------
            BuildProgress?.Invoke(this, new BuildProgressEventArgs($"Generating {backend} code...", 60));
            _outputService.WriteLine($"Generating {backend} output...", OutputCategory.Build);

            string generatedCode;
            string extension;
            try
            {
                switch (backend)
                {
                    // No "cpp" case: native projects (Language=Cpp OR TargetBackend=Cpp)
                    // route to BuildCppProject/CppProjectBuilder above and never reach
                    // this managed codegen switch.
                    case "llvm":
                        generatedCode = new BasicLang.Compiler.CodeGen.LLVM.LLVMCodeGenerator().Generate(compilation.CombinedIR);
                        extension = ".ll";
                        break;
                    case "msil":
                        generatedCode = new BasicLang.Compiler.CodeGen.MSIL.MSILCodeGenerator().Generate(compilation.CombinedIR);
                        extension = ".il";
                        break;
                    default: // csharp
                        generatedCode = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(compilation.CombinedIR);
                        extension = ".cs";
                        break;
                }
            }
            catch (Exception genEx)
            {
                result.Success = false;
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL4001",
                    Message = $"Code generation error: {genEx.Message}",
                    FilePath = project.FilePath,
                    Severity = DiagnosticSeverity.Error
                });
                _outputService.WriteError($"  Code generation error: {genEx.Message}", OutputCategory.Build);
                return result;
            }

            result.GeneratedCode = generatedCode;
            result.GeneratedFileName = $"{project.Name}{extension}";

            Directory.CreateDirectory(outputDir);
            result.OutputPath = outputDir;

            var generatedFilePath = Path.Combine(outputDir, result.GeneratedFileName);
            await File.WriteAllTextAsync(generatedFilePath, generatedCode, cancellationToken);
            _outputService.WriteLine($"Generated: {generatedFilePath}", OutputCategory.Build);

            // ---------- Other non-.NET backends stop at source ----------
            if (backend != "csharp")
            {
                var toolchainHint = backend switch
                {
                    "llvm" => $"Generated LLVM IR: {generatedFilePath} — compile it with the LLVM toolchain (llc/clang).",
                    "msil" => $"Generated MSIL: {generatedFilePath} — assemble it with ilasm.",
                    _ => $"Generated output: {generatedFilePath}."
                };
                _outputService.WriteLine(toolchainHint, OutputCategory.Build);
                _outputService.WriteLine($"Output directory: {outputDir}", OutputCategory.Build);

                BuildProgress?.Invoke(this, new BuildProgressEventArgs("Output generated", 90));
                result.Success = true;
                return result;
            }

            // ---------- Phase 4: C# backend — csproj + dotnet build (CLI parity) ----------
            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Generating output...", 75));

            // Assembly references: the .blproj's explicit references, plus the
            // auto-injected game-engine wrapper when the program uses the engine
            // (hint-pathed next to the IDE/compiler binaries), exactly like the CLI.
            var assemblyReferences = cliProject != null
                ? new List<BasicLang.Compiler.ProjectSystem.AssemblyReference>(cliProject.AssemblyReferences)
                : project.References
                    .Where(r => !r.IsProjectReference)
                    .Select(r => new BasicLang.Compiler.ProjectSystem.AssemblyReference { Name = r.Name, HintPath = r.Path })
                    .ToList();

            var usesEngine = BasicLang.Compiler.ProjectSystem.EngineDeployment.UsesEngine(generatedCode);
            var engineBaseDir = FindEngineBaseDir();

            if (usesEngine && !assemblyReferences.Any(BasicLang.Compiler.ProjectSystem.EngineDeployment.IsWrapperReference))
            {
                if (engineBaseDir != null)
                {
                    assemblyReferences.Add(BasicLang.Compiler.ProjectSystem.EngineDeployment.GetEngineReference(engineBaseDir));
                    _outputService.WriteLine($"Added engine reference: {BasicLang.Compiler.ProjectSystem.EngineDeployment.WrapperDllName} ({engineBaseDir})", OutputCategory.Build);
                }
                else
                {
                    // Without the wrapper the C# build will fail on the engine types —
                    // say why up front instead of leaving a cryptic CS0246/MSB3245.
                    var warning = $"This program uses the game engine, but {BasicLang.Compiler.ProjectSystem.EngineDeployment.WrapperDllName} was not found next to the IDE binaries. Install the engine runtime beside the IDE or add an explicit assembly reference to the project.";
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL0030",
                        Message = warning,
                        FilePath = project.FilePath,
                        Severity = DiagnosticSeverity.Warning
                    });
                    _outputService.WriteError($"Warning: {warning}", OutputCategory.Build);
                }
            }

            var csprojPath = Path.Combine(outputDir, $"{project.Name}.csproj");
            var csprojContent = GenerateCsprojContent(
                Path.GetFileName(generatedFilePath), project, cliProject, generatedCode, assemblyReferences);
            await File.WriteAllTextAsync(csprojPath, csprojContent, cancellationToken);
            _outputService.WriteLine($"Generated csproj: {csprojPath}", OutputCategory.Build);

            // Check for library/app type mismatches and warn
            CheckForMismatchWarnings(generatedCode, project);

            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Compiling C#...", 85));

            var (exitCode, buildOutput) = await RunDotnetBuildAsync(csprojPath, config.Name, outputDir, cancellationToken);

            if (exitCode != 0)
            {
                result.Success = false;
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL5001",
                    Message = $"C# compilation failed: {buildOutput}",
                    Severity = DiagnosticSeverity.Error
                });
                _outputService.WriteLine($"Build error: {buildOutput}", OutputCategory.Build);
            }
            else
            {
                result.Success = true;

                // Find the executable (directly in the output directory)
                var exePath = Path.Combine(outputDir, $"{project.Name}.exe");
                if (File.Exists(exePath))
                {
                    result.ExecutablePath = exePath;
                    _outputService.WriteLine($"Executable: {exePath}", OutputCategory.Build);
                }

                // Build succeeded and the program uses the engine: copy the native
                // engine DLL(s) next to the built game so the wrapper's P/Invoke
                // resolves at runtime (they are not managed references, so the
                // build won't copy them for us) — same as the CLI.
                if (usesEngine)
                {
                    DeployNativeEngine(outputDir);
                }
            }

            _outputService.WriteLine($"Output directory: {outputDir}", OutputCategory.Build);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Diagnostics.Add(new DiagnosticItem
            {
                Id = "BL9998",
                Message = $"Internal compiler error: {ex.Message}",
                Severity = DiagnosticSeverity.Error
            });
        }

        return result;
    }

    /// <summary>Maps the IDE's backend enum to the compiler's backend identifier.</summary>
    private static string GetBackendId(TargetBackend backend) => backend switch
    {
        TargetBackend.Cpp => "cpp",
        TargetBackend.LLVM => "llvm",
        TargetBackend.MSIL => "msil",
        _ => "csharp"
    };

    /// <summary>
    /// Loads the CLI's project model from the .blproj (PackageReferences, assembly
    /// references, UseWindowsForms/UseWPF, TargetFramework). Returns null when the
    /// project only exists in memory or the file cannot be parsed by the CLI loader.
    /// </summary>
    private BasicLang.Compiler.ProjectSystem.ProjectFile? TryLoadCliProject(string projectFilePath)
    {
        try
        {
            if (string.IsNullOrEmpty(projectFilePath) || !File.Exists(projectFilePath))
                return null;
            return BasicLang.Compiler.ProjectSystem.ProjectFile.Load(projectFilePath);
        }
        catch (Exception ex)
        {
            _outputService.WriteLine($"Note: could not read extended project settings from {Path.GetFileName(projectFilePath)}: {ex.Message}", OutputCategory.Build);
            return null;
        }
    }

    /// <summary>
    /// Maps compiler errors/warnings into Error List diagnostics with the best
    /// file attribution available:
    /// - errors recorded on a compilation unit carry that unit's file path;
    /// - unattributed errors (parse failures live only in AllErrors) fall back to
    ///   the single failed unit when it is unambiguous, otherwise no file path.
    /// </summary>
    private void MapCompilerDiagnostics(BasicCompiler compiler, CompilationResult compilation, BuildResult result)
    {
        // Registry.Modules also has units when compilation aborted before
        // CompilationResult.Units was populated (e.g. parse errors).
        var units = compiler.Registry.Modules.ToList();

        var attributed = new HashSet<SemanticError>(ReferenceEqualityComparer.Instance);
        var attributedList = new List<SemanticError>();
        foreach (var unit in units)
        {
            foreach (var error in unit.Errors)
            {
                if (attributed.Add(error))
                {
                    AddCompilerDiagnostic(result, error, unit.FilePath);
                    attributedList.Add(error);
                }
            }
        }

        // A genuine orphan is an AllErrors entry that reached no unit (parse /
        // infrastructure errors) — neither reference-attributed NOR a value-duplicate
        // of an already-attributed per-unit error. FinalizeResult -> WithInlineLocation
        // replaces each located AllErrors entry with a fresh prefixed COPY, so plain
        // reference-dedup misses it and the same semantic error is double-reported
        // (once attributed to its file, once as an orphan with the "Error at line ..."
        // double-prefix). Value-dedup by location + message-suffix suppresses that.
        var orphans = compilation.AllErrors
            .Where(e => !attributed.Contains(e) && !IsDuplicateOfAttributed(e, attributedList))
            .ToList();
        string? fallbackPath = null;
        var unattributedFailedUnits = units
            .Where(u => u.Status == CompilationStatus.Error && u.Errors.Count == 0)
            .ToList();
        if (unattributedFailedUnits.Count == 1)
        {
            fallbackPath = unattributedFailedUnits[0].FilePath;
        }

        foreach (var error in orphans)
        {
            AddCompilerDiagnostic(result, error, fallbackPath);
        }
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

    private void AddCompilerDiagnostic(BuildResult result, SemanticError error, string? filePath)
    {
        var isError = error.Severity == ErrorSeverity.Error;
        var item = new DiagnosticItem
        {
            Id = string.IsNullOrEmpty(error.ErrorCode) ? (isError ? "BL3001" : "BL3002") : error.ErrorCode,
            Message = error.Message,
            FilePath = filePath,
            Line = error.Line,
            Column = error.Column,
            Severity = isError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning
        };
        result.Diagnostics.Add(item);

        var location = filePath != null ? $"{filePath}({error.Line},{error.Column})" : $"({error.Line},{error.Column})";
        var kind = isError ? "error" : "warning";
        _outputService.WriteLine($"  {location}: {kind} {item.Id}: {error.Message}", OutputCategory.Build);
    }

    /// <summary>
    /// Generates the temporary .csproj for the C# backend with CLI parity:
    /// TargetFramework and UseWindowsForms/UseWPF from the .blproj (with the
    /// net*-windows TFM adjustment), PackageReferences flowed through, and the
    /// project's assembly references (including the auto-injected engine wrapper).
    /// Output-layout properties keep the executable directly in the IDE's
    /// configured output directory.
    /// </summary>
    private string GenerateCsprojContent(
        string csFileName,
        BasicLangProject project,
        BasicLang.Compiler.ProjectSystem.ProjectFile? cliProject,
        string generatedCode,
        List<BasicLang.Compiler.ProjectSystem.AssemblyReference> assemblyReferences)
    {
        var sb = new System.Text.StringBuilder();

        var codeUpper = generatedCode.ToUpperInvariant();
        bool usesWindowsForms = codeUpper.Contains("USING SYSTEM.WINDOWS.FORMS;");
        bool usesWpf = codeUpper.Contains("USING SYSTEM.WINDOWS;") &&
                       (codeUpper.Contains("USING SYSTEM.WINDOWS.CONTROLS;") ||
                        codeUpper.Contains("USING SYSTEM.WINDOWS.MEDIA;"));
        bool usesDrawing = codeUpper.Contains("USING SYSTEM.DRAWING;");
        bool usesAspNet = codeUpper.Contains("USING MICROSOFT.ASPNETCORE;");

        string outputType = project.OutputType switch
        {
            OutputType.WinExe => "WinExe",
            OutputType.Library => "Library",
            _ => "Exe"
        };

        // UI framework flags come from the .blproj (CLI parity). Legacy projects
        // without the flags keep the old behavior: WinExe implies a UI framework
        // detected from the generated code.
        bool enableWindowsForms = cliProject?.UseWindowsForms ?? false;
        bool enableWpf = cliProject?.UseWpf ?? false;
        if (!enableWindowsForms && !enableWpf && project.OutputType == OutputType.WinExe)
        {
            enableWpf = usesWpf;
            enableWindowsForms = usesWindowsForms || !usesWpf;
        }

        // Windows desktop UI frameworks need the net*-windows TFM or the
        // WinForms/WPF types won't resolve (CLI parity).
        var targetFramework = cliProject?.TargetFramework;
        if (string.IsNullOrEmpty(targetFramework))
            targetFramework = "net8.0";
        if ((enableWindowsForms || enableWpf) && !targetFramework.Contains("-windows"))
            targetFramework += "-windows";

        string sdk = usesAspNet ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk";

        sb.AppendLine($@"<Project Sdk=""{sdk}"">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <OutputType>{outputType}</OutputType>");
        sb.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>disable</Nullable>");
        // User-controlled values are XML/MSBuild-escaped: ';' in a project name
        // splits derived item paths (MSB4094), '&' breaks the XML load (MSB4025).
        sb.AppendLine($"    <AssemblyName>{MSBuildText.EscapeValue(project.Name)}</AssemblyName>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
        sb.AppendLine("    <OutputPath>.\\</OutputPath>");

        if (enableWindowsForms)
        {
            sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
        }

        if (enableWpf)
        {
            sb.AppendLine("    <UseWPF>true</UseWPF>");
        }

        sb.AppendLine("  </PropertyGroup>");

        // Compile items
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Compile Include=\"{MSBuildText.EscapeValue(csFileName)}\" />");
        sb.AppendLine("  </ItemGroup>");

        // Assembly references (explicit .blproj references + injected engine wrapper)
        if (assemblyReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var reference in assemblyReferences)
            {
                if (!string.IsNullOrEmpty(reference.HintPath))
                {
                    sb.AppendLine($"    <Reference Include=\"{MSBuildText.EscapeValue(reference.Name)}\">");
                    sb.AppendLine($"      <HintPath>{MSBuildText.EscapeValue(reference.HintPath)}</HintPath>");
                    sb.AppendLine("    </Reference>");
                }
                else
                {
                    sb.AppendLine($"    <Reference Include=\"{MSBuildText.EscapeValue(reference.Name)}\" />");
                }
            }
            sb.AppendLine("  </ItemGroup>");
        }

        // NuGet package references from the .blproj (CLI parity), plus
        // System.Drawing.Common when the code draws without WinForms.
        var packageReferences = cliProject?.PackageReferences
            ?? new List<BasicLang.Compiler.ProjectSystem.PackageReference>();
        var needsDrawingPackage = usesDrawing && !enableWindowsForms &&
            !packageReferences.Any(p => p.Name.Equals("System.Drawing.Common", StringComparison.OrdinalIgnoreCase));

        if (packageReferences.Count > 0 || needsDrawingPackage)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var package in packageReferences)
            {
                sb.AppendLine($"    <PackageReference Include=\"{MSBuildText.EscapeValue(package.Name)}\" Version=\"{MSBuildText.EscapeValue(package.Version)}\" />");
            }
            if (needsDrawingPackage)
            {
                sb.AppendLine("    <PackageReference Include=\"System.Drawing.Common\" Version=\"8.0.0\" />");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetBuildAsync(
        string csprojPath, string configurationName, string workingDirectory, CancellationToken cancellationToken)
    {
        using var buildProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{csprojPath}\" -c {configurationName} --nologo -v q",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        buildProcess.Start();
        var stdoutTask = buildProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = buildProcess.StandardError.ReadToEndAsync();

        try
        {
            await buildProcess.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { buildProcess.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Show both stdout and stderr for better error diagnostics
        var output = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
        return (buildProcess.ExitCode, output);
    }

    /// <summary>
    /// Native C++ project build (Language=Cpp). Delegates to the SAME
    /// CppProjectBuilder the CLI uses (BasicLang.Compiler.ProjectSystem) — no
    /// IDE-side reimplementation. Never throws: any I/O or project-load failure
    /// maps to a failed BuildResult so BuildInternalAsync's shared finalization
    /// (logging + BuildCompleted, the Error List's only feed) always runs.
    /// </summary>
    private BuildResult BuildCppProject(BasicLangProject project)
    {
        var result = new BuildResult();
        var configuration = CurrentConfiguration?.Name ?? "Debug";

        _outputService.WriteLine($"Building C++ project {project.Name} [{configuration}]...", OutputCategory.Build);

        BasicLang.Compiler.ProjectSystem.CppProjectBuildResult cpp;
        try
        {
            var projectFile = BasicLang.Compiler.ProjectSystem.ProjectFile.Load(
                Path.GetFullPath(project.FilePath));

            var requestedId = projectFile.CppToolchain;

            // Pre-validate a PINNED, set-but-Invalid override before EmitCore ever runs:
            // an authoritative-when-set override must hard-fail even when that same
            // backend also happens to be on PATH — never a silent fallback.
            if (!string.IsNullOrEmpty(requestedId))
            {
                var pin = _overrides.ResolveCompiler(requestedId);
                if (pin.State == OverrideState.Invalid)
                    return FailCppBuild(result,
                        $"{requestedId} compiler path is set but not found: {pin.Message} " +
                        "Fix or clear it in Settings › C++.", projectFile.FilePath);
            }

            // Pinned: a Usable override is authoritative (FromExplicit); otherwise fall
            // through to the PATH probe seam (a set-Invalid override was already
            // hard-failed above, so only None reaches here).
            Func<string, CppToolchain> resolveById = id =>
            {
                var r = _overrides.ResolveCompiler(id);
                return r.State == OverrideState.Usable
                    ? CppToolchain.FromExplicit(id, r.ResolvedPath)
                    : _pathResolve(id);
            };

            // Unpinned: fixed precedence llvm -> gcc -> msvc. A backend is a candidate
            // when its override is set&Usable, or blank&on-PATH; a set&Invalid backend is
            // a non-candidate (skipped, never silently substituted).
            Func<CppToolchain> resolveToolchain = () =>
            {
                // Nothing configured for any backend: today's exact pre-override
                // behavior, routed through the same single-shot probe rather than
                // reimplementing it via three per-id lookups.
                if (Array.TrueForAll(CppToolchainOverrides.Backends,
                        id => _overrides.ResolveCompiler(id).State == OverrideState.None))
                    return _pathFind();

                foreach (var id in CppToolchainOverrides.Backends)       // fixed order llvm, gcc, msvc
                {
                    var r = _overrides.ResolveCompiler(id);
                    if (r.State == OverrideState.Usable) return CppToolchain.FromExplicit(id, r.ResolvedPath);
                    if (r.State == OverrideState.None && _pathResolve(id) is { } tc) return tc;
                    // Invalid -> non-candidate, skip
                }
                return null;                                             // -> BL6005 in EmitCore
            };

            cpp = BasicLang.Compiler.ProjectSystem.CppProjectBuilder.Build(
                projectFile, configuration, resolveById, probeAvailability: null, resolveToolchain);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Diagnostics.Add(new DiagnosticItem
            {
                Id = "BL6010",
                Message = $"C++ build failed: {ex.Message}",
                FilePath = project.FilePath,
                Severity = DiagnosticSeverity.Error,
                Source = "cpp"
            });
            _outputService.WriteError($"C++ build failed: {ex.Message}", OutputCategory.Build);
            return result;
        }

        foreach (var msg in cpp.Messages)
            _outputService.WriteLine(msg, OutputCategory.Build);

        foreach (var diag in cpp.Diagnostics)
        {
            result.Diagnostics.Add(new DiagnosticItem
            {
                Id = diag.Code,
                Message = diag.Message,
                FilePath = diag.FilePath,
                Line = diag.Line,
                Column = diag.Column,
                Severity = diag.IsWarning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                Source = "cpp"
            });
            // Echo in the OutputPanel's clickable format.
            _outputService.WriteLine("  " +
                BasicLang.Compiler.ProjectSystem.CppDiagnosticsParser.FormatNormalized(diag),
                OutputCategory.Build);
        }

        result.Success = cpp.Success;
        result.ExecutablePath = cpp.ExecutablePath;
        result.OutputPath = cpp.OutputPath;
        return result;
    }

    /// <summary>Records a hard C++-build failure using the SAME DiagnosticItem/BuildResult
    /// shape the rest of <see cref="BuildCppProject"/> uses (mirrors the catch block above)
    /// — a dedicated helper only for the pre-EmitCore, pinned-Invalid-override gate, not a
    /// new failure surface.</summary>
    private BuildResult FailCppBuild(BuildResult result, string message, string? filePath)
    {
        result.Diagnostics.Add(new DiagnosticItem
        {
            Id = "BL6015",
            Message = message,
            FilePath = filePath ?? "",
            Severity = DiagnosticSeverity.Error,
            Source = "cpp"
        });
        _outputService.WriteError(message, OutputCategory.Build);
        result.Success = false;
        return result;
    }

    /// <summary>
    /// Finds the directory where the game-engine wrapper (RaylibWrapper.dll)
    /// ships. The primary location is the running process's base directory (the
    /// IDE folder — same model as the CLI resolving next to BasicLang.exe), with
    /// dev-tree fallbacks for running from build output.
    /// </summary>
    private static string? FindEngineBaseDir()
    {
        foreach (var dir in CandidateEngineDirs())
        {
            if (BasicLang.Compiler.ProjectSystem.EngineDeployment.WrapperExists(dir))
                return Path.GetFullPath(dir);
        }
        return null;
    }

    private static IEnumerable<string> CandidateEngineDirs()
    {
        // Primary: next to the running binaries (IDE folder — CLI parity).
        var baseDir = AppContext.BaseDirectory;
        yield return baseDir;

        // Dev-tree fallbacks. Base dir is typically
        // <repo>/<Project>/bin/<Config>/net8.0/ (repo root is 4 levels up)
        // but historic layouts at 1 and 3 levels up are probed too.
        foreach (var levelsUp in new[] { "..", @"..\..\..", @"..\..\..\.." })
        {
            yield return Path.Combine(baseDir, levelsUp, "RaylibWrapper", "bin", "Release", "net8.0");
            yield return Path.Combine(baseDir, levelsUp, "RaylibWrapper", "bin", "Debug", "net8.0");
        }

        yield return Path.Combine(baseDir, "..", "..", "..", "..", "IDE");
    }

    /// <summary>
    /// Candidate directories for the NATIVE engine DLL (VisualGameStudioEngine.dll).
    /// It ships next to the wrapper in deployed layouts but lives in x64/<Config>
    /// in the dev tree.
    /// </summary>
    private static IEnumerable<string> CandidateNativeEngineDirs()
    {
        foreach (var dir in CandidateEngineDirs())
            yield return dir;

        var baseDir = AppContext.BaseDirectory;
        foreach (var levelsUp in new[] { "..", @"..\..\..", @"..\..\..\.." })
        {
            yield return Path.Combine(baseDir, levelsUp, "x64", "Release");
            yield return Path.Combine(baseDir, levelsUp, "x64", "Debug");
        }
    }

    /// <summary>
    /// Copies the native engine DLL(s) next to the built game (CLI parity —
    /// managed references are copied by msbuild, the native P/Invoke target is not).
    /// </summary>
    private void DeployNativeEngine(string outputDir)
    {
        var deployedAny = false;

        foreach (var dir in CandidateNativeEngineDirs())
        {
            var nativeDlls = BasicLang.Compiler.ProjectSystem.EngineDeployment.GetNativeDllPaths(dir);
            if (nativeDlls.Count == 0)
                continue;

            foreach (var nativeDll in nativeDlls)
            {
                try
                {
                    var dest = Path.Combine(outputDir, Path.GetFileName(nativeDll));
                    File.Copy(nativeDll, dest, overwrite: true);
                    _outputService.WriteLine($"Deployed engine runtime: {Path.GetFileName(nativeDll)}", OutputCategory.Build);
                }
                catch (Exception copyEx)
                {
                    _outputService.WriteError($"Warning: could not deploy {Path.GetFileName(nativeDll)}: {copyEx.Message}", OutputCategory.Build);
                }
            }

            deployedAny = true;
            break;
        }

        if (!deployedAny)
        {
            _outputService.WriteError(
                "Warning: native engine DLL (VisualGameStudioEngine.dll) not found — the game may not run until it is placed next to the executable.",
                OutputCategory.Build);
        }
    }

    /// <summary>
    /// Check for mismatches between project type and code usage, and output warnings
    /// </summary>
    private void CheckForMismatchWarnings(string generatedCode, BasicLangProject project)
    {
        var codeUpper = generatedCode.ToUpperInvariant();
        bool usesWindowsForms = codeUpper.Contains("USING SYSTEM.WINDOWS.FORMS;");
        bool usesWpf = codeUpper.Contains("USING SYSTEM.WINDOWS;") &&
                       (codeUpper.Contains("USING SYSTEM.WINDOWS.CONTROLS;") ||
                        codeUpper.Contains("USING SYSTEM.WINDOWS.MEDIA;"));
        bool usesDrawing = codeUpper.Contains("USING SYSTEM.DRAWING;");
        bool usesConsoleIO = codeUpper.Contains("CONSOLE.READLINE()") ||
                             codeUpper.Contains("CONSOLE.READKEY()") ||
                             generatedCode.Contains("Console.Write(") ||
                             generatedCode.Contains("Console.WriteLine(");

        // Console app using GUI libraries
        if (project.OutputType == OutputType.Exe)
        {
            if (usesWindowsForms)
            {
                _outputService.WriteLine("WARNING: Console app uses System.Windows.Forms - forms won't display properly.", OutputCategory.Build);
                _outputService.WriteLine("  Consider changing project type to 'Windows Forms Application'.", OutputCategory.Build);
            }
            if (usesWpf)
            {
                _outputService.WriteLine("WARNING: Console app uses WPF - windows won't display properly.", OutputCategory.Build);
                _outputService.WriteLine("  Consider changing project type to 'WPF Application'.", OutputCategory.Build);
            }
            if (usesDrawing && !usesWindowsForms)
            {
                _outputService.WriteLine("Note: Console app uses System.Drawing - this is supported.", OutputCategory.Build);
            }
        }

        // GUI app using console I/O
        if (project.OutputType == OutputType.WinExe)
        {
            if (usesConsoleIO)
            {
                _outputService.WriteLine("WARNING: GUI app uses Console I/O (ReadLine, WriteLine) - no console window available.", OutputCategory.Build);
                _outputService.WriteLine("  Use MessageBox, TextBox, or change project type to 'Console Application'.", OutputCategory.Build);
            }
        }

        // Log the project type being used
        string appType = project.OutputType switch
        {
            OutputType.WinExe => "Windows GUI Application",
            OutputType.Library => "Class Library",
            _ => "Console Application"
        };
        _outputService.WriteLine($"Building as: {appType}", OutputCategory.Build);
    }
}
