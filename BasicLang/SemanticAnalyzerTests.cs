//using System;
//using System.Linq;
//using BasicLang.Compiler;
//using BasicLang.Compiler.AST;
//using BasicLang.Compiler.SemanticAnalysis;

//namespace BasicLang.Test
//{
//    class SemanticAnalyzerTests
//    {
//        static void Main(string[] args)
//        {
//            Console.WriteLine("BasicLang Semantic Analyzer Test Suite\n");
//            Console.WriteLine("=".PadRight(80, '='));
            
//            // Test 1: Simple variable declaration and usage
//            RunTest("Simple Variable Declaration", @"
//Sub Main()
//    Dim x As Integer
//    x = 42
//End Sub
//", expectSuccess: true);
            
//            // Test 2: Type mismatch error
//            RunTest("Type Mismatch Error", @"
//Sub Main()
//    Dim x As Integer
//    x = ""Hello""
//End Sub
//", expectSuccess: false);
            
//            // Test 3: Undefined variable
//            RunTest("Undefined Variable", @"
//Sub Main()
//    x = 42
//End Sub
//", expectSuccess: false);
            
//            // Test 4: Function with correct return type
//            RunTest("Function Return Type - Correct", @"
//Function Add(a As Integer, b As Integer) As Integer
//    Return a + b
//End Function
//", expectSuccess: true);
            
//            // Test 5: Function with wrong return type
//            RunTest("Function Return Type - Wrong", @"
//Function GetName() As String
//    Return 42
//End Function
//", expectSuccess: false);
            
//            // Test 6: Type inference with Auto
//            RunTest("Auto Type Inference", @"
//Sub Main()
//    Auto x = 42
//    Auto name = ""John""
//    Auto flag = True
//End Sub
//", expectSuccess: true);
            
//            // Test 7: Arithmetic with correct types
//            RunTest("Arithmetic Operations", @"
//Sub Main()
//    Dim x As Integer = 10
//    Dim y As Integer = 20
//    Dim result As Integer
//    result = x + y
//    result = x - y
//    result = x * y
//    result = x / y
//End Sub
//", expectSuccess: true);
            
//            // Test 8: Arithmetic with wrong types
//            RunTest("Arithmetic Type Error", @"
//Sub Main()
//    Dim x As Integer = 10
//    Dim y As String = ""20""
//    Dim result As Integer
//    result = x + y
//End Sub
//", expectSuccess: false);
            
//            // Test 9: Class with inheritance
//            RunTest("Class Inheritance", @"
//Class Base
//    Public x As Integer
//End Class

//Class Derived Inherits Base
//    Public y As Integer
//End Class
//", expectSuccess: true);
            
//            // Test 10: Undefined base class
//            RunTest("Undefined Base Class", @"
//Class Derived Inherits NonExistent
//    Public y As Integer
//End Class
//", expectSuccess: false);
            
//            // Test 11: Method overloading (same name, different params)
//            RunTest("Duplicate Method Name", @"
//Class Calculator
//    Public Function Add(a As Integer, b As Integer) As Integer
//        Return a + b
//    End Function
//End Class
//", expectSuccess: true);
            
//            // Test 12: Variable shadowing
//            RunTest("Variable Shadowing", @"
//Sub Main()
//    Dim x As Integer = 10
//    If True Then
//        Dim x As Integer = 20
//    End If
//End Sub
//", expectSuccess: true);
            
//            // Test 13: Constant assignment error
//            RunTest("Constant Assignment Error", @"
//Sub Main()
//    Const PI As Double = 3.14159
//    PI = 3.14
//End Sub
//", expectSuccess: false);
            
//            // Test 14: Function call with correct arguments
//            RunTest("Function Call - Correct", @"
//Function Add(a As Integer, b As Integer) As Integer
//    Return a + b
//End Function

//Sub Main()
//    Dim result As Integer
//    result = Add(10, 20)
//End Sub
//", expectSuccess: true);
            
//            // Test 15: Function call with wrong argument types
//            RunTest("Function Call - Wrong Types", @"
//Function Add(a As Integer, b As Integer) As Integer
//    Return a + b
//End Function

//Sub Main()
//    Dim result As Integer
//    result = Add(""10"", 20)
//End Sub
//", expectSuccess: false);
            
//            // Test 16: Function call with wrong argument count
//            RunTest("Function Call - Wrong Count", @"
//Function Add(a As Integer, b As Integer) As Integer
//    Return a + b
//End Function

//Sub Main()
//    Dim result As Integer
//    result = Add(10)
//End Sub
//", expectSuccess: false);
            
//            // Test 17: Array declaration and access
//            RunTest("Array Access", @"
//Sub Main()
//    Dim arr[10] As Integer
//    Dim value As Integer
//    value = arr[5]
//    arr[3] = 42
//End Sub
//", expectSuccess: true);
            
//            // Test 18: Array with wrong index type
//            RunTest("Array Index Type Error", @"
//Sub Main()
//    Dim arr[10] As Integer
//    Dim value As Integer
//    value = arr[""5""]
//End Sub
//", expectSuccess: false);
            
//            // Test 19: Member access
//            RunTest("Member Access", @"
//Class Person
//    Public Name As String
//    Public Age As Integer
//End Class

//Sub Main()
//    Dim p As New Person()
//    p.Name = ""John""
//    p.Age = 30
//End Sub
//", expectSuccess: true);
            
//            // Test 20: Undefined member access
//            RunTest("Undefined Member", @"
//Class Person
//    Public Name As String
//End Class

//Sub Main()
//    Dim p As New Person()
//    p.InvalidMember = 42
//End Sub
//", expectSuccess: false);
            
//            // Test 21: Return outside function
//            RunTest("Return Outside Function", @"
//Sub Main()
//    Return 42
//End Sub
//", expectSuccess: false);
            
//            // Test 22: If condition type
//            RunTest("If Condition - Boolean", @"
//Sub Main()
//    If True Then
//        Dim x As Integer = 10
//    End If
//End Sub
//", expectSuccess: true);
            
//            // Test 23: Logical operators
//            RunTest("Logical Operators", @"
//Sub Main()
//    Dim a As Boolean = True
//    Dim b As Boolean = False
//    Dim result As Boolean
//    result = a And b
//    result = a Or b
//    result = Not a
//End Sub
//", expectSuccess: true);
            
//            // Test 24: Logical operators with wrong types
//            RunTest("Logical Operators Type Error", @"
//Sub Main()
//    Dim x As Integer = 10
//    Dim y As Integer = 20
//    Dim result As Boolean
//    result = x And y
//End Sub
//", expectSuccess: false);
            
//            // Test 25: Comparison operators
//            RunTest("Comparison Operators", @"
//Sub Main()
//    Dim x As Integer = 10
//    Dim y As Integer = 20
//    Dim result As Boolean
//    result = x < y
//    result = x <= y
//    result = x > y
//    result = x >= y
//    result = x == y
//    result = x != y
//End Sub
//", expectSuccess: true);
            
//            // Test 26: For loop
//            RunTest("For Loop", @"
//Sub Main()
//    Dim sum As Integer = 0
//    For i = 1 To 10
//        sum = sum + i
//    Next i
//End Sub
//", expectSuccess: true);
            
//            // Test 27: While loop
//            RunTest("While Loop", @"
//Sub Main()
//    Dim x As Integer = 0
//    While x < 10
//        x = x + 1
//    Wend
//End Sub
//", expectSuccess: true);
            
//            // Test 28: Type alias
//            RunTest("Type Alias", @"
//TypeDefine Age As Integer

//Sub Main()
//    Dim myAge As Age
//    myAge = 25
//End Sub
//", expectSuccess: true);
            
//            // Test 29: User-defined type
//            RunTest("User-Defined Type", @"
//Type Person
//    Name As String
//    Age As Integer
//End Type

//Sub Main()
//    Dim p As Person
//End Sub
//", expectSuccess: true);
            
//            // Test 30: Complex program
//            RunTest("Complex Program", @"
//Class Calculator
//    Private result As Double
    
//    Public Function Add(a As Double, b As Double) As Double
//        result = a + b
//        Return result
//    End Function
    
//    Public Function Multiply(a As Double, b As Double) As Double
//        result = a * b
//        Return result
//    End Function
//End Class

//Sub Main()
//    Dim calc As New Calculator()
//    Dim x As Double = 10.5
//    Dim y As Double = 20.3
//    Dim sum As Double
//    Dim product As Double
    
//    sum = calc.Add(x, y)
//    product = calc.Multiply(x, y)
    
//    If sum > product Then
//        Print(""Sum is greater"")
//    Else
//        Print(""Product is greater"")
//    End If
//End Sub
//", expectSuccess: true);
            
//            Console.WriteLine("\n" + "=".PadRight(80, '='));
//            Console.WriteLine("Test Summary");
//            Console.WriteLine("=".PadRight(80, '='));
//            Console.WriteLine($"Total tests: {_totalTests}");
//            Console.WriteLine($"Passed: {_passedTests}");
//            Console.WriteLine($"Failed: {_failedTests}");
//            Console.WriteLine($"Success rate: {(_passedTests * 100.0 / _totalTests):F1}%");
//        }
        
//        private static int _totalTests = 0;
//        private static int _passedTests = 0;
//        private static int _failedTests = 0;
        
//        static void RunTest(string testName, string source, bool expectSuccess)
//        {
//            _totalTests++;
            
//            Console.WriteLine($"\n--- {testName} ---");
//            Console.WriteLine("Source:");
//            Console.WriteLine(source.Trim());
//            Console.WriteLine();
            
//            try
//            {
//                // Lex
//                var lexer = new Lexer(source);
//                var tokens = lexer.Tokenize();
                
//                // Parse
//                var parser = new Parser(tokens);
//                var ast = parser.Parse();
                
//                // Semantic analysis
//                var analyzer = new SemanticAnalyzer();
//                bool success = analyzer.Analyze(ast);
                
//                Console.WriteLine($"Semantic Analysis: {(success ? "PASSED" : "FAILED")}");
                
//                if (analyzer.Errors.Count > 0)
//                {
//                    Console.WriteLine("\nErrors/Warnings:");
//                    foreach (var error in analyzer.Errors)
//                    {
//                        Console.WriteLine($"  {error}");
//                    }
//                }
                
//                if (success && !expectSuccess)
//                {
//                    Console.WriteLine($"âœ— TEST FAILED: Expected errors but got none");
//                    _failedTests++;
//                }
//                else if (!success && expectSuccess)
//                {
//                    Console.WriteLine($"âœ— TEST FAILED: Expected success but got errors");
//                    _failedTests++;
//                }
//                else
//                {
//                    Console.WriteLine($"âœ“ TEST PASSED");
//                    _passedTests++;
//                }
                
//                // Print symbol table info
//                if (success)
//                {
//                    Console.WriteLine($"\nSymbol Table:");
//                    PrintScope(analyzer.GlobalScope, 2);
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"âœ— Exception: {ex.Message}");
//                Console.WriteLine(ex.StackTrace);
//                _failedTests++;
//            }
//        }
        
//        static void PrintScope(Scope scope, int indent)
//        {
//            string indentStr = new string(' ', indent);
            
//            if (scope.Symbols.Count > 0)
//            {
//                Console.WriteLine($"{indentStr}{scope.Name}:");
//                foreach (var kvp in scope.Symbols)
//                {
//                    Console.WriteLine($"{indentStr}  {kvp.Value.Kind} {kvp.Key} : {kvp.Value.Type}");
//                }
//            }
            
//            foreach (var child in scope.Children)
//            {
//                PrintScope(child, indent + 2);
//            }
//        }
//    }
//}
