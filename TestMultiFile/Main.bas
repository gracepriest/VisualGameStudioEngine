' Main.bas - Main program file
' Tests: Import, qualified access

' Import for shorthand access
Import MathUtils
Import StringUtils

Public dim arr as String = "Yep"
Sub Main()
    Dim sum As Integer
    Dim product As Integer
    Dim greeting As String
    
    ' Test imported function with shorthand
    sum = Add(5, 3)
    Console.WriteLine("Sum = " & Add(5, 3))

    ' Test qualified access
    product = MathUtils.Multiply(4, 7)
    Console.WriteLine("Product = " & product)

    ' Test string utilities
    greeting = StringUtils.Concat("Hello ", "World")
    StringUtils.PrintMessage(greeting)
    Kprint(arr)
    returnSTRING()
End Sub
