using System.Diagnostics;
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

            foreach (var sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePath = Path.Combine(project.ProjectDirectory, sourceFile.Include);
                _outputService.WriteLine($"Compiling: {sourceFile.Include}", OutputCategory.Build);

                BuildProgress?.Invoke(this, new BuildProgressEventArgs(
                    $"Compiling {sourceFile.FileName}...",
                    (int)((double)processedFiles / totalFiles * 80),
                    sourceFile.FileName));

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

                // Read the source file
                var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);

                // Use the BasicLang compiler API
                try
                {
                    var compileResult = CompileSource(sourceCode, filePath, project, config);

                    foreach (var diagnostic in compileResult.Diagnostics)
                    {
                        result.Diagnostics.Add(diagnostic);

                        var prefix = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
                        _outputService.WriteLine(
                            $"  {diagnostic.Location}: {prefix} {diagnostic.Id}: {diagnostic.Message}",
                            OutputCategory.Build);
                    }

                    // Copy generated code from compile result
                    if (compileResult.Success && !string.IsNullOrEmpty(compileResult.GeneratedCode))
                    {
                        result.GeneratedCode = compileResult.GeneratedCode;
                        result.GeneratedFileName = compileResult.GeneratedFileName;
                    }
                }
                catch (Exception ex)
                {
                    result.Diagnostics.Add(new DiagnosticItem
                    {
                        Id = "BL0003",
                        Message = $"Compilation error: {ex.Message}",
                        FilePath = filePath,
                        Severity = DiagnosticSeverity.Error
                    });
                }

                processedFiles++;
            }

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
                var csprojContent = GenerateCsprojContent(result.GeneratedCode, Path.GetFileName(csFilePath), project);
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
    private string GenerateCsprojContent(string generatedCode, string csFileName, BasicLangProject project)
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

        sb.AppendLine("</Project>");

        return sb.ToString();
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
