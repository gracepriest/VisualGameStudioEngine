using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BasicLang.Debugger
{
    /// <summary>
    /// P/Invoke bindings to VisualGameStudioEngine.dll for the debuggable interpreter.
    /// Maps BasicLang stdlib function names to native engine calls.
    /// </summary>
    internal static class EngineBindings
    {
        private const string DLL = "VisualGameStudioEngine.dll";

        private static bool _engineLoaded;
        private static bool _engineAvailable;

        /// <summary>
        /// Check if the engine DLL is available. Searches multiple locations
        /// and adds the DLL directory to the search path if found.
        /// </summary>
        public static bool IsAvailable()
        {
            if (_engineLoaded) return _engineAvailable;
            _engineLoaded = true;
            try
            {
                // First try direct load (already in PATH or working dir)
                if (NativeLibrary.TryLoad(DLL, typeof(EngineBindings).Assembly, null, out _))
                {
                    _engineAvailable = true;
                    return true;
                }

                // Search common locations relative to the compiler
                var baseDir = AppContext.BaseDirectory;
                var searchPaths = new[]
                {
                    Path.Combine(baseDir, DLL),                                    // Next to BasicLang.dll
                    Path.Combine(baseDir, "..", "IDE", DLL),                        // ../IDE/
                    Path.Combine(baseDir, "..", "..", "..", "..", "IDE", DLL),       // Solution root/IDE/
                    Path.Combine(baseDir, "..", "..", "..", "..", "VisualGameStudioEngine", "x64", "Release", DLL),
                    Path.Combine(baseDir, "..", "..", "..", "..", "VisualGameStudioEngine", "x64", "Debug", DLL),
                };

                foreach (var path in searchPaths)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        // Add the directory containing the DLL to search path
                        var dllDir = Path.GetDirectoryName(fullPath)!;
                        SetDllDirectory(dllDir);
                        if (NativeLibrary.TryLoad(fullPath, out _))
                        {
                            _engineAvailable = true;
                            return true;
                        }
                    }
                }

                _engineAvailable = false;
            }
            catch
            {
                _engineAvailable = false;
            }
            return _engineAvailable;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        // ==================== CORE ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_Initialize(int width, int height, [MarshalAs(UnmanagedType.LPStr)] string title);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_Update();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_ShouldClose();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_Shutdown();

        // ==================== DRAW ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_BeginDrawing();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_EndDrawing();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_ClearBackground(byte r, byte g, byte b, byte a);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawText([MarshalAs(UnmanagedType.LPStr)] string text, int x, int y, int fontSize, byte r, byte g, byte b, byte a);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawRectangle(int x, int y, int width, int height, byte r, byte g, byte b, byte a);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawCircle(float centerX, float centerY, float radius, byte r, byte g, byte b, byte a);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawLine(float startX, float startY, float endX, float endY, byte r, byte g, byte b, byte a);

        // ==================== TIMING ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_SetTargetFPS(int fps);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float Framework_GetFrameTime();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float Framework_GetDeltaTime();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern double Framework_GetTime();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Framework_GetFPS();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_SetTimeScale(float scale);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float Framework_GetTimeScale();

        // ==================== INPUT - KEYBOARD ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsKeyPressed(int key);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsKeyDown(int key);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsKeyReleased(int key);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsKeyUp(int key);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Framework_GetKeyPressed();

        // ==================== INPUT - MOUSE ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Framework_GetMouseX();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Framework_GetMouseY();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsMouseButtonPressed(int button);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsMouseButtonDown(int button);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsMouseButtonReleased(int button);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_SetMousePosition(int x, int y);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern float Framework_GetMouseWheelMove();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_ShowCursor();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_HideCursor();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool Framework_IsCursorHidden();

        // ==================== TEXTURES ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Framework_LoadTexture([MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_UnloadTexture(int handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawTextureSimple(int handle, float x, float y, byte r, byte g, byte b, byte a);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawTextureEx(int handle, float x, float y, float rotation, float scale, byte r, byte g, byte b, byte a);

        // ==================== AUDIO ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_InitAudio();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_CloseAudio();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Framework_LoadSound([MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_PlaySound(int handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_StopSound(int handle);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_SetSoundVolume(int handle, float volume);

        // ==================== DRAWING EXTRAS ====================
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawRectangleLines(int x, int y, int w, int h, byte r, byte g, byte b, byte a);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Framework_DrawFPS(int x, int y);

        /// <summary>
        /// Try to execute a BasicLang stdlib function via the native engine DLL.
        /// Returns null sentinel if function not recognized.
        /// </summary>
        public static object TryExecute(string name, object[] args, object notFoundSentinel)
        {
            if (!IsAvailable()) return notFoundSentinel;

            try
            {
                switch (name.ToLowerInvariant())
                {
                    // Core
                    case "gameinit":
                        Framework_Initialize(ToInt(args, 0), ToInt(args, 1), ToStr(args, 2));
                        return null;
                    case "gameshutdown":
                        Framework_Shutdown();
                        return null;
                    case "gamebeginframe":
                        Framework_Update();
                        Framework_BeginDrawing();
                        return null;
                    case "gameendframe":
                        Framework_EndDrawing();
                        return null;
                    case "gameshouldclose":
                        return Framework_ShouldClose();
                    case "gamegetdeltatime":
                        return Framework_GetDeltaTime();
                    case "gamegetfps":
                        return Framework_GetFPS();

                    // Timing
                    case "settargetfps":
                        Framework_SetTargetFPS(ToInt(args, 0));
                        return null;
                    case "getframetime":
                        return Framework_GetFrameTime();
                    case "gettime":
                        return Framework_GetTime();
                    case "settimescale":
                        Framework_SetTimeScale(ToFloat(args, 0));
                        return null;
                    case "gettimescale":
                        return Framework_GetTimeScale();

                    // Drawing
                    case "clearbackground":
                        Framework_ClearBackground(ToByte(args, 0), ToByte(args, 1), ToByte(args, 2),
                            args.Length > 3 ? ToByte(args, 3) : (byte)255);
                        return null;
                    case "drawrectangle":
                        Framework_DrawRectangle(ToInt(args, 0), ToInt(args, 1), ToInt(args, 2), ToInt(args, 3),
                            ToByte(args, 4), ToByte(args, 5), ToByte(args, 6), ToByte(args, 7));
                        return null;
                    case "drawcircle":
                        Framework_DrawCircle(ToFloat(args, 0), ToFloat(args, 1), ToFloat(args, 2),
                            ToByte(args, 3), ToByte(args, 4), ToByte(args, 5), ToByte(args, 6));
                        return null;
                    case "drawline":
                        Framework_DrawLine(ToFloat(args, 0), ToFloat(args, 1), ToFloat(args, 2), ToFloat(args, 3),
                            ToByte(args, 4), ToByte(args, 5), ToByte(args, 6), ToByte(args, 7));
                        return null;
                    case "drawtext":
                        Framework_DrawText(ToStr(args, 0), ToInt(args, 1), ToInt(args, 2), ToInt(args, 3),
                            ToByte(args, 4), ToByte(args, 5), ToByte(args, 6), ToByte(args, 7));
                        return null;
                    case "drawrectanglelines":
                        Framework_DrawRectangleLines(ToInt(args, 0), ToInt(args, 1), ToInt(args, 2), ToInt(args, 3),
                            ToByte(args, 4), ToByte(args, 5), ToByte(args, 6), ToByte(args, 7));
                        return null;
                    case "drawfps":
                        Framework_DrawFPS(ToInt(args, 0), ToInt(args, 1));
                        return null;

                    // Input - Keyboard
                    case "iskeypressed":
                        return Framework_IsKeyPressed(ToInt(args, 0));
                    case "iskeydown":
                        return Framework_IsKeyDown(ToInt(args, 0));
                    case "iskeyreleased":
                        return Framework_IsKeyReleased(ToInt(args, 0));
                    case "iskeyup":
                        return Framework_IsKeyUp(ToInt(args, 0));
                    case "getkeypressed":
                        return Framework_GetKeyPressed();

                    // Input - Mouse
                    case "getmousex":
                        return Framework_GetMouseX();
                    case "getmousey":
                        return Framework_GetMouseY();
                    case "ismousebuttonpressed":
                        return Framework_IsMouseButtonPressed(ToInt(args, 0));
                    case "ismousebuttondown":
                        return Framework_IsMouseButtonDown(ToInt(args, 0));
                    case "ismousebuttonreleased":
                        return Framework_IsMouseButtonReleased(ToInt(args, 0));
                    case "setmouseposition":
                        Framework_SetMousePosition(ToInt(args, 0), ToInt(args, 1));
                        return null;
                    case "getmousewheelmove":
                        return Framework_GetMouseWheelMove();
                    case "showcursor":
                        Framework_ShowCursor();
                        return null;
                    case "hidecursor":
                        Framework_HideCursor();
                        return null;
                    case "iscursorhidden":
                        return Framework_IsCursorHidden();

                    // Textures
                    case "loadtexture":
                        return Framework_LoadTexture(ToStr(args, 0));
                    case "unloadtexture":
                        Framework_UnloadTexture(ToInt(args, 0));
                        return null;
                    case "drawtexture":
                        Framework_DrawTextureSimple(ToInt(args, 0), ToFloat(args, 1), ToFloat(args, 2),
                            ToByte(args, 3), ToByte(args, 4), ToByte(args, 5), ToByte(args, 6));
                        return null;
                    case "drawtextureex":
                        Framework_DrawTextureEx(ToInt(args, 0), ToFloat(args, 1), ToFloat(args, 2),
                            ToFloat(args, 3), ToFloat(args, 4),
                            ToByte(args, 5), ToByte(args, 6), ToByte(args, 7), ToByte(args, 8));
                        return null;

                    // Audio
                    case "initaudio":
                        Framework_InitAudio();
                        return null;
                    case "closeaudio":
                        Framework_CloseAudio();
                        return null;
                    case "loadsound":
                        return Framework_LoadSound(ToStr(args, 0));
                    case "playsound":
                        Framework_PlaySound(ToInt(args, 0));
                        return null;
                    case "stopsound":
                        Framework_StopSound(ToInt(args, 0));
                        return null;
                    case "setsoundvolume":
                        Framework_SetSoundVolume(ToInt(args, 0), ToFloat(args, 1));
                        return null;
                }
            }
            catch (DllNotFoundException)
            {
                _engineAvailable = false;
            }
            catch (EntryPointNotFoundException)
            {
                // Function exists in StdLib but not in this DLL version — fall through
            }

            return notFoundSentinel;
        }

        private static int ToInt(object[] args, int i) =>
            i < args.Length ? Convert.ToInt32(args[i]) : 0;

        private static float ToFloat(object[] args, int i) =>
            i < args.Length ? Convert.ToSingle(args[i]) : 0f;

        private static byte ToByte(object[] args, int i) =>
            i < args.Length ? (byte)Math.Clamp(Convert.ToInt32(args[i]), 0, 255) : (byte)0;

        private static string ToStr(object[] args, int i) =>
            i < args.Length ? args[i]?.ToString() ?? "" : "";
    }
}
