using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C4 (raw raylib shader management). Loading/using a shader needs a live OpenGL
/// context, so this fixture is [Category("Integration")] + [NonParallelizable] (it owns the single global raylib window).
/// It is a real end-to-end exercise, not a smoke: it compiles a custom fragment shader, reads back uniform/attribute
/// locations, and drives every C4 entry point with genuine payloads — a float (SetShaderValue), a vec2 (SetShaderValueV),
/// an identity Matrix by value (SetShaderValueMatrix), and a real GPU-loaded Texture2D by value (SetShaderValueTexture).
/// The reliable by-value ABI proofs are IsShaderValid (reads id+locs back out of the returned Shader), the non-zero GPU
/// texture id (Texture2D return), and every uniform/attribute location resolving (Shader passed by value into the getters);
/// the setter calls are supplementary smoke checks (a __cdecl size mismatch often does not AV, and a native AV is an
/// uncatchable corrupted-state exception anyway). Self-skips via Assert.Ignore when no display/GL context exists or the
/// DLL predates the C4 exports.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibCoreC4ShaderTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;
    private const int SHADER_UNIFORM_FLOAT = 0;
    private const int SHADER_UNIFORM_VEC2 = 1;

    // Local struct mirrors (verify the ABI independently of the production wrapper).
    [StructLayout(LayoutKind.Sequential)] private struct Shader { public uint id; public IntPtr locs; }
    [StructLayout(LayoutKind.Sequential)] private struct Texture2D { public uint id; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct Image { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix
    {
        // raylib declaration order (m0,m4,m8,m12 / m1,m5,m9,m13 / ...) — memory layout follows declaration order.
        public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15;
        public static Matrix Identity() => new Matrix { m0 = 1f, m5 = 1f, m10 = 1f, m15 = 1f };
    }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern Shader Framework_LoadShaderFromMemory(string vsCode, string fsCode);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsShaderValid(Shader shader);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_GetShaderLocation(Shader shader, string uniformName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_GetShaderLocationAttrib(Shader shader, string attribName);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetShaderValue(Shader shader, int locIndex, IntPtr value, int uniformType);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetShaderValueV(Shader shader, int locIndex, IntPtr value, int uniformType, int count);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetShaderValueMatrix(Shader shader, int locIndex, Matrix mat);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetShaderValueTexture(Shader shader, int locIndex, Texture2D texture);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadShader(Shader shader);

    [DllImport(DLL, CallingConvention = CC)] private static extern Image Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern Texture2D Framework_LoadTextureFromImage(Image image);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadTexture(Texture2D texture);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(Image image);

    // A minimal GLSL 3.3 fragment shader (LoadShaderFromMemory pairs it with raylib's default vertex shader). Every custom
    // uniform is USED so the GLSL compiler cannot optimize it away and GetShaderLocation returns a real slot.
    private const string FragShader =
        "#version 330\n" +
        "in vec2 fragTexCoord;\n" +
        "in vec4 fragColor;\n" +
        "out vec4 finalColor;\n" +
        "uniform sampler2D texture0;\n" +
        "uniform float uAmount;\n" +
        "uniform vec2 uOffset;\n" +
        "uniform mat4 uMat;\n" +
        "void main()\n" +
        "{\n" +
        "    vec4 texel = texture(texture0, fragTexCoord + uOffset);\n" +
        "    float k = uMat[0][0] + uMat[1][1];\n" +
        "    finalColor = texel * fragColor * uAmount * k;\n" +
        "}\n";

    [Test]
    public void Shader_load_locations_and_uniform_setters_marshal_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_c4_shader_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C4 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        Shader shader = default;
        bool shaderLoaded = false;
        Image img = default;
        bool imgLoaded = false;
        Texture2D tex = default;
        bool texLoaded = false;
        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);

            // First C4 export exercised: on a DLL that predates C4 (InitWindow is a pre-existing rcore-C1 export, so it
            // succeeds), the missing entry point surfaces HERE — skip gracefully instead of erroring red.
            try { shader = Framework_LoadShaderFromMemory(null, FragShader); shaderLoaded = true; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C4 exports; refresh IDE\\ first."); return; }

            // A GPU-loaded, non-default texture for the sampler2d ABI check (a zeroed Texture2D would early-return in
            // raylib, proving nothing about the by-value struct).
            img = Framework_GenImageColor(2, 2, 200, 40, 60, 255);
            imgLoaded = true;
            tex = Framework_LoadTextureFromImage(img);
            texLoaded = true;

            int locAmount = Framework_GetShaderLocation(shader, "uAmount");
            int locOffset = Framework_GetShaderLocation(shader, "uOffset");
            int locMat = Framework_GetShaderLocation(shader, "uMat");
            int locTex0 = Framework_GetShaderLocation(shader, "texture0");
            int attribPos = Framework_GetShaderLocationAttrib(shader, "vertexPosition");

            Assert.Multiple(() =>
            {
                // Shader returned by value: IsShaderValid reads id (>0) and locs (!=NULL) back out of the 16-byte struct.
                Assert.That(Framework_IsShaderValid(shader), Is.True, "custom shader compiled and is GPU-valid");
                // Texture returned by value from LoadTextureFromImage: a real GPU id.
                Assert.That(tex.id, Is.Not.EqualTo(0u), "GenImageColor -> LoadTextureFromImage yields a non-zero GPU texture id");
                // vertexPosition is a built-in attribute in raylib's default vertex shader — always present (never
                // optimized out), so this is the reliable location anchor proving GetShaderLocationAttrib works.
                Assert.That(attribPos, Is.GreaterThanOrEqualTo(0), "vertexPosition attribute location resolves");
                // texture0 + the custom uniforms are all used in the shader body, so they should resolve too.
                Assert.That(locTex0, Is.GreaterThanOrEqualTo(0), "texture0 sampler uniform resolves");
                Assert.That(locAmount, Is.GreaterThanOrEqualTo(0), "uAmount float uniform resolves");
                Assert.That(locOffset, Is.GreaterThanOrEqualTo(0), "uOffset vec2 uniform resolves");
                Assert.That(locMat, Is.GreaterThanOrEqualTo(0), "uMat mat4 uniform resolves");

                // Every C4 uniform setter marshals its payload without throwing a managed exception. Smoke checks, NOT the
                // ABI oracle (the by-value proofs are the location/id/IsShaderValid asserts above): a __cdecl by-value size
                // mismatch frequently does not AV, and a native AV is an uncatchable corrupted-state exception regardless.
                Assert.DoesNotThrow(() =>
                {
                    IntPtr pf = Marshal.AllocHGlobal(sizeof(float));
                    try { Marshal.Copy(new float[] { 0.75f }, 0, pf, 1); Framework_SetShaderValue(shader, locAmount, pf, SHADER_UNIFORM_FLOAT); }
                    finally { Marshal.FreeHGlobal(pf); }
                }, "SetShaderValue (float via void* -> IntPtr)");

                Assert.DoesNotThrow(() =>
                {
                    IntPtr pv = Marshal.AllocHGlobal(sizeof(float) * 2);
                    try { Marshal.Copy(new float[] { 0.1f, 0.2f }, 0, pv, 2); Framework_SetShaderValueV(shader, locOffset, pv, SHADER_UNIFORM_VEC2, 1); }
                    finally { Marshal.FreeHGlobal(pv); }
                }, "SetShaderValueV (vec2 via void* -> IntPtr, count 1)");

                Assert.DoesNotThrow(() => Framework_SetShaderValueMatrix(shader, locMat, Matrix.Identity()),
                    "SetShaderValueMatrix (Matrix by value, 64 bytes)");

                Assert.DoesNotThrow(() => Framework_SetShaderValueTexture(shader, locTex0, tex),
                    "SetShaderValueTexture (Texture2D by value, real GPU id)");
            });
        }
        finally
        {
            if (texLoaded) { try { Framework_UnloadTexture(tex); } catch { } }
            if (imgLoaded) { try { Framework_UnloadImage(img); } catch { } }
            if (shaderLoaded) { try { Framework_UnloadShader(shader); } catch { } }
            Framework_CloseWindow();
        }
    }
}
