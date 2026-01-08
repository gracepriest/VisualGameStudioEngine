' StringUtils.bas - String utility functions

Public Function Concat(a As String, b As String) As String
    Return a & b
End Function

Public Function GetLength(s As String) As Integer
    Return Len(s)
End Function

Public Sub PrintMessage(msg As String)
    Console.WriteLine(msg)
End Sub

Public Sub kprint(msg as String)
Console.WriteLine(msg)
END Sub 
