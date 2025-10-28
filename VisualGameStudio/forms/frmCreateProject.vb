Public Class frmCreateProject
    Private Sub frmCreateProject_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        lstView.Items.Clear()
        InitializeTemplates()
        'load templates into listbox
        For Each item In arrTemplates
            lstProjectType.Items.Add(item.Name)
        Next
        lstProjectType.SelectedIndex = 0
        lblDes.Text = templateMan(lstProjectType.SelectedIndex).Description
        lblProjectType.Text = arrTemplates(lstProjectType.SelectedIndex).Name
        lstView.LargeImageList = imgList
        lstView.MultiSelect = False
        lstView.View = View.LargeIcon
        For Each item In templateMan
            lstView.Items.Add(New ListViewItem(item.Name, Convert.ToInt32(item.Id)))
        Next

    End Sub

    Private Sub frmCreateProject_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        Me.BringToFront()
        Me.Focus()
    End Sub

    Private Sub ListBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles lstProjectType.SelectedIndexChanged

    End Sub
    Private Sub ListBox1_Click(sender As Object, e As EventArgs) Handles lstProjectType.Click
        lstView.Items.Clear()
        templateMan = LoadSelectedTemplateManifest(arrTemplates(lstProjectType.SelectedIndex))
        lblProjectType.Text = lstProjectType.SelectedItem.ToString()
        lblDes.Text = templateMan(0).Description

        For Each item In templateMan
            lstView.Items.Add(New ListViewItem(item.Name, item.Id))
        Next

    End Sub

    Private Sub ListView1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles lstView.SelectedIndexChanged
        If lstView.SelectedItems.Count > 0 Then
            Dim selectedItem As ListViewItem = lstView.SelectedItems(0)
            templateMan = LoadSelectedTemplateManifest(arrTemplates(lstProjectType.SelectedIndex))
            lblProjectType.Text = templateMan(selectedItem.Index).Name
            lblDes.Text = templateMan(selectedItem.Index).Description
        End If
    End Sub

    Private Sub SaveFileDialog1_FileOk(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles SaveFileDialog1.FileOk

    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Me.Close()
    End Sub
End Class