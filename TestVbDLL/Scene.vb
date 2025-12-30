' SceneBridge.vb
' Requires delegates/structs + P/Invokes already defined in RaylibWrapper.vb:
'   SceneVoidFn, SceneUpdateFixedFn, SceneUpdateFrameFn, SceneCallbacks
'   Framework_CreateScriptScene, Framework_SceneChange, Framework_ScenePush,
'   Framework_ScenePop, Framework_SceneTick, Framework_SetDrawCallback

Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

' Abstract scene base = C++ "scripted scene" interface
Public MustInherit Class Scene
    Public sceneId As Integer = -1

    Protected MustOverride Sub OnEnter()
    Protected MustOverride Sub OnExit()
    Protected MustOverride Sub OnResume()
    Protected MustOverride Sub OnUpdateFixed(dt As Double)
    Protected MustOverride Sub OnUpdateFrame(dt As Single)
    Protected MustOverride Sub OnDraw()

    ' Public façade (so engine code calls these, not the MustOverride methods directly)
    Public Sub Enter()
        OnEnter()
    End Sub

    Public Sub [Exit]()
        OnExit()
    End Sub

    Public Sub [Resume]()
        OnResume()
    End Sub

    Public Sub UpdateFixed(dt As Double)
        OnUpdateFixed(dt)
    End Sub

    Public Sub UpdateFrame(dt As Single)
        OnUpdateFrame(dt)
    End Sub

    Public Sub Draw()
        OnDraw()
    End Sub
End Class

Module SceneBridge

    ' ================
    ' Managed state
    ' ================

    ' The VB scene that should receive all update/draw callbacks
    Private _current As Scene

    ' Simple mirror of the native scene stack (top = Peek())
    Private ReadOnly _managedStack As New Stack(Of Scene)

    ' ===========================
    ' Delegate roots (GC pin)
    ' ===========================

    ' We only use callbacks for fixed/frame/draw.
    ' Enter / Exit / Resume are handled in VB directly.
    Private ReadOnly _dFixed As SceneUpdateFixedFn = AddressOf CB_OnUpdateFixed
    Private ReadOnly _dFrame As SceneUpdateFrameFn = AddressOf CB_OnUpdateFrame
    Private ReadOnly _dDraw As SceneVoidFn = AddressOf CB_OnDraw

    ' Draw callback for the *engine* (called once per frame by Framework_Update)
    Private ReadOnly _drawDel As DrawCallback = AddressOf EngineDraw

    ' ===========================
    ' Callback implementations
    ' ===========================

    Private Sub CB_OnUpdateFixed(dt As Double)
        Dim s = _current
        If s IsNot Nothing Then
            s.UpdateFixed(dt)
        End If
    End Sub

    Private Sub CB_OnUpdateFrame(dt As Single)
        Dim s = _current
        If s IsNot Nothing Then
            s.UpdateFrame(dt)
        End If
    End Sub

    Private Sub CB_OnDraw()
        Dim s = _current
        If s IsNot Nothing Then
            s.Draw()
        End If
    End Sub

    ' Called by raylib every frame via Framework_Update -> userDrawCallback
    Private Sub EngineDraw()
        ' Let the native scene system run fixed-step + frame + draw.
        Framework_SceneTick()
    End Sub

    ' Build a SceneCallbacks struct for a VB scene.
    ' NOTE: Enter/Exit/Resume are NULL here; we drive those manually in VB.
    Private Function MakeCallbacks() As SceneCallbacks
        Return New SceneCallbacks With {
            .onEnter = Nothing,
            .onExit = Nothing,
            .onResume = Nothing,
            .onUpdateFixed = _dFixed,
            .onUpdateFrame = _dFrame,
            .onDraw = _dDraw
        }
    End Function

    ' Register a scene with the native engine and assign its handle.
    Private Function RegisterScene(scene As Scene) As Integer
        Dim cb = MakeCallbacks()
        Dim id = Framework_CreateScriptScene(cb)
        scene.sceneId = id
        Return id
    End Function

    ' ===========================
    ' Public API
    ' ===========================

    ' Hook the engine’s draw callback exactly once at startup.
    Public Sub WireEngineDraw()
        Framework_SetDrawCallback(_drawDel)
    End Sub

    Public Function GetCurrentScene() As Scene
        Return _current
    End Function

    ' Simple alias for “hard change” (clears stack, goes to new scene)
    Public Function SetCurrentScene(scene As Scene) As Integer
        Return ChangeTo(scene)
    End Function

    ' Replace whatever is on the stack with a new scene
    Public Function ChangeTo(scene As Scene) As Integer
        ' Call Exit() on the old top (if any)
        Dim old As Scene = If(_managedStack.Count > 0, _managedStack.Peek(), Nothing)
        If old IsNot Nothing Then
            old.Exit()
        End If

        ' Reset our managed stack and push the new scene
        _managedStack.Clear()
        _managedStack.Push(scene)

        ' Make this the “current” scene for callbacks
        _current = scene

        ' Call Enter() in managed land
        scene.Enter()

        ' Register with native + change top of native stack
        Dim id = RegisterScene(scene)
        Framework_SceneChange(id)
        Return id
    End Function

    ' Push a new scene on top (e.g. pause menu)
    Public Function Push(scene As Scene) As Integer
        ' Optional: you could add an OnPause() on Scene if you want.
        ' For now we just push the new scene.

        _managedStack.Push(scene)
        _current = scene

        ' Scene lifecycle in VB
        scene.Enter()

        ' Register with engine + push onto native stack
        Dim id = RegisterScene(scene)
        Framework_ScenePush(id)
        Return id
    End Function

    ' Pop the top scene and resume the one underneath
    Public Sub Pop()
        If _managedStack.Count = 0 Then Return

        ' Pop & exit the current scene (managed)
        Dim old As Scene = _managedStack.Pop()
        old.Exit()

        ' New top (if any)
        _current = If(_managedStack.Count > 0, _managedStack.Peek(), Nothing)

        ' Resume the scene underneath (managed)
        If _current IsNot Nothing Then
            _current.Resume()
        End If

        ' Keep native stack in sync
        Framework_ScenePop()
    End Sub

End Module
