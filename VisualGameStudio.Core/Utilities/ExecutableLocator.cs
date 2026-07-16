using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Searches PATH for a named executable and returns the full path to the first match.
///
/// <para><b>Why a path and not a name.</b> Callers that hand the result to
/// <see cref="System.Diagnostics.ProcessStartInfo.FileName"/> need the path, not the name: a bare
/// name resolves against the child's own search rules, which are not the ones applied here.</para>
///
/// <para><b>PATHEXT.</b> On Windows an executable named <c>clangd</c> is a file called
/// <c>clangd.exe</c>; a literal <c>File.Exists(dir + "clangd")</c> finds nothing. This class
/// expands a name that does not already carry an executable extension across PATHEXT, so callers
/// may pass the bare tool name on every platform. (The predecessor of this code —
/// <c>ShellProfileDetector.FindOnPath</c> — probed literally, and worked only because each of its
/// callers spelled out <c>"pwsh.exe"</c>/<c>"bash.exe"</c>/<c>"nu.exe"</c>. Those names still
/// resolve identically here: a name whose extension is already in PATHEXT is probed literally and
/// only literally.)</para>
///
/// <para>The environment is read in <see cref="Find"/> alone; <see cref="FindIn"/> and
/// <see cref="CandidateNames"/> are pure functions of their arguments, so every rule above can be
/// pinned by tests on a machine that has none of the tools installed.</para>
/// </summary>
public static class ExecutableLocator
{
    /// <summary>
    /// The executable extensions assumed when PATHEXT is unset or empty — the executable subset of
    /// the Windows default. Omitting the script extensions (.VBS/.JS/.WSF/...) is deliberate: a
    /// language server or compiler is not shipped as one, and PATHEXT is in practice always set, so
    /// this is a floor rather than a faithful mirror.
    /// </summary>
    private const string DefaultWindowsPathExt = ".COM;.EXE;.BAT;.CMD";

    /// <summary>
    /// Finds <paramref name="executable"/> on the current process's PATH, or null if it is not
    /// there. <paramref name="executable"/> may be a bare name (<c>"clangd"</c>) or carry an
    /// extension (<c>"pwsh.exe"</c>).
    /// </summary>
    public static string? Find(string? executable) => FindIn(
        executable,
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("PATHEXT"),
        File.Exists,
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    /// <summary>
    /// The <see cref="Find"/> rule with every environmental input supplied explicitly.
    /// </summary>
    /// <param name="executable">Tool name, bare or with an extension.</param>
    /// <param name="pathValue">The PATH value: entries separated by ';' (Windows) or ':'.</param>
    /// <param name="pathExtValue">The PATHEXT value; ignored unless <paramref name="isWindows"/>.</param>
    /// <param name="fileExists">Existence probe (<see cref="File.Exists(string)"/> in production).</param>
    /// <param name="isWindows">Selects the PATH separator and whether PATHEXT applies.</param>
    /// <returns>
    /// The first hit as <c>Path.Combine(pathEntry, candidateName)</c>, or null. Directories are
    /// searched in PATH order and, within each one, candidates in PATHEXT order — so an earlier
    /// PATH entry outranks a higher-priority extension in a later one, as on Windows itself.
    /// <para>
    /// The result is spelled as the probe CONSTRUCTED it, which is not necessarily how the file is
    /// spelled on disk: a PATHEXT of <c>.EXE</c> matching a file named <c>clangd.exe</c> returns
    /// <c>clangd.EXE</c>. That path opens the same file — Windows compares paths case-insensitively,
    /// which is how the match was made — so it is fine to spawn, but do not treat it as the file's
    /// canonical name.
    /// </para>
    /// </returns>
    public static string? FindIn(
        string? executable,
        string? pathValue,
        string? pathExtValue,
        Func<string, bool> fileExists,
        bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        if (string.IsNullOrWhiteSpace(pathValue)) return null;

        var exists = fileExists ?? File.Exists;
        var candidates = CandidateNames(executable, pathExtValue, isWindows);
        var separator = isWindows ? ';' : ':';

        foreach (var entry in pathValue.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = entry.Trim();
            if (dir.Length == 0) continue;

            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.Combine(dir, candidate);
                    if (exists(full)) return full;
                }
                catch
                {
                    // Defensive, carried over from the predecessor: whatever a single PATH entry can
                    // do to Path.Combine or to the probe, it must not abort the search. Give up on
                    // this directory (its remaining candidates would fail the same way) and keep
                    // looking down the rest of PATH.
                    break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The file names to probe for <paramref name="executable"/>, in priority order.
    /// </summary>
    /// <returns>
    /// Off Windows, or when the name already ends in an extension listed in
    /// <paramref name="pathExtValue"/>: the name alone. Otherwise: the name, followed by the name
    /// plus each PATHEXT extension (trimmed, dot-prefixed, de-duplicated case-insensitively).
    /// </returns>
    /// <remarks>
    /// The bare name leads even on Windows, preserving the predecessor's exact behavior for names
    /// that have no extension at all; PATHEXT candidates are additions, never replacements.
    /// </remarks>
    public static IReadOnlyList<string> CandidateNames(
        string executable,
        string? pathExtValue,
        bool isWindows)
    {
        if (!isWindows) return new[] { executable };

        var extensions = ParseExtensions(pathExtValue);

        var current = Path.GetExtension(executable);
        if (!string.IsNullOrEmpty(current))
        {
            foreach (var ext in extensions)
            {
                if (string.Equals(ext, current, StringComparison.OrdinalIgnoreCase))
                    return new[] { executable };
            }
        }

        var names = new List<string>(extensions.Count + 1) { executable };
        foreach (var ext in extensions)
            names.Add(executable + ext);
        return names;
    }

    /// <summary>Splits a PATHEXT value into normalized, de-duplicated extensions.</summary>
    private static IReadOnlyList<string> ParseExtensions(string? pathExtValue)
    {
        var raw = string.IsNullOrWhiteSpace(pathExtValue) ? DefaultWindowsPathExt : pathExtValue;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = part.Trim();
            if (ext.Length == 0) continue;
            if (!ext.StartsWith('.')) ext = "." + ext;
            if (seen.Add(ext)) result.Add(ext);
        }

        return result;
    }
}
