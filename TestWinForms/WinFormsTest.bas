' WinForms test in BasicLang
Using System.Windows.Forms
Using System.Drawing

Sub Main()
    Dim frm As Form = New Form()
    frm.Text = "My BasicLang WinForms App"
    frm.Size = New Size(400, 300)
    frm.StartPosition = FormStartPosition.CenterScreen

    Dim btn As Button = New Button()
    btn.Text = "Click Me!"
    btn.Size = New Size(100, 30)
    btn.Location = New Point(150, 100)
    frm.Controls.Add(btn)

    Dim lbl As Label = New Label()
    lbl.Text = "Hello from BasicLang!"
    lbl.Location = New Point(130, 50)
    lbl.AutoSize = True
    frm.Controls.Add(lbl)

    Application.Run(frm)
End Sub
