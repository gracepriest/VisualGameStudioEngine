using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Examples
{
    class CompleteCompilerDemo
    {
        static void RunDemo(string[] args)
        {
            Console.WriteLine("BasicLang Complete Compiler Pipeline Demo");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();
            
            // Example program with various features
            string source = @"
' BasicLang Example Program
Namespace MathLibrary
    Class Calculator
        Private result As Double
        
        Public Function Add(a As Double, b As Double) As Double
            result = a + b
            Return result
        End Function
        
        Public Function Subtract(a As Double, b As Double) As Double
            result = a - b
            Return result
        End Function
        
        Public Function Multiply(a As Double, b As Double) As Double
            result = a * b
            Return result
        End Function
        
        Public Function Divide(a As Double, b As Double) As Double
            If b == 0 Then
                Return 0
            Else
                result = a / b
                Return result
            End If
        End Function
    End Class
    
    Module Utilities
        Function Square(x As Double) As Double
            Return x * x
        End Function
        
        Function IsEven(n As Integer) As Boolean
            Return (n % 2) == 0
        End Function
    End Module
End Namespace

Sub Main()
    Dim calc As New Calculator()
    Dim x As Double = 10.5
    Dim y As Double = 3.5
    
    Dim sum As Double = calc.Add(x, y)
    Dim diff As Double = calc.Subtract(x, y)
    Dim prod As Double = calc.Multiply(x, y)
    Dim quot As Double = calc.Divide(x, y)
    
    ' Use utilities
    Dim squared As Double = Square(5.0)
    
    ' Control flow
    If sum > 10 Then
        Dim message As String = ""Sum is large""
    End If
    
    ' Loop
    For i = 1 To 10
        Dim n As Integer = i
        If IsEven(n) Then
            ' Do something
        End If
    Next i
End Sub
";
            
            try
            {
                // ================================================================
                // STAGE 1: LEXICAL ANALYSIS
                // ================================================================
                Console.WriteLine("STAGE 1: LEXICAL ANALYSIS");
                Console.WriteLine("-".PadRight(80, '-'));
                
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                
                var significantTokens = tokens.Where(t => 
                    t.Type != TokenType.Newline && 
                    t.Type != TokenType.Comment && 
                    t.Type != TokenType.EOF
                ).ToList();
                
                Console.WriteLine($"âœ“ Lexer completed successfully");
                Console.WriteLine($"  Total tokens (including newlines): {tokens.Count}");
                Console.WriteLine($"  Significant tokens: {significantTokens.Count}");
                Console.WriteLine();
                
                // ================================================================
                // STAGE 2: SYNTAX ANALYSIS
                // ================================================================
                Console.WriteLine("STAGE 2: SYNTAX ANALYSIS");
                Console.WriteLine("-".PadRight(80, '-'));
                
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                
                Console.WriteLine($"âœ“ Parser completed successfully");
                Console.WriteLine($"  Top-level declarations: {ast.Declarations.Count}");
                
                // Count nodes
                int namespaces = 0, classes = 0, modules = 0, functions = 0, subroutines = 0;
                CountDeclarations(ast, ref namespaces, ref classes, ref modules, ref functions, ref subroutines);
                
                Console.WriteLine($"  Namespaces: {namespaces}");
                Console.WriteLine($"  Classes: {classes}");
                Console.WriteLine($"  Modules: {modules}");
                Console.WriteLine($"  Functions: {functions}");
                Console.WriteLine($"  Subroutines: {subroutines}");
                Console.WriteLine();
                
                // ================================================================
                // STAGE 3: SEMANTIC ANALYSIS
                // ================================================================
                Console.WriteLine("STAGE 3: SEMANTIC ANALYSIS");
                Console.WriteLine("-".PadRight(80, '-'));
                
                var analyzer = new SemanticAnalyzer();
                bool success = analyzer.Analyze(ast);
                
                if (success)
                {
                    Console.WriteLine($"âœ“ Semantic analysis completed successfully");
                    Console.WriteLine($"  No errors found");
                }
                else
                {
                    Console.WriteLine($"âœ— Semantic analysis found {analyzer.Errors.Count} error(s)");
                }
                
                Console.WriteLine();
                
                // Display errors/warnings
                if (analyzer.Errors.Count > 0)
                {
                    Console.WriteLine("Errors and Warnings:");
                    foreach (var error in analyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    Console.WriteLine();
                }
                
                // ================================================================
                // SYMBOL TABLE
                // ================================================================
                Console.WriteLine("SYMBOL TABLE");
                Console.WriteLine("-".PadRight(80, '-'));
                
                PrintSymbolTable(analyzer.GlobalScope);
                Console.WriteLine();
                
                // ================================================================
                // TYPE INFORMATION
                // ================================================================
                Console.WriteLine("TYPE CHECKING RESULTS");
                Console.WriteLine("-".PadRight(80, '-'));
                
                PrintTypeCheckingInfo(analyzer);
                Console.WriteLine();
                
                // ================================================================
                // COMPILATION SUMMARY
                // ================================================================
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("COMPILATION SUMMARY");
                Console.WriteLine("=".PadRight(80, '='));
                
                Console.WriteLine($"âœ“ Stage 1: Lexical Analysis    - PASSED");
                Console.WriteLine($"âœ“ Stage 2: Syntax Analysis     - PASSED");
                Console.WriteLine($"{(success ? "âœ“" : "âœ—")} Stage 3: Semantic Analysis   - {(success ? "PASSED" : "FAILED")}");
                
                if (success)
                {
                    Console.WriteLine();
                    Console.WriteLine("ðŸŽ‰ Program is semantically correct!");
                    Console.WriteLine();
                    Console.WriteLine("Next Steps:");
                    Console.WriteLine("  - Generate Intermediate Representation (IR)");
                    Console.WriteLine("  - Optimize IR");
                    Console.WriteLine("  - Generate target code (C#, C++, MSIL, or LLVM)");
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("âŒ Please fix semantic errors before proceeding");
                }
            }
            catch (ParseException ex)
            {
                Console.WriteLine();
                Console.WriteLine($"âœ— Parse Error: {ex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"âœ— Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        static void CountDeclarations(ASTNode node, ref int namespaces, ref int classes, 
                                     ref int modules, ref int functions, ref int subroutines)
        {
            switch (node)
            {
                case ProgramNode program:
                    foreach (var decl in program.Declarations)
                        CountDeclarations(decl, ref namespaces, ref classes, ref modules, 
                                        ref functions, ref subroutines);
                    break;
                    
                case NamespaceNode ns:
                    namespaces++;
                    foreach (var member in ns.Members)
                        CountDeclarations(member, ref namespaces, ref classes, ref modules, 
                                        ref functions, ref subroutines);
                    break;
                    
                case ModuleNode module:
                    modules++;
                    foreach (var member in module.Members)
                        CountDeclarations(member, ref namespaces, ref classes, ref modules, 
                                        ref functions, ref subroutines);
                    break;
                    
                case ClassNode cls:
                    classes++;
                    foreach (var member in cls.Members)
                        CountDeclarations(member, ref namespaces, ref classes, ref modules, 
                                        ref functions, ref subroutines);
                    break;
                    
                case FunctionNode func:
                    functions++;
                    break;
                    
                case SubroutineNode sub:
                    subroutines++;
                    break;
            }
        }
        
        static void PrintSymbolTable(Scope scope, int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            
            if (scope.Symbols.Count > 0)
            {
                Console.WriteLine($"{indentStr}{scope.Kind} '{scope.Name}':");
                
                foreach (var kvp in scope.Symbols.OrderBy(s => s.Key))
                {
                    var symbol = kvp.Value;
                    string symbolInfo = $"{indentStr}  {symbol.Kind,-12} {symbol.Name,-20}";
                    
                    if (symbol.Type != null)
                    {
                        symbolInfo += $" : {symbol.Type}";
                    }
                    
                    if (symbol.Kind == SymbolKind.Function && symbol.Parameters.Count > 0)
                    {
                        var paramTypes = string.Join(", ", symbol.Parameters.Select(p => p.Type?.ToString() ?? "?"));
                        symbolInfo += $" ({paramTypes})";
                    }
                    
                    Console.WriteLine(symbolInfo);
                }
            }
            
            foreach (var child in scope.Children)
            {
                PrintSymbolTable(child, indent + 1);
            }
        }
        
        static void PrintTypeCheckingInfo(SemanticAnalyzer analyzer)
        {
            var stats = new
            {
                TotalSymbols = CountSymbols(analyzer.GlobalScope),
                Functions = CountSymbolKind(analyzer.GlobalScope, SymbolKind.Function),
                Subroutines = CountSymbolKind(analyzer.GlobalScope, SymbolKind.Subroutine),
                Variables = CountSymbolKind(analyzer.GlobalScope, SymbolKind.Variable),
                Constants = CountSymbolKind(analyzer.GlobalScope, SymbolKind.Constant),
                Classes = CountSymbolKind(analyzer.GlobalScope, SymbolKind.Class),
                Errors = analyzer.Errors.Count(e => e.Severity == ErrorSeverity.Error),
                Warnings = analyzer.Errors.Count(e => e.Severity == ErrorSeverity.Warning)
            };
            
            Console.WriteLine($"Total Symbols:      {stats.TotalSymbols}");
            Console.WriteLine($"  Functions:        {stats.Functions}");
            Console.WriteLine($"  Subroutines:      {stats.Subroutines}");
            Console.WriteLine($"  Variables:        {stats.Variables}");
            Console.WriteLine($"  Constants:        {stats.Constants}");
            Console.WriteLine($"  Classes:          {stats.Classes}");
            Console.WriteLine();
            Console.WriteLine($"Errors:             {stats.Errors}");
            Console.WriteLine($"Warnings:           {stats.Warnings}");
        }
        
        static int CountSymbols(Scope scope)
        {
            int count = scope.Symbols.Count;
            foreach (var child in scope.Children)
            {
                count += CountSymbols(child);
            }
            return count;
        }
        
        static int CountSymbolKind(Scope scope, SymbolKind kind)
        {
            int count = scope.Symbols.Values.Count(s => s.Kind == kind);
            foreach (var child in scope.Children)
            {
                count += CountSymbolKind(child, kind);
            }
            return count;
        }
    }
}
