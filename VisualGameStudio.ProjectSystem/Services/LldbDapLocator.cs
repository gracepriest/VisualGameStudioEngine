using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Decides which lldb-dap executable launches a native C++ debug session. The chain, first
/// answer wins: the <c>cpp.lldbDap.path</c> override (when it names a file that exists) → the
/// IDE-installed copy under <c>~/.vgs/tools</c> → PATH → known install directories → nothing.
/// The structural sibling of <see cref="ClangdLocator"/>, and deliberately the same override rule
/// (<see cref="LanguageService.ResolveLspPathOverride"/>) that <c>basiclang.lsp.path</c> and
/// <c>cpp.clangd.path</c> already follow.
///
/// <para>⚠ NEVER probe lldb-dap by spawning it ("--version" parks on stdin and hangs).
/// File-existence checks only, like FindInLlvmInstallDirectories.</para>
///
/// <para><b>Null is a real answer — and never an empty string.</b> lldb-dap does not ship with
/// the IDE; pre-install, every leg misses and the answer is null.
/// <see cref="DebugAdapterDescriptor.LldbDap"/>'s factory pattern-matches
/// <c>resolveExecutable() is string p</c>, which an empty string would satisfy — composing a
/// launch command around an empty filename that fails at spawn instead of reporting "not
/// installed". Null is what the descriptor turns into a useful answer.</para>
/// </summary>
public static class LldbDapLocator
{
    /// <summary>
    /// The name to look for on PATH. Bare, with no <c>.exe</c>: <see cref="ExecutableLocator"/>
    /// applies PATHEXT, so this one name is correct on Windows and POSIX alike.
    /// </summary>
    public const string LldbDapExecutableName = "lldb-dap";

    /// <summary>
    /// What a versioned install directory under the tools root looks like. Broad on purpose,
    /// like <see cref="ClangdLocator"/>'s <c>clangd*</c>: the probe must keep finding installs
    /// after the pinned release moves on, and the numeric version ranking below picks the newest
    /// among several.
    /// </summary>
    private const string ToolsDirPattern = "lldb-dap*";

    /// <summary>
    /// The one known install location lldb-dap has that clangd's LLVM list does not carry:
    /// winlibs (a GCC+LLVM/Clang/LLD/LLDB MinGW distribution) ships lldb-dap where the official
    /// LLVM Windows installer does not. Appended to THIS locator's list only — see
    /// <see cref="BuildKnownInstallDirectories"/>.
    /// </summary>
    private const string WinlibsMingw64Bin = @"C:\winlibs\mingw64\bin";

    /// <summary>
    /// The conventional Windows install locations swept as the chain's LAST resort, in probe
    /// order: <see cref="ClangdLocator.WindowsLlvmInstallDirectories"/> — every place an LLVM
    /// toolchain conventionally lands is equally where its lldb-dap lands — plus
    /// <see cref="WinlibsMingw64Bin"/> appended at the end. Empty off Windows for now, like the
    /// clangd list it wraps.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownInstallDirectories =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? AppendWinlibs(ClangdLocator.WindowsLlvmInstallDirectories)
            : Array.Empty<string>();

    /// <summary>
    /// Resolves lldb-dap's path from settings, registering this class as the consumer of
    /// <see cref="DebugAdapterDescriptor.LldbDapSettingsKey"/> — the registration and the read it
    /// claims sit together on purpose (see <see cref="SettingsConsumerRegistry"/>).
    /// </summary>
    /// <param name="settingsService">
    /// Source of the override; null means "no override configured", not an error — the probe
    /// chain still runs.
    /// </param>
    /// <returns>The lldb-dap path, or null when no link of the chain yields one.</returns>
    public static string? Locate(ISettingsService? settingsService)
    {
        SettingsConsumerRegistry.RegisterConsumer(
            DebugAdapterDescriptor.LldbDapSettingsKey,
            "LldbDapLocator → lldb-dap executable path override for native debugging");

        return Resolve(
            settingsService?.Get<string>(DebugAdapterDescriptor.LldbDapSettingsKey, ""));
    }

    /// <summary>
    /// The resolution rule: a non-empty <paramref name="configuredPath"/> naming an existing file
    /// wins (trimmed); otherwise the probes run in CHAIN order — <paramref name="toolsProbe"/>
    /// (the IDE-installed <c>~/.vgs/tools</c> copy), then <paramref name="pathProbe"/> (the
    /// user's own PATH arrangement), then <paramref name="knownDirsProbe"/> (known install
    /// locations, the last resort) — first non-null answer wins. A configured path that does NOT
    /// exist falls through to the chain rather than failing — a stale override left behind by an
    /// uninstall should degrade to auto-detection.
    /// </summary>
    /// <param name="fileExists">
    /// Existence probe for the override; defaults to <see cref="File.Exists(string)"/>.
    /// </param>
    /// <param name="toolsProbe">
    /// The <c>~/.vgs/tools</c> step; defaults to <see cref="FindInToolsRoot"/>.
    /// </param>
    /// <param name="pathProbe">
    /// The PATH step; defaults to searching PATH for <see cref="LldbDapExecutableName"/>.
    /// </param>
    /// <param name="knownDirsProbe">
    /// The known-install-dir step; defaults to <see cref="FindInKnownInstallDirectories"/>.
    /// </param>
    /// <remarks>
    /// Pure and static with every dependency injectable, matching
    /// <see cref="ClangdLocator.ResolveClangdPath"/>: it is the only seam these rules can be
    /// pinned through (the assembly exposes no internals to the test project), and what lldb-dap
    /// a machine carries varies — probes hardwired to the real disk could not be asserted stably.
    /// </remarks>
    public static string? Resolve(
        string? configuredPath,
        Func<string, bool>? fileExists = null,
        Func<string?>? toolsProbe = null,
        Func<string?>? pathProbe = null,
        Func<string?>? knownDirsProbe = null)
    {
        var overridePath = LanguageService.ResolveLspPathOverride(configuredPath, fileExists);
        if (overridePath != null) return overridePath;

        var tools = toolsProbe ?? (() => FindInToolsRoot());
        if (tools() is string toolsHit) return toolsHit;

        var path = pathProbe ?? (() => ExecutableLocator.Find(LldbDapExecutableName));
        if (path() is string pathHit) return pathHit;

        var knownDirs = knownDirsProbe ?? (() => FindInKnownInstallDirectories());
        return knownDirs();
    }

    /// <summary>
    /// Probes the tools root for an installed lldb-dap: scans <c>lldb-dap*</c> directories, ranks
    /// them by the NUMERIC version parsed from the <c>lldb-dap_</c> suffix (highest first; a
    /// suffix that does not parse ranks below any that does), and returns the first ranked
    /// directory carrying the binary — as an absolute path, or null. Two layouts are probed per
    /// directory, in order: <c>bin/lldb-dap.exe</c> (canonical, matching the clangd install
    /// shape) and then <c>lldb-dap.exe</c> at the directory root (tolerated — release archives
    /// differ in whether they carry a <c>bin/</c> level). <c>.exe</c>-less off Windows.
    /// </summary>
    /// <param name="toolsRoot">
    /// Where versioned tool directories live; null means <see cref="ClangdInstaller.DefaultToolsRoot"/>
    /// — the same root the clangd probe and installer share, so all IDE-acquired tools live
    /// together.
    /// </param>
    /// <param name="listToolDirectories">
    /// Directory scan seam; defaults to <c>Directory.GetDirectories(root, "lldb-dap*")</c>.
    /// </param>
    /// <param name="fileExists">Existence probe; defaults to <see cref="File.Exists(string)"/>.</param>
    /// <param name="isWindows">Selects the binary name; defaults to the real OS.</param>
    /// <remarks>
    /// Numeric, not ordinal: an ordinal sort ranks <c>lldb-dap_9</c> above <c>lldb-dap_22</c> and
    /// would pin the probe to an ancient install forever. A missing or unreadable tools root
    /// returns null — that is the pre-first-install NORMAL, not an error.
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

        var binaryName = windows ? "lldb-dap.exe" : "lldb-dap";

        var ranked = dirs
            .Select(dir => (dir, version: ParseToolsDirVersion(dir)))
            .OrderByDescending(entry => entry.version != null)
            .ThenByDescending(entry => entry.version);

        foreach (var (dir, _) in ranked)
        {
            try
            {
                var inBin = Path.Combine(dir, "bin", binaryName);
                if (exists(inBin)) return Path.GetFullPath(inBin);

                var atRoot = Path.Combine(dir, binaryName);
                if (exists(atRoot)) return Path.GetFullPath(atRoot);
            }
            catch
            {
                // An unreadable versioned dir must not abort the probe; try the next one.
            }
        }

        return null;
    }

    /// <summary>
    /// The version encoded in a versioned install directory's name
    /// (<c>lldb-dap_22.1.0</c> → 22.1.0), or null when the name does not carry one.
    /// </summary>
    private static Version? ParseToolsDirVersion(string directory)
    {
        var name = Path.GetFileName(directory);
        const string prefix = LldbDapExecutableName + "_";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return Version.TryParse(name.Substring(prefix.Length), out var version) ? version : null;
    }

    /// <summary>
    /// Sweeps the known install directories for lldb-dap — cheap file-existence probes only,
    /// NEVER a process spawn per directory (see the class doc: lldb-dap has no safe probe
    /// invocation at all, <c>--version</c> parks on stdin). Defaults sweep
    /// <see cref="KnownInstallDirectories"/> against the real environment; every input is a seam
    /// so tests can pin the sweep's shape on any machine.
    /// </summary>
    public static string? FindInKnownInstallDirectories(
        IEnumerable<string>? directories = null,
        string? pathExtValue = null,
        Func<string, bool>? fileExists = null,
        bool? isWindows = null) =>
        ExecutableLocator.FindInDirectories(
            LldbDapExecutableName,
            directories ?? KnownInstallDirectories,
            pathExtValue ?? Environment.GetEnvironmentVariable("PATHEXT"),
            fileExists ?? File.Exists,
            isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    /// <summary>
    /// Composes the pinned candidate list: exactly
    /// <see cref="ClangdLocator.BuildWindowsLlvmInstallDirectories"/> for the same VS root —
    /// where an LLVM toolchain lands, its lldb-dap lands — plus <see cref="WinlibsMingw64Bin"/>
    /// appended last. The winlibs entry belongs to THIS list only; do not add it to the clangd
    /// list (it would flip nothing today, but the lists are deliberately separate). Split from
    /// the field initializer so tests can pin the shape with an injected VS root.
    /// </summary>
    public static IReadOnlyList<string> BuildKnownInstallDirectories(string? vsInstallationPath) =>
        AppendWinlibs(ClangdLocator.BuildWindowsLlvmInstallDirectories(vsInstallationPath));

    private static IReadOnlyList<string> AppendWinlibs(IReadOnlyList<string> llvmDirectories)
    {
        var dirs = new List<string>(llvmDirectories.Count + 1);
        dirs.AddRange(llvmDirectories);
        dirs.Add(WinlibsMingw64Bin);
        return dirs;
    }
}
