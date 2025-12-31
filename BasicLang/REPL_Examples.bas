' BasicLang REPL Examples
' Load this file in the REPL with: :load REPL_Examples.bas

' ============================================
' Example 1: Simple Expressions
' ============================================
' Try these in the REPL:
' >>> 5 + 3
' >>> 2 * (10 + 5)
' >>> "Hello, " & "World!"

' ============================================
' Example 2: Variable Declarations
' ============================================
Dim message As String = "Hello from BasicLang!"
Dim count As Integer = 42
Dim pi As Double = 3.14159

PrintLine(message)
PrintLine("Count: " & count)
PrintLine("Pi: " & pi)

' ============================================
' Example 3: Function Definitions
' ============================================
Function Factorial(n As Integer) As Integer
    If n <= 1 Then
        Return 1
    Else
        Return n * Factorial(n - 1)
    End If
End Function

Function Fibonacci(n As Integer) As Integer
    If n <= 1 Then
        Return n
    Else
        Return Fibonacci(n - 1) + Fibonacci(n - 2)
    End If
End Function

' Test the functions
PrintLine("Factorial(5) = " & Factorial(5))
PrintLine("Fibonacci(10) = " & Fibonacci(10))

' ============================================
' Example 4: Loops and Arrays
' ============================================
Sub DemoArrays()
    Dim numbers[5] As Integer
    Dim i As Integer

    ' Fill array
    For i = 0 To 4
        numbers[i] = (i + 1) * 10
    Next i

    ' Print array
    PrintLine("Array contents:")
    For i = 0 To 4
        PrintLine("  numbers[" & i & "] = " & numbers[i])
    Next i
End Sub

DemoArrays()

' ============================================
' Example 5: String Manipulation
' ============================================
Sub StringDemo()
    Dim text As String
    Dim upper As String
    Dim lower As String

    text = "BasicLang REPL"
    upper = UCase(text)
    lower = LCase(text)

    PrintLine("Original: " & text)
    PrintLine("Upper: " & upper)
    PrintLine("Lower: " & lower)
    PrintLine("Length: " & Len(text))
End Sub

StringDemo()

' ============================================
' Example 6: Math Operations
' ============================================
Sub MathDemo()
    Dim x As Double = 16.0
    Dim y As Double = -5.5

    PrintLine("Math Operations:")
    PrintLine("  Sqrt(16) = " & Sqrt(x))
    PrintLine("  Abs(-5.5) = " & Abs(y))
    PrintLine("  Pow(2, 8) = " & Pow(2.0, 8.0))
    PrintLine("  Max(10, 20) = " & Max(10.0, 20.0))
    PrintLine("  Min(10, 20) = " & Min(10.0, 20.0))
End Sub

MathDemo()

' ============================================
' Example 7: Control Flow
' ============================================
Function GetGrade(score As Integer) As String
    Dim grade As String

    Select Case score
        Case 90 To 100
            grade = "A"
        Case 80 To 89
            grade = "B"
        Case 70 To 79
            grade = "C"
        Case 60 To 69
            grade = "D"
        Case Else
            grade = "F"
    End Select

    Return grade
End Function

PrintLine("Grade for 95: " & GetGrade(95))
PrintLine("Grade for 75: " & GetGrade(75))
PrintLine("Grade for 55: " & GetGrade(55))

' ============================================
' Tips for Interactive Use:
' ============================================
' 1. Multi-line input: Functions and other blocks automatically
'    continue to the next line until the block is complete
'
' 2. Line continuation: End a line with underscore (_) to continue
'
' 3. Use :vars to see all defined variables
'
' 4. Use :funcs to see all defined functions
'
' 5. Use :clear to reset the session state
'
' 6. Use up/down arrows to navigate command history
'
' 7. Use :help to see all available commands
