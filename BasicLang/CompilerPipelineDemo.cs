using System;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;

namespace BasicLang.Examples
{
    class CompilerPipeline
    {
        static void RunDemo(string[] args)
        {
            Console.WriteLine("BasicLang Compiler Pipeline Demo");
            Console.WriteLine("=".PadRight(80, '='));
            
            // Example: Complete BasicLang program
            string source = @"
Namespace Calculator
    Class BasicCalculator
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
    
    Module MathUtilities
        Function Square(x As Double) As Double
            Return x * x
        End Function
        
        Function Cube(x As Double) As Double
            Return x * x * x
        End Function
        
        Function Average(arr[10] As Double) As Double
            Dim sum As Double = 0
            Dim count As Integer = 0
            
            For i = 0 To 9
                sum = sum + arr[i]
                count++
            Next i
            
            If count > 0 Then
                Return sum / count
            Else
                Return 0
            End If
        End Function
    End Module
End Namespace

Sub Main()
    Dim calc As New BasicCalculator()
    Dim result As Double
    
    result = calc.Add(10, 20)
    Print(result)
    
    result = calc.Multiply(5, 6)
    Print(result)
    
    Dim numbers[10] As Double
    For i = 0 To 9
        numbers[i] = i + 1
    Next i
    
    Dim avg As Double = Average(numbers)
    Print(avg)
End Sub
";
            
            Console.WriteLine("\n--- SOURCE CODE ---");
            Console.WriteLine(source);
            
            try
            {
                // Stage 1: Lexical Analysis
                Console.WriteLine("\n" + "=".PadRight(80, '='));
                Console.WriteLine("STAGE 1: LEXICAL ANALYSIS (Lexer)");
                Console.WriteLine("=".PadRight(80, '='));
                
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                
                Console.WriteLine($"\nâœ“ Lexer completed successfully");
                Console.WriteLine($"  Total tokens: {tokens.Count}");
                
                var tokenStats = new System.Collections.Generic.Dictionary<TokenType, int>();
                foreach (var token in tokens)
                {
                    if (token.Type == TokenType.Newline || token.Type == TokenType.EOF)
                        continue;
                        
                    if (!tokenStats.ContainsKey(token.Type))
                        tokenStats[token.Type] = 0;
                    tokenStats[token.Type]++;
                }
                
                Console.WriteLine("\n  Token Statistics:");
                foreach (var kvp in tokenStats)
                {
                    Console.WriteLine($"    {kvp.Key,-30}: {kvp.Value,4}");
                }
                
                // Stage 2: Parsing
                Console.WriteLine("\n" + "=".PadRight(80, '='));
                Console.WriteLine("STAGE 2: SYNTAX ANALYSIS (Parser)");
                Console.WriteLine("=".PadRight(80, '='));
                
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                
                Console.WriteLine($"\nâœ“ Parser completed successfully");
                Console.WriteLine($"  Top-level declarations: {ast.Declarations.Count}");
                
                // Analyze AST
                Console.WriteLine("\n  AST Structure:");
                PrintASTSummary(ast);
                
                // Pretty print AST
                Console.WriteLine("\n" + "=".PadRight(80, '='));
                Console.WriteLine("STAGE 3: AST VISUALIZATION");
                Console.WriteLine("=".PadRight(80, '=') + "\n");
                
                var printer = new ASTPrettyPrinter();
                ast.Accept(printer);
                Console.WriteLine(printer.GetOutput());
                
                // Summary
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("COMPILATION SUMMARY");
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("âœ“ Lexical Analysis:  PASSED");
                Console.WriteLine("âœ“ Syntax Analysis:   PASSED");
                Console.WriteLine("âœ“ AST Generation:    PASSED");
                Console.WriteLine("\nNext Steps:");
                Console.WriteLine("  - Semantic Analysis (type checking, scope resolution)");
                Console.WriteLine("  - Intermediate Representation generation");
                Console.WriteLine("  - Code generation (C#, C++, MSIL, LLVM IR)");
            }
            catch (ParseException ex)
            {
                Console.WriteLine($"\nâœ— Parse Error:");
                Console.WriteLine($"  {ex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nâœ— Error:");
                Console.WriteLine($"  {ex.Message}");
                Console.WriteLine($"\n{ex.StackTrace}");
            }
        }
        
        static void PrintASTSummary(ProgramNode program)
        {
            int namespaces = 0;
            int modules = 0;
            int classes = 0;
            int interfaces = 0;
            int functions = 0;
            int subroutines = 0;
            
            foreach (var decl in program.Declarations)
            {
                CountDeclarations(decl, ref namespaces, ref modules, ref classes, 
                                ref interfaces, ref functions, ref subroutines);
            }
            
            Console.WriteLine($"    Namespaces:  {namespaces}");
            Console.WriteLine($"    Modules:     {modules}");
            Console.WriteLine($"    Classes:     {classes}");
            Console.WriteLine($"    Interfaces:  {interfaces}");
            Console.WriteLine($"    Functions:   {functions}");
            Console.WriteLine($"    Subroutines: {subroutines}");
        }
        
        static void CountDeclarations(ASTNode node, ref int namespaces, ref int modules,
                                     ref int classes, ref int interfaces, 
                                     ref int functions, ref int subroutines)
        {
            switch (node)
            {
                case NamespaceNode ns:
                    namespaces++;
                    foreach (var member in ns.Members)
                        CountDeclarations(member, ref namespaces, ref modules, ref classes,
                                        ref interfaces, ref functions, ref subroutines);
                    break;
                    
                case ModuleNode module:
                    modules++;
                    foreach (var member in module.Members)
                        CountDeclarations(member, ref namespaces, ref modules, ref classes,
                                        ref interfaces, ref functions, ref subroutines);
                    break;
                    
                case ClassNode cls:
                    classes++;
                    foreach (var member in cls.Members)
                        CountDeclarations(member, ref namespaces, ref modules, ref classes,
                                        ref interfaces, ref functions, ref subroutines);
                    break;
                    
                case InterfaceNode iface:
                    interfaces++;
                    functions += iface.Methods.Count;
                    break;
                    
                case FunctionNode func:
                    functions++;
                    break;
                    
                case SubroutineNode sub:
                    subroutines++;
                    break;
            }
        }
    }
}
