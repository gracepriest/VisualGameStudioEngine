using System;
using System.Collections.Generic;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;

namespace BasicLang.Test
{
    public class ParserTests
    {
        public static void Run()
        {
            Console.WriteLine("BasicLang Parser Test Suite\n");
            Console.WriteLine("=".PadRight(80, '='));
            
            RunTest("Simple Variable Declaration", @"
Dim x As Integer
");
            
            RunTest("Variable with Initializer", @"
Dim count As Integer = 42
Dim name As String = ""John Doe""
");
            
            RunTest("Auto Declaration", @"
Auto x = 42
Auto message = ""Hello""
");
            
            RunTest("Constant Declaration", @"
Const PI As Double = 3.14159
Const GREETING As String = ""Hello, World!""
");
            
            RunTest("Array Declaration", @"
Dim arr[10] As Integer
Dim matrix[5][5] As Double
");
            
            RunTest("Simple Function", @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
");
            
            RunTest("Simple Subroutine", @"
Sub PrintMessage(msg As String)
    Print(msg)
End Sub
");
            
            RunTest("If Statement", @"
Sub Test()
    If x > 10 Then
        Print(""Large"")
    Else
        Print(""Small"")
    End If
End Sub
");
            
            RunTest("If-ElseIf-Else Statement", @"
Sub Test()
    If x > 100 Then
        Print(""Very Large"")
    ElseIf x > 50 Then
        Print(""Large"")
    ElseIf x > 10 Then
        Print(""Medium"")
    Else
        Print(""Small"")
    End If
End Sub
");
            
            RunTest("Select Case Statement", @"
Sub Test()
    Select Case value
        Case 1
            Print(""One"")
        Case 2, 3
            Print(""Two or Three"")
        Case Else
            Print(""Other"")
    End Select
End Sub
");
            
            RunTest("For Loop", @"
Sub Test()
    For i = 1 To 10
        Print(i)
    Next i
End Sub
");
            
            RunTest("For Loop with Step", @"
Sub Test()
    For i = 0 To 100 Step 10
        Print(i)
    Next i
End Sub
");
            
            RunTest("For Each Loop", @"
Sub Test()
    For Each item As String In collection
        Print(item)
    Next item
End Sub
");
            
            RunTest("While Loop", @"
Sub Test()
    While x < 100
        x = x + 1
    Wend
End Sub
");
            
            RunTest("Do-While Loop", @"
Sub Test()
    Do
        x = x + 1
    Loop While x < 100
End Sub
");
            
            RunTest("Try-Catch", @"
Sub Test()
    Try
        x = DivideByZero()
    Catch ex As Exception
        Print(ex.Message)
    End Try
End Sub
");
            
            RunTest("User-Defined Type", @"
Type Person
    Name As String
    Age As Integer
End Type
");
            
            RunTest("Structure", @"
Structure Point
    X As Double
    Y As Double
End Structure
");
            
            RunTest("Simple Class", @"
Class Calculator
    Private result As Double
    
    Public Function Add(x As Double, y As Double) As Double
        result = x + y
        Return result
    End Function
End Class
");
            
            RunTest("Class with Inheritance", @"
Class Derived Inherits Base
    Public Sub NewMethod()
        Print(""New method"")
    End Sub
End Class
");
            
            RunTest("Interface", @"
Interface IComparable
    Function CompareTo(other As Object) As Integer
End Interface
");
            
            RunTest("Class Implementing Interface", @"
Class MyClass Implements IComparable
    Public Function CompareTo(other As Object) As Integer Implements IComparable.CompareTo
        Return 0
    End Function
End Class
");
            
            RunTest("Generic Class", @"
Template Class Stack(Of T)
    Private items[100] As T
    Private count As Integer
    
    Public Sub Push(item As T)
        items[count] = item
        count = count + 1
    End Sub
End Class
");
            
            RunTest("Generic Function", @"
Template Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    Else
        Return b
    End If
End Function
");
            
            RunTest("Delegate", @"
Delegate Function Comparer(a As Integer, b As Integer) As Boolean
");
            
            RunTest("Extension Method", @"
Module Extensions
    <Extension>
    Function String.Reverse() As String
        Dim result As String = """"
        For i = Len(Me) To 1 Step -1
            result = result & Mid(Me, i, 1)
        Next i
        Return result
    End Function
End Module
");
            
            RunTest("Namespace", @"
Namespace MyNamespace
    Class MyClass
        Public Sub DoSomething()
        End Sub
    End Class
End Namespace
");
            
            RunTest("Module", @"
Module Utilities
    Function Helper() As String
        Return ""Help""
    End Function
End Module
");
            
            RunTest("TypeDefine", @"
TypeDefine Age As Integer
TypeDefine Name As String
");
            
            RunTest("Pointer Type", @"
Dim ptr As Pointer To Integer
");
            
            RunTest("Binary Expressions", @"
Sub Test()
    result = (a + b) * (c - d) / e
    flag = x > 10 And y < 20
    value = a || b && c
End Sub
");
            
            RunTest("Unary Expressions", @"
Sub Test()
    x = -y
    flag = Not condition
    value = !other
End Sub
");
            
            RunTest("Member Access", @"
Sub Test()
    value = obj.Property
    result = obj.Method(arg1, arg2)
End Sub
");
            
            RunTest("Array Access", @"
Sub Test()
    value = arr[0]
    element = matrix[i][j]
End Sub
");
            
            RunTest("New Expression", @"
Sub Test()
    obj = New MyClass()
    person = New Person(""John"", 30)
End Sub
");
            
            RunTest("Increment/Decrement", @"
Sub Test()
    x++
    y--
    ++z
    --w
End Sub
");
            
            RunTest("Assignment Operators", @"
Sub Test()
    x = 10
    y =+ 5
    z =- 3
End Sub
");
            
            RunTest("Complex Program", @"
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
    End Class

    Module Utilities
        Function Average(arr[10] As Double) As Double
            Dim sum As Double = 0
            Dim count As Integer = 0

            For i = 0 To 9
                sum = sum + arr[i]
                count++
            Next i

            Return sum / count
        End Function
    End Module
End Namespace
");

            RunTest("Async Function", @"
Async Function FetchDataAsync(url As String) As String
    Dim result As String
    result = Await GetDataAsync(url)
    Return result
End Function
");

            RunTest("Async Subroutine", @"
Async Sub ProcessAsync()
    Dim data As String
    data = Await FetchAsync()
    PrintLine(data)
End Sub
");

            RunTest("Multiple Await Expressions", @"
Async Function CombineDataAsync() As String
    Dim data1 As String
    Dim data2 As String
    data1 = Await FetchFirstAsync()
    data2 = Await FetchSecondAsync()
    Return data1 & data2
End Function
");

            RunTest("Iterator Function", @"
Iterator Function CountTo(max As Integer) As Integer
    Dim i As Integer
    For i = 1 To max
        Yield i
    Next i
End Function
");

            RunTest("Iterator with Yield Return", @"
Iterator Function GetNumbers() As Integer
    Yield 1
    Yield 2
    Yield 3
End Function
");

            RunTest("Iterator with Yield Exit", @"
Iterator Function FindFirst(arr[10] As Integer, target As Integer) As Integer
    Dim i As Integer
    For i = 0 To 9
        If arr[i] = target Then
            Yield arr[i]
            Yield Exit
        End If
    Next i
End Function
");

            RunTest("Async and Iterator in Class", @"
Class DataService
    Public Async Function LoadAsync(id As Integer) As String
        Dim result As String
        result = Await QueryDatabaseAsync(id)
        Return result
    End Function

    Public Iterator Function GetItems() As String
        Yield ""Item1""
        Yield ""Item2""
        Yield ""Item3""
    End Function
End Class
");

            Console.WriteLine("\n" + "=".PadRight(80, '='));
            Console.WriteLine("All parser tests completed!");
        }
        
        static void RunTest(string testName, string source)
        {
            Console.WriteLine($"\n--- {testName} ---");
            Console.WriteLine("Source:");
            Console.WriteLine(source.Trim());
            Console.WriteLine("\nParsing...");
            
            try
            {
                // Lex
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"Lexer produced {tokens.Count} tokens");
                
                // Parse
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                
                Console.WriteLine($"âœ“ Parse successful!");
                Console.WriteLine($"  Program has {ast.Declarations.Count} top-level declaration(s)");
                
                // Print AST structure
                PrintASTStructure(ast, 0);
            }
            catch (ParseException ex)
            {
                Console.WriteLine($"âœ— Parse error: {ex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"âœ— Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        static void PrintASTStructure(ASTNode node, int indent)
        {
            string indentStr = new string(' ', indent * 2);
            
            switch (node)
            {
                case ProgramNode program:
                    Console.WriteLine($"{indentStr}Program:");
                    foreach (var decl in program.Declarations)
                    {
                        PrintASTStructure(decl, indent + 1);
                    }
                    break;
                    
                case NamespaceNode ns:
                    Console.WriteLine($"{indentStr}Namespace: {ns.Name}");
                    foreach (var member in ns.Members)
                    {
                        PrintASTStructure(member, indent + 1);
                    }
                    break;
                    
                case ModuleNode module:
                    Console.WriteLine($"{indentStr}Module: {module.Name}");
                    foreach (var member in module.Members)
                    {
                        PrintASTStructure(member, indent + 1);
                    }
                    break;
                    
                case ClassNode cls:
                    Console.WriteLine($"{indentStr}Class: {cls.Name}");
                    if (cls.BaseClass != null)
                        Console.WriteLine($"{indentStr}  Inherits: {cls.BaseClass}");
                    if (cls.Interfaces.Count > 0)
                        Console.WriteLine($"{indentStr}  Implements: {string.Join(", ", cls.Interfaces)}");
                    foreach (var member in cls.Members)
                    {
                        PrintASTStructure(member, indent + 1);
                    }
                    break;
                    
                case FunctionNode func:
                    var funcModifiers = new List<string>();
                    if (func.IsAsync) funcModifiers.Add("Async");
                    if (func.IsIterator) funcModifiers.Add("Iterator");
                    var funcModStr = funcModifiers.Count > 0 ? $"[{string.Join(", ", funcModifiers)}] " : "";
                    Console.WriteLine($"{indentStr}{funcModStr}Function: {func.Name}({func.Parameters.Count} params) -> {func.ReturnType}");
                    break;

                case SubroutineNode sub:
                    var subModStr = sub.IsAsync ? "[Async] " : "";
                    Console.WriteLine($"{indentStr}{subModStr}Sub: {sub.Name}({sub.Parameters.Count} params)");
                    break;
                    
                case VariableDeclarationNode var:
                    Console.WriteLine($"{indentStr}Variable: {var.Name} : {var.Type}");
                    break;
                    
                case ConstantDeclarationNode constant:
                    Console.WriteLine($"{indentStr}Constant: {constant.Name} : {constant.Type}");
                    break;
                    
                case TypeNode type:
                    Console.WriteLine($"{indentStr}Type: {type.Name} ({type.Members.Count} members)");
                    break;
                    
                case StructureNode structure:
                    Console.WriteLine($"{indentStr}Structure: {structure.Name} ({structure.Members.Count} members)");
                    break;
                    
                case InterfaceNode iface:
                    Console.WriteLine($"{indentStr}Interface: {iface.Name}");
                    break;
                    
                case TemplateDeclarationNode template:
                    Console.WriteLine($"{indentStr}Template: {string.Join(", ", template.TypeParameters)}");
                    if (template.Declaration != null)
                    {
                        PrintASTStructure(template.Declaration, indent + 1);
                    }
                    break;
                    
                case DelegateDeclarationNode del:
                    Console.WriteLine($"{indentStr}Delegate: {del.Name}");
                    break;
                    
                case TypeDefineNode typedef:
                    Console.WriteLine($"{indentStr}TypeDefine: {typedef.AliasName} = {typedef.BaseType}");
                    break;
                    
                case UsingDirectiveNode usingDir:
                    Console.WriteLine($"{indentStr}Using: {usingDir.Namespace}");
                    break;
                    
                case ImportDirectiveNode importDir:
                    Console.WriteLine($"{indentStr}Import: {importDir.Module}");
                    break;
                    
                case ExtensionMethodNode ext:
                    Console.WriteLine($"{indentStr}Extension: {ext.ExtendedType}.{ext.Method?.Name}");
                    break;
                    
                default:
                    Console.WriteLine($"{indentStr}{node.GetType().Name}");
                    break;
            }
        }
    }
}
