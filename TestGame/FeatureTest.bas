' ============================================
' Feature Test File for IDE Features
' Tests: Ctrl+Click, Peek Definition, Bracket Matching,
'        Code Lens, Minimap, Breadcrumbs, Snippets, etc.
' ============================================

Module FeatureTestModule

    ' Test variable for hover info
    Dim testCounter As Integer = 0
    Dim testMessage As String = "Hello World"

    ' Main entry point - test Code Lens reference counts
    Sub Main()
        PrintLine("=== IDE Feature Test ===")

        ' Test function calls (for Go to Definition, Find References)
        TestBracketMatching()
        TestControlFlow()
        TestClassFeatures()

        ' Test local variable hover
        Dim localVar As Integer = 15
        PrintLine("Local var: " & localVar)

        PrintLine("=== All tests completed ===")
    End Sub

    ' Test bracket matching with control flow
    Sub TestBracketMatching()
        ' Nested If statements - test bracket matching
        If testCounter >= 0 Then
            If testMessage <> "" Then
                PrintLine("Bracket matching test passed")
            End If
        End If

        ' Nested loops - more bracket matching
        For i = 1 To 3
            For j = 1 To 2
                testCounter = testCounter + 1
            Next
        Next

        PrintLine("Bracket test done, counter: " & testCounter)
    End Sub

    ' Test control flow structures
    Sub TestControlFlow()
        ' Select Case - breadcrumb should show: Module > TestControlFlow
        Dim day As Integer = 3

        Select Case day
            Case 1
                PrintLine("Monday")
            Case 2
                PrintLine("Tuesday")
            Case 3
                PrintLine("Wednesday")
            Case Else
                PrintLine("Other day")
        End Select

        ' While loop
        Dim count As Integer = 0
        While count < 3
            count = count + 1
            PrintLine("Count: " & count)
        Wend

        ' Do loop
        Do
            count = count - 1
        Loop While count > 0
    End Sub

    ' Test class-related features
    Sub TestClassFeatures()
        ' Create instance - test Ctrl+Click on class name
        Dim person As New Person()
        person.Name = "John"
        person.Age = 30

        ' Method call - test Go to Definition
        person.Greet()

        PrintLine("Class features test done")
    End Sub

End Module

' Class for testing class features
' Should appear in Document Outline
Class Person
    ' Fields - test field hover
    Public Name As String
    Public Age As Integer

    ' Method - test method signature help
    Sub Greet()
        PrintLine("Hello, my name is " & Name)
    End Sub
End Class

' Enum for testing
Enum Colors
    Red = 1
    Green = 2
    Blue = 3
End Enum
