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

End Module
