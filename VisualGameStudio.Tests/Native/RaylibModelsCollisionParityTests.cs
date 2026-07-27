using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rmodels COLLISION sub-batch (the first slice of the big models module). 7 raw
/// geometric-collision functions — all pure raymath, headless. Checks:
///   1. Every_collision_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 7, PLUS a
///      raylib.h completeness cross-check: the CheckCollisionSpheres..GetRayCollisionQuad range is exactly these 7 + the
///      deliberately DEFERRED GetRayCollisionMesh (which needs the Mesh struct -> goes with the mesh sub-batch). This proves
///      nothing in the collision surface was silently missed.
///   2. Wrapper_collision_bindings_declare_the_correct_marshaling — TYPE scan: the Check* predicates return I1 Boolean; the
///      GetRayCollision* helpers return RayCollision by value; BoundingBox/Ray/Vector3 params; plus the new-struct layouts.
///   3. Collision_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibModelsCollisionParityTests
{
    // The 7 bound here. GetRayCollisionMesh is deferred (needs Mesh).
    private static readonly string[] Names =
    {
        "CheckCollisionSpheres", "CheckCollisionBoxes", "CheckCollisionBoxSphere",
        "GetRayCollisionSphere", "GetRayCollisionBox", "GetRayCollisionTriangle", "GetRayCollisionQuad",
    };

    // The full raylib collision range = the 7 above + GetRayCollisionMesh (now bound in the mesh sub-batch, verified there).
    private static readonly string[] AllRaylibCollision =
    {
        "CheckCollisionSpheres", "CheckCollisionBoxes", "CheckCollisionBoxSphere",
        "GetRayCollisionSphere", "GetRayCollisionBox", "GetRayCollisionMesh",
        "GetRayCollisionTriangle", "GetRayCollisionQuad",
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
    public void Every_collision_export_is_bound_3_ways()
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

            // GetRayCollisionMesh landed in the mesh sub-batch (it needs the Mesh struct); its 3-way binding is verified by
            // RaylibModelsMeshParityTests. Here we only confirm raylib's full collision range is accounted for.
            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "CheckCollisionSpheres(", "GetRayCollisionQuad(");
            Assert.That(range, Is.EquivalentTo(AllRaylibCollision),
                "raylib's CheckCollisionSpheres..GetRayCollisionQuad range must be exactly the 7 bound + the deferred GetRayCollisionMesh");
        });
    }

    [Test]
    public void Wrapper_collision_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));
        var utiliy = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb"));

        Assert.Multiple(() =>
        {
            Assert.That(wrapper.Contains("Framework_CheckCollisionSpheres(center1 As Vector3, radius1 As Single, center2 As Vector3, radius2 As Single) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "CheckCollisionSpheres: Vector3/Single + I1 Boolean");
            Assert.That(wrapper.Contains("Framework_CheckCollisionBoxes(box1 As BoundingBox, box2 As BoundingBox) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "CheckCollisionBoxes: two BoundingBox + I1 Boolean");
            Assert.That(wrapper.Contains("Framework_CheckCollisionBoxSphere(box As BoundingBox, center As Vector3, radius As Single) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "CheckCollisionBoxSphere: BoundingBox/Vector3/Single + I1 Boolean");
            Assert.That(wrapper.Contains("Framework_GetRayCollisionSphere(ray As Ray, center As Vector3, radius As Single) As RayCollision"), Is.True, "GetRayCollisionSphere returns RayCollision by value");
            Assert.That(wrapper.Contains("Framework_GetRayCollisionBox(ray As Ray, box As BoundingBox) As RayCollision"), Is.True, "GetRayCollisionBox returns RayCollision by value");
            Assert.That(wrapper.Contains("Framework_GetRayCollisionTriangle(ray As Ray, p1 As Vector3, p2 As Vector3, p3 As Vector3) As RayCollision"), Is.True, "GetRayCollisionTriangle returns RayCollision by value");
            Assert.That(wrapper.Contains("Framework_GetRayCollisionQuad(ray As Ray, p1 As Vector3, p2 As Vector3, p3 As Vector3, p4 As Vector3) As RayCollision"), Is.True, "GetRayCollisionQuad returns RayCollision by value");

            // New struct layouts (Utiliy.vb): BoundingBox = 2 Vector3; RayCollision leads with an I1 Boolean then Single distance.
            Assert.That(utiliy.Contains("Public Structure BoundingBox"), Is.True, "BoundingBox struct defined");
            Assert.That(utiliy.Contains("Public Structure RayCollision"), Is.True, "RayCollision struct defined");
            Assert.That(Regex.IsMatch(utiliy, @"Public\s+Structure\s+RayCollision\s+<MarshalAs\(UnmanagedType\.I1\)>\s+Public\s+hit\s+As\s+Boolean", RegexOptions.Singleline), Is.True,
                "RayCollision.hit must be the leading field marshaled as I1 Boolean");
        });
    }

    [Test]
    public void Collision_exports_are_each_declared_exactly_once()
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
