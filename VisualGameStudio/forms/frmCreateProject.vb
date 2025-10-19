Public Class frmCreateProject
    Private Sub frmCreateProject_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        'load templates into listbox
        Dim templateManager As New TemplateManager()
        Dim templates = templateManager.LoadTemplates()
        For Each template In templates
            ListBox1.Items.Add(template.Name)
        Next
        'lblDes.Text = templates(0).Name
        ListBox1.SelectedIndex = 0
        lblProjectType.Text = templates(0).Category

        Dim templateMan = templateManager.LoadTemplateManifest(templates(0))
        Dim imgList As New ImageList()

        imgList.ImageSize = New Size(64, 64)
        imgList.ColorDepth = ColorDepth.Depth32Bit
        imgList.Images.Add(templateMan.Name, Image.FromFile(templateMan.thumbnail))

        ListView1.LargeImageList = imgList
        ListView1.MultiSelect = False
        ListView1.View = View.LargeIcon
        ListView1.Items.Add(New ListViewItem(templateMan.Name, templateMan.Name))

        lblDes.Text = templateMan.Description
    End Sub

    Private Sub frmCreateProject_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        Me.BringToFront()
        Me.Focus()

    End Sub

    Private Sub ListBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBox1.SelectedIndexChanged

    End Sub

    Private Sub ListBox1_Click(sender As Object, e As EventArgs) Handles ListBox1.Click
        lblProjectType.Text = ListBox1.SelectedItem.ToString()
    End Sub

    Private Sub ListView1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListView1.SelectedIndexChanged

    End Sub
End Class