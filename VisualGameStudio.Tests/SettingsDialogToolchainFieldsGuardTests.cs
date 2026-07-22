using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 11 guard: the six per-backend <c>cpp.toolchain.*</c> FilePath fields actually reach the
/// Settings dialog's live inventory (which is also what makes
/// <see cref="SettingsConsumerContractTests.EveryDialogSettingKey_HasARegisteredConsumer"/> and
/// <see cref="SettingsConsumerContractTests.EveryDialogSettingKey_ExistsInSchema"/> cover them — the
/// consumer was already forced in Task 3), AND the two silent traps the plan review caught can never
/// regress unnoticed:
///
/// <list type="number">
/// <item>The <c>StringValue</c> proxy: <c>SearchableSettingItem.StringValue</c> reads/writes through
/// <c>SettingsViewModel.GetStringSetting</c>/<c>SetStringSetting</c>, both explicit per-property
/// switches with no default fall-through. Miss a case on either switch and the typed VM property
/// silently stops round-tripping through the dialog's bound control (a dead textbox) even though the
/// inventory test above still passes.</item>
/// <item>User-scope forcing (spec §1): <c>AutoSaveSettingToService</c>/<c>LoadFromService</c> must
/// force <see cref="SettingsScope.User"/> for these six keys regardless of the dialog's
/// <see cref="SettingsViewModel.ActiveScope"/> — a per-backend compiler/debugger override is global
/// toolchain config, not a per-workspace setting. Miss this and a Workspace-scope edit would silently
/// land in the wrong scope (invisible unless you check the *other* scope's raw store, exactly what
/// the second test below does).</item>
/// </list>
/// </summary>
[TestFixture]
public class SettingsDialogToolchainFieldsGuardTests
{
    private static SettingsService MakeService(out string home)
    {
        home = Path.Combine(Path.GetTempPath(), $"SettingsDialogToolchainFields_{Guid.NewGuid()}");
        Directory.CreateDirectory(home);
        return new SettingsService(home);
    }

    [Test]
    public void Six_Toolchain_Keys_Are_In_The_Dialog_Inventory()
    {
        var service = MakeService(out var home);
        try
        {
            var vm = new SettingsViewModel(service);
            foreach (var id in CppToolchainOverrides.Backends)
                foreach (var key in new[] { CppToolchainOverrides.CompilerKey(id), CppToolchainOverrides.DebuggerKey(id) })
                    Assert.That(vm.DialogSettingKeys, Does.Contain(key), key);
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    // Round-trips through the GetStringSetting/SetStringSetting proxy AND lands at User scope even
    // when the dialog's active scope is Workspace (spec §1). Exercised via SearchableSettingItem.
    // StringValue — the ACTUAL AXAML binding surface (Text="{Binding StringValue, Mode=TwoWay}") — not
    // the raw [ObservableProperty] directly: setting the typed VM property (e.g. vm.GccCompilerPath = …)
    // never calls SetStringSetting/AutoSaveSettingToService at all in this codebase's architecture (that
    // only happens via the item's proxy, same as every other dialog setting), so it would not actually
    // exercise — and could not catch a regression in — either trap. This is the guard against the two
    // silent traps the plan review caught: without the GetStringSetting/SetStringSetting cases the
    // proxy never round-trips (StringValue's getter would keep returning "" via the `_ => ""` default
    // arm even though something was "set"), and without the User-scope force the value lands in the
    // wrong scope — both invisible to the inventory test alone.
    [Test]
    public void Toolchain_Path_Persists_At_User_Scope_And_Round_Trips()
    {
        var service = MakeService(out var home);
        try
        {
            var vm = new SettingsViewModel(service) { ActiveScope = SettingsScope.Workspace };
            var item = GetItem(vm, CppToolchainOverrides.CompilerKey("gcc"));

            item.StringValue = @"C:\w\g++.exe"; // exercises SetStringSetting + AutoSave

            Assert.That(item.StringValue, Is.EqualTo(@"C:\w\g++.exe"),
                "GetStringSetting/SetStringSetting proxy must round-trip");
            Assert.That(service.Get<string>(CppToolchainOverrides.CompilerKey("gcc"), "", SettingsScope.User),
                Is.EqualTo(@"C:\w\g++.exe"),
                "cpp.toolchain.* keys must persist at User scope regardless of ActiveScope");
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    // Same two traps, exercised for all six properties (not just GccCompilerPath) so a missed case on
    // any single one of the twelve switch arms (six GetStringSetting + six SetStringSetting) fails by
    // name rather than only being caught by luck of which single property the smaller test above picks.
    [Test]
    public void All_Six_Toolchain_Properties_Persist_At_User_Scope_And_Round_Trip()
    {
        var service = MakeService(out var home);
        try
        {
            var vm = new SettingsViewModel(service) { ActiveScope = SettingsScope.Workspace };

            void Check(string key, string value)
            {
                var item = GetItem(vm, key);
                item.StringValue = value;
                Assert.That(item.StringValue, Is.EqualTo(value), $"{key}: GetStringSetting/SetStringSetting proxy");
                Assert.That(service.Get<string>(key, "", SettingsScope.User), Is.EqualTo(value),
                    $"{key}: must persist at User scope regardless of ActiveScope");
            }

            Check(CppToolchainOverrides.CompilerKey("llvm"), @"C:\w\clang++.exe");
            Check(CppToolchainOverrides.DebuggerKey("llvm"), @"C:\w\lldb-dap.exe");
            Check(CppToolchainOverrides.CompilerKey("gcc"), @"C:\w\g++.exe");
            Check(CppToolchainOverrides.DebuggerKey("gcc"), @"C:\w\lldb-dap.exe");
            Check(CppToolchainOverrides.CompilerKey("msvc"), @"C:\VS\VC\Auxiliary\Build\vcvars64.bat");
            Check(CppToolchainOverrides.DebuggerKey("msvc"), @"C:\w\lldb-dap.exe");
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    // ResetToDefault/IsModified must handle the FilePath control kind (else the six fields can't be
    // reset and never show the Modified badge).
    [Test]
    public void FilePath_Item_Supports_IsModified_And_ResetToDefault()
    {
        var service = MakeService(out var home);
        try
        {
            var vm = new SettingsViewModel(service);
            var key = CppToolchainOverrides.CompilerKey("gcc");
            var item = vm.DialogSettingKeys.Contains(key)
                ? GetItem(vm, key)
                : throw new InvalidOperationException($"{key} missing from dialog inventory");

            Assert.That(item.IsFilePath, Is.True);
            Assert.That(item.IsModified, Is.False, "blank default => not modified");

            item.StringValue = @"C:\w\g++.exe";
            Assert.That(item.IsModified, Is.True);

            item.ResetToDefaultCommand.Execute(null);
            Assert.That(item.StringValue, Is.EqualTo(""));
            Assert.That(item.IsModified, Is.False);
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    private static SearchableSettingItem GetItem(SettingsViewModel vm, string key)
    {
        // BuildCategories/UpdateCategorySettings populate CategorySettings for the selected category;
        // select the C++ category (where the six toolchain fields live) and pull the item out of it.
        var cppCategory = vm.Categories.First(c => c.Id == "cpp");
        vm.SelectedCategory = cppCategory;
        return vm.CategorySettings.First(i => i.Key == key);
    }
}
