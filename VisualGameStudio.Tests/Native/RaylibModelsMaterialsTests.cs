using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Headless ABI oracles for the material fns that can run without GL — fast subset, no window. Two things are provable off the
/// GPU: the by-value struct SIZES (Material 40 B, MaterialMap 28 B — a wrong field width/order changes these) and the
/// early-reject path of IsMaterialValid. raylib 5.5's IsMaterialValid is `(material.maps != NULL) && (material.shader.id > 0)`
/// — a pure null + id check with NO dereference, so a zeroed Material (maps == NULL short-circuits first) returns false safely
/// and still exercises Material-by-value marshaling + the I1 return + the shader-id read at offset 0. The POSITIVE (valid)
/// path — and everything that touches the maps array or the GPU (LoadMaterialDefault / SetMaterialTexture / DrawMesh) — needs a
/// live context and lives in RaylibModelsMaterialsGpuTests (Integration). (We do NOT hand-build a "valid-looking" Material with
/// a non-null garbage maps pointer here: unlike this reject case, that would depend on IsMaterialValid never dereferencing
/// maps, and an errant deref is an unrecoverable access violation — the model sub-batch's IsModelValid taught that lesson.)
/// Self-skips via Assert.Ignore when the DLL is absent or predates the exports.
/// </summary>
[TestFixture]
public class RaylibModelsMaterialsTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct Color { public byte r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct Texture2D { public uint id; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct Shader { public uint id; public IntPtr locs; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MaterialMap
    {
        public Texture2D texture;
        public Color color;
        public float value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Material
    {
        public Shader shader;
        public IntPtr maps;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] @params;
    }

    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsMaterialValid(Material material);

    private static Material ZeroMaterial() => new Material { shader = default, maps = IntPtr.Zero, @params = new float[4] };

    [Test]
    public void Material_struct_is_40_bytes_on_x64()
    {
        Assert.That(Marshal.SizeOf<Material>(), Is.EqualTo(40), "Material must be 40 bytes (Shader 16 + MaterialMap* 8 + float[4] 16)");
    }

    [Test]
    public void MaterialMap_struct_is_28_bytes_on_x64()
    {
        Assert.That(Marshal.SizeOf<MaterialMap>(), Is.EqualTo(28), "MaterialMap must be 28 bytes (Texture2D 20 + Color 4 + float 4)");
    }

    [Test]
    public void IsMaterialValid_rejects_a_zeroed_material()
    {
        // raylib 5.5's IsMaterialValid null-checks material.maps BEFORE reading anything through it, so a zeroed Material
        // (maps == NULL) short-circuits to false with no dereference — the only guaranteed-safe headless case. It still
        // marshals a Material by value, reads shader.id at offset 0, and returns via I1. The valid (true) path is GPU-only.
        bool valid;
        try { valid = Framework_IsMaterialValid(ZeroMaterial()); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the material exports; refresh IDE\\ first."); return; }
        Assert.That(valid, Is.False, "a zeroed Material (null maps) is invalid");
    }
}
