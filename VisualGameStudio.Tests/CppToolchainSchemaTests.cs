using System;
using System.IO;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 3 — the six cpp.toolchain.* keys must exist in the settings schema (with an empty-string
/// default) before any consumer (BuildService, dialog, F5 site) can be built on top of them.
/// </summary>
[TestFixture]
public class CppToolchainSchemaTests
{
    [Test]
    public void All_Six_Toolchain_Keys_Are_In_Schema_With_Empty_Default()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CppToolchainSchema_{Guid.NewGuid()}");
        Directory.CreateDirectory(home);
        using var svc = new SettingsService(home);
        try
        {
            foreach (var id in CppToolchainOverrides.Backends)
                foreach (var key in new[] { CppToolchainOverrides.CompilerKey(id), CppToolchainOverrides.DebuggerKey(id) })
                {
                    Assert.That(svc.GetPropertySchema(key), Is.Not.Null, key);       // schema membership
                    Assert.That(svc.Get<string>(key, "sentinel"), Is.EqualTo(""));   // schema default seeds ""
                }
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { }
        }
    }
}
