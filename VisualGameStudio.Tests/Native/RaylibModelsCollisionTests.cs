using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// HEADLESS correctness for the rmodels COLLISION sub-batch. Every function is pure raymath in raylib (no GL, no device,
/// no static state), so the whole batch is deterministic in the FAST SUBSET and gives real geometry oracles:
///   * the three Check* predicates return true/false on hand-constructed overlapping/disjoint shapes (proves the I1 bool
///     return + BoundingBox by value);
///   * GetRayCollisionSphere is a FULL-STRUCT oracle: a ray from (0,0,-5) toward +z at a unit sphere at the origin hits at
///     distance 4, point (0,0,-1), normal (0,0,-1) -- this validates the ENTIRE new RayCollision by-value return
///     (I1 hit + Single distance + two Vector3 unmarshaled from the correct offsets), the batch's main ABI risk;
///   * GetRayCollisionBox/Triangle/Quad give distance+point oracles (4 / 5 / 5), and clean geometric misses set hit=false.
/// BoundingBox/RayCollision/Ray/Vector3 are passed/returned BY VALUE. Self-Ignores when the DLL isn't staged or predates
/// the exports.
/// </summary>
[TestFixture]
public class RaylibModelsCollisionTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const float Tol = 1e-3f;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct Ray { public Vector3 position, direction; public Ray(Vector3 p, Vector3 d) { position = p; direction = d; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundingBox { public Vector3 min, max; public BoundingBox(Vector3 mn, Vector3 mx) { min = mn; max = mx; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct RayCollision
    {
        [MarshalAs(UnmanagedType.I1)] public bool hit;
        public float distance;
        public Vector3 point, normal;
    }

    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_CheckCollisionSpheres(Vector3 c1, float r1, Vector3 c2, float r2);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_CheckCollisionBoxes(BoundingBox b1, BoundingBox b2);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_CheckCollisionBoxSphere(BoundingBox box, Vector3 center, float radius);
    [DllImport(DLL, CallingConvention = CC)] private static extern RayCollision Framework_GetRayCollisionSphere(Ray ray, Vector3 center, float radius);
    [DllImport(DLL, CallingConvention = CC)] private static extern RayCollision Framework_GetRayCollisionBox(Ray ray, BoundingBox box);
    [DllImport(DLL, CallingConvention = CC)] private static extern RayCollision Framework_GetRayCollisionTriangle(Ray ray, Vector3 p1, Vector3 p2, Vector3 p3);
    [DllImport(DLL, CallingConvention = CC)] private static extern RayCollision Framework_GetRayCollisionQuad(Ray ray, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4);

    private static bool EnsureAvailable()
    {
        try { Framework_CheckCollisionSpheres(new Vector3(0, 0, 0), 1f, new Vector3(0, 0, 0), 1f); return true; }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return false; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the collision exports; refresh IDE\\ first."); return false; }
    }

    [Test]
    public void CheckCollisionSpheres_true_when_overlapping_false_when_apart()
    {
        if (!EnsureAvailable()) return;
        Assert.Multiple(() =>
        {
            // centers 3 apart, radii sum 4 > 3 -> overlap
            Assert.That(Framework_CheckCollisionSpheres(new Vector3(0, 0, 0), 2f, new Vector3(0, 0, 3), 2f), Is.True);
            // centers 10 apart, radii sum 2 -> disjoint
            Assert.That(Framework_CheckCollisionSpheres(new Vector3(0, 0, 0), 1f, new Vector3(0, 0, 10), 1f), Is.False);
        });
    }

    [Test]
    public void CheckCollisionBoxes_true_when_overlapping_false_when_apart()
    {
        if (!EnsureAvailable()) return;
        var a = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        Assert.Multiple(() =>
        {
            Assert.That(Framework_CheckCollisionBoxes(a, new BoundingBox(new Vector3(0, 0, 0), new Vector3(2, 2, 2))), Is.True);
            Assert.That(Framework_CheckCollisionBoxes(a, new BoundingBox(new Vector3(5, 5, 5), new Vector3(6, 6, 6))), Is.False);
        });
    }

    [Test]
    public void CheckCollisionBoxSphere_true_when_touching_false_when_apart()
    {
        if (!EnsureAvailable()) return;
        var box = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        Assert.Multiple(() =>
        {
            // sphere center 1 unit outside the +x face, radius 1.5 -> reaches into the box
            Assert.That(Framework_CheckCollisionBoxSphere(box, new Vector3(2, 0, 0), 1.5f), Is.True);
            // sphere far away
            Assert.That(Framework_CheckCollisionBoxSphere(box, new Vector3(5, 0, 0), 1f), Is.False);
        });
    }

    [Test]
    public void GetRayCollisionSphere_full_struct_oracle_and_geometric_miss()
    {
        if (!EnsureAvailable()) return;

        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1)); // unit direction toward +z
        var hitRc = Framework_GetRayCollisionSphere(ray, new Vector3(0, 0, 0), 1f);

        // Clean geometric miss: parallel ray offset in x by 5, never within r=1 of the origin.
        var missRay = new Ray(new Vector3(5, 0, -5), new Vector3(0, 0, 1));
        var missRc = Framework_GetRayCollisionSphere(missRay, new Vector3(0, 0, 0), 1f);

        Assert.Multiple(() =>
        {
            Assert.That(hitRc.hit, Is.True, "ray through the unit sphere hits");
            Assert.That(hitRc.distance, Is.EqualTo(4f).Within(Tol), "near-surface hit at z=-1 is 4 units from z=-5");
            Assert.That(hitRc.point.x, Is.EqualTo(0f).Within(Tol));
            Assert.That(hitRc.point.y, Is.EqualTo(0f).Within(Tol));
            Assert.That(hitRc.point.z, Is.EqualTo(-1f).Within(Tol), "hit point is the near pole (0,0,-1)");
            Assert.That(hitRc.normal.z, Is.EqualTo(-1f).Within(Tol), "surface normal points back along -z");
            Assert.That(missRc.hit, Is.False, "a parallel ray offset by 5 misses the unit sphere");
        });
    }

    [Test]
    public void GetRayCollisionBox_oracle_and_geometric_miss()
    {
        if (!EnsureAvailable()) return;
        var box = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var rc = Framework_GetRayCollisionBox(new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1)), box);
        // Parallel ray offset to x=5 never enters the box's x-slab [-1,1].
        var missRc = Framework_GetRayCollisionBox(new Ray(new Vector3(5, 0, -5), new Vector3(0, 0, 1)), box);
        Assert.Multiple(() =>
        {
            Assert.That(rc.hit, Is.True, "ray enters the box");
            Assert.That(rc.distance, Is.EqualTo(4f).Within(Tol), "enters the near face at z=-1, 4 units from z=-5");
            Assert.That(rc.point.z, Is.EqualTo(-1f).Within(Tol), "entry point on the z=-1 face");
            Assert.That(missRc.hit, Is.False, "a ray offset to x=5 misses the box entirely");
        });
    }

    [Test]
    public void GetRayCollisionTriangle_oracle_and_geometric_miss()
    {
        if (!EnsureAvailable()) return;
        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1));
        var p1 = new Vector3(-1, -1, 0); var p2 = new Vector3(1, -1, 0); var p3 = new Vector3(0, 1, 0);
        var hitRc = Framework_GetRayCollisionTriangle(ray, p1, p2, p3);

        // Ray hitting the z=0 plane far outside the triangle (barycentric out of range).
        var missRay = new Ray(new Vector3(5, 5, -5), new Vector3(0, 0, 1));
        var missRc = Framework_GetRayCollisionTriangle(missRay, p1, p2, p3);

        Assert.Multiple(() =>
        {
            Assert.That(hitRc.hit, Is.True, "ray passes through the triangle interior");
            Assert.That(hitRc.distance, Is.EqualTo(5f).Within(Tol), "hits the z=0 plane 5 units from z=-5");
            Assert.That(hitRc.point.z, Is.EqualTo(0f).Within(Tol));
            Assert.That(missRc.hit, Is.False, "a ray hitting the plane outside the triangle misses");
        });
    }

    [Test]
    public void GetRayCollisionQuad_oracle_and_geometric_miss()
    {
        if (!EnsureAvailable()) return;
        var q1 = new Vector3(-1, -1, 0); var q2 = new Vector3(1, -1, 0); var q3 = new Vector3(1, 1, 0); var q4 = new Vector3(-1, 1, 0);
        var rc = Framework_GetRayCollisionQuad(new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1)), q1, q2, q3, q4);
        // Ray hits the z=0 plane far outside the quad.
        var missRc = Framework_GetRayCollisionQuad(new Ray(new Vector3(5, 5, -5), new Vector3(0, 0, 1)), q1, q2, q3, q4);
        Assert.Multiple(() =>
        {
            Assert.That(rc.hit, Is.True, "ray passes through the quad");
            Assert.That(rc.distance, Is.EqualTo(5f).Within(Tol), "hits the z=0 plane 5 units from z=-5");
            Assert.That(rc.point.z, Is.EqualTo(0f).Within(Tol));
            Assert.That(missRc.hit, Is.False, "a ray hitting the plane outside the quad misses");
        });
    }
}
