
Module Sound
    ' Game sounds
    Public sfxHit As Integer
    Public sfxWall As Integer
    Public Function LoadSounds() As Boolean
        Try
            ' Load game sounds here
            sfxHit = Framework_LoadSoundH("hit.wav")
            sfxWall = Framework_LoadSoundH("wall.wav")
            ' Load music stream
            Dim music As MusicHandle = New MusicHandle("music.mp3")
        Catch ex As Exception
            Console.WriteLine("Failed to load sound: " & ex.Message)
            Return False
        End Try
        Return True
    End Function
    ' Unload sounds
    Public Sub UnloadSounds()
        If sfxHit <> 0 Then
            Framework_UnloadSoundH(sfxHit)
            sfxHit = 0
        End If
        If sfxWall <> 0 Then
            Framework_UnloadSoundH(sfxWall)
            sfxWall = 0
        End If
        ' MusicHandle will be disposed automatically when it goes out of scope
    End Sub
End Module
