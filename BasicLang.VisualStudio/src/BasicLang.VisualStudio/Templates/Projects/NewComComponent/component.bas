' $safeprojectname$ - BasicLang binding for NewCOM component
' Created with Visual Studio 2022

Declare Function ${safeprojectname$}_Create Lib "$safeprojectname$_newcom" () As Integer
Declare Function ${safeprojectname$}_GetValue Lib "$safeprojectname$_newcom" (h As Integer) As Integer
Declare Sub ${safeprojectname$}_Release Lib "$safeprojectname$_newcom" (h As Integer)

Class $safeprojectname$
    Private _handle As Integer

    Public Sub New()
        _handle = ${safeprojectname$}_Create()
    End Sub

    Public Function GetValue() As Integer
        Return ${safeprojectname$}_GetValue(_handle)
    End Function

    Public Sub Close()
        ${safeprojectname$}_Release(_handle)
        _handle = 0
    End Sub
End Class
