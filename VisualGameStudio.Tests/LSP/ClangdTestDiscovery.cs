using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// The clangd discovery trio shared by every fixture that drives a REAL clangd
/// (<see cref="ClangdLaunchTests"/>, <see cref="ClangdE2ETests"/>): locate the executable the
/// way the IDE would, or skip the fixture — with the fixture's own wording — when there is none.
///
/// <para>
/// <b>The ordinal-descending directory pick is deliberate.</b> These tests want "any working
/// clangd" and tolerate ordinal ordering of the <c>clangd*</c> directory names. The PRODUCTION
/// tools probe (<see cref="ClangdLocator.FindInToolsRoot"/>, since <c>da46530</c>) ranks by the
/// NUMERIC version parsed from the directory name instead, because it must never pin a user to
/// <c>clangd_9</c> over <c>clangd_22</c>. That difference is by design — do not "fix" either
/// side to match the other.
/// </para>
/// </summary>
internal static class ClangdTestDiscovery
{
    /// <summary>
    /// The clangd the IDE would use, or null — the skip condition for every real-clangd test.
    /// <para>
    /// The <c>~\.vgs\tools</c> probe is passed as <c>configuredPath</c>, which exercises
    /// Task 11's <c>cpp.clangd.path</c> override branch against a real executable — pinning the
    /// override path deliberately, independent of the production probe. Since <c>da46530</c> the
    /// production chain ALSO probes the same tools root natively, so a machine with clangd under
    /// <c>~\.vgs\tools</c> but off PATH would be found either way; routing the hit through the
    /// override is what keeps that branch exercised against a real process.
    /// </para>
    /// </summary>
    public static string? LocateClangd() =>
        ClangdLocator.ResolveClangdPath(configuredPath: ProbeVgsToolsDir());

    /// <summary>
    /// A <c>clangd.exe</c> under <c>%USERPROFILE%\.vgs\tools\clangd*\bin</c> (the highest
    /// ordinal-sorting directory name wins — any working clangd will do here; see the class doc
    /// for why the production probe ranks differently), or null.
    /// </summary>
    public static string? ProbeVgsToolsDir()
    {
        try
        {
            var toolsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vgs", "tools");
            if (!Directory.Exists(toolsDir)) return null;

            return Directory.GetDirectories(toolsDir, "clangd*")
                .OrderByDescending(dir => dir, StringComparer.OrdinalIgnoreCase)
                .Select(dir => Path.Combine(dir, "bin", "clangd.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null; // An unreadable profile dir must degrade to the PATH probe, not fail the fixture.
        }
    }

    /// <summary>
    /// Skips the calling test when <paramref name="clangdPath"/> is null. The reason is the
    /// caller's: each fixture names the probe locations in its own words, and those messages
    /// are preserved byte-for-byte from before the extraction.
    /// </summary>
    public static void RequireClangd(string? clangdPath, string notInstalledReason)
    {
        if (clangdPath == null)
        {
            Assert.Ignore(notInstalledReason);
        }
    }
}
