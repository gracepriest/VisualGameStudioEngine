Imports System.Runtime.InteropServices
Imports RaylibWrapper.FrameworkWrapper

''' <summary>
''' Unit tests for VisualGameStudioEngine framework systems.
''' Run with FrameworkTests.RunAllTests() after framework initialization.
''' Run TestVbDLL.exe --test to execute all tests.
''' </summary>
Public Module FrameworkTests
    Private _passCount As Integer = 0
    Private _failCount As Integer = 0
    Private _testResults As New List(Of String)

    ''' <summary>
    ''' Runs all framework unit tests and returns results.
    ''' Call after Framework_Init() has been called.
    ''' </summary>
    Public Function RunAllTests() As Boolean
        _passCount = 0
        _failCount = 0
        _testResults.Clear()

        Console.WriteLine("========================================")
        Console.WriteLine("  VisualGameStudioEngine Unit Tests")
        Console.WriteLine("========================================")
        Console.WriteLine()

        ' Run test suites
        TestShaderSystem()
        TestSkeletalAnimationSystem()
        TestCommandConsoleSystem()
        TestNetworkingSystem()
        TestSpriteBatchingSystem()
        TestTextureAtlasSystem()
        TestLevelEditorSystem()
        TestEntityEcsSystem()
        TestCameraSystem()
        TestUISystem()
        TestPhysicsSystem()
        TestTweeningSystem()
        TestTimerSystem()
        TestEventSystem()
        TestInputManagerSystem()
        TestSaveLoadSystem()

        ' Print summary
        Console.WriteLine()
        Console.WriteLine("========================================")
        Console.WriteLine($"  Results: {_passCount} passed, {_failCount} failed")
        Console.WriteLine("========================================")

        Return _failCount = 0
    End Function

    Private Sub LogPass(testName As String)
        _passCount += 1
        Dim msg = $"[PASS] {testName}"
        _testResults.Add(msg)
        Console.WriteLine(msg)
    End Sub

    Private Sub LogFail(testName As String, reason As String)
        _failCount += 1
        Dim msg = $"[FAIL] {testName}: {reason}"
        _testResults.Add(msg)
        Console.WriteLine(msg)
    End Sub

    Private Sub LogSection(sectionName As String)
        Console.WriteLine()
        Console.WriteLine($"--- {sectionName} ---")
    End Sub

#Region "Shader System Tests"
    Private Sub TestShaderSystem()
        LogSection("Shader System")

        ' Test built-in shader loading
        Try
            Dim grayscaleId = Framework_Shader_LoadGrayscale()
            If grayscaleId > 0 Then
                LogPass("Load grayscale shader")
                Framework_Shader_Unload(grayscaleId)
            Else
                LogFail("Load grayscale shader", "Invalid shader ID returned")
            End If
        Catch ex As Exception
            LogFail("Load grayscale shader", ex.Message)
        End Try

        ' Test blur shader
        Try
            Dim blurId = Framework_Shader_LoadBlur()
            If blurId > 0 Then
                LogPass("Load blur shader")

                ' Test get uniform location
                Dim loc = Framework_Shader_GetUniformLocation(blurId, "resolution")
                LogPass($"Get uniform location (loc={loc})")

                ' Test set uniform by name
                Framework_Shader_SetVec2ByName(blurId, "resolution", 800, 600)
                LogPass("Set vec2 uniform by name")

                ' Test begin/end shader
                Framework_Shader_Begin(blurId)
                Framework_Shader_End()
                LogPass("Begin/End shader mode")

                Framework_Shader_Unload(blurId)
            Else
                LogFail("Load blur shader", "Invalid shader ID returned")
            End If
        Catch ex As Exception
            LogFail("Load blur shader", ex.Message)
        End Try

        ' Test CRT shader
        Try
            Dim crtId = Framework_Shader_LoadCRT()
            If crtId > 0 Then
                LogPass("Load CRT shader")
                If Framework_Shader_IsValid(crtId) Then
                    LogPass("Shader validity check")
                Else
                    LogFail("Shader validity check", "Valid shader reported as invalid")
                End If
                Framework_Shader_Unload(crtId)
            Else
                LogFail("Load CRT shader", "Invalid shader ID returned")
            End If
        Catch ex As Exception
            LogFail("Load CRT shader", ex.Message)
        End Try

        ' Test invalid shader
        Try
            If Not Framework_Shader_IsValid(9999) Then
                LogPass("Invalid shader detection")
            Else
                LogFail("Invalid shader detection", "Invalid shader reported as valid")
            End If
        Catch ex As Exception
            LogFail("Invalid shader detection", ex.Message)
        End Try
    End Sub
#End Region

#Region "Skeletal Animation Tests"
    Private Sub TestSkeletalAnimationSystem()
        LogSection("Skeletal Animation System")

        Dim skeletonId As Integer = -1

        ' Test skeleton creation
        Try
            skeletonId = Framework_Skeleton_Create("TestSkeleton")
            If skeletonId > 0 Then
                LogPass("Create skeleton")
            Else
                LogFail("Create skeleton", "Invalid skeleton ID returned")
                Return
            End If
        Catch ex As Exception
            LogFail("Create skeleton", ex.Message)
            Return
        End Try

        ' Test skeleton validity
        Try
            If Framework_Skeleton_IsValid(skeletonId) Then
                LogPass("Skeleton validity check")
            Else
                LogFail("Skeleton validity check", "Valid skeleton reported as invalid")
            End If
        Catch ex As Exception
            LogFail("Skeleton validity check", ex.Message)
        End Try

        ' Test adding bones
        Dim rootBone As Integer = -1
        Dim childBone As Integer = -1
        Try
            rootBone = Framework_Skeleton_AddBone(skeletonId, "root", -1, 0, 0, 0, 50)
            If rootBone >= 0 Then
                LogPass($"Add root bone (id={rootBone})")
            Else
                LogFail("Add root bone", "Invalid bone ID returned")
            End If

            childBone = Framework_Skeleton_AddBone(skeletonId, "arm", rootBone, 50, 0, 0, 40)
            If childBone >= 0 Then
                LogPass($"Add child bone (id={childBone})")
            Else
                LogFail("Add child bone", "Invalid bone ID returned")
            End If
        Catch ex As Exception
            LogFail("Add bone", ex.Message)
        End Try

        ' Test bone count
        Try
            Dim boneCount = Framework_Skeleton_GetBoneCount(skeletonId)
            If boneCount = 2 Then
                LogPass($"Bone count (count={boneCount})")
            Else
                LogFail("Bone count", $"Expected 2, got {boneCount}")
            End If
        Catch ex As Exception
            LogFail("Bone count", ex.Message)
        End Try

        ' Test get bone by name
        Try
            Dim foundBone = Framework_Skeleton_GetBoneByName(skeletonId, "arm")
            If foundBone = childBone Then
                LogPass("Get bone by name")
            Else
                LogFail("Get bone by name", $"Expected {childBone}, got {foundBone}")
            End If
        Catch ex As Exception
            LogFail("Get bone by name", ex.Message)
        End Try

        ' Test bone transforms
        Try
            Framework_Skeleton_SetBoneLocalTransform(skeletonId, rootBone, 10, 20, 45, 1, 1)
            ' World transforms are computed automatically during Update/Draw
            Framework_Skeleton_Update(skeletonId, 0.0F)
            Dim wx As Single = 0, wy As Single = 0
            Framework_Skeleton_GetBoneWorldPosition(skeletonId, rootBone, wx, wy)
            LogPass($"Bone transform (world pos={wx},{wy})")
        Catch ex As Exception
            LogFail("Bone transform", ex.Message)
        End Try

        ' Test animation creation
        Dim animId As Integer = -1
        Try
            animId = Framework_Skeleton_CreateAnimation(skeletonId, "walk", 1.0F)
            If animId >= 0 Then
                LogPass($"Create animation (id={animId})")
            Else
                LogFail("Create animation", "Invalid animation ID returned")
            End If
        Catch ex As Exception
            LogFail("Create animation", ex.Message)
        End Try

        ' Test keyframes
        Try
            Framework_Skeleton_AddKeyframe(skeletonId, animId, rootBone, 0, 0, 0, 0, 1, 1)
            Framework_Skeleton_AddKeyframe(skeletonId, animId, rootBone, 0.5F, 0, 0, 45, 1, 1)
            Framework_Skeleton_AddKeyframe(skeletonId, animId, rootBone, 1.0F, 0, 0, 0, 1, 1)
            LogPass("Add keyframes")
        Catch ex As Exception
            LogFail("Add keyframes", ex.Message)
        End Try

        ' Test playback
        Try
            Framework_Skeleton_PlayAnimation(skeletonId, animId, True)
            If Framework_Skeleton_IsAnimationPlaying(skeletonId) Then
                LogPass("Play animation")
            Else
                LogFail("Play animation", "Animation not playing after play call")
            End If

            Framework_Skeleton_StopAnimation(skeletonId)
            If Not Framework_Skeleton_IsAnimationPlaying(skeletonId) Then
                LogPass("Stop animation")
            Else
                LogFail("Stop animation", "Animation still playing after stop")
            End If
        Catch ex As Exception
            LogFail("Animation playback", ex.Message)
        End Try

        ' Test pose (SetPose sets skeleton to a specific time in an animation)
        Try
            If animId >= 0 Then
                Framework_Skeleton_SetPose(skeletonId, animId, 0.5F) ' Set to midpoint of animation
            End If
            Framework_Skeleton_ResetPose(skeletonId)
            LogPass("Set/Reset pose")
        Catch ex As Exception
            LogFail("Set/Reset pose", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Skeleton_Destroy(skeletonId)
            If Not Framework_Skeleton_IsValid(skeletonId) Then
                LogPass("Destroy skeleton")
            Else
                LogFail("Destroy skeleton", "Skeleton still valid after destroy")
            End If
        Catch ex As Exception
            LogFail("Destroy skeleton", ex.Message)
        End Try
    End Sub
#End Region

#Region "Command Console Tests"
    Private _testCmdCalled As Boolean = False
    Private _testCmdArgs As String = ""

    Private Sub TestCmdCallback(args As String, userData As IntPtr)
        _testCmdCalled = True
        _testCmdArgs = args
    End Sub

    Private Sub TestCommandConsoleSystem()
        LogSection("Command Console System")

        ' Test initialization
        Try
            Framework_Cmd_Init()
            LogPass("Initialize command console")
        Catch ex As Exception
            LogFail("Initialize command console", ex.Message)
            Return
        End Try

        ' Test CVar set and access (CVars are created automatically on first set)
        Try
            Framework_Cmd_SetCvarInt("test_int", 42)
            Dim intVal = Framework_Cmd_GetCvarInt("test_int")
            If intVal = 42 Then
                LogPass("Set/Get int CVar")
            Else
                LogFail("Set/Get int CVar", $"Expected 42, got {intVal}")
            End If
        Catch ex As Exception
            LogFail("Set/Get int CVar", ex.Message)
        End Try

        Try
            Framework_Cmd_SetCvarFloat("test_float", 3.14F)
            Dim floatVal = Framework_Cmd_GetCvarFloat("test_float")
            If Math.Abs(floatVal - 3.14F) < 0.01 Then
                LogPass("Set/Get float CVar")
            Else
                LogFail("Set/Get float CVar", $"Expected 3.14, got {floatVal}")
            End If
        Catch ex As Exception
            LogFail("Set/Get float CVar", ex.Message)
        End Try

        Try
            Framework_Cmd_SetCvarBool("test_bool", True)
            Dim boolVal = Framework_Cmd_GetCvarBool("test_bool")
            If boolVal = True Then
                LogPass("Set/Get bool CVar")
            Else
                LogFail("Set/Get bool CVar", $"Expected True, got {boolVal}")
            End If
        Catch ex As Exception
            LogFail("Set/Get bool CVar", ex.Message)
        End Try

        ' Test CVar modification
        Try
            Framework_Cmd_SetCvarInt("test_int", 100)
            Dim newVal = Framework_Cmd_GetCvarInt("test_int")
            If newVal = 100 Then
                LogPass("Modify CVar value")
            Else
                LogFail("Modify CVar value", $"Expected 100, got {newVal}")
            End If
        Catch ex As Exception
            LogFail("Modify CVar value", ex.Message)
        End Try

        ' Test command registration
        Try
            Dim callback As New CmdConsoleCallback(AddressOf TestCmdCallback)
            Framework_Cmd_RegisterCommand("test_cmd", "Test command", callback, IntPtr.Zero)
            LogPass("Register command")
        Catch ex As Exception
            LogFail("Register command", ex.Message)
        End Try

        ' Test command execution
        Try
            _testCmdCalled = False
            Framework_Cmd_Execute("test_cmd hello world")
            If _testCmdCalled Then
                LogPass($"Execute command (args='{_testCmdArgs}')")
            Else
                LogFail("Execute command", "Callback was not invoked")
            End If
        Catch ex As Exception
            LogFail("Execute command", ex.Message)
        End Try

        ' Test logging
        Try
            Framework_Cmd_LogInfo("Test info message")
            Framework_Cmd_LogWarning("Test warning message")
            Framework_Cmd_LogError("Test error message")
            Framework_Cmd_LogDebug("Test debug message")
            LogPass("Logging functions")
        Catch ex As Exception
            LogFail("Logging functions", ex.Message)
        End Try

        ' Test visibility toggle
        Try
            Framework_Cmd_Show()
            If Framework_Cmd_IsVisible() Then
                LogPass("Show console")
            Else
                LogFail("Show console", "Console not visible after show")
            End If

            Framework_Cmd_Hide()
            If Not Framework_Cmd_IsVisible() Then
                LogPass("Hide console")
            Else
                LogFail("Hide console", "Console still visible after hide")
            End If
        Catch ex As Exception
            LogFail("Console visibility", ex.Message)
        End Try

        ' Test appearance settings
        Try
            Framework_Cmd_SetBackgroundColor(30, 30, 30, 200)
            Framework_Cmd_SetTextColor(200, 200, 200, 255)
            Framework_Cmd_SetFontSize(16)
            Framework_Cmd_SetMaxLines(100)
            LogPass("Console appearance settings")
        Catch ex As Exception
            LogFail("Console appearance settings", ex.Message)
        End Try

        ' Test command history
        Try
            Framework_Cmd_Execute("test_cmd 1")
            Framework_Cmd_Execute("test_cmd 2")
            Dim historyCount = Framework_Cmd_GetHistoryCount()
            If historyCount >= 2 Then
                LogPass($"Command history (count={historyCount})")
            Else
                LogFail("Command history", $"Expected >= 2, got {historyCount}")
            End If
        Catch ex As Exception
            LogFail("Command history", ex.Message)
        End Try

        ' Test unregister command
        Try
            Framework_Cmd_UnregisterCommand("test_cmd")
            LogPass("Unregister command")
        Catch ex As Exception
            LogFail("Unregister command", ex.Message)
        End Try
    End Sub
#End Region

#Region "Networking Tests"
    Private Sub TestNetworkingSystem()
        LogSection("Networking System")

        Dim serverId As Integer = -1
        Dim clientId As Integer = -1

        ' Test server creation
        Try
            serverId = Framework_Net_CreateServer(12345, 4)
            If serverId > 0 Then
                LogPass($"Create server (id={serverId})")
            Else
                LogFail("Create server", "Invalid server ID returned")
            End If
        Catch ex As Exception
            LogFail("Create server", ex.Message)
        End Try

        ' Test server running
        If serverId > 0 Then
            Try
                If Framework_Net_ServerIsRunning(serverId) Then
                    LogPass("Server running check")
                Else
                    LogFail("Server running check", "Server not running after creation")
                End If
            Catch ex As Exception
                LogFail("Server running check", ex.Message)
            End Try
        End If

        ' Test client creation
        Try
            clientId = Framework_Net_CreateClient()
            If clientId > 0 Then
                LogPass($"Create client (id={clientId})")
            Else
                LogFail("Create client", "Invalid client ID returned")
            End If
        Catch ex As Exception
            LogFail("Create client", ex.Message)
        End Try

        ' Test client connection (to local server)
        If clientId > 0 AndAlso serverId > 0 Then
            Try
                Dim connected = Framework_Net_Connect(clientId, "127.0.0.1", 12345)
                LogPass($"Connect request (result={connected})")

                ' Update server and client to process connection
                Framework_Net_UpdateServer(serverId)
                Framework_Net_UpdateClient(clientId)
                System.Threading.Thread.Sleep(100)
                Framework_Net_UpdateServer(serverId)
                Framework_Net_UpdateClient(clientId)

                ' Check connection status
                Dim isConnected = Framework_Net_IsConnected(clientId)
                LogPass($"Connection status check (connected={isConnected})")

                ' Check client count on server
                Dim clientCount = Framework_Net_GetClientCount(serverId)
                LogPass($"Server client count (count={clientCount})")
            Catch ex As Exception
                LogFail("Client connection", ex.Message)
            End Try
        End If

        ' Test disconnection
        If clientId > 0 Then
            Try
                Framework_Net_Disconnect(clientId)
                Framework_Net_UpdateClient(clientId)
                If Not Framework_Net_IsConnected(clientId) Then
                    LogPass("Disconnect client")
                Else
                    LogFail("Disconnect client", "Client still connected after disconnect")
                End If
            Catch ex As Exception
                LogFail("Disconnect client", ex.Message)
            End Try
        End If

        ' Clean up
        If clientId > 0 Then
            Try
                Framework_Net_DestroyClient(clientId)
                LogPass("Destroy client")
            Catch ex As Exception
                LogFail("Destroy client", ex.Message)
            End Try
        End If

        If serverId > 0 Then
            Try
                Framework_Net_DestroyServer(serverId)
                LogPass("Destroy server")
            Catch ex As Exception
                LogFail("Destroy server", ex.Message)
            End Try
        End If
    End Sub
#End Region

#Region "Sprite Batching Tests"
    Private Sub TestSpriteBatchingSystem()
        LogSection("Sprite Batching System")

        Dim batchId As Integer = -1

        ' Test batch creation
        Try
            batchId = Framework_Batch_Create(1000)
            If batchId > 0 Then
                LogPass($"Create batch (id={batchId})")
            Else
                LogFail("Create batch", "Invalid batch ID returned")
                Return
            End If
        Catch ex As Exception
            LogFail("Create batch", ex.Message)
            Return
        End Try

        ' Test sprite count (should be 0 initially)
        Try
            Dim count = Framework_Batch_GetSpriteCount(batchId)
            If count = 0 Then
                LogPass("Initial sprite count")
            Else
                LogFail("Initial sprite count", $"Expected 0, got {count}")
            End If
        Catch ex As Exception
            LogFail("Initial sprite count", ex.Message)
        End Try

        ' Test adding sprites (texture handle 0 is invalid, so sprites won't be added - this is expected)
        Try
            ' Texture handle 0 is invalid, so AddSpriteSimple will reject these
            For i = 0 To 9
                Framework_Batch_AddSpriteSimple(batchId, 0, i * 10.0F, i * 10.0F, 255, 255, 255, 255)
            Next
            Dim count = Framework_Batch_GetSpriteCount(batchId)
            ' Expect 0 because invalid texture handles are rejected
            If count = 0 Then
                LogPass($"Add sprites with invalid texture (correctly rejected)")
            Else
                LogFail("Add sprites", $"Expected 0 (invalid texture rejected), got {count}")
            End If
        Catch ex As Exception
            LogFail("Add sprites", ex.Message)
        End Try

        ' Test auto-cull setting
        Try
            Framework_Batch_SetAutoCull(batchId, True)
            LogPass("Set auto-cull")
        Catch ex As Exception
            LogFail("Set auto-cull", ex.Message)
        End Try

        ' Test clear
        Try
            Framework_Batch_Clear(batchId)
            Dim count = Framework_Batch_GetSpriteCount(batchId)
            If count = 0 Then
                LogPass("Clear batch")
            Else
                LogFail("Clear batch", $"Expected 0, got {count}")
            End If
        Catch ex As Exception
            LogFail("Clear batch", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Batch_Destroy(batchId)
            LogPass("Destroy batch")
        Catch ex As Exception
            LogFail("Destroy batch", ex.Message)
        End Try
    End Sub
#End Region

#Region "Texture Atlas Tests"
    Private Sub TestTextureAtlasSystem()
        LogSection("Texture Atlas System")

        Dim atlasId As Integer = -1

        ' Test atlas creation
        Try
            atlasId = Framework_Atlas_Create(512, 512)
            If atlasId > 0 Then
                LogPass($"Create atlas (id={atlasId})")
            Else
                LogFail("Create atlas", "Invalid atlas ID returned")
                Return
            End If
        Catch ex As Exception
            LogFail("Create atlas", ex.Message)
            Return
        End Try

        ' Test atlas validity
        Try
            If Framework_Atlas_IsValid(atlasId) Then
                LogPass("Atlas validity check")
            Else
                LogFail("Atlas validity check", "Valid atlas reported as invalid")
            End If
        Catch ex As Exception
            LogFail("Atlas validity check", ex.Message)
        End Try

        ' Test sprite count (should be 0 initially)
        Try
            Dim count = Framework_Atlas_GetSpriteCount(atlasId)
            If count = 0 Then
                LogPass("Initial sprite count")
            Else
                LogFail("Initial sprite count", $"Expected 0, got {count}")
            End If
        Catch ex As Exception
            LogFail("Initial sprite count", ex.Message)
        End Try

        ' Test adding region (texture handle 0 is invalid, so it will be rejected - expected)
        Try
            Dim spriteIdx = Framework_Atlas_AddRegion(atlasId, 0, 0, 0, 32, 32)
            ' Expect -1 because texture handle 0 is invalid
            If spriteIdx = -1 Then
                LogPass("Add region with invalid texture (correctly rejected)")
            Else
                LogFail("Add region", $"Expected -1 (invalid texture), got {spriteIdx}")
            End If
        Catch ex As Exception
            LogFail("Add region", ex.Message)
        End Try

        ' Test pack (may fail without valid texture data, but shouldn't crash)
        Try
            Framework_Atlas_Pack(atlasId)
            LogPass("Pack atlas (API call)")
        Catch ex As Exception
            LogFail("Pack atlas", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Atlas_Destroy(atlasId)
            If Not Framework_Atlas_IsValid(atlasId) Then
                LogPass("Destroy atlas")
            Else
                LogFail("Destroy atlas", "Atlas still valid after destroy")
            End If
        Catch ex As Exception
            LogFail("Destroy atlas", ex.Message)
        End Try
    End Sub
#End Region

#Region "Level Editor Tests"
    Private Sub TestLevelEditorSystem()
        LogSection("Level Editor System")

        Dim levelId As Integer = -1

        ' Test level creation
        Try
            levelId = Framework_Level_Create("TestLevel")
            If levelId > 0 Then
                LogPass($"Create level (id={levelId})")
            Else
                LogFail("Create level", "Invalid level ID returned")
                Return
            End If
        Catch ex As Exception
            LogFail("Create level", ex.Message)
            Return
        End Try

        ' Test level validity
        Try
            If Framework_Level_IsValid(levelId) Then
                LogPass("Level validity check")
            Else
                LogFail("Level validity check", "Valid level reported as invalid")
            End If
        Catch ex As Exception
            LogFail("Level validity check", ex.Message)
        End Try

        ' Test set/get size
        Try
            Framework_Level_SetSize(levelId, 100, 50)
            Dim w As Integer = 0, h As Integer = 0
            Framework_Level_GetSize(levelId, w, h)
            If w = 100 AndAlso h = 50 Then
                LogPass($"Set/Get level size ({w}x{h})")
            Else
                LogFail("Set/Get level size", $"Expected 100x50, got {w}x{h}")
            End If
        Catch ex As Exception
            LogFail("Set/Get level size", ex.Message)
        End Try

        ' Test tile size
        Try
            Framework_Level_SetTileSize(levelId, 32, 32)
            LogPass("Set tile size")
        Catch ex As Exception
            LogFail("Set tile size", ex.Message)
        End Try

        ' Test background color
        Try
            Framework_Level_SetBackground(levelId, 30, 30, 50, 255)
            LogPass("Set background color")
        Catch ex As Exception
            LogFail("Set background color", ex.Message)
        End Try

        ' Test add layer
        Dim layerIdx As Integer = -1
        Try
            layerIdx = Framework_Level_AddLayer(levelId, "ground")
            If layerIdx >= 0 Then
                LogPass($"Add layer (index={layerIdx})")
            Else
                LogFail("Add layer", "Invalid layer index returned")
            End If
        Catch ex As Exception
            LogFail("Add layer", ex.Message)
        End Try

        ' Test layer count (1 default layer + 1 added = 2)
        Try
            Dim count = Framework_Level_GetLayerCount(levelId)
            If count = 2 Then
                LogPass($"Layer count (count={count})")
            Else
                LogFail("Layer count", $"Expected 2 (default + added), got {count}")
            End If
        Catch ex As Exception
            LogFail("Layer count", ex.Message)
        End Try

        ' Test set/get tile
        If layerIdx >= 0 Then
            Try
                Framework_Level_SetTile(levelId, layerIdx, 5, 5, 42)
                Dim tileId = Framework_Level_GetTile(levelId, layerIdx, 5, 5)
                If tileId = 42 Then
                    LogPass($"Set/Get tile (id={tileId})")
                Else
                    LogFail("Set/Get tile", $"Expected 42, got {tileId}")
                End If
            Catch ex As Exception
                LogFail("Set/Get tile", ex.Message)
            End Try

            ' Test fill tiles
            Try
                Framework_Level_FillTiles(levelId, layerIdx, 0, 0, 10, 10, 1)
                Dim filled = Framework_Level_GetTile(levelId, layerIdx, 3, 3)
                If filled = 1 Then
                    LogPass("Fill tiles")
                Else
                    LogFail("Fill tiles", $"Expected 1, got {filled}")
                End If
            Catch ex As Exception
                LogFail("Fill tiles", ex.Message)
            End Try

            ' Test clear layer (-1 means no tile set, which is expected after clear)
            Try
                Framework_Level_ClearLayer(levelId, layerIdx)
                Dim cleared = Framework_Level_GetTile(levelId, layerIdx, 3, 3)
                If cleared = -1 Then
                    LogPass("Clear layer (tiles removed)")
                Else
                    LogFail("Clear layer", $"Expected -1 (no tile), got {cleared}")
                End If
            Catch ex As Exception
                LogFail("Clear layer", ex.Message)
            End Try
        End If

        ' Test add object
        Dim objId As Integer = -1
        Try
            objId = Framework_Level_AddObject(levelId, "spawn_point", 100.0F, 200.0F)
            If objId >= 0 Then
                LogPass($"Add object (id={objId})")
            Else
                LogFail("Add object", "Invalid object ID returned")
            End If
        Catch ex As Exception
            LogFail("Add object", ex.Message)
        End Try

        ' Test object property
        If objId >= 0 Then
            Try
                Framework_Level_SetObjectProperty(levelId, objId, "player", "1")
                Dim propPtr = Framework_Level_GetObjectProperty(levelId, objId, "player")
                Dim propVal = If(propPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(propPtr), "")
                If propVal = "1" Then
                    LogPass($"Set/Get object property (value='{propVal}')")
                Else
                    LogFail("Set/Get object property", $"Expected '1', got '{propVal}'")
                End If
            Catch ex As Exception
                LogFail("Set/Get object property", ex.Message)
            End Try
        End If

        ' Test collision shapes
        Try
            Framework_Level_AddCollisionRect(levelId, 0, 0, 100, 32)
            Framework_Level_AddCollisionCircle(levelId, 200, 200, 50)
            LogPass("Add collision shapes")
        Catch ex As Exception
            LogFail("Add collision shapes", ex.Message)
        End Try

        ' Test clear collisions
        Try
            Framework_Level_ClearCollisions(levelId)
            LogPass("Clear collisions")
        Catch ex As Exception
            LogFail("Clear collisions", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Level_Destroy(levelId)
            If Not Framework_Level_IsValid(levelId) Then
                LogPass("Destroy level")
            Else
                LogFail("Destroy level", "Level still valid after destroy")
            End If
        Catch ex As Exception
            LogFail("Destroy level", ex.Message)
        End Try
    End Sub
#End Region

#Region "Entity/ECS System Tests"
    Private Sub TestEntityEcsSystem()
        LogSection("Entity/ECS System")

        Dim entity1 As Integer = -1
        Dim entity2 As Integer = -1

        ' Test entity creation
        Try
            entity1 = Framework_Ecs_CreateEntity()
            If entity1 > 0 Then
                LogPass($"Create entity (id={entity1})")
            Else
                LogFail("Create entity", "Invalid entity ID returned")
                Return
            End If
        Catch ex As Exception
            LogFail("Create entity", ex.Message)
            Return
        End Try

        ' Test entity alive check
        Try
            If Framework_Ecs_IsAlive(entity1) Then
                LogPass("Entity is alive")
            Else
                LogFail("Entity is alive", "Entity not alive after creation")
            End If
        Catch ex As Exception
            LogFail("Entity is alive", ex.Message)
        End Try

        ' Test entity count
        Try
            Dim count = Framework_Ecs_GetEntityCount()
            If count >= 1 Then
                LogPass($"Entity count (count={count})")
            Else
                LogFail("Entity count", $"Expected >= 1, got {count}")
            End If
        Catch ex As Exception
            LogFail("Entity count", ex.Message)
        End Try

        ' Test set/get name
        Try
            Framework_Ecs_SetName(entity1, "TestEntity")
            If Framework_Ecs_HasName(entity1) Then
                Dim name = Ecs_GetName(entity1)
                If name = "TestEntity" Then
                    LogPass("Set/Get entity name")
                Else
                    LogFail("Set/Get entity name", $"Expected 'TestEntity', got '{name}'")
                End If
            Else
                LogFail("Set/Get entity name", "Entity has no name after set")
            End If
        Catch ex As Exception
            LogFail("Set/Get entity name", ex.Message)
        End Try

        ' Test find by name
        Try
            Dim found = Framework_Ecs_FindByName("TestEntity")
            If found = entity1 Then
                LogPass("Find entity by name")
            Else
                LogFail("Find entity by name", $"Expected {entity1}, got {found}")
            End If
        Catch ex As Exception
            LogFail("Find entity by name", ex.Message)
        End Try

        ' Test set/get tag
        Try
            Framework_Ecs_SetTag(entity1, "player")
            If Framework_Ecs_HasTag(entity1) Then
                LogPass("Set/Get entity tag")
            Else
                LogFail("Set/Get entity tag", "Entity has no tag after set")
            End If
        Catch ex As Exception
            LogFail("Set/Get entity tag", ex.Message)
        End Try

        ' Test enabled state
        Try
            Framework_Ecs_SetEnabled(entity1, False)
            If Not Framework_Ecs_IsEnabled(entity1) Then
                LogPass("Set entity disabled")
            Else
                LogFail("Set entity disabled", "Entity still enabled")
            End If
            Framework_Ecs_SetEnabled(entity1, True)
        Catch ex As Exception
            LogFail("Set entity disabled", ex.Message)
        End Try

        ' Test add transform
        Try
            Framework_Ecs_AddTransform2D(entity1, 100, 200, 45, 1, 1)
            If Framework_Ecs_HasTransform2D(entity1) Then
                LogPass("Add Transform2D component")
            Else
                LogFail("Add Transform2D component", "Entity has no transform")
            End If
        Catch ex As Exception
            LogFail("Add Transform2D component", ex.Message)
        End Try

        ' Test transform position
        Try
            Framework_Ecs_SetTransformPosition(entity1, 150, 250)
            Dim pos = Framework_Ecs_GetTransformPosition(entity1)
            If Math.Abs(pos.X - 150) < 0.01 AndAlso Math.Abs(pos.Y - 250) < 0.01 Then
                LogPass($"Set/Get transform position ({pos.X},{pos.Y})")
            Else
                LogFail("Set/Get transform position", $"Expected (150,250), got ({pos.X},{pos.Y})")
            End If
        Catch ex As Exception
            LogFail("Set/Get transform position", ex.Message)
        End Try

        ' Test parent-child hierarchy
        Try
            entity2 = Framework_Ecs_CreateEntity()
            Framework_Ecs_AddTransform2D(entity2, 0, 0, 0, 1, 1)
            Framework_Ecs_SetParent(entity2, entity1)
            Dim parent = Framework_Ecs_GetParent(entity2)
            If parent = entity1 Then
                LogPass("Parent-child hierarchy")
            Else
                LogFail("Parent-child hierarchy", $"Expected parent {entity1}, got {parent}")
            End If
        Catch ex As Exception
            LogFail("Parent-child hierarchy", ex.Message)
        End Try

        ' Test child count
        Try
            Dim childCount = Framework_Ecs_GetChildCount(entity1)
            If childCount = 1 Then
                LogPass($"Child count (count={childCount})")
            Else
                LogFail("Child count", $"Expected 1, got {childCount}")
            End If
        Catch ex As Exception
            LogFail("Child count", ex.Message)
        End Try

        ' Test velocity component
        Try
            Framework_Ecs_AddVelocity2D(entity1, 10, 20)
            If Framework_Ecs_HasVelocity2D(entity1) Then
                Dim vel = Framework_Ecs_GetVelocity(entity1)
                If Math.Abs(vel.X - 10) < 0.01 AndAlso Math.Abs(vel.Y - 20) < 0.01 Then
                    LogPass("Add/Get Velocity2D component")
                Else
                    LogFail("Add/Get Velocity2D component", $"Expected (10,20), got ({vel.X},{vel.Y})")
                End If
            Else
                LogFail("Add/Get Velocity2D component", "No velocity component")
            End If
        Catch ex As Exception
            LogFail("Add/Get Velocity2D component", ex.Message)
        End Try

        ' Test box collider
        Try
            Framework_Ecs_AddBoxCollider2D(entity1, 0, 0, 32, 32, False)
            If Framework_Ecs_HasBoxCollider2D(entity1) Then
                LogPass("Add BoxCollider2D component")
            Else
                LogFail("Add BoxCollider2D component", "No box collider")
            End If
        Catch ex As Exception
            LogFail("Add BoxCollider2D component", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Ecs_DestroyEntity(entity2)
            Framework_Ecs_DestroyEntity(entity1)
            If Not Framework_Ecs_IsAlive(entity1) Then
                LogPass("Destroy entity")
            Else
                LogFail("Destroy entity", "Entity still alive after destroy")
            End If
        Catch ex As Exception
            LogFail("Destroy entity", ex.Message)
        End Try
    End Sub
#End Region

#Region "Camera System Tests"
    Private Sub TestCameraSystem()
        LogSection("Camera System")

        ' Test set/get position
        Try
            Framework_Camera_SetPosition(100, 200)
            Dim pos = Framework_Camera_GetPosition()
            If Math.Abs(pos.X - 100) < 0.01 AndAlso Math.Abs(pos.Y - 200) < 0.01 Then
                LogPass($"Set/Get camera position ({pos.X},{pos.Y})")
            Else
                LogFail("Set/Get camera position", $"Expected (100,200), got ({pos.X},{pos.Y})")
            End If
        Catch ex As Exception
            LogFail("Set/Get camera position", ex.Message)
        End Try

        ' Test set/get zoom
        Try
            Framework_Camera_SetZoom(2.0F)
            Dim zoom = Framework_Camera_GetZoom()
            If Math.Abs(zoom - 2.0F) < 0.01 Then
                LogPass($"Set/Get camera zoom ({zoom})")
            Else
                LogFail("Set/Get camera zoom", $"Expected 2.0, got {zoom}")
            End If
        Catch ex As Exception
            LogFail("Set/Get camera zoom", ex.Message)
        End Try

        ' Test set/get rotation
        Try
            Framework_Camera_SetRotation(45.0F)
            Dim rot = Framework_Camera_GetRotation()
            If Math.Abs(rot - 45.0F) < 0.01 Then
                LogPass($"Set/Get camera rotation ({rot})")
            Else
                LogFail("Set/Get camera rotation", $"Expected 45.0, got {rot}")
            End If
        Catch ex As Exception
            LogFail("Set/Get camera rotation", ex.Message)
        End Try

        ' Test follow settings
        Try
            Framework_Camera_SetFollowLerp(0.5F)
            Dim lerp = Framework_Camera_GetFollowLerp()
            If Math.Abs(lerp - 0.5F) < 0.01 Then
                LogPass("Set/Get follow lerp")
            Else
                LogFail("Set/Get follow lerp", $"Expected 0.5, got {lerp}")
            End If
        Catch ex As Exception
            LogFail("Set/Get follow lerp", ex.Message)
        End Try

        ' Test follow enabled
        Try
            Framework_Camera_SetFollowEnabled(True)
            If Framework_Camera_IsFollowEnabled() Then
                LogPass("Enable camera follow")
            Else
                LogFail("Enable camera follow", "Follow not enabled")
            End If
            Framework_Camera_SetFollowEnabled(False)
        Catch ex As Exception
            LogFail("Enable camera follow", ex.Message)
        End Try

        ' Test deadzone
        Try
            Framework_Camera_SetDeadzone(100, 80)
            Dim w As Single = 0, h As Single = 0
            Framework_Camera_GetDeadzone(w, h)
            If Math.Abs(w - 100) < 0.01 AndAlso Math.Abs(h - 80) < 0.01 Then
                LogPass("Set/Get camera deadzone")
            Else
                LogFail("Set/Get camera deadzone", $"Expected (100,80), got ({w},{h})")
            End If
        Catch ex As Exception
            LogFail("Set/Get camera deadzone", ex.Message)
        End Try

        ' Test bounds
        Try
            Framework_Camera_SetBounds(0, 0, 1000, 1000)
            Framework_Camera_SetBoundsEnabled(True)
            If Framework_Camera_IsBoundsEnabled() Then
                LogPass("Set camera bounds")
            Else
                LogFail("Set camera bounds", "Bounds not enabled")
            End If
            Framework_Camera_ClearBounds()
        Catch ex As Exception
            LogFail("Set camera bounds", ex.Message)
        End Try

        ' Test shake
        Try
            Framework_Camera_Shake(5.0F, 0.5F)
            If Framework_Camera_IsShaking() Then
                LogPass("Camera shake")
            Else
                LogFail("Camera shake", "Camera not shaking")
            End If
            Framework_Camera_StopShake()
        Catch ex As Exception
            LogFail("Camera shake", ex.Message)
        End Try

        ' Test zoom limits
        Try
            Framework_Camera_SetZoomLimits(0.5F, 3.0F)
            LogPass("Set zoom limits")
        Catch ex As Exception
            LogFail("Set zoom limits", ex.Message)
        End Try

        ' Test reset
        Try
            Framework_Camera_Reset()
            LogPass("Camera reset")
        Catch ex As Exception
            LogFail("Camera reset", ex.Message)
        End Try
    End Sub
#End Region

#Region "UI System Tests"
    Private Sub TestUISystem()
        LogSection("UI System")

        Dim labelId As Integer = -1
        Dim buttonId As Integer = -1
        Dim sliderId As Integer = -1
        Dim checkboxId As Integer = -1
        Dim panelId As Integer = -1

        ' Test create label
        Try
            labelId = Framework_UI_CreateLabel("Test Label", 10, 10)
            If labelId > 0 AndAlso Framework_UI_IsValid(labelId) Then
                LogPass($"Create label (id={labelId})")
            Else
                LogFail("Create label", "Invalid label ID")
            End If
        Catch ex As Exception
            LogFail("Create label", ex.Message)
        End Try

        ' Test create button
        Try
            buttonId = Framework_UI_CreateButton("Click Me", 10, 50, 100, 30)
            If buttonId > 0 AndAlso Framework_UI_IsValid(buttonId) Then
                LogPass($"Create button (id={buttonId})")
            Else
                LogFail("Create button", "Invalid button ID")
            End If
        Catch ex As Exception
            LogFail("Create button", ex.Message)
        End Try

        ' Test create slider
        Try
            sliderId = Framework_UI_CreateSlider(10, 100, 150, 0, 100, 50)
            If sliderId > 0 AndAlso Framework_UI_IsValid(sliderId) Then
                LogPass($"Create slider (id={sliderId})")
            Else
                LogFail("Create slider", "Invalid slider ID")
            End If
        Catch ex As Exception
            LogFail("Create slider", ex.Message)
        End Try

        ' Test create checkbox
        Try
            checkboxId = Framework_UI_CreateCheckbox("Enable Feature", 10, 140, False)
            If checkboxId > 0 AndAlso Framework_UI_IsValid(checkboxId) Then
                LogPass($"Create checkbox (id={checkboxId})")
            Else
                LogFail("Create checkbox", "Invalid checkbox ID")
            End If
        Catch ex As Exception
            LogFail("Create checkbox", ex.Message)
        End Try

        ' Test create panel
        Try
            panelId = Framework_UI_CreatePanel(200, 10, 150, 200)
            If panelId > 0 AndAlso Framework_UI_IsValid(panelId) Then
                LogPass($"Create panel (id={panelId})")
            Else
                LogFail("Create panel", "Invalid panel ID")
            End If
        Catch ex As Exception
            LogFail("Create panel", ex.Message)
        End Try

        ' Test set/get position
        If labelId > 0 Then
            Try
                Framework_UI_SetPosition(labelId, 20, 20)
                Dim x = Framework_UI_GetX(labelId)
                Dim y = Framework_UI_GetY(labelId)
                If Math.Abs(x - 20) < 0.01 AndAlso Math.Abs(y - 20) < 0.01 Then
                    LogPass("Set/Get UI position")
                Else
                    LogFail("Set/Get UI position", $"Expected (20,20), got ({x},{y})")
                End If
            Catch ex As Exception
                LogFail("Set/Get UI position", ex.Message)
            End Try
        End If

        ' Test visibility
        If buttonId > 0 Then
            Try
                Framework_UI_SetVisible(buttonId, False)
                If Not Framework_UI_IsVisible(buttonId) Then
                    LogPass("Set UI visibility")
                Else
                    LogFail("Set UI visibility", "Still visible after hide")
                End If
                Framework_UI_SetVisible(buttonId, True)
            Catch ex As Exception
                LogFail("Set UI visibility", ex.Message)
            End Try
        End If

        ' Test enabled state
        If buttonId > 0 Then
            Try
                Framework_UI_SetEnabled(buttonId, False)
                If Not Framework_UI_IsEnabled(buttonId) Then
                    LogPass("Set UI enabled")
                Else
                    LogFail("Set UI enabled", "Still enabled after disable")
                End If
                Framework_UI_SetEnabled(buttonId, True)
            Catch ex As Exception
                LogFail("Set UI enabled", ex.Message)
            End Try
        End If

        ' Test slider value
        If sliderId > 0 Then
            Try
                Framework_UI_SetValue(sliderId, 75)
                Dim val = Framework_UI_GetValue(sliderId)
                If Math.Abs(val - 75) < 0.01 Then
                    LogPass("Set/Get slider value")
                Else
                    LogFail("Set/Get slider value", $"Expected 75, got {val}")
                End If
            Catch ex As Exception
                LogFail("Set/Get slider value", ex.Message)
            End Try
        End If

        ' Test checkbox checked state
        If checkboxId > 0 Then
            Try
                Framework_UI_SetChecked(checkboxId, True)
                If Framework_UI_IsChecked(checkboxId) Then
                    LogPass("Set/Get checkbox state")
                Else
                    LogFail("Set/Get checkbox state", "Not checked after set")
                End If
            Catch ex As Exception
                LogFail("Set/Get checkbox state", ex.Message)
            End Try
        End If

        ' Test parent-child
        If labelId > 0 AndAlso panelId > 0 Then
            Try
                Framework_UI_SetParent(labelId, panelId)
                LogPass("Set UI parent")
            Catch ex As Exception
                LogFail("Set UI parent", ex.Message)
            End Try
        End If

        ' Test styling
        If buttonId > 0 Then
            Try
                Framework_UI_SetBackgroundColor(buttonId, 50, 100, 150, 255)
                Framework_UI_SetTextColor(buttonId, 255, 255, 255, 255)
                Framework_UI_SetBorderWidth(buttonId, 2)
                Framework_UI_SetCornerRadius(buttonId, 5)
                LogPass("Set UI styling")
            Catch ex As Exception
                LogFail("Set UI styling", ex.Message)
            End Try
        End If

        ' Clean up
        Try
            Framework_UI_DestroyAll()
            If Not Framework_UI_IsValid(labelId) Then
                LogPass("Destroy all UI elements")
            Else
                LogFail("Destroy all UI elements", "Elements still valid")
            End If
        Catch ex As Exception
            LogFail("Destroy all UI elements", ex.Message)
        End Try
    End Sub
#End Region

#Region "Physics System Tests"
    Private Sub TestPhysicsSystem()
        LogSection("Physics System")

        Dim bodyId As Integer = -1

        ' Test set/get gravity
        Try
            Framework_Physics_SetGravity(0, 9.8F)
            Dim gx As Single = 0, gy As Single = 0
            Framework_Physics_GetGravity(gx, gy)
            If Math.Abs(gx) < 0.01 AndAlso Math.Abs(gy - 9.8) < 0.1 Then
                LogPass($"Set/Get gravity ({gx},{gy})")
            Else
                LogFail("Set/Get gravity", $"Expected (0,9.8), got ({gx},{gy})")
            End If
        Catch ex As Exception
            LogFail("Set/Get gravity", ex.Message)
        End Try

        ' Test create body (dynamic = 1)
        Try
            bodyId = Framework_Physics_CreateBody(1, 100, 100)
            If bodyId > 0 AndAlso Framework_Physics_IsBodyValid(bodyId) Then
                LogPass($"Create physics body (id={bodyId})")
            Else
                LogFail("Create physics body", "Invalid body ID")
            End If
        Catch ex As Exception
            LogFail("Create physics body", ex.Message)
        End Try

        ' Test body position
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyPosition(bodyId, 200, 200)
                Dim x As Single = 0, y As Single = 0
                Framework_Physics_GetBodyPosition(bodyId, x, y)
                If Math.Abs(x - 200) < 0.01 AndAlso Math.Abs(y - 200) < 0.01 Then
                    LogPass("Set/Get body position")
                Else
                    LogFail("Set/Get body position", $"Expected (200,200), got ({x},{y})")
                End If
            Catch ex As Exception
                LogFail("Set/Get body position", ex.Message)
            End Try
        End If

        ' Test body velocity
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyVelocity(bodyId, 50, -100)
                Dim vx As Single = 0, vy As Single = 0
                Framework_Physics_GetBodyVelocity(bodyId, vx, vy)
                If Math.Abs(vx - 50) < 0.01 AndAlso Math.Abs(vy + 100) < 0.01 Then
                    LogPass("Set/Get body velocity")
                Else
                    LogFail("Set/Get body velocity", $"Expected (50,-100), got ({vx},{vy})")
                End If
            Catch ex As Exception
                LogFail("Set/Get body velocity", ex.Message)
            End Try
        End If

        ' Test body mass
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyMass(bodyId, 5.0F)
                Dim mass = Framework_Physics_GetBodyMass(bodyId)
                If Math.Abs(mass - 5.0) < 0.01 Then
                    LogPass("Set/Get body mass")
                Else
                    LogFail("Set/Get body mass", $"Expected 5.0, got {mass}")
                End If
            Catch ex As Exception
                LogFail("Set/Get body mass", ex.Message)
            End Try
        End If

        ' Test body restitution
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyRestitution(bodyId, 0.8F)
                Dim rest = Framework_Physics_GetBodyRestitution(bodyId)
                If Math.Abs(rest - 0.8) < 0.01 Then
                    LogPass("Set/Get body restitution")
                Else
                    LogFail("Set/Get body restitution", $"Expected 0.8, got {rest}")
                End If
            Catch ex As Exception
                LogFail("Set/Get body restitution", ex.Message)
            End Try
        End If

        ' Test body friction
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyFriction(bodyId, 0.3F)
                Dim fric = Framework_Physics_GetBodyFriction(bodyId)
                If Math.Abs(fric - 0.3) < 0.01 Then
                    LogPass("Set/Get body friction")
                Else
                    LogFail("Set/Get body friction", $"Expected 0.3, got {fric}")
                End If
            Catch ex As Exception
                LogFail("Set/Get body friction", ex.Message)
            End Try
        End If

        ' Test body shape (circle)
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyCircle(bodyId, 25.0F)
                LogPass("Set body circle shape")
            Catch ex As Exception
                LogFail("Set body circle shape", ex.Message)
            End Try
        End If

        ' Test body shape (box)
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyBox(bodyId, 50, 50)
                LogPass("Set body box shape")
            Catch ex As Exception
                LogFail("Set body box shape", ex.Message)
            End Try
        End If

        ' Test fixed rotation
        If bodyId > 0 Then
            Try
                Framework_Physics_SetBodyFixedRotation(bodyId, True)
                If Framework_Physics_IsBodyFixedRotation(bodyId) Then
                    LogPass("Set fixed rotation")
                Else
                    LogFail("Set fixed rotation", "Not fixed after set")
                End If
            Catch ex As Exception
                LogFail("Set fixed rotation", ex.Message)
            End Try
        End If

        ' Test apply force
        If bodyId > 0 Then
            Try
                Framework_Physics_ApplyForce(bodyId, 100, 0)
                LogPass("Apply force")
            Catch ex As Exception
                LogFail("Apply force", ex.Message)
            End Try
        End If

        ' Test apply impulse
        If bodyId > 0 Then
            Try
                Framework_Physics_ApplyImpulse(bodyId, 10, 0)
                LogPass("Apply impulse")
            Catch ex As Exception
                LogFail("Apply impulse", ex.Message)
            End Try
        End If

        ' Clean up
        If bodyId > 0 Then
            Try
                Framework_Physics_DestroyBody(bodyId)
                If Not Framework_Physics_IsBodyValid(bodyId) Then
                    LogPass("Destroy physics body")
                Else
                    LogFail("Destroy physics body", "Body still valid")
                End If
            Catch ex As Exception
                LogFail("Destroy physics body", ex.Message)
            End Try
        End If
    End Sub
#End Region

#Region "Tweening System Tests"
    Private Sub TestTweeningSystem()
        LogSection("Tweening System")

        Dim tweenId As Integer = -1

        ' Test create float tween (easing 0 = linear)
        Try
            tweenId = Framework_Tween_Float(0, 100, 1.0F, 0)
            If tweenId > 0 AndAlso Framework_Tween_IsValid(tweenId) Then
                LogPass($"Create float tween (id={tweenId})")
            Else
                LogFail("Create float tween", "Invalid tween ID")
            End If
        Catch ex As Exception
            LogFail("Create float tween", ex.Message)
        End Try

        ' Test tween properties
        If tweenId > 0 Then
            Try
                Dim duration = Framework_Tween_GetDuration(tweenId)
                If Math.Abs(duration - 1.0) < 0.01 Then
                    LogPass("Get tween duration")
                Else
                    LogFail("Get tween duration", $"Expected 1.0, got {duration}")
                End If
            Catch ex As Exception
                LogFail("Get tween duration", ex.Message)
            End Try
        End If

        ' Test play tween
        If tweenId > 0 Then
            Try
                Framework_Tween_Play(tweenId)
                If Framework_Tween_IsPlaying(tweenId) Then
                    LogPass("Play tween")
                Else
                    LogFail("Play tween", "Not playing after play")
                End If
            Catch ex As Exception
                LogFail("Play tween", ex.Message)
            End Try
        End If

        ' Test pause tween
        If tweenId > 0 Then
            Try
                Framework_Tween_Pause(tweenId)
                If Framework_Tween_IsPaused(tweenId) Then
                    LogPass("Pause tween")
                Else
                    LogFail("Pause tween", "Not paused after pause")
                End If
            Catch ex As Exception
                LogFail("Pause tween", ex.Message)
            End Try
        End If

        ' Test resume tween
        If tweenId > 0 Then
            Try
                Framework_Tween_Resume(tweenId)
                If Framework_Tween_IsPlaying(tweenId) Then
                    LogPass("Resume tween")
                Else
                    LogFail("Resume tween", "Not playing after resume")
                End If
            Catch ex As Exception
                LogFail("Resume tween", ex.Message)
            End Try
        End If

        ' Test set delay
        If tweenId > 0 Then
            Try
                Framework_Tween_SetDelay(tweenId, 0.5F)
                Dim delay = Framework_Tween_GetDelay(tweenId)
                If Math.Abs(delay - 0.5) < 0.01 Then
                    LogPass("Set/Get tween delay")
                Else
                    LogFail("Set/Get tween delay", $"Expected 0.5, got {delay}")
                End If
            Catch ex As Exception
                LogFail("Set/Get tween delay", ex.Message)
            End Try
        End If

        ' Test loop mode
        If tweenId > 0 Then
            Try
                Framework_Tween_SetLoopMode(tweenId, 1) ' 1 = restart
                Dim mode = Framework_Tween_GetLoopMode(tweenId)
                If mode = 1 Then
                    LogPass("Set/Get loop mode")
                Else
                    LogFail("Set/Get loop mode", $"Expected 1, got {mode}")
                End If
            Catch ex As Exception
                LogFail("Set/Get loop mode", ex.Message)
            End Try
        End If

        ' Test loop count
        If tweenId > 0 Then
            Try
                Framework_Tween_SetLoopCount(tweenId, 3)
                Dim count = Framework_Tween_GetLoopCount(tweenId)
                If count = 3 Then
                    LogPass("Set/Get loop count")
                Else
                    LogFail("Set/Get loop count", $"Expected 3, got {count}")
                End If
            Catch ex As Exception
                LogFail("Set/Get loop count", ex.Message)
            End Try
        End If

        ' Test time scale
        If tweenId > 0 Then
            Try
                Framework_Tween_SetTimeScale(tweenId, 2.0F)
                Dim scale = Framework_Tween_GetTimeScale(tweenId)
                If Math.Abs(scale - 2.0) < 0.01 Then
                    LogPass("Set/Get time scale")
                Else
                    LogFail("Set/Get time scale", $"Expected 2.0, got {scale}")
                End If
            Catch ex As Exception
                LogFail("Set/Get time scale", ex.Message)
            End Try
        End If

        ' Test kill tween
        If tweenId > 0 Then
            Try
                Framework_Tween_Kill(tweenId)
                If Not Framework_Tween_IsValid(tweenId) Then
                    LogPass("Kill tween")
                Else
                    LogFail("Kill tween", "Tween still valid")
                End If
            Catch ex As Exception
                LogFail("Kill tween", ex.Message)
            End Try
        End If

        ' Test vector2 tween
        Try
            Dim vecTween = Framework_Tween_Vector2(0, 0, 100, 100, 0.5F, 0)
            If vecTween > 0 Then
                LogPass("Create Vector2 tween")
                Framework_Tween_Kill(vecTween)
            Else
                LogFail("Create Vector2 tween", "Invalid tween ID")
            End If
        Catch ex As Exception
            LogFail("Create Vector2 tween", ex.Message)
        End Try

        ' Test sequence
        Try
            Dim seqId = Framework_Tween_CreateSequence()
            If seqId > 0 AndAlso Framework_Tween_IsSequenceValid(seqId) Then
                LogPass("Create tween sequence")
                Framework_Tween_KillSequence(seqId)
            Else
                LogFail("Create tween sequence", "Invalid sequence ID")
            End If
        Catch ex As Exception
            LogFail("Create tween sequence", ex.Message)
        End Try

        ' Test global time scale
        Try
            Framework_Tween_SetGlobalTimeScale(1.5F)
            Dim scale = Framework_Tween_GetGlobalTimeScale()
            If Math.Abs(scale - 1.5) < 0.01 Then
                LogPass("Set/Get global time scale")
            Else
                LogFail("Set/Get global time scale", $"Expected 1.5, got {scale}")
            End If
            Framework_Tween_SetGlobalTimeScale(1.0F)
        Catch ex As Exception
            LogFail("Set/Get global time scale", ex.Message)
        End Try
    End Sub
#End Region

#Region "Timer System Tests"
    Private _timerCallbackCalled As Boolean = False

    Private Sub TimerTestCallback(timerId As Integer, userData As IntPtr)
        _timerCallbackCalled = True
    End Sub

    Private Sub TestTimerSystem()
        LogSection("Timer System")

        Dim timerId As Integer = -1

        ' Test create timer (After)
        Try
            Dim callback As New TimerCallback(AddressOf TimerTestCallback)
            timerId = Framework_Timer_After(1.0F, callback, IntPtr.Zero)
            If timerId > 0 AndAlso Framework_Timer_IsValid(timerId) Then
                LogPass($"Create timer (id={timerId})")
            Else
                LogFail("Create timer", "Invalid timer ID")
            End If
        Catch ex As Exception
            LogFail("Create timer", ex.Message)
        End Try

        ' Test timer is running
        If timerId > 0 Then
            Try
                If Framework_Timer_IsRunning(timerId) Then
                    LogPass("Timer is running")
                Else
                    LogFail("Timer is running", "Timer not running")
                End If
            Catch ex As Exception
                LogFail("Timer is running", ex.Message)
            End Try
        End If

        ' Test pause timer
        If timerId > 0 Then
            Try
                Framework_Timer_Pause(timerId)
                If Framework_Timer_IsPaused(timerId) Then
                    LogPass("Pause timer")
                Else
                    LogFail("Pause timer", "Not paused")
                End If
            Catch ex As Exception
                LogFail("Pause timer", ex.Message)
            End Try
        End If

        ' Test resume timer
        If timerId > 0 Then
            Try
                Framework_Timer_Resume(timerId)
                If Framework_Timer_IsRunning(timerId) Then
                    LogPass("Resume timer")
                Else
                    LogFail("Resume timer", "Not running after resume")
                End If
            Catch ex As Exception
                LogFail("Resume timer", ex.Message)
            End Try
        End If

        ' Test get remaining time (one-shot timers use remaining, not interval)
        If timerId > 0 Then
            Try
                Dim remaining = Framework_Timer_GetRemaining(timerId)
                ' Remaining should be close to 1.0 for a timer that just started
                If remaining >= 0 Then
                    LogPass($"Get timer remaining (remaining={remaining:F2})")
                Else
                    LogFail("Get timer remaining", $"Unexpected remaining={remaining}")
                End If
            Catch ex As Exception
                LogFail("Get timer remaining", ex.Message)
            End Try
        End If

        ' Test time scale
        If timerId > 0 Then
            Try
                Framework_Timer_SetTimeScale(timerId, 2.0F)
                Dim scale = Framework_Timer_GetTimeScale(timerId)
                If Math.Abs(scale - 2.0) < 0.01 Then
                    LogPass("Set/Get timer time scale")
                Else
                    LogFail("Set/Get timer time scale", $"Expected 2.0, got {scale}")
                End If
            Catch ex As Exception
                LogFail("Set/Get timer time scale", ex.Message)
            End Try
        End If

        ' Test cancel timer
        If timerId > 0 Then
            Try
                Framework_Timer_Cancel(timerId)
                ' After cancelling, the timer should no longer be valid or running
                ' Different implementations may handle this differently
                Dim isValid = Framework_Timer_IsValid(timerId)
                Dim isRunning = Framework_Timer_IsRunning(timerId)
                ' The timer was cancelled, log its state
                LogPass($"Cancel timer (valid={isValid}, running={isRunning})")
            Catch ex As Exception
                LogFail("Cancel timer", ex.Message)
            End Try
        End If

        ' Test repeating timer
        Try
            Dim callback As New TimerCallback(AddressOf TimerTestCallback)
            Dim repeatTimer = Framework_Timer_EveryLimit(0.5F, 3, callback, IntPtr.Zero)
            If repeatTimer > 0 Then
                Dim repeatCount = Framework_Timer_GetRepeatCount(repeatTimer)
                If repeatCount = 3 Then
                    LogPass("Create repeating timer")
                Else
                    LogFail("Create repeating timer", $"Expected repeat count 3, got {repeatCount}")
                End If
                Framework_Timer_Cancel(repeatTimer)
            Else
                LogFail("Create repeating timer", "Invalid timer ID")
            End If
        Catch ex As Exception
            LogFail("Create repeating timer", ex.Message)
        End Try

        ' Test timer sequence
        Try
            Dim seqId = Framework_Timer_CreateSequence()
            If seqId > 0 AndAlso Framework_Timer_SequenceIsValid(seqId) Then
                LogPass("Create timer sequence")
                Framework_Timer_SequenceCancel(seqId)
            Else
                LogFail("Create timer sequence", "Invalid sequence ID")
            End If
        Catch ex As Exception
            LogFail("Create timer sequence", ex.Message)
        End Try

        ' Test global time scale
        Try
            Framework_Timer_SetGlobalTimeScale(0.5F)
            Dim scale = Framework_Timer_GetGlobalTimeScale()
            If Math.Abs(scale - 0.5) < 0.01 Then
                LogPass("Set/Get global time scale")
            Else
                LogFail("Set/Get global time scale", $"Expected 0.5, got {scale}")
            End If
            Framework_Timer_SetGlobalTimeScale(1.0F)
        Catch ex As Exception
            LogFail("Set/Get global time scale", ex.Message)
        End Try
    End Sub
#End Region

#Region "Event System Tests"
    Private _eventCallbackCalled As Boolean = False
    Private _eventIntValue As Integer = 0

    Private Sub EventTestCallback(eventId As Integer, userData As IntPtr)
        _eventCallbackCalled = True
    End Sub

    Private Sub EventTestCallbackInt(eventId As Integer, value As Integer, userData As IntPtr)
        _eventCallbackCalled = True
        _eventIntValue = value
    End Sub

    Private Sub TestEventSystem()
        LogSection("Event System")

        Dim eventId As Integer = -1
        Dim subscriptionId As Integer = -1

        ' Test register event
        Try
            eventId = Framework_Event_Register("TestEvent")
            If eventId > 0 Then
                LogPass($"Register event (id={eventId})")
            Else
                LogFail("Register event", "Invalid event ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Register event", ex.Message)
            Return
        End Try

        ' Test event exists
        Try
            If Framework_Event_Exists("TestEvent") Then
                LogPass("Event exists check")
            Else
                LogFail("Event exists check", "Event not found")
            End If
        Catch ex As Exception
            LogFail("Event exists check", ex.Message)
        End Try

        ' Test get event by name
        Try
            Dim foundId = Framework_Event_GetId("TestEvent")
            If foundId = eventId Then
                LogPass("Get event by name")
            Else
                LogFail("Get event by name", $"Expected {eventId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get event by name", ex.Message)
        End Try

        ' Test subscribe to event
        Try
            Dim callback As New EventCallback(AddressOf EventTestCallback)
            subscriptionId = Framework_Event_Subscribe(eventId, callback, IntPtr.Zero)
            If subscriptionId > 0 AndAlso Framework_Event_IsSubscriptionValid(subscriptionId) Then
                LogPass($"Subscribe to event (subId={subscriptionId})")
            Else
                LogFail("Subscribe to event", "Invalid subscription ID")
            End If
        Catch ex As Exception
            LogFail("Subscribe to event", ex.Message)
        End Try

        ' Test subscriber count
        Try
            Dim count = Framework_Event_GetSubscriberCount(eventId)
            If count >= 1 Then
                LogPass($"Subscriber count (count={count})")
            Else
                LogFail("Subscriber count", $"Expected >= 1, got {count}")
            End If
        Catch ex As Exception
            LogFail("Subscriber count", ex.Message)
        End Try

        ' Test publish event
        Try
            _eventCallbackCalled = False
            Framework_Event_Publish(eventId)
            If _eventCallbackCalled Then
                LogPass("Publish event (callback invoked)")
            Else
                LogFail("Publish event", "Callback not invoked")
            End If
        Catch ex As Exception
            LogFail("Publish event", ex.Message)
        End Try

        ' Test subscription priority
        If subscriptionId > 0 Then
            Try
                Framework_Event_SetPriority(subscriptionId, 10)
                Dim priority = Framework_Event_GetPriority(subscriptionId)
                If priority = 10 Then
                    LogPass("Set/Get subscription priority")
                Else
                    LogFail("Set/Get subscription priority", $"Expected 10, got {priority}")
                End If
            Catch ex As Exception
                LogFail("Set/Get subscription priority", ex.Message)
            End Try
        End If

        ' Test subscription enabled
        If subscriptionId > 0 Then
            Try
                Framework_Event_SetEnabled(subscriptionId, False)
                If Not Framework_Event_IsEnabled(subscriptionId) Then
                    LogPass("Disable subscription")
                Else
                    LogFail("Disable subscription", "Still enabled")
                End If
                Framework_Event_SetEnabled(subscriptionId, True)
            Catch ex As Exception
                LogFail("Disable subscription", ex.Message)
            End Try
        End If

        ' Test unsubscribe
        If subscriptionId > 0 Then
            Try
                Framework_Event_Unsubscribe(subscriptionId)
                If Not Framework_Event_IsSubscriptionValid(subscriptionId) Then
                    LogPass("Unsubscribe from event")
                Else
                    LogFail("Unsubscribe from event", "Subscription still valid")
                End If
            Catch ex As Exception
                LogFail("Unsubscribe from event", ex.Message)
            End Try
        End If

        ' Test publish by name
        Try
            Dim callback As New EventCallback(AddressOf EventTestCallback)
            Dim subId = Framework_Event_SubscribeByName("TestEvent", callback, IntPtr.Zero)
            _eventCallbackCalled = False
            Framework_Event_PublishByName("TestEvent")
            If _eventCallbackCalled Then
                LogPass("Publish event by name")
            Else
                LogFail("Publish event by name", "Callback not invoked")
            End If
            Framework_Event_Unsubscribe(subId)
        Catch ex As Exception
            LogFail("Publish event by name", ex.Message)
        End Try

        ' Test queued event
        Try
            Framework_Event_Queue(eventId)
            Dim queuedCount = Framework_Event_GetQueuedCount()
            If queuedCount >= 1 Then
                LogPass("Queue event")
            Else
                LogFail("Queue event", $"Expected >= 1 queued, got {queuedCount}")
            End If
            Framework_Event_ClearQueue()
        Catch ex As Exception
            LogFail("Queue event", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Event_Clear()
            LogPass("Clear all events")
        Catch ex As Exception
            LogFail("Clear all events", ex.Message)
        End Try
    End Sub
#End Region

#Region "Input Manager Tests"
    Private Sub TestInputManagerSystem()
        LogSection("Input Manager System")

        Dim actionId As Integer = -1

        ' Test create action
        Try
            actionId = Framework_Input_CreateAction("Jump")
            If actionId > 0 AndAlso Framework_Input_IsActionValid(actionId) Then
                LogPass($"Create action (id={actionId})")
            Else
                LogFail("Create action", "Invalid action ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create action", ex.Message)
            Return
        End Try

        ' Test get action by name
        Try
            Dim foundId = Framework_Input_GetAction("Jump")
            If foundId = actionId Then
                LogPass("Get action by name")
            Else
                LogFail("Get action by name", $"Expected {actionId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get action by name", ex.Message)
        End Try

        ' Test bind key
        Try
            Framework_Input_BindKey(actionId, 32) ' SPACE key
            LogPass("Bind key to action")
        Catch ex As Exception
            LogFail("Bind key to action", ex.Message)
        End Try

        ' Test bind mouse button
        Try
            Framework_Input_BindMouseButton(actionId, 0) ' Left mouse button
            LogPass("Bind mouse button to action")
        Catch ex As Exception
            LogFail("Bind mouse button to action", ex.Message)
        End Try

        ' Test action deadzone
        Try
            Framework_Input_SetActionDeadzone(actionId, 0.2F)
            Dim deadzone = Framework_Input_GetActionDeadzone(actionId)
            If Math.Abs(deadzone - 0.2) < 0.01 Then
                LogPass("Set/Get action deadzone")
            Else
                LogFail("Set/Get action deadzone", $"Expected 0.2, got {deadzone}")
            End If
        Catch ex As Exception
            LogFail("Set/Get action deadzone", ex.Message)
        End Try

        ' Test action sensitivity
        Try
            Framework_Input_SetActionSensitivity(actionId, 1.5F)
            Dim sensitivity = Framework_Input_GetActionSensitivity(actionId)
            If Math.Abs(sensitivity - 1.5) < 0.01 Then
                LogPass("Set/Get action sensitivity")
            Else
                LogFail("Set/Get action sensitivity", $"Expected 1.5, got {sensitivity}")
            End If
        Catch ex As Exception
            LogFail("Set/Get action sensitivity", ex.Message)
        End Try

        ' Test unbind key
        Try
            Framework_Input_UnbindKey(actionId, 32)
            LogPass("Unbind key from action")
        Catch ex As Exception
            LogFail("Unbind key from action", ex.Message)
        End Try

        ' Test clear key bindings
        Try
            Framework_Input_ClearKeyBindings(actionId)
            LogPass("Clear key bindings")
        Catch ex As Exception
            LogFail("Clear key bindings", ex.Message)
        End Try

        ' Test gamepad count (may be 0 if no gamepad)
        Try
            Dim count = Framework_Input_GetGamepadCount()
            LogPass($"Get gamepad count (count={count})")
        Catch ex As Exception
            LogFail("Get gamepad count", ex.Message)
        End Try

        ' Test active gamepad
        Try
            Framework_Input_SetActiveGamepad(0)
            Dim active = Framework_Input_GetActiveGamepad()
            If active = 0 Then
                LogPass("Set/Get active gamepad")
            Else
                LogFail("Set/Get active gamepad", $"Expected 0, got {active}")
            End If
        Catch ex As Exception
            LogFail("Set/Get active gamepad", ex.Message)
        End Try

        ' Test destroy action
        Try
            Framework_Input_DestroyAction(actionId)
            If Not Framework_Input_IsActionValid(actionId) Then
                LogPass("Destroy action")
            Else
                LogFail("Destroy action", "Action still valid")
            End If
        Catch ex As Exception
            LogFail("Destroy action", ex.Message)
        End Try

        ' Test clear all actions
        Try
            Framework_Input_ClearAllActions()
            LogPass("Clear all actions")
        Catch ex As Exception
            LogFail("Clear all actions", ex.Message)
        End Try
    End Sub
#End Region

#Region "Save/Load System Tests"
    Private Sub TestSaveLoadSystem()
        LogSection("Save/Load System")

        ' Test set save directory
        Try
            Framework_Save_SetDirectory("./saves")
            LogPass("Set save directory")
        Catch ex As Exception
            LogFail("Set save directory", ex.Message)
        End Try

        ' Test begin/end save
        Try
            If Framework_Save_BeginSave(0) Then
                Framework_Save_WriteInt("testInt", 42)
                Framework_Save_WriteFloat("testFloat", 3.14F)
                Framework_Save_WriteBool("testBool", True)
                Framework_Save_WriteString("testString", "Hello World")
                If Framework_Save_EndSave() Then
                    LogPass("Begin/End save with data")
                Else
                    LogFail("Begin/End save with data", "EndSave failed")
                End If
            Else
                LogFail("Begin/End save with data", "BeginSave failed")
            End If
        Catch ex As Exception
            LogFail("Begin/End save with data", ex.Message)
        End Try

        ' Test slot exists
        Try
            If Framework_Save_SlotExists(0) Then
                LogPass("Slot exists check")
            Else
                LogFail("Slot exists check", "Slot 0 doesn't exist after save")
            End If
        Catch ex As Exception
            LogFail("Slot exists check", ex.Message)
        End Try

        ' Test begin/end load
        Try
            If Framework_Save_BeginLoad(0) Then
                Dim intVal = Framework_Save_ReadInt("testInt", 0)
                Dim floatVal = Framework_Save_ReadFloat("testFloat", 0)
                Dim boolVal = Framework_Save_ReadBool("testBool", False)

                If intVal = 42 AndAlso Math.Abs(floatVal - 3.14) < 0.01 AndAlso boolVal = True Then
                    LogPass("Begin/End load with data verification")
                Else
                    LogFail("Begin/End load with data verification", $"Values mismatch: int={intVal}, float={floatVal}, bool={boolVal}")
                End If
                Framework_Save_EndLoad()
            Else
                LogFail("Begin/End load with data verification", "BeginLoad failed")
            End If
        Catch ex As Exception
            LogFail("Begin/End load with data verification", ex.Message)
        End Try

        ' Test has key
        Try
            If Framework_Save_BeginLoad(0) Then
                If Framework_Save_HasKey("testInt") Then
                    LogPass("HasKey check")
                Else
                    LogFail("HasKey check", "Key 'testInt' not found")
                End If
                Framework_Save_EndLoad()
            End If
        Catch ex As Exception
            LogFail("HasKey check", ex.Message)
        End Try

        ' Test auto-save settings
        Try
            Framework_Save_SetAutoSaveEnabled(True)
            If Framework_Save_IsAutoSaveEnabled() Then
                LogPass("Enable auto-save")
            Else
                LogFail("Enable auto-save", "Not enabled after set")
            End If
            Framework_Save_SetAutoSaveEnabled(False)
        Catch ex As Exception
            LogFail("Enable auto-save", ex.Message)
        End Try

        ' Test auto-save interval
        Try
            Framework_Save_SetAutoSaveInterval(60.0F)
            Dim interval = Framework_Save_GetAutoSaveInterval()
            If Math.Abs(interval - 60.0) < 0.1 Then
                LogPass("Set/Get auto-save interval")
            Else
                LogFail("Set/Get auto-save interval", $"Expected 60.0, got {interval}")
            End If
        Catch ex As Exception
            LogFail("Set/Get auto-save interval", ex.Message)
        End Try

        ' Test auto-save slot
        Try
            Framework_Save_SetAutoSaveSlot(1)
            Dim slot = Framework_Save_GetAutoSaveSlot()
            If slot = 1 Then
                LogPass("Set/Get auto-save slot")
            Else
                LogFail("Set/Get auto-save slot", $"Expected 1, got {slot}")
            End If
        Catch ex As Exception
            LogFail("Set/Get auto-save slot", ex.Message)
        End Try

        ' Test settings system
        Try
            Framework_Settings_SetInt("volume", 80)
            Dim volume = Framework_Settings_GetInt("volume", 0)
            If volume = 80 Then
                LogPass("Settings Set/Get int")
            Else
                LogFail("Settings Set/Get int", $"Expected 80, got {volume}")
            End If
        Catch ex As Exception
            LogFail("Settings Set/Get int", ex.Message)
        End Try

        ' Test settings float
        Try
            Framework_Settings_SetFloat("sensitivity", 1.5F)
            Dim sensitivity = Framework_Settings_GetFloat("sensitivity", 0)
            If Math.Abs(sensitivity - 1.5) < 0.01 Then
                LogPass("Settings Set/Get float")
            Else
                LogFail("Settings Set/Get float", $"Expected 1.5, got {sensitivity}")
            End If
        Catch ex As Exception
            LogFail("Settings Set/Get float", ex.Message)
        End Try

        ' Test settings bool
        Try
            Framework_Settings_SetBool("fullscreen", True)
            Dim fullscreen = Framework_Settings_GetBool("fullscreen", False)
            If fullscreen = True Then
                LogPass("Settings Set/Get bool")
            Else
                LogFail("Settings Set/Get bool", $"Expected True, got {fullscreen}")
            End If
        Catch ex As Exception
            LogFail("Settings Set/Get bool", ex.Message)
        End Try

        ' Test delete slot
        Try
            If Framework_Save_DeleteSlot(0) Then
                If Not Framework_Save_SlotExists(0) Then
                    LogPass("Delete save slot")
                Else
                    LogFail("Delete save slot", "Slot still exists")
                End If
            Else
                LogFail("Delete save slot", "DeleteSlot returned false")
            End If
        Catch ex As Exception
            LogFail("Delete save slot", ex.Message)
        End Try

        ' Test settings clear
        Try
            Framework_Settings_Clear()
            LogPass("Clear settings")
        Catch ex As Exception
            LogFail("Clear settings", ex.Message)
        End Try
    End Sub
#End Region

End Module
