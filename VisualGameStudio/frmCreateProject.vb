Public Class frmCreateProject
    Private Sub frmCreateProject_Load(sender As Object, e As EventArgs) Handles MyBase.Load


    End Sub

    Private Sub frmCreateProject_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        Me.BringToFront()
        Me.Focus()

    End Sub
End Class