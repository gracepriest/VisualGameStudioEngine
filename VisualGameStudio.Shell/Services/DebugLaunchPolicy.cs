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
    /// The configuration feed for <see cref="ShouldWarnNoDebugInfo"/>: the PROJECT's own
    /// per-configuration entry, via the same <see cref="BasicLangProject.GetConfiguration"/>
    /// lookup the build itself performs (BuildService and CppProjectBuilder both read the
    /// .blproj per-config table). Never feed <c>IBuildService.CurrentConfiguration</c> here —
    /// that getter fabricates a fresh <see cref="BuildConfiguration"/> on every read, and
    /// DebugSymbols defaults TRUE, so the warning would be silenced forever. Null when there
    /// is no project or no configuration name — nothing to accuse, no warning.
    /// </summary>
    public static BuildConfiguration? ResolveActiveConfiguration(
        BasicLangProject? project, string? configurationName)
    {
        if (project is null || string.IsNullOrEmpty(configurationName)) return null;

        // GetConfiguration falls back (first entry, then a DebugSymbols=true default) rather
        // than returning null — the conservative direction: an unknown name can only ever
        // SUPPRESS the warning, never invent one.
        return project.GetConfiguration(configurationName);
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
    /// <see cref="DebugAdapterDescriptor.ResolveLaunchCommand"/> cannot find on disk. The
    /// remedies are descriptor-conditional: only lldb-dap gets the one-click download (Tools
    /// menu) and the <see cref="DebugAdapterDescriptor.LldbDapSettingsKey"/> override — for any
    /// other descriptor (a missing managed compiler, a future extension adapter) that advice
    /// would be wrong, so they get the generic line only. Task 12 upgrades the delivery from
    /// plain Output text to the offer toast; the wording here is the contract.
    /// </summary>
    public static string ComposeAdapterMissingMessage(DebugAdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.Equals(descriptor.Id, DebugAdapterDescriptor.LldbDapId, StringComparison.Ordinal))
        {
            return $"{descriptor.DisplayName} is not installed. " +
                "Install it via Tools → Download C++ Debugger, or point the " +
                $"{DebugAdapterDescriptor.LldbDapSettingsKey} " +
                "setting at an existing lldb-dap executable.";
        }

        return $"{descriptor.DisplayName} could not be resolved.";
    }
}
