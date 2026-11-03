using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NewCom.Runtime;

/// <summary>
/// Managed loader for NewCOM components. Mirrors <c>newcom_loader.cpp</c> discovery
/// (./components/, ~/.newcom/, $NEWCOM_PATH/) and invokes the C-ABI factory.
/// Phase 3 foundation: loading + lifetime only; control-contract marshalling is next.
/// </summary>
public static class NewComLoader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    public sealed class ComponentHandle : IDisposable
    {
        public IntPtr Native { get; }
        private readonly IntPtr _module;
        private readonly IntPtr _releaseFn;
        internal ComponentHandle(IntPtr module, IntPtr native, IntPtr releaseFn)
        {
            _module = module;
            Native = native;
            _releaseFn = releaseFn;
        }
        public void Dispose()
        {
            if (_nativeReleased) return;
            _nativeReleased = true;
            if (_releaseFn != IntPtr.Zero)
            {
                var del = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(_releaseFn);
                del(Native);
            }
            if (_module != IntPtr.Zero) FreeLibrary(_module);
        }
        private bool _nativeReleased;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReleaseDelegate(IntPtr h);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateDelegate();

    /// <summary>Find a manifest by component name across the standard search paths.</summary>
    public static string? FindManifest(string name)
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "components"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".newcom"),
        };
        var env = Environment.GetEnvironmentVariable("NEWCOM_PATH");
        if (!string.IsNullOrEmpty(env)) searchPaths = searchPaths.Append(env).ToArray();

        foreach (var sp in searchPaths)
        {
            if (!Directory.Exists(sp)) continue;
            // ./components/<name>/newcom.json
            var cand = Path.Combine(sp, name, "newcom.json");
            if (File.Exists(cand)) return cand;
            // ./components/newcom.json with matching name
            cand = Path.Combine(sp, "newcom.json");
            if (File.Exists(cand))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cand));
                    if (doc.RootElement.TryGetProperty("name", out var n) &&
                        string.Equals(n.GetString(), name, StringComparison.OrdinalIgnoreCase))
                        return cand;
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>Load a component by name and invoke its factory. Returns a handle.</summary>
    public static ComponentHandle? Load(string name)
    {
        var manifestPath = FindManifest(name);
        if (manifestPath == null) return null;

        string libName, factoryName;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            libName = root.GetProperty("library").GetString() ?? "";
            factoryName = root.GetProperty("factory").GetString() ?? (name + "_Create");
        }
        catch { return null; }

        var dir = Path.GetDirectoryName(manifestPath)!;
        var libPath = Path.IsPathRooted(libName) ? libName : Path.Combine(dir, libName);
        if (!File.Exists(libPath)) return null;

        var module = LoadLibrary(libPath);
        if (module == IntPtr.Zero) return null;

        var createPtr = GetProcAddress(module, factoryName);
        var releasePtr = GetProcAddress(module, name + "_Release");
        if (createPtr == IntPtr.Zero)
        {
            FreeLibrary(module);
            return null;
        }

        var create = Marshal.GetDelegateForFunctionPointer<CreateDelegate>(createPtr);
        var native = create();
        if (native == IntPtr.Zero)
        {
            FreeLibrary(module);
            return null;
        }
        return new ComponentHandle(module, native, releasePtr);
    }
}
