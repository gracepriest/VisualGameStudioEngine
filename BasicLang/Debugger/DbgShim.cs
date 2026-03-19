using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BasicLang.Debugger
{
    /// <summary>
    /// P/Invoke declarations for dbgshim.dll (.NET Core debugging bootstrap)
    /// </summary>
    internal static class DbgShim
    {
        private static string _dbgshimPath;
        private static bool _loaded;

        // Callback delegate for RegisterForRuntimeStartup
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void RuntimeStartupCallback(
            [MarshalAs(UnmanagedType.Interface)] object pCordb,
            IntPtr parameter,
            int hresult);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int RegisterForRuntimeStartup(
            int processId,
            RuntimeStartupCallback callback,
            IntPtr parameter,
            out IntPtr unregisterToken);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int UnregisterForRuntimeStartup(IntPtr token);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern int CreateDebuggingInterfaceFromVersion3(
            int iDebuggerVersion,
            string szDebuggeeVersion,
            string szApplicationGroupId,
            [MarshalAs(UnmanagedType.Interface)] out object ppCordb);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int EnumerateCLRs(
            int processId,
            out IntPtr ppHandleArrayOut,
            out IntPtr ppStringArrayOut,
            out int pdwArrayLengthOut);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CloseCLREnumeration(
            IntPtr pHandleArray,
            IntPtr pStringArray,
            int dwArrayLength);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern int CreateVersionStringFromModule(
            int pidDebuggee,
            string szModuleName,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pBuffer,
            int cchBuffer,
            out int pdwLength);

        /// <summary>
        /// Find and load dbgshim.dll. Returns true if found.
        /// </summary>
        public static bool TryLoad()
        {
            if (_loaded) return _dbgshimPath != null;
            _loaded = true;

            // dbgshim.dll is NOT in the .NET runtime — it ships with:
            // - Visual Studio (Common7/IDE/Remote Debugger/x64/)
            // - Visual Studio (Common7/Packages/Debugger/)
            // - JetBrains Rider
            // - .NET SDK diagnostics tools
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var searchPaths = new[]
            {
                // Next to compiler (IDE/dbgshim.dll) — user can copy it here
                Path.Combine(AppContext.BaseDirectory, "dbgshim.dll"),
                // VS 2022 Remote Debugger (most reliable location)
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise",
                    "Common7", "IDE", "Remote Debugger", "x64", "dbgshim.dll"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional",
                    "Common7", "IDE", "Remote Debugger", "x64", "dbgshim.dll"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community",
                    "Common7", "IDE", "Remote Debugger", "x64", "dbgshim.dll"),
                // VS 2022 Packages/Debugger
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise",
                    "Common7", "Packages", "Debugger", "dbgshim.dll"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional",
                    "Common7", "Packages", "Debugger", "dbgshim.dll"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community",
                    "Common7", "Packages", "Debugger", "dbgshim.dll"),
                // DOTNET_ROOT environment variable
                Environment.GetEnvironmentVariable("DOTNET_ROOT") is string root
                    ? Path.Combine(root, "shared", "Microsoft.NETCore.App") : null,
                // .NET runtime (some SDK installs include it)
                Path.Combine(programFiles, "dotnet", "shared", "Microsoft.NETCore.App"),
            };

            foreach (var basePath in searchPaths)
            {
                if (string.IsNullOrEmpty(basePath)) continue;

                // Direct file check
                if (File.Exists(basePath))
                {
                    _dbgshimPath = basePath;
                    NativeLibrary.Load(_dbgshimPath);
                    return true;
                }

                // Directory with version subdirs — find latest
                if (Directory.Exists(basePath))
                {
                    var versionDirs = Directory.GetDirectories(basePath);
                    Array.Sort(versionDirs);
                    for (int i = versionDirs.Length - 1; i >= 0; i--)
                    {
                        var candidate = Path.Combine(versionDirs[i], "dbgshim.dll");
                        if (File.Exists(candidate))
                        {
                            _dbgshimPath = candidate;
                            NativeLibrary.Load(_dbgshimPath);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Get the path to the loaded dbgshim.dll, or null if not found.
        /// </summary>
        public static string GetLoadedPath() => _dbgshimPath;
    }
}
