Imports RaylibWrapper.UtiliyClasses

Public Class Player
    Public Property Name As String
    Public Property Score As Integer
    Public Property avatar As TextureHandle
    Public Property gPaddle As Paddle
    'Public Property TextColor As 
    Public Property ID As Integer

    Public Sub New(id As Integer, name As String, score As Integer, paddle As Paddle)
        Me.Name = name
        Me.Score = score
        'Me.avatar = avatar
        Me.ID = id
        Me.gPaddle = paddle
        Me.gPaddle.ownerID = Me.ID
    End Sub



End Class
