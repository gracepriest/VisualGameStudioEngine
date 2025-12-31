Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

Module Program

    Sub Main(args As String())
        ' Check for test mode
        If args.Length > 0 AndAlso (args(0) = "--test" OrElse args(0) = "-t") Then
            RunTests()
            Return
        End If

        Dim game As New Game
        game.Run()
    End Sub

    Private Sub RunTests()
        Console.WriteLine("Starting VisualGameStudioEngine Unit Tests...")
        Console.WriteLine()

        ' Initialize framework for testing (required for most API calls)
        If Not Framework_Initialize(800, 600, "Unit Test Runner") Then
            Console.WriteLine("ERROR: Failed to initialize framework for testing!")
            Return
        End If

        Framework_InitAudio()

        ' Run all tests
        Dim allPassed = FrameworkTests.RunAllTests()

        ' Clean up
        Framework_CloseAudio()
        Framework_Shutdown()

        Console.WriteLine()
        If allPassed Then
            Console.WriteLine("All tests passed!")
        Else
            Console.WriteLine("Some tests failed - see above for details.")
        End If

        Console.WriteLine()
        Console.WriteLine("Press any key to exit...")
        Console.ReadKey()
    End Sub
End Module
