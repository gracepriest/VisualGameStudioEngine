' BibleHub home-page mock, rebuilt with GridPanel containers.
' Main runs before the page's form is dispatched (the JavaScript backend calls
' VgsForms.VgsDispatchForm() after Main when no user code calls it).

Sub Main()
    Console.WriteLine("BibleHub grid mock loaded")
End Sub
