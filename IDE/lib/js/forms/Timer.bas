' Portable control library - Timer (task 39).
'
' The web template keeps its own w.setInterval path. A portable form constructs a
' System.Windows.Forms.Timer instead; this class is the web implementation of that type.
' Enabled is the switch: there is no interval handle while the Timer is stopped.
Namespace System.Windows.Forms
    Public Class Timer
        Private _interval As Integer
        Private _enabled As Boolean
        Private _handle As Integer

        Public Sub New()
            _interval = 100
            _enabled = False
            _handle = 0
        End Sub

        Public Property Interval As Integer
            Get
                Return _interval
            End Get
            Set(value As Integer)
                If value < 1 Then Throw New ArgumentOutOfRangeException("Interval")
                If _interval = value Then Return
                _interval = value
                If _enabled Then
                    Me.VgsStopInterval()
                    Me.VgsStartInterval()
                End If
            End Set
        End Property

        Public Property Enabled As Boolean
            Get
                Return _enabled
            End Get
            Set(value As Boolean)
                If _enabled = value Then Return
                _enabled = value
                If value Then
                    Me.VgsStartInterval()
                Else
                    Me.VgsStopInterval()
                End If
            End Set
        End Property

        Public Event Tick(sender As Object, e As EventArgs)

        Protected Overridable Sub OnTick(e As EventArgs)
            RaiseEvent Tick(Me, e)
        End Sub

        Public Sub Start()
            Me.Enabled = True
        End Sub

        Public Sub Stop()
            Me.Enabled = False
        End Sub

        Private Sub VgsStartInterval()
            Dim w As Window = ::window
            Dim tick As Action = Sub() Me.VgsTick()
            _handle = w.setInterval(tick, _interval)
        End Sub

        Private Sub VgsStopInterval()
            If _handle = 0 Then Return
            Dim w As Window = ::window
            w.clearInterval(_handle)
            _handle = 0
        End Sub

        Private Sub VgsTick()
            Me.OnTick(EventArgs.Empty)
        End Sub
    End Class
End Namespace
