Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy
Imports RaylibWrapper.UtiliyClasses

Public Module Fonts

    'UI font
    Public RETRO_FONT As FontHandle

    Public Function LoadFonts() As Boolean
        Try
            ' Load game fonts here
            RETRO_FONT = New FontHandle("fonts\retro.ttf", 20)
        Catch ex As Exception
            Console.WriteLine("Failed to load font: " & ex.Message)
            Return False
        End Try
        Return True

    End Function

    Public Sub UnloadFonts()
        If RETRO_FONT IsNot Nothing Then

            RETRO_FONT.Dispose()
            RETRO_FONT = Nothing
        End If
    End Sub
End Module
