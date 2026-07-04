Using System
Using System.Threading.Tasks

Async Function Inner() As Task(Of Integer)
    Console.WriteLine("inner before")
    Await Task.Delay(300)
    Console.WriteLine("inner after")
    Return 42
End Function

Function AddOne(x As Integer) As Integer
    Dim y As Integer = x + 1
    Return y
End Function

Sub Main()
    Console.WriteLine("start")
    Dim t As Task(Of Integer) = Inner()
    t.Wait()
    Dim z As Integer = AddOne(1)
    Console.WriteLine("done")
End Sub
