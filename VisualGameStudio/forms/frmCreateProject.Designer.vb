<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmCreateProject
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        ListBox1 = New ListBox()
        ListView1 = New ListView()
        Button1 = New Button()
        Button2 = New Button()
        lblProjectType = New Label()
        lblDes = New Label()
        SuspendLayout()
        ' 
        ' ListBox1
        ' 
        ListBox1.FormattingEnabled = True
        ListBox1.ItemHeight = 15
        ListBox1.Location = New Point(5, 12)
        ListBox1.Name = "ListBox1"
        ListBox1.Size = New Size(121, 364)
        ListBox1.TabIndex = 0
        ' 
        ' ListView1
        ' 
        ListView1.Location = New Point(134, 12)
        ListView1.Name = "ListView1"
        ListView1.Size = New Size(533, 362)
        ListView1.TabIndex = 1
        ListView1.UseCompatibleStateImageBehavior = False
        ' 
        ' Button1
        ' 
        Button1.Location = New Point(700, 12)
        Button1.Name = "Button1"
        Button1.Size = New Size(79, 31)
        Button1.TabIndex = 2
        Button1.Text = "Select"
        Button1.UseVisualStyleBackColor = True
        ' 
        ' Button2
        ' 
        Button2.Location = New Point(700, 49)
        Button2.Name = "Button2"
        Button2.Size = New Size(79, 31)
        Button2.TabIndex = 3
        Button2.Text = "Cancel"
        Button2.UseVisualStyleBackColor = True
        ' 
        ' lblProjectType
        ' 
        lblProjectType.AutoSize = True
        lblProjectType.Location = New Point(21, 401)
        lblProjectType.Name = "lblProjectType"
        lblProjectType.Size = New Size(41, 15)
        lblProjectType.TabIndex = 4
        lblProjectType.Text = "Label1"
        ' 
        ' lblDes
        ' 
        lblDes.AutoSize = True
        lblDes.Location = New Point(156, 401)
        lblDes.Name = "lblDes"
        lblDes.Size = New Size(41, 15)
        lblDes.TabIndex = 5
        lblDes.Text = "Label1"
        ' 
        ' frmCreateProject
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = SystemColors.ControlLight
        ClientSize = New Size(800, 450)
        Controls.Add(lblDes)
        Controls.Add(lblProjectType)
        Controls.Add(Button2)
        Controls.Add(Button1)
        Controls.Add(ListView1)
        Controls.Add(ListBox1)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "frmCreateProject"
        Text = "New from template"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents ListBox1 As ListBox
    Friend WithEvents ListView1 As ListView
    Friend WithEvents Button1 As Button
    Friend WithEvents Button2 As Button
    Friend WithEvents lblProjectType As Label
    Friend WithEvents lblDes As Label
End Class
