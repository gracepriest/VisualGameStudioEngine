' MathUtils.bas - Math utility functions

Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Public Function Multiply(a As Integer, b As Integer) As Integer
    Return a * b
End Function

Private Function InternalHelper(x As Integer) As Integer
    Return x * 2
End Function

Public Function DoubleValue(x As Integer) As Integer
    Return InternalHelper(x)
End Function
