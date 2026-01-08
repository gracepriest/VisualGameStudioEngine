' Main.bas - Main program file
' Tests: Import, qualified access

' Import for shorthand access
Import MathUtils
Import StringUtils

Sub Main()
    ' Test qualified access
    Dim sum As Integer = MathUtils.Add(5, 3)
    Console.WriteLine("Sum = " & sum)

    ' Test shorthand access via Import
    Dim product As Integer = Multiply(4, 7)
    Console.WriteLine("Product = " & product)

    ' Test string utilities
    Dim greeting As String = Concat("Hello ", "World")
    PrintMessage(greeting)
End Sub
