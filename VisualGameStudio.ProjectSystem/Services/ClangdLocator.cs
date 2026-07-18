using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Decides which clangd executable the IDE should talk to. The chain, first answer wins:
/// the <c>cpp.clangd.path</c> override (when it names a file that exists) → the IDE-installed copy
/// under <c>~/.vgs/tools</c> → PATH → conventional LLVM install directories → nothing.
///
/// <para>The override rule is <see cref="LanguageService.ResolveLspPathOverride"/> — the same one
/// <c>basiclang.lsp.path</c> already follows — with the auto-probe chain folded in, so callers get
/// the final answer rather than a multi-step decision to re-implement.</para>
///
/// <para><b>Null is a real answer.</b> clangd does not ship with the IDE and is not installed on
/// every machine. Returning null (rather than the bare name <c>"clangd"</c>, or a guessed path)
/// keeps "no C++ IntelliSense available" a fact the caller can act on up front, rather than
/// deferring it to a spawn that fails.</para>
/// </summary>
public static class ClangdLocator
{
    /// <summary>
    /// The name to look for on PATH. Bare, with no <c>.exe</c>: <see cref="ExecutableLocator"/>
    /// applies PATHEXT, so this one name is correct on Windows and POSIX alike.
    /// </summary>
    public const string ClangdExecutableName = "clangd";

    /// <summary>
    /// What a versioned install directory under the tools root looks like. Deliberately broader
    /// than <see cref="ClangdInstaller.InstalledDirName"/>: the probe must keep finding installs
    /// after the pinned release moves on (<c>clangd_23.x</c>), and the numeric version ranking
    /// below picks the newest among several.
    /// </summary>
    private const string ToolsDirPattern = "clangd*";

    /// <summary>
    /// The conventional Windows LLVM install locations swept as the chain's LAST resort, in probe
    /// order. These are conventions, not measurements — S0.4 found every one of them absent on the
    /// reference machine, so this list is unverifiable there; each entry is a documented default of
    /// its installer. Order is deliberate: Program Files (the official LLVM installer default)
    /// outranks the VS-bundled toolset, which outranks package managers (scoop, MSYS2).
    /// Empty off Windows for now.
    /// <para>Built once per process (the vswhere lookup for the VS-bundled entries spawns a
    /// process, bounded at ~2s) — at DI time in production, since that is the first touch.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> WindowsLlvmInstallDirectories =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? BuildWindowsLlvmInstallDirectories(TryGetVisualStudioInstallationPath())
            : Array.Empty<string>();

    /// <summary>
    /// Resolves clangd's path from settings, registering this class as the consumer of
    /// <see cref="LanguageServerDescriptor.ClangdSettingsKey"/> — the registration and the read it
    /// claims sit together on purpose (see <see cref="SettingsConsumerRegistry"/>).
    /// </summary>
    /// <param name="settingsService">
    /// Source of the override; null means "no override configured", not an error — the probe chain
    /// still runs.
    /// </param>
    /// <returns>The clangd path, or null when no link of the chain yields one.</returns>
    public static string? Locate(ISettingsService? settingsService)
    {
        SettingsConsumerRegistry.RegisterConsumer(
            LanguageServerDescriptor.ClangdSettingsKey,
            "ClangdLocator → clangd executable path override for the C++ language server");

        return ResolveClangdPath(
            settingsService?.Get<string>(LanguageServerDescriptor.ClangdSettingsKey, ""));
    }

    /// <summary>
    /// The resolution rule: a non-empty <paramref name="configuredPath"/> naming an existing file
    /// wins (trimmed); otherwise the probes run in CHAIN order — <paramref name="toolsProbe"/>
    /// (the IDE-installed <c>~/.vgs/tools</c> copy, a pinned hash-verified release), then
    /// <paramref name="pathProbe"/> (the user's own PATH arrangement), then
    /// <paramref name="llvmProbe"/> (conventional install locations, the last resort) — first
    /// non-null answer wins. A configured path that does NOT exist falls through to the chain
    /// rather than failing — a stale override left behind by an uninstall should degrade to
    /// auto-detection.
    /// </summary>
    /// <param name="fileExists">
    /// Existence probe for the override; defaults to <see cref="File.Exists(string)"/>.
    /// </param>
    /// <param name="pathProbe">
    /// The PATH step; defaults to searching PATH for <see cref="ClangdExecutableName"/>. (Kept
    /// third for call-site compatibility — the CHAIN runs the tools probe first.)
    /// </param>
    /// <param name="toolsProbe">
    /// The <c>~/.vgs/tools</c> step; defaults to <see cref="FindInToolsRoot"/>.
    /// </param>
    /// <param name="llvmProbe">
    /// The LLVM install-dir step; defaults to <see cref="FindInLlvmInstallDirectories"/>.
    /// </param>
    /// <remarks>
    /// Pure and static with every dependency injectable, matching
    /// <see cref="LanguageService.ResolveLspPathOverride"/>: it is the only seam these rules can be
    /// pinned through (the assembly exposes no internals to the test project), and what clangd a
    /// machine carries varies — probes hardwired to the real disk could not be asserted stably.
    /// </remarks>
    public static string? ResolveClangdPath(
        string? configuredPath,
        Func<string, bool>? fileExists = null,
        Func<string?>? pathProbe = null,
        Func<string?>? toolsProbe = null,
        Func<string?>? llvmProbe = null)
    {
        var overridePath = LanguageService.ResolveLspPathOverride(configuredPath, fileExists);
        if (overridePath != null) return overridePath;

        var tools = toolsProbe ?? (() => FindInToolsRoot());
        if (tools() is string toolsHit) return toolsHit;

        var path = pathProbe ?? (() => ExecutableLocator.Find(ClangdExecutableName));
        if (path() is string pathHit) return pathHit;

        var llvm = llvmProbe ?? FindInLlvmInstallDirectories;
        return llvm();
    }

    /// <summary>
    /// Probes the tools root for an installed clangd: scans <c>clangd*</c> directories, ranks them
    /// by the NUMERIC version parsed from the <c>clangd_</c> suffix (highest first; a suffix that
    /// does not parse ranks below any that does), and returns the first ranked directory whose
    /// <c>bin/clangd.exe</c> (<c>bin/clangd</c> off Windows) exists — as an absolute path, or null.
    /// </summary>
    /// <param name="toolsRoot">
    /// Where versioned tool directories live; null means <see cref="ClangdInstaller.DefaultToolsRoot"/>
    /// — composed from the installer's own property, so probe and installer cannot drift apart.
    /// </param>
    /// <param name="listToolDirectories">
    /// Directory scan seam; defaults to <c>Directory.GetDirectories(root, "clangd*")</c>.
    /// </param>
    /// <param name="fileExists">Existence probe; defaults to <see cref="File.Exists(string)"/>.</param>
    /// <param name="isWindows">Selects the binary name; defaults to the real OS.</param>
    /// <remarks>
    /// Numeric, not ordinal: an ordinal sort ranks <c>clangd_9</c> above <c>clangd_22</c> and would
    /// pin the probe to an ancient install forever. A missing or unreadable tools root returns null
    /// — that is the pre-first-install NORMAL, not an error.
    /// </remarks>
    public static string? FindInToolsRoot(
        string? toolsRoot = null,
        Func<string, string[]>? listToolDirectories = null,
        Func<string, bool>? fileExists = null,
        bool? isWindows = null)
    {
        var root = toolsRoot ?? ClangdInstaller.DefaultToolsRoot;
        var list = listToolDirectories ?? (r => Directory.GetDirectories(r, ToolsDirPattern));
        var exists = fileExists ?? File.Exists;
        var windows = isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        string[] dirs;
        try
        {
            dirs = list(root) ?? Array.Empty<string>();
        }
        catch
        {
            return null; // no tools root yet — nothing has been installed
        }

        var binaryName = windows ? "clangd.exe" : "clangd";

        var ranked = dirs
            .Select(dir => (dir, version: ParseToolsDirVersion(dir)))
            .OrderByDescending(entry => entry.version != null)
            .ThenByDescending(entry => entry.version);

        foreach (var (dir, _) in ranked)
        {
            try
            {
                var candidate = Path.Combine(dir, "bin", binaryName);
                if (exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch
            {
                // An unreadable versioned dir must not abort the probe; try the next one.
            }
        }

        return null;
    }

    /// <summary>
    /// The version encoded in a versioned install directory's name (<c>clangd_22.1.6</c> → 22.1.6),
    /// or null when the name does not carry one.
    /// </summary>
    private static Version? ParseToolsDirVersion(string directory)
    {
        var name = Path.GetFileName(directory);
        const string prefix = ClangdExecutableName + "_";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return Version.TryParse(name.Substring(prefix.Length), out var version) ? version : null;
    }

    /// <summary>
    /// Sweeps <see cref="WindowsLlvmInstallDirectories"/> for clangd — cheap file-existence probes
    /// only, NEVER a process spawn per directory (the 35s spawn-probe hazard documented at
    /// <see cref="IntelliSenseEmissionService"/>). The one bounded spawn (vswhere) happened when
    /// the directory list was built.
    /// </summary>
    public static string? FindInLlvmInstallDirectories() =>
        ExecutableLocator.FindInDirectories(
            ClangdExecutableName,
            WindowsLlvmInstallDirectories,
            Environment.GetEnvironmentVariable("PATHEXT"),
            File.Exists,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    /// <summary>
    /// Composes the pinned candidate list from the environment's roots plus
    /// <paramref name="vsInstallationPath"/> (null = no VS, its two entries are skipped). A root
    /// the environment cannot supply is skipped rather than emitted as a relative junk path.
    /// Split from the field initializer so tests can pin the SHAPE with an injected VS root.
    /// </summary>
    public static IReadOnlyList<string> BuildWindowsLlvmInstallDirectories(string? vsInstallationPath)
    {
        var dirs = new List<string>(9);

        void AddUnder(string? root, params string[] parts)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var path = root!;
            foreach (var part in parts) path = Path.Combine(path, part);
            dirs.Add(path);
        }

        AddUnder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin");
        AddUnder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LLVM", "bin");
        AddUnder(vsInstallationPath, "VC", "Tools", "Llvm", "x64", "bin");
        AddUnder(vsInstallationPath, "VC", "Tools", "Llvm", "bin");

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddUnder(profile, "scoop", "shims");
        AddUnder(profile, "scoop", "apps", "llvm", "current", "bin");

        dirs.Add(@"C:\msys64\mingw64\bin");
        dirs.Add(@"C:\msys64\ucrt64\bin");
        dirs.Add(@"C:\msys64\clang64\bin");

        return dirs;
    }

    /// <summary>
    /// The latest VS installation path via vswhere (the same pattern as
    /// <c>CppToolchain.Find</c>), or null. Bounded at ~2s and fully guarded: this feeds a
    /// static field initializer, where an exception would poison the whole type.
    /// </summary>
    private static string? TryGetVisualStudioInstallationPath()
    {
        try
        {
            var vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere)) return null;

            using var p = Process.Start(new ProcessStartInfo(vswhere,
                "-latest -products * -property installationPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p == null) return null;

            var installPath = p.StandardOutput.ReadToEnd().Trim();
            if (!p.WaitForExit(2000))
            {
                try { p.Kill(); } catch { /* best effort */ }
                return null;
            }

            return installPath.Length == 0 ? null : installPath;
        }
        catch
        {
            return null; // no vswhere, no VS, or a wedged installer — skip the VS-bundled entries
        }
    }
}
