using System;
using System.IO;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen;

namespace BasicLang.Compiler.Demo
{
    /// <summary>
    /// Demonstrates the complete BasicLang to C# compilation pipeline
    /// </summary>
    public class CSharpBackendDemo
    {
        public static void RunDemo(string[] args)
        {
            Console.WriteLine("=".PadRight(70, '='));
            Console.WriteLine("BasicLang to C# Compiler - Complete Pipeline Demo");
            Console.WriteLine("=".PadRight(70, '='));
            Console.WriteLine();
            
            // Run all demos
            DemoSimpleFunction();
            DemoFibonacci();
            DemoLoops();
            DemoArrays();
            DemoCompleteProgram();
            
            Console.WriteLine();
            Console.WriteLine("Demo complete! Check the generated C# files.");
        }
        
        // ====================================================================
        // Demo 1: Simple Function
        // ====================================================================
        
        static void DemoSimpleFunction()
        {
            Console.WriteLine("Demo 1: Simple Function");
            Console.WriteLine("-".PadRight(70, '-'));
            
            string source = @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub Main()
    Dim result As Integer
    result = Add(5, 3)
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
        // Compilation Pipeline
        // ====================================================================
        
        static string CompileToCSharp(string source, string className)
        {
            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                
                Console.WriteLine($"âœ“ Lexical analysis: {tokens.Count} tokens");
                
                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                
                Console.WriteLine($"âœ“ Parsing: AST with {ast.Declarations.Count} declarations");
                
                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool semanticSuccess = semanticAnalyzer.Analyze(ast);
                
                if (!semanticSuccess)
                {
                    Console.WriteLine("âœ— Semantic analysis failed:");
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    return null;
                }
                
                Console.WriteLine($"âœ“ Semantic analysis: {semanticAnalyzer.Errors.Count} errors");
                
                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, className);
                
                Console.WriteLine($"âœ“ IR generation: {irModule.Functions.Count} functions");
                
                // Phase 5: Optimization
                var optimizer = new OptimizationPipeline();
                optimizer.AddStandardPasses();
                
                var optimizationResult = optimizer.Run(irModule);
                
                Console.WriteLine($"âœ“ Optimization: {optimizationResult.TotalModifications} improvements");
                
                // Phase 6: C# Code Generation
                var codeGenOptions = new CodeGenOptions
                {
                    Namespace = "GeneratedCode",
                    ClassName = className,
                    GenerateMainMethod = true
                };
                
                var csharpGenerator = new CSharpCodeGenerator(codeGenOptions);
                var csharpCode = csharpGenerator.Generate(irModule);
                
                Console.WriteLine($"âœ“ C# code generation: {csharpCode.Length} characters");
                
                return csharpCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"âœ— Compilation failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }
        
        static void SaveToFile(string filename, string content)
        {
            try
            {
                string outputPath = Path.Combine("GeneratedCode", filename);
                Directory.CreateDirectory("GeneratedCode");
                File.WriteAllText(outputPath, content);
                Console.WriteLine($"âœ“ Saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"âœ— Failed to save file: {ex.Message}");
            }
        }
    }
}
