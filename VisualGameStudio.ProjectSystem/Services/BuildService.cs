using System.Diagnostics;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

public class BuildService : IBuildService
{
    private readonly IOutputService _outputService;
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
    {
        _outputService = outputService;
    }

    public async Task<BuildResult> BuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default)
    {
        return await BuildInternalAsync(project, false, cancellationToken);
    }

    public async Task<BuildResult> BuildSolutionAsync(BasicLangSolution solution, CancellationToken cancellationToken = default)
    {
        // For now, just build the startup project
        // In the future, this should build all projects in dependency order
        var result = new BuildResult { Success = true };

        foreach (var projRef in solution.Projects)
        {
            var projectPath = projRef.GetFullPath(solution.SolutionDirectory);
            // Would need to load and build each project
            _outputService.WriteLine($"Building project: {projRef.Name}", OutputCategory.Build);
        }

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
            _buildCts.Cancel();
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

            // Try to use the BasicLang compiler API directly
            result = await CompileWithBasicLangApiAsync(project, sourceFiles, _buildCts.Token);

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

    private async Task<BuildResult> CompileWithBasicLangApiAsync(
        BasicLangProject project,
        List<ProjectItem> sourceFiles,
        CancellationToken cancellationToken)
    {
        var result = new BuildResult();
        var config = project.GetConfiguration(_currentConfigurationName);

        try
        {
            int totalFiles = sourceFiles.Count;
            int processedFiles = 0;

            // ========== PHASE 1: Parse all files and collect symbols ==========
            _outputService.WriteLine($"Phase 1: Parsing {totalFiles} file(s)...", OutputCategory.Build);
            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Parsing source files...", 10));

            var compilationUnits = new List<CompilationUnit>();
            var projectSymbolTable = new ProjectSymbolTable();
            var allErrors = new List<DiagnosticItem>();

            foreach (var sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    continue;
                }

                // Read and parse the source file
                var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
                var unit = new CompilationUnit(filePath) { SourceCode = sourceCode };

                var parseResult = ParseSourceFile(unit, filePath);
                if (parseResult.Errors.Count > 0)
                {
                    allErrors.AddRange(parseResult.Errors);
                    foreach (var err in parseResult.Errors)
                    {
                        var prefix = err.Severity == DiagnosticSeverity.Error ? "error" : "warning";
                        _outputService.WriteLine($"  {err.Location}: {prefix} {err.Id}: {err.Message}", OutputCategory.Build);
                    }
                }

                if (parseResult.AST != null)
                {
                    unit.AST = parseResult.AST;
                    compilationUnits.Add(unit);
                    _outputService.WriteLine($"  Parsed: {sourceFile.Include}", OutputCategory.Build);
                }

                processedFiles++;
            }

            // Check for parse errors
            if (allErrors.Any(e => e.Severity == DiagnosticSeverity.Error))
            {
                result.Diagnostics.AddRange(allErrors);
                result.Success = false;
                return result;
            }

            // ========== PHASE 2: Collect symbols from all files ==========
            _outputService.WriteLine($"Phase 2: Collecting symbols...", OutputCategory.Build);
            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Collecting symbols...", 30));

            foreach (var unit in compilationUnits)
            {
                CollectModuleSymbols(unit, projectSymbolTable);
            }

            var moduleNames = projectSymbolTable.GetModuleNames().ToList();
            _outputService.WriteLine($"  Found {moduleNames.Count} module(s): {string.Join(", ", moduleNames)}", OutputCategory.Build);

            // ========== PHASE 3: Semantic analysis with shared symbol table ==========
            _outputService.WriteLine($"Phase 3: Semantic analysis...", OutputCategory.Build);
            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Analyzing...", 50));

            var analyzers = new List<SemanticAnalyzer>();
            foreach (var unit in compilationUnits)
            {
                var analyzer = new SemanticAnalyzer();
                // Configure the analyzer with the project symbol table for cross-file references
                analyzer.ConfigureProjectSymbols(projectSymbolTable, unit.ModuleName);
                analyzer.Analyze(unit.AST);

                if (analyzer.Errors.Any(e => e.Severity == ErrorSeverity.Error))
                {
                    foreach (var error in analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error))
                    {
                        result.Diagnostics.Add(new DiagnosticItem
                        {
                            Id = "BL3001",
                            Message = error.Message,
                            FilePath = unit.FilePath,
                            Line = error.Line,
                            Column = error.Column,
                            Severity = DiagnosticSeverity.Error
                        });
                        _outputService.WriteLine($"  {unit.FilePath}({error.Line},{error.Column}): error BL3001: {error.Message}", OutputCategory.Build);
                    }
                }

                foreach (var warning in analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Warning))
                {
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL3002",
                        Message = warning.Message,
                        FilePath = unit.FilePath,
                        Line = warning.Line,
                        Column = warning.Column,
                        Severity = DiagnosticSeverity.Warning
                    });
                }

                unit.Symbols = analyzer.GlobalScope;
                analyzers.Add(analyzer);
            }

            result.Success = result.ErrorCount == 0;

            if (!result.Success)
            {
                return result;
            }

            // ========== PHASE 4: IR generation and code output ==========
            _outputService.WriteLine($"Phase 4: Generating code...", OutputCategory.Build);
            BuildProgress?.Invoke(this, new BuildProgressEventArgs("Generating code...", 70));

            // Generate IR for each module and merge
            var allIRModules = new List<BasicLang.Compiler.IR.IRModule>();
            for (int i = 0; i < compilationUnits.Count; i++)
            {
                var unit = compilationUnits[i];
                var analyzer = analyzers[i];

                try
                {
                    var irBuilder = new BasicLang.Compiler.IR.IRBuilder(analyzer);
                    irBuilder.Build(unit.AST, unit.ModuleName);
                    allIRModules.Add(irBuilder.Module);
                    unit.IR = irBuilder.Module;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL4001",
                        Message = $"IR generation error: {ex.Message}",
                        FilePath = unit.FilePath,
                        Severity = DiagnosticSeverity.Error
                    });
                    _outputService.WriteError($"  IR error in {unit.ModuleName}: {ex.Message}", OutputCategory.Build);
                }
            }

            if (!result.Success)
            {
                return result;
            }

            // Merge IR modules into combined output
            var mergedIR = MergeIRModules(allIRModules, project.Name);

            // Apply optimizations
            var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(mergedIR);

            // Generate C# code
            var generator = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator();
            var csharpCode = generator.Generate(mergedIR);

            result.GeneratedCode = csharpCode;
            result.GeneratedFileName = $"{project.Name}.cs";

            _outputService.WriteLine($"  Generated {result.GeneratedFileName}", OutputCategory.Build);

            // If we have errors, the build failed
            result.Success = result.ErrorCount == 0;

            if (result.Success && !string.IsNullOrEmpty(result.GeneratedCode))
            {
                // Create output directory
                var outputDir = Path.Combine(project.ProjectDirectory, config.OutputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                result.OutputPath = outputDir;

                BuildProgress?.Invoke(this, new BuildProgressEventArgs("Generating output...", 80));

                // Write generated C# code
                var csFilePath = Path.Combine(outputDir, result.GeneratedFileName ?? "Program.cs");
                File.WriteAllText(csFilePath, result.GeneratedCode);
                _outputService.WriteLine($"Generated: {csFilePath}", OutputCategory.Build);

                // Create temporary csproj for compilation
                var csprojPath = Path.Combine(outputDir, $"{project.Name}.csproj");
                var csprojContent = GenerateCsprojContent(result.GeneratedCode, Path.GetFileName(csFilePath), project, outputDir);
                File.WriteAllText(csprojPath, csprojContent);
                _outputService.WriteLine($"Generated csproj: {csprojPath}", OutputCategory.Build);

                // Check for library/app type mismatches and warn
                CheckForMismatchWarnings(result.GeneratedCode, project);

                BuildProgress?.Invoke(this, new BuildProgressEventArgs("Compiling C#...", 90));

                // Compile to executable using dotnet build
                var buildProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"build \"{csprojPath}\" -c {config.Name} --nologo -v q",
                        WorkingDirectory = outputDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                buildProcess.Start();
                var buildOutput = buildProcess.StandardOutput.ReadToEnd();
                var buildError = buildProcess.StandardError.ReadToEnd();
                buildProcess.WaitForExit();

                if (buildProcess.ExitCode != 0)
                {
                    result.Success = false;
                    // Show both stdout and stderr for better error diagnostics
                    var errorMessage = !string.IsNullOrWhiteSpace(buildError) ? buildError : buildOutput;
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL5001",
                        Message = $"C# compilation failed: {errorMessage}",
                        Severity = DiagnosticSeverity.Error
                    });
                    _outputService.WriteLine($"Build error: {errorMessage}", OutputCategory.Build);
                }
                else
                {
                    // Find the executable (now directly in output directory)
                    var exePath = Path.Combine(outputDir, $"{project.Name}.exe");
                    if (File.Exists(exePath))
                    {
                        result.ExecutablePath = exePath;
                        _outputService.WriteLine($"Executable: {exePath}", OutputCategory.Build);
                    }
                }

                _outputService.WriteLine($"Output directory: {outputDir}", OutputCategory.Build);
            }
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

    /// <summary>
    /// Parse a source file and return the AST with any errors
    /// </summary>
    private (ProgramNode AST, List<DiagnosticItem> Errors) ParseSourceFile(CompilationUnit unit, string filePath)
    {
        var errors = new List<DiagnosticItem>();
        ProgramNode ast = null;

        try
        {
            var lexer = new BasicLang.Compiler.Lexer(unit.SourceCode);
            var tokens = lexer.Tokenize();

            // Check for lexer errors
            foreach (var token in tokens.Where(t => t.Type == BasicLang.Compiler.TokenType.Unknown))
            {
                errors.Add(new DiagnosticItem
                {
                    Id = "BL1001",
                    Message = $"Unknown token: {token.Value}",
                    FilePath = filePath,
                    Line = token.Line,
                    Column = token.Column,
                    Severity = DiagnosticSeverity.Error
                });
            }

            try
            {
                var parser = new BasicLang.Compiler.Parser(tokens);
                ast = parser.Parse();

                foreach (var error in parser.Errors)
                {
                    errors.Add(new DiagnosticItem
                    {
                        Id = "BL2001",
                        Message = error.Message,
                        FilePath = filePath,
                        Line = error.Token?.Line ?? 0,
                        Column = error.Token?.Column ?? 0,
                        Severity = DiagnosticSeverity.Error
                    });
                }
            }
            catch (Exception parseEx) when (parseEx.Message.Contains("Too many parse errors"))
            {
                var tokenSummary = string.Join(", ", tokens.Take(10).Select(t => $"{t.Type}:{t.Value}"));
                errors.Add(new DiagnosticItem
                {
                    Id = "BL2000",
                    Message = $"Parse failed: {parseEx.Message}. First tokens: {tokenSummary}",
                    FilePath = filePath,
                    Severity = DiagnosticSeverity.Error
                });
            }
        }
        catch (Exception ex)
        {
            errors.Add(new DiagnosticItem
            {
                Id = "BL9000",
                Message = ex.Message,
                FilePath = filePath,
                Severity = DiagnosticSeverity.Error
            });
        }

        return (ast, errors);
    }

    /// <summary>
    /// Collect public/friend symbols from a compilation unit into the project symbol table
    /// </summary>
    private void CollectModuleSymbols(CompilationUnit unit, ProjectSymbolTable projectSymbolTable)
    {
        var extension = Path.GetExtension(unit.FilePath).ToLowerInvariant();
        var isModuleFile = extension == ".mod"; // .mod files are public by default
        var moduleSymbols = new ModuleSymbols(unit.ModuleName, unit.FilePath) { IsModuleFile = isModuleFile };

        // Walk the AST and collect top-level declarations
        foreach (var declaration in unit.AST.Declarations)
        {
            switch (declaration)
            {
                case FunctionNode func:
                    var funcAccess = isModuleFile && func.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public  // .mod files default to public
                        : func.Access;
                    if (funcAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        funcAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(func.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Function, ConvertTypeReference(func.ReturnType), func.Line, func.Column);
                        // Copy function parameters
                        symbol.Parameters = func.Parameters?.Select(p => new Symbol(p.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Parameter, ConvertTypeReference(p.Type), p.Line, p.Column)).ToList()
                            ?? new List<Symbol>();
                        symbol.ReturnType = ConvertTypeReference(func.ReturnType);
                        symbol.Access = (BasicLang.Compiler.AST.AccessModifier)(int)funcAccess;
                        moduleSymbols.AddSymbol(symbol, funcAccess);
                    }
                    break;

                case VariableDeclarationNode varDecl:
                    var varAccess = isModuleFile && varDecl.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public
                        : varDecl.Access;
                    if (varAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        varAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(varDecl.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Variable, ConvertTypeReference(varDecl.Type), varDecl.Line, varDecl.Column);
                        moduleSymbols.AddSymbol(symbol, varAccess);
                    }
                    break;

                case ConstantDeclarationNode constDecl:
                    var constAccess = isModuleFile && constDecl.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public
                        : constDecl.Access;
                    if (constAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        constAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(constDecl.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Constant, null, constDecl.Line, constDecl.Column);
                        moduleSymbols.AddSymbol(symbol, constAccess);
                    }
                    break;

                case ClassNode classNode:
                    var classAccess = isModuleFile && classNode.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public
                        : classNode.Access;
                    if (classAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        classAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(classNode.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Class, new TypeInfo(classNode.Name, TypeKind.Class), classNode.Line, classNode.Column);
                        moduleSymbols.AddSymbol(symbol, classAccess);
                    }
                    break;

                case StructureNode structNode:
                    var structAccess = isModuleFile && structNode.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public
                        : structNode.Access;
                    if (structAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        structAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(structNode.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Structure, new TypeInfo(structNode.Name, TypeKind.Structure), structNode.Line, structNode.Column);
                        moduleSymbols.AddSymbol(symbol, structAccess);
                    }
                    break;

                case EnumNode enumNode:
                    var enumAccess = isModuleFile && enumNode.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public
                        : enumNode.Access;
                    if (enumAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        enumAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(enumNode.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Type, new TypeInfo(enumNode.Name, TypeKind.Enum), enumNode.Line, enumNode.Column);
                        moduleSymbols.AddSymbol(symbol, enumAccess);
                    }
                    break;

                case SubroutineNode sub:
                    var subAccess = isModuleFile && sub.Access == BasicLang.Compiler.AST.AccessModifier.Private
                        ? BasicLang.Compiler.AST.AccessModifier.Public  // .mod files default to public
                        : sub.Access;
                    if (subAccess == BasicLang.Compiler.AST.AccessModifier.Public ||
                        subAccess == BasicLang.Compiler.AST.AccessModifier.Friend)
                    {
                        var symbol = new Symbol(sub.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Function, new TypeInfo("Void", TypeKind.Primitive), sub.Line, sub.Column);
                        // Copy subroutine parameters
                        symbol.Parameters = sub.Parameters?.Select(p => new Symbol(p.Name, BasicLang.Compiler.SemanticAnalysis.SymbolKind.Parameter, ConvertTypeReference(p.Type), p.Line, p.Column)).ToList()
                            ?? new List<Symbol>();
                        symbol.ReturnType = new TypeInfo("Void", TypeKind.Primitive);
                        symbol.Access = (BasicLang.Compiler.AST.AccessModifier)(int)subAccess;
                        moduleSymbols.AddSymbol(symbol, subAccess);
                    }
                    break;
            }
        }

        projectSymbolTable.RegisterModule(unit.ModuleName, moduleSymbols);
    }

    /// <summary>
    /// Convert AST TypeReference to SemanticAnalysis TypeInfo
    /// </summary>
    private TypeInfo ConvertTypeReference(TypeReference typeRef)
    {
        if (typeRef == null)
            return null;

        // Simple conversion - determine TypeKind from the type name
        var kind = typeRef.Name.ToLowerInvariant() switch
        {
            "integer" or "int" or "int32" => TypeKind.Primitive,
            "long" or "int64" => TypeKind.Primitive,
            "short" or "int16" => TypeKind.Primitive,
            "byte" => TypeKind.Primitive,
            "single" or "float" => TypeKind.Primitive,
            "double" => TypeKind.Primitive,
            "decimal" => TypeKind.Primitive,
            "boolean" or "bool" => TypeKind.Primitive,
            "string" => TypeKind.Primitive,
            "char" => TypeKind.Primitive,
            "object" => TypeKind.Class,
            "void" => TypeKind.Void,
            _ => TypeKind.UserDefinedType
        };

        return new TypeInfo(typeRef.Name, kind);
    }

    /// <summary>
    /// Merge multiple IR modules into a single module
    /// </summary>
    private BasicLang.Compiler.IR.IRModule MergeIRModules(List<BasicLang.Compiler.IR.IRModule> modules, string projectName)
    {
        if (modules.Count == 0)
            return new BasicLang.Compiler.IR.IRModule(projectName);

        if (modules.Count == 1)
            return modules[0];

        // Create a merged module
        var merged = new BasicLang.Compiler.IR.IRModule(projectName);

        foreach (var module in modules)
        {
            // Merge functions - set ModuleName for tracking
            foreach (var func in module.Functions)
            {
                func.ModuleName = module.Name;
                merged.Functions.Add(func);
            }

            // Merge global variables - set ModuleName for tracking
            foreach (var kvp in module.GlobalVariables)
            {
                var globalVar = kvp.Value;
                globalVar.ModuleName = module.Name;
                var key = kvp.Key.Contains(".")
                    ? kvp.Key
                    : $"{module.Name}.{kvp.Key}";
                merged.GlobalVariables[key] = globalVar;
            }

            // Merge types
            foreach (var kvp in module.Types)
            {
                if (!merged.Types.ContainsKey(kvp.Key))
                {
                    merged.Types[kvp.Key] = kvp.Value;
                }
            }

            // Merge classes
            foreach (var kvp in module.Classes)
            {
                if (!merged.Classes.ContainsKey(kvp.Key))
                {
                    merged.Classes[kvp.Key] = kvp.Value;
                }
            }

            // Merge enums
            foreach (var kvp in module.Enums)
            {
                if (!merged.Enums.ContainsKey(kvp.Key))
                {
                    merged.Enums[kvp.Key] = kvp.Value;
                }
            }

            // Merge .NET usings
            foreach (var using_ in module.NetUsings)
            {
                if (!merged.NetUsings.Any(u => u.Namespace == using_.Namespace))
                {
                    merged.NetUsings.Add(using_);
                }
            }

            // Merge namespaces
            foreach (var ns in module.Namespaces)
            {
                if (!merged.Namespaces.Contains(ns))
                {
                    merged.Namespaces.Add(ns);
                }
            }
        }

        return merged;
    }

    private BuildResult CompileSource(string sourceCode, string filePath, BasicLangProject project, BuildConfiguration config)
    {
        var result = new BuildResult { Success = true };

        try
        {
            // Use BasicLang compiler
            var lexer = new BasicLang.Compiler.Lexer(sourceCode);
            var tokens = lexer.Tokenize();

            // Check for lexer errors
            foreach (var token in tokens.Where(t => t.Type == BasicLang.Compiler.TokenType.Unknown))
            {
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL1001",
                    Message = $"Unknown token: {token.Value}",
                    FilePath = filePath,
                    Line = token.Line,
                    Column = token.Column,
                    Severity = DiagnosticSeverity.Error
                });
            }

            // Parse
            BasicLang.Compiler.AST.ProgramNode ast;
            try
            {
                var parser = new BasicLang.Compiler.Parser(tokens);
                ast = parser.Parse();

                // Check for parser errors
                foreach (var error in parser.Errors)
                {
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL2001",
                        Message = error.Message,
                        FilePath = filePath,
                        Line = error.Token?.Line ?? 0,
                        Column = error.Token?.Column ?? 0,
                        Severity = DiagnosticSeverity.Error
                    });
                }
            }
            catch (Exception parseEx) when (parseEx.Message.Contains("Too many parse errors"))
            {
                // Show the first few tokens to help diagnose the issue
                var tokenSummary = string.Join(", ", tokens.Take(10).Select(t => $"{t.Type}:{t.Value}"));
                result.Success = false;
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL2000",
                    Message = $"Parse failed: {parseEx.Message}. First tokens: {tokenSummary}",
                    FilePath = filePath,
                    Severity = DiagnosticSeverity.Error
                });
                return result;
            }

            if (result.ErrorCount > 0)
            {
                result.Success = false;
                return result;
            }

            // Semantic analysis
            var analyzer = new BasicLang.Compiler.SemanticAnalysis.SemanticAnalyzer();
            analyzer.Analyze(ast);

            foreach (var error in analyzer.Errors.Where(e => e.Severity == BasicLang.Compiler.SemanticAnalysis.ErrorSeverity.Error))
            {
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL3001",
                    Message = error.Message,
                    FilePath = filePath,
                    Line = error.Line,
                    Column = error.Column,
                    Severity = DiagnosticSeverity.Error
                });
            }

            // Check for warnings (SemanticErrors with Warning severity)
            foreach (var warning in analyzer.Errors.Where(e => e.Severity == BasicLang.Compiler.SemanticAnalysis.ErrorSeverity.Warning))
            {
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = "BL3002",
                    Message = warning.Message,
                    FilePath = filePath,
                    Line = warning.Line,
                    Column = warning.Column,
                    Severity = DiagnosticSeverity.Warning
                });
            }

            result.Success = result.ErrorCount == 0;

            // Generate C# code if no errors
            if (result.Success)
            {
                try
                {
                    // Build IR
                    var irBuilder = new BasicLang.Compiler.IR.IRBuilder(analyzer);
                    irBuilder.Build(ast);
                    var irModule = irBuilder.Module;

                    // Apply optimizations
                    var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
                    pipeline.AddStandardPasses();
                    pipeline.Run(irModule);

                    // Generate C# code
                    var generator = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator();
                    var csharpCode = generator.Generate(irModule);

                    // Store generated code for later use
                    result.GeneratedCode = csharpCode;
                    result.GeneratedFileName = Path.GetFileNameWithoutExtension(filePath) + ".cs";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL4001",
                        Message = $"Code generation error: {ex.Message}",
                        FilePath = filePath,
                        Severity = DiagnosticSeverity.Error
                    });
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Diagnostics.Add(new DiagnosticItem
            {
                Id = "BL9000",
                Message = ex.Message,
                FilePath = filePath,
                Severity = DiagnosticSeverity.Error
            });
        }

        return result;
    }

    /// <summary>
    /// Generate .csproj content with appropriate framework references based on the project settings
    /// </summary>
    private string GenerateCsprojContent(string generatedCode, string csFileName, BasicLangProject project, string outputDir)
    {
        var sb = new System.Text.StringBuilder();

        // Detect which namespaces are used (case-insensitive)
        var codeUpper = generatedCode.ToUpperInvariant();
        bool usesWindowsForms = codeUpper.Contains("USING SYSTEM.WINDOWS.FORMS;");
        bool usesWpf = codeUpper.Contains("USING SYSTEM.WINDOWS;") &&
                       (codeUpper.Contains("USING SYSTEM.WINDOWS.CONTROLS;") ||
                        codeUpper.Contains("USING SYSTEM.WINDOWS.MEDIA;"));
        bool usesDrawing = codeUpper.Contains("USING SYSTEM.DRAWING;");
        bool usesAspNet = codeUpper.Contains("USING MICROSOFT.ASPNETCORE;");

        // Detect if game framework is used (FrameworkWrapper calls)
        bool usesGameFramework = generatedCode.Contains("FrameworkWrapper.");

        // Use project's OutputType setting instead of auto-detecting
        string sdk = "Microsoft.NET.Sdk";
        string outputType = project.OutputType switch
        {
            OutputType.WinExe => "WinExe",
            OutputType.Library => "Library",
            _ => "Exe"
        };

        // For WinExe, enable Windows Forms or WPF based on what's used
        bool enableWindowsForms = project.OutputType == OutputType.WinExe && (usesWindowsForms || !usesWpf);
        bool enableWpf = project.OutputType == OutputType.WinExe && usesWpf;

        if (usesAspNet)
        {
            sdk = "Microsoft.NET.Sdk.Web";
        }

        sb.AppendLine($@"<Project Sdk=""{sdk}"">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <OutputType>{outputType}</OutputType>");
        sb.AppendLine("    <TargetFramework>net8.0-windows</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>disable</Nullable>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
        sb.AppendLine("    <OutputPath>.\\</OutputPath>");

        // Add Windows Forms support
        if (enableWindowsForms)
        {
            sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
        }

        // Add WPF support
        if (enableWpf)
        {
            sb.AppendLine("    <UseWPF>true</UseWPF>");
        }

        sb.AppendLine("  </PropertyGroup>");

        // Add compile items
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Compile Include=\"{csFileName}\" />");
        sb.AppendLine("  </ItemGroup>");

        // Add package references for System.Drawing if needed (for non-Windows Forms projects)
        if (usesDrawing && !usesWindowsForms)
        {
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <PackageReference Include=\"System.Drawing.Common\" Version=\"8.0.0\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        // Add RaylibWrapper.dll reference for game projects
        if (usesGameFramework)
        {
            // Copy the framework DLLs to output directory
            CopyGameFrameworkDlls(outputDir);

            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <Reference Include=\"RaylibWrapper\">");
            sb.AppendLine("      <HintPath>RaylibWrapper.dll</HintPath>");
            sb.AppendLine("    </Reference>");
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    /// <summary>
    /// Copy game framework DLLs to the output directory
    /// </summary>
    private void CopyGameFrameworkDlls(string outputDir)
    {
        // Find the RaylibWrapper.dll and VisualGameStudioEngine.dll
        var ideDir = AppDomain.CurrentDomain.BaseDirectory;

        // Possible locations for RaylibWrapper.dll
        var possibleRaylibPaths = new[]
        {
            Path.Combine(ideDir, "RaylibWrapper.dll"),
            Path.Combine(ideDir, "..", "RaylibWrapper", "bin", "Release", "net8.0", "RaylibWrapper.dll"),
            Path.Combine(ideDir, "..", "RaylibWrapper", "bin", "Debug", "net8.0", "RaylibWrapper.dll"),
            Path.Combine(ideDir, "..", "..", "..", "RaylibWrapper", "bin", "Release", "net8.0", "RaylibWrapper.dll"),
            Path.Combine(ideDir, "..", "..", "..", "RaylibWrapper", "bin", "Debug", "net8.0", "RaylibWrapper.dll"),
        };

        // Possible locations for VisualGameStudioEngine.dll (native)
        var possibleEnginePaths = new[]
        {
            Path.Combine(ideDir, "VisualGameStudioEngine.dll"),
            Path.Combine(ideDir, "..", "x64", "Release", "VisualGameStudioEngine.dll"),
            Path.Combine(ideDir, "..", "x64", "Debug", "VisualGameStudioEngine.dll"),
            Path.Combine(ideDir, "..", "..", "..", "x64", "Release", "VisualGameStudioEngine.dll"),
            Path.Combine(ideDir, "..", "..", "..", "x64", "Debug", "VisualGameStudioEngine.dll"),
        };

        // Copy RaylibWrapper.dll
        string raylibSource = null;
        foreach (var path in possibleRaylibPaths)
        {
            if (File.Exists(path))
            {
                raylibSource = path;
                break;
            }
        }

        if (raylibSource != null)
        {
            var destPath = Path.Combine(outputDir, "RaylibWrapper.dll");
            try
            {
                File.Copy(raylibSource, destPath, true);
                _outputService.WriteLine($"Copied: RaylibWrapper.dll", OutputCategory.Build);
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"Failed to copy RaylibWrapper.dll: {ex.Message}", OutputCategory.Build);
            }
        }
        else
        {
            _outputService.WriteError("RaylibWrapper.dll not found - game may not run correctly", OutputCategory.Build);
        }

        // Copy VisualGameStudioEngine.dll (native engine)
        string engineSource = null;
        foreach (var path in possibleEnginePaths)
        {
            if (File.Exists(path))
            {
                engineSource = path;
                break;
            }
        }

        if (engineSource != null)
        {
            var destPath = Path.Combine(outputDir, "VisualGameStudioEngine.dll");
            try
            {
                File.Copy(engineSource, destPath, true);
                _outputService.WriteLine($"Copied: VisualGameStudioEngine.dll", OutputCategory.Build);
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"Failed to copy VisualGameStudioEngine.dll: {ex.Message}", OutputCategory.Build);
            }
        }
        else
        {
            _outputService.WriteError("VisualGameStudioEngine.dll not found - game may not run correctly", OutputCategory.Build);
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
