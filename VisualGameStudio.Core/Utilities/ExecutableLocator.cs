using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Searches PATH for a named executable and returns the absolute path of the first match.
///
/// <para><b>Why a path and not a name.</b> Callers that hand the result to
/// <see cref="System.Diagnostics.ProcessStartInfo.FileName"/> need the path, not the name: a bare
/// name resolves against the child's own search rules, which are not the ones applied here. For
/// the same reason <see cref="Find"/> returns an ABSOLUTE path — PATH entries are absolute by
/// convention but not by guarantee, and a relative hit would re-resolve against the spawner's
/// working directory. (<see cref="FindIn"/>, being pure, cannot read the working directory and so
/// returns the hit as joined; <see cref="Find"/> is where that is settled.)</para>
///
/// <para><b>PATHEXT.</b> On Windows an executable named <c>clangd</c> is a file called
/// <c>clangd.exe</c>; a literal <c>File.Exists(dir + "clangd")</c> finds nothing. This class
/// resolves a name that does not already carry an executable extension THROUGH PATHEXT, so callers
/// may pass the bare tool name on every platform. (The predecessor of this code —
/// <c>ShellProfileDetector.FindOnPath</c> — probed literally, and worked only because each of its
/// callers spelled out <c>"pwsh.exe"</c>/<c>"bash.exe"</c>/<c>"nu.exe"</c>. Such names are
/// unaffected: a name whose extension is already in PATHEXT is probed literally and only
/// literally.)</para>
///
/// <para>The environment is read in <see cref="Find"/> alone; <see cref="FindIn"/> and
/// <see cref="CandidateNames"/> are pure functions of their arguments, so every rule above can be
/// pinned by tests on a machine that has none of the tools installed.</para>
/// </summary>
public static class ExecutableLocator
{
    /// <summary>
    /// The executable extensions assumed when PATHEXT is unset or empty — the executable subset of
    /// the Windows default. A floor rather than a faithful mirror: the script extensions
    /// (.VBS/.JS/.WSF/...) are omitted deliberately, since the tools looked for here (a language
    /// server, a compiler) are not shipped as scripts. Windows sets PATHEXT itself, so this is a
    /// fallback for a stripped environment, not the normal path.
    /// </summary>
    private const string DefaultWindowsPathExt = ".COM;.EXE;.BAT;.CMD";

    /// <summary>
    /// Finds <paramref name="executable"/> on the current process's PATH and returns its absolute
    /// path, or null if it is not there. <paramref name="executable"/> may be a bare name
    /// (<c>"clangd"</c>) or carry an extension (<c>"pwsh.exe"</c>).
    /// </summary>
    /// <remarks>
    /// The hit is resolved to an absolute path because a PATH entry may be relative (<c>.</c>,
    /// <c>bin</c>), and a relative result would re-resolve against the working directory of
    /// whoever spawns it — not necessarily this one. That resolution is not a new behavior being
    /// introduced: the existence probe below already interpreted the path against THIS process's
    /// working directory, so this pins the very file just proven to exist, at the moment of proof.
    /// It belongs here rather than in <see cref="FindIn"/> because the working directory is
    /// environment, and <see cref="FindIn"/> is pure.
    /// </remarks>
    public static string? Find(string? executable) => FindIn(
        executable,
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("PATHEXT"),
        File.Exists,
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) is string hit
            ? Path.GetFullPath(hit)
            : null;

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
    /// Off Windows: the name alone (POSIX has no PATHEXT). On Windows, when the name already ends
    /// in an extension listed in <paramref name="pathExtValue"/>: the name alone. Otherwise: the
    /// name plus each PATHEXT extension (trimmed, dot-prefixed, de-duplicated case-insensitively) —
    /// the bare name is NOT among them.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Dropping the bare name on Windows is what makes this match Windows' own resolution of a bare
    /// command: an extension in PATHEXT is what makes a file executable BY NAME, so a bare
    /// <c>clangd</c> means "the clangd executable", not "a file literally called clangd".
    /// </para>
    /// <para>
    /// It is also the difference between finding clangd and finding a decoy. MSYS2, Cygwin and
    /// git-for-windows all install extensionless shims beside real <c>.exe</c>s, so
    /// <c>clangd</c> and <c>clangd.exe</c> in one directory is an ordinary shape — and returning
    /// the extensionless one would hand the caller a path to spawn that Windows' own name
    /// resolution would never have selected.
    /// </para>
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

        var names = new List<string>(extensions.Count);
        foreach (var ext in extensions)
            names.Add(executable + ext);
        return names;
    }

    /// <summary>
    /// Splits a PATHEXT value into normalized, de-duplicated extensions, falling back to
    /// <see cref="DefaultWindowsPathExt"/> when it yields none.
    /// </summary>
    /// <remarks>
    /// The fallback covers an empty parse (<c>";;;"</c>), not just an empty value: since the bare
    /// name is not a candidate on Windows, returning zero extensions here would mean zero candidates
    /// and a search that silently never matches anything.
    /// </remarks>
    private static IReadOnlyList<string> ParseExtensions(string? pathExtValue)
    {
        var parsed = SplitExtensions(pathExtValue);
        return parsed.Count > 0 ? parsed : SplitExtensions(DefaultWindowsPathExt);
    }

    private static List<string> SplitExtensions(string? pathExtValue)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(pathExtValue)) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in pathExtValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = part.Trim();
            if (ext.Length == 0) continue;
            if (!ext.StartsWith('.')) ext = "." + ext;
            if (seen.Add(ext)) result.Add(ext);
        }

        return result;
    }
}
