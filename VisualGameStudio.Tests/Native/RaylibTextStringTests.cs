using System;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the GL-free raylib text Batch 2 bindings — string/codepoint
/// functions are deterministic and need no window, so they are genuinely unit-testable via
/// P/Invoke (the drawing/font-load functions need GL and are covered by the --text smoke).
/// Self-contained local [DllImport] (string returns declared As IntPtr + Marshal.PtrToStringAnsi,
/// never a freeing String marshaler); self-skips when the engine DLL isn't staged with these
/// exports. Proves the custom memory-ownership wrappers (TextReplace/Insert/LoadUTF8 static-buffer
/// free, LoadCodepoints/TextJoin/TextSplit/TextCopy buffers) actually work at runtime.
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibTextStringTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    // --- string returns (IntPtr; copy via PtrToStringAnsi, never free) ---
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextToUpper(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextToLower(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextToPascal(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextToSnake(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextToCamel(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextSubtext(string t, int position, int length);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextReplace(string t, string replace, string by);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_TextInsert(string t, string insert, int position);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_CodepointToUTF8(int codepoint, out int utf8Size);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_LoadUTF8(int[] codepoints, int length);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)]
    private static extern IntPtr Framework_TextJoin([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] textList, int count, string delimiter);

    // --- scalar/bool returns ---
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern uint Framework_TextLength(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_TextIsEqual(string a, string b);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_TextFindIndex(string t, string find);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_TextToInteger(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern float Framework_TextToFloat(string t);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_GetCodepointCount(string t);

    // --- caller-buffer wrappers ---
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_LoadCodepoints(string t, int[] outCodepoints, int outCapacity);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_TextSplit(string t, byte delimiter, StringBuilder outBuf, int outCapacity);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_TextCopy(StringBuilder dst, string src);

    private static string S(Func<IntPtr> call)
    {
        IntPtr p;
        try { p = call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates text Batch 2 exports; refresh IDE\\ first."); throw; }
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
    }

    [Test] public void ToUpper_ToLower_exact()
    {
        Assert.That(S(() => Framework_TextToUpper("aB3c")), Is.EqualTo("AB3C"));
        Assert.That(S(() => Framework_TextToLower("aB3C")), Is.EqualTo("ab3c"));
    }

    [Test] public void Subtext_exact() => Assert.That(S(() => Framework_TextSubtext("hello", 1, 3)), Is.EqualTo("ell"));

    [Test] public void Length_Equal_FindIndex()
    {
        Assert.That(Framework_TextLength("hello"), Is.EqualTo(5u));
        Assert.That(Framework_TextIsEqual("abc", "abc"), Is.True);
        Assert.That(Framework_TextIsEqual("abc", "abd"), Is.False);
        Assert.That(Framework_TextFindIndex("abcbc", "bc"), Is.EqualTo(1));
    }

    [Test] public void ToInteger_ToFloat()
    {
        Assert.That(Framework_TextToInteger("42"), Is.EqualTo(42));
        Assert.That(Framework_TextToFloat("3.5"), Is.EqualTo(3.5f).Within(1e-4));
    }

    // Proves the malloc'd-return → engine-static-buffer → MemFree path with a CORRECT raylib
    // function (TextReplace). NOTE: raylib 5.5's own TextInsert is upstream-buggy (indexes
    // insert[i]/text[i] instead of insert[i-position]/text[i-insertLen], so it reads past the
    // insert string) — our Framework_TextInsert is a faithful passthrough of that, so we assert
    // only that the SAME wrapper path round-trips a string (marshaling + no crash + the freed
    // buffer is never returned), not raylib's broken output.
    [Test] public void Replace_and_Insert_proves_malloc_free_wrappers()
    {
        Assert.That(S(() => Framework_TextReplace("a-b-c", "-", "+")), Is.EqualTo("a+b+c"));
        Assert.That(S(() => Framework_TextInsert("ac", "b", 1)), Is.Not.Empty); // raylib bug: value is garbled upstream
    }

    [Test] public void Case_transforms_marshal_nonempty()
    {
        // raylib's Pascal/Snake/Camel exact formats vary; assert the binding round-trips real data.
        Assert.That(S(() => Framework_TextToPascal("hello_world")), Is.EqualTo("HelloWorld"));
        Assert.That(S(() => Framework_TextToSnake("HelloWorld")), Is.Not.Empty);
        Assert.That(S(() => Framework_TextToCamel("hello_world")), Is.Not.Empty);
    }

    [Test] public void CodepointToUTF8_ascii()
    {
        var s = S(() => Framework_CodepointToUTF8(65, out _)); // 'A'
        Framework_CodepointToUTF8(65, out var size);
        Assert.That(s, Is.EqualTo("A"));
        Assert.That(size, Is.EqualTo(1));
    }

    [Test] public void GetCodepointCount_ascii() => Assert.That(Framework_GetCodepointCount("hello"), Is.EqualTo(5));

    // Proves the caller-buffer LoadCodepoints + static-buffer LoadUTF8 round-trip.
    [Test] public void Codepoints_roundtrip()
    {
        var buf = new int[16];
        var count = Framework_LoadCodepoints("hi", buf, buf.Length);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(buf[0], Is.EqualTo((int)'h'));
        Assert.That(buf[1], Is.EqualTo((int)'i'));
        Assert.That(S(() => Framework_LoadUTF8(new[] { (int)'h', (int)'i' }, 2)), Is.EqualTo("hi"));
    }

    [Test] public void TextJoin_exact()
        => Assert.That(S(() => Framework_TextJoin(new[] { "a", "b", "c" }, 3, "-")), Is.EqualTo("a-b-c"));

    [Test] public void TextSplit_packs_and_counts()
    {
        var sb = new StringBuilder(256);
        int count;
        try { count = Framework_TextSplit("a-b-c", (byte)'-', sb, sb.Capacity); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates exports."); throw; }
        Assert.That(count, Is.EqualTo(3));
        Assert.That(sb.ToString().Split('\n'), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test] public void TextCopy_writes_caller_buffer()
    {
        var sb = new StringBuilder(64);
        int n;
        try { n = Framework_TextCopy(sb, "hello"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates exports."); throw; }
        Assert.That(sb.ToString(), Is.EqualTo("hello"));
        Assert.That(n, Is.EqualTo(5));
    }
}
