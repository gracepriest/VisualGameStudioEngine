Public Class frmCreateProject
    Private Sub frmCreateProject_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        'load templates into listbox
        Dim templateManager As New TemplateManager()
        Dim templates = templateManager.LoadTemplates()
        For Each template In templates
            ListBox1.Items.Add(template.Name)
        Next
        'lblDes.Text = templates(0).Name
    End Sub

    Private Sub frmCreateProject_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        Me.BringToFront()
        Me.Focus()

    End Sub

    Private Sub ListBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBox1.SelectedIndexChanged

    End Sub
End Class