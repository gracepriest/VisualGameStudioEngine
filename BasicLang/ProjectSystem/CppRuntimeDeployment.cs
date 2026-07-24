using System.Collections.Generic;
using System.IO;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Deploys a MinGW C++ toolchain's runtime DLLs next to a freshly built executable.
    ///
    /// A g++ / winlibs-clang linked exe dynamically imports libstdc++-6.dll,
    /// libgcc_s_seh-1.dll and libwinpthread-1.dll. When the toolchain lives OFF PATH
    /// (the winlibs override this repo blesses), the exe cannot resolve them at
    /// standalone launch and dies with "libstdc++-6.dll not found" before entry.
    /// Copying them into bin/&lt;config&gt; beside the exe fixes standalone runs and makes
    /// the build output distributable — mirroring how the engine's own native DLLs are
    /// already deployed (see <see cref="EngineDeployment"/>).
    ///
    /// Detection is implicit: only DLLs that actually exist in the compiler's bin dir are
    /// returned. An MSVC-ABI toolchain (cl, or clang targeting the MSVC runtime) has none
    /// of these beside it, so this is a no-op for it — correctly, since its runtime is the
    /// system UCRT/vcruntime. (A clang-with-libc++ MinGW build would also need
    /// libc++.dll/libunwind.dll; winlibs' default GCC-backed clang emits libstdc++, so those
    /// are left as a future addition rather than shipped unused.)
    /// </summary>
    public static class CppRuntimeDeployment
    {
        /// <summary>The GNU/MinGW C++ runtime DLLs a g++ or winlibs-clang link imports dynamically.</summary>
        public static readonly string[] RuntimeDllNames =
        {
            "libstdc++-6.dll",
            "libgcc_s_seh-1.dll",
            "libwinpthread-1.dll",
        };

        /// <summary>
        /// The subset of <see cref="RuntimeDllNames"/> present in <paramref name="compilerBinDir"/>,
        /// as full source paths to copy. Empty when the directory is null/missing or holds none of
        /// them (an MSVC-ABI toolchain), so a caller can iterate unconditionally.
        /// </summary>
        public static List<string> LocateRuntimeDlls(string compilerBinDir)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(compilerBinDir) || !Directory.Exists(compilerBinDir))
                return found;

            foreach (var name in RuntimeDllNames)
            {
                var path = Path.Combine(compilerBinDir, name);
                if (File.Exists(path))
                    found.Add(path);
            }
            return found;
        }
    }
}
