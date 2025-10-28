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
        lstProjectType = New ListBox()
        lstView = New ListView()
        Button1 = New Button()
        Button2 = New Button()
        lblProjectType = New Label()
        lblDes = New Label()
        SaveFileDialog1 = New SaveFileDialog()
        SuspendLayout()
        ' 
        ' lstProjectType
        ' 
        lstProjectType.FormattingEnabled = True
        lstProjectType.ItemHeight = 15
        lstProjectType.Location = New Point(5, 12)
        lstProjectType.Name = "lstProjectType"
        lstProjectType.Size = New Size(121, 364)
        lstProjectType.TabIndex = 0
        ' 
        ' lstView
        ' 
        lstView.Location = New Point(132, 12)
        lstView.Name = "lstView"
        lstView.Size = New Size(533, 362)
        lstView.TabIndex = 1
        lstView.UseCompatibleStateImageBehavior = False
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
        ' SaveFileDialog1
        ' 
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
        Controls.Add(lstView)
        Controls.Add(lstProjectType)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "frmCreateProject"
        Text = "New from template"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents lstProjectType As ListBox
    Friend WithEvents lstView As ListView
    Friend WithEvents Button1 As Button
    Friend WithEvents Button2 As Button
    Friend WithEvents lblProjectType As Label
    Friend WithEvents lblDes As Label
    Friend WithEvents SaveFileDialog1 As SaveFileDialog
End Class
