using System.Runtime.InteropServices;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Represents a detected shell that can be used in the integrated terminal.
/// </summary>
public class ShellProfile
{
    /// <summary>
    /// Display name shown in the dropdown (e.g., "PowerShell 7", "Git Bash").
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Full path to the shell executable.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Optional arguments to pass when launching the shell.
    /// </summary>
    public string Arguments { get; init; } = "";

    /// <summary>
    /// Short icon/glyph hint for the UI (e.g., "PS7", "CMD", "GIT").
    /// </summary>
    public string Icon { get; init; } = "\u25BA";

    public override string ToString() => Name;
}

/// <summary>
/// Detects available terminal shells on the current system by scanning
/// PATH, common install locations, and the Windows registry.
/// </summary>
public static class ShellProfileDetector
{
    /// <summary>
    /// Returns all shells available on this machine, ordered by preference.
    /// </summary>
    public static List<ShellProfile> DetectProfiles()
    {
        var profiles = new List<ShellProfile>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DetectWindowsShells(profiles);
        }
        else
        {
            DetectUnixShells(profiles);
        }

        return profiles;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Windows detection
    // ──────────────────────────────────────────────────────────────────

    private static void DetectWindowsShells(List<ShellProfile> profiles)
    {
        DetectPowerShell7(profiles);
        DetectWindowsPowerShell(profiles);
        DetectCommandPrompt(profiles);
        DetectGitBash(profiles);
        DetectWsl(profiles);
        DetectMsys2(profiles);
        DetectCygwin(profiles);
        DetectNushell(profiles);
    }

    /// <summary>
    /// PowerShell 7+ (pwsh.exe) — check common install paths, PATH, and registry.
    /// </summary>
    private static void DetectPowerShell7(List<ShellProfile> profiles)
    {
        var candidates = new List<string>
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7", "pwsh.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7-preview", "pwsh.exe"),
        };

        // Check registry for PowerShell Core install path
        var registryPath = ReadRegistryString(
            @"SOFTWARE\Microsoft\PowerShellCore\InstalledVersions",
            null);
        if (registryPath != null)
        {
            // Enumerate sub-keys for install paths
            var regPwsh = TryReadPowerShellCoreRegistry();
            if (regPwsh != null)
                candidates.Add(regPwsh);
        }

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                profiles.Add(new ShellProfile
                {
                    Name = "PowerShell 7",
                    Path = p,
                    Icon = "PS7"
                });
                return;
            }
        }

        // Fallback: check PATH
        var pwshOnPath = FindOnPath("pwsh.exe");
        if (pwshOnPath != null)
        {
            profiles.Add(new ShellProfile
            {
                Name = "PowerShell 7",
                Path = pwshOnPath,
                Icon = "PS7"
            });
        }
    }

    /// <summary>
    /// Windows PowerShell 5.1 (always available on Windows 10+).
    /// </summary>
    private static void DetectWindowsPowerShell(List<ShellProfile> profiles)
    {
        var winPowerShell = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

        profiles.Add(new ShellProfile
        {
            Name = "Windows PowerShell",
            Path = File.Exists(winPowerShell) ? winPowerShell : "powershell.exe",
            Icon = "PS"
        });
    }

    /// <summary>
    /// Command Prompt (cmd.exe — always available).
    /// </summary>
    private static void DetectCommandPrompt(List<ShellProfile> profiles)
    {
        var cmdPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        profiles.Add(new ShellProfile
        {
            Name = "Command Prompt",
            Path = File.Exists(cmdPath) ? cmdPath : "cmd.exe",
            Icon = "CMD"
        });
    }

    /// <summary>
    /// Git Bash — check common install locations and registry (Git for Windows).
    /// </summary>
    private static void DetectGitBash(List<ShellProfile> profiles)
    {
        var candidates = new List<string>
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Git", "bin", "bash.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Git", "bin", "bash.exe"),
            @"C:\Git\bin\bash.exe",
        };

        // Check registry for Git install path
        var gitInstallPath = ReadRegistryString(
            @"SOFTWARE\GitForWindows", "InstallPath");
        if (!string.IsNullOrEmpty(gitInstallPath))
        {
            var regBash = System.IO.Path.Combine(gitInstallPath, "bin", "bash.exe");
            if (!candidates.Contains(regBash))
                candidates.Insert(0, regBash);
        }

        // Also try HKCU
        var gitInstallPathUser = ReadRegistryString(
            @"SOFTWARE\GitForWindows", "InstallPath", useHkcu: true);
        if (!string.IsNullOrEmpty(gitInstallPathUser))
        {
            var regBash = System.IO.Path.Combine(gitInstallPathUser, "bin", "bash.exe");
            if (!candidates.Contains(regBash))
                candidates.Insert(0, regBash);
        }

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                profiles.Add(new ShellProfile
                {
                    Name = "Git Bash",
                    Path = p,
                    Arguments = "--login -i",
                    Icon = "GIT"
                });
                return;
            }
        }

        // Fallback: check PATH
        var bashOnPath = FindOnPath("bash.exe");
        if (bashOnPath != null && bashOnPath.Contains("Git", StringComparison.OrdinalIgnoreCase))
        {
            profiles.Add(new ShellProfile
            {
                Name = "Git Bash",
                Path = bashOnPath,
                Arguments = "--login -i",
                Icon = "GIT"
            });
        }
    }

    /// <summary>
    /// WSL (Windows Subsystem for Linux).
    /// </summary>
    private static void DetectWsl(List<ShellProfile> profiles)
    {
        var wslPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

        if (File.Exists(wslPath))
        {
            profiles.Add(new ShellProfile
            {
                Name = "WSL",
                Path = wslPath,
                Icon = "WSL"
            });
        }
    }

    /// <summary>
    /// MSYS2 — check common install locations and registry.
    /// </summary>
    private static void DetectMsys2(List<ShellProfile> profiles)
    {
        var candidates = new List<string>
        {
            @"C:\msys64\usr\bin\bash.exe",
            @"C:\msys2\usr\bin\bash.exe",
        };

        // Check registry for MSYS2 install
        var msys2Install = ReadRegistryString(
            @"SOFTWARE\MSYS2", "InstallDir");
        if (!string.IsNullOrEmpty(msys2Install))
        {
            var regBash = System.IO.Path.Combine(msys2Install, "usr", "bin", "bash.exe");
            if (!candidates.Contains(regBash))
                candidates.Insert(0, regBash);
        }

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                // Avoid adding if it overlaps with a Git Bash entry
                if (profiles.Any(pr => string.Equals(pr.Path, p, StringComparison.OrdinalIgnoreCase)))
                    return;

                profiles.Add(new ShellProfile
                {
                    Name = "MSYS2",
                    Path = p,
                    Arguments = "--login -i",
                    Icon = "MSYS"
                });
                return;
            }
        }
    }

    /// <summary>
    /// Cygwin — check common install locations and registry.
    /// </summary>
    private static void DetectCygwin(List<ShellProfile> profiles)
    {
        var candidates = new List<string>
        {
            @"C:\cygwin64\bin\bash.exe",
            @"C:\cygwin\bin\bash.exe",
        };

        // Check registry for Cygwin install path
        var cygwinInstall = ReadRegistryString(
            @"SOFTWARE\Cygwin\setup", "rootdir");
        if (!string.IsNullOrEmpty(cygwinInstall))
        {
            var regBash = System.IO.Path.Combine(cygwinInstall, "bin", "bash.exe");
            if (!candidates.Contains(regBash))
                candidates.Insert(0, regBash);
        }

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                profiles.Add(new ShellProfile
                {
                    Name = "Cygwin",
                    Path = p,
                    Arguments = "--login -i",
                    Icon = "CYG"
                });
                return;
            }
        }
    }

    /// <summary>
    /// Nushell (nu.exe) — a modern shell sometimes installed via cargo or scoop.
    /// </summary>
    private static void DetectNushell(List<ShellProfile> profiles)
    {
        var nuOnPath = FindOnPath("nu.exe");
        if (nuOnPath != null)
        {
            profiles.Add(new ShellProfile
            {
                Name = "Nushell",
                Path = nuOnPath,
                Icon = "NU"
            });
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Unix / macOS detection
    // ──────────────────────────────────────────────────────────────────

    private static void DetectUnixShells(List<ShellProfile> profiles)
    {
        var shells = new (string name, string path, string icon)[]
        {
            ("Bash",  "/bin/bash",      "BASH"),
            ("Zsh",   "/bin/zsh",       "ZSH"),
            ("Fish",  "/usr/bin/fish",  "FISH"),
            ("Nushell", "/usr/bin/nu",  "NU"),
            ("sh",    "/bin/sh",        "SH"),
        };

        foreach (var (name, path, icon) in shells)
        {
            if (File.Exists(path))
            {
                profiles.Add(new ShellProfile { Name = name, Path = path, Icon = icon });
            }
        }

        // Also check Homebrew locations on macOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var homebrewBash = "/opt/homebrew/bin/bash";
            if (File.Exists(homebrewBash) && !profiles.Any(p => p.Name == "Bash"))
            {
                profiles.Insert(0, new ShellProfile
                {
                    Name = "Bash (Homebrew)",
                    Path = homebrewBash,
                    Icon = "BASH"
                });
            }

            var homebrewFish = "/opt/homebrew/bin/fish";
            if (File.Exists(homebrewFish) && !profiles.Any(p => p.Name == "Fish"))
            {
                profiles.Add(new ShellProfile
                {
                    Name = "Fish (Homebrew)",
                    Path = homebrewFish,
                    Icon = "FISH"
                });
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches PATH for the given executable name.
    /// </summary>
    internal static string? FindOnPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = System.IO.Path.Combine(dir.Trim(), executable);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // skip invalid paths
            }
        }
        return null;
    }

    /// <summary>
    /// Reads a string value from the Windows registry (HKLM or HKCU).
    /// Returns null on non-Windows platforms or if the key doesn't exist.
    /// </summary>
    private static string? ReadRegistryString(string subKey, string? valueName, bool useHkcu = false)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            using var baseKey = useHkcu
                ? Microsoft.Win32.Registry.CurrentUser
                : Microsoft.Win32.Registry.LocalMachine;
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to find the PowerShell Core install path from registry sub-keys.
    /// Each installed version gets its own GUID sub-key with an "InstallDir" value.
    /// </summary>
    private static string? TryReadPowerShellCoreRegistry()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            using var baseKey = Microsoft.Win32.Registry.LocalMachine;
            using var versionsKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\PowerShellCore\InstalledVersions");
            if (versionsKey == null) return null;

            foreach (var subKeyName in versionsKey.GetSubKeyNames())
            {
                using var versionKey = versionsKey.OpenSubKey(subKeyName);
                var installDir = versionKey?.GetValue("InstallDir") as string;
                if (!string.IsNullOrEmpty(installDir))
                {
                    var pwshPath = System.IO.Path.Combine(installDir, "pwsh.exe");
                    if (File.Exists(pwshPath))
                        return pwshPath;
                }
            }
        }
        catch
        {
            // Registry access may fail
        }
        return null;
    }
}
