using System;
using System.IO;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins <see cref="ClangdLocator"/> — how the IDE decides WHICH clangd to talk to.
///
/// <para>The rule is deliberately the same one <c>basiclang.lsp.path</c> already uses: a user
/// override wins only when it names a file that exists, otherwise auto-probe. The four cases below
/// (configured+exists / configured+missing / empty / probe-found-nothing) are the whole contract.</para>
///
/// <para>Both dependencies are injected: clangd is not installed on this dev box, so a probe wired
/// straight to the real PATH could only ever assert "null" here.</para>
/// </summary>
[TestFixture]
public class ClangdLocatorTests
{
    private const string Key = LanguageServerDescriptor.ClangdSettingsKey;

    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new System.Collections.Generic.HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p);
    }

    // ---- Case 1: a configured override that exists wins outright. ----

    [Test]
    public void ResolveClangdPath_ConfiguredPathExists_WinsAndTheProbeIsNeverRun()
    {
        var configured = @"C:\my\clangd.exe";
        var probeRuns = 0;

        var resolved = ClangdLocator.ResolveClangdPath(
            configured,
            fileExists: Existing(configured),
            pathProbe: () => { probeRuns++; return @"C:\path\clangd.exe"; });

        Assert.That(resolved, Is.EqualTo(configured));
        Assert.That(probeRuns, Is.Zero, "an override that exists must short-circuit the PATH search");
    }

    [Test]
    public void ResolveClangdPath_ConfiguredPath_IsTrimmed()
    {
        var resolved = ClangdLocator.ResolveClangdPath(
            "  C:\\my\\clangd.exe  ",
            fileExists: Existing(@"C:\my\clangd.exe"),
            pathProbe: () => null);

        // Exact bytes: this string becomes a process filename.
        Assert.That(resolved, Is.EqualTo(@"C:\my\clangd.exe"));
    }

    // ---- Case 2: a configured override that does NOT exist falls back to the probe. ----

    [Test]
    public void ResolveClangdPath_ConfiguredPathMissing_FallsBackToTheProbe()
    {
        var probed = @"C:\tools\clangd.exe";

        var resolved = ClangdLocator.ResolveClangdPath(
            @"C:\stale\clangd.exe",
            fileExists: Existing(),      // nothing exists
            pathProbe: () => probed);

        Assert.That(resolved, Is.EqualTo(probed),
            "a stale override (uninstalled/renamed tool) must degrade to auto-detection, not to nothing");
    }

    // ---- Case 3: no override configured → probe. ----

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveClangdPath_NoConfiguredPath_UsesTheProbe(string? configured)
    {
        var probed = @"C:\tools\clangd.exe";

        var resolved = ClangdLocator.ResolveClangdPath(
            configured,
            fileExists: _ => throw new AssertionException(
                "an unset override must not be existence-checked"),
            pathProbe: () => probed);

        Assert.That(resolved, Is.EqualTo(probed));
    }

    // ---- Case 4: the probe finds nothing → null (the honest "no clangd here" answer). ----

    [Test]
    public void ResolveClangdPath_ProbeFindsNothing_ReturnsNull()
    {
        var resolved = ClangdLocator.ResolveClangdPath(
            "",
            fileExists: Existing(),
            pathProbe: () => null);

        Assert.That(resolved, Is.Null,
            "null is the signal Task 12 needs in order to degrade gracefully; a bare \"clangd\" " +
            "fallback string would be spawned and fail late instead");
    }

    [Test]
    public void ResolveClangdPath_ConfiguredMissingAndProbeFindsNothing_ReturnsNull()
    {
        Assert.That(ClangdLocator.ResolveClangdPath(
            @"C:\stale\clangd.exe", Existing(), () => null), Is.Null);
    }

    // ---- The settings read: key, consumer registration, and the resolved result. ----

    [Test]
    public void Locate_ReadsTheClangdSettingsKey_AndReturnsTheOverride()
    {
        var real = Path.Combine(Path.GetTempPath(), $"clangd_locate_{Guid.NewGuid():N}.exe");
        File.WriteAllText(real, "stand-in for a real clangd");
        try
        {
            var settings = new Mock<ISettingsService>();
            settings.Setup(s => s.Get(Key, "", SettingsScope.Effective)).Returns(real);

            var resolved = ClangdLocator.Locate(settings.Object);

            Assert.That(resolved, Is.EqualTo(real));
            settings.Verify(s => s.Get(Key, "", SettingsScope.Effective), Times.Once,
                "the override must be read from cpp.clangd.path — exactly the key the dialog writes");
        }
        finally
        {
            try { File.Delete(real); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Locate_RegistersItselfAsTheConsumerOfTheClangdSettingsKey()
    {
        // The contract test (SettingsConsumerContractTests) demands a registered consumer for every
        // dialog key; this is the registration it forces, sitting next to the read above.
        ClangdLocator.Locate(new Mock<ISettingsService>().Object);

        Assert.That(SettingsConsumerRegistry.IsRegistered(Key), Is.True);
        Assert.That(SettingsConsumerRegistry.Consumers[Key], Does.Contain("ClangdLocator"));
    }

    [Test]
    public void Locate_WithNoSettingsService_StillProbes_AndDoesNotThrow()
    {
        // The registry may construct the locator before settings are wired; a null service means
        // "no override", not a crash.
        Assert.DoesNotThrow(() => ClangdLocator.Locate(null));
    }

    [Test]
    public void ClangdExecutableName_IsTheBareName_NotAWindowsSpecificFileName()
    {
        // Bare on purpose: ExecutableLocator applies PATHEXT, so hardcoding ".exe" here would
        // break the POSIX probe.
        Assert.That(ClangdLocator.ClangdExecutableName, Is.EqualTo("clangd"));
    }
}
