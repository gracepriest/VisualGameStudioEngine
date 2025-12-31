using System;
using System.Collections.Generic;
using BasicLang.Compiler;

namespace BasicLang.Test
{
    class Program
    {
        static void RunDemo(string[] args)
        {
            Console.WriteLine("BasicLang Lexer Test\n");
            Console.WriteLine("=".PadRight(80, '='));
            
            // Test 1: Variable declarations
            TestLexer("Variable Declarations", @"
Dim intVar As Integer
Dim strVar As String
Auto x = 42
Const PI As Double = 3.14159
");

            // Test 2: Control structures
            TestLexer("Control Structures", @"
If x > 10 Then
    y = 20
Else
    y = 30
End If

For i = 1 To 10
    sum = sum + i
Next i

While condition
    DoSomething()
Wend
");

            // Test 3: Functions
            TestLexer("Functions and Subroutines", @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub PrintMessage(msg As String)
    Print(msg)
End Sub
");

            // Test 4: User-Defined Types
            TestLexer("User-Defined Types", @"
Type Person
    Name As String
    Age As Integer
End Type

Structure Point
    X As Double
    Y As Double
End Structure
");

            // Test 5: Classes and OOP
            TestLexer("Classes and OOP", @"
Class MyClass
    Private value As Integer
    
    Public Function GetValue() As Integer
        Return Me.value
    End Function
End Class

Class Derived Inherits Base
    Public Sub NewMethod()
    End Sub
End Class
");

            // Test 6: Operators
            TestLexer("Operators", @"
x = 10
x++
x--
x =+ 5
x =- 3
result = (a + b) * c
flag = x > 10 And y < 20
flag2 = a == b || c != d
");

            // Test 7: Arrays and Pointers
            TestLexer("Arrays and Pointers", @"
Dim arr[10] As Integer
Dim matrix[5][5] As Double
Dim ptr As Pointer To Integer
value = ptr^
");

            // Test 8: Templates
            TestLexer("Templates", @"
Template Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    Else
        Return b
    End If
End Function

Template Class Stack(Of T)
    Private items[100] As T
End Class
");

            // Test 9: Namespaces and Modules
            TestLexer("Namespaces and Modules", @"
Namespace MyNamespace
    Class MyClass
    End Class
End Namespace

Module Utilities
    Function Helper() As String
    End Function
End Module
");

            // Test 10: String literals and comments
            TestLexer("Strings and Comments", @"
' This is a comment
Dim msg As String = ""Hello, World!""
Dim path As String = ""C:\\Users\\Test\\file.txt""
Dim multiline As String = ""Line 1
Line 2
Line 3""
");

            // Test 11: Number literals
            TestLexer("Number Literals", @"
Dim int1 As Integer = 42
Dim long1 As Long = 1000000L
Dim float1 As Single = 3.14f
Dim double1 As Double = 2.71828
Dim exp1 As Double = 1.23e-4
");

            // Test 12: Compilation directives
            TestLexer("Compilation Directives", @"
#If WINDOWS Then
    ' Windows code
#ElseIf LINUX Then
    ' Linux code
#Else
    ' Other platform
#EndIf
");

            // Test 13: Delegates and function pointers
            TestLexer("Delegates", @"
Delegate Function Comparer(a As Integer, b As Integer) As Boolean

Dim compare As Comparer
compare = AddressOf MyCompareFunction
result = compare(10, 20)
");

            // Test 14: Error handling
            TestLexer("Error Handling", @"
Try
    x = Divide(10, 0)
Catch ex As Exception
    Print(""Error: "" & ex.Message)
End Try
");

            // Test 15: Complex expression
            TestLexer("Complex Expression", @"
result = (a + b * c) / (d - e) ^ 2
array[index + 1] = value
obj.Property = obj.Method(param1, param2)
");

            Console.WriteLine("\nAll tests completed!");
        }
        
        static void TestLexer(string testName, string source)
        {
            Console.WriteLine($"\n--- {testName} ---");
            Console.WriteLine($"Source:");
            Console.WriteLine(source.Trim());
            Console.WriteLine("\nTokens:");
            
            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                
                foreach (var token in tokens)
                {
                    if (token.Type == TokenType.Newline || token.Type == TokenType.EOF)
                        continue;
                        
                    if (token.Type == TokenType.Comment)
                    {
                        Console.WriteLine($"  {token.Type,-25} | {token.Lexeme}");
                    }
                    else if (token.Value != null && token.Value.ToString() != token.Lexeme)
                    {
                        Console.WriteLine($"  {token.Type,-25} | '{token.Lexeme}' (Value: {token.Value})");
                    }
                    else
                    {
                        Console.WriteLine($"  {token.Type,-25} | '{token.Lexeme}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR: {ex.Message}");
            }
            
            Console.WriteLine();
        }
    }
}
