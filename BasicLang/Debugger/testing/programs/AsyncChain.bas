Using System
Using System.Threading.Tasks

Async Function Inner() As Task(Of Integer)
    Console.WriteLine("inner before")
    Await Task.Delay(300)
    Console.WriteLine("inner after")
    Return 42
End Function

Async Function Outer() As Task(Of Integer)
    Console.WriteLine("outer before")
    Dim v As Integer = Await Inner()
    Console.WriteLine("outer after")
    Return v + 1
End Function

Sub Main()
    Console.WriteLine("start")
    Dim t As Task(Of Integer) = Outer()
    t.Wait()
    Console.WriteLine("done")
End Sub
