using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C5 (raw raylib screen-space / camera-math API). Unlike the audio batches,
/// these are pure CPU math — the *Ex/*2D/*Matrix* variants take an explicit viewport or camera only and never touch the
/// window or a GL context, so this fixture runs FULLY HEADLESS (no [Category("Integration")], no device) and asserts
/// exact numeric results. That makes it the strongest by-value marshaling check of the batch: Camera3D/Camera2D go in
/// by value and Ray/Vector2/Matrix come back by value, so a layout slip corrupts a known constant (e.g. the view
/// matrix's m14 == -eye.z, or the identity diagonal) and the assertion catches it directly.
///
/// The two non-Ex variants (GetWorldToScreen / GetScreenToWorldRay) read the current screen size internally, so with no
/// window GetWorldToScreen yields NaN (aspect 0/0) while GetScreenToWorldRay still copies camera.position into the ray
/// origin verbatim — both deterministic, both exercised here. Self-contained local [DllImport] + struct mirrors;
/// self-skips (whole fixture) when the engine DLL isn't staged with the C5 exports (stale DLL -> refresh IDE\ first).
///
/// ⚠ raylib's Matrix declares its 16 floats in a scrambled order (m0,m4,m8,m12 / m1,m5,m9,m13 / …); RMatrix mirrors
/// that exact order so the sequential marshaling lines up byte-for-byte.
/// </summary>
[TestFixture]
public class RaylibScreenSpaceMathTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const int CAMERA_PERSPECTIVE = 0;

    [StructLayout(LayoutKind.Sequential)] private struct RVector2 { public float x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct RVector3 { public float x, y, z; }

    // raylib Matrix field order is scrambled by design — keep this sequence exactly (see class remarks).
    [StructLayout(LayoutKind.Sequential)]
    private struct RMatrix
    {
        public float m0, m4, m8, m12;
        public float m1, m5, m9, m13;
        public float m2, m6, m10, m14;
        public float m3, m7, m11, m15;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RCamera3D { public RVector3 position, target, up; public float fovy; public int projection; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RCamera2D { public RVector2 offset, target; public float rotation, zoom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RRay { public RVector3 position, direction; }

    [DllImport(DLL, CallingConvention = CC)] private static extern RRay Framework_GetScreenToWorldRay(RVector2 position, RCamera3D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern RRay Framework_GetScreenToWorldRayEx(RVector2 position, RCamera3D camera, int width, int height);
    [DllImport(DLL, CallingConvention = CC)] private static extern RVector2 Framework_GetWorldToScreen(RVector3 position, RCamera3D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern RVector2 Framework_GetWorldToScreenEx(RVector3 position, RCamera3D camera, int width, int height);
    [DllImport(DLL, CallingConvention = CC)] private static extern RVector2 Framework_GetWorldToScreen2D(RVector2 position, RCamera2D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern RVector2 Framework_GetScreenToWorld2D(RVector2 position, RCamera2D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern RMatrix Framework_GetCameraMatrix(RCamera3D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern RMatrix Framework_GetCameraMatrix2D(RCamera2D camera);

    private static RCamera2D Cam2D(float ox, float oy, float zoom) =>
        new RCamera2D { offset = new RVector2 { x = ox, y = oy }, target = default, rotation = 0f, zoom = zoom };

    // Eye 10 units down +Z, looking at the origin, +Y up — a canonical view whose matrix is known exactly.
    private static RCamera3D EyeAt10() => new RCamera3D
    {
        position = new RVector3 { x = 0, y = 0, z = 10 },
        target = default,
        up = new RVector3 { x = 0, y = 1, z = 0 },
        fovy = 45f,
        projection = CAMERA_PERSPECTIVE,
    };

    [OneTimeSetUp]
    public void ProbeEngine()
    {
        // Cheapest pure-math export; its absence means the DLL predates C5 (or isn't staged) -> skip the whole fixture.
        try { Framework_GetCameraMatrix2D(Cam2D(0, 0, 1f)); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C5 exports; refresh IDE\\ first."); }
    }

    [Test]
    public void GetCameraMatrix2D_is_identity_for_a_default_camera()
    {
        // offset=0, target=0, rotation=0, zoom=1 composes to the identity transform.
        var m = Framework_GetCameraMatrix2D(Cam2D(0, 0, 1f));
        Assert.Multiple(() =>
        {
            Assert.That(m.m0, Is.EqualTo(1f).Within(1e-4), "m0 (x scale)");
            Assert.That(m.m5, Is.EqualTo(1f).Within(1e-4), "m5 (y scale)");
            Assert.That(m.m10, Is.EqualTo(1f).Within(1e-4), "m10");
            Assert.That(m.m15, Is.EqualTo(1f).Within(1e-4), "m15");
            Assert.That(m.m12, Is.EqualTo(0f).Within(1e-4), "m12 (x translate)");
            Assert.That(m.m13, Is.EqualTo(0f).Within(1e-4), "m13 (y translate)");
        });
    }

    [Test]
    public void GetWorldToScreen2D_applies_offset_and_zoom_and_round_trips()
    {
        var cam = Cam2D(400, 300, 2f); // screen = offset + (world - target)*zoom
        var origin = Framework_GetWorldToScreen2D(new RVector2 { x = 0, y = 0 }, cam);
        var p = Framework_GetWorldToScreen2D(new RVector2 { x = 10, y = 20 }, cam);
        var back = Framework_GetScreenToWorld2D(p, cam);
        Assert.Multiple(() =>
        {
            Assert.That(origin.x, Is.EqualTo(400f).Within(1e-3), "world origin maps to the camera offset (x)");
            Assert.That(origin.y, Is.EqualTo(300f).Within(1e-3), "world origin maps to the camera offset (y)");
            Assert.That(p.x, Is.EqualTo(420f).Within(1e-3), "400 + 10*2");
            Assert.That(p.y, Is.EqualTo(340f).Within(1e-3), "300 + 20*2");
            Assert.That(back.x, Is.EqualTo(10f).Within(1e-3), "screen->world is the inverse (x)");
            Assert.That(back.y, Is.EqualTo(20f).Within(1e-3), "screen->world is the inverse (y)");
        });
    }

    [Test]
    public void GetCameraMatrix_is_the_view_matrix_translating_by_negative_eye_distance()
    {
        // LookAt(eye=(0,0,10), target=0, up=+Y): identity rotation, translation z = -10. m14 == -eye.z is the key
        // marshaling proof — a scrambled Matrix layout would land -10 in the wrong slot.
        var m = Framework_GetCameraMatrix(EyeAt10());
        Assert.Multiple(() =>
        {
            Assert.That(m.m0, Is.EqualTo(1f).Within(1e-4), "m0");
            Assert.That(m.m5, Is.EqualTo(1f).Within(1e-4), "m5");
            Assert.That(m.m10, Is.EqualTo(1f).Within(1e-4), "m10");
            Assert.That(m.m15, Is.EqualTo(1f).Within(1e-4), "m15");
            Assert.That(m.m12, Is.EqualTo(0f).Within(1e-4), "m12 (x translate)");
            Assert.That(m.m13, Is.EqualTo(0f).Within(1e-4), "m13 (y translate)");
            Assert.That(m.m14, Is.EqualTo(-10f).Within(1e-4), "m14 (z translate = -eye.z)");
        });
    }

    [Test]
    public void GetWorldToScreenEx_maps_the_camera_target_to_the_viewport_center()
    {
        // The target is dead-center of the view frustum -> NDC (0,0) -> exact pixel center.
        var s = Framework_GetWorldToScreenEx(new RVector3 { x = 0, y = 0, z = 0 }, EyeAt10(), 800, 450);
        Assert.Multiple(() =>
        {
            Assert.That(s.x, Is.EqualTo(400f).Within(0.5), "target projects to width/2");
            Assert.That(s.y, Is.EqualTo(225f).Within(0.5), "target projects to height/2");
        });
    }

    [Test]
    public void GetScreenToWorldRayEx_originates_at_the_camera_and_points_down_minus_Z()
    {
        // Center pixel of an 800x450 viewport with a +Z eye -> a ray from the camera straight toward -Z.
        var ray = Framework_GetScreenToWorldRayEx(new RVector2 { x = 400, y = 225 }, EyeAt10(), 800, 450);
        Assert.Multiple(() =>
        {
            Assert.That(ray.position.x, Is.EqualTo(0f).Within(1e-3), "ray origin x = camera.position.x");
            Assert.That(ray.position.y, Is.EqualTo(0f).Within(1e-3), "ray origin y = camera.position.y");
            Assert.That(ray.position.z, Is.EqualTo(10f).Within(1e-3), "ray origin z = camera.position.z");
            Assert.That(ray.direction.z, Is.LessThan(-0.99f), "direction points toward -Z (normalized)");
            Assert.That(Math.Abs(ray.direction.x), Is.LessThan(1e-3), "no x drift at the center pixel");
            Assert.That(Math.Abs(ray.direction.y), Is.LessThan(1e-3), "no y drift at the center pixel");
        });
    }

    [Test]
    public void NonEx_variants_marshal_and_are_screen_size_dependent()
    {
        // No window here, so the screen size is 0. GetScreenToWorldRay still assigns ray.position = camera.position
        // directly (proves Camera3D-in / Ray-out marshaling), while its direction and GetWorldToScreen go NaN
        // (aspect 0/0) — the deterministic, documented headless behavior of the two screen-size-dependent variants.
        var ray = Framework_GetScreenToWorldRay(new RVector2 { x = 0, y = 0 }, EyeAt10());
        var screen = Framework_GetWorldToScreen(new RVector3 { x = 0, y = 0, z = 0 }, EyeAt10());
        Assert.Multiple(() =>
        {
            Assert.That(ray.position.z, Is.EqualTo(10f).Within(1e-3), "GetScreenToWorldRay copies camera.position into the ray origin");
            Assert.That(float.IsNaN(screen.x), Is.True, "GetWorldToScreen is screen-size-dependent -> NaN with no window (use the Ex variant headless)");
        });
    }
}
