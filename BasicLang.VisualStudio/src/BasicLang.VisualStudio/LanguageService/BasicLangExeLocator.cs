using System.Diagnostics;

namespace BasicLang.VisualStudio.LanguageService;

/// <summary>
/// Shared discovery logic for locating BasicLang.exe, which serves as both
/// the LSP language server (--lsp) and the compiler.
/// Used by <see cref="BasicLangLanguageClient"/> and the Build/Run commands.
/// </summary>
public static class BasicLangExeLocator
{
    /// <summary>
    /// Finds BasicLang.exe. Resolution order:
    /// 1. User override from Tools &gt; Options &gt; BasicLang &gt; General (Language Server Path)
    /// 2. Extension directory (if BasicLang.exe is bundled with the VSIX)
    /// 3. Known install locations (%LOCALAPPDATA%\BasicLang, Program Files)
    /// 4. Assembly-relative development paths (repo IDE\ and BasicLang\bin\ outputs)
    /// 5. PATH environment variable scan
    /// </summary>
    public static string? FindBasicLangExe()
    {
        // 1. User override from the options page
        var overridePath = GetOptionsOverride();
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            Debug.WriteLine($"Found BasicLang.exe via options override: {overridePath}");
            return overridePath;
        }

        var asmDir = Path.GetDirectoryName(typeof(BasicLangExeLocator).Assembly.Location) ?? "";

        // 2. Extension directory, 3. Known install locations
        var candidates = new[]
        {
            Path.Combine(asmDir, "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BasicLang", "BasicLang.exe")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Found BasicLang.exe at: {path}");
                    return path;
                }
            }
            catch
            {
                // Ignore path errors
            }
        }

        // 4. Assembly-relative development paths
        // (bin\Release\net48 -> six levels up -> repo root)
        var devRelativePaths = new[]
        {
            @"..\..\..\..\..\..\IDE\BasicLang.exe",
            @"..\..\..\..\..\..\BasicLang\bin\Release\net8.0\BasicLang.exe",
            @"..\..\..\..\..\..\BasicLang\bin\Debug\net8.0\BasicLang.exe"
        };

        foreach (var relative in devRelativePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(asmDir, relative));
                if (File.Exists(fullPath))
                {
                    Debug.WriteLine($"Found BasicLang.exe at dev path: {fullPath}");
                    return fullPath;
                }
            }
            catch
            {
                // Ignore path errors
            }
        }

        // 5. Search in PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, "BasicLang.exe");
                    if (File.Exists(fullPath))
                    {
                        Debug.WriteLine($"Found BasicLang.exe in PATH: {fullPath}");
                        return fullPath;
                    }
                }
                catch
                {
                    // Ignore path errors
                }
            }
        }

        Debug.WriteLine("BasicLang.exe not found in any location");
        return null;
    }

    /// <summary>
    /// Reads the Language Server Path override from the General options snapshot.
    /// The snapshot is populated on the UI thread by the package (at load time and
    /// whenever the user applies the options page), so this is a plain field read
    /// that is safe on any thread and cannot block or deadlock. Before the package
    /// has loaded, the snapshot holds the defaults (empty path), which matches the
    /// old "package not loaded yet" behavior of returning no override.
    /// </summary>
    private static string? GetOptionsOverride()
    {
        return Options.GeneralOptionsPage.Snapshot.LanguageServerPath;
    }
}
