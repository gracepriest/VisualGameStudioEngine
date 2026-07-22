using System;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public enum OverrideState { None, Usable, Invalid }

public readonly record struct ToolchainOverride(OverrideState State, string? ResolvedPath, string Message);

/// <summary>
/// Reads the six per-backend cpp.toolchain.* override paths, validates them, and
/// returns a tri-state. DI singleton; the single reader for BuildService, DebugService's
/// F5 caller (MainWindowViewModel) and CppToolchainProbeService.
/// </summary>
public sealed class CppToolchainOverrides
{
    public static string CompilerKey(string id) => $"cpp.toolchain.{id}.compiler";
    public static string DebuggerKey(string id) => $"cpp.toolchain.{id}.debugger";
    public static readonly string[] Backends = { "llvm", "gcc", "msvc" };

    private readonly ISettingsService? _settings;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _dirExists;
    private readonly Func<string, VersionProbeResult>? _versionProbe;

    public CppToolchainOverrides(ISettingsService? settings,
        Func<string, bool>? fileExists = null, Func<string, bool>? dirExists = null,
        Func<string, VersionProbeResult>? versionProbe = null)
    {
        _settings = settings;
        _fileExists = fileExists ?? System.IO.File.Exists;
        _dirExists = dirExists ?? System.IO.Directory.Exists;
        _versionProbe = versionProbe;
    }

    public ToolchainOverride ResolveCompiler(string id) => Resolve(id, ToolchainSlotKind.Compiler);

    public ToolchainOverride ResolveDebugger(string id) => Resolve(id, ToolchainSlotKind.Debugger);

    private ToolchainOverride Resolve(string id, ToolchainSlotKind kind)
    {
        // Normalize BEFORE building the key: CppToolchain.TryFindById and every sibling
        // treat backend ids case-insensitively, so an un-normalized id (e.g. UI-sourced,
        // Task 11) must not silently build a differently-cased key that reads as unset.
        id = id?.Trim().ToLowerInvariant() ?? "";
        var key = kind == ToolchainSlotKind.Compiler ? CompilerKey(id) : DebuggerKey(id);
        var slotName = kind == ToolchainSlotKind.Compiler ? "compiler" : "debugger";
        SettingsConsumerRegistry.RegisterConsumer(key, $"CppToolchainOverrides → {id} {slotName} path override");
        var raw = _settings?.Get<string>(key, "") ?? "";
        var vr = ToolchainPathValidator.Validate(id, kind, raw, _fileExists, _dirExists, _versionProbe);
        return vr.Status switch
        {
            ToolchainPathStatus.Empty => new(OverrideState.None, null, ""),
            ToolchainPathStatus.Invalid => new(OverrideState.Invalid, null, vr.Message),
            _ => new(OverrideState.Usable, vr.ResolvedPath, vr.Message), // Valid or Warning
        };
    }

    /// <summary>Forces consumer registration for all six keys (for the settings contract test).</summary>
    public void RegisterAllConsumers()
    {
        foreach (var id in Backends) { ResolveCompiler(id); ResolveDebugger(id); }
    }
}
