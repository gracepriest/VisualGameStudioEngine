using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rmodels MODEL sub-batch (5 model-management fns + 10 model/billboard draws). All
/// headless (text scan) — fast subset. Checks:
///   1. Every_model_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 15, PLUS a
///      raylib.h completeness cross-check (LoadModel..GetModelBoundingBox == the 5 mgmt fns; DrawModel..DrawBillboardPro ==
///      the 10 draws), so nothing in the model surface was silently missed.
///   2. Wrapper_model_bindings_declare_the_correct_marshaling — TYPE scan: LoadModel/LoadModelFromMesh return Model by value;
///      IsModelValid -> <MarshalAs(I1)> Boolean; UnloadModel takes Model by value; GetModelBoundingBox -> BoundingBox; the 10
///      draws are Subs with Color expanded to r,g,b,a Bytes (no "As Color" param); Model/Camera3D/Texture2D/Rectangle by value;
///      LoadModel path is a CharSet.Ansi String.
///   3. Model_struct_mirrors_raylib_layout — the Utiliy.vb Model struct declares raylib's 9 fields, in order, with the widths
///      that fix the ABI (Matrix transform; Integer counts; IntPtr pointers).
///   4. Model_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibModelsModelParityTests
{
    private static readonly string[] MgmtNames =
    {
        "LoadModel", "LoadModelFromMesh", "IsModelValid", "UnloadModel", "GetModelBoundingBox",
    };

    private static readonly string[] DrawNames =
    {
        "DrawModel", "DrawModelEx", "DrawModelWires", "DrawModelWiresEx", "DrawModelPoints", "DrawModelPointsEx",
        "DrawBoundingBox", "DrawBillboard", "DrawBillboardRec", "DrawBillboardPro",
    };

    private static string[] AllBound()
    {
        var all = new List<string>(MgmtNames);
        all.AddRange(DrawNames);
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
    public void Every_model_export_is_bound_3_ways()
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
            var mgmtRange = ExtractRlapiRange(raylibHeader, "LoadModel(", "GetModelBoundingBox(");
            Assert.That(mgmtRange, Is.EquivalentTo(MgmtNames), "raylib's LoadModel..GetModelBoundingBox range must be exactly the 5 mgmt fns");
            var drawRange = ExtractRlapiRange(raylibHeader, "DrawModel(", "DrawBillboardPro(");
            Assert.That(drawRange, Is.EquivalentTo(DrawNames), "raylib's DrawModel..DrawBillboardPro range must be exactly the 10 draws");
        });
    }

    [Test]
    public void Wrapper_model_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // Model returns by value; IsModelValid I1; UnloadModel Sub; GetModelBoundingBox -> BoundingBox.
            Assert.That(wrapper.Contains("Public Function Framework_LoadModel(fileName As String) As Model"), Is.True, "LoadModel: String -> Model by value");
            Assert.That(wrapper.Contains("Public Function Framework_LoadModelFromMesh(mesh As Mesh) As Model"), Is.True, "LoadModelFromMesh: Mesh -> Model by value");
            Assert.That(wrapper.Contains("Public Function Framework_IsModelValid(model As Model) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsModelValid: Model -> I1 Boolean");
            Assert.That(wrapper.Contains("Public Sub Framework_UnloadModel(model As Model)"), Is.True, "UnloadModel: Model by value");
            Assert.That(wrapper.Contains("Public Function Framework_GetModelBoundingBox(model As Model) As BoundingBox"), Is.True, "GetModelBoundingBox: Model -> BoundingBox");

            // LoadModel import carries CharSet.Ansi.
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi\)>\s*\r?\n\s*Public Function Framework_LoadModel\("), Is.True, "LoadModel import must set CharSet.Ansi");

            // All 10 draws are Subs; representative signatures pin the Color-as-bytes + by-value struct marshaling.
            foreach (var d in DrawNames)
                Assert.That(wrapper.Contains($"Public Sub Framework_{d}("), Is.True, $"Framework_{d} must be a Sub (void draw)");
            Assert.That(wrapper.Contains("Framework_DrawModel(model As Model, position As Vector3, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawModel: Model + Vector3 + Single + 4 bytes");
            Assert.That(wrapper.Contains("Framework_DrawBoundingBox(box As BoundingBox, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawBoundingBox: BoundingBox + 4 bytes");
            Assert.That(wrapper.Contains("Framework_DrawBillboard(camera As Camera3D, texture As Texture2D, position As Vector3, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawBillboard: Camera3D + Texture2D + Vector3 + Single + 4 bytes");
            Assert.That(wrapper.Contains("Framework_DrawBillboardPro(camera As Camera3D, texture As Texture2D, source As Rectangle, position As Vector3, up As Vector3, size As Vector2, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)"), Is.True, "DrawBillboardPro: full billboard signature");

            // Color is byte-expanded, never passed as the struct, across the whole model-draw region.
            Assert.That(Regex.IsMatch(wrapper, @"Framework_Draw(Model|Billboard|BoundingBox)\w*\([^)]*As Color"), Is.False, "model draws expand Color to r,g,b,a Bytes, not an As Color param");
        });
    }

    [Test]
    public void Model_struct_mirrors_raylib_layout()
    {
        var root = RepoRoot();
        var util = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb")).Replace("\r\n", "\n");

        var m = Regex.Match(util, @"Public Structure Model\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(m.Success, Is.True, "Utiliy.vb must declare a Model structure");
        var fields = new List<string>();
        foreach (Match f in Regex.Matches(m.Groups[1].Value, @"Public (\w+) As (\w+)"))
            fields.Add($"{f.Groups[1].Value}:{f.Groups[2].Value}");

        // Name AND width pinned for every field: a wrong-width regression (e.g. meshes As Integer) or a wrong transform type
        // would shift meshCount/meshes offsets and desync the by-value Model ABI.
        var expected = new[]
        {
            "transform:Matrix", "meshCount:Integer", "materialCount:Integer",
            "meshes:IntPtr", "materials:IntPtr", "meshMaterial:IntPtr",
            "boneCount:Integer", "bones:IntPtr", "bindPose:IntPtr",
        };
        Assert.That(fields, Is.EqualTo(expected), "Model fields must mirror raylib's 9 fields, in order, with correct widths");
    }

    [Test]
    public void Model_exports_are_each_declared_exactly_once()
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
