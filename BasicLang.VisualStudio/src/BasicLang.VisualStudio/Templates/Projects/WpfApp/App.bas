' $safeprojectname$ - BasicLang WPF Application
' Created with Visual Studio 2022

Using System
Using System.Windows

Module Program
    ''' <summary>
    ''' Main entry point for the application.
    ''' </summary>
    Sub Main()
        Dim app As Application = New Application()
        app.Run(New MainWindow())
    End Sub
End Module
