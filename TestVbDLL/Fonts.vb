Module Fonts

    'UI font
    Dim _UI As FontHandle

    Public Function LoadFonts() As Boolean
        Try
            ' Load game fonts here
            _UI = New FontHandle("resources/font.ttf", 20)
        Catch ex As Exception
            Console.WriteLine("Failed to load font: " & ex.Message)
            Return False
        End Try
        Return True

    End Function

    Public Sub UnloadFonts()
        If _UI IsNot Nothing Then
            _UI.Dispose()
            _UI = Nothing
        End If
    End Sub
End Module
