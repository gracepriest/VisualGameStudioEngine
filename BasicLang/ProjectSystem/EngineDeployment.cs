using System;
using System.Collections.Generic;
using System.IO;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Deployment helper for the BasicLang game engine.
    ///
    /// A program that uses the engine is transpiled with <c>using RaylibWrapper;</c>
    /// and <c>FrameworkWrapper.Framework_*</c> calls (see StdLib/FrameworkStdLib),
    /// but the generated C# needs the managed <c>RaylibWrapper.dll</c> as a
    /// compile reference and the native <c>VisualGameStudioEngine.dll</c> (its
    /// P/Invoke target) copied next to the built game to run.
    ///
    /// Both DLLs ship next to <c>BasicLang.exe</c> (the IDE/ folder), so the
    /// compiler resolves them relative to its own base directory and injects the
    /// reference + copies the native DLL automatically whenever it detects engine
    /// use — the same "just works" model as the implicit <c>using RaylibWrapper;</c>.
    /// </summary>
    public static class EngineDeployment
    {
        public const string WrapperAssemblyName = "RaylibWrapper";
        public const string WrapperDllName = "RaylibWrapper.dll";

        /// <summary>
        /// Native DLLs the managed wrapper P/Invokes into. These are NOT managed
        /// assemblies, so they can't be a <c>&lt;Reference&gt;</c> — they must be
        /// copied to the game's output directory explicitly.
        /// </summary>
        public static readonly string[] NativeEngineDllNames = { "VisualGameStudioEngine.dll" };

        /// <summary>
        /// MSVC import library for the native engine — the Cpp backend links
        /// generated games against it so the Framework_* dllimports resolve.
        /// Ships next to the compiler/IDE binaries like the DLLs.
        /// </summary>
        public const string EngineImportLibName = "VisualGameStudioEngine.lib";

        private const string WrapperUsingDirective = "using RaylibWrapper;";
        private const string CppFrameworkMarker = "#define FRAMEWORK_API";

        /// <summary>True if the generated C# uses the BasicLang game engine.</summary>
        public static bool UsesEngine(string generatedCode)
        {
            if (string.IsNullOrEmpty(generatedCode))
                return false;

            // The backend emits `using RaylibWrapper;` whenever the program
            // calls engine functions (its using auto-detection keys on
            // "FrameworkWrapper."), so the line-anchored directive is the one
            // reliable marker. Bare substring matching would false-positive on
            // user string literals that merely mention the wrapper — literals
            // are emitted single-line inside statements, so they can never put
            // the directive at the start of a line.
            return generatedCode.StartsWith(WrapperUsingDirective, StringComparison.Ordinal)
                || generatedCode.Contains("\n" + WrapperUsingDirective);
        }

        /// <summary>
        /// True if generated C++ uses the game engine. The Cpp backend emits a
        /// line-anchored <c>#define FRAMEWORK_API</c> preprocessor block above
        /// its extern "C" Framework_* declarations whenever engine calls are
        /// present; string literals are emitted inside statements and can never
        /// put that directive at the start of a line.
        /// </summary>
        public static bool UsesEngineCpp(string generatedCpp)
        {
            if (string.IsNullOrEmpty(generatedCpp))
                return false;
            return generatedCpp.StartsWith(CppFrameworkMarker, StringComparison.Ordinal)
                || generatedCpp.Contains("\n" + CppFrameworkMarker);
        }

        /// <summary>Path to the engine import library under <paramref name="baseDir"/>, or null.</summary>
        public static string GetImportLibPath(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir))
                return null;
            var path = Path.Combine(baseDir, EngineImportLibName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Locate the engine import library: next to the running binaries first
        /// (installed layout — IDE/ ships the .lib), then walking up from the
        /// base directory looking for a dev-tree x64/{Release,Debug} build.
        /// Null when not found anywhere.
        /// </summary>
        public static string LocateImportLib()
        {
            var direct = GetImportLibPath(AppContext.BaseDirectory);
            if (direct != null) return direct;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(dir.FullName, "x64", config, EngineImportLibName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        /// <summary>True when an existing assembly reference already covers the wrapper, in any legal Include form.</summary>
        public static bool IsWrapperReference(AssemblyReference reference)
        {
            if (reference == null)
                return false;

            // <Reference Include> legally takes "RaylibWrapper",
            // "RaylibWrapper.dll", or a full path; the HintPath can also be
            // what actually names the wrapper. Compare by file name.
            return NamesWrapper(reference.Name) || NamesWrapper(reference.HintPath);
        }

        private static bool NamesWrapper(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return false;
            try
            {
                return string.Equals(Path.GetFileNameWithoutExtension(nameOrPath),
                    WrapperAssemblyName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // invalid path characters — not the wrapper
            }
        }

        /// <summary>True when RaylibWrapper.dll ships in <paramref name="baseDir"/>.</summary>
        public static bool WrapperExists(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir))
                return false;
            return File.Exists(Path.Combine(baseDir, WrapperDllName));
        }

        /// <summary>The managed RaylibWrapper reference, hint-pathed into <paramref name="baseDir"/>.</summary>
        public static AssemblyReference GetEngineReference(string baseDir)
        {
            return new AssemblyReference
            {
                Name = WrapperAssemblyName,
                HintPath = Path.Combine(baseDir ?? string.Empty, WrapperDllName)
            };
        }

        /// <summary>Existing native engine DLL source paths under <paramref name="baseDir"/> to copy to output.</summary>
        public static List<string> GetNativeDllPaths(string baseDir)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(baseDir))
                return result;

            foreach (var name in NativeEngineDllNames)
            {
                var path = Path.Combine(baseDir, name);
                if (File.Exists(path))
                    result.Add(path);
            }

            return result;
        }

        /// <summary>Native DLLs matching wherever the import lib was found (same dir), falling back to the base-directory set.</summary>
        public static List<string> LocateNativeDlls(string importLibPath)
        {
            if (importLibPath != null)
            {
                var dir = Path.GetDirectoryName(importLibPath);
                var fromLibDir = GetNativeDllPaths(dir);
                if (fromLibDir.Count > 0) return fromLibDir;
            }
            return GetNativeDllPaths(AppContext.BaseDirectory);
        }
    }
}
