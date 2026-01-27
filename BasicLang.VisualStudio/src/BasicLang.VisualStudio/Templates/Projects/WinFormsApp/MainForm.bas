' MainForm.bas - Main application window
' $safeprojectname$

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace $safeprojectname$
    ''' <summary>
    ''' Main application form.
    ''' </summary>
    Public Class MainForm
        Inherits Form

        Private lblMessage As Label
        Private btnClick As Button

        ''' <summary>
        ''' Creates a new instance of MainForm.
        ''' </summary>
        Public Sub New()
            InitializeComponent()
        End Sub

        ''' <summary>
        ''' Initializes form components.
        ''' </summary>
        Private Sub InitializeComponent()
            Me.Text = "$safeprojectname$"
            Me.Size = New Size(400, 300)
            Me.StartPosition = FormStartPosition.CenterScreen

            ' Create label
            lblMessage = New Label()
            lblMessage.Text = "Hello, BasicLang!"
            lblMessage.Location = New Point(20, 20)
            lblMessage.Size = New Size(200, 30)
            lblMessage.Font = New Font("Segoe UI", 12)
            Me.Controls.Add(lblMessage)

            ' Create button
            btnClick = New Button()
            btnClick.Text = "Click Me"
            btnClick.Location = New Point(20, 60)
            btnClick.Size = New Size(100, 30)
            AddHandler btnClick.Click, AddressOf btnClick_Click
            Me.Controls.Add(btnClick)
        End Sub

        ''' <summary>
        ''' Handles button click event.
        ''' </summary>
        Private Sub btnClick_Click(sender As Object, e As EventArgs)
            MessageBox.Show("Button clicked!", "$safeprojectname$", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub
    End Class
End Namespace
