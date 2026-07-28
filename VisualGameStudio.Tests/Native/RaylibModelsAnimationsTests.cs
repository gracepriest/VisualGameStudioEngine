using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Headless ABI oracles for the rmodels ANIMATIONS sub-batch — fast subset, no window. Unusually for a draw-adjacent module,
/// ALL SIX fns have a deref-free runtime path exercisable off the GPU, so there is no Integration fixture:
///   • Struct SIZES: ModelAnimation 56 B, BoneInfo 36 B, Transform 40 B (a wrong field width/order changes these).
///   • IsModelAnimationValid: on a boneCount MISMATCH raylib 5.5 returns false BEFORE the bone loop (no deref) — headless-safe;
///     and a hand-built MATCHING pair (boneCount 1, real 36-B BoneInfo allocations) walks bones[0].parent, pinning the
///     ModelAnimation/Model bones-pointer offsets + BoneInfo.parent@32 + the by-value pass of both 120-B Model and 56-B anim.
///   • UnloadModelAnimation / UnloadModelAnimations: free()-of-NULL is a C-standard no-op, so a zeroed/empty animation (or a
///     NULL array) unloads safely — exercises the by-value ModelAnimation marshaling + the void call.
///   • LoadModelAnimations on a missing file returns a NULL array (pure file parse, no GL; *animCount is not guaranteed written
///     on failure) — exercises the CharSet.Ansi string in, the ByRef out-count, and the IntPtr return.
///   • UpdateModelAnimation(Bones) on an EMPTY model (meshCount 0) + EMPTY animation (frameCount 0) no-op: UpdateModelAnimationBones
///     guards frameCount>0 (skips the frame%frameCount) and UpdateModelAnimation's mesh loop is bounded by meshCount 0 — no deref.
/// The REAL animation-application path (Update* against a loaded, animated model + poses) needs an animated asset (IQM/GLTF/M3D)
/// this repo doesn't carry, and is NOT exercised here — the parity type-scan pins the static binding, these pin the ABI + the
/// empty/reject paths. Hand-built BoneInfo memory (AllocHGlobal) is NEVER passed to Unload* (RL_FREE cross-heap), only FreeHGlobal'd.
/// Self-skips via Assert.Ignore when the DLL is absent or predates the exports.
/// </summary>
[TestFixture]
public class RaylibModelsAnimationsTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const int BONEINFO_PARENT_OFFSET = 32;  // char name[32] precedes int parent

    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; }
    [StructLayout(LayoutKind.Sequential)] private struct Vector4 { public float x, y, z, w; }
    [StructLayout(LayoutKind.Sequential)] private struct Matrix { public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Model
    {
        public Matrix transform;
        public int meshCount, materialCount;
        public IntPtr meshes, materials, meshMaterial;
        public int boneCount;
        public IntPtr bones, bindPose;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Transform { public Vector3 translation; public Vector4 rotation; public Vector3 scale; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoneInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] name;
        public int parent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ModelAnimation
    {
        public int boneCount, frameCount;
        public IntPtr bones, framePoses;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] name;
    }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_LoadModelAnimations(string fileName, ref int animCount);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateModelAnimation(Model model, ModelAnimation anim, int frame);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateModelAnimationBones(Model model, ModelAnimation anim, int frame);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadModelAnimation(ModelAnimation anim);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadModelAnimations(IntPtr animations, int animCount);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsModelAnimationValid(Model model, ModelAnimation anim);

    private static ModelAnimation EmptyAnim() => new ModelAnimation { name = new byte[32] };

    [Test] public void ModelAnimation_struct_is_56_bytes_on_x64()
        => Assert.That(Marshal.SizeOf<ModelAnimation>(), Is.EqualTo(56), "ModelAnimation = 2 int + 2 ptr + char[32]");

    [Test] public void BoneInfo_struct_is_36_bytes_on_x64()
        => Assert.That(Marshal.SizeOf<BoneInfo>(), Is.EqualTo(36), "BoneInfo = char[32] + int");

    [Test] public void Transform_struct_is_40_bytes_on_x64()
        => Assert.That(Marshal.SizeOf<Transform>(), Is.EqualTo(40), "Transform = Vector3 + Vector4 + Vector3");

    [Test]
    public void IsModelAnimationValid_rejects_mismatched_bone_counts()
    {
        // model.boneCount != anim.boneCount -> raylib returns false BEFORE the bone loop (no deref of the null bones ptrs).
        var model = new Model { boneCount = 1 };            // bones = IntPtr.Zero
        var anim = EmptyAnim(); anim.boneCount = 2;         // bones = IntPtr.Zero
        bool valid;
        try { valid = Framework_IsModelAnimationValid(model, anim); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the animation exports; refresh IDE\\ first."); return; }
        Assert.That(valid, Is.False, "a bone-count mismatch is invalid (no bone dereference)");
    }

    [Test]
    public void IsModelAnimationValid_walks_matching_and_mismatched_bone_parents()
    {
        // Matching boneCount (1) makes raylib walk bones[0].parent for both — a real deref over KNOWN-valid 36-B allocations.
        // Equal parents -> valid; differing -> invalid. Pins Model.bones / ModelAnimation.bones offsets + BoneInfo.parent@32.
        IntPtr modelBones = Marshal.AllocHGlobal(Marshal.SizeOf<BoneInfo>());
        IntPtr animBones = Marshal.AllocHGlobal(Marshal.SizeOf<BoneInfo>());
        try
        {
            for (int k = 0; k < 36; k++) { Marshal.WriteByte(modelBones, k, 0); Marshal.WriteByte(animBones, k, 0); }
            Marshal.WriteInt32(modelBones, BONEINFO_PARENT_OFFSET, -1);
            Marshal.WriteInt32(animBones, BONEINFO_PARENT_OFFSET, -1);

            var model = new Model { boneCount = 1, bones = modelBones };
            var anim = new ModelAnimation { boneCount = 1, bones = animBones, name = new byte[32] };

            bool matched;
            try { matched = Framework_IsModelAnimationValid(model, anim); }
            catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the animation exports; refresh IDE\\ first."); return; }
            Assert.That(matched, Is.True, "equal bone parents -> the skeletons match");

            Marshal.WriteInt32(animBones, BONEINFO_PARENT_OFFSET, 5);  // desync the one bone's parent
            Assert.That(Framework_IsModelAnimationValid(model, anim), Is.False, "a differing bone parent -> the skeletons don't match");
        }
        finally
        {
            Marshal.FreeHGlobal(modelBones);
            Marshal.FreeHGlobal(animBones);
        }
    }

    [Test]
    public void UnloadModelAnimation_on_a_zeroed_animation_is_safe()
    {
        // frameCount 0 -> the framePoses-free loop never runs; RL_FREE(NULL bones) / RL_FREE(NULL framePoses) are no-ops.
        try { Framework_UnloadModelAnimation(EmptyAnim()); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the animation exports; refresh IDE\\ first."); return; }
        Assert.Pass("UnloadModelAnimation on a zeroed animation did not crash");
    }

    [Test]
    public void UnloadModelAnimations_on_a_null_array_is_safe()
    {
        // animCount 0 -> the per-element loop never runs; RL_FREE(NULL) is a no-op.
        try { Framework_UnloadModelAnimations(IntPtr.Zero, 0); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the animation exports; refresh IDE\\ first."); return; }
        Assert.Pass("UnloadModelAnimations on a null/empty array did not crash");
    }

    [Test]
    public void LoadModelAnimations_on_a_missing_file_returns_null()
    {
        // raylib returns a NULL ModelAnimation* for a missing/unsupported file and does NOT guarantee writing *animCount on
        // that failure path (a seeded sentinel survives), so the reliable "no animations" signal is the null return — asserting
        // the count would only test our own seed. This still exercises the entry point, the CharSet.Ansi string in, the ByRef
        // out-count marshaling, and the IntPtr return.
        int count = 0;
        IntPtr p;
        try { p = Framework_LoadModelAnimations("this_file_does_not_exist_vgs_anim.iqm", ref count); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the animation exports; refresh IDE\\ first."); return; }
        Assert.That(p, Is.EqualTo(IntPtr.Zero), "a missing animation file yields a null ModelAnimation* array");
        if (p != IntPtr.Zero) Framework_UnloadModelAnimations(p, count);  // defensive; the assert above should hold
    }

    [Test]
    public void Update_animation_on_empty_model_and_animation_is_safe()
    {
        // UpdateModelAnimationBones guards frameCount>0 (so no frame%frameCount on the empty anim); UpdateModelAnimation's mesh
        // loop is bounded by meshCount 0. Neither dereferences the null bones/framePoses/meshes pointers.
        var model = new Model();      // meshCount 0, boneCount 0, all pointers null
        var anim = EmptyAnim();       // frameCount 0
        try { Framework_UpdateModelAnimationBones(model, anim, 0); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the animation exports; refresh IDE\\ first."); return; }
        Framework_UpdateModelAnimation(model, anim, 0);
        Assert.Pass("Update* on an empty model + empty animation did not crash");
    }
}
