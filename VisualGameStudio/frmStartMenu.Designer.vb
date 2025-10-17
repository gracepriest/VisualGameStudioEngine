<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmStartMenu
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
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
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmStartMenu))
        PictureBox1 = New PictureBox()
        LblTitle = New Label()
        btnCreate = New Button()
        btnOpen = New Button()
        lblRecent = New Label()
        lstRecent = New ListBox()
        lblVersion = New Label()
        CType(PictureBox1, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        ' 
        ' PictureBox1
        ' 
        PictureBox1.Image = CType(resources.GetObject("PictureBox1.Image"), Image)
        PictureBox1.Location = New Point(232, 25)
        PictureBox1.Name = "PictureBox1"
        PictureBox1.Size = New Size(100, 50)
        PictureBox1.SizeMode = PictureBoxSizeMode.StretchImage
        PictureBox1.TabIndex = 0
        PictureBox1.TabStop = False
        ' 
        ' LblTitle
        ' 
        LblTitle.AutoSize = True
        LblTitle.Font = New Font("Segoe UI", 18F, FontStyle.Regular, GraphicsUnit.Point, CByte(0))
        LblTitle.Location = New Point(154, 94)
        LblTitle.Name = "LblTitle"
        LblTitle.Size = New Size(251, 32)
        LblTitle.TabIndex = 1
        LblTitle.Text = "VISUAL GAME STUDIO"
        ' 
        ' btnCreate
        ' 
        btnCreate.Location = New Point(167, 172)
        btnCreate.Name = "btnCreate"
        btnCreate.Size = New Size(238, 23)
        btnCreate.TabIndex = 2
        btnCreate.Text = "CREATE NEW PROJECT"
        btnCreate.UseVisualStyleBackColor = True
        ' 
        ' btnOpen
        ' 
        btnOpen.Location = New Point(167, 211)
        btnOpen.Name = "btnOpen"
        btnOpen.Size = New Size(238, 23)
        btnOpen.TabIndex = 3
        btnOpen.Text = "OPEN EXISTING PROJECT"
        btnOpen.UseVisualStyleBackColor = True
        ' 
        ' lblRecent
        ' 
        lblRecent.AutoSize = True
        lblRecent.Location = New Point(154, 250)
        lblRecent.Name = "lblRecent"
        lblRecent.Size = New Size(88, 15)
        lblRecent.TabIndex = 4
        lblRecent.Text = "Recent Projects"
        ' 
        ' lstRecent
        ' 
        lstRecent.FormattingEnabled = True
        lstRecent.ItemHeight = 15
        lstRecent.Location = New Point(163, 292)
        lstRecent.Name = "lstRecent"
        lstRecent.Size = New Size(242, 94)
        lstRecent.TabIndex = 5
        ' 
        ' lblVersion
        ' 
        lblVersion.AutoSize = True
        lblVersion.Location = New Point(410, 412)
        lblVersion.Name = "lblVersion"
        lblVersion.Size = New Size(13, 15)
        lblVersion.TabIndex = 6
        lblVersion.Text = "0"
        ' 
        ' frmStartMenu
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(565, 450)
        Controls.Add(lblVersion)
        Controls.Add(lstRecent)
        Controls.Add(lblRecent)
        Controls.Add(btnOpen)
        Controls.Add(btnCreate)
        Controls.Add(LblTitle)
        Controls.Add(PictureBox1)
        Name = "frmStartMenu"
        StartPosition = FormStartPosition.CenterScreen
        CType(PictureBox1, ComponentModel.ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents PictureBox1 As PictureBox
    Friend WithEvents LblTitle As Label
    Friend WithEvents btnCreate As Button
    Friend WithEvents btnOpen As Button
    Friend WithEvents lblRecent As Label
    Friend WithEvents lstRecent As ListBox
    Friend WithEvents lblVersion As Label

End Class
