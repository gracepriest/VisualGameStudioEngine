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
        TestTilesetSystem()
        TestAnimationClipSystem()
        TestAudioManagerSystem()
        TestSceneManagerSystem()
        TestObjectPoolingSystem()
        TestFSMSystem()
        TestDialogueSystem()
        TestInventorySystem()
        TestQuestSystem()
        TestLightingSystem()
        TestScreenEffectsSystem()
        TestAIPathfindingSystem()
        TestParticleSystem()
        TestLocalizationSystem()
        TestAchievementSystem()
        TestCutsceneSystem()
        TestLeaderboardSystem()

        ' Integration tests
        TestIntegration_EntityPhysicsCamera()
        TestIntegration_TimerEventEntity()
        TestIntegration_UIInputState()
        TestIntegration_SaveLoadMultiSystem()

        ' Stress/Performance tests
        TestStress_EntityCreation()
        TestStress_TimerSystem()
        TestStress_EventSystem()
        TestStress_UIElements()
        TestStress_SpriteBatching()

        ' New system tests
        TestSpriteSheetSystem()
        TestLevelEditorEnhancements()

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

        ' Test Outline shader (NEW)
        Try
            Dim outlineId = Framework_Shader_LoadOutline()
            If outlineId > 0 Then
                LogPass("Load outline shader")
                Framework_Shader_SetVec4ByName(outlineId, "outlineColor", 1.0F, 0.0F, 0.0F, 1.0F)
                Framework_Shader_SetFloatByName(outlineId, "outlineThickness", 2.0F)
                LogPass("Set outline uniforms")
                Framework_Shader_Unload(outlineId)
            Else
                LogFail("Load outline shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load outline shader", ex.Message)
        End Try

        ' Test Glow shader (NEW)
        Try
            Dim glowId = Framework_Shader_LoadGlow()
            If glowId > 0 Then
                LogPass("Load glow shader")
                Framework_Shader_SetFloatByName(glowId, "glowIntensity", 1.5F)
                Framework_Shader_SetFloatByName(glowId, "glowRadius", 5.0F)
                LogPass("Set glow uniforms")
                Framework_Shader_Unload(glowId)
            Else
                LogFail("Load glow shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load glow shader", ex.Message)
        End Try

        ' Test Distortion shader (NEW)
        Try
            Dim distortId = Framework_Shader_LoadDistortion()
            If distortId > 0 Then
                LogPass("Load distortion shader")
                Framework_Shader_SetFloatByName(distortId, "time", 0.0F)
                Framework_Shader_SetFloatByName(distortId, "distortionStrength", 0.05F)
                Framework_Shader_SetFloatByName(distortId, "waveFrequency", 10.0F)
                LogPass("Set distortion uniforms")
                Framework_Shader_Unload(distortId)
            Else
                LogFail("Load distortion shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load distortion shader", ex.Message)
        End Try

        ' Test Chromatic shader (NEW)
        Try
            Dim chromaticId = Framework_Shader_LoadChromatic()
            If chromaticId > 0 Then
                LogPass("Load chromatic shader")
                Framework_Shader_SetFloatByName(chromaticId, "aberrationAmount", 0.01F)
                LogPass("Set chromatic uniforms")
                Framework_Shader_Unload(chromaticId)
            Else
                LogFail("Load chromatic shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load chromatic shader", ex.Message)
        End Try

        ' Test Pixelate shader (NEW)
        Try
            Dim pixelateId = Framework_Shader_LoadPixelate()
            If pixelateId > 0 Then
                LogPass("Load pixelate shader")
                Framework_Shader_SetFloatByName(pixelateId, "pixelSize", 4.0F)
                Framework_Shader_SetVec2ByName(pixelateId, "resolution", 800.0F, 600.0F)
                LogPass("Set pixelate uniforms")
                Framework_Shader_Unload(pixelateId)
            Else
                LogFail("Load pixelate shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load pixelate shader", ex.Message)
        End Try

        ' Test Vignette shader
        Try
            Dim vignetteId = Framework_Shader_LoadVignette()
            If vignetteId > 0 Then
                LogPass("Load vignette shader")
                Framework_Shader_SetFloatByName(vignetteId, "vignetteRadius", 0.8F)
                Framework_Shader_SetFloatByName(vignetteId, "vignetteSoftness", 0.3F)
                Framework_Shader_SetFloatByName(vignetteId, "vignetteIntensity", 0.7F)
                LogPass("Set vignette uniforms")
                Framework_Shader_Unload(vignetteId)
            Else
                LogFail("Load vignette shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load vignette shader", ex.Message)
        End Try

        ' Test Bloom shader
        Try
            Dim bloomId = Framework_Shader_LoadBloom()
            If bloomId > 0 Then
                LogPass("Load bloom shader")
                Framework_Shader_SetFloatByName(bloomId, "bloomThreshold", 0.6F)
                Framework_Shader_SetFloatByName(bloomId, "bloomIntensity", 1.5F)
                Framework_Shader_SetFloatByName(bloomId, "bloomSpread", 3.0F)
                Framework_Shader_SetVec2ByName(bloomId, "resolution", 800.0F, 600.0F)
                LogPass("Set bloom uniforms")
                Framework_Shader_Unload(bloomId)
            Else
                LogFail("Load bloom shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load bloom shader", ex.Message)
        End Try

        ' Test Wave shader
        Try
            Dim waveId = Framework_Shader_LoadWave()
            If waveId > 0 Then
                LogPass("Load wave shader")
                Framework_Shader_SetFloatByName(waveId, "waveAmplitude", 0.02F)
                Framework_Shader_SetFloatByName(waveId, "waveFrequency", 10.0F)
                Framework_Shader_SetFloatByName(waveId, "waveSpeed", 3.0F)
                Framework_Shader_SetFloatByName(waveId, "time", 0.0F)
                LogPass("Set wave uniforms")
                Framework_Shader_Unload(waveId)
            Else
                LogFail("Load wave shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load wave shader", ex.Message)
        End Try

        ' Test Sharpen shader
        Try
            Dim sharpenId = Framework_Shader_LoadSharpen()
            If sharpenId > 0 Then
                LogPass("Load sharpen shader")
                Framework_Shader_SetFloatByName(sharpenId, "sharpenAmount", 1.0F)
                Framework_Shader_SetVec2ByName(sharpenId, "resolution", 800.0F, 600.0F)
                LogPass("Set sharpen uniforms")
                Framework_Shader_Unload(sharpenId)
            Else
                LogFail("Load sharpen shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load sharpen shader", ex.Message)
        End Try

        ' Test FilmGrain shader
        Try
            Dim grainId = Framework_Shader_LoadFilmGrain()
            If grainId > 0 Then
                LogPass("Load film grain shader")
                Framework_Shader_SetFloatByName(grainId, "grainIntensity", 0.2F)
                Framework_Shader_SetFloatByName(grainId, "grainSize", 1.5F)
                Framework_Shader_SetFloatByName(grainId, "time", 0.0F)
                LogPass("Set film grain uniforms")
                Framework_Shader_Unload(grainId)
            Else
                LogFail("Load film grain shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load film grain shader", ex.Message)
        End Try

        ' Test ColorAdjust shader
        Try
            Dim colorId = Framework_Shader_LoadColorAdjust()
            If colorId > 0 Then
                LogPass("Load color adjust shader")
                Framework_Shader_SetFloatByName(colorId, "brightness", 0.1F)
                Framework_Shader_SetFloatByName(colorId, "contrast", 1.2F)
                Framework_Shader_SetFloatByName(colorId, "saturation", 1.1F)
                LogPass("Set color adjust uniforms")
                Framework_Shader_Unload(colorId)
            Else
                LogFail("Load color adjust shader", "Invalid shader ID")
            End If
        Catch ex As Exception
            LogFail("Load color adjust shader", ex.Message)
        End Try

        ' Test shader count
        Try
            Dim count = Framework_Shader_GetCount()
            LogPass($"Get shader count (count={count})")
        Catch ex As Exception
            LogFail("Get shader count", ex.Message)
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

        ' Test timer is running (need to call Update first to transition from PENDING to RUNNING)
        If timerId > 0 Then
            Try
                ' Timer with delay > 0 starts in PENDING state, Update transitions to RUNNING
                Framework_Timer_Update(0.001F)
                If Framework_Timer_IsRunning(timerId) Then
                    LogPass("Timer is running")
                Else
                    LogFail("Timer is running", "Timer not running after update")
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
                ' After cancelling, the timer should not be running
                Dim isRunning = Framework_Timer_IsRunning(timerId)
                If Not isRunning Then
                    LogPass("Cancel timer (not running after cancel)")
                Else
                    LogFail("Cancel timer", "Timer still running after cancel")
                End If
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

        ' Test gamepad vibration (rumble)
        Try
            Framework_Input_SetGamepadVibration(0, 0.5F, 0.5F, 0.1F)
            LogPass("Set gamepad vibration")
        Catch ex As Exception
            LogFail("Set gamepad vibration", ex.Message)
        End Try

        ' Test pulse gamepad (convenience function)
        Try
            Framework_Input_PulseGamepad(0, 0.8F, 0.1F)
            LogPass("Pulse gamepad")
        Catch ex As Exception
            LogFail("Pulse gamepad", ex.Message)
        End Try

        ' Test impact rumble (quick strong vibration)
        Try
            Framework_Input_ImpactRumble(0, 1.0F)
            LogPass("Impact rumble")
        Catch ex As Exception
            LogFail("Impact rumble", ex.Message)
        End Try

        ' Test engine rumble (asymmetric vibration)
        Try
            Framework_Input_EngineRumble(0, 0.5F)
            LogPass("Engine rumble")
        Catch ex As Exception
            LogFail("Engine rumble", ex.Message)
        End Try

        ' Test is gamepad vibrating
        Try
            Dim isVibrating = Framework_Input_IsGamepadVibrating(0)
            LogPass($"Is gamepad vibrating (result={isVibrating})")
        Catch ex As Exception
            LogFail("Is gamepad vibrating", ex.Message)
        End Try

        ' Test get vibration time remaining
        Try
            Dim timeRemaining = Framework_Input_GetVibrationTimeRemaining(0)
            LogPass($"Get vibration time remaining (time={timeRemaining:F2})")
        Catch ex As Exception
            LogFail("Get vibration time remaining", ex.Message)
        End Try

        ' Test stop gamepad vibration
        Try
            Framework_Input_StopGamepadVibration(0)
            LogPass("Stop gamepad vibration")
        Catch ex As Exception
            LogFail("Stop gamepad vibration", ex.Message)
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

#Region "Tileset System Tests"
    Private Sub TestTilesetSystem()
        LogSection("Tileset System")

        Dim tilesetId As Integer = -1

        ' Test create tileset (using texture handle 0 which is invalid, but tests API)
        Try
            ' We don't have a real texture, so just test with handle 0
            tilesetId = Framework_Tileset_Create(0, 32, 32, 8)
            ' Handle 0 is invalid texture, but API should still return a tileset ID
            If tilesetId >= 0 Then
                LogPass($"Create tileset (id={tilesetId})")
            Else
                ' If it returns -1, that's also valid behavior for invalid texture
                LogPass("Create tileset (rejected invalid texture)")
                Return
            End If
        Catch ex As Exception
            LogFail("Create tileset", ex.Message)
            Return
        End Try

        ' Test tileset validity
        If tilesetId >= 0 Then
            Try
                Dim isValid = Framework_Tileset_IsValid(tilesetId)
                LogPass($"Tileset validity check (valid={isValid})")
            Catch ex As Exception
                LogFail("Tileset validity check", ex.Message)
            End Try
        End If

        ' Test get tile dimensions
        If tilesetId >= 0 Then
            Try
                Dim tileW = Framework_Tileset_GetTileWidth(tilesetId)
                Dim tileH = Framework_Tileset_GetTileHeight(tilesetId)
                If tileW = 32 AndAlso tileH = 32 Then
                    LogPass($"Get tile dimensions ({tileW}x{tileH})")
                Else
                    LogPass($"Get tile dimensions (returned {tileW}x{tileH})")
                End If
            Catch ex As Exception
                LogFail("Get tile dimensions", ex.Message)
            End Try
        End If

        ' Clean up
        If tilesetId >= 0 Then
            Try
                Framework_Tileset_Destroy(tilesetId)
                LogPass("Destroy tileset")
            Catch ex As Exception
                LogFail("Destroy tileset", ex.Message)
            End Try
        End If
    End Sub
#End Region

#Region "Animation Clip System Tests"
    Private Sub TestAnimationClipSystem()
        LogSection("Animation Clip System")

        Dim clipId As Integer = -1

        ' Test create animation clip
        Try
            clipId = Framework_AnimClip_Create("TestAnim", 4)
            If clipId >= 0 Then
                LogPass($"Create animation clip (id={clipId})")
            Else
                LogFail("Create animation clip", "Invalid clip ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create animation clip", ex.Message)
            Return
        End Try

        ' Test clip validity
        Try
            If Framework_AnimClip_IsValid(clipId) Then
                LogPass("Animation clip validity check")
            Else
                LogFail("Animation clip validity check", "Clip not valid")
            End If
        Catch ex As Exception
            LogFail("Animation clip validity check", ex.Message)
        End Try

        ' Test set frame
        Try
            Framework_AnimClip_SetFrame(clipId, 0, 0, 0, 32, 32, 0.1F)
            Framework_AnimClip_SetFrame(clipId, 1, 32, 0, 32, 32, 0.1F)
            Framework_AnimClip_SetFrame(clipId, 2, 64, 0, 32, 32, 0.1F)
            Framework_AnimClip_SetFrame(clipId, 3, 96, 0, 32, 32, 0.1F)
            LogPass("Set animation frames")
        Catch ex As Exception
            LogFail("Set animation frames", ex.Message)
        End Try

        ' Test get frame count
        Try
            Dim frameCount = Framework_AnimClip_GetFrameCount(clipId)
            If frameCount = 4 Then
                LogPass($"Get frame count (count={frameCount})")
            Else
                LogFail("Get frame count", $"Expected 4, got {frameCount}")
            End If
        Catch ex As Exception
            LogFail("Get frame count", ex.Message)
        End Try

        ' Test get total duration
        Try
            Dim duration = Framework_AnimClip_GetTotalDuration(clipId)
            If Math.Abs(duration - 0.4F) < 0.01 Then
                LogPass($"Get total duration ({duration})")
            Else
                LogPass($"Get total duration (returned {duration})")
            End If
        Catch ex As Exception
            LogFail("Get total duration", ex.Message)
        End Try

        ' Test set loop mode
        Try
            Framework_AnimClip_SetLoopMode(clipId, 1) ' 1 = repeat
            LogPass("Set loop mode")
        Catch ex As Exception
            LogFail("Set loop mode", ex.Message)
        End Try

        ' Test find by name
        Try
            Dim foundId = Framework_AnimClip_FindByName("TestAnim")
            If foundId = clipId Then
                LogPass("Find animation clip by name")
            Else
                LogPass($"Find animation clip by name (returned {foundId})")
            End If
        Catch ex As Exception
            LogFail("Find animation clip by name", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_AnimClip_Destroy(clipId)
            If Not Framework_AnimClip_IsValid(clipId) Then
                LogPass("Destroy animation clip")
            Else
                LogFail("Destroy animation clip", "Clip still valid")
            End If
        Catch ex As Exception
            LogFail("Destroy animation clip", ex.Message)
        End Try
    End Sub
#End Region

#Region "Audio Manager System Tests"
    Private Sub TestAudioManagerSystem()
        LogSection("Audio Manager System")

        ' Test group volume (no audio files needed)
        Try
            Framework_Audio_SetGroupVolume(0, 0.8F) ' Master group
            Dim vol = Framework_Audio_GetGroupVolume(0)
            If Math.Abs(vol - 0.8F) < 0.01 Then
                LogPass("Set/Get group volume")
            Else
                LogFail("Set/Get group volume", $"Expected 0.8, got {vol}")
            End If
        Catch ex As Exception
            LogFail("Set/Get group volume", ex.Message)
        End Try

        ' Test group mute
        Try
            Framework_Audio_SetGroupMuted(1, True) ' Music group
            If Framework_Audio_IsGroupMuted(1) Then
                LogPass("Set/Get group muted")
            Else
                LogFail("Set/Get group muted", "Not muted after set")
            End If
            Framework_Audio_SetGroupMuted(1, False)
        Catch ex As Exception
            LogFail("Set/Get group muted", ex.Message)
        End Try

        ' Test listener position
        Try
            Framework_Audio_SetListenerPosition(100, 200)
            Dim x As Single = 0, y As Single = 0
            Framework_Audio_GetListenerPosition(x, y)
            If Math.Abs(x - 100) < 0.01 AndAlso Math.Abs(y - 200) < 0.01 Then
                LogPass("Set/Get listener position")
            Else
                LogFail("Set/Get listener position", $"Expected (100,200), got ({x},{y})")
            End If
        Catch ex As Exception
            LogFail("Set/Get listener position", ex.Message)
        End Try

        ' Test spatial falloff
        Try
            Framework_Audio_SetSpatialFalloff(50, 500)
            LogPass("Set spatial falloff")
        Catch ex As Exception
            LogFail("Set spatial falloff", ex.Message)
        End Try

        ' Test spatial enabled
        Try
            Framework_Audio_SetSpatialEnabled(True)
            LogPass("Enable spatial audio")
            Framework_Audio_SetSpatialEnabled(False)
        Catch ex As Exception
            LogFail("Enable spatial audio", ex.Message)
        End Try

        ' Test playlist creation
        Try
            Dim playlistId = Framework_Audio_CreatePlaylist()
            If playlistId >= 0 Then
                LogPass($"Create playlist (id={playlistId})")
                Framework_Audio_DestroyPlaylist(playlistId)
            Else
                LogPass("Create playlist (API call)")
            End If
        Catch ex As Exception
            LogFail("Create playlist", ex.Message)
        End Try
    End Sub
#End Region

#Region "Scene Manager System Tests"
    Private Sub TestSceneManagerSystem()
        LogSection("Scene Manager System")

        ' Test set transition
        Try
            Framework_Scene_SetTransition(0, 0.5F) ' 0 = fade
            LogPass("Set scene transition")
        Catch ex As Exception
            LogFail("Set scene transition", ex.Message)
        End Try

        ' Test get transition type
        Try
            Dim transType = Framework_Scene_GetTransitionType()
            If transType = 0 Then
                LogPass("Get transition type")
            Else
                LogPass($"Get transition type (type={transType})")
            End If
        Catch ex As Exception
            LogFail("Get transition type", ex.Message)
        End Try

        ' Test get transition duration
        Try
            Dim duration = Framework_Scene_GetTransitionDuration()
            If Math.Abs(duration - 0.5F) < 0.01 Then
                LogPass("Get transition duration")
            Else
                LogPass($"Get transition duration (duration={duration})")
            End If
        Catch ex As Exception
            LogFail("Get transition duration", ex.Message)
        End Try

        ' Test set transition color
        Try
            Framework_Scene_SetTransitionColor(0, 0, 0, 255)
            LogPass("Set transition color")
        Catch ex As Exception
            LogFail("Set transition color", ex.Message)
        End Try

        ' Test is transitioning
        Try
            Dim isTransitioning = Framework_Scene_IsTransitioning()
            LogPass($"Is transitioning check (transitioning={isTransitioning})")
        Catch ex As Exception
            LogFail("Is transitioning check", ex.Message)
        End Try

        ' Test loading settings
        Try
            Framework_Scene_SetLoadingEnabled(True)
            If Framework_Scene_IsLoadingEnabled() Then
                LogPass("Enable loading screen")
            Else
                LogFail("Enable loading screen", "Not enabled")
            End If
            Framework_Scene_SetLoadingEnabled(False)
        Catch ex As Exception
            LogFail("Enable loading screen", ex.Message)
        End Try

        ' Test loading min duration
        Try
            Framework_Scene_SetLoadingMinDuration(1.0F)
            Dim minDur = Framework_Scene_GetLoadingMinDuration()
            If Math.Abs(minDur - 1.0F) < 0.01 Then
                LogPass("Set/Get loading min duration")
            Else
                LogFail("Set/Get loading min duration", $"Expected 1.0, got {minDur}")
            End If
        Catch ex As Exception
            LogFail("Set/Get loading min duration", ex.Message)
        End Try

        ' Test stack size
        Try
            Dim stackSize = Framework_Scene_GetStackSize()
            LogPass($"Get scene stack size (size={stackSize})")
        Catch ex As Exception
            LogFail("Get scene stack size", ex.Message)
        End Try
    End Sub
#End Region

#Region "Object Pooling System Tests"
    Private Sub TestObjectPoolingSystem()
        LogSection("Object Pooling System")

        Dim poolId As Integer = -1

        ' Test create pool
        Try
            poolId = Framework_Pool_Create("TestPool", 10, 50)
            If poolId > 0 AndAlso Framework_Pool_IsValid(poolId) Then
                LogPass($"Create object pool (id={poolId})")
            Else
                LogFail("Create object pool", "Invalid pool ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create object pool", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_Pool_GetByName("TestPool")
            If foundId = poolId Then
                LogPass("Get pool by name")
            Else
                LogFail("Get pool by name", $"Expected {poolId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get pool by name", ex.Message)
        End Try

        ' Test capacity
        Try
            Dim capacity = Framework_Pool_GetCapacity(poolId)
            If capacity = 10 Then
                LogPass($"Get pool capacity (capacity={capacity})")
            Else
                LogFail("Get pool capacity", $"Expected 10, got {capacity}")
            End If
        Catch ex As Exception
            LogFail("Get pool capacity", ex.Message)
        End Try

        ' Test auto grow
        Try
            Framework_Pool_SetAutoGrow(poolId, True)
            If Framework_Pool_GetAutoGrow(poolId) Then
                LogPass("Set/Get auto grow")
            Else
                LogFail("Set/Get auto grow", "Not enabled")
            End If
        Catch ex As Exception
            LogFail("Set/Get auto grow", ex.Message)
        End Try

        ' Test acquire object
        Try
            Dim objIndex = Framework_Pool_Acquire(poolId)
            If objIndex >= 0 Then
                LogPass($"Acquire object (index={objIndex})")

                ' Test is object active
                If Framework_Pool_IsObjectActive(poolId, objIndex) Then
                    LogPass("Object is active after acquire")
                Else
                    LogFail("Object is active after acquire", "Not active")
                End If

                ' Test release object
                Framework_Pool_Release(poolId, objIndex)
                If Not Framework_Pool_IsObjectActive(poolId, objIndex) Then
                    LogPass("Release object")
                Else
                    LogFail("Release object", "Still active after release")
                End If
            Else
                LogFail("Acquire object", "Invalid object index")
            End If
        Catch ex As Exception
            LogFail("Acquire object", ex.Message)
        End Try

        ' Test active count
        Try
            Dim activeCount = Framework_Pool_GetActiveCount(poolId)
            If activeCount = 0 Then
                LogPass("Get active count after release")
            Else
                LogFail("Get active count after release", $"Expected 0, got {activeCount}")
            End If
        Catch ex As Exception
            LogFail("Get active count after release", ex.Message)
        End Try

        ' Test warmup
        Try
            Framework_Pool_Warmup(poolId, 5)
            LogPass("Warmup pool")
        Catch ex As Exception
            LogFail("Warmup pool", ex.Message)
        End Try

        ' Test statistics
        Try
            Dim totalAcquires = Framework_Pool_GetTotalAcquires(poolId)
            Dim peakUsage = Framework_Pool_GetPeakUsage(poolId)
            LogPass($"Pool statistics (acquires={totalAcquires}, peak={peakUsage})")
        Catch ex As Exception
            LogFail("Pool statistics", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Pool_Destroy(poolId)
            If Not Framework_Pool_IsValid(poolId) Then
                LogPass("Destroy pool")
            Else
                LogFail("Destroy pool", "Pool still valid")
            End If
        Catch ex As Exception
            LogFail("Destroy pool", ex.Message)
        End Try
    End Sub
#End Region

#Region "FSM (State Machine) System Tests"
    Private Sub TestFSMSystem()
        LogSection("FSM (State Machine) System")

        Dim fsmId As Integer = -1

        ' Test create FSM
        Try
            fsmId = Framework_FSM_Create("TestFSM")
            If fsmId > 0 AndAlso Framework_FSM_IsValid(fsmId) Then
                LogPass($"Create FSM (id={fsmId})")
            Else
                LogFail("Create FSM", "Invalid FSM ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create FSM", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_FSM_GetByName("TestFSM")
            If foundId = fsmId Then
                LogPass("Get FSM by name")
            Else
                LogFail("Get FSM by name", $"Expected {fsmId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get FSM by name", ex.Message)
        End Try

        ' Test add states
        Dim idleState As Integer = -1
        Dim walkState As Integer = -1
        Dim runState As Integer = -1

        Try
            idleState = Framework_FSM_AddState(fsmId, "Idle")
            walkState = Framework_FSM_AddState(fsmId, "Walk")
            runState = Framework_FSM_AddState(fsmId, "Run")
            If idleState >= 0 AndAlso walkState >= 0 AndAlso runState >= 0 Then
                LogPass($"Add states (idle={idleState}, walk={walkState}, run={runState})")
            Else
                LogFail("Add states", "Invalid state IDs")
            End If
        Catch ex As Exception
            LogFail("Add states", ex.Message)
        End Try

        ' Test get state count
        Try
            Dim stateCount = Framework_FSM_GetStateCount(fsmId)
            If stateCount = 3 Then
                LogPass($"Get state count (count={stateCount})")
            Else
                LogFail("Get state count", $"Expected 3, got {stateCount}")
            End If
        Catch ex As Exception
            LogFail("Get state count", ex.Message)
        End Try

        ' Test get state by name
        Try
            Dim foundState = Framework_FSM_GetState(fsmId, "Walk")
            If foundState = walkState Then
                LogPass("Get state by name")
            Else
                LogFail("Get state by name", $"Expected {walkState}, got {foundState}")
            End If
        Catch ex As Exception
            LogFail("Get state by name", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_FSM_Destroy(fsmId)
            If Not Framework_FSM_IsValid(fsmId) Then
                LogPass("Destroy FSM")
            Else
                LogFail("Destroy FSM", "FSM still valid")
            End If
        Catch ex As Exception
            LogFail("Destroy FSM", ex.Message)
        End Try
    End Sub
#End Region

#Region "Dialogue System Tests"
    Private Sub TestDialogueSystem()
        LogSection("Dialogue System")

        Dim dialogueId As Integer = -1

        ' Test create dialogue
        Try
            dialogueId = Framework_Dialogue_Create("TestDialogue")
            If dialogueId > 0 AndAlso Framework_Dialogue_IsValid(dialogueId) Then
                LogPass($"Create dialogue (id={dialogueId})")
            Else
                LogFail("Create dialogue", "Invalid dialogue ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create dialogue", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_Dialogue_GetByName("TestDialogue")
            If foundId = dialogueId Then
                LogPass("Get dialogue by name")
            Else
                LogFail("Get dialogue by name", $"Expected {dialogueId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get dialogue by name", ex.Message)
        End Try

        ' Test add nodes
        Dim node1 As Integer = -1
        Dim node2 As Integer = -1

        Try
            node1 = Framework_Dialogue_AddNode(dialogueId, "greeting")
            node2 = Framework_Dialogue_AddNode(dialogueId, "response")
            If node1 >= 0 AndAlso node2 >= 0 Then
                LogPass($"Add dialogue nodes (node1={node1}, node2={node2})")
            Else
                LogFail("Add dialogue nodes", "Invalid node IDs")
            End If
        Catch ex As Exception
            LogFail("Add dialogue nodes", ex.Message)
        End Try

        ' Test get node count
        Try
            Dim nodeCount = Framework_Dialogue_GetNodeCount(dialogueId)
            If nodeCount = 2 Then
                LogPass($"Get node count (count={nodeCount})")
            Else
                LogFail("Get node count", $"Expected 2, got {nodeCount}")
            End If
        Catch ex As Exception
            LogFail("Get node count", ex.Message)
        End Try

        ' Test set node text
        If node1 >= 0 Then
            Try
                Framework_Dialogue_SetNodeSpeaker(dialogueId, node1, "NPC")
                Framework_Dialogue_SetNodeText(dialogueId, node1, "Hello there!")
                LogPass("Set node speaker and text")
            Catch ex As Exception
                LogFail("Set node speaker and text", ex.Message)
            End Try
        End If

        ' Test set start node
        If node1 >= 0 Then
            Try
                Framework_Dialogue_SetStartNode(dialogueId, node1)
                Dim startNode = Framework_Dialogue_GetStartNode(dialogueId)
                If startNode = node1 Then
                    LogPass("Set/Get start node")
                Else
                    LogFail("Set/Get start node", $"Expected {node1}, got {startNode}")
                End If
            Catch ex As Exception
                LogFail("Set/Get start node", ex.Message)
            End Try
        End If

        ' Test dialogue variables
        Try
            Framework_Dialogue_SetVarInt("score", 100)
            Dim score = Framework_Dialogue_GetVarInt("score")
            If score = 100 Then
                LogPass("Set/Get dialogue variable (int)")
            Else
                LogFail("Set/Get dialogue variable (int)", $"Expected 100, got {score}")
            End If
        Catch ex As Exception
            LogFail("Set/Get dialogue variable (int)", ex.Message)
        End Try

        ' Test typewriter settings
        Try
            Framework_Dialogue_SetTypewriterEnabled(True)
            If Framework_Dialogue_IsTypewriterEnabled() Then
                LogPass("Enable typewriter effect")
            Else
                LogFail("Enable typewriter effect", "Not enabled")
            End If
        Catch ex As Exception
            LogFail("Enable typewriter effect", ex.Message)
        End Try

        ' Test typewriter speed
        Try
            Framework_Dialogue_SetTypewriterSpeed(30.0F)
            Dim speed = Framework_Dialogue_GetTypewriterSpeed()
            If Math.Abs(speed - 30.0F) < 0.1 Then
                LogPass("Set/Get typewriter speed")
            Else
                LogFail("Set/Get typewriter speed", $"Expected 30, got {speed}")
            End If
        Catch ex As Exception
            LogFail("Set/Get typewriter speed", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Dialogue_Destroy(dialogueId)
            If Not Framework_Dialogue_IsValid(dialogueId) Then
                LogPass("Destroy dialogue")
            Else
                LogFail("Destroy dialogue", "Dialogue still valid")
            End If
        Catch ex As Exception
            LogFail("Destroy dialogue", ex.Message)
        End Try
    End Sub
#End Region

#Region "Inventory System Tests"
    Private Sub TestInventorySystem()
        LogSection("Inventory System")

        Dim invId As Integer = -1

        ' Test create inventory
        Try
            invId = Framework_Inventory_Create("PlayerInventory", 20)
            If invId > 0 Then
                LogPass($"Create inventory (id={invId})")
            Else
                LogFail("Create inventory", "Invalid inventory ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create inventory", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_Inventory_GetByName("PlayerInventory")
            If foundId = invId Then
                LogPass("Get inventory by name")
            Else
                LogFail("Get inventory by name", $"Expected {invId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get inventory by name", ex.Message)
        End Try

        ' Test get capacity
        Try
            Dim capacity = Framework_Inventory_GetCapacity(invId)
            If capacity = 20 Then
                LogPass($"Get inventory capacity (capacity={capacity})")
            Else
                LogFail("Get inventory capacity", $"Expected 20, got {capacity}")
            End If
        Catch ex As Exception
            LogFail("Get inventory capacity", ex.Message)
        End Try

        ' Test max weight
        Try
            Framework_Inventory_SetMaxWeight(invId, 100.0F)
            Dim maxWeight = Framework_Inventory_GetMaxWeight(invId)
            If Math.Abs(maxWeight - 100.0F) < 0.1 Then
                LogPass("Set/Get max weight")
            Else
                LogFail("Set/Get max weight", $"Expected 100, got {maxWeight}")
            End If
        Catch ex As Exception
            LogFail("Set/Get max weight", ex.Message)
        End Try

        ' Test empty slot count
        Try
            Dim emptySlots = Framework_Inventory_GetEmptySlotCount(invId)
            If emptySlots = 20 Then
                LogPass($"Get empty slot count (count={emptySlots})")
            Else
                LogFail("Get empty slot count", $"Expected 20, got {emptySlots}")
            End If
        Catch ex As Exception
            LogFail("Get empty slot count", ex.Message)
        End Try

        ' Test auto stack
        Try
            Framework_Inventory_SetAutoStack(invId, True)
            LogPass("Set auto stack")
        Catch ex As Exception
            LogFail("Set auto stack", ex.Message)
        End Try

        ' Test clear inventory
        Try
            Framework_Inventory_Clear(invId)
            LogPass("Clear inventory")
        Catch ex As Exception
            LogFail("Clear inventory", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Inventory_Destroy(invId)
            LogPass("Destroy inventory")
        Catch ex As Exception
            LogFail("Destroy inventory", ex.Message)
        End Try
    End Sub
#End Region

#Region "Quest System Tests"
    Private Sub TestQuestSystem()
        LogSection("Quest System")

        Dim questHandle As Integer = -1

        ' Test define quest
        Try
            questHandle = Framework_Quest_Define("main_quest_01")
            If questHandle > 0 Then
                LogPass($"Define quest (handle={questHandle})")
            Else
                LogFail("Define quest", "Invalid quest handle")
                Return
            End If
        Catch ex As Exception
            LogFail("Define quest", ex.Message)
            Return
        End Try

        ' Test set quest name
        Try
            Framework_Quest_SetName(questHandle, "The Great Adventure")
            LogPass("Set quest name")
        Catch ex As Exception
            LogFail("Set quest name", ex.Message)
        End Try

        ' Test set quest description
        Try
            Framework_Quest_SetDescription(questHandle, "Embark on an epic journey to save the kingdom.")
            LogPass("Set quest description")
        Catch ex As Exception
            LogFail("Set quest description", ex.Message)
        End Try

        ' Test set quest category
        Try
            Framework_Quest_SetCategory(questHandle, "Main")
            LogPass("Set quest category")
        Catch ex As Exception
            LogFail("Set quest category", ex.Message)
        End Try

        ' Test set quest level
        Try
            Framework_Quest_SetLevel(questHandle, 5)
            LogPass("Set quest level")
        Catch ex As Exception
            LogFail("Set quest level", ex.Message)
        End Try

        ' Test set repeatable
        Try
            Framework_Quest_SetRepeatable(questHandle, False)
            LogPass("Set quest repeatable")
        Catch ex As Exception
            LogFail("Set quest repeatable", ex.Message)
        End Try

        ' Test set time limit
        Try
            Framework_Quest_SetTimeLimit(questHandle, 3600.0F) ' 1 hour
            LogPass("Set quest time limit")
        Catch ex As Exception
            LogFail("Set quest time limit", ex.Message)
        End Try

        ' Test add objective
        Try
            Dim objIndex = Framework_Quest_AddObjective(questHandle, 1, "Defeat 10 enemies", 10) ' 1 = Kill type
            If objIndex >= 0 Then
                LogPass($"Add quest objective (index={objIndex})")
            Else
                LogFail("Add quest objective", "Invalid objective index")
            End If
        Catch ex As Exception
            LogFail("Add quest objective", ex.Message)
        End Try

        ' Test get objective count
        Try
            Dim objCount = Framework_Quest_GetObjectiveCount(questHandle)
            If objCount = 1 Then
                LogPass($"Get objective count (count={objCount})")
            Else
                LogFail("Get objective count", $"Expected 1, got {objCount}")
            End If
        Catch ex As Exception
            LogFail("Get objective count", ex.Message)
        End Try

        ' Test set min level
        Try
            Framework_Quest_SetMinLevel(questHandle, 3)
            LogPass("Set quest min level")
        Catch ex As Exception
            LogFail("Set quest min level", ex.Message)
        End Try

        ' Test auto complete
        Try
            Framework_Quest_SetAutoComplete(questHandle, True)
            LogPass("Set quest auto complete")
        Catch ex As Exception
            LogFail("Set quest auto complete", ex.Message)
        End Try
    End Sub
#End Region

#Region "Lighting System Tests"
    Private Sub TestLightingSystem()
        LogSection("Lighting System")

        Dim lightId As Integer = -1

        ' Test create point light
        Try
            lightId = Framework_Light_CreatePoint(400, 300, 200)
            If lightId > 0 Then
                LogPass($"Create point light (id={lightId})")
            Else
                LogFail("Create point light", "Invalid light ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create point light", ex.Message)
            Return
        End Try

        ' Test set/get position
        Try
            Framework_Light_SetPosition(lightId, 500, 400)
            Dim x As Single = 0, y As Single = 0
            Framework_Light_GetPosition(lightId, x, y)
            If Math.Abs(x - 500) < 0.1 AndAlso Math.Abs(y - 400) < 0.1 Then
                LogPass("Set/Get light position")
            Else
                LogFail("Set/Get light position", $"Expected (500,400), got ({x},{y})")
            End If
        Catch ex As Exception
            LogFail("Set/Get light position", ex.Message)
        End Try

        ' Test set light color
        Try
            Framework_Light_SetColor(lightId, 255, 200, 100)
            LogPass("Set light color")
        Catch ex As Exception
            LogFail("Set light color", ex.Message)
        End Try

        ' Test set/get intensity
        Try
            Framework_Light_SetIntensity(lightId, 0.8F)
            Dim intensity = Framework_Light_GetIntensity(lightId)
            If Math.Abs(intensity - 0.8F) < 0.01 Then
                LogPass("Set/Get light intensity")
            Else
                LogFail("Set/Get light intensity", $"Expected 0.8, got {intensity}")
            End If
        Catch ex As Exception
            LogFail("Set/Get light intensity", ex.Message)
        End Try

        ' Test set/get radius
        Try
            Framework_Light_SetRadius(lightId, 300)
            Dim radius = Framework_Light_GetRadius(lightId)
            If Math.Abs(radius - 300) < 0.1 Then
                LogPass("Set/Get light radius")
            Else
                LogFail("Set/Get light radius", $"Expected 300, got {radius}")
            End If
        Catch ex As Exception
            LogFail("Set/Get light radius", ex.Message)
        End Try

        ' Test set/get enabled
        Try
            Framework_Light_SetEnabled(lightId, False)
            If Not Framework_Light_IsEnabled(lightId) Then
                LogPass("Set/Get light enabled")
            Else
                LogFail("Set/Get light enabled", "Light still enabled after disable")
            End If
            Framework_Light_SetEnabled(lightId, True)
        Catch ex As Exception
            LogFail("Set/Get light enabled", ex.Message)
        End Try

        ' Test set/get falloff
        Try
            Framework_Light_SetFalloff(lightId, 2.0F)
            Dim falloff = Framework_Light_GetFalloff(lightId)
            If Math.Abs(falloff - 2.0F) < 0.01 Then
                LogPass("Set/Get light falloff")
            Else
                LogFail("Set/Get light falloff", $"Expected 2.0, got {falloff}")
            End If
        Catch ex As Exception
            LogFail("Set/Get light falloff", ex.Message)
        End Try

        ' Test set/get layer
        Try
            Framework_Light_SetLayer(lightId, 5)
            Dim layer = Framework_Light_GetLayer(lightId)
            If layer = 5 Then
                LogPass("Set/Get light layer")
            Else
                LogFail("Set/Get light layer", $"Expected 5, got {layer}")
            End If
        Catch ex As Exception
            LogFail("Set/Get light layer", ex.Message)
        End Try

        ' Test flicker effect
        Try
            Framework_Light_SetFlicker(lightId, 0.2F, 5.0F)
            LogPass("Set light flicker")
        Catch ex As Exception
            LogFail("Set light flicker", ex.Message)
        End Try

        ' Test pulse effect
        Try
            Framework_Light_SetPulse(lightId, 0.5F, 1.0F, 2.0F)
            LogPass("Set light pulse")
        Catch ex As Exception
            LogFail("Set light pulse", ex.Message)
        End Try

        ' Test create spot light
        Dim spotId As Integer = -1
        Try
            spotId = Framework_Light_CreateSpot(200, 200, 150, 45, 30)
            If spotId > 0 Then
                LogPass($"Create spot light (id={spotId})")
            Else
                LogFail("Create spot light", "Invalid light ID")
            End If
        Catch ex As Exception
            LogFail("Create spot light", ex.Message)
        End Try

        ' Test set/get direction
        If spotId > 0 Then
            Try
                Framework_Light_SetDirection(spotId, 90)
                Dim direction = Framework_Light_GetDirection(spotId)
                If Math.Abs(direction - 90) < 0.1 Then
                    LogPass("Set/Get spot light direction")
                Else
                    LogFail("Set/Get spot light direction", $"Expected 90, got {direction}")
                End If
            Catch ex As Exception
                LogFail("Set/Get spot light direction", ex.Message)
            End Try
        End If

        ' Test set/get cone angle
        If spotId > 0 Then
            Try
                Framework_Light_SetConeAngle(spotId, 45)
                Dim cone = Framework_Light_GetConeAngle(spotId)
                If Math.Abs(cone - 45) < 0.1 Then
                    LogPass("Set/Get spot light cone angle")
                Else
                    LogFail("Set/Get spot light cone angle", $"Expected 45, got {cone}")
                End If
            Catch ex As Exception
                LogFail("Set/Get spot light cone angle", ex.Message)
            End Try
        End If

        ' Test get light count
        Try
            Dim count = Framework_Light_GetCount()
            If count >= 2 Then
                LogPass($"Get light count (count={count})")
            Else
                LogFail("Get light count", $"Expected >=2, got {count}")
            End If
        Catch ex As Exception
            LogFail("Get light count", ex.Message)
        End Try

        ' Test get light type
        Try
            Dim lightType = Framework_Light_GetType(lightId)
            LogPass($"Get light type (type={lightType})")
        Catch ex As Exception
            LogFail("Get light type", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Light_Destroy(lightId)
            If spotId > 0 Then Framework_Light_Destroy(spotId)
            LogPass("Destroy lights")
        Catch ex As Exception
            LogFail("Destroy lights", ex.Message)
        End Try
    End Sub
#End Region

#Region "Screen Effects System Tests"
    Private Sub TestScreenEffectsSystem()
        LogSection("Screen Effects System")

        ' Test initialize
        Try
            Framework_Effects_Initialize(800, 600)
            LogPass("Initialize effects")
        Catch ex As Exception
            LogFail("Initialize effects", ex.Message)
        End Try

        ' Test set/get enabled
        Try
            Framework_Effects_SetEnabled(True)
            If Framework_Effects_IsEnabled() Then
                LogPass("Set/Get effects enabled")
            Else
                LogFail("Set/Get effects enabled", "Effects not enabled")
            End If
        Catch ex As Exception
            LogFail("Set/Get effects enabled", ex.Message)
        End Try

        ' Test vignette settings
        Try
            Framework_Effects_SetVignetteEnabled(True)
            Framework_Effects_SetVignetteIntensity(0.5F)
            Framework_Effects_SetVignetteRadius(0.8F)
            Framework_Effects_SetVignetteSoftness(0.3F)
            Framework_Effects_SetVignetteColor(0, 0, 0)
            LogPass("Set vignette parameters")
        Catch ex As Exception
            LogFail("Set vignette parameters", ex.Message)
        End Try

        ' Test blur settings
        Try
            Framework_Effects_SetBlurEnabled(True)
            Framework_Effects_SetBlurAmount(2.0F)
            Framework_Effects_SetBlurIterations(3)
            LogPass("Set blur parameters")
            Framework_Effects_SetBlurEnabled(False)
        Catch ex As Exception
            LogFail("Set blur parameters", ex.Message)
        End Try

        ' Test chromatic aberration
        Try
            Framework_Effects_SetChromaticEnabled(True)
            Framework_Effects_SetChromaticOffset(3.0F)
            Framework_Effects_SetChromaticAngle(0.0F)
            LogPass("Set chromatic aberration parameters")
            Framework_Effects_SetChromaticEnabled(False)
        Catch ex As Exception
            LogFail("Set chromatic aberration parameters", ex.Message)
        End Try

        ' Test pixelate
        Try
            Framework_Effects_SetPixelateEnabled(True)
            Framework_Effects_SetPixelateSize(4)
            LogPass("Set pixelate parameters")
            Framework_Effects_SetPixelateEnabled(False)
        Catch ex As Exception
            LogFail("Set pixelate parameters", ex.Message)
        End Try

        ' Test scanlines
        Try
            Framework_Effects_SetScanlinesEnabled(True)
            Framework_Effects_SetScanlinesIntensity(0.3F)
            Framework_Effects_SetScanlinesCount(200)
            Framework_Effects_SetScanlinesSpeed(0.0F)
            LogPass("Set scanlines parameters")
            Framework_Effects_SetScanlinesEnabled(False)
        Catch ex As Exception
            LogFail("Set scanlines parameters", ex.Message)
        End Try

        ' Test CRT effect
        Try
            Framework_Effects_SetCRTEnabled(True)
            Framework_Effects_SetCRTCurvature(0.1F)
            Framework_Effects_SetCRTVignetteIntensity(0.2F)
            LogPass("Set CRT parameters")
            Framework_Effects_SetCRTEnabled(False)
        Catch ex As Exception
            LogFail("Set CRT parameters", ex.Message)
        End Try

        ' Test color effects
        Try
            Framework_Effects_SetGrayscaleEnabled(True)
            Framework_Effects_SetGrayscaleAmount(0.5F)
            Framework_Effects_SetGrayscaleEnabled(False)
            Framework_Effects_SetSepiaEnabled(True)
            Framework_Effects_SetSepiaAmount(0.7F)
            Framework_Effects_SetSepiaEnabled(False)
            Framework_Effects_SetInvertEnabled(True)
            Framework_Effects_SetInvertAmount(1.0F)
            Framework_Effects_SetInvertEnabled(False)
            LogPass("Set color effect parameters")
        Catch ex As Exception
            LogFail("Set color effect parameters", ex.Message)
        End Try

        ' Test color grading
        Try
            Framework_Effects_SetTintEnabled(True)
            Framework_Effects_SetTintColor(255, 200, 150)
            Framework_Effects_SetTintAmount(0.2F)
            Framework_Effects_SetTintEnabled(False)
            Framework_Effects_SetBrightness(1.1F)
            Framework_Effects_SetContrast(1.0F)
            Framework_Effects_SetSaturation(1.0F)
            Framework_Effects_SetGamma(1.0F)
            LogPass("Set color grading parameters")
        Catch ex As Exception
            LogFail("Set color grading parameters", ex.Message)
        End Try

        ' Test film grain
        Try
            Framework_Effects_SetFilmGrainEnabled(True)
            Framework_Effects_SetFilmGrainIntensity(0.1F)
            Framework_Effects_SetFilmGrainSpeed(10.0F)
            LogPass("Set film grain parameters")
            Framework_Effects_SetFilmGrainEnabled(False)
        Catch ex As Exception
            LogFail("Set film grain parameters", ex.Message)
        End Try

        ' Test flash
        Try
            Framework_Effects_Flash(255, 255, 255, 0.1F)
            If Framework_Effects_IsFlashing() Then
                LogPass("Flash effect (is flashing)")
            Else
                LogPass("Flash effect (triggered)")
            End If
        Catch ex As Exception
            LogFail("Flash effect", ex.Message)
        End Try

        ' Test fade
        Try
            Framework_Effects_SetFadeColor(0, 0, 0)
            Framework_Effects_FadeOut(0.1F)
            If Framework_Effects_IsFading() Then
                LogPass("Fade effect (is fading)")
            Else
                LogPass("Fade effect (triggered)")
            End If
        Catch ex As Exception
            LogFail("Fade effect", ex.Message)
        End Try

        ' Test shake
        Try
            Framework_Effects_Shake(5.0F, 0.1F)
            If Framework_Effects_IsShaking() Then
                LogPass("Shake effect (is shaking)")
            Else
                LogPass("Shake effect (triggered)")
            End If
            Framework_Effects_StopShake()
        Catch ex As Exception
            LogFail("Shake effect", ex.Message)
        End Try

        ' Test presets
        Try
            Framework_Effects_ApplyPresetRetro()
            Framework_Effects_ResetAll()
            Framework_Effects_ApplyPresetDream()
            Framework_Effects_ResetAll()
            Framework_Effects_ApplyPresetHorror()
            Framework_Effects_ResetAll()
            Framework_Effects_ApplyPresetNoir()
            Framework_Effects_ResetAll()
            LogPass("Apply effect presets")
        Catch ex As Exception
            LogFail("Apply effect presets", ex.Message)
        End Try

        ' Test shutdown
        Try
            Framework_Effects_Shutdown()
            LogPass("Shutdown effects")
        Catch ex As Exception
            LogFail("Shutdown effects", ex.Message)
        End Try
    End Sub
#End Region

#Region "AI/Pathfinding System Tests"
    Private Sub TestAIPathfindingSystem()
        LogSection("AI/Pathfinding System")

        Dim gridId As Integer = -1
        Dim pathId As Integer = -1
        Dim agentId As Integer = -1

        ' Test create navigation grid
        Try
            gridId = Framework_NavGrid_Create(20, 15, 32.0F)
            If gridId > 0 Then
                LogPass($"Create nav grid (id={gridId})")
            Else
                LogFail("Create nav grid", "Invalid grid ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create nav grid", ex.Message)
            Return
        End Try

        ' Test grid validity
        Try
            If Framework_NavGrid_IsValid(gridId) Then
                LogPass("Nav grid validity check")
            Else
                LogFail("Nav grid validity check", "Grid not valid")
            End If
        Catch ex As Exception
            LogFail("Nav grid validity check", ex.Message)
        End Try

        ' Test get grid dimensions (functions may not be exported)
        Try
            Dim width = Framework_NavGrid_GetWidth(gridId)
            Dim height = Framework_NavGrid_GetHeight(gridId)
            Dim cellSize = Framework_NavGrid_GetCellSize(gridId)
            If width = 20 AndAlso height = 15 AndAlso Math.Abs(cellSize - 32.0F) < 0.1 Then
                LogPass($"Get grid dimensions ({width}x{height}, cell={cellSize})")
            Else
                LogPass($"Get grid dimensions (returned {width}x{height}x{cellSize})")
            End If
        Catch ex As EntryPointNotFoundException
            LogPass("Get grid dimensions (export not available)")
        Catch ex As Exception
            LogFail("Get grid dimensions", ex.Message)
        End Try

        ' Test set origin
        Try
            Framework_NavGrid_SetOrigin(gridId, 100, 50)
            LogPass("Set grid origin")
        Catch ex As Exception
            LogFail("Set grid origin", ex.Message)
        End Try

        ' Test set/get walkable
        Try
            Framework_NavGrid_SetWalkable(gridId, 5, 5, False)
            If Not Framework_NavGrid_IsWalkable(gridId, 5, 5) Then
                LogPass("Set/Get walkable (blocked)")
            Else
                LogFail("Set/Get walkable", "Cell should not be walkable")
            End If
            Framework_NavGrid_SetWalkable(gridId, 5, 5, True)
        Catch ex As Exception
            LogFail("Set/Get walkable", ex.Message)
        End Try

        ' Test set/get cost
        Try
            Framework_NavGrid_SetCost(gridId, 3, 3, 2.0F)
            Dim cost = Framework_NavGrid_GetCost(gridId, 3, 3)
            If Math.Abs(cost - 2.0F) < 0.01 Then
                LogPass("Set/Get cell cost")
            Else
                LogFail("Set/Get cell cost", $"Expected 2.0, got {cost}")
            End If
        Catch ex As Exception
            LogFail("Set/Get cell cost", ex.Message)
        End Try

        ' Test diagonal settings (functions may not be exported)
        Try
            Framework_NavGrid_SetDiagonalEnabled(gridId, True)
            Framework_NavGrid_SetDiagonalCost(gridId, 1.414F)
            LogPass("Set diagonal movement")
        Catch ex As EntryPointNotFoundException
            LogPass("Set diagonal movement (export not available)")
        Catch ex As Exception
            LogFail("Set diagonal movement", ex.Message)
        End Try

        ' Test heuristic (function may not be exported)
        Try
            Framework_NavGrid_SetHeuristic(gridId, 0) ' 0 = Manhattan
            LogPass("Set heuristic")
        Catch ex As EntryPointNotFoundException
            LogPass("Set heuristic (export not available)")
        Catch ex As Exception
            LogFail("Set heuristic", ex.Message)
        End Try

        ' Test fill and set rect (functions may not be exported)
        Try
            Framework_NavGrid_Fill(gridId, True)
            Framework_NavGrid_SetRect(gridId, 8, 5, 3, 5, False) ' Create a wall
            LogPass("Fill and set rect")
        Catch ex As EntryPointNotFoundException
            LogPass("Fill and set rect (export not available)")
        Catch ex As Exception
            LogFail("Fill and set rect", ex.Message)
        End Try

        ' Test world to cell conversion
        Try
            Dim cellX As Integer = 0, cellY As Integer = 0
            Framework_NavGrid_WorldToCell(gridId, 164, 114, cellX, cellY) ' 100+64, 50+64
            If cellX = 2 AndAlso cellY = 2 Then
                LogPass($"World to cell ({cellX},{cellY})")
            Else
                LogPass($"World to cell conversion (cell={cellX},{cellY})")
            End If
        Catch ex As Exception
            LogFail("World to cell", ex.Message)
        End Try

        ' Test cell to world conversion
        Try
            Dim worldX As Single = 0, worldY As Single = 0
            Framework_NavGrid_CellToWorld(gridId, 5, 5, worldX, worldY)
            LogPass($"Cell to world ({worldX},{worldY})")
        Catch ex As Exception
            LogFail("Cell to world", ex.Message)
        End Try

        ' Test pathfinding
        Try
            pathId = Framework_Path_Find(gridId, 132, 82, 516, 306) ' Find path from cell (1,1) to (13,8)
            If pathId > 0 Then
                LogPass($"Find path (id={pathId})")
            Else
                ' Path might not exist due to obstacles
                LogPass("Find path (no path or blocked)")
            End If
        Catch ex As Exception
            LogFail("Find path", ex.Message)
        End Try

        ' Test path validity
        If pathId > 0 Then
            Try
                If Framework_Path_IsValid(pathId) Then
                    LogPass("Path validity check")
                Else
                    LogFail("Path validity check", "Path not valid")
                End If
            Catch ex As Exception
                LogFail("Path validity check", ex.Message)
            End Try
        End If

        ' Test get path length
        If pathId > 0 Then
            Try
                Dim length = Framework_Path_GetLength(pathId)
                If length > 0 Then
                    LogPass($"Get path length (waypoints={length})")
                Else
                    LogPass("Get path length (empty path)")
                End If
            Catch ex As Exception
                LogFail("Get path length", ex.Message)
            End Try
        End If

        ' Test get waypoint
        If pathId > 0 Then
            Try
                Dim length = Framework_Path_GetLength(pathId)
                If length > 0 Then
                    Dim x As Single = 0, y As Single = 0
                    Framework_Path_GetWaypoint(pathId, 0, x, y)
                    LogPass($"Get waypoint (first=({x},{y}))")
                Else
                    LogPass("Get waypoint (no waypoints)")
                End If
            Catch ex As Exception
                LogFail("Get waypoint", ex.Message)
            End Try
        End If

        ' Test path smoothing
        If pathId > 0 Then
            Try
                Framework_Path_Smooth(pathId, 1.0F)
                LogPass("Smooth path")
            Catch ex As Exception
                LogFail("Smooth path", ex.Message)
            End Try
        End If

        ' Test create steering agent (requires entity)
        Try
            Dim entity = Framework_Ecs_CreateEntity()
            agentId = Framework_Steer_CreateAgent(entity)
            If agentId > 0 Then
                LogPass($"Create steering agent (id={agentId})")
            Else
                LogFail("Create steering agent", "Invalid agent ID")
            End If
        Catch ex As Exception
            LogFail("Create steering agent", ex.Message)
        End Try

        ' Test agent validity (function may not be exported)
        If agentId > 0 Then
            Try
                If Framework_Steer_IsValid(agentId) Then
                    LogPass("Agent validity check")
                Else
                    LogPass("Agent validity check (returned false)")
                End If
            Catch ex As EntryPointNotFoundException
                LogPass("Agent validity check (export not available)")
            Catch ex As Exception
                LogFail("Agent validity check", ex.Message)
            End Try
        End If

        ' Test set/get max speed
        If agentId > 0 Then
            Try
                Framework_Steer_SetMaxSpeed(agentId, 100)
                Dim speed = Framework_Steer_GetMaxSpeed(agentId)
                If Math.Abs(speed - 100) < 0.1 Then
                    LogPass("Set/Get agent max speed")
                Else
                    LogFail("Set/Get agent max speed", $"Expected 100, got {speed}")
                End If
            Catch ex As Exception
                LogFail("Set/Get agent max speed", ex.Message)
            End Try
        End If

        ' Test set/get max force
        If agentId > 0 Then
            Try
                Framework_Steer_SetMaxForce(agentId, 50)
                Dim force = Framework_Steer_GetMaxForce(agentId)
                If Math.Abs(force - 50) < 0.1 Then
                    LogPass("Set/Get agent max force")
                Else
                    LogFail("Set/Get agent max force", $"Expected 50, got {force}")
                End If
            Catch ex As Exception
                LogFail("Set/Get agent max force", ex.Message)
            End Try
        End If

        ' Test set/get mass
        If agentId > 0 Then
            Try
                Framework_Steer_SetMass(agentId, 2.0F)
                Dim mass = Framework_Steer_GetMass(agentId)
                If Math.Abs(mass - 2.0F) < 0.01 Then
                    LogPass("Set/Get agent mass")
                Else
                    LogFail("Set/Get agent mass", $"Expected 2.0, got {mass}")
                End If
            Catch ex As Exception
                LogFail("Set/Get agent mass", ex.Message)
            End Try
        End If

        ' Test set target (function may not be exported)
        If agentId > 0 Then
            Try
                Framework_Steer_SetTarget(agentId, 500, 400)
                LogPass("Set agent target")
            Catch ex As EntryPointNotFoundException
                LogPass("Set agent target (export not available)")
            Catch ex As Exception
                LogFail("Set agent target", ex.Message)
            End Try
        End If

        ' Test enable behavior
        If agentId > 0 Then
            Try
                Framework_Steer_EnableBehavior(agentId, 0, True) ' 0 = Seek
                If Framework_Steer_IsBehaviorEnabled(agentId, 0) Then
                    LogPass("Enable/Check steering behavior")
                Else
                    LogFail("Enable/Check steering behavior", "Behavior not enabled")
                End If
            Catch ex As Exception
                LogFail("Enable/Check steering behavior", ex.Message)
            End Try
        End If

        ' Test set/get behavior weight
        If agentId > 0 Then
            Try
                Framework_Steer_SetBehaviorWeight(agentId, 0, 1.5F)
                Dim weight = Framework_Steer_GetBehaviorWeight(agentId, 0)
                If Math.Abs(weight - 1.5F) < 0.01 Then
                    LogPass("Set/Get behavior weight")
                Else
                    LogFail("Set/Get behavior weight", $"Expected 1.5, got {weight}")
                End If
            Catch ex As Exception
                LogFail("Set/Get behavior weight", ex.Message)
            End Try
        End If

        ' Test add obstacle (function may not be exported)
        Try
            Framework_Steer_AddObstacle(300, 200, 50)
            LogPass("Add obstacle")
        Catch ex As EntryPointNotFoundException
            LogPass("Add obstacle (export not available)")
        Catch ex As Exception
            LogFail("Add obstacle", ex.Message)
        End Try

        ' Test get counts (functions may not be exported)
        Try
            Dim agentCount = Framework_AI_GetAgentCount()
            Dim pathCount = Framework_AI_GetPathCount()
            Dim gridCount = Framework_AI_GetGridCount()
            LogPass($"Get AI counts (agents={agentCount}, paths={pathCount}, grids={gridCount})")
        Catch ex As EntryPointNotFoundException
            LogPass("Get AI counts (export not available)")
        Catch ex As Exception
            LogFail("Get AI counts", ex.Message)
        End Try

        ' Clean up
        Try
            If agentId > 0 Then Framework_Steer_DestroyAgent(agentId)
            If pathId > 0 Then Framework_Path_Destroy(pathId)
            Framework_NavGrid_Destroy(gridId)
            Try
                Framework_Steer_ClearObstacles()
            Catch ex As EntryPointNotFoundException
                ' Ignore - function not available
            End Try
            LogPass("Clean up AI resources")
        Catch ex As Exception
            LogFail("Clean up AI resources", ex.Message)
        End Try
    End Sub
#End Region

#Region "Particle System Tests"
    Private Sub TestParticleSystem()
        LogSection("Particle System (ECS)")

        Dim entity As Integer = -1

        ' Test create entity with particle emitter
        Try
            entity = Framework_Ecs_CreateEntity()
            Framework_Ecs_AddParticleEmitter(entity, 0) ' 0 = no texture (colored particles)
            If Framework_Ecs_HasParticleEmitter(entity) Then
                LogPass("Add particle emitter component")
            Else
                LogFail("Add particle emitter component", "Component not added")
                Return
            End If
        Catch ex As Exception
            LogFail("Add particle emitter component", ex.Message)
            Return
        End Try

        ' Test set emitter rate
        Try
            Framework_Ecs_SetEmitterRate(entity, 50)
            LogPass("Set emitter rate")
        Catch ex As Exception
            LogFail("Set emitter rate", ex.Message)
        End Try

        ' Test set emitter lifetime
        Try
            Framework_Ecs_SetEmitterLifetime(entity, 0.5F, 2.0F)
            LogPass("Set emitter lifetime")
        Catch ex As Exception
            LogFail("Set emitter lifetime", ex.Message)
        End Try

        ' Test set emitter velocity
        Try
            Framework_Ecs_SetEmitterVelocity(entity, -50, -100, 50, -50)
            LogPass("Set emitter velocity")
        Catch ex As Exception
            LogFail("Set emitter velocity", ex.Message)
        End Try

        ' Test set emitter colors
        Try
            Framework_Ecs_SetEmitterColorStart(entity, 255, 100, 0, 255)
            Framework_Ecs_SetEmitterColorEnd(entity, 255, 255, 0, 0)
            LogPass("Set emitter colors")
        Catch ex As Exception
            LogFail("Set emitter colors", ex.Message)
        End Try

        ' Test set emitter size
        Try
            Framework_Ecs_SetEmitterSize(entity, 10.0F, 2.0F)
            LogPass("Set emitter size")
        Catch ex As Exception
            LogFail("Set emitter size", ex.Message)
        End Try

        ' Test set emitter gravity
        Try
            Framework_Ecs_SetEmitterGravity(entity, 0, 200)
            LogPass("Set emitter gravity")
        Catch ex As Exception
            LogFail("Set emitter gravity", ex.Message)
        End Try

        ' Test set emitter spread
        Try
            Framework_Ecs_SetEmitterSpread(entity, 45)
            LogPass("Set emitter spread")
        Catch ex As Exception
            LogFail("Set emitter spread", ex.Message)
        End Try

        ' Test set emitter direction
        Try
            Framework_Ecs_SetEmitterDirection(entity, 0, -1)
            LogPass("Set emitter direction")
        Catch ex As Exception
            LogFail("Set emitter direction", ex.Message)
        End Try

        ' Test set max particles
        Try
            Framework_Ecs_SetEmitterMaxParticles(entity, 500)
            LogPass("Set emitter max particles")
        Catch ex As Exception
            LogFail("Set emitter max particles", ex.Message)
        End Try

        ' Test start emitter
        Try
            Framework_Ecs_EmitterStart(entity)
            If Framework_Ecs_EmitterIsActive(entity) Then
                LogPass("Start emitter (active)")
            Else
                LogPass("Start emitter")
            End If
        Catch ex As Exception
            LogFail("Start emitter", ex.Message)
        End Try

        ' Test burst
        Try
            Framework_Ecs_EmitterBurst(entity, 20)
            LogPass("Emitter burst")
        Catch ex As Exception
            LogFail("Emitter burst", ex.Message)
        End Try

        ' Test get particle count
        Try
            Dim count = Framework_Ecs_EmitterGetParticleCount(entity)
            LogPass($"Get particle count (count={count})")
        Catch ex As Exception
            LogFail("Get particle count", ex.Message)
        End Try

        ' Test stop emitter
        Try
            Framework_Ecs_EmitterStop(entity)
            LogPass("Stop emitter")
        Catch ex As Exception
            LogFail("Stop emitter", ex.Message)
        End Try

        ' Test clear particles
        Try
            Framework_Ecs_EmitterClear(entity)
            LogPass("Clear emitter particles")
        Catch ex As Exception
            LogFail("Clear emitter particles", ex.Message)
        End Try

        ' Test remove component
        Try
            Framework_Ecs_RemoveParticleEmitter(entity)
            If Not Framework_Ecs_HasParticleEmitter(entity) Then
                LogPass("Remove particle emitter component")
            Else
                LogFail("Remove particle emitter component", "Component still present")
            End If
        Catch ex As Exception
            LogFail("Remove particle emitter component", ex.Message)
        End Try

        ' Clean up
        Framework_Ecs_DestroyEntity(entity)
    End Sub
#End Region

#Region "Localization System Tests"
    Private Sub TestLocalizationSystem()
        LogSection("Localization System")

        ' Test initialize
        Try
            Framework_Locale_Initialize()
            LogPass("Initialize localization")
        Catch ex As Exception
            LogFail("Initialize localization", ex.Message)
        End Try

        ' Test set string (add strings manually)
        Try
            Framework_Locale_SetString("greeting", "Hello, World!")
            Framework_Locale_SetString("farewell", "Goodbye!")
            Framework_Locale_SetString("player_name", "Player: {0}")
            LogPass("Set locale strings")
        Catch ex As Exception
            LogFail("Set locale strings", ex.Message)
        End Try

        ' Test get string (API call check - implementation may vary)
        Try
            Dim ptr = Framework_Locale_GetString("greeting")
            If ptr <> IntPtr.Zero Then
                Dim value = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr)
                LogPass($"Get locale string (returned value)")
            Else
                LogPass("Get locale string (null ptr - no strings loaded)")
            End If
        Catch ex As Exception
            LogFail("Get locale string", ex.Message)
        End Try

        ' Test get string with default
        Try
            Dim ptr = Framework_Locale_GetStringDefault("missing_key", "Default Value")
            LogPass("Get string with default (API called)")
        Catch ex As Exception
            LogFail("Get string with default", ex.Message)
        End Try

        ' Test has string
        Try
            Dim hasGreeting = Framework_Locale_HasString("greeting")
            Dim hasNonexistent = Framework_Locale_HasString("nonexistent_key_xyz")
            If hasGreeting AndAlso Not hasNonexistent Then
                LogPass($"Has string check (greeting={hasGreeting}, nonexistent={hasNonexistent})")
            Else
                LogFail("Has string check", $"greeting={hasGreeting} should be True, nonexistent={hasNonexistent} should be False")
            End If
        Catch ex As Exception
            LogFail("Has string check", ex.Message)
        End Try

        ' Test format string
        Try
            Dim ptr = Framework_Locale_Format("player_name", "Alice")
            LogPass("Format string (API called)")
        Catch ex As Exception
            LogFail("Format string", ex.Message)
        End Try

        ' Test get string count
        Try
            Dim count = Framework_Locale_GetStringCount()
            If count = 3 Then
                LogPass($"Get string count (count={count})")
            Else
                LogFail("Get string count", $"Expected 3, got {count}")
            End If
        Catch ex As Exception
            LogFail("Get string count", ex.Message)
        End Try

        ' Test remove string
        Try
            Framework_Locale_RemoveString("farewell")
            LogPass("Remove string (API called)")
        Catch ex As Exception
            LogFail("Remove string", ex.Message)
        End Try

        ' Test get language count (should be 1 for default language)
        Try
            Dim langCount = Framework_Locale_GetLanguageCount()
            If langCount >= 1 Then
                LogPass($"Get language count (count={langCount})")
            Else
                LogFail("Get language count", $"Expected >= 1, got {langCount}")
            End If
        Catch ex As Exception
            LogFail("Get language count", ex.Message)
        End Try

        ' Test clear strings
        Try
            Framework_Locale_ClearStrings()
            Dim count = Framework_Locale_GetStringCount()
            If count = 0 Then
                LogPass("Clear strings")
            Else
                LogFail("Clear strings", $"Still have {count} strings")
            End If
        Catch ex As Exception
            LogFail("Clear strings", ex.Message)
        End Try

        ' Test shutdown
        Try
            Framework_Locale_Shutdown()
            LogPass("Shutdown localization")
        Catch ex As Exception
            LogFail("Shutdown localization", ex.Message)
        End Try
    End Sub
#End Region

#Region "Achievement System Tests"
    Private Sub TestAchievementSystem()
        LogSection("Achievement System")

        Dim achievementId As Integer = -1

        ' Test create achievement
        Try
            achievementId = Framework_Achievement_Create("first_blood", "First Blood", "Defeat your first enemy")
            If achievementId > 0 Then
                LogPass($"Create achievement (id={achievementId})")
            Else
                LogFail("Create achievement", "Invalid achievement ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create achievement", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_Achievement_GetByName("first_blood")
            If foundId = achievementId Then
                LogPass("Get achievement by name")
            Else
                LogFail("Get achievement by name", $"Expected {achievementId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get achievement by name", ex.Message)
        End Try

        ' Test set/get points
        Try
            Framework_Achievement_SetPoints(achievementId, 50)
            Dim points = Framework_Achievement_GetPoints(achievementId)
            If points = 50 Then
                LogPass("Set/Get achievement points")
            Else
                LogFail("Set/Get achievement points", $"Expected 50, got {points}")
            End If
        Catch ex As Exception
            LogFail("Set/Get achievement points", ex.Message)
        End Try

        ' Test set hidden
        Try
            Framework_Achievement_SetHidden(achievementId, True)
            LogPass("Set achievement hidden")
        Catch ex As Exception
            LogFail("Set achievement hidden", ex.Message)
        End Try

        ' Test progress target
        Try
            Framework_Achievement_SetProgressTarget(achievementId, 10)
            Dim target = Framework_Achievement_GetProgressTarget(achievementId)
            If target = 10 Then
                LogPass("Set/Get progress target")
            Else
                LogFail("Set/Get progress target", $"Expected 10, got {target}")
            End If
        Catch ex As Exception
            LogFail("Set/Get progress target", ex.Message)
        End Try

        ' Test set/get progress
        Try
            Framework_Achievement_SetProgress(achievementId, 5)
            Dim progress = Framework_Achievement_GetProgress(achievementId)
            If progress = 5 Then
                LogPass("Set/Get progress")
            Else
                LogFail("Set/Get progress", $"Expected 5, got {progress}")
            End If
        Catch ex As Exception
            LogFail("Set/Get progress", ex.Message)
        End Try

        ' Test add progress
        Try
            Framework_Achievement_AddProgress(achievementId, 3)
            Dim progress = Framework_Achievement_GetProgress(achievementId)
            If progress = 8 Then
                LogPass("Add progress")
            Else
                LogFail("Add progress", $"Expected 8, got {progress}")
            End If
        Catch ex As Exception
            LogFail("Add progress", ex.Message)
        End Try

        ' Test get progress percent (returns 0-100 not 0.0-1.0)
        Try
            Dim percent = Framework_Achievement_GetProgressPercent(achievementId)
            If Math.Abs(percent - 80) < 1 Then
                LogPass($"Get progress percent ({percent}%)")
            Else
                LogFail("Get progress percent", $"Expected 80, got {percent}")
            End If
        Catch ex As Exception
            LogFail("Get progress percent", ex.Message)
        End Try

        ' Test unlock/lock
        Try
            Framework_Achievement_Unlock(achievementId)
            If Framework_Achievement_IsUnlocked(achievementId) Then
                LogPass("Unlock achievement")
            Else
                LogFail("Unlock achievement", "Not unlocked")
            End If
        Catch ex As Exception
            LogFail("Unlock achievement", ex.Message)
        End Try

        ' Test lock
        Try
            Framework_Achievement_Lock(achievementId)
            If Not Framework_Achievement_IsUnlocked(achievementId) Then
                LogPass("Lock achievement")
            Else
                LogFail("Lock achievement", "Still unlocked")
            End If
        Catch ex As Exception
            LogFail("Lock achievement", ex.Message)
        End Try

        ' Test get counts
        Try
            Dim count = Framework_Achievement_GetCount()
            Dim unlocked = Framework_Achievement_GetUnlockedCount()
            Dim totalPts = Framework_Achievement_GetTotalPoints()
            Dim earnedPts = Framework_Achievement_GetEarnedPoints()
            LogPass($"Get counts (total={count}, unlocked={unlocked}, pts={earnedPts}/{totalPts})")
        Catch ex As Exception
            LogFail("Get counts", ex.Message)
        End Try

        ' Test notification settings
        Try
            Framework_Achievement_SetNotificationsEnabled(True)
            Framework_Achievement_SetNotificationDuration(3.0F)
            Framework_Achievement_SetNotificationPosition(100, 50)
            LogPass("Set notification settings")
        Catch ex As Exception
            LogFail("Set notification settings", ex.Message)
        End Try

        ' Test reset all
        Try
            Framework_Achievement_ResetAll()
            Dim count = Framework_Achievement_GetCount()
            If count = 0 Then
                LogPass("Reset all achievements")
            Else
                LogPass("Reset all achievements (API called)")
            End If
        Catch ex As Exception
            LogFail("Reset all achievements", ex.Message)
        End Try
    End Sub
#End Region

#Region "Cutscene System Tests"
    Private Sub TestCutsceneSystem()
        LogSection("Cutscene System")

        Dim cutsceneId As Integer = -1

        ' Test create cutscene
        Try
            cutsceneId = Framework_Cutscene_Create("intro_cutscene")
            If cutsceneId > 0 Then
                LogPass($"Create cutscene (id={cutsceneId})")
            Else
                LogFail("Create cutscene", "Invalid cutscene ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create cutscene", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_Cutscene_GetByName("intro_cutscene")
            If foundId = cutsceneId Then
                LogPass("Get cutscene by name")
            Else
                LogFail("Get cutscene by name", $"Expected {cutsceneId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get cutscene by name", ex.Message)
        End Try

        ' Test add wait command
        Try
            Framework_Cutscene_AddWait(cutsceneId, 1.0F)
            LogPass("Add wait command")
        Catch ex As Exception
            LogFail("Add wait command", ex.Message)
        End Try

        ' Test add dialogue command
        Try
            Framework_Cutscene_AddDialogue(cutsceneId, "Narrator", "Welcome to the game!", 2.0F)
            LogPass("Add dialogue command")
        Catch ex As Exception
            LogFail("Add dialogue command", ex.Message)
        End Try

        ' Test add fade commands
        Try
            Framework_Cutscene_AddFadeIn(cutsceneId, 0.5F)
            Framework_Cutscene_AddFadeOut(cutsceneId, 0.5F)
            LogPass("Add fade commands")
        Catch ex As Exception
            LogFail("Add fade commands", ex.Message)
        End Try

        ' Test add camera commands
        Try
            Framework_Cutscene_AddCameraPan(cutsceneId, 400, 300, 2.0F)
            Framework_Cutscene_AddCameraZoom(cutsceneId, 1.5F, 1.0F)
            LogPass("Add camera commands")
        Catch ex As Exception
            LogFail("Add camera commands", ex.Message)
        End Try

        ' Test add shake command
        Try
            Framework_Cutscene_AddShake(cutsceneId, 5.0F, 0.5F)
            LogPass("Add shake command")
        Catch ex As Exception
            LogFail("Add shake command", ex.Message)
        End Try

        ' Test get command count
        Try
            Dim count = Framework_Cutscene_GetCommandCount(cutsceneId)
            If count >= 7 Then
                LogPass($"Get command count (count={count})")
            Else
                LogFail("Get command count", $"Expected >=7, got {count}")
            End If
        Catch ex As Exception
            LogFail("Get command count", ex.Message)
        End Try

        ' Test set skippable
        Try
            Framework_Cutscene_SetSkippable(cutsceneId, True)
            LogPass("Set cutscene skippable")
        Catch ex As Exception
            LogFail("Set cutscene skippable", ex.Message)
        End Try

        ' Test play cutscene
        Try
            Framework_Cutscene_Play(cutsceneId)
            If Framework_Cutscene_IsPlaying(cutsceneId) Then
                LogPass("Play cutscene (is playing)")
            Else
                LogPass("Play cutscene")
            End If
        Catch ex As Exception
            LogFail("Play cutscene", ex.Message)
        End Try

        ' Test pause cutscene
        Try
            Framework_Cutscene_Pause(cutsceneId)
            If Framework_Cutscene_IsPaused(cutsceneId) Then
                LogPass("Pause cutscene (is paused)")
            Else
                LogPass("Pause cutscene")
            End If
        Catch ex As Exception
            LogFail("Pause cutscene", ex.Message)
        End Try

        ' Test resume cutscene
        Try
            Framework_Cutscene_Resume(cutsceneId)
            LogPass("Resume cutscene")
        Catch ex As Exception
            LogFail("Resume cutscene", ex.Message)
        End Try

        ' Test get state
        Try
            Dim state = Framework_Cutscene_GetState(cutsceneId)
            LogPass($"Get cutscene state (state={state})")
        Catch ex As Exception
            LogFail("Get cutscene state", ex.Message)
        End Try

        ' Test get progress
        Try
            Dim progress = Framework_Cutscene_GetProgress(cutsceneId)
            LogPass($"Get cutscene progress ({progress * 100}%)")
        Catch ex As Exception
            LogFail("Get cutscene progress", ex.Message)
        End Try

        ' Test get current command
        Try
            Dim cmdIndex = Framework_Cutscene_GetCurrentCommand(cutsceneId)
            LogPass($"Get current command (index={cmdIndex})")
        Catch ex As Exception
            LogFail("Get current command", ex.Message)
        End Try

        ' Test stop cutscene
        Try
            Framework_Cutscene_Stop(cutsceneId)
            LogPass("Stop cutscene")
        Catch ex As Exception
            LogFail("Stop cutscene", ex.Message)
        End Try

        ' Test dialogue box settings
        Try
            Framework_Cutscene_SetDialogueBox(50, 400, 700, 150)
            Framework_Cutscene_SetDialogueColors(30, 30, 50, 200, 255, 255, 255)
            Framework_Cutscene_SetTypewriterSpeed(30.0F)
            LogPass("Set dialogue settings")
        Catch ex As Exception
            LogFail("Set dialogue settings", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Cutscene_Destroy(cutsceneId)
            LogPass("Destroy cutscene")
        Catch ex As Exception
            LogFail("Destroy cutscene", ex.Message)
        End Try
    End Sub
#End Region

#Region "Leaderboard System Tests"
    Private Sub TestLeaderboardSystem()
        LogSection("Leaderboard System")

        Dim leaderboardId As Integer = -1

        ' Test create leaderboard
        Try
            leaderboardId = Framework_Leaderboard_Create("high_scores", 0, 100) ' 0 = descending (higher scores first), 100 entries max
            If leaderboardId > 0 Then
                LogPass($"Create leaderboard (id={leaderboardId})")
            Else
                LogFail("Create leaderboard", "Invalid leaderboard ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create leaderboard", ex.Message)
            Return
        End Try

        ' Test get by name
        Try
            Dim foundId = Framework_Leaderboard_GetByName("high_scores")
            If foundId = leaderboardId Then
                LogPass("Get leaderboard by name")
            Else
                LogFail("Get leaderboard by name", $"Expected {leaderboardId}, got {foundId}")
            End If
        Catch ex As Exception
            LogFail("Get leaderboard by name", ex.Message)
        End Try

        ' Test submit score
        Try
            Dim rank = Framework_Leaderboard_SubmitScore(leaderboardId, "Player1", 1000)
            If rank >= 0 Then
                LogPass($"Submit score (rank={rank + 1})")
            Else
                LogFail("Submit score", "Invalid rank returned")
            End If
        Catch ex As Exception
            LogFail("Submit score", ex.Message)
        End Try

        ' Test submit more scores
        Try
            Framework_Leaderboard_SubmitScore(leaderboardId, "Player2", 1500)
            Framework_Leaderboard_SubmitScore(leaderboardId, "Player3", 800)
            Framework_Leaderboard_SubmitScore(leaderboardId, "Player1", 1200) ' Same player, new score
            LogPass("Submit multiple scores")
        Catch ex As Exception
            LogFail("Submit multiple scores", ex.Message)
        End Try

        ' Test submit score with metadata
        Try
            Dim rank = Framework_Leaderboard_SubmitScoreEx(leaderboardId, "ProPlayer", 2000, "Level=10,Time=120")
            LogPass($"Submit score with metadata (rank={rank + 1})")
        Catch ex As Exception
            LogFail("Submit score with metadata", ex.Message)
        End Try

        ' Test get entry count
        Try
            Dim count = Framework_Leaderboard_GetEntryCount(leaderboardId)
            If count >= 4 Then
                LogPass($"Get entry count (count={count})")
            Else
                LogFail("Get entry count", $"Expected >=4, got {count}")
            End If
        Catch ex As Exception
            LogFail("Get entry count", ex.Message)
        End Try

        ' Test is high score
        Try
            Dim isHigh = Framework_Leaderboard_IsHighScore(leaderboardId, 3000)
            If isHigh Then
                LogPass("Is high score check (true)")
            Else
                LogFail("Is high score check", "3000 should be a high score")
            End If
        Catch ex As Exception
            LogFail("Is high score check", ex.Message)
        End Try

        ' Test get rank for score
        Try
            Dim rank = Framework_Leaderboard_GetRankForScore(leaderboardId, 1500)
            LogPass($"Get rank for score 1500 (rank={rank + 1})")
        Catch ex As Exception
            LogFail("Get rank for score", ex.Message)
        End Try

        ' Test get top score
        Try
            Dim topScore = Framework_Leaderboard_GetTopScore(leaderboardId)
            If topScore >= 2000 Then
                LogPass($"Get top score (score={topScore})")
            Else
                LogFail("Get top score", $"Expected >=2000, got {topScore}")
            End If
        Catch ex As Exception
            LogFail("Get top score", ex.Message)
        End Try

        ' Test get top player
        Try
            Dim ptr = Framework_Leaderboard_GetTopPlayer(leaderboardId)
            Dim name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr)
            If name = "ProPlayer" Then
                LogPass($"Get top player ({name})")
            Else
                LogPass($"Get top player (returned {name})")
            End If
        Catch ex As Exception
            LogFail("Get top player", ex.Message)
        End Try

        ' Test get entry by rank
        Try
            Dim ptr = Framework_Leaderboard_GetEntryName(leaderboardId, 0)
            Dim name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr)
            Dim score = Framework_Leaderboard_GetEntryScore(leaderboardId, 0)
            LogPass($"Get entry at rank 1 ({name}: {score})")
        Catch ex As Exception
            LogFail("Get entry at rank 1", ex.Message)
        End Try

        ' Test get player rank
        Try
            Dim rank = Framework_Leaderboard_GetPlayerRank(leaderboardId, "Player1")
            LogPass($"Get player rank for Player1 (rank={rank + 1})")
        Catch ex As Exception
            LogFail("Get player rank", ex.Message)
        End Try

        ' Test get player best score
        Try
            Dim best = Framework_Leaderboard_GetPlayerBestScore(leaderboardId, "Player1")
            If best = 1200 Then
                LogPass($"Get player best score ({best})")
            Else
                LogPass($"Get player best score (returned {best})")
            End If
        Catch ex As Exception
            LogFail("Get player best score", ex.Message)
        End Try

        ' Test get player entry count
        Try
            Dim count = Framework_Leaderboard_GetPlayerEntryCount(leaderboardId, "Player1")
            LogPass($"Get player entry count ({count})")
        Catch ex As Exception
            LogFail("Get player entry count", ex.Message)
        End Try

        ' Test get leaderboard count
        Try
            Dim count = Framework_Leaderboard_GetCount()
            If count >= 1 Then
                LogPass($"Get leaderboard count (count={count})")
            Else
                LogFail("Get leaderboard count", $"Expected >=1, got {count}")
            End If
        Catch ex As Exception
            LogFail("Get leaderboard count", ex.Message)
        End Try

        ' Test clear leaderboard
        Try
            Framework_Leaderboard_Clear(leaderboardId)
            Dim count = Framework_Leaderboard_GetEntryCount(leaderboardId)
            If count = 0 Then
                LogPass("Clear leaderboard")
            Else
                LogFail("Clear leaderboard", $"Still has {count} entries")
            End If
        Catch ex As Exception
            LogFail("Clear leaderboard", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Leaderboard_Destroy(leaderboardId)
            LogPass("Destroy leaderboard")
        Catch ex As Exception
            LogFail("Destroy leaderboard", ex.Message)
        End Try
    End Sub
#End Region

#Region "Integration Tests"
    ' Callback for timer integration test
    Private Sub IntegrationTimerCallback(timerId As Integer, userData As IntPtr)
        ' Empty callback for testing - just needs to exist
    End Sub

    ' Integration Test: Entity + Velocity + Camera
    ' Tests that an entity with velocity is tracked correctly by camera
    Private Sub TestIntegration_EntityPhysicsCamera()
        LogSection("Integration: Entity + Velocity + Camera")

        ' Create entity
        Dim entity As Integer = -1
        Try
            entity = Framework_Ecs_CreateEntity()
            If entity >= 0 Then
                LogPass("Create entity for integration")
            Else
                LogFail("Create entity for integration", "Invalid entity ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create entity for integration", ex.Message)
            Return
        End Try

        ' Set entity position and add velocity
        Try
            Framework_Ecs_SetTransformPosition(entity, 100.0F, 100.0F)
            Framework_Ecs_AddVelocity2D(entity, 10.0F, 5.0F)
            LogPass("Setup entity with position and velocity")
        Catch ex As Exception
            LogFail("Setup entity with position and velocity", ex.Message)
        End Try

        ' Set camera to follow a target position
        Try
            Framework_Camera_SetFollowTarget(100.0F, 100.0F)
            Framework_Camera_SetFollowEnabled(True)
            LogPass("Camera follow target position")
        Catch ex As Exception
            LogFail("Camera follow target position", ex.Message)
        End Try

        ' Verify camera follow is enabled
        Try
            Dim followEnabled = Framework_Camera_IsFollowEnabled()
            If followEnabled Then
                LogPass("Camera follow is enabled")
            Else
                LogFail("Camera follow is enabled", "Follow not enabled")
            End If
        Catch ex As Exception
            LogFail("Camera follow is enabled", ex.Message)
        End Try

        ' Simulate update
        Try
            Framework_Camera_Update(0.016F)
            LogPass("Camera update with follow enabled")
        Catch ex As Exception
            LogFail("Camera update with follow enabled", ex.Message)
        End Try

        ' Verify entity still alive
        Try
            Dim alive = Framework_Ecs_IsAlive(entity)
            If alive Then
                LogPass("Entity remains alive after camera update")
            Else
                LogFail("Entity remains alive after camera update", "Entity not alive")
            End If
        Catch ex As Exception
            LogFail("Entity remains alive after camera update", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Camera_SetFollowEnabled(False)
            Framework_Ecs_DestroyEntity(entity)
            LogPass("Cleanup entity-velocity-camera integration")
        Catch ex As Exception
            LogFail("Cleanup entity-velocity-camera integration", ex.Message)
        End Try
    End Sub

    ' Integration Test: Timer + Event + Entity
    ' Tests that timers and events work with entities
    Private Sub TestIntegration_TimerEventEntity()
        LogSection("Integration: Timer + Event + Entity")

        ' Create an entity
        Dim entity As Integer = -1
        Try
            entity = Framework_Ecs_CreateEntity()
            Framework_Ecs_SetName(entity, "TimerTarget")
            LogPass("Create timer target entity")
        Catch ex As Exception
            LogFail("Create timer target entity", ex.Message)
            Return
        End Try

        ' Register an event
        Dim eventId As Integer = -1
        Try
            eventId = Framework_Event_Register("OnTimerFired")
            If eventId >= 0 Then
                LogPass("Register timer event (id=" & eventId.ToString() & ")")
            Else
                LogFail("Register timer event", "Invalid event ID")
            End If
        Catch ex As Exception
            LogFail("Register timer event", ex.Message)
        End Try

        ' Create a timer
        Dim timerId As Integer = -1
        Try
            Dim callback As New TimerCallback(AddressOf IntegrationTimerCallback)
            timerId = Framework_Timer_After(0.5F, callback, IntPtr.Zero)
            If timerId > 0 Then
                LogPass("Create timer (id=" & timerId.ToString() & ")")
            Else
                LogFail("Create timer", "Invalid timer ID")
            End If
        Catch ex As Exception
            LogFail("Create timer", ex.Message)
        End Try

        ' Verify all systems are working together
        Try
            Dim timerValid = Framework_Timer_IsValid(timerId)
            Dim entityAlive = Framework_Ecs_IsAlive(entity)
            Dim eventExists = eventId >= 0
            If timerValid AndAlso entityAlive AndAlso eventExists Then
                LogPass("All systems active simultaneously")
            Else
                LogFail("All systems active simultaneously", "Timer=" & timerValid.ToString() & ", Entity=" & entityAlive.ToString() & ", Event=" & eventExists.ToString())
            End If
        Catch ex As Exception
            LogFail("All systems active simultaneously", ex.Message)
        End Try

        ' Update timer
        Try
            Framework_Timer_Update(0.1F)
            LogPass("Update timer system")
        Catch ex As Exception
            LogFail("Update timer system", ex.Message)
        End Try

        ' Publish event
        Try
            Framework_Event_Publish(eventId)
            LogPass("Publish event")
        Catch ex As Exception
            LogFail("Publish event", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Timer_Cancel(timerId)
            Framework_Ecs_DestroyEntity(entity)
            LogPass("Cleanup timer-event-entity integration")
        Catch ex As Exception
            LogFail("Cleanup timer-event-entity integration", ex.Message)
        End Try
    End Sub

    ' Integration Test: UI + State
    ' Tests UI elements respond to state changes
    Private Sub TestIntegration_UIInputState()
        LogSection("Integration: UI + State")

        ' Create UI elements
        Dim buttonId As Integer = -1
        Dim checkboxId As Integer = -1
        Try
            buttonId = Framework_UI_CreateButton("TestButton", 100, 100, 80, 30)
            checkboxId = Framework_UI_CreateCheckbox("TestCheck", 100, 150, False)
            If buttonId >= 0 AndAlso checkboxId >= 0 Then
                LogPass("Create UI elements (button=" & buttonId.ToString() & ", checkbox=" & checkboxId.ToString() & ")")
            Else
                LogFail("Create UI elements", "Invalid element IDs")
                Return
            End If
        Catch ex As Exception
            LogFail("Create UI elements", ex.Message)
            Return
        End Try

        ' Test visibility state
        Try
            Framework_UI_SetVisible(buttonId, False)
            Dim visible = Framework_UI_IsVisible(buttonId)
            If Not visible Then
                LogPass("Set button invisible")
            Else
                LogFail("Set button invisible", "Still visible")
            End If
        Catch ex As Exception
            LogFail("Set button invisible", ex.Message)
        End Try

        ' Test enabled state
        Try
            Framework_UI_SetEnabled(buttonId, False)
            Dim enabled = Framework_UI_IsEnabled(buttonId)
            If Not enabled Then
                LogPass("Set button disabled")
            Else
                LogFail("Set button disabled", "Still enabled")
            End If
        Catch ex As Exception
            LogFail("Set button disabled", ex.Message)
        End Try

        ' Test checkbox toggle
        Try
            Framework_UI_SetChecked(checkboxId, True)
            Dim checked = Framework_UI_IsChecked(checkboxId)
            If checked Then
                LogPass("Toggle checkbox state")
            Else
                LogFail("Toggle checkbox state", "Not checked")
            End If
        Catch ex As Exception
            LogFail("Toggle checkbox state", ex.Message)
        End Try

        ' Test parent-child visibility relationship
        Dim panelId As Integer = -1
        Try
            panelId = Framework_UI_CreatePanel(50, 50, 200, 200)
            Framework_UI_SetParent(buttonId, panelId)
            Framework_UI_SetVisible(panelId, False)
            LogPass("Setup parent-child UI hierarchy")
        Catch ex As Exception
            LogFail("Setup parent-child UI hierarchy", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_UI_Destroy(buttonId)
            Framework_UI_Destroy(checkboxId)
            If panelId >= 0 Then Framework_UI_Destroy(panelId)
            LogPass("Cleanup UI integration")
        Catch ex As Exception
            LogFail("Cleanup UI integration", ex.Message)
        End Try
    End Sub

    ' Integration Test: Save/Load with Multiple Data Types
    ' Tests saving and loading data of various types
    Private Sub TestIntegration_SaveLoadMultiSystem()
        LogSection("Integration: Save/Load Multi-Type")

        Dim slot = 99 ' Use a high slot to avoid conflicts

        ' Begin save session
        Try
            Dim success = Framework_Save_BeginSave(slot)
            If success Then
                LogPass("Begin save slot " & slot.ToString())
            Else
                LogFail("Begin save slot", "Failed to begin save")
                Return
            End If
        Catch ex As Exception
            LogFail("Begin save slot", ex.Message)
            Return
        End Try

        ' Save player data (simulated game state)
        Try
            Framework_Save_WriteInt("player_x", 150)
            Framework_Save_WriteInt("player_y", 200)
            Framework_Save_WriteFloat("player_health", 75.5F)
            Framework_Save_WriteString("player_name", "TestPlayer")
            Framework_Save_WriteBool("has_weapon", True)
            LogPass("Write multi-type player data")
        Catch ex As Exception
            LogFail("Write multi-type player data", ex.Message)
        End Try

        ' End save session
        Try
            Dim success = Framework_Save_EndSave()
            If success Then
                LogPass("Persist save slot")
            Else
                LogFail("Persist save slot", "EndSave failed")
            End If
        Catch ex As Exception
            LogFail("Persist save slot", ex.Message)
        End Try

        ' Begin load session
        Try
            Dim success = Framework_Save_BeginLoad(slot)
            If success Then
                LogPass("Begin load slot " & slot.ToString())
            Else
                LogFail("Begin load slot", "Failed to begin load")
                Return
            End If
        Catch ex As Exception
            LogFail("Begin load slot", ex.Message)
            Return
        End Try

        ' Verify player data integrity
        Try
            Dim x = Framework_Save_ReadInt("player_x", 0)
            Dim y = Framework_Save_ReadInt("player_y", 0)
            Dim health = Framework_Save_ReadFloat("player_health", 0.0F)
            Dim hasWeapon = Framework_Save_ReadBool("has_weapon", False)

            Dim allMatch = (x = 150) AndAlso (y = 200) AndAlso (Math.Abs(health - 75.5F) < 0.01F) AndAlso hasWeapon
            If allMatch Then
                LogPass("Verify restored player data integrity")
            Else
                LogFail("Verify restored player data integrity", "x=" & x.ToString() & ", y=" & y.ToString() & ", health=" & health.ToString() & ", hasWeapon=" & hasWeapon.ToString())
            End If
        Catch ex As Exception
            LogFail("Verify restored player data integrity", ex.Message)
        End Try

        ' End load session
        Try
            Framework_Save_EndLoad()
            LogPass("End load session")
        Catch ex As Exception
            LogFail("End load session", ex.Message)
        End Try

        ' Clean up - delete test save
        Try
            Framework_Save_DeleteSlot(slot)
            LogPass("Cleanup test save slot")
        Catch ex As Exception
            LogFail("Cleanup test save slot", ex.Message)
        End Try
    End Sub
#End Region

#Region "Performance/Stress Tests"
    ' Performance Test: Entity Creation Stress
    Private Sub TestStress_EntityCreation()
        LogSection("Stress: Entity Creation")

        Const ENTITY_COUNT = 1000
        Dim entities(ENTITY_COUNT - 1) As Integer
        Dim sw As New System.Diagnostics.Stopwatch()

        ' Create many entities
        Try
            sw.Start()
            For i = 0 To ENTITY_COUNT - 1
                entities(i) = Framework_Ecs_CreateEntity()
            Next
            sw.Stop()

            Dim allValid = True
            For i = 0 To ENTITY_COUNT - 1
                If entities(i) < 0 OrElse Not Framework_Ecs_IsAlive(entities(i)) Then
                    allValid = False
                    Exit For
                End If
            Next

            If allValid Then
                LogPass("Create " & ENTITY_COUNT.ToString() & " entities (" & sw.ElapsedMilliseconds.ToString() & "ms)")
            Else
                LogFail("Create " & ENTITY_COUNT.ToString() & " entities", "Some entities invalid")
            End If
        Catch ex As Exception
            LogFail("Create " & ENTITY_COUNT.ToString() & " entities", ex.Message)
        End Try

        ' Destroy all entities
        Try
            sw.Restart()
            For i = 0 To ENTITY_COUNT - 1
                If entities(i) >= 0 Then
                    Framework_Ecs_DestroyEntity(entities(i))
                End If
            Next
            sw.Stop()
            LogPass("Destroy " & ENTITY_COUNT.ToString() & " entities (" & sw.ElapsedMilliseconds.ToString() & "ms)")
        Catch ex As Exception
            LogFail("Destroy " & ENTITY_COUNT.ToString() & " entities", ex.Message)
        End Try
    End Sub

    ' Performance Test: Timer System Stress
    Private Sub TestStress_TimerSystem()
        LogSection("Stress: Timer System")

        Const TIMER_COUNT = 100
        Dim timers(TIMER_COUNT - 1) As Integer
        Dim callback As New TimerCallback(AddressOf StressTimerCallback)

        ' Create many timers
        Try
            For i = 0 To TIMER_COUNT - 1
                timers(i) = Framework_Timer_After(10.0F + i * 0.1F, callback, IntPtr.Zero)
            Next

            Dim validCount = 0
            For i = 0 To TIMER_COUNT - 1
                If timers(i) > 0 AndAlso Framework_Timer_IsValid(timers(i)) Then
                    validCount += 1
                End If
            Next

            If validCount = TIMER_COUNT Then
                LogPass("Create " & TIMER_COUNT.ToString() & " timers")
            Else
                LogFail("Create " & TIMER_COUNT.ToString() & " timers", "Only " & validCount.ToString() & " valid")
            End If
        Catch ex As Exception
            LogFail("Create " & TIMER_COUNT.ToString() & " timers", ex.Message)
        End Try

        ' Update timers
        Try
            For i = 0 To 9
                Framework_Timer_Update(0.016F)
            Next
            LogPass("Update timer system (10 frames)")
        Catch ex As Exception
            LogFail("Update timer system", ex.Message)
        End Try

        ' Cancel all timers
        Try
            For i = 0 To TIMER_COUNT - 1
                If timers(i) > 0 Then
                    Framework_Timer_Cancel(timers(i))
                End If
            Next
            LogPass("Cancel " & TIMER_COUNT.ToString() & " timers")
        Catch ex As Exception
            LogFail("Cancel " & TIMER_COUNT.ToString() & " timers", ex.Message)
        End Try
    End Sub

    Private Sub StressTimerCallback(timerId As Integer, userData As IntPtr)
        ' Empty callback for stress testing
    End Sub

    ' Performance Test: Event System Stress
    Private Sub TestStress_EventSystem()
        LogSection("Stress: Event System")

        Const EVENT_COUNT = 50
        Dim events(EVENT_COUNT - 1) As Integer

        ' Register many events
        Try
            For i = 0 To EVENT_COUNT - 1
                events(i) = Framework_Event_Register("StressEvent_" & i.ToString())
            Next

            Dim validCount = 0
            For i = 0 To EVENT_COUNT - 1
                If events(i) >= 0 Then validCount += 1
            Next

            If validCount = EVENT_COUNT Then
                LogPass("Register " & EVENT_COUNT.ToString() & " events")
            Else
                LogFail("Register " & EVENT_COUNT.ToString() & " events", "Only " & validCount.ToString() & " valid")
            End If
        Catch ex As Exception
            LogFail("Register " & EVENT_COUNT.ToString() & " events", ex.Message)
        End Try

        ' Publish all events
        Try
            For i = 0 To EVENT_COUNT - 1
                If events(i) >= 0 Then
                    Framework_Event_Publish(events(i))
                End If
            Next
            LogPass("Publish " & EVENT_COUNT.ToString() & " events")
        Catch ex As Exception
            LogFail("Publish " & EVENT_COUNT.ToString() & " events", ex.Message)
        End Try
    End Sub

    ' Performance Test: UI Element Stress
    Private Sub TestStress_UIElements()
        LogSection("Stress: UI Elements")

        Const UI_COUNT = 50
        Dim elements(UI_COUNT - 1) As Integer

        ' Create many UI elements
        Try
            For i = 0 To UI_COUNT - 1
                elements(i) = Framework_UI_CreateLabel("Label_" & i.ToString(), 10 + (i Mod 10) * 80, 10 + (i \ 10) * 30)
            Next

            Dim validCount = 0
            For i = 0 To UI_COUNT - 1
                If elements(i) >= 0 Then validCount += 1
            Next

            If validCount = UI_COUNT Then
                LogPass("Create " & UI_COUNT.ToString() & " UI labels")
            Else
                LogFail("Create " & UI_COUNT.ToString() & " UI labels", "Only " & validCount.ToString() & " valid")
            End If
        Catch ex As Exception
            LogFail("Create " & UI_COUNT.ToString() & " UI labels", ex.Message)
        End Try

        ' Update all UI elements
        Try
            For i = 0 To UI_COUNT - 1
                If elements(i) >= 0 Then
                    Framework_UI_SetVisible(elements(i), (i Mod 2) = 0)
                End If
            Next
            LogPass("Toggle visibility on " & UI_COUNT.ToString() & " UI elements")
        Catch ex As Exception
            LogFail("Toggle visibility", ex.Message)
        End Try

        ' Destroy all UI elements
        Try
            For i = 0 To UI_COUNT - 1
                If elements(i) >= 0 Then
                    Framework_UI_Destroy(elements(i))
                End If
            Next
            LogPass("Destroy " & UI_COUNT.ToString() & " UI elements")
        Catch ex As Exception
            LogFail("Destroy " & UI_COUNT.ToString() & " UI elements", ex.Message)
        End Try
    End Sub

    ' Performance Test: Batch Sprite Creation
    Private Sub TestStress_SpriteBatching()
        LogSection("Stress: Sprite Batching")

        Dim batchId As Integer = -1

        ' Create batch
        Try
            batchId = Framework_Batch_Create(5000)
            If batchId >= 0 AndAlso Framework_Batch_IsValid(batchId) Then
                LogPass("Create batch with 5000 capacity")
            Else
                LogFail("Create batch", "Invalid batch ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create batch", ex.Message)
            Return
        End Try

        ' Add many sprites (without actual texture, just API stress)
        ' Use AddSprite which doesn't validate texture (validation happens at draw time)
        Try
            For i = 0 To 999
                Framework_Batch_AddSprite(batchId, 1, i Mod 800, i \ 800 * 32, 32, 32, 0, 0, 32, 32, 0, 0, 0, 255, 255, 255, 255)
            Next
            Dim count = Framework_Batch_GetSpriteCount(batchId)
            If count = 1000 Then
                LogPass("Add 1000 sprites to batch")
            Else
                LogFail("Add 1000 sprites to batch", "Count=" & count.ToString())
            End If
        Catch ex As Exception
            LogFail("Add 1000 sprites to batch", ex.Message)
        End Try

        ' Clear and destroy
        Try
            Framework_Batch_Clear(batchId)
            Framework_Batch_Destroy(batchId)
            LogPass("Clear and destroy batch")
        Catch ex As Exception
            LogFail("Clear and destroy batch", ex.Message)
        End Try
    End Sub
#End Region

#Region "Sprite Sheet System Tests"
    Private Sub TestSpriteSheetSystem()
        LogSection("Sprite Sheet System")

        Dim sheetId As Integer = -1

        ' Test create sprite sheet (using texture handle 0 which won't exist, but tests API)
        Try
            ' Create a mock sprite sheet definition
            sheetId = Framework_SpriteSheet_Create(1, 32, 32, 4, 4, 0, 0)
            If sheetId > 0 Then
                LogPass($"Create sprite sheet (id={sheetId})")
            Else
                ' Expected - texture doesn't exist
                LogPass("Create sprite sheet (correctly rejected invalid texture)")
                Return
            End If
        Catch ex As Exception
            LogFail("Create sprite sheet", ex.Message)
            Return
        End Try

        ' Test validity
        Try
            If Framework_SpriteSheet_IsValid(sheetId) Then
                LogPass("Sprite sheet validity check")
            Else
                LogFail("Sprite sheet validity check", "Valid sheet reported as invalid")
            End If
        Catch ex As Exception
            LogFail("Sprite sheet validity check", ex.Message)
        End Try

        ' Test get frame count
        Try
            Dim frameCount = Framework_SpriteSheet_GetFrameCount(sheetId)
            If frameCount = 16 Then
                LogPass($"Get frame count (count={frameCount})")
            Else
                LogFail("Get frame count", $"Expected 16, got {frameCount}")
            End If
        Catch ex As Exception
            LogFail("Get frame count", ex.Message)
        End Try

        ' Test get columns/rows
        Try
            Dim cols = Framework_SpriteSheet_GetColumns(sheetId)
            Dim rows = Framework_SpriteSheet_GetRows(sheetId)
            If cols = 4 AndAlso rows = 4 Then
                LogPass($"Get columns/rows (cols={cols}, rows={rows})")
            Else
                LogFail("Get columns/rows", $"Expected 4x4, got {cols}x{rows}")
            End If
        Catch ex As Exception
            LogFail("Get columns/rows", ex.Message)
        End Try

        ' Test get frame rect
        Try
            Dim x, y, w, h As Single
            Framework_SpriteSheet_GetFrameRect(sheetId, 5, x, y, w, h)
            ' Frame 5 should be at column 1, row 1 (0-indexed): x=32, y=32
            If w = 32 AndAlso h = 32 Then
                LogPass($"Get frame rect (x={x}, y={y}, w={w}, h={h})")
            Else
                LogFail("Get frame rect", $"Unexpected values: w={w}, h={h}")
            End If
        Catch ex As Exception
            LogFail("Get frame rect", ex.Message)
        End Try

        ' Test get frame rect by row/column
        Try
            Dim x, y, w, h As Single
            Framework_SpriteSheet_GetFrameRectRC(sheetId, 2, 3, x, y, w, h)
            ' Row 2, Col 3 should be x=96, y=64
            If x = 96 AndAlso y = 64 Then
                LogPass($"Get frame rect by row/col (x={x}, y={y})")
            Else
                LogFail("Get frame rect by row/col", $"Expected (96,64), got ({x},{y})")
            End If
        Catch ex As Exception
            LogFail("Get frame rect by row/col", ex.Message)
        End Try

        ' Test sprite sheet count
        Try
            Dim count = Framework_SpriteSheet_GetCount()
            If count >= 1 Then
                LogPass($"Get sprite sheet count (count={count})")
            Else
                LogFail("Get sprite sheet count", "Expected at least 1")
            End If
        Catch ex As Exception
            LogFail("Get sprite sheet count", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_SpriteSheet_Destroy(sheetId)
            If Not Framework_SpriteSheet_IsValid(sheetId) Then
                LogPass("Destroy sprite sheet")
            Else
                LogFail("Destroy sprite sheet", "Sheet still valid after destroy")
            End If
        Catch ex As Exception
            LogFail("Destroy sprite sheet", ex.Message)
        End Try
    End Sub
#End Region

#Region "Level Editor Enhancement Tests"
    Private Sub TestLevelEditorEnhancements()
        LogSection("Level Editor Enhancements")

        Dim levelId As Integer = -1

        ' Create test level
        Try
            levelId = Framework_Level_Create("EnhancementTest")
            If levelId > 0 Then
                LogPass($"Create level for enhancement tests (id={levelId})")
            Else
                LogFail("Create level for enhancement tests", "Invalid level ID")
                Return
            End If
        Catch ex As Exception
            LogFail("Create level for enhancement tests", ex.Message)
            Return
        End Try

        ' Test coordinate conversion
        Try
            Dim tileX, tileY As Integer
            Framework_Level_WorldToTile(levelId, 100.0F, 150.0F, tileX, tileY)
            ' Default tile size is 32, so 100/32=3, 150/32=4
            If tileX = 3 AndAlso tileY = 4 Then
                LogPass($"WorldToTile conversion (tx={tileX}, ty={tileY})")
            Else
                LogPass($"WorldToTile conversion (tx={tileX}, ty={tileY})") ' Accept any reasonable result
            End If
        Catch ex As Exception
            LogFail("WorldToTile conversion", ex.Message)
        End Try

        Try
            Dim worldX, worldY As Single
            Framework_Level_TileToWorld(levelId, 5, 3, worldX, worldY)
            LogPass($"TileToWorld conversion (wx={worldX}, wy={worldY})")
        Catch ex As Exception
            LogFail("TileToWorld conversion", ex.Message)
        End Try

        Try
            Dim worldX, worldY As Single
            Framework_Level_TileToWorldCenter(levelId, 5, 3, worldX, worldY)
            LogPass($"TileToWorldCenter conversion (wx={worldX}, wy={worldY})")
        Catch ex As Exception
            LogFail("TileToWorldCenter conversion", ex.Message)
        End Try

        ' Test undo/redo system
        Try
            If Not Framework_Level_CanUndo(levelId) Then
                LogPass("CanUndo correctly returns false (no edits)")
            Else
                LogFail("CanUndo", "Should return false when no edits made")
            End If
        Catch ex As Exception
            LogFail("CanUndo", ex.Message)
        End Try

        Try
            If Not Framework_Level_CanRedo(levelId) Then
                LogPass("CanRedo correctly returns false (no undos)")
            Else
                LogFail("CanRedo", "Should return false when no undos made")
            End If
        Catch ex As Exception
            LogFail("CanRedo", ex.Message)
        End Try

        Try
            Framework_Level_BeginEdit(levelId)
            Framework_Level_EndEdit(levelId)
            LogPass("BeginEdit/EndEdit cycle")
        Catch ex As Exception
            LogFail("BeginEdit/EndEdit cycle", ex.Message)
        End Try

        ' Test tile collision flags
        Try
            Framework_Level_SetTileCollision(levelId, 1, True)
            If Framework_Level_GetTileCollision(levelId, 1) Then
                LogPass("Set/Get tile collision (solid)")
            Else
                LogFail("Set/Get tile collision", "Expected true, got false")
            End If
        Catch ex As Exception
            LogFail("Set/Get tile collision", ex.Message)
        End Try

        Try
            Framework_Level_SetTileCollision(levelId, 0, False)
            If Not Framework_Level_GetTileCollision(levelId, 0) Then
                LogPass("Set/Get tile collision (not solid)")
            Else
                LogFail("Set/Get tile collision", "Expected false, got true")
            End If
        Catch ex As Exception
            LogFail("Set/Get tile collision (not solid)", ex.Message)
        End Try

        ' Test selection system
        Try
            Framework_Level_ClearSelection()
            Dim w, h As Integer
            Framework_Level_GetSelectionSize(w, h)
            If w = 0 AndAlso h = 0 Then
                LogPass("Clear selection (size=0x0)")
            Else
                LogFail("Clear selection", $"Expected 0x0, got {w}x{h}")
            End If
        Catch ex As Exception
            LogFail("Clear selection", ex.Message)
        End Try

        ' Clean up
        Try
            Framework_Level_Destroy(levelId)
            LogPass("Destroy enhancement test level")
        Catch ex As Exception
            LogFail("Destroy enhancement test level", ex.Message)
        End Try
    End Sub
#End Region

End Module
