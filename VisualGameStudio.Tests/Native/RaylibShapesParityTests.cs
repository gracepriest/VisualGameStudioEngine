using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Enforces the engine⇄wrapper sync invariant for the raylib shapes Batch 1: every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; of the
/// same name in RaylibWrapper.vb (design spec §4.4 step 3). Pure text scan — no engine load,
/// so it runs in the normal (and fast) suite.
/// </summary>
[TestFixture]
public class RaylibShapesParityTests
{
    private static readonly string[] Batch1 =
    {
        // Group A (21)
        "DrawPixelV","DrawLineV","DrawLineEx","DrawLineBezier","DrawCircleGradient","DrawCircleV",
        "DrawCircleLinesV","DrawRectangleV","DrawRectangleRec","DrawRectanglePro","DrawRectangleGradientV",
        "DrawRectangleGradientH","DrawRectangleGradientEx","DrawRectangleLinesEx","DrawRectangleRoundedLinesEx",
        "DrawPolyLinesEx","DrawSplineSegmentLinear","DrawSplineSegmentBasis","DrawSplineSegmentCatmullRom",
        "DrawSplineSegmentBezierQuadratic","DrawSplineSegmentBezierCubic",
        // Group B (8)
        "DrawLineStrip","DrawTriangleFan","DrawTriangleStrip","DrawSplineLinear","DrawSplineBasis",
        "DrawSplineCatmullRom","DrawSplineBezierQuadratic","DrawSplineBezierCubic",
        // Group C (5)
        "GetSplinePointLinear","GetSplinePointBasis","GetSplinePointCatmullRom","GetSplinePointBezierQuad",
        "GetSplinePointBezierCubic",
        // Group D (3)
        "SetShapesTexture","GetShapesTexture","GetShapesTextureRectangle",
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
    public void Every_batch1_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch1)
            {
                // Trailing '(' anchors on the call/decl site so e.g. DrawSplineLinear does not
                // match inside DrawSplineSegmentLinear (nor Basis/BezierQuad vs Quadratic).
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch1, Has.Length.EqualTo(37));
        });
    }
}
