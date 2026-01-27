' $safeprojectname$ - BasicLang WPF Application
' Created with Visual Studio 2022

Imports System
Imports System.Windows

Namespace $safeprojectname$
    ''' <summary>
    ''' Main application class.
    ''' </summary>
    Public Class App
        Inherits Application

        ''' <summary>
        ''' Main entry point.
        ''' </summary>
        <STAThread>
        Public Shared Sub Main()
            Dim app As New App()
            app.StartupUri = New Uri("MainWindow.xaml", UriKind.Relative)
            app.Run()
        End Sub
    End Class
End Namespace
