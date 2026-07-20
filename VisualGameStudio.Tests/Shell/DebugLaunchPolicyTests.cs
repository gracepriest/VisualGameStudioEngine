using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.Services;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Pins <see cref="DebugLaunchPolicy"/> — the F5 seam's pure decisions, extracted from
/// <c>MainWindowViewModel.StartDebuggingAsync</c> because the VM is not constructible in tests
/// (the <see cref="ClangdDownloadFlow"/> pattern). Three decisions live here: when the
/// no-debug-info warning fires, what it says, and what the "adapter not installed" message says.
/// The exact strings asserted here are the contract the Task 14 smoke script quotes.
/// </summary>
[TestFixture]
public class DebugLaunchPolicyTests
{
    // Real factory-built descriptors, not stand-ins: the policy's predicate must hold against
    // the same objects the registry routes at F5. The locators return null — installation is
    // irrelevant to every decision under test.
    private static DebugAdapterDescriptor Managed => DebugAdapterDescriptor.BasicLangManaged(() => null);
    private static DebugAdapterDescriptor Lldb => DebugAdapterDescriptor.LldbDap(() => null);

    [Test]
    public void ShouldWarnNoDebugInfo_TrueOnlyForNativeAdapterWithDebugSymbolsOff()
    {
        var symbolsOn = new BuildConfiguration { Name = "Debug", DebugSymbols = true };
        var symbolsOff = new BuildConfiguration { Name = "Release", DebugSymbols = false };

        Assert.Multiple(() =>
        {
            // Managed builds get PDBs from the C# toolchain regardless of this flag's C++
            // meaning — the warning would be noise, so it never fires for the managed adapter.
            Assert.That(DebugLaunchPolicy.ShouldWarnNoDebugInfo(Managed, symbolsOn), Is.False,
                "managed adapter, DebugSymbols on");
            Assert.That(DebugLaunchPolicy.ShouldWarnNoDebugInfo(Managed, symbolsOff), Is.False,
                "managed adapter, DebugSymbols off");
            Assert.That(DebugLaunchPolicy.ShouldWarnNoDebugInfo(Managed, null), Is.False,
                "managed adapter, no configuration");

            Assert.That(DebugLaunchPolicy.ShouldWarnNoDebugInfo(Lldb, symbolsOn), Is.False,
                "lldb-dap, DebugSymbols on");
            Assert.That(DebugLaunchPolicy.ShouldWarnNoDebugInfo(Lldb, symbolsOff), Is.True,
                "lldb-dap, DebugSymbols off — the one warning case");
            // No configuration in hand = nothing to accuse. Stay silent rather than guess.
            Assert.That(DebugLaunchPolicy.ShouldWarnNoDebugInfo(Lldb, null), Is.False,
                "lldb-dap, no configuration");
        });
    }

    [Test]
    public void ComposeNoDebugInfoWarning_NamesTheConfiguration()
    {
        var warning = DebugLaunchPolicy.ComposeNoDebugInfoWarning("Release");

        Assert.Multiple(() =>
        {
            Assert.That(warning, Does.Contain("'Release'"),
                "the warning must name the configuration it accuses");
            // The mechanism, so the fix is discoverable: CppToolchain.FlagsFor adds /Zi (MSVC)
            // or -g (clang/g++) only when the configuration's DebugSymbols is on.
            Assert.That(warning, Does.Contain("/Zi"));
            Assert.That(warning, Does.Contain("-g"));
            Assert.That(warning, Does.Contain("DebugSymbols"));
        });
    }

    [Test]
    public void ComposeAdapterMissingMessage_NamesAdapterAndBothRemedies()
    {
        var message = DebugLaunchPolicy.ComposeAdapterMissingMessage(Lldb);

        // Exact string: the display name plus BOTH remedies — the one-click download and the
        // settings override (DebugAdapterDescriptor.LldbDapSettingsKey), mirroring clangd's
        // missing-tool wording. Task 12's offer toast quotes this contract.
        Assert.That(message, Is.EqualTo(
            "lldb-dap (native C++) is not installed. " +
            "Install it via Tools → Download C++ Debugger, or point the cpp.lldbDap.path " +
            "setting at an existing lldb-dap executable."));
    }
}
