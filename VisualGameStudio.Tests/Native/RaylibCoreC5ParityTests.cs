using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C5 (raw raylib screen-space / camera-math passthroughs). Pure
/// text scan — no engine load. None of the 8 names collide with existing exports or convenience helpers, so all bind
/// unsuffixed with raylib's exact spelling. The trailing '(' anchor keeps near-name boundaries apart
/// (Framework_GetWorldToScreen( != Framework_GetWorldToScreenEx( != Framework_GetWorldToScreen2D().
///
/// This is the opener of the rcore parity module (127-fn gap): screen-space math is fully unit-testable (see
/// <see cref="RaylibScreenSpaceMathTests"/>) and defines the Ray / Camera3D / Matrix structs reused by later batches.
/// </summary>
[TestFixture]
public class RaylibCoreC5ParityTests
{
    // All 8 raw screen-space/camera-math names bind unsuffixed (import name == export name) — no collision.
    private static readonly string[] MathNames =
    {
        "GetScreenToWorldRay", "GetScreenToWorldRayEx", "GetWorldToScreen", "GetWorldToScreenEx",
        "GetWorldToScreen2D", "GetScreenToWorld2D", "GetCameraMatrix", "GetCameraMatrix2D",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_core_C5_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in MathNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(MathNames.Length, Is.EqualTo(8));
        });
    }
}
