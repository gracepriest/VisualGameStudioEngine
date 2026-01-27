' MainWindow.bas - Main application window
' $safeprojectname$

Imports System
Imports System.Windows
Imports System.Windows.Controls

Namespace $safeprojectname$
    ''' <summary>
    ''' Main window code-behind.
    ''' </summary>
    Partial Public Class MainWindow
        Inherits Window

        ''' <summary>
        ''' Creates a new instance of MainWindow.
        ''' </summary>
        Public Sub New()
            InitializeComponent()
        End Sub

        ''' <summary>
        ''' Handles button click event.
        ''' </summary>
        Private Sub btnClick_Click(sender As Object, e As RoutedEventArgs)
            MessageBox.Show("Hello from BasicLang WPF!", "$safeprojectname$", MessageBoxButton.OK, MessageBoxImage.Information)
        End Sub
    End Class
End Namespace
