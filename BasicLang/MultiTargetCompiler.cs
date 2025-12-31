using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.Driver
{
    /// <summary>
    /// Unified compiler driver supporting multiple backend targets
    /// </summary>
    public class MultiTargetCompiler
    {
        private readonly Dictionary<string, Func<ICodeGenerator>> _backendFactories;
        private readonly CompilerOptions _options;

        public MultiTargetCompiler(CompilerOptions options = null)
        {
            _options = options ?? new CompilerOptions();
            _backendFactories = new Dictionary<string, Func<ICodeGenerator>>(StringComparer.OrdinalIgnoreCase);
            RegisterDefaultBackends();
        }

        private void RegisterDefaultBackends()
        {
            // Register C# backend factory
            RegisterBackend("csharp", () =>
            {
                var csharpOptions = new CodeGenOptions
                {
                    Namespace = _options.Namespace,
                    ClassName = _options.ClassName,
                    GenerateMainMethod = _options.GenerateMainMethod,
                    GenerateComments = _options.GenerateComments
                };
                return new CSharpCodeGenerator(csharpOptions);
            });

            // Register C++ backend factory
            RegisterBackend("cpp", () =>
            {
                var cppOptions = new CppCodeGenOptions
                {
                    Namespace = _options.Namespace,
                    GenerateMainFunction = _options.GenerateMainMethod,
                    GenerateComments = _options.GenerateComments
                };
                return new CppCodeGenerator(cppOptions);
            });
        }

        public void RegisterBackend(string name, Func<ICodeGenerator> factory)
        {
            _backendFactories[name.ToLower()] = factory;
            var testGenerator = factory();
            Console.WriteLine($"âœ“ Registered backend: {testGenerator.BackendName}");
        }

        /// <summary>
        /// Compile source code to a specific target
        /// </summary>
        public CompilationResult Compile(string source, string targetBackend)
        {
            if (!_backendFactories.TryGetValue(targetBackend.ToLower(), out var factory))
            {
                return CompilationResult.CreateError($"Unknown backend: {targetBackend}");
            }

            try
            {
                var generator = factory();

                Console.WriteLine($"\n{'='.ToString().PadRight(70, '=')}");
                Console.WriteLine($"Compiling to {generator.BackendName}");
                Console.WriteLine($"{'='.ToString().PadRight(70, '=')}");

                // Phase 1: Lexing
                Console.Write("Phase 1: Lexical Analysis... ");
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"âœ“ {tokens.Count} tokens");

                // Phase 2: Parsing
                Console.Write("Phase 2: Parsing... ");
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                Console.WriteLine($"âœ“ AST generated");

                // Phase 3: Semantic Analysis
                Console.Write("Phase 3: Semantic Analysis... ");
                var semanticAnalyzer = new SemanticAnalyzer();
                if (!semanticAnalyzer.Analyze(ast))
                {
                    var errors = string.Join("\n  ", semanticAnalyzer.Errors.Select(e => e.ToString()));
                    return CompilationResult.CreateError($"Semantic errors:\n  {errors}");
                }
                Console.WriteLine($"âœ“ Type checking passed");

                // Phase 4: IR Generation
                Console.Write("Phase 4: IR Generation... ");
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, _options.ClassName);
                Console.WriteLine($"âœ“ {irModule.Functions.Count} functions");

                // Phase 5: Optimization
                if (_options.OptimizationLevel > 0)
                {
                    Console.Write("Phase 5: Optimization... ");
                    var optimizer = new OptimizationPipeline(_options.OptimizationIterations);

                    if (_options.OptimizationLevel >= 1)
                        optimizer.AddStandardPasses();
                    if (_options.OptimizationLevel >= 2)
                        optimizer.AddAggressivePasses();

                    var optResult = optimizer.Run(irModule);
                    Console.WriteLine($"âœ“ {optResult.TotalModifications} improvements");
                }
                else
                {
                    Console.WriteLine("Phase 5: Optimization... Skipped");
                }

                // Phase 6: Code Generation
                Console.Write($"Phase 6: Code Generation ({generator.BackendName})... ");
                var generatedCode = generator.Generate(irModule);
                Console.WriteLine($"âœ“ {generatedCode.Length} characters");

                return CompilationResult.CreateSuccess(generatedCode, generator.BackendName);
            }
            catch (Exception ex)
            {
                return CompilationResult.CreateError($"Compilation error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Compile to all registered backends
        /// </summary>
        public Dictionary<string, CompilationResult> CompileToAll(string source)
        {
            var results = new Dictionary<string, CompilationResult>();

            foreach (var backendName in _backendFactories.Keys)
            {
                var result = Compile(source, backendName);
                results[backendName] = result;
            }

            return results;
        }

        /// <summary>
        /// Get list of available backends
        /// </summary>
        public IEnumerable<string> GetAvailableBackends()
        {
            return _backendFactories.Keys;
        }
    }

    /// <summary>
    /// Result of a compilation
    /// </summary>
    public class CompilationResult
    {
        public bool IsSuccess { get; }
        public string GeneratedCode { get; }
        public string Backend { get; }
        public string ErrorMessage { get; }

        private CompilationResult(bool isSuccess, string code, string backend, string error)
        {
            IsSuccess = isSuccess;
            GeneratedCode = code;
            Backend = backend;
            ErrorMessage = error;
        }

        public static CompilationResult CreateSuccess(string code, string backend)
        {
            return new CompilationResult(true, code, backend, null);
        }

        public static CompilationResult CreateError(string message)
        {
            return new CompilationResult(false, null, null, message);
        }
    }

    /// <summary>
    /// Compiler configuration options
    /// </summary>
    public class CompilerOptions
    {
        public string Namespace { get; set; } = "GeneratedCode";
        public string ClassName { get; set; } = "Program";
        public bool GenerateMainMethod { get; set; } = true;
        public bool GenerateComments { get; set; } = true;
        public int OptimizationLevel { get; set; } = 1; // 0=none, 1=standard, 2=aggressive
        public int OptimizationIterations { get; set; } = 10;
        public string OutputDirectory { get; set; } = "./output";
    }

    /// <summary>
    /// Example usage of multi-target compiler
    /// </summary>
    class MultiTargetCompilerDemo
    {
        static void RunDemo(string[] args)
        {
            Console.WriteLine("BasicLang Multi-Target Compiler Demo\n");

            // Example BasicLang program
            string sourceCode = @"
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
End Sub
";

            // Create compiler with options
            var options = new CompilerOptions
            {
                Namespace = "BasicLangDemo",
                ClassName = "MathUtils",
                OptimizationLevel = 1
            };

            var compiler = new MultiTargetCompiler(options);

            Console.WriteLine("Available backends:");
            foreach (var backend in compiler.GetAvailableBackends())
            {
                Console.WriteLine($"  â€¢ {backend}");
            }

            // Compile to all backends
            var results = compiler.CompileToAll(sourceCode);

            // Save results
            Console.WriteLine($"\n{'='.ToString().PadRight(70, '=')}");
            Console.WriteLine("Compilation Complete");
            Console.WriteLine($"{'='.ToString().PadRight(70, '=')}");

            foreach (var (backendName, result) in results)
            {
                if (result.IsSuccess)
                {
                    SaveOutput(backendName, result.GeneratedCode);
                    Console.WriteLine($"âœ“ {backendName.ToUpper()}: Success");
                }
                else
                {
                    Console.WriteLine($"âœ— {backendName.ToUpper()}: {result.ErrorMessage}");
                }
            }
        }

        static void SaveOutput(string backend, string code)
        {
            try
            {
                var filename = backend switch
                {
                    "csharp" => "output.cs",
                    "cpp" => "output.cpp",
                    _ => $"output_{backend}"
                };

                Directory.CreateDirectory("output");
                File.WriteAllText(Path.Combine("output", filename), code);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save {backend}: {ex.Message}");
            }
        }
    }
}