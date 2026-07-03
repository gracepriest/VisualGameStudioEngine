' MainWindow.bas - Main application window
' $safeprojectname$

Using System
Using System.Windows
Using System.Windows.Controls

''' <summary>
''' Main window, built in code (BasicLang does not use XAML).
''' </summary>
Public Class MainWindow
    Inherits Window

    ''' <summary>
    ''' Creates a new instance of MainWindow.
    ''' </summary>
    Public Sub New()
        Me.Title = "$safeprojectname$"
        Me.Width = 400
        Me.Height = 300
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        Dim lblMessage As Label = New Label()
        lblMessage.Content = "Hello, BasicLang!"
        lblMessage.FontSize = 16

        Dim btnClick As Button = New Button()
        btnClick.Content = "Click Me"
        btnClick.Width = 100
        btnClick.Height = 30
        btnClick.Margin = New Thickness(0, 10, 0, 0)
        AddHandler btnClick.Click, AddressOf btnClick_Click

        Dim panel As StackPanel = New StackPanel()
        panel.Margin = New Thickness(20)
        panel.Children.Add(lblMessage)
        panel.Children.Add(btnClick)
        Me.Content = panel
    End Sub

    ''' <summary>
    ''' Handles button click event.
    ''' </summary>
    Private Sub btnClick_Click(sender As Object, e As RoutedEventArgs)
        MessageBox.Show("Hello from BasicLang WPF!", "$safeprojectname$", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub
End Class
