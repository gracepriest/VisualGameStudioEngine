using System;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// The F5 seam's pure decisions (Phase 4 Task 9): when to warn that a native launch has no
/// debug info, and what the messages say. Extracted from <c>MainWindowViewModel</c> for the
/// same reason as <see cref="ClangdDownloadFlow"/> — the VM is a DI singleton no test can
/// construct, so every decision worth pinning lives in a class with no dependencies at all.
/// The VM keeps only the plumbing (which output panel, which status bar).
///
/// <para>
/// <b>"Native adapter" is Id equality against
/// <see cref="DebugAdapterDescriptor.LldbDapId"/></b> (ordinal — ids are wire-stable tokens),
/// not <see cref="DebugAdapterDescriptor.Toolchains"/> emptiness. The descriptor pins both
/// halves of that choice: Toolchains is documented as informational pairing metadata that
/// nothing routes on, and descriptors "are compared by Id if at all". The warning is about
/// OUR native build pipeline — <c>CppToolchain.FlagsFor</c> withholding <c>/Zi</c> |
/// <c>-g</c> — which in v1 is exactly the pipeline lldb-dap debugs the output of.
/// </para>
/// </summary>
public static class DebugLaunchPolicy
{
    /// <summary>
    /// True exactly when the session about to start is native (lldb-dap) AND the build
    /// configuration in hand says <see cref="BuildConfiguration.DebugSymbols"/> is off — the
    /// one combination where the exe that just built carries no debug info
    /// (<c>CppToolchain.FlagsFor</c> adds <c>/Zi</c> for MSVC / <c>-g</c> for clang and g++
    /// only when DebugSymbols is on). Managed builds get PDBs on their own rules, and a null
    /// configuration gives the warning nothing to accuse — both stay silent. Warn, never
    /// block: launching Release under a debugger is legitimate.
    /// </summary>
    public static bool ShouldWarnNoDebugInfo(
        DebugAdapterDescriptor descriptor, BuildConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return string.Equals(descriptor.Id, DebugAdapterDescriptor.LldbDapId, StringComparison.Ordinal)
            && configuration is { DebugSymbols: false };
    }

    /// <summary>
    /// The Output-panel warning for a native launch whose configuration builds without debug
    /// info. Names the configuration it accuses and the mechanism (<c>/Zi</c> | <c>-g</c> are
    /// only emitted when the configuration's DebugSymbols is on — <c>CppToolchain.FlagsFor</c>),
    /// so the fix is discoverable from the message alone.
    /// </summary>
    public static string ComposeNoDebugInfoWarning(string configurationName) =>
        $"Warning: configuration '{configurationName}' builds without debug info — the C++ " +
        "toolchain emits /Zi (MSVC) or -g (clang/g++) only when the configuration's " +
        "DebugSymbols is on, so breakpoints will not bind and stepping will show no source. " +
        "Switch to a configuration with DebugSymbols enabled for source-level debugging.";

    /// <summary>
    /// The message for an adapter the registry routes to but
    /// <see cref="DebugAdapterDescriptor.ResolveLaunchCommand"/> cannot find on disk: the
    /// adapter's display name plus BOTH remedies — the one-click download (Tools menu) and the
    /// <see cref="DebugAdapterDescriptor.LldbDapSettingsKey"/> override. Task 12 upgrades the
    /// delivery from plain Output text to the offer toast; the wording here is the contract.
    /// </summary>
    public static string ComposeAdapterMissingMessage(DebugAdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return $"{descriptor.DisplayName} is not installed. " +
            "Install it via Tools → Download C++ Debugger, or point the " +
            $"{DebugAdapterDescriptor.LldbDapSettingsKey} " +
            "setting at an existing lldb-dap executable.";
    }
}
