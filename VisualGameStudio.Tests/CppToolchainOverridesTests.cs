using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

[TestFixture]
public class CppToolchainOverridesTests
{
    /// <summary>
    /// Minimal <see cref="ISettingsService"/> stub. Only <see cref="Get{T}"/> has real
    /// behavior (backed by an in-memory dictionary, settable via the indexer for a terse
    /// collection-initializer syntax in tests); every other member is unused by
    /// <see cref="CppToolchainOverrides"/> and throws.
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

    [Test]
    public void Blank_Override_Is_None()
    {
        var ov = new CppToolchainOverrides(new FakeSettings(), fileExists: _ => true);
        Assert.That(ov.ResolveCompiler("gcc").State, Is.EqualTo(OverrideState.None));
    }

    [Test]
    public void Set_Existing_Gcc_Is_Usable_And_Registers_Consumer()
    {
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = @"C:\w\g++.exe" };
        var ov = new CppToolchainOverrides(settings, fileExists: p => p == @"C:\w\g++.exe");
        var r = ov.ResolveCompiler("gcc");
        Assert.That(r.State, Is.EqualTo(OverrideState.Usable));
        Assert.That(r.ResolvedPath, Is.EqualTo(@"C:\w\g++.exe"));
        Assert.That(SettingsConsumerRegistry.IsRegistered("cpp.toolchain.gcc.compiler"), Is.True);
    }

    [Test]
    public void Set_Missing_Is_Invalid()
    {
        var settings = new FakeSettings { ["cpp.toolchain.llvm.compiler"] = @"C:\nope.exe" };
        var ov = new CppToolchainOverrides(settings, fileExists: _ => false);
        Assert.That(ov.ResolveCompiler("llvm").State, Is.EqualTo(OverrideState.Invalid));
    }

    [Test]
    public void ResolveCompiler_Msvc_With_Fake_Vcvars_Is_Usable()
    {
        var bat = @"C:\VS\VC\Auxiliary\Build\vcvars64.bat";
        var settings = new FakeSettings { ["cpp.toolchain.msvc.compiler"] = bat };
        var ov = new CppToolchainOverrides(settings, fileExists: p => p == bat);
        var r = ov.ResolveCompiler("msvc");
        Assert.That(r.State, Is.EqualTo(OverrideState.Usable));
        Assert.That(r.ResolvedPath, Is.EqualTo(bat));
    }

    [Test]
    public void ResolveCompiler_Is_Case_Insensitive_On_Backend_Id()
    {
        // The lowercase key is what the schema/dialog actually persist under; an
        // uppercase caller-supplied id (e.g. a differently-cased UI source) must still
        // resolve it rather than silently reading as unset.
        var settings = new FakeSettings { ["cpp.toolchain.llvm.compiler"] = @"C:\llvm\bin\clang++.exe" };
        var ov = new CppToolchainOverrides(settings, fileExists: p => p == @"C:\llvm\bin\clang++.exe");
        var r = ov.ResolveCompiler("LLVM");
        Assert.That(r.State, Is.EqualTo(OverrideState.Usable));
    }

    // ---- UsableCompilerToolchain: the DRY'd resolver shared by BuildService's pinned
    // resolveById and IntelliSenseEmissionService's regen-caller resolver (both build
    // "usable override -> FromExplicit, else PATH probe" and must not duplicate the lambda).

    [Test]
    public void UsableCompilerToolchain_UsableOverride_ReturnsFromExplicit_NeverProbesPath()
    {
        var path = @"C:\w\g++.exe";
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = path };
        var ov = new CppToolchainOverrides(settings, fileExists: p => p == path);

        var tc = ov.UsableCompilerToolchain("gcc",
            pathResolve: _ => throw new InvalidOperationException(
                "must not fall through to the PATH probe when the override is Usable"));

        Assert.That(tc, Is.Not.Null);
        Assert.That(tc!.DriverName, Is.EqualTo(path));
    }

    [Test]
    public void UsableCompilerToolchain_NoneOverride_FallsThroughToPathResolve()
    {
        var ov = new CppToolchainOverrides(new FakeSettings(), fileExists: _ => true);
        var calledWith = "";
        var probed = CppToolchain.FromExplicit("llvm", @"C:\path-probe\clang++.exe");

        var tc = ov.UsableCompilerToolchain("llvm", pathResolve: id => { calledWith = id; return probed; });

        Assert.That(calledWith, Is.EqualTo("llvm"));
        Assert.That(tc, Is.SameAs(probed));
    }

    // The helper only special-cases Usable; None AND Invalid both fall through to
    // pathResolve as non-candidates in the same way — the None/Invalid distinction (hard-error
    // vs. silent candidacy skip) is the CALLER's job via ResolveCompiler directly, per the
    // helper's own doc comment.
    [Test]
    public void UsableCompilerToolchain_InvalidOverride_AlsoFallsThroughToPathResolve()
    {
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = @"C:\nope.exe" };
        var ov = new CppToolchainOverrides(settings, fileExists: _ => false);
        var probeCalled = false;

        var tc = ov.UsableCompilerToolchain("gcc", pathResolve: _ => { probeCalled = true; return null; });

        Assert.That(probeCalled, Is.True);
        Assert.That(tc, Is.Null);
    }

    [Test]
    public void UsableCompilerToolchain_NoPathResolveSupplied_DefaultsToTryFindById()
    {
        // An id TryFindById does not recognize (its switch only knows llvm/gcc/msvc) resolves
        // deterministically to null without spawning any real probe process.
        var ov = new CppToolchainOverrides(new FakeSettings(), fileExists: _ => true);
        Assert.That(ov.UsableCompilerToolchain("borland"), Is.Null);
    }
}
