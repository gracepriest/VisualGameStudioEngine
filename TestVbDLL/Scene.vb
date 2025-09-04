' SceneBridge.vb
' Requires the delegates/structs and P/Invokes already in RaylibWrapper.vb:
'   - SceneVoidFn, SceneUpdateFixedFn, SceneUpdateFrameFn, SceneCallbacks
'   - Framework_CreateScriptScene, Framework_SceneChange, Framework_SceneTick, Framework_SetDrawCallback

' Abstract base (VB: MustInherit + MustOverride == C++ pure virtuals)
Public MustInherit Class Scene
    Public sceneId As Integer = -1
    Protected MustOverride Sub OnEnter()
    Protected MustOverride Sub OnExit()
    Protected MustOverride Sub OnResume()
    Protected MustOverride Sub OnUpdateFixed(dt As Double)
    Protected MustOverride Sub OnUpdateFrame(dt As Single)
    Protected MustOverride Sub OnDraw()

    Public Sub enter()
        OnEnter()
    End Sub
    Public Sub [exit]()
        OnExit()
    End Sub
    Public Sub [resume]()
        OnResume()
    End Sub
    Public Sub updateFixed(dt As Double)
        OnUpdateFixed(dt)
    End Sub
    Public Sub updateFrame(dt As Single)
        OnUpdateFrame(dt)
    End Sub
    Public Sub draw()
        OnDraw()
    End Sub

End Class

Module SceneBridge

    ' Global “current” scene (like static Scene* gCurrent)
    Private _current As Scene

    ' Keep all delegates rooted so the GC never collects them
    Private ReadOnly _dEnter As SceneVoidFn = AddressOf CB_OnEnter
    Private ReadOnly _dExit As SceneVoidFn = AddressOf CB_OnExit
    Private ReadOnly _dResume As SceneVoidFn = AddressOf CB_OnResume
    Private ReadOnly _dFixed As SceneUpdateFixedFn = AddressOf CB_OnUpdateFixed
    Private ReadOnly _dFrame As SceneUpdateFrameFn = AddressOf CB_OnUpdateFrame
    Private ReadOnly _dDraw As SceneVoidFn = AddressOf CB_OnDraw

    ' Draw callback that ticks the native scene stack
    Private ReadOnly _drawDel As DrawCallback = AddressOf EngineDraw

    ' --------- C++-style forwarders ---------
    Private Sub CB_OnEnter()
        If _current IsNot Nothing Then
            _current.enter()
        End If
    End Sub

    Private Sub CB_OnExit()
        If _current IsNot Nothing Then
            _current.exit()
        End If
    End Sub

    Private Sub CB_OnResume()
        If _current IsNot Nothing Then
            _current.[resume]()
        End If
    End Sub

    Private Sub CB_OnUpdateFixed(dt As Double)
        If _current IsNot Nothing Then
            _current.updateFixed(dt)
        End If
    End Sub

    Private Sub CB_OnUpdateFrame(dt As Single)
        If _current IsNot Nothing Then
            _current.updateFrame(dt)
        End If
    End Sub

    Private Sub CB_OnDraw()
        If _current IsNot Nothing Then
            _current.draw()
        End If
    End Sub

    Private Sub EngineDraw()
        ' Bridge into the engine’s scene tick each frame
        Framework_SceneTick()
    End Sub

    Private Function MakeCallbacks() As SceneCallbacks
        Return New SceneCallbacks With {
            .onEnter = _dEnter,
            .onExit = _dExit,
            .onResume = _dResume,
            .onUpdateFixed = _dFixed,
            .onUpdateFrame = _dFrame,
            .onDraw = _dDraw
        }
    End Function

    ' ---- Public API (matches your SetCurrentScene / WireEngineDraw) ----
    Public Function SetCurrentScene(scene As Scene) As Integer
        _current = scene
        Dim cb = MakeCallbacks()
        Dim handle = Framework_CreateScriptScene(cb)
        Framework_SceneChange(handle)
        Return handle
    End Function

    Public Function GetCurrentScene() As Scene
        Return _current
    End Function
    ' Create scene WITHOUT changing/pushing
    Public Function RegisterScene(scene As Scene) As Integer
        _current = scene                   ' make bridge dispatch into this scene
        Dim cb = MakeCallbacks()           ' build delegates for THIS scene
        Dim id = Framework_CreateScriptScene(cb)
        scene.sceneId = id
        Return id
    End Function

    Public Sub WireEngineDraw()
        Framework_SetDrawCallback(_drawDel)
    End Sub

    Private ReadOnly _managedStack As New Stack(Of Scene)


    ' Only registers the scene and returns its id (no change/push here)


    Public Function ChangeTo(scene As Scene) As Integer
        Dim id = RegisterScene(scene)
        _managedStack.Clear()
        _managedStack.Push(scene)
        Framework_SceneChange(id)
        Return id
    End Function

    Public Function Push(scene As Scene) As Integer
        Dim id = RegisterScene(scene)
        _managedStack.Push(scene)
        Framework_ScenePush(id)
        Return id
    End Function

    Public Sub Pop()
        ' swap _current to what will be shown after the native pop
        If _managedStack.Count > 0 Then _managedStack.Pop()
        _current = If(_managedStack.Count > 0, _managedStack.Peek(), Nothing)
        Framework_ScenePop()
    End Sub
End Module
