using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rmodels MATERIALS sub-batch (6 material-management fns + DrawMesh/DrawMeshInstanced,
/// relocated from the mesh sub-batch because they take a Material by value). All headless (text scan) — fast subset. Checks:
///   1. Every_materials_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 8, PLUS a
///      raylib.h completeness cross-check (LoadMaterials..SetModelMeshMaterial == the 6 material fns; the two mesh draws are
///      exactly DrawMesh/DrawMeshInstanced), so nothing in the material surface was silently missed.
///   2. Wrapper_materials_bindings_declare_the_correct_marshaling — TYPE scan: LoadMaterials returns IntPtr (raylib-owned
///      Material* array) + ByRef materialCount + CharSet.Ansi; LoadMaterialDefault returns Material by value; IsMaterialValid
///      -> <MarshalAs(I1)> Boolean; UnloadMaterial/DrawMesh/DrawMeshInstanced are Subs; SetMaterialTexture/SetModelMeshMaterial
///      take a ByRef Material/Model the callee mutates; DrawMeshInstanced takes a Matrix() array; Material/Mesh/Texture2D/Matrix
///      by value.
///   3. Material_and_MaterialMap_structs_mirror_raylib_layout — field/width/order pin for both new structs, incl. the
///      ByValArray SizeConst:=4 on Material.params (a wrong width/order desyncs the by-value Material/MaterialMap ABI).
///   4. Materials_exports_are_each_declared_exactly_once — duplicate-export guard (also proves DrawMesh/DrawMeshInstanced were
///      MOVED out of the mesh batch, not duplicated).
/// </summary>
[TestFixture]
public class RaylibModelsMaterialsParityTests
{
    private static readonly string[] MgmtNames =
    {
        "LoadMaterials", "LoadMaterialDefault", "IsMaterialValid", "UnloadMaterial", "SetMaterialTexture", "SetModelMeshMaterial",
    };

    private static readonly string[] MeshDrawNames =
    {
        "DrawMesh", "DrawMeshInstanced",
    };

    private static string[] AllBound()
    {
        var all = new List<string>(MgmtNames);
        all.AddRange(MeshDrawNames);
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
    public void Every_materials_export_is_bound_3_ways()
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
            var mgmtRange = ExtractRlapiRange(raylibHeader, "LoadMaterials(", "SetModelMeshMaterial(");
            Assert.That(mgmtRange, Is.EquivalentTo(MgmtNames), "raylib's LoadMaterials..SetModelMeshMaterial range must be exactly the 6 material fns");
            var drawRange = ExtractRlapiRange(raylibHeader, "DrawMesh(", "DrawMeshInstanced(");
            Assert.That(drawRange, Is.EquivalentTo(MeshDrawNames), "raylib's DrawMesh..DrawMeshInstanced range must be exactly the 2 material-drawn-mesh fns");
        });
    }

    [Test]
    public void Wrapper_materials_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // LoadMaterials: raylib-owned Material* array -> IntPtr, count via ByRef, path is a CharSet.Ansi String.
            Assert.That(wrapper.Contains("Public Function Framework_LoadMaterials(fileName As String, ByRef materialCount As Integer) As IntPtr"), Is.True, "LoadMaterials: String + ByRef Integer -> IntPtr");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi\)>\s*\r?\n\s*Public Function Framework_LoadMaterials\("), Is.True, "LoadMaterials import must set CharSet.Ansi");

            // LoadMaterialDefault returns by value; IsMaterialValid I1; the rest are Subs.
            Assert.That(wrapper.Contains("Public Function Framework_LoadMaterialDefault() As Material"), Is.True, "LoadMaterialDefault: () -> Material by value");
            Assert.That(wrapper.Contains("Public Function Framework_IsMaterialValid(material As Material) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsMaterialValid: Material -> I1 Boolean");
            Assert.That(wrapper.Contains("Public Sub Framework_UnloadMaterial(material As Material)"), Is.True, "UnloadMaterial: Material by value");

            // Mutators take a ByRef pointer the callee writes through.
            Assert.That(wrapper.Contains("Public Sub Framework_SetMaterialTexture(ByRef material As Material, mapType As Integer, texture As Texture2D)"), Is.True, "SetMaterialTexture: ByRef Material + Integer + Texture2D");
            Assert.That(wrapper.Contains("Public Sub Framework_SetModelMeshMaterial(ByRef model As Model, meshId As Integer, materialId As Integer)"), Is.True, "SetModelMeshMaterial: ByRef Model + 2 Integers");

            // Relocated mesh draws: Material by value; DrawMeshInstanced takes a Matrix() LPArray.
            Assert.That(wrapper.Contains("Public Sub Framework_DrawMesh(mesh As Mesh, material As Material, transform As Matrix)"), Is.True, "DrawMesh: Mesh + Material + Matrix, all by value");
            Assert.That(wrapper.Contains("Public Sub Framework_DrawMeshInstanced(mesh As Mesh, material As Material, transforms As Matrix(), instances As Integer)"), Is.True, "DrawMeshInstanced: Mesh + Material + Matrix() array + Integer");
        });
    }

    [Test]
    public void Material_and_MaterialMap_structs_mirror_raylib_layout()
    {
        var root = RepoRoot();
        var util = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb")).Replace("\r\n", "\n");

        // MaterialMap: Texture2D texture; Color color; float value. Every field name AND width pinned.
        var mm = Regex.Match(util, @"Public Structure MaterialMap\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(mm.Success, Is.True, "Utiliy.vb must declare a MaterialMap structure");
        var mmFields = ScanFields(mm.Groups[1].Value);
        Assert.That(mmFields, Is.EqualTo(new[] { "texture:Texture2D", "color:Color", "value:Single" }),
            "MaterialMap fields must mirror raylib's 3 fields, in order, with correct widths");

        // Material: Shader shader; MaterialMap* maps (-> IntPtr); float params[4] (-> ByValArray SizeConst:=4).
        var mt = Regex.Match(util, @"Public Structure Material\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(mt.Success, Is.True, "Utiliy.vb must declare a Material structure");
        var mtBody = mt.Groups[1].Value;
        var mtFields = ScanFields(mtBody);
        Assert.That(mtFields, Is.EqualTo(new[] { "shader:Shader", "maps:IntPtr", "params:Single" }),
            "Material fields must mirror raylib's 3 fields, in order, with correct widths (params is the ByValArray Single())");
        Assert.That(Regex.IsMatch(mtBody, @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=4\)>\s*\n\s*Public params As Single\(\)"), Is.True,
            "Material.params must be an inline fixed 4-float array (ByValArray SizeConst:=4), not a pointer");
    }

    [Test]
    public void Materials_exports_are_each_declared_exactly_once()
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

    private static List<string> ScanFields(string structBody)
    {
        var fields = new List<string>();
        foreach (Match f in Regex.Matches(structBody, @"Public (\w+) As (\w+)"))
            fields.Add($"{f.Groups[1].Value}:{f.Groups[2].Value}");
        return fields;
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
