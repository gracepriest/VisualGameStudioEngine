using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rmodels BASIC-3D-SHAPES sub-batch (20 immediate-mode 3D primitive draws;
/// DrawGrid was already bound). All headless (text scan) — fast subset. Checks:
///   1. Every_shape_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 20, PLUS a
///      raylib.h completeness cross-check that the DrawLine3D..DrawGrid range is exactly these 20 + the already-bound
///      DrawGrid, so nothing in the basic-3d-shapes surface was silently missed.
///   2. Wrapper_shape_bindings_declare_the_correct_marshaling — TYPE scan: all 20 are Subs (void); Color is expanded to
///      r,g,b,a Bytes (the 2D-shapes convention); Vector3/Vector2/Ray by value; DrawTriangleStrip3D takes a Vector3() array.
///   3. Shape_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibModelsShapesParityTests
{
    private static readonly string[] Names =
    {
        "DrawLine3D", "DrawPoint3D", "DrawCircle3D", "DrawTriangle3D", "DrawTriangleStrip3D",
        "DrawCube", "DrawCubeV", "DrawCubeWires", "DrawCubeWiresV",
        "DrawSphere", "DrawSphereEx", "DrawSphereWires",
        "DrawCylinder", "DrawCylinderEx", "DrawCylinderWires", "DrawCylinderWiresEx",
        "DrawCapsule", "DrawCapsuleWires", "DrawPlane", "DrawRay",
    };

    // Full raylib basic-3d-shapes range = the 20 above + the already-bound DrawGrid.
    private static string[] AllShapes()
    {
        var all = new List<string>(Names) { "DrawGrid" };
        return all.ToArray();
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_shape_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Names)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "DrawLine3D(", "DrawGrid(");
            Assert.That(range, Is.EquivalentTo(AllShapes()),
                "raylib's DrawLine3D..DrawGrid range must be exactly the 20 bound + the already-bound DrawGrid");
        });
    }

    [Test]
    public void Wrapper_shape_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // All 20 are Subs (void draws).
            foreach (var name in Names)
                Assert.That(wrapper.Contains($"Public Sub Framework_{name}("), Is.True, $"Framework_{name} must be a Sub (void)");

            // Representative signatures pin the marshaling: Color -> r,g,b,a Bytes; Vector3/Vector2/Ray by value;
            // int params -> Integer; float -> Single; the strip takes a Vector3() array.
            Assert.That(wrapper.Contains("Framework_DrawLine3D(startPos As Vector3, endPos As Vector3, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawLine3D: two Vector3 + Color-as-4-bytes");
            Assert.That(wrapper.Contains("Framework_DrawTriangleStrip3D(points As Vector3(), pointCount As Integer, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawTriangleStrip3D: Vector3() array + Integer count + 4 bytes");
            Assert.That(wrapper.Contains("Framework_DrawPlane(centerPos As Vector3, size As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawPlane: Vector3 + Vector2 + 4 bytes");
            Assert.That(wrapper.Contains("Framework_DrawRay(ray As Ray, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawRay: Ray by value + 4 bytes");
            Assert.That(wrapper.Contains("Framework_DrawSphereEx(centerPos As Vector3, radius As Single, rings As Integer, slices As Integer, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawSphereEx: Single radius + Integer rings/slices + 4 bytes");

            // Color is NEVER passed as the struct here (byte-expansion convention) -> no "As Color" param in the region.
            Assert.That(Regex.IsMatch(wrapper, @"Framework_Draw\w+\([^)]*As Color"), Is.False, "3D-shape draws expand Color to r,g,b,a Bytes, not an As Color param");
        });
    }

    [Test]
    public void Shape_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in Names)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    [Test]
    public void Every_shape_forwarder_reconstructs_color_in_rgba_order()
    {
        // The parity 3-way check only proves the forwarder EXISTS; a transposed Color reconstruction (e.g. Color{b,g,r,a})
        // would render a wrong tint yet pass both the parity presence check and the DoesNotThrow integration test (no pixels
        // read back). Pin the forwarder body's byte order here — cheap and headless.
        var root = RepoRoot();
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var lines = forwarder.Replace("\r\n", "\n").Split('\n');

        Assert.Multiple(() =>
        {
            foreach (var name in Names)
            {
                var line = Array.Find(lines, l => l.Contains($"void Framework_{name}("));
                Assert.That(line, Is.Not.Null, $"forwarder line for Framework_{name} not found");
                Assert.That(line, Does.Contain("Color{r, g, b, a}"),
                    $"Framework_{name} forwarder must reconstruct Color in r,g,b,a order (a transposed order tints wrong silently)");
            }
        });
    }

    private static List<string> ExtractRlapiRange(string raylibHeader, string firstContains, string lastContains)
    {
        var lines = raylibHeader.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains("RLAPI") && l.Contains(firstContains));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"raylib.h start anchor '{firstContains}' not found");
        var names = new List<string>();
        var rx = new Regex(@"RLAPI\s+.*?(\w+)\s*\(");
        for (int i = start; i < lines.Length; i++)
        {
            if (lines[i].Contains("RLAPI"))
            {
                var m = rx.Match(lines[i]);
                if (m.Success) names.Add(m.Groups[1].Value);
            }
            if (lines[i].Contains(lastContains)) break;
        }
        return names;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
