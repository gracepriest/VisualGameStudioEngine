using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 8: <see cref="CppToolchainProbeService"/> applies the authoritative-when-set rule
/// on top of the pure PATH/vswhere probe. "On PATH" / "off PATH" are faked through the
/// ctor's <c>baseProbe</c> test seam so these tests never depend on a real installed
/// toolchain — winlibs stays OFF PATH (standing constraint).
/// </summary>
[TestFixture]
public class CppToolchainProbeOverrideTests
{
    /// <summary>
    /// Minimal <see cref="ISettingsService"/> stub — mirrors CppToolchainOverridesTests'
    /// FakeSettings. Only <see cref="Get{T}"/> has real behavior; everything else is unused
    /// by <see cref="CppToolchainOverrides"/> and throws.
    /// </summary>
    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = new();

        public string this[string key]
        {
            set => _values[key] = value;
        }

        public T Get<T>(string key, T defaultValue, SettingsScope scope = SettingsScope.Effective)
        {
            if (_values.TryGetValue(key, out var raw) && raw is T typed) return typed;
            return defaultValue;
        }

        public T? GetValue<T>(string key, T? defaultValue = default) => throw new NotImplementedException();
        public object? Get(string key, SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public void Set<T>(string key, T value, SettingsScope scope = SettingsScope.User) => throw new NotImplementedException();
        public void SetValue<T>(string key, T value) => throw new NotImplementedException();
        public void Remove(string key, SettingsScope scope = SettingsScope.User) => throw new NotImplementedException();
        public bool Has(string key, SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, object?> GetAll(SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, object?> GetSection(string prefix, SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public void ResetToDefault(string key) => throw new NotImplementedException();
        public void ResetAllToDefaults() => throw new NotImplementedException();
        public Task LoadAsync() => throw new NotImplementedException();
        public Task SaveAsync() => throw new NotImplementedException();
        public Task ImportAsync(string filePath) => throw new NotImplementedException();
        public Task ExportAsync(string filePath, SettingsScope scope = SettingsScope.User) => throw new NotImplementedException();
        public void RegisterSchema(SettingsSchema schema) => throw new NotImplementedException();
        public IReadOnlyList<SettingsSchema> GetSchemas() => throw new NotImplementedException();
        public SettingsPropertySchema? GetPropertySchema(string key) => throw new NotImplementedException();
        public void SetWorkspacePath(string? path) => throw new NotImplementedException();
        public bool HasWorkspace => false;
        public string? WorkspaceSettingsPath => null;
        public bool IsOverriddenInWorkspace(string key) => throw new NotImplementedException();
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        public event EventHandler<string?>? WorkspacePathChanged;
    }

    private static CppToolchainAvailability AllOffPath =>
        new(Llvm: false, Gcc: false, Msvc: false);

    private static CppToolchainAvailability AllOnPath =>
        new(Llvm: true, Gcc: true, Msvc: true);

    // ------------------------------------------------------------------
    // 1. gcc override Usable (fake existing g++) while gcc is OFF PATH per
    //    the faked base probe -> Probe().Gcc == true (the winlibs-off-PATH goal).
    // ------------------------------------------------------------------

    [Test]
    public void UsableOverride_GccOffPath_ProbeGccIsTrue()
    {
        var overridePath = @"C:\fake-override\gcc\bin\g++.exe"; // never on disk, never really executed
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = overridePath };
        var overrides = new CppToolchainOverrides(settings, fileExists: p => p == overridePath);

        var probe = new CppToolchainProbeService(overrides, baseProbe: () => AllOffPath);

        var result = probe.Probe();

        Assert.That(result.Gcc, Is.True,
            "a set, Usable gcc override must mark gcc available even though it's off PATH");
    }

    // ------------------------------------------------------------------
    // 2. gcc override Invalid while gcc IS "on PATH" per the faked base probe
    //    -> Probe().Gcc == false. Proves authoritative-when-set greys the
    //    backend even when the plain PATH probe would have said yes.
    // ------------------------------------------------------------------

    [Test]
    public void InvalidOverride_GccOnPath_ProbeGccIsFalse()
    {
        var badPath = @"C:\does-not-exist\gcc\bin\g++.exe";
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = badPath };
        var overrides = new CppToolchainOverrides(settings, fileExists: _ => false); // -> Invalid

        // gcc IS "on PATH" per this fake base probe -- if the Invalid gate ever fell back
        // to the base probe result, this would resolve Gcc == true and the assertion below
        // would catch the regression.
        var probe = new CppToolchainProbeService(overrides, baseProbe: () => AllOnPath);

        var result = probe.Probe();

        Assert.That(result.Gcc, Is.False,
            "a set, Invalid gcc override must grey the backend even though it's on PATH");
    }

    // ------------------------------------------------------------------
    // 3. Blank override (no settings service / nothing configured) -> the
    //    pure base-probe result passes through unchanged, for every backend.
    // ------------------------------------------------------------------

    [Test]
    public void BlankOverride_FallsThroughToBaseProbe_Unchanged()
    {
        var overrides = new CppToolchainOverrides(null);

        var probeAllOn = new CppToolchainProbeService(overrides, baseProbe: () => AllOnPath);
        var onResult = probeAllOn.Probe();
        Assert.That(onResult.Llvm, Is.True);
        Assert.That(onResult.Gcc, Is.True);
        Assert.That(onResult.Msvc, Is.True);

        var probeAllOff = new CppToolchainProbeService(overrides, baseProbe: () => AllOffPath);
        var offResult = probeAllOff.Probe();
        Assert.That(offResult.Llvm, Is.False);
        Assert.That(offResult.Gcc, Is.False);
        Assert.That(offResult.Msvc, Is.False);
    }

    // ------------------------------------------------------------------
    // 4. Default ctor (no baseProbe seam) wires to the real
    //    CppToolchain.ProbeAvailability without throwing -- sanity that the
    //    production default path is still there for DI-composed callers.
    // ------------------------------------------------------------------

    [Test]
    public void DefaultBaseProbe_UsesRealCppToolchainProbeAvailability_DoesNotThrow()
    {
        var overrides = new CppToolchainOverrides(null);
        var probe = new CppToolchainProbeService(overrides);

        Assert.DoesNotThrow(() => probe.Probe());
    }
}
