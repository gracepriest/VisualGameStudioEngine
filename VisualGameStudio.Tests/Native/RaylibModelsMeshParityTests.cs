using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rmodels MESH sub-batch (11 GenMesh* generators + 7 mesh-management fns +
/// GetRayCollisionMesh, deferred here from the collision batch). All headless (text scan) — fast subset. Checks:
///   1. Every_mesh_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 19, PLUS a
///      raylib.h completeness cross-check: the GenMeshPoly..GenMeshCubicmap range is exactly the 11 generators, and the
///      UploadMesh..ExportMeshAsCode range is exactly the 7 bound management fns PLUS the two DEFERRED draws
///      (DrawMesh/DrawMeshInstanced), so nothing in the mesh surface was silently missed.
///   2. Deferred_mesh_draws_are_not_bound_yet — DrawMesh/DrawMeshInstanced take a Material by value and are deferred to
///      the materials batch; this asserts they are NOT bound yet (an honest deferral marker that flips when materials lands,
///      mirroring how the collision batch asserted GetRayCollisionMesh was deferred here).
///   3. Wrapper_mesh_bindings_declare_the_correct_marshaling — TYPE scan: GenMesh* are Functions returning Mesh;
///      UploadMesh/GenMeshTangents take ByRef Mesh (mutate-in-place); the rest take Mesh by value; ExportMesh(AsCode) return
///      <MarshalAs(I1)> Boolean with CharSet.Ansi String paths; GetMeshBoundingBox->BoundingBox, GetRayCollisionMesh->RayCollision.
///   4. Mesh_struct_mirrors_raylib_layout — the Utiliy.vb Mesh struct declares exactly raylib's 17 fields, in order, with the
///      widths that determine the ABI (Integer counts, IntPtr array pointers, UInteger vaoId).
///   5. Mesh_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibModelsMeshParityTests
{
    private static readonly string[] GenNames =
    {
        "GenMeshPoly", "GenMeshPlane", "GenMeshCube", "GenMeshSphere", "GenMeshHemiSphere",
        "GenMeshCylinder", "GenMeshCone", "GenMeshTorus", "GenMeshKnot", "GenMeshHeightmap", "GenMeshCubicmap",
    };

    // Management fns bound in THIS batch (DrawMesh/DrawMeshInstanced deferred to the materials batch).
    private static readonly string[] MgmtNames =
    {
        "UploadMesh", "UpdateMeshBuffer", "UnloadMesh", "GetMeshBoundingBox", "GenMeshTangents",
        "ExportMesh", "ExportMeshAsCode",
    };

    private static readonly string[] DeferredToMaterials = { "DrawMesh", "DrawMeshInstanced" };

    // All 19 bound this batch.
    private static string[] AllBound()
    {
        var all = new List<string>(GenNames);
        all.AddRange(MgmtNames);
        all.Add("GetRayCollisionMesh");
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
    public void Every_mesh_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in AllBound())
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));

            // The generator surface must be exactly the 11 GenMesh*.
            var genRange = ExtractRlapiRange(raylibHeader, "GenMeshPoly(", "GenMeshCubicmap(");
            Assert.That(genRange, Is.EquivalentTo(GenNames), "raylib's GenMeshPoly..GenMeshCubicmap range must be exactly the 11 generators");

            // The management surface must be exactly the 7 bound + the 2 deferred draws — nothing silently dropped.
            var mgmtRange = ExtractRlapiRange(raylibHeader, "UploadMesh(", "ExportMeshAsCode(");
            var expectedMgmt = new List<string>(MgmtNames);
            expectedMgmt.AddRange(DeferredToMaterials);
            Assert.That(mgmtRange, Is.EquivalentTo(expectedMgmt),
                "raylib's UploadMesh..ExportMeshAsCode range must be exactly the 7 bound mgmt fns + the 2 deferred draws");

            Assert.That(raylibHeader.Contains("GetRayCollisionMesh("), Is.True, "raylib.h must declare GetRayCollisionMesh");
        });
    }

    [Test]
    public void Deferred_mesh_draws_are_not_bound_yet()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        // DrawMesh/DrawMeshInstanced need a Material by value -> materials batch. This tripwire flips (and must be updated)
        // when that batch lands. "Framework_DrawMesh(" does not substring-match "Framework_DrawMeshInstanced(".
        Assert.Multiple(() =>
        {
            Assert.That(wrapper.Contains("Framework_DrawMesh("), Is.False, "Framework_DrawMesh is deferred to the materials batch");
            Assert.That(wrapper.Contains("Framework_DrawMeshInstanced("), Is.False, "Framework_DrawMeshInstanced is deferred to the materials batch");
        });
    }

    [Test]
    public void Wrapper_mesh_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // All 11 generators are Functions returning Mesh by value.
            foreach (var g in GenNames)
                Assert.That(Regex.IsMatch(wrapper, $@"Public Function Framework_{g}\([^)]*\) As Mesh"), Is.True,
                    $"Framework_{g} must be a Function returning Mesh by value");

            // Mutate-in-place: ByRef Mesh.
            Assert.That(wrapper.Contains("Public Sub Framework_UploadMesh(ByRef mesh As Mesh, <MarshalAs(UnmanagedType.I1)> dynamic As Boolean)"), Is.True, "UploadMesh: ByRef Mesh + bool->I1");
            Assert.That(wrapper.Contains("Public Sub Framework_GenMeshTangents(ByRef mesh As Mesh)"), Is.True, "GenMeshTangents: ByRef Mesh");

            // By-value Mesh (NOT ByRef) for the read-only consumers.
            Assert.That(wrapper.Contains("Public Sub Framework_UnloadMesh(mesh As Mesh)"), Is.True, "UnloadMesh: Mesh by value");
            Assert.That(wrapper.Contains("Public Sub Framework_UpdateMeshBuffer(mesh As Mesh, index As Integer, data As IntPtr, dataSize As Integer, offset As Integer)"), Is.True, "UpdateMeshBuffer: Mesh by value + const void* -> IntPtr");
            Assert.That(wrapper.Contains("Public Function Framework_GetMeshBoundingBox(mesh As Mesh) As BoundingBox"), Is.True, "GetMeshBoundingBox: Mesh by value -> BoundingBox");
            Assert.That(wrapper.Contains("Public Function Framework_GetRayCollisionMesh(ray As Ray, mesh As Mesh, transform As Matrix) As RayCollision"), Is.True, "GetRayCollisionMesh: Ray+Mesh+Matrix by value -> RayCollision");

            // bool returns -> <MarshalAs(I1)> Boolean; file paths -> Ansi String.
            Assert.That(wrapper.Contains("Public Function Framework_ExportMesh(mesh As Mesh, fileName As String) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "ExportMesh: Mesh + String -> I1 Boolean");
            Assert.That(wrapper.Contains("Public Function Framework_ExportMeshAsCode(mesh As Mesh, fileName As String) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "ExportMeshAsCode: Mesh + String -> I1 Boolean");
            // Both export imports carry CharSet.Ansi. The trailing '\(' keeps "Framework_ExportMesh" from also matching
            // "Framework_ExportMeshAsCode".
            Assert.That(Regex.Matches(wrapper, @"CharSet:=CharSet\.Ansi\)>\s*\r?\n\s*Public Function Framework_ExportMesh\(").Count, Is.EqualTo(1), "ExportMesh import must set CharSet.Ansi");
            Assert.That(Regex.Matches(wrapper, @"CharSet:=CharSet\.Ansi\)>\s*\r?\n\s*Public Function Framework_ExportMeshAsCode\(").Count, Is.EqualTo(1), "ExportMeshAsCode import must set CharSet.Ansi");
        });
    }

    [Test]
    public void Mesh_struct_mirrors_raylib_layout()
    {
        var root = RepoRoot();
        var util = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb")).Replace("\r\n", "\n");

        // Extract the "Public Structure Mesh ... End Structure" block and read its (field, type) pairs in declared order.
        var m = Regex.Match(util, @"Public Structure Mesh\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(m.Success, Is.True, "Utiliy.vb must declare a Mesh structure");
        var fields = new List<string>();
        foreach (Match f in Regex.Matches(m.Groups[1].Value, @"Public (\w+) As (\w+)"))
            fields.Add($"{f.Groups[1].Value}:{f.Groups[2].Value}");

        // Every field's name AND width is pinned: a pointer field silently regressing to Integer (4 bytes vs IntPtr 8) would
        // keep the correct name/order yet desync the ABI from that offset on, so the type token is checked for all 17 fields.
        var expected = new[]
        {
            "vertexCount:Integer", "triangleCount:Integer",
            "vertices:IntPtr", "texcoords:IntPtr", "texcoords2:IntPtr", "normals:IntPtr", "tangents:IntPtr",
            "colors:IntPtr", "indices:IntPtr", "animVertices:IntPtr", "animNormals:IntPtr", "boneIds:IntPtr",
            "boneWeights:IntPtr", "boneMatrices:IntPtr",
            "boneCount:Integer", "vaoId:UInteger", "vboId:IntPtr",
        };
        Assert.That(fields, Is.EqualTo(expected), "Mesh fields must mirror raylib's 17 fields, in order, with correct widths");
    }

    [Test]
    public void Mesh_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in AllBound())
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
                var mm = rx.Match(lines[i]);
                if (mm.Success) names.Add(mm.Groups[1].Value);
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
