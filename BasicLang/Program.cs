using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.LLVM;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.Repl;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.StdLib;
using BasicLang.Debugger;

namespace BasicLang.Compiler.Driver
{
    /// <summary>
    /// Main entry point for the BasicLang compiler
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // Check for LSP mode
            if (args.Contains("--lsp") || args.Contains("--language-server"))
            {
                // Use simple LSP server for better compatibility
                var server = new SimpleLspServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
                await server.RunAsync();
                return;
            }

            // Check for Debug Adapter mode
            if (args.Contains("--debug-adapter") || args.Contains("--dap"))
            {
                var debugSession = new DebugSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
                await debugSession.RunAsync();
                return;
            }

            // Check for REPL mode
            if (args.Contains("--repl") || args.Contains("-i") || args.Contains("--interactive"))
            {
                var repl = new REPL();
                repl.Run();
                return;
            }

            // Check for help
            if (args.Contains("--help") || args.Contains("-h"))
            {
                PrintUsage();
                return;
            }

            // Check for version
            if (args.Contains("--version") || args.Contains("-v"))
            {
                PrintVersion();
                return;
            }

            // Handle subcommands
            if (args.Length > 0)
            {
                var command = args[0].ToLowerInvariant();
                var subArgs = args.Skip(1).ToArray();

                switch (command)
                {
                    case "new":
                        HandleNewCommand(subArgs);
                        return;

                    case "restore":
                        await HandleRestoreCommand(subArgs);
                        return;

                    case "add":
                        await HandleAddCommand(subArgs);
                        return;

                    case "remove":
                        HandleRemoveCommand(subArgs);
                        return;

                    case "build":
                        await HandleBuildCommand(subArgs);
                        return;

                    case "run":
                        await HandleRunCommand(subArgs);
                        return;

                    case "list":
                        HandleListCommand(subArgs);
                        return;

                    case "search":
                        await HandleSearchCommand(subArgs);
                        return;
                }
            }

            // Check for parser tests
            if (args.Contains("--parser-tests"))
            {
                BasicLang.Test.ParserTests.Run();
                return;
            }

            // Check for file argument (compile a file)
            var fileArg = args.FirstOrDefault(a => !a.StartsWith("-") &&
                (a.EndsWith(".bas") || a.EndsWith(".bl") || a.EndsWith(".basic") || a.EndsWith(".blproj")));

            if (fileArg != null)
            {
                if (fileArg.EndsWith(".blproj"))
                {
                    await HandleBuildCommand(new[] { fileArg });
                }
                else
                {
                    CompileFile(fileArg, args);
                }
                return;
            }

            Console.WriteLine("=".PadRight(70, '='));
            Console.WriteLine("BasicLang Multi-Target Transpiler - Complete Pipeline Demo");
            Console.WriteLine("=".PadRight(70, '='));
            Console.WriteLine();

            // Run all demos
            DemoSimpleFunction();
            DemoFibonacci();
            DemoLoops();
            DemoArrays();
            DemoCompleteProgram();
            DemoStringOperations();
            DemoMathOperations();
            DemoTypeConversion();
            DemoArrayBoundsAndConcat();
            DemoSelectCase();
            DemoExitStatements();
            DemoDoLoopVariations();
            DemoRandomNumbers();
            DemoCppBackend();
            DemoLLVMBackend();
            DemoMSILBackend();
            DemoStdLibAbstraction();
            DemoPlatformExterns();
            DemoOOPFeatures();
            DemoAdvancedFeatures();
            DemoAsyncAndIterators();
            DemoOperatorOverloading();
            DemoInterfacesEnumsDelegates();
            DemoEvents();
            DemoExtensionMethods();
            DemoLinqQueries();
            DemoPatternMatching();
            DemoOptionalAndParamArray();

            Console.WriteLine();
            Console.WriteLine("Demo complete! Check the generated files in GeneratedCode folder.");
        }

        static void PrintVersion()
        {
            Console.WriteLine("BasicLang Compiler v1.0.0");
            Console.WriteLine("Target Framework: .NET 8.0");
            Console.WriteLine("Supported Backends: C#, C++, LLVM, MSIL");
            Console.WriteLine();
            Console.WriteLine("Copyright (c) 2025 BasicLang Project");
        }

        static void PrintUsage()
        {
            Console.WriteLine("BasicLang Compiler v1.0.0");
            Console.WriteLine("=========================");
            Console.WriteLine();
            Console.WriteLine("Usage: basiclang [command] [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  new <template>    Create a new project from a template");
            Console.WriteLine("  build             Build a project");
            Console.WriteLine("  run               Build and run a project");
            Console.WriteLine("  restore           Restore NuGet packages");
            Console.WriteLine("  add package <id>  Add a NuGet package to the project");
            Console.WriteLine("  remove package    Remove a NuGet package");
            Console.WriteLine("  list packages     List installed packages");
            Console.WriteLine("  search <query>    Search for NuGet packages");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --repl, -i        Start interactive REPL");
            Console.WriteLine("  --lsp             Start Language Server Protocol mode");
            Console.WriteLine("  --debug-adapter   Start Debug Adapter Protocol mode");
            Console.WriteLine("  --help, -h        Show this help message");
            Console.WriteLine("  --version, -v     Show version information");
            Console.WriteLine("  --target=X        Target backend (csharp, cpp, llvm, msil)");
            Console.WriteLine("  --output=FILE     Output file path");
            Console.WriteLine("  --optimize        Enable aggressive optimizations");
            Console.WriteLine("  --search-path=DIR Add module search path");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  basiclang new console -n MyApp         Create new console app");
            Console.WriteLine("  basiclang build                        Build current project");
            Console.WriteLine("  basiclang run                          Build and run project");
            Console.WriteLine("  basiclang add package Newtonsoft.Json  Add a package");
            Console.WriteLine("  basiclang restore                      Restore all packages");
            Console.WriteLine("  basiclang --repl                       Start interactive mode");
            Console.WriteLine("  basiclang program.bas                  Compile a source file");
        }

        // ====================================================================
        // Command Handlers
        // ====================================================================

        static void HandleNewCommand(string[] args)
        {
            var templateEngine = new TemplateEngine();

            if (args.Length == 0 || args.Contains("--list") || args.Contains("-l"))
            {
                templateEngine.ListTemplates();
                return;
            }

            var templateName = args[0];
            string projectName = null;
            string outputPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "-n" || args[i] == "--name") && i + 1 < args.Length)
                {
                    projectName = args[++i];
                }
                else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }
            }

            templateEngine.CreateProject(templateName, projectName, outputPath);
        }

        static async Task HandleRestoreCommand(string[] args)
        {
            var projectPath = FindProjectFile(args.FirstOrDefault(a => !a.StartsWith("-")));
            if (projectPath == null)
            {
                Console.WriteLine("No project file found. Use 'basiclang new' to create a project.");
                return;
            }

            var project = ProjectFile.Load(projectPath);
            var packageManager = new PackageManager();
            await packageManager.RestoreAsync(project);
        }

        static async Task HandleAddCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: basiclang add package <package-id> [--version <version>]");
                return;
            }

            if (args[0].ToLowerInvariant() == "package")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: basiclang add package <package-id> [--version <version>]");
                    return;
                }

                var packageId = args[1];
                string version = null;

                for (int i = 2; i < args.Length; i++)
                {
                    if ((args[i] == "-v" || args[i] == "--version") && i + 1 < args.Length)
                    {
                        version = args[++i];
                    }
                }

                var projectPath = FindProjectFile(null);
                if (projectPath == null)
                {
                    Console.WriteLine("No project file found.");
                    return;
                }

                var project = ProjectFile.Load(projectPath);
                var packageManager = new PackageManager();
                await packageManager.AddPackageAsync(project, packageId, version);
            }
            else
            {
                Console.WriteLine($"Unknown add target: {args[0]}");
                Console.WriteLine("Available: package");
            }
        }

        static void HandleRemoveCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: basiclang remove package <package-id>");
                return;
            }

            if (args[0].ToLowerInvariant() == "package")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: basiclang remove package <package-id>");
                    return;
                }

                var packageId = args[1];
                var projectPath = FindProjectFile(null);
                if (projectPath == null)
                {
                    Console.WriteLine("No project file found.");
                    return;
                }

                var project = ProjectFile.Load(projectPath);
                var packageManager = new PackageManager();
                packageManager.RemovePackage(project, packageId);
            }
        }

        static async Task HandleBuildCommand(string[] args)
        {
            var projectPath = FindProjectFile(args.FirstOrDefault(a => !a.StartsWith("-")));
            if (projectPath == null)
            {
                Console.WriteLine("No project file found.");
                return;
            }

            Console.WriteLine($"Building {Path.GetFileName(projectPath)}...");

            var project = ProjectFile.Load(projectPath);

            // Restore packages first
            var packageManager = new PackageManager();
            var restoreResult = await packageManager.RestoreAsync(project);

            if (!restoreResult.Success)
            {
                Console.WriteLine("Package restore failed. Fix errors and try again.");
                return;
            }

            // Get configuration
            var configuration = "Debug";
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-c" || args[i] == "--configuration") && i + 1 < args.Length)
                {
                    configuration = args[++i];
                }
            }

            // Compile each source file
            var projectDir = Path.GetDirectoryName(projectPath) ?? ".";
            var outputDir = Path.Combine(projectDir, "bin", configuration, project.TargetFramework);
            Directory.CreateDirectory(outputDir);

            var sourceFiles = project.GetSourceFiles().ToList();
            if (sourceFiles.Count == 0)
            {
                Console.WriteLine("No source files found.");
                return;
            }

            var options = new BasicLang.Compiler.CompilerOptions
            {
                TargetBackend = project.Backend.ToLowerInvariant(),
                OutputPath = outputDir,
                OptimizeAggressive = project.Configurations.TryGetValue(configuration, out var config) && config.OptimizationsEnabled
            };

            // Add package assemblies to search paths
            foreach (var assembly in restoreResult.ResolvedAssemblies)
            {
                var dir = Path.GetDirectoryName(assembly);
                if (!string.IsNullOrEmpty(dir) && !options.SearchPaths.Contains(dir))
                {
                    options.SearchPaths.Add(dir);
                }
            }

            var compiler = new BasicCompiler(options);
            var success = true;
            IR.IRModule combinedIR = null;

            foreach (var sourceFile in sourceFiles)
            {
                Console.WriteLine($"  Compiling {Path.GetFileName(sourceFile)}...");
                var result = compiler.CompileFile(sourceFile);
                if (!result.Success)
                {
                    success = false;
                    foreach (var error in result.AllErrors)
                    {
                        Console.WriteLine($"    Error: {error.Message}");
                    }
                }
                else if (result.CombinedIR != null)
                {
                    combinedIR = result.CombinedIR;
                }
            }

            if (success && combinedIR != null)
            {
                // Generate output code
                Console.WriteLine("  Generating output...");

                var outputFileName = project.AssemblyName ?? project.ProjectName ?? "Program";
                var backend = project.Backend?.ToLowerInvariant() ?? "csharp";

                string generatedCode;
                string extension;

                switch (backend)
                {
                    case "cpp":
                    case "c++":
                        var cppGen = new CodeGen.CPlusPlus.CppCodeGenerator();
                        generatedCode = cppGen.Generate(combinedIR);
                        extension = ".cpp";
                        break;
                    case "llvm":
                        var llvmGen = new CodeGen.LLVM.LLVMCodeGenerator();
                        generatedCode = llvmGen.Generate(combinedIR);
                        extension = ".ll";
                        break;
                    case "msil":
                        var msilGen = new CodeGen.MSIL.MSILCodeGenerator();
                        generatedCode = msilGen.Generate(combinedIR);
                        extension = ".il";
                        break;
                    default: // csharp
                        var csGen = new CodeGen.CSharp.CSharpCodeGenerator();
                        generatedCode = csGen.Generate(combinedIR);
                        extension = ".cs";
                        break;
                }

                var outputPath = Path.Combine(outputDir, outputFileName + extension);
                File.WriteAllText(outputPath, generatedCode);

                // For C# backend, compile to .dll
                if (backend == "csharp" || backend == "cs")
                {
                    Console.WriteLine("  Compiling to .NET assembly...");

                    // Generate a temporary .csproj
                    var csprojPath = Path.Combine(outputDir, outputFileName + ".csproj");
                    var csFileName = outputFileName + ".cs";
                    var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>{(project.OutputType == "Library" ? "Library" : "Exe")}</OutputType>
    <TargetFramework>{project.TargetFramework}</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>{outputFileName}</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""{csFileName}"" />
  </ItemGroup>
</Project>";
                    File.WriteAllText(csprojPath, csprojContent);

                    // Run dotnet build
                    var buildProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"build \"{csprojPath}\" -c {configuration} -o \"{outputDir}\" --nologo -v q",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WorkingDirectory = outputDir
                        }
                    };

                    buildProcess.Start();
                    var buildOutput = await buildProcess.StandardOutput.ReadToEndAsync();
                    var buildErrors = await buildProcess.StandardError.ReadToEndAsync();
                    await buildProcess.WaitForExitAsync();

                    if (buildProcess.ExitCode != 0)
                    {
                        Console.WriteLine("  .NET compilation failed:");
                        if (!string.IsNullOrWhiteSpace(buildOutput))
                            Console.WriteLine(buildOutput);
                        if (!string.IsNullOrWhiteSpace(buildErrors))
                            Console.WriteLine(buildErrors);
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Build succeeded. Output: {outputPath}");
            }
            else if (!success)
            {
                Console.WriteLine();
                Console.WriteLine("Build failed.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Build completed but no output generated.");
            }
        }

        static async Task HandleRunCommand(string[] args)
        {
            // First build
            await HandleBuildCommand(args);

            // Then run
            var projectPath = FindProjectFile(args.FirstOrDefault(a => !a.StartsWith("-")));
            if (projectPath == null)
                return;

            var project = ProjectFile.Load(projectPath);
            var projectDir = Path.GetDirectoryName(projectPath) ?? ".";
            var configuration = "Debug";
            var outputDir = Path.Combine(projectDir, "bin", configuration, project.TargetFramework);

            // Find the output executable
            var exeName = project.AssemblyName ?? project.ProjectName;

            // Check multiple possible locations for the dll
            var possiblePaths = new[]
            {
                Path.Combine(outputDir, $"{exeName}.dll"),
                Path.Combine(outputDir, "bin", configuration, project.TargetFramework, $"{exeName}.dll"),
            };

            var exePath = possiblePaths.FirstOrDefault(File.Exists);

            if (exePath != null)
            {
                Console.WriteLine();
                Console.WriteLine($"Running {exeName}...");
                Console.WriteLine(new string('-', 40));

                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"\"{exePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                Console.WriteLine(await process.StandardOutput.ReadToEndAsync());
                var errors = await process.StandardError.ReadToEndAsync();
                if (!string.IsNullOrEmpty(errors))
                {
                    Console.Error.WriteLine(errors);
                }
                await process.WaitForExitAsync();
            }
            else
            {
                Console.WriteLine($"Output not found. Searched:");
                foreach (var p in possiblePaths)
                    Console.WriteLine($"  - {p}");
            }
        }

        static void HandleListCommand(string[] args)
        {
            if (args.Length == 0 || args[0].ToLowerInvariant() == "packages")
            {
                var projectPath = FindProjectFile(null);
                if (projectPath == null)
                {
                    Console.WriteLine("No project file found.");
                    return;
                }

                var project = ProjectFile.Load(projectPath);
                var packageManager = new PackageManager();
                packageManager.ListPackages(project);
            }
            else if (args[0].ToLowerInvariant() == "templates")
            {
                var templateEngine = new TemplateEngine();
                templateEngine.ListTemplates();
            }
            else
            {
                Console.WriteLine($"Unknown list target: {args[0]}");
                Console.WriteLine("Available: packages, templates");
            }
        }

        static async Task HandleSearchCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: basiclang search <query>");
                return;
            }

            var query = string.Join(" ", args);
            var packageManager = new PackageManager();
            var results = await packageManager.SearchAsync(query);

            if (results.Count == 0)
            {
                Console.WriteLine("No packages found.");
                return;
            }

            Console.WriteLine($"Found {results.Count} packages:");
            Console.WriteLine();

            foreach (var pkg in results)
            {
                Console.WriteLine($"  {pkg.Id} ({pkg.Version})");
                if (!string.IsNullOrEmpty(pkg.Description))
                {
                    var desc = pkg.Description.Length > 70
                        ? pkg.Description.Substring(0, 67) + "..."
                        : pkg.Description;
                    Console.WriteLine($"    {desc}");
                }
                Console.WriteLine($"    Downloads: {pkg.TotalDownloads:N0}");
                Console.WriteLine();
            }
        }

        static string FindProjectFile(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            {
                if (File.Exists(explicitPath))
                    return explicitPath;
                if (Directory.Exists(explicitPath))
                {
                    var projInDir = Directory.GetFiles(explicitPath, "*.blproj").FirstOrDefault();
                    if (projInDir != null)
                        return projInDir;
                }
            }

            // Search current directory
            var currentDir = Directory.GetCurrentDirectory();
            var projectFiles = Directory.GetFiles(currentDir, "*.blproj");

            if (projectFiles.Length == 1)
                return projectFiles[0];

            if (projectFiles.Length > 1)
            {
                Console.WriteLine("Multiple project files found. Please specify one:");
                foreach (var pf in projectFiles)
                {
                    Console.WriteLine($"  {Path.GetFileName(pf)}");
                }
                return null;
            }

            return null;
        }

        /// <summary>
        /// Compile a source file using the new multi-file compiler
        /// </summary>
        static void CompileFile(string filePath, string[] args)
        {
            Console.WriteLine($"Compiling: {filePath}");
            Console.WriteLine();

            // Parse options
            var options = new BasicLang.Compiler.CompilerOptions();
            string targetBackend = "csharp";
            string outputPath = null;

            foreach (var arg in args)
            {
                if (arg.StartsWith("--target="))
                {
                    targetBackend = arg.Substring("--target=".Length).ToLowerInvariant();
                    options.TargetBackend = targetBackend;
                }
                else if (arg.StartsWith("--output="))
                {
                    outputPath = arg.Substring("--output=".Length);
                    options.OutputPath = outputPath;
                }
                else if (arg == "--optimize" || arg == "-O")
                {
                    options.OptimizeAggressive = true;
                }
                else if (arg.StartsWith("--search-path="))
                {
                    options.SearchPaths.Add(arg.Substring("--search-path=".Length));
                }
            }

            // Create compiler and compile
            var compiler = new BasicCompiler(options);
            var result = compiler.CompileFile(filePath);

            // Report results
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Compilation successful! ({result.Duration.TotalMilliseconds:F0}ms)");
                Console.ResetColor();
                Console.WriteLine($"  Files compiled: {result.Units.Count}");

                // Generate output
                if (result.CombinedIR != null)
                {
                    string outputCode = GenerateCode(result.CombinedIR, targetBackend);

                    if (!string.IsNullOrEmpty(outputPath))
                    {
                        File.WriteAllText(outputPath, outputCode);
                        Console.WriteLine($"  Output written to: {outputPath}");
                    }
                    else
                    {
                        // Default output path
                        var baseName = Path.GetFileNameWithoutExtension(filePath);
                        var ext = GetOutputExtension(targetBackend);
                        var defaultOutputPath = Path.Combine(Path.GetDirectoryName(filePath) ?? ".", baseName + ext);
                        File.WriteAllText(defaultOutputPath, outputCode);
                        Console.WriteLine($"  Output written to: {defaultOutputPath}");
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Compilation failed with {result.AllErrors.Count} error(s):");
                Console.ResetColor();

                // Group errors for better display
                var grouped = ErrorGrouper.GroupErrors(result.AllErrors);
                foreach (var group in grouped)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Error at line {group.PrimaryError.Line}: {group.PrimaryError.Message}");
                    Console.ResetColor();

                    if (!string.IsNullOrEmpty(group.CommonCause))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"    Cause: {group.CommonCause}");
                        Console.ResetColor();
                    }

                    if (group.RelatedErrors.Count > 0)
                    {
                        Console.WriteLine($"    ({group.RelatedErrors.Count} related error(s))");
                    }
                }
            }
        }

        /// <summary>
        /// Generate code for the specified backend
        /// </summary>
        static string GenerateCode(IRModule ir, string backend)
        {
            ICodeGenerator generator = backend?.ToLowerInvariant() switch
            {
                "cpp" or "c++" => new CppCodeGenerator(),
                "llvm" => new BasicLang.Compiler.CodeGen.LLVM.LLVMCodeGenerator(),
                "msil" or "il" => new BasicLang.Compiler.CodeGen.MSIL.MSILCodeGenerator(),
                _ => new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator()  // Default to C#
            };

            return generator.Generate(ir);
        }

        /// <summary>
        /// Get output file extension for backend
        /// </summary>
        static string GetOutputExtension(string backend)
        {
            return backend?.ToLowerInvariant() switch
            {
                "cpp" or "c++" => ".cpp",
                "llvm" => ".ll",
                "msil" or "il" => ".il",
                _ => ".cs"
            };
        }

        // ====================================================================
        // Demo 1: Simple Function
        // ====================================================================

        static void DemoSimpleFunction()
        {
            Console.WriteLine("Demo 1: Simple Function with PrintLine");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub Main()
    Dim result As Integer
    result = Add(5, 3)
    PrintLine(""The result is:"")
    PrintLine(result)
End Sub
";

            var csharpCode = CompileToCSharp(source, "SimpleFunction");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("SimpleFunction.cs", csharpCode);
        }



        // ====================================================================
        // Demo 2: Fibonacci
        // ====================================================================

        static void DemoFibonacci()
        {
            Console.WriteLine("Demo 2: Fibonacci Calculator");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function Fibonacci(n As Integer) As Integer
    If n <= 1 Then
        Return n
    Else
        Return Fibonacci(n - 1) + Fibonacci(n - 2)
    End If
End Function

Sub Main()
    Dim result As Integer
    result = Fibonacci(10)
End Sub
";

            var csharpCode = CompileToCSharp(source, "Fibonacci");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("Fibonacci.cs", csharpCode);
        }

        // ====================================================================
        // Demo 3: Loops
        // ====================================================================

        static void DemoLoops()
        {
            Console.WriteLine("Demo 3: Loop Examples");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function SumNumbers(n As Integer) As Integer
    Dim sum As Integer
    Dim i As Integer
    
    sum = 0
    
    For i = 1 To n
        sum = sum + i
    Next i
    
    Return sum
End Function

Sub Main()
    Dim total As Integer
    total = SumNumbers(100)
End Sub
";

            var csharpCode = CompileToCSharp(source, "Loops");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("Loops.cs", csharpCode);
        }

        // ====================================================================
        // Demo 4: Arrays
        // ====================================================================

        static void DemoArrays()
        {
            Console.WriteLine("Demo 4: Array Processing");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function FindMax(arr[10] As Integer) As Integer
    Dim max As Integer
    Dim i As Integer
    
    max = arr[0]
    
    For i = 1 To 9
        If arr[i] > max Then
            max = arr[i]
        End If
    Next i
    
    Return max
End Function

Sub Main()
    Dim numbers[10] As Integer
    Dim maximum As Integer
    maximum = FindMax(numbers)
End Sub
";

            var csharpCode = CompileToCSharp(source, "Arrays");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("Arrays.cs", csharpCode);
        }

        // ====================================================================
        // Demo 5: Complete Program
        // ====================================================================

        static void DemoCompleteProgram()
        {
            Console.WriteLine("Demo 5: Complete Program with Multiple Functions");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function IsPrime(n As Integer) As Boolean
    Dim i As Integer
    
    If n <= 1 Then
        Return False
    End If
    
    If n <= 3 Then
        Return True
    End If
    
    For i = 2 To n \ 2
        If n % i = 0 Then
            Return False
        End If
    Next i
    
    Return True
End Function

Function CountPrimes(max As Integer) As Integer
    Dim count As Integer
    Dim i As Integer
    
    count = 0
    
    For i = 2 To max
        If IsPrime(i) Then
            count = count + 1
        End If
    Next i
    
    Return count
End Function

Sub Main()
    Dim primeCount As Integer
    primeCount = CountPrimes(100)
End Sub
";

            var csharpCode = CompileToCSharp(source, "PrimeCounter");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("PrimeCounter.cs", csharpCode);
        }

        // ====================================================================
        // Demo 6: String Operations
        // ====================================================================

        static void DemoStringOperations()
        {
            Console.WriteLine("Demo 6: String Operations");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Sub Main()
    Dim text As String
    Dim length As Integer
    Dim upper As String
    Dim lower As String
    Dim sub1 As String
    Dim sub2 As String
    Dim sub3 As String
    Dim trimmed As String
    Dim pos As Integer
    Dim replaced As String

    text = ""  Hello World  ""
    trimmed = Trim(text)
    length = Len(trimmed)
    upper = UCase(trimmed)
    lower = LCase(trimmed)
    sub1 = Left(trimmed, 5)
    sub2 = Right(trimmed, 5)
    sub3 = Mid(trimmed, 7, 5)
    pos = InStr(trimmed, ""World"")
    replaced = Replace(trimmed, ""World"", ""BasicLang"")

    PrintLine(trimmed)
    PrintLine(length)
    PrintLine(upper)
    PrintLine(lower)
    PrintLine(sub1)
    PrintLine(sub2)
    PrintLine(sub3)
    PrintLine(pos)
    PrintLine(replaced)
End Sub
";

            var csharpCode = CompileToCSharp(source, "StringOps");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("StringOps.cs", csharpCode);
        }

        // ====================================================================
        // Demo 7: Math Operations
        // ====================================================================

        static void DemoMathOperations()
        {
            Console.WriteLine("Demo 7: Math Operations");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Sub Main()
    Dim x As Double
    Dim y As Double
    Dim result As Double

    x = -5.5
    y = 2.0

    PrintLine(Abs(x))
    PrintLine(Sqrt(16.0))
    PrintLine(Pow(2.0, 8.0))
    PrintLine(Floor(3.7))
    PrintLine(Ceiling(3.2))
    PrintLine(Round(3.5))
    PrintLine(Min(x, y))
    PrintLine(Max(x, y))
    PrintLine(Sin(0.0))
    PrintLine(Cos(0.0))
    PrintLine(Log(2.718281828))
    PrintLine(Exp(1.0))
End Sub
";

            var csharpCode = CompileToCSharp(source, "MathOps");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("MathOps.cs", csharpCode);
        }

        // ====================================================================
        // Demo 8: Type Conversion
        // ====================================================================

        static void DemoTypeConversion()
        {
            Console.WriteLine("Demo 8: Type Conversion");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Sub Main()
    Dim strNum As String
    Dim intVal As Integer
    Dim dblVal As Double
    Dim strResult As String
    Dim boolVal As Boolean

    strNum = ""42""
    intVal = CInt(strNum)
    dblVal = CDbl(""3.14159"")
    strResult = CStr(intVal)
    boolVal = CBool(1)

    PrintLine(intVal)
    PrintLine(dblVal)
    PrintLine(strResult)
    PrintLine(boolVal)
End Sub
";

            var csharpCode = CompileToCSharp(source, "ConversionOps");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("ConversionOps.cs", csharpCode);
        }

        // ====================================================================
        // Demo 9: Array Bounds and String Concatenation
        // ====================================================================

        static void DemoArrayBoundsAndConcat()
        {
            Console.WriteLine("Demo 9: Array Bounds and String Concatenation");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Sub Main()
    Dim arr[5] As Integer
    Dim lower As Integer
    Dim upper As Integer
    Dim greeting As String
    Dim name As String
    Dim message As String

    lower = LBound(arr)
    upper = UBound(arr)

    name = ""World""
    greeting = ""Hello, "" & name & ""!""

    PrintLine(lower)
    PrintLine(upper)
    PrintLine(greeting)
End Sub
";

            var csharpCode = CompileToCSharp(source, "ArrayBoundsConcat");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("ArrayBoundsConcat.cs", csharpCode);
        }

        // ====================================================================
        // Demo 10: Select Case
        // ====================================================================

        static void DemoSelectCase()
        {
            Console.WriteLine("Demo 10: Select Case");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function GetDayName(day As Integer) As String
    Dim result As String

    Select Case day
        Case 1
            result = ""Monday""
        Case 2
            result = ""Tuesday""
        Case 3
            result = ""Wednesday""
        Case 4
            result = ""Thursday""
        Case 5
            result = ""Friday""
        Case Else
            result = ""Weekend""
    End Select

    Return result
End Function

Sub Main()
    PrintLine(GetDayName(1))
    PrintLine(GetDayName(3))
    PrintLine(GetDayName(6))
End Sub
";

            var csharpCode = CompileToCSharp(source, "SelectCaseDemo");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("SelectCaseDemo.cs", csharpCode);
        }

        // ====================================================================
        // Demo 11: Exit Statements
        // ====================================================================

        static void DemoExitStatements()
        {
            Console.WriteLine("Demo 11: Exit Statements");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function FindFirst(arr[10] As Integer, target As Integer) As Integer
    Dim i As Integer
    Dim result As Integer

    result = -1

    For i = 0 To 9
        If arr[i] = target Then
            result = i
            Exit For
        End If
    Next i

    Return result
End Function

Sub Main()
    Dim numbers[10] As Integer
    Dim found As Integer

    numbers[0] = 5
    numbers[1] = 10
    numbers[2] = 15
    numbers[3] = 20
    numbers[4] = 25
    numbers[5] = 30
    numbers[6] = 35
    numbers[7] = 40
    numbers[8] = 45
    numbers[9] = 50

    found = FindFirst(numbers, 25)
    PrintLine(found)

    found = FindFirst(numbers, 100)
    PrintLine(found)
End Sub
";

            var csharpCode = CompileToCSharp(source, "ExitStatements");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("ExitStatements.cs", csharpCode);
        }

        // ====================================================================
        // Demo 12: Do...Loop Variations
        // ====================================================================

        static void DemoDoLoopVariations()
        {
            Console.WriteLine("Demo 12: Do...Loop Variations");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Sub Main()
    Dim count As Integer

    PrintLine(""Do While...Loop (condition at start):"")
    count = 1
    Do While count <= 3
        PrintLine(count)
        count = count + 1
    Loop

    PrintLine(""Do Until...Loop (condition at start):"")
    count = 1
    Do Until count > 3
        PrintLine(count)
        count = count + 1
    Loop

    PrintLine(""Do...Loop While (condition at end):"")
    count = 1
    Do
        PrintLine(count)
        count = count + 1
    Loop While count <= 3

    PrintLine(""Do...Loop Until (condition at end):"")
    count = 1
    Do
        PrintLine(count)
        count = count + 1
    Loop Until count > 3
End Sub
";

            var csharpCode = CompileToCSharp(source, "DoLoopDemo");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("DoLoopDemo.cs", csharpCode);
        }

        // ====================================================================
        // Demo 13: Random Numbers
        // ====================================================================

        static void DemoRandomNumbers()
        {
            Console.WriteLine("Demo 13: Random Numbers");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Sub Main()
    Dim i As Integer
    Dim rand As Double
    Dim diceRoll As Integer

    PrintLine(""Random numbers (0 to 1):"")
    For i = 1 To 5
        rand = Rnd()
        PrintLine(rand)
    Next i

    PrintLine(""Simulated dice rolls:"")
    For i = 1 To 6
        diceRoll = CInt(Floor(Rnd() * 6.0)) + 1
        PrintLine(diceRoll)
    Next i
End Sub
";

            var csharpCode = CompileToCSharp(source, "RandomDemo");

            Console.WriteLine("Generated C#:");
            Console.WriteLine(csharpCode);
            Console.WriteLine();

            SaveToFile("RandomDemo.cs", csharpCode);
        }

        // ====================================================================
        // Demo 14: C++ Backend
        // ====================================================================

        static void DemoCppBackend()
        {
            Console.WriteLine("Demo 14: C++ Backend (Multi-Target)");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function Factorial(n As Integer) As Integer
    If n <= 1 Then
        Return 1
    Else
        Return n * Factorial(n - 1)
    End If
End Function

Sub Main()
    Dim result As Integer
    result = Factorial(5)
    PrintLine(result)
End Sub
";

            var cppCode = CompileToCpp(source, "FactorialCpp");

            Console.WriteLine("Generated C++:");
            Console.WriteLine(cppCode);
            Console.WriteLine();

            SaveToFile("Factorial.cpp", cppCode);
        }

        // ====================================================================
        // Demo 15: LLVM Backend
        // ====================================================================

        static void DemoLLVMBackend()
        {
            Console.WriteLine("Demo 15: LLVM IR Backend (Native Compilation)");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function Square(x As Integer) As Integer
    Return x * x
End Function

Function SumSquares(n As Integer) As Integer
    Dim sum As Integer
    Dim i As Integer

    sum = 0
    For i = 1 To n
        sum = sum + Square(i)
    Next i

    Return sum
End Function

Sub Main()
    Dim result As Integer
    result = SumSquares(5)
    PrintLine(result)
End Sub
";

            var llvmCode = CompileToLLVM(source, "SumSquares");

            Console.WriteLine("Generated LLVM IR:");
            Console.WriteLine(llvmCode);
            Console.WriteLine();

            SaveToFile("SumSquares.ll", llvmCode);
        }

        // ====================================================================
        // Demo 16: MSIL Backend
        // ====================================================================

        static void DemoMSILBackend()
        {
            Console.WriteLine("Demo 16: MSIL Backend (.NET IL Assembly)");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
Function Multiply2(x As Integer) As Integer
    Return x * 2
End Function

Function AddDoubled(a As Integer, b As Integer) As Integer
    Dim result As Integer
    result = Multiply2(a) + Multiply2(b)
    Return result
End Function

Sub Main()
    Dim x As Integer
    Dim y As Integer
    Dim result As Integer

    x = 5
    y = 10
    result = AddDoubled(x, y)
    PrintLine(result)
End Sub
";

            var msilCode = CompileToMSIL(source, "DoubleSumDemo");

            Console.WriteLine("Generated MSIL:");
            Console.WriteLine(msilCode);
            Console.WriteLine();

            SaveToFile("DoubleSumDemo.il", msilCode);
        }

        // ====================================================================
        // Demo 17: Standard Library Abstraction
        // ====================================================================

        static void DemoStdLibAbstraction()
        {
            Console.WriteLine("Demo 17: Standard Library Abstraction (Multi-Backend)");
            Console.WriteLine("-".PadRight(70, '-'));

            // Initialize the registry
            StdLibRegistry.Initialize();

            // Show support matrix
            Console.WriteLine(StdLibRegistry.GenerateSupportMatrix());
            Console.WriteLine();

            // Demo stdlib functions across backends
            Console.WriteLine("Sample Stdlib Emissions by Backend:");
            Console.WriteLine("-".PadRight(40, '-'));

            var testFunctions = new[] { "PrintLine", "Sqrt", "Abs", "Len", "CInt" };
            var targets = new[] { TargetPlatform.CSharp, TargetPlatform.Cpp, TargetPlatform.LLVM, TargetPlatform.MSIL };

            foreach (var func in testFunctions)
            {
                Console.WriteLine($"\n{func}():");
                foreach (var target in targets)
                {
                    if (StdLibRegistry.CanHandle(target, func))
                    {
                        var emission = StdLibRegistry.EmitCall(target, func, "value");
                        Console.WriteLine($"  {target,-8}: {emission}");
                    }
                }
            }

            Console.WriteLine();

            // Show inline implementations for C++
            Console.WriteLine("\nC++ Inline Helper Functions:");
            Console.WriteLine("-".PadRight(40, '-'));

            var cppHelpers = new[] { "UCase", "LCase", "Trim", "Replace" };
            foreach (var func in cppHelpers)
            {
                var impl = StdLibRegistry.GetInlineImplementation(TargetPlatform.Cpp, func);
                if (!string.IsNullOrEmpty(impl))
                {
                    Console.WriteLine($"// {func}");
                    Console.WriteLine(impl.Trim());
                    Console.WriteLine();
                }
            }

            // Demo: Compile a program using stdlib to all backends
            Console.WriteLine("\nCompiling Stdlib Demo to All Backends:");
            Console.WriteLine("-".PadRight(40, '-'));

            string source = @"
Sub Main()
    Dim x As Double
    Dim result As Double

    x = 16.0
    result = Sqrt(x)
    PrintLine(result)

    result = Abs(-5.5)
    PrintLine(result)

    result = Pow(2, 8)
    PrintLine(result)
End Sub
";

            // Compile to C#
            var csharpCode = CompileToCSharp(source, "StdLibDemo");
            if (csharpCode != null)
            {
                Console.WriteLine("\n--- C# Output ---");
                Console.WriteLine(csharpCode.Substring(0, Math.Min(500, csharpCode.Length)) + "...");
                SaveToFile("StdLibDemo.cs", csharpCode);
            }

            // Compile to C++
            var cppCode = CompileToCpp(source, "StdLibDemo");
            if (cppCode != null)
            {
                Console.WriteLine("\n--- C++ Output ---");
                Console.WriteLine(cppCode.Substring(0, Math.Min(500, cppCode.Length)) + "...");
                SaveToFile("StdLibDemo.cpp", cppCode);
            }

            // Compile to LLVM
            var llvmCode = CompileToLLVM(source, "StdLibDemo");
            if (llvmCode != null)
            {
                Console.WriteLine("\n--- LLVM IR Output ---");
                Console.WriteLine(llvmCode.Substring(0, Math.Min(800, llvmCode.Length)) + "...");
                SaveToFile("StdLibDemo.ll", llvmCode);
            }

            // Compile to MSIL
            var msilCode = CompileToMSIL(source, "StdLibDemo");
            if (msilCode != null)
            {
                Console.WriteLine("\n--- MSIL Output ---");
                Console.WriteLine(msilCode.Substring(0, Math.Min(800, msilCode.Length)) + "...");
                SaveToFile("StdLibDemo.il", msilCode);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 18: Platform Externs
        // ====================================================================

        static void DemoPlatformExterns()
        {
            Console.WriteLine("Demo 18: Platform Externs (Native API Bindings)");
            Console.WriteLine("-".PadRight(70, '-'));

            // Demo: Parsing extern declarations
            Console.WriteLine("Platform externs allow declaring native APIs with per-backend implementations.");
            Console.WriteLine();

            string source = @"
' Platform extern declaration - maps to native APIs per backend
Extern Function ShowMessage(text As String) As Integer
    CSharp: ""System.Windows.Forms.MessageBox.Show""
    Cpp: ""MessageBoxA""
    LLVM: ""@printf""
    MSIL: ""System.Console::WriteLine""
End Extern

Extern Function GetCurrentTime() As Integer
    CSharp: ""Environment.TickCount""
    Cpp: ""time(nullptr)""
End Extern

Sub Main()
    Dim result As Integer
    result = ShowMessage(""Hello from BasicLang!"")
    PrintLine(""Message result: "")
    PrintLine(result)
End Sub
";

            Console.WriteLine("Source code with extern declarations:");
            Console.WriteLine("-".PadRight(40, '-'));
            Console.WriteLine(source);

            // Parse and show the AST
            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Show extern-related tokens
                Console.WriteLine("\nExtern-related tokens:");
                foreach (var token in tokens.Where(t =>
                    t.Type == TokenType.Extern ||
                    t.Type == TokenType.EndExtern ||
                    (t.Value?.ToString()?.Contains("CSharp") == true) ||
                    (t.Value?.ToString()?.Contains("Cpp") == true) ||
                    (t.Value?.ToString()?.Contains("LLVM") == true) ||
                    (t.Value?.ToString()?.Contains("MSIL") == true)))
                {
                    Console.WriteLine($"  {token.Type}: {token.Value}");
                }

                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Count externs
                var externCount = ast.Declarations.Count(d => d is ExternDeclarationNode);
                Console.WriteLine($"  - {externCount} extern declaration(s)");

                // Show extern details
                foreach (var decl in ast.Declarations.OfType<ExternDeclarationNode>())
                {
                    Console.WriteLine($"\n  Extern {(decl.IsFunction ? "Function" : "Sub")} {decl.Name}:");
                    Console.WriteLine($"    Parameters: {decl.Parameters.Count}");
                    if (decl.IsFunction)
                    {
                        Console.WriteLine($"    Return Type: {decl.ReturnType?.Name ?? "Void"}");
                    }
                    Console.WriteLine($"    Platform Implementations:");
                    foreach (var impl in decl.PlatformImplementations)
                    {
                        Console.WriteLine($"      {impl.Key}: {impl.Value}");
                    }
                }

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (semanticSuccess)
                {
                    Console.WriteLine($"✓ Semantic analysis passed");
                }
                else
                {
                    Console.WriteLine("✗ Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                }

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "PlatformExterns");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions, {irModule.ExternDeclarations.Count} externs");

                // Show IR externs
                Console.WriteLine("\nIR Extern Declarations:");
                foreach (var externDecl in irModule.ExternDeclarations.Values)
                {
                    Console.WriteLine($"  {externDecl.Name}: {externDecl.PlatformImplementations.Count} platform(s)");
                }

                // Generate code for each backend and show how extern is handled
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated Code Snippets (extern calls):");
                Console.WriteLine("-".PadRight(40, '-'));

                // C# output
                var csharpCode = CompileToCSharp(source, "PlatformExterns");
                if (csharpCode != null)
                {
                    Console.WriteLine("\n--- C# Output ---");
                    var snippet = ExtractExternCallSnippet(csharpCode, "ShowMessage");
                    Console.WriteLine(snippet ?? csharpCode.Substring(0, Math.Min(400, csharpCode.Length)) + "...");
                    SaveToFile("PlatformExterns.cs", csharpCode);
                }

                // C++ output
                var cppCode = CompileToCpp(source, "PlatformExterns");
                if (cppCode != null)
                {
                    Console.WriteLine("\n--- C++ Output ---");
                    var snippet = ExtractExternCallSnippet(cppCode, "ShowMessage");
                    Console.WriteLine(snippet ?? cppCode.Substring(0, Math.Min(400, cppCode.Length)) + "...");
                    SaveToFile("PlatformExterns.cpp", cppCode);
                }

                // LLVM output
                var llvmCode = CompileToLLVM(source, "PlatformExterns");
                if (llvmCode != null)
                {
                    Console.WriteLine("\n--- LLVM IR Output ---");
                    var snippet = ExtractExternCallSnippet(llvmCode, "ShowMessage");
                    Console.WriteLine(snippet ?? llvmCode.Substring(0, Math.Min(400, llvmCode.Length)) + "...");
                    SaveToFile("PlatformExterns.ll", llvmCode);
                }

                // MSIL output
                var msilCode = CompileToMSIL(source, "PlatformExterns");
                if (msilCode != null)
                {
                    Console.WriteLine("\n--- MSIL Output ---");
                    var snippet = ExtractExternCallSnippet(msilCode, "ShowMessage");
                    Console.WriteLine(snippet ?? msilCode.Substring(0, Math.Min(400, msilCode.Length)) + "...");
                    SaveToFile("PlatformExterns.il", msilCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        static string ExtractExternCallSnippet(string code, string functionName)
        {
            // Try to find a snippet containing the extern call
            var lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(functionName) || lines[i].Contains("ShowMessage"))
                {
                    // Return context around this line
                    int start = Math.Max(0, i - 2);
                    int end = Math.Min(lines.Length - 1, i + 2);
                    return string.Join("\n", lines.Skip(start).Take(end - start + 1));
                }
            }
            return null;
        }

        // ====================================================================
        // Demo 19: OOP Features (Classes, Constructors, Properties, Inheritance)
        // ====================================================================

        static void DemoOOPFeatures()
        {
            Console.WriteLine("Demo 19: OOP Features (Classes, Constructors, Properties, Inheritance)");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Base class with constructor, properties, and methods
Class Person
    Private _name As String
    Private _age As Integer

    ' Constructor
    Sub New(name As String, age As Integer)
        _name = name
        _age = age
    End Sub

    ' Property with Get and Set
    Property Name As String
        Get
            Return _name
        End Get
        Set(value As String)
            _name = value
        End Set
    End Property

    ' Read-only style property (only Get)
    Property Age As Integer
        Get
            Return _age
        End Get
        Set(value As Integer)
            _age = value
        End Set
    End Property

    ' Method using Me reference
    Public Function GetInfo() As String
        Return Me.Name & "" is "" & CStr(Me.Age) & "" years old""
    End Function

    ' Virtual method to get a greeting
    Public Overridable Function Greet() As String
        Return ""Hello, I am "" & _name
    End Function

    ' Static factory method
    Public Shared Function CreateDefault() As Person
        Return New Person(""Unknown"", 0)
    End Function
End Class

' Derived class with inheritance
Class Employee Inherits Person
    Private _department As String

    ' Constructor calling base constructor
    Sub New(name As String, age As Integer, dept As String)
        MyBase.New(name, age)
        _department = dept
    End Sub

    ' Property for department
    Property Department As String
        Get
            Return _department
        End Get
        Set(value As String)
            _department = value
        End Set
    End Property

    ' Override virtual method
    Public Overrides Function Greet() As String
        Return MyBase.Greet() & "" from "" & _department
    End Function
End Class

Sub Main()
    ' Create instances
    Dim person As Person
    Dim employee As Employee

    person = New Person(""John"", 30)
    employee = New Employee(""Jane"", 25, ""Engineering"")

    ' Use properties
    PrintLine(person.Name)
    PrintLine(person.Age)

    ' Use Me reference in method
    PrintLine(person.GetInfo())

    ' Modify via property
    person.Name = ""Johnny""
    PrintLine(person.Name)

    ' Use inherited class with properties
    PrintLine(employee.Department)
    PrintLine(employee.Greet())

    ' Static method call
    Dim defaultPerson As Person
    defaultPerson = Person.CreateDefault()
End Sub
";

            Console.WriteLine("Source code with OOP features:");
            Console.WriteLine("-".PadRight(40, '-'));
            Console.WriteLine(source);

            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Show OOP-related tokens
                Console.WriteLine("\nOOP-related tokens:");
                foreach (var token in tokens.Where(t =>
                    t.Type == TokenType.Class ||
                    t.Type == TokenType.EndClass ||
                    t.Type == TokenType.Inherits ||
                    t.Type == TokenType.Property ||
                    t.Type == TokenType.EndProperty ||
                    t.Type == TokenType.Get ||
                    t.Type == TokenType.EndGet ||
                    t.Type == TokenType.Set ||
                    t.Type == TokenType.EndSet ||
                    t.Type == TokenType.Me ||
                    t.Type == TokenType.MyBase ||
                    t.Type == TokenType.Shared ||
                    t.Type == TokenType.Overridable ||
                    t.Type == TokenType.Overrides))
                {
                    Console.WriteLine($"  {token.Type}: {token.Value}");
                }

                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Count classes
                var classCount = ast.Declarations.Count(d => d is ClassNode);
                Console.WriteLine($"  - {classCount} class declaration(s)");

                // Show class details
                foreach (var decl in ast.Declarations.OfType<ClassNode>())
                {
                    Console.WriteLine($"\n  Class {decl.Name}:");
                    if (!string.IsNullOrEmpty(decl.BaseClass))
                    {
                        Console.WriteLine($"    Inherits: {decl.BaseClass}");
                    }
                    Console.WriteLine($"    Members: {decl.Members.Count}");

                    // Count member types
                    var constructorCount = decl.Members.Count(m => m is ConstructorNode);
                    var propertyCount = decl.Members.Count(m => m is PropertyNode);
                    var methodCount = decl.Members.Count(m => m is FunctionNode || m is SubroutineNode);
                    var fieldCount = decl.Members.Count(m => m is VariableDeclarationNode);

                    Console.WriteLine($"      - {constructorCount} constructor(s)");
                    Console.WriteLine($"      - {propertyCount} property/properties");
                    Console.WriteLine($"      - {methodCount} method(s)");
                    Console.WriteLine($"      - {fieldCount} field(s)");
                }

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (semanticSuccess)
                {
                    Console.WriteLine($"✓ Semantic analysis passed");
                }
                else
                {
                    Console.WriteLine("✗ Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                }

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "OOPFeatures");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions, {irModule.Classes.Count} classes");

                // Show IR class info
                Console.WriteLine("\nIR Class Structures:");
                foreach (var irClass in irModule.Classes.Values)
                {
                    Console.WriteLine($"  {irClass.Name}:");
                    Console.WriteLine($"    Base: {irClass.BaseClass ?? "(none)"}");
                    Console.WriteLine($"    Fields: {irClass.Fields.Count}");
                    Console.WriteLine($"    Constructors: {irClass.Constructors.Count}");
                    Console.WriteLine($"    Properties: {irClass.Properties.Count}");
                    Console.WriteLine($"    Methods: {irClass.Methods.Count}");
                }

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "OOPFeatures");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("OOPFeatures.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 20: Advanced Language Features (Generics, Lambdas, Exception Handling)
        // ====================================================================

        static void DemoAdvancedFeatures()
        {
            Console.WriteLine("Demo 20: Advanced Language Features (Generics, Lambdas, Exception Handling)");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' ============================================
' Demo: Generics, Lambdas, and Exception Handling
' ============================================

' Generic class definition
Class Stack(Of T)
    Private items(100) As T
    Private count As Integer

    Sub New()
        count = 0
    End Sub

    Sub Push(item As T)
        items(count) = item
        count = count + 1
    End Sub

    Function Pop() As T
        count = count - 1
        Return items(count)
    End Function

    Function IsEmpty() As Boolean
        Return count = 0
    End Function
End Class

' Generic function
Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    Else
        Return b
    End If
End Function

' Function that demonstrates exception handling
Sub ProcessData(value As Integer)
    Try
        If value < 0 Then
            Throw New Exception(""Value cannot be negative"")
        End If
        PrintLine(""Processing: "" & Str(value))
    Catch ex As Exception
        PrintLine(""Error: "" & ex.Message)
    Finally
        PrintLine(""Cleanup complete"")
    End Try
End Sub

' Main program demonstrating all features
Sub Main()
    PrintLine(""=== Generics Demo ==="")

    ' Using generic class
    Dim intStack As Stack(Of Integer)
    intStack = New Stack(Of Integer)()
    intStack.Push(10)
    intStack.Push(20)
    intStack.Push(30)
    PrintLine(""Popped: "" & Str(intStack.Pop()))

    ' Using generic function
    Dim maxVal As Integer
    maxVal = Max(Of Integer)(42, 17)
    PrintLine(""Max value: "" & Str(maxVal))

    PrintLine("""")
    PrintLine(""=== Exception Handling Demo ==="")

    ' Normal processing
    ProcessData(100)

    ' This will throw and catch an exception
    ProcessData(-5)

    PrintLine("""")
    PrintLine(""=== Lambda Demo ==="")

    ' Lambda expressions (conceptual - showing syntax)
    ' Dim square = Function(x As Integer) x * x
    ' Dim doubled = Function(x) x * 2
    PrintLine(""Lambda syntax supported: Function(x) x * 2"")
End Sub
";

            Console.WriteLine("Source code with advanced features:");
            Console.WriteLine("-".PadRight(40, '-'));
            Console.WriteLine(source);

            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Show relevant tokens
                Console.WriteLine("\nAdvanced feature tokens found:");
                var relevantTypes = new[] {
                    TokenType.Of, TokenType.Try, TokenType.Catch, TokenType.Finally,
                    TokenType.Throw, TokenType.EndTry, TokenType.Function
                };
                foreach (var token in tokens.Where(t => relevantTypes.Contains(t.Type)))
                {
                    Console.WriteLine($"  {token.Type}: {token.Value ?? token.Lexeme}");
                }

                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"\n✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Count declarations by type
                var classCount = ast.Declarations.Count(d => d is ClassNode);
                var funcCount = ast.Declarations.Count(d => d is FunctionNode);
                var subCount = ast.Declarations.Count(d => d is SubroutineNode);
                Console.WriteLine($"  - {classCount} class(es)");
                Console.WriteLine($"  - {funcCount} function(s)");
                Console.WriteLine($"  - {subCount} subroutine(s)");

                // Show generic class details
                foreach (var decl in ast.Declarations.OfType<ClassNode>())
                {
                    if (decl.GenericParameters.Count > 0)
                    {
                        Console.WriteLine($"\n  Generic Class: {decl.Name}<{string.Join(", ", decl.GenericParameters)}>");
                    }
                }

                // Show generic function details
                foreach (var decl in ast.Declarations.OfType<FunctionNode>())
                {
                    if (decl.GenericParameters.Count > 0)
                    {
                        Console.WriteLine($"  Generic Function: {decl.Name}<{string.Join(", ", decl.GenericParameters)}>");
                    }
                }

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (semanticSuccess)
                {
                    Console.WriteLine($"✓ Semantic analysis passed");
                }
                else
                {
                    Console.WriteLine("✗ Semantic analysis had warnings/errors:");
                    foreach (var error in semanticAnalyzer.Errors.Take(5))
                    {
                        Console.WriteLine($"  {error}");
                    }
                }

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "AdvancedFeatures");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions, {irModule.Classes.Count} classes");

                // Show IR info
                Console.WriteLine("\nIR Details:");
                foreach (var func in irModule.Functions)
                {
                    var genericInfo = func.GenericParameters.Count > 0
                        ? $"<{string.Join(", ", func.GenericParameters)}>"
                        : "";
                    Console.WriteLine($"  Function: {func.Name}{genericInfo}");
                }
                foreach (var irClass in irModule.Classes.Values)
                {
                    var genericInfo = irClass.GenericParameters.Count > 0
                        ? $"<{string.Join(", ", irClass.GenericParameters)}>"
                        : "";
                    Console.WriteLine($"  Class: {irClass.Name}{genericInfo}");
                }

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "AdvancedFeatures");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("AdvancedFeatures.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 21: Async/Await and Iterators
        // ====================================================================

        static void DemoAsyncAndIterators()
        {
            Console.WriteLine("Demo 21: Async/Await and Iterators");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Async Functions and Iterators

' Async function that simulates an HTTP call
Async Function GetStringAsync(url As String) As String
    Return ""Response from: "" & url
End Function

' Async function example
Async Function FetchDataAsync(url As String) As String
    Dim result As String
    result = Await GetStringAsync(url)
    Return result
End Function

' Async subroutine example
Async Sub ProcessAsync()
    Dim data As String
    data = Await FetchDataAsync(""https://example.com/api"")
    PrintLine(data)
End Sub

' Simple iterator function
Iterator Function CountTo(max As Integer) As Integer
    Dim i As Integer
    For i = 1 To max
        Yield i
    Next
End Function

Sub Main()
    PrintLine(""Async/Await and Iterator Demo"")
    ProcessAsync()
End Sub
";

            Console.WriteLine("Source code with Async/Await and Iterators:");
            Console.WriteLine("-".PadRight(40, '-'));
            Console.WriteLine(source);

            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"\n✓ Lexical analysis: {tokens.Count} tokens");

                // Show relevant tokens
                Console.WriteLine("\nAsync/Iterator tokens found:");
                var relevantTypes = new[] {
                    TokenType.Async, TokenType.Await, TokenType.Iterator, TokenType.Yield
                };
                foreach (var token in tokens.Where(t => relevantTypes.Contains(t.Type)))
                {
                    Console.WriteLine($"  {token.Type}: {token.Value ?? token.Lexeme}");
                }

                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"\n✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Count declarations by type and features
                var asyncFuncs = ast.Declarations.OfType<FunctionNode>().Where(f => f.IsAsync).ToList();
                var asyncSubs = ast.Declarations.OfType<SubroutineNode>().Where(s => s.IsAsync).ToList();
                var iteratorFuncs = ast.Declarations.OfType<FunctionNode>().Where(f => f.IsIterator).ToList();

                Console.WriteLine($"  - {asyncFuncs.Count} async function(s)");
                foreach (var f in asyncFuncs)
                    Console.WriteLine($"      {f.Name}");
                Console.WriteLine($"  - {asyncSubs.Count} async subroutine(s)");
                foreach (var s in asyncSubs)
                    Console.WriteLine($"      {s.Name}");
                Console.WriteLine($"  - {iteratorFuncs.Count} iterator function(s)");
                foreach (var f in iteratorFuncs)
                    Console.WriteLine($"      {f.Name}");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors, {semanticAnalyzer.Errors.Count(e => e.Severity == ErrorSeverity.Warning)} warnings");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "AsyncIteratorDemo");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "AsyncIteratorDemo");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("AsyncIteratorDemo.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 22: Operator Overloading and Namespaces
        // ====================================================================

        static void DemoOperatorOverloading()
        {
            Console.WriteLine("Demo 22: Operator Overloading and Namespaces");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Operator Overloading and Namespaces
' Shows how to define custom operators and organize code in namespaces

Namespace MathLibrary

Class Vector2D
    Private _x As Double
    Private _y As Double

    Sub New(x As Double, y As Double)
        _x = x
        _y = y
    End Sub

    Property X As Double
        Get
            Return _x
        End Get
        Set(value As Double)
            _x = value
        End Set
    End Property

    Property Y As Double
        Get
            Return _y
        End Get
        Set(value As Double)
            _y = value
        End Set
    End Property

    ' Operator overloads
    Public Shared Operator +(left As Vector2D, right As Vector2D) As Vector2D
        Return New Vector2D(left.X + right.X, left.Y + right.Y)
    End Operator

    Public Shared Operator -(left As Vector2D, right As Vector2D) As Vector2D
        Return New Vector2D(left.X - right.X, left.Y - right.Y)
    End Operator

    Public Shared Operator *(vec As Vector2D, scalar As Double) As Vector2D
        Return New Vector2D(vec.X * scalar, vec.Y * scalar)
    End Operator

    Public Overridable Function ToString() As String
        Return ""("" & _x & "", "" & _y & "")""
    End Function
End Class

End Namespace

Sub Main()
    ' Create two vectors
    Dim v1 As Vector2D = New Vector2D(3.0, 4.0)
    Dim v2 As Vector2D = New Vector2D(1.0, 2.0)

    ' Note: Binary operator usage in expressions will be fully supported
    ' after semantic analyzer recognizes operator overloads
    PrintLine(""Vector2D class with operators +, -, * defined"")
    PrintLine(""v1 = "" & v1.ToString())
    PrintLine(""v2 = "" & v2.ToString())
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Find namespaces and operator declarations
                var namespaces = ast.Declarations.OfType<NamespaceNode>().ToList();
                if (namespaces.Any())
                {
                    Console.WriteLine($"  - {namespaces.Count} namespace(s) declared:");
                    foreach (var ns in namespaces)
                    {
                        Console.WriteLine($"      {ns.Name}");
                        // Find classes in namespace
                        var classesInNs = ns.Members.OfType<ClassNode>().ToList();
                        foreach (var cls in classesInNs)
                        {
                            var operators = cls.Members.OfType<OperatorDeclarationNode>().ToList();
                            Console.WriteLine($"      └─ Class '{cls.Name}' has {operators.Count} operator(s)");
                        }
                    }
                }

                var classes = ast.Declarations.OfType<ClassNode>().ToList();
                foreach (var cls in classes)
                {
                    var operators = cls.Members.OfType<OperatorDeclarationNode>().ToList();
                    if (operators.Any())
                    {
                        Console.WriteLine($"  - Class '{cls.Name}' has {operators.Count} operator(s):");
                        foreach (var op in operators)
                        {
                            Console.WriteLine($"      operator {op.OperatorSymbol}");
                        }
                    }
                }

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "OperatorOverloading");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "OperatorOverloading");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("OperatorOverloading.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 23: Interfaces, Enums, and Delegates
        // ====================================================================

        static void DemoInterfacesEnumsDelegates()
        {
            Console.WriteLine("Demo 23: Interfaces, Enums, and Delegates");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Interfaces, Enums, and Delegates

' Define an interface
Interface IShape
    Function GetArea() As Double
    Function GetPerimeter() As Double
End Interface

' Define an enum
Enum Color
    Red
    Green = 5
    Blue
End Enum

Enum DayOfWeek As Integer
    Sunday = 0
    Monday
    Tuesday
    Wednesday
    Thursday
    Friday
    Saturday
End Enum

' Define a delegate
Delegate Function MathOperation(a As Integer, b As Integer) As Integer
Delegate Sub EventHandler(sender As Object, args As String)

' Class implementing interface
Class Circle
    Private _radius As Double

    Sub New(radius As Double)
        _radius = radius
    End Sub

    Public Function GetArea() As Double
        Return 3.14159 * _radius * _radius
    End Function

    Public Function GetPerimeter() As Double
        Return 2 * 3.14159 * _radius
    End Function
End Class

Sub Main()
    Dim circle As Circle = New Circle(5.0)
    PrintLine(""Circle area: "" & circle.GetArea())
    PrintLine(""Circle perimeter: "" & circle.GetPerimeter())
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Count types
                var interfaces = ast.Declarations.OfType<InterfaceNode>().ToList();
                var enums = ast.Declarations.OfType<EnumNode>().ToList();
                var delegates = ast.Declarations.OfType<DelegateDeclarationNode>().ToList();

                Console.WriteLine($"  - {interfaces.Count} interface(s): {string.Join(", ", interfaces.Select(i => i.Name))}");
                Console.WriteLine($"  - {enums.Count} enum(s): {string.Join(", ", enums.Select(e => e.Name))}");
                Console.WriteLine($"  - {delegates.Count} delegate(s): {string.Join(", ", delegates.Select(d => d.Name))}");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "InterfacesEnumsDelegates");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions, {irModule.Interfaces.Count} interfaces, {irModule.Enums.Count} enums, {irModule.Delegates.Count} delegates");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "InterfacesEnumsDelegates");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("InterfacesEnumsDelegates.cs", csharpCode);
                }

                // Generate C++ code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C++ Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var cppCode = CompileToCpp(source, "InterfacesEnumsDelegates");
                if (cppCode != null)
                {
                    Console.WriteLine(cppCode);
                    SaveToFile("InterfacesEnumsDelegates.cpp", cppCode);
                }

                // Generate LLVM IR
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated LLVM IR:");
                Console.WriteLine("-".PadRight(40, '-'));

                var llvmCode = CompileToLLVM(source, "InterfacesEnumsDelegates");
                if (llvmCode != null)
                {
                    Console.WriteLine(llvmCode);
                    SaveToFile("InterfacesEnumsDelegates.ll", llvmCode);
                }

                // Generate MSIL
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated MSIL:");
                Console.WriteLine("-".PadRight(40, '-'));

                var msilCode = CompileToMSIL(source, "InterfacesEnumsDelegates");
                if (msilCode != null)
                {
                    Console.WriteLine(msilCode);
                    SaveToFile("InterfacesEnumsDelegates.il", msilCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 24: Events
        // ====================================================================

        static void DemoEvents()
        {
            Console.WriteLine("Demo 24: Events");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Events in Classes

' Define a delegate for the event
Delegate Sub ClickHandler(sender As Object, x As Integer, y As Integer)
Delegate Sub MessageHandler(message As String)

' Button class with events
Class Button
    Private _text As String

    ' Declare events
    Public Event Click As ClickHandler
    Public Event MouseEnter As MessageHandler

    Sub New(text As String)
        _text = text
    End Sub

    Property Text As String
        Get
            Return _text
        End Get
        Set(value As String)
            _text = value
        End Set
    End Property

    ' Method that raises the Click event
    Public Sub OnClick(x As Integer, y As Integer)
        PrintLine(""Button '"" & _text & ""' clicked at ("" & x & "", "" & y & "")"")
        ' Note: RaiseEvent would invoke the event handlers
    End Sub

    ' Method that raises the MouseEnter event
    Public Sub OnMouseEnter()
        PrintLine(""Mouse entered button '"" & _text & ""'"")
        ' Note: RaiseEvent would invoke the event handlers
    End Sub
End Class

' Main program
Sub Main()
    Dim btn As Button = New Button(""Submit"")

    PrintLine(""Button text: "" & btn.Text)

    ' Simulate events
    btn.OnClick(100, 200)
    btn.OnMouseEnter()
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Count types
                var delegates = ast.Declarations.OfType<DelegateDeclarationNode>().ToList();
                var classes = ast.Declarations.OfType<ClassNode>().ToList();

                Console.WriteLine($"  - {delegates.Count} delegate(s): {string.Join(", ", delegates.Select(d => d.Name))}");
                Console.WriteLine($"  - {classes.Count} class(es): {string.Join(", ", classes.Select(c => c.Name))}");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "EventDemo");

                // Count events in classes
                int totalEvents = irModule.Classes.Values.Sum(c => c.Events.Count);
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions, {irModule.Classes.Count} classes, {totalEvents} events");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "EventDemo");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("EventDemo.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 25: Extension Methods
        // ====================================================================

        static void DemoExtensionMethods()
        {
            Console.WriteLine("Demo 25: Extension Methods");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Extension Methods

' Extension method for Double - squares the value
Extension Function Squared(value As Double) As Double
    Return value * value
End Function

' Extension method for Integer - doubles the value
Extension Function Doubled(value As Integer) As Integer
    Return value + value
End Function

Sub Main()
    Dim num As Integer = 5
    Dim d As Double = 3.5

    PrintLine(""5 doubled: "" & Doubled(num))
    PrintLine(""3.5 squared: "" & Squared(d))
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Count extension methods
                var extensionMethods = ast.Declarations.OfType<ExtensionMethodNode>().ToList();
                Console.WriteLine($"  - {extensionMethods.Count} extension method(s): {string.Join(", ", extensionMethods.Select(e => e.Method?.Name))}");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "ExtensionMethods");

                // Count extension methods in IR
                int extMethodCount = irModule.Functions.Count(f => f.IsExtension);
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions ({extMethodCount} extension methods)");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "ExtensionMethods");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("ExtensionMethods.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 26: LINQ-style Queries
        // ====================================================================

        static void DemoLinqQueries()
        {
            Console.WriteLine("Demo 26: LINQ-style Queries");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: LINQ-style Queries
' Note: LINQ tokens are recognized, full method chain generation is in progress

Sub Main()
    Dim numbers[10] As Integer

    ' Initialize array
    numbers[0] = 5
    numbers[1] = 2
    numbers[2] = 8
    numbers[3] = 1
    numbers[4] = 9

    ' LINQ query expression is parsed (tokens recognized)
    ' Full code gen would use method chains like:
    ' numbers.Where(n => n > 5).Select(n => n)

    PrintLine(""LINQ infrastructure is in place"")
    PrintLine(""Query tokens: From, Where, Select, OrderBy, etc."")
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "LinqQueries");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "LinqQueries");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("LinqQueries.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 27: Pattern Matching
        // ====================================================================

        static void DemoPatternMatching()
        {
            Console.WriteLine("Demo 27: Pattern Matching");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Pattern Matching with Select Case

Sub Main()
    ' Simple value patterns
    Dim day As Integer = 3
    Select Case day
        Case 1
            PrintLine(""Monday"")
        Case 2
            PrintLine(""Tuesday"")
        Case 3
            PrintLine(""Wednesday"")
        Case 4
            PrintLine(""Thursday"")
        Case 5
            PrintLine(""Friday"")
        Case 6
            PrintLine(""Saturday"")
        Case 7
            PrintLine(""Sunday"")
        Case Else
            PrintLine(""Invalid day"")
    End Select

    ' String pattern matching
    Dim command As String = ""save""
    Select Case command
        Case ""open""
            PrintLine(""Opening file..."")
        Case ""save""
            PrintLine(""Saving file..."")
        Case ""close""
            PrintLine(""Closing file..."")
        Case Else
            PrintLine(""Unknown command"")
    End Select
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "PatternMatching");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "PatternMatching");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("PatternMatching.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Demo 28: Optional Parameters, ParamArray, and With Blocks
        // ====================================================================

        static void DemoOptionalAndParamArray()
        {
            Console.WriteLine("Demo 28: Optional Parameters, ParamArray, and With Blocks");
            Console.WriteLine("-".PadRight(70, '-'));

            string source = @"
' Demo: Optional Parameters, ParamArray, and With Block Improvements

' Class to demonstrate With block
Class Person
    Public Name As String
    Public Age As Integer

    Sub New(n As String, a As Integer)
        Name = n
        Age = a
    End Sub
End Class

' Function with optional parameters
Function Greet(name As String, Optional greeting As String = ""Hello"", Optional excited As Boolean = False) As String
    Dim result As String
    result = greeting & "", "" & name
    If excited Then
        result = result & ""!""
    End If
    Return result
End Function

' Function with ParamArray for variable arguments
' Note: ParamArray receives an array, demonstrated via C# code generation
Function Sum(ParamArray numbers As Integer) As Integer
    Return 0
End Function

' Subroutine with ByRef parameter
Sub Increment(ByRef value As Integer)
    value = value + 1
End Sub

Sub Main()
    PrintLine(""=== Optional Parameters Demo ==="")

    ' Using all defaults
    PrintLine(Greet(""World""))

    ' Providing custom greeting
    PrintLine(Greet(""User"", ""Hi""))

    ' Providing all arguments
    PrintLine(Greet(""Developer"", ""Welcome"", True))

    PrintLine("""")
    PrintLine(""=== ParamArray Demo ==="")

    ' ParamArray allows variable number of arguments
    Dim result As Integer
    result = Sum(1, 2, 3)
    PrintLine(result)

    PrintLine("""")
    PrintLine(""=== ByRef Parameter Demo ==="")

    Dim counter As Integer = 10
    PrintLine(counter)
    Increment(counter)
    PrintLine(counter)
End Sub
";

            try
            {
                // Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Check for Optional and ParamArray tokens
                var optionalTokens = tokens.Where(t => t.Type == TokenType.Optional).ToList();
                var paramArrayTokens = tokens.Where(t => t.Type == TokenType.ParamArray).ToList();
                var byRefTokens = tokens.Where(t => t.Type == TokenType.ByRef).ToList();

                if (optionalTokens.Count > 0 || paramArrayTokens.Count > 0 || byRefTokens.Count > 0)
                {
                    Console.WriteLine($"  Found: {optionalTokens.Count} Optional, {paramArrayTokens.Count} ParamArray, {byRefTokens.Count} ByRef tokens");
                }

                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"✓ Parsing: {ast.Declarations.Count} declarations");

                // Semantic analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (semanticSuccess)
                {
                    Console.WriteLine($"✓ Semantic analysis passed");
                }
                else
                {
                    Console.WriteLine($"✗ Semantic analysis had warnings/errors:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                }

                // IR generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "OptionalParamArray");
                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Check IR for optional/paramarray parameters
                foreach (var func in irModule.Functions)
                {
                    var optParams = func.Parameters.Where(p => p.IsOptional).ToList();
                    var paramArrayParams = func.Parameters.Where(p => p.IsParamArray).ToList();
                    var byRefParams = func.Parameters.Where(p => p.IsByRef).ToList();

                    if (optParams.Count > 0 || paramArrayParams.Count > 0 || byRefParams.Count > 0)
                    {
                        Console.WriteLine($"  {func.Name}: {optParams.Count} optional, {paramArrayParams.Count} ParamArray, {byRefParams.Count} ByRef params");
                    }
                }

                // Generate C# code
                Console.WriteLine("\n" + "-".PadRight(40, '-'));
                Console.WriteLine("Generated C# Code:");
                Console.WriteLine("-".PadRight(40, '-'));

                var csharpCode = CompileToCSharp(source, "OptionalParamArray");
                if (csharpCode != null)
                {
                    Console.WriteLine(csharpCode);
                    SaveToFile("OptionalParamArray.cs", csharpCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
        }

        // ====================================================================
        // Compilation Pipeline
        // ====================================================================

        static string CompileToCSharp(string source, string className)
        {
            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                Console.WriteLine($"✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (!semanticSuccess)
                {
                    Console.WriteLine("✗ Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    return null;
                }

                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, className);

                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Phase 5: Optimization
                var optimizer = new OptimizationPipeline();
                optimizer.AddStandardPasses();

                var optimizationResult = optimizer.Run(irModule);

                Console.WriteLine($"✓ Optimization: {optimizationResult.TotalModifications} improvements");

                // Phase 6: C# Code Generation
                var codeGenOptions = new CodeGenOptions
                {
                    Namespace = "GeneratedCode",
                    ClassName = className,
                    GenerateMainMethod = true
                };

                var csharpGenerator = new CSharpCodeGenerator(codeGenOptions);
                var csharpCode = csharpGenerator.Generate(irModule);

                Console.WriteLine($"✓ C# code generation: {csharpCode.Length} characters");

                return csharpCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Compilation failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Compile BasicLang source to C++
        /// </summary>
        static string CompileToCpp(string source, string className)
        {
            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                Console.WriteLine($"✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (!semanticSuccess)
                {
                    Console.WriteLine("✗ Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    return null;
                }

                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, className);

                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Phase 5: Optimization
                var optimizer = new OptimizationPipeline();
                optimizer.AddStandardPasses();

                var optimizationResult = optimizer.Run(irModule);

                Console.WriteLine($"✓ Optimization: {optimizationResult.TotalModifications} improvements");

                // Phase 6: C++ Code Generation
                var cppOptions = new CppCodeGenOptions
                {
                    GenerateComments = true,
                    GenerateMainFunction = true
                };

                var cppGenerator = new CppCodeGenerator(cppOptions);
                var cppCode = cppGenerator.Generate(irModule);

                Console.WriteLine($"✓ C++ code generation: {cppCode.Length} characters");

                return cppCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Compilation failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Compile BasicLang source to LLVM IR
        /// </summary>
        static string CompileToLLVM(string source, string className)
        {
            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                Console.WriteLine($"✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (!semanticSuccess)
                {
                    Console.WriteLine("✗ Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    return null;
                }

                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, className);

                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Phase 5: Optimization
                var optimizer = new OptimizationPipeline();
                optimizer.AddStandardPasses();

                var optimizationResult = optimizer.Run(irModule);

                Console.WriteLine($"✓ Optimization: {optimizationResult.TotalModifications} improvements");

                // Phase 6: LLVM IR Code Generation
                var llvmOptions = new LLVMCodeGenOptions
                {
                    GenerateComments = true
                };

                var llvmGenerator = new LLVMCodeGenerator(llvmOptions);
                var llvmCode = llvmGenerator.Generate(irModule);

                Console.WriteLine($"✓ LLVM IR generation: {llvmCode.Length} characters");

                return llvmCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Compilation failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Compile BasicLang source to MSIL (.NET IL)
        /// </summary>
        static string CompileToMSIL(string source, string className)
        {
            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                Console.WriteLine($"✓ Lexical analysis: {tokens.Count} tokens");

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                Console.WriteLine($"✓ Parsing: AST with {ast.Declarations.Count} declarations");

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);

                if (!semanticSuccess)
                {
                    Console.WriteLine("✗ Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    return null;
                }

                Console.WriteLine($"✓ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, className);

                Console.WriteLine($"✓ IR generation: {irModule.Functions.Count} functions");

                // Phase 5: Optimization
                var optimizer = new OptimizationPipeline();
                optimizer.AddStandardPasses();

                var optimizationResult = optimizer.Run(irModule);

                Console.WriteLine($"✓ Optimization: {optimizationResult.TotalModifications} improvements");

                // Phase 6: MSIL Code Generation
                var msilOptions = new MSILCodeGenOptions
                {
                    GenerateComments = true,
                    AssemblyName = className
                };

                var msilGenerator = new MSILCodeGenerator(msilOptions);
                var msilCode = msilGenerator.Generate(irModule);

                Console.WriteLine($"✓ MSIL generation: {msilCode.Length} characters");

                return msilCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Compilation failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Generic compile method using BackendRegistry
        /// </summary>
        static string Compile(string source, string className, TargetPlatform target)
        {
            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                if (!semanticAnalyzer.Analyze(ast))
                {
                    return null;
                }

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, className);

                // Phase 5: Optimization
                var optimizer = new OptimizationPipeline();
                optimizer.AddStandardPasses();
                optimizer.Run(irModule);

                // Phase 6: Code Generation using BackendRegistry
                var options = new CodeGenOptions
                {
                    Namespace = "GeneratedCode",
                    ClassName = className,
                    GenerateMainMethod = true,
                    TargetBackend = target
                };

                var generator = BackendRegistry.Create(target, options);
                return generator.Generate(irModule);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Compilation failed: {ex.Message}");
                return null;
            }
        }

        static void SaveToFile(string filename, string content)
        {
            if (content == null) return;
            
            try
            {
                string outputPath = Path.Combine("GeneratedCode", filename);
                Directory.CreateDirectory("GeneratedCode");
                File.WriteAllText(outputPath, content);
                Console.WriteLine($"✓ Saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to save file: {ex.Message}");
            }
        }
    }
}
