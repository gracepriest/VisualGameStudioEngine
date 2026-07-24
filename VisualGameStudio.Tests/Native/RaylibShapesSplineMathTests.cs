using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Group C spline-point evaluators are pure math (no GL), so they are genuinely
/// unit-testable through P/Invoke. Declares its OWN [DllImport] so the assertions test the
/// engine export + return-by-value ABI directly. Integration-tagged + self-skipping: needs
/// the freshly built VisualGameStudioEngine.dll staged next to the test binary (the test
/// csproj already copies IDE\VisualGameStudioEngine.dll; the IDE refresh ships the new exports).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibShapesSplineMathTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct V2 { public float x, y; public V2(float x, float y) { this.x = x; this.y = y; } }

    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointLinear(V2 a, V2 b, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointCatmullRom(V2 p1, V2 p2, V2 p3, V2 p4, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointBasis(V2 p1, V2 p2, V2 p3, V2 p4, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointBezierQuad(V2 p1, V2 c2, V2 p3, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointBezierCubic(V2 p1, V2 c2, V2 c3, V2 p4, float t);

    private static V2 Call(Func<V2> f)
    {
        try { return f(); }
        catch (DllNotFoundException) { Assert.Ignore("VisualGameStudioEngine.dll not staged; rebuild engine + refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore("engine DLL predates Batch 1 exports; refresh IDE\\ first."); throw; }
    }

    private static void AssertClose(V2 got, float x, float y)
    {
        Assert.That(got.x, Is.EqualTo(x).Within(1e-3), "x");
        Assert.That(got.y, Is.EqualTo(y).Within(1e-3), "y");
    }

    [Test]
    public void Linear_midpoint()
        => AssertClose(Call(() => Framework_GetSplinePointLinear(new V2(0, 0), new V2(10, 0), 0.5f)), 5, 0);

    [Test]
    public void CatmullRom_interpolates_p2_at_t0_and_p3_at_t1()
    {
        var p1 = new V2(0, 0); var p2 = new V2(1, 2); var p3 = new V2(3, 4); var p4 = new V2(5, 6);
        AssertClose(Call(() => Framework_GetSplinePointCatmullRom(p1, p2, p3, p4, 0f)), p2.x, p2.y);
        AssertClose(Call(() => Framework_GetSplinePointCatmullRom(p1, p2, p3, p4, 1f)), p3.x, p3.y);
    }

    [Test]
    public void Basis_partition_of_unity_returns_the_point_when_all_equal()
        => AssertClose(Call(() => Framework_GetSplinePointBasis(new V2(2, 3), new V2(2, 3), new V2(2, 3), new V2(2, 3), 0.5f)), 2, 3);

    [Test]
    public void BezierQuad_hits_endpoints()
    {
        var p1 = new V2(0, 0); var c2 = new V2(5, 9); var p3 = new V2(10, 0);
        AssertClose(Call(() => Framework_GetSplinePointBezierQuad(p1, c2, p3, 0f)), p1.x, p1.y);
        AssertClose(Call(() => Framework_GetSplinePointBezierQuad(p1, c2, p3, 1f)), p3.x, p3.y);
    }

    [Test]
    public void BezierCubic_hits_endpoints()
    {
        var p1 = new V2(0, 0); var c2 = new V2(2, 8); var c3 = new V2(8, 8); var p4 = new V2(10, 0);
        AssertClose(Call(() => Framework_GetSplinePointBezierCubic(p1, c2, c3, p4, 0f)), p1.x, p1.y);
        AssertClose(Call(() => Framework_GetSplinePointBezierCubic(p1, c2, c3, p4, 1f)), p4.x, p4.y);
    }
}
