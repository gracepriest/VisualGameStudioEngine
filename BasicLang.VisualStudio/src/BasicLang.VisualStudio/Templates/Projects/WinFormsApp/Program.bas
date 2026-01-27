' $safeprojectname$ - BasicLang Windows Forms Application
' Created with Visual Studio 2022

Imports System
Imports System.Windows.Forms

Module Program
    ''' <summary>
    ''' Main entry point for the application.
    ''' </summary>
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module
