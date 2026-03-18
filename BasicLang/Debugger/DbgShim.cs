using System;
using System.IO;
using System.Runtime.InteropServices;

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

        /// <summary>
        /// Find and load dbgshim.dll. Returns true if found.
        /// </summary>
        public static bool TryLoad()
        {
            if (_loaded) return _dbgshimPath != null;
            _loaded = true;

            var searchPaths = new[]
            {
                // Next to compiler (IDE/dbgshim.dll)
                Path.Combine(AppContext.BaseDirectory, "dbgshim.dll"),
                // DOTNET_ROOT environment variable
                Environment.GetEnvironmentVariable("DOTNET_ROOT") is string root
                    ? Path.Combine(root, "shared", "Microsoft.NETCore.App") : null,
                // Standard Windows install
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.NETCore.App"),
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
