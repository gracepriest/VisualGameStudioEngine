Imports System.Runtime.InteropServices

Module Program


    ' Your custom draw function (VB.NET version)
    Public Sub MyCustomDraw()
        Framework_DrawText("Hello from VB.NET Callback!", 100, 10, 20, 255, 0, 0, 255) ' Red
        Framework_DrawText("Score: 5000", 100, 40, 16, 0, 255, 0, 255) ' Green
        Framework_DrawText("Level: 10", 100, 60, 16, 0, 0, 255, 255) ' Blue
        Framework_DrawText("Lives: 3", 100, 80, 16, 255, 255, 0, 255) ' Yellow
        Framework_DrawRectangle(50, 100, 200, 100, 255, 255, 0, 128)
        Framework_DrawLine(50, 100, 250, 200, 255, 0, 255, 255) ' Magenta line
        Framework_DrawCircle(400, 200, 50, 0, 255, 255, 128)
    End Sub

    Sub Main()
        Console.WriteLine("Testing VisualGameStudioEngine with VB.NET Callback...")

        ' Initialize the framework
        If Not Framework_Initialize(800, 450, "VB.NET Callback Test") Then
            Console.WriteLine("Failed to initialize framework!")
            Console.ReadLine()
            Return
        End If

        Console.WriteLine("Framework initialized successfully!")
        Console.WriteLine("Setting up callback...")

        ' Create delegate and set the callback
        Dim drawDelegate As New DrawCallback(AddressOf MyCustomDraw)
        Framework_SetDrawCallback(drawDelegate)

        Console.WriteLine("Callback set! Window should show VB.NET drawn text")
        Console.WriteLine("Close the window to exit...")

        ' Main game loop - Framework_Update() now calls our VB.NET function!
        While Not Framework_ShouldClose()
            Framework_Update() ' This will call MyCustomDraw() automatically
        End While

        ' Cleanup
        Framework_Shutdown()

        Console.WriteLine("Framework shut down successfully!")
        Console.WriteLine("Press Enter to exit...")
        Console.ReadLine()
    End Sub
End Module