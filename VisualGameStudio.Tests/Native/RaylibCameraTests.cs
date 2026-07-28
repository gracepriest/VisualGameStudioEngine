using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// HEADLESS correctness for the rcamera batch. raylib's Camera is a typedef of Camera3D, passed as Camera* and MUTATED IN
/// PLACE — so the wrapper binds ByRef Camera3D. UpdateCameraPro applies its movement/rotation/zoom args DIRECTLY via
/// raymath (no input reads, no GL, no GetTime), so it is fully deterministic in the FAST SUBSET and gives real mutation
/// oracles — not just an ABI smoke test:
///   * zoom moves the camera along its forward axis by a known distance (proves the Single arg + ByRef copy-back),
///   * movement.x translates the camera forward (proves the FIRST Vector3 arg, and — because a movement/rotation SWAP would
///     rotate instead of translate — that the two Vector3 params are not transposed),
///   * a nonzero rotation reorients the view (proves the SECOND Vector3 arg is received, not dropped).
/// UpdateCamera(CAMERA_CUSTOM) is a documented no-op (raylib enum: "UpdateCamera() does nothing"), which round-trips the
/// 44-byte Camera3D unchanged through ByRef. (Headless, EVERY mode is a no-op — UpdateCamera's built-in modes are
/// input/GL-driven — so the `mode` Integer's marshaling is pinned STRUCTURALLY by the parity test's "mode As Integer" scan,
/// not behaviorally here; behavioral mode coverage would need a live window + input, i.e. Integration.)
/// Self-Ignores when the DLL isn't staged or predates the exports.
/// </summary>
[TestFixture]
public class RaylibCameraTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    private const int CAMERA_CUSTOM = 0;       // raylib CameraMode: UpdateCamera() does nothing
    private const int CAMERA_PERSPECTIVE = 0;  // raylib CameraProjection
    private const float Tol = 1e-3f;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct Camera3D
    {
        public Vector3 position, target, up;
        public float fovy;
        public int projection;
    }

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateCamera(ref Camera3D camera, int mode);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateCameraPro(ref Camera3D camera, Vector3 movement, Vector3 rotation, float zoom);

    private static Camera3D LookDownZ() => new Camera3D
    {
        position = new Vector3(0f, 0f, 10f),
        target = new Vector3(0f, 0f, 0f),
        up = new Vector3(0f, 1f, 0f),
        fovy = 45f,
        projection = CAMERA_PERSPECTIVE,
    };

    private static bool EnsureAvailable()
    {
        var cam = LookDownZ();
        try { Framework_UpdateCamera(ref cam, CAMERA_CUSTOM); return true; }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return false; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the rcamera exports; refresh IDE\\ first."); return false; }
    }

    [Test]
    public void UpdateCamera_custom_mode_is_a_noop_and_round_trips_the_struct()
    {
        if (!EnsureAvailable()) return;

        var cam = new Camera3D
        {
            position = new Vector3(1f, 2f, 10f),
            target = new Vector3(3f, 4f, 5f),
            up = new Vector3(0f, 1f, 0f),
            fovy = 60f,
            projection = CAMERA_PERSPECTIVE,
        };

        Framework_UpdateCamera(ref cam, CAMERA_CUSTOM); // documented no-op -> struct must come back byte-for-byte

        Assert.Multiple(() =>
        {
            Assert.That(cam.position.x, Is.EqualTo(1f).Within(Tol));
            Assert.That(cam.position.y, Is.EqualTo(2f).Within(Tol));
            Assert.That(cam.position.z, Is.EqualTo(10f).Within(Tol));
            Assert.That(cam.target.x, Is.EqualTo(3f).Within(Tol));
            Assert.That(cam.target.y, Is.EqualTo(4f).Within(Tol));
            Assert.That(cam.target.z, Is.EqualTo(5f).Within(Tol));
            Assert.That(cam.up.y, Is.EqualTo(1f).Within(Tol));
            Assert.That(cam.fovy, Is.EqualTo(60f).Within(Tol));
            Assert.That(cam.projection, Is.EqualTo(CAMERA_PERSPECTIVE));
        });
    }

    [Test]
    public void UpdateCameraPro_zoom_moves_position_along_the_forward_axis()
    {
        if (!EnsureAvailable()) return;

        var cam = LookDownZ(); // position (0,0,10) looking at origin; distance 10
        // movement=0, rotation=0, zoom=+2 -> only CameraMoveToTarget runs:
        //   distance 10 -> 12; position = target + forward*(-12) = (0,0,12). target unchanged.
        Framework_UpdateCameraPro(ref cam, default, default, 2f);

        Assert.Multiple(() =>
        {
            Assert.That(cam.position.x, Is.EqualTo(0f).Within(Tol));
            Assert.That(cam.position.y, Is.EqualTo(0f).Within(Tol));
            Assert.That(cam.position.z, Is.EqualTo(12f).Within(Tol), "zoom +2 pushes the camera from z=10 to z=12");
            Assert.That(cam.target.x, Is.EqualTo(0f).Within(Tol));
            Assert.That(cam.target.y, Is.EqualTo(0f).Within(Tol));
            Assert.That(cam.target.z, Is.EqualTo(0f).Within(Tol), "target is unchanged by zoom");
        });
    }

    [Test]
    public void UpdateCameraPro_forward_movement_translates_camera_and_is_not_swapped_with_rotation()
    {
        if (!EnsureAvailable()) return;

        var cam = LookDownZ(); // forward = normalize(target - position) = (0,0,-1)
        // movement.x = 1 (move forward, moveInWorldPlane) -> position & target both shift by (0,0,-1):
        //   position (0,0,10) -> (0,0,9); target (0,0,0) -> (0,0,-1); zoom 0 leaves them put.
        // If movement/rotation were SWAPPED, rotation.x=1 would YAW instead of translate and position.z would stay ~10.
        Framework_UpdateCameraPro(ref cam, new Vector3(1f, 0f, 0f), default, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(cam.position.x, Is.EqualTo(0f).Within(Tol));
            Assert.That(cam.position.y, Is.EqualTo(0f).Within(Tol));
            Assert.That(cam.position.z, Is.EqualTo(9f).Within(Tol), "movement.x=1 moves the camera forward from z=10 to z=9");
            Assert.That(cam.target.z, Is.EqualTo(-1f).Within(Tol), "the look-at target moves forward with the camera");
        });
    }

    [Test]
    public void UpdateCameraPro_rotation_reorients_the_view()
    {
        if (!EnsureAvailable()) return;

        var cam = LookDownZ();
        // rotation.x = 90 (yaw), movement=0, zoom=0. CameraYaw swings the look-at target around the up axis, so the
        // target must move appreciably off the origin. Exact trig isn't asserted -- the point is that the SECOND Vector3
        // arg reaches raylib and takes effect (a dropped/zeroed rotation would leave the target at the origin).
        Framework_UpdateCameraPro(ref cam, default, new Vector3(90f, 0f, 0f), 0f);

        float targetShift = MathF.Sqrt(cam.target.x * cam.target.x + cam.target.y * cam.target.y + cam.target.z * cam.target.z);
        Assert.That(targetShift, Is.GreaterThan(1f), "a 90-degree yaw must move the look-at target off the origin");
    }
}
