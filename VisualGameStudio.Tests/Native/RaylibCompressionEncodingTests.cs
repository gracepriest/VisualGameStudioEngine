using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C10 (raw raylib compression / encoding / hash API). All of these are pure
/// CPU data transforms — no window, no GL, no device — so this fixture runs FULLY HEADLESS (no [Category("Integration")])
/// in the fast subset. The correctness-critical properties are the heap-buffer ownership contract and the exact bytes:
///
///  • CompressData/DecompressData round-trip the original bytes (and DEFLATE shrinks repetitive input); each returns a
///    raylib-malloc'd buffer released via Framework_MemFree.
///  • EncodeDataBase64/DecodeDataBase64 round-trip and match the canonical "Man" ⇄ "TWFu" vector; heap buffers freed.
///  • ComputeCRC32 matches the universal IEEE CRC-32 check value 0xCBF43926 for "123456789".
///  • ComputeMD5 is cross-checked byte-for-byte against .NET's own MD5 (self-validating, no hand-copied digest); its
///    static int[4] returns a pointer that must NOT be freed. raylib's native-endian uint32 words match MD5's standard
///    little-endian-word digest directly.
///  • ComputeSHA1 is a FAITHFUL passthrough of raylib 5.5, whose ComputeSHA1 has upstream undefined behaviour (fixed
///    only after 5.5 — CHANGELOG #5957) and does NOT yield the standard digest; we pin its binding contract (stable,
///    input-sensitive 20-byte static buffer, never freed) rather than a golden value. ComputeMD5 already proves the
///    identical hash-marshaling mechanism against a standard digest.
///
/// Self-contained local [DllImport]; self-skips (whole fixture) when the engine DLL isn't staged with the C10 exports.
/// </summary>
[TestFixture]
public class RaylibCompressionEncodingTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_CompressData(byte[] data, int dataSize, ref int compDataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_DecompressData(byte[] compData, int compDataSize, ref int dataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_EncodeDataBase64(byte[] data, int dataSize, ref int outputSize);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_DecodeDataBase64(string data, ref int outputSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern uint Framework_ComputeCRC32(byte[] data, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_ComputeMD5(byte[] data, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_ComputeSHA1(byte[] data, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_MemFree(IntPtr ptr);

    [OneTimeSetUp]
    public void ProbeEngine()
    {
        try { Framework_ComputeCRC32(new byte[] { 1 }, 1); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C10 exports; refresh IDE\\ first."); }
    }

    // Copy `size` bytes out of a raylib-malloc'd buffer and release it with the engine's own allocator.
    private static byte[] DrainAndFree(IntPtr p, int size)
    {
        Assert.That(p, Is.Not.EqualTo(IntPtr.Zero), "heap-returning codec must not return null");
        Assert.That(size, Is.GreaterThan(0), "codec must report a positive output length");
        var bytes = new byte[size];
        Marshal.Copy(p, bytes, 0, size);
        Framework_MemFree(p);
        return bytes;
    }

    [Test]
    public void CompressData_then_DecompressData_round_trips_and_shrinks_repetitive_input()
    {
        // Highly repetitive -> DEFLATE must shrink it well below the original.
        var original = new byte[8192];
        for (int i = 0; i < original.Length; i++) original[i] = (byte)('A' + (i % 4));

        int compSize = 0;
        byte[] compressed = DrainAndFree(Framework_CompressData(original, original.Length, ref compSize), compSize);
        Assert.That(compSize, Is.LessThan(original.Length), "DEFLATE must compress repetitive data");

        int decSize = 0;
        byte[] decompressed = DrainAndFree(Framework_DecompressData(compressed, compressed.Length, ref decSize), decSize);
        Assert.Multiple(() =>
        {
            Assert.That(decSize, Is.EqualTo(original.Length), "decompressed length equals the original");
            Assert.That(decompressed, Is.EqualTo(original), "decompress(compress(x)) == x");
        });
    }

    [Test]
    public void Base64_matches_the_canonical_vector_and_round_trips_arbitrary_bytes()
    {
        // Canonical RFC 4648 example: "Man" (0x4D 0x61 0x6E) <-> "TWFu".
        var man = new byte[] { 0x4D, 0x61, 0x6E };
        int encSize = 0;
        byte[] encoded = DrainAndFree(Framework_EncodeDataBase64(man, man.Length, ref encSize), encSize);
        string encodedText = Encoding.ASCII.GetString(encoded).TrimEnd('\0');
        Assert.That(encodedText, Is.EqualTo("TWFu"), "EncodeDataBase64(\"Man\") == \"TWFu\"");

        int decSize = 0;
        byte[] decoded = DrainAndFree(Framework_DecodeDataBase64("TWFu", ref decSize), decSize);
        Assert.That(decoded, Is.EqualTo(man), "DecodeDataBase64(\"TWFu\") == \"Man\"");

        // Round-trip every byte value through the String-in / IntPtr-out path.
        var arbitrary = new byte[256];
        for (int i = 0; i < 256; i++) arbitrary[i] = (byte)i;
        int rtEnc = 0;
        byte[] b64 = DrainAndFree(Framework_EncodeDataBase64(arbitrary, arbitrary.Length, ref rtEnc), rtEnc);
        string b64Text = Encoding.ASCII.GetString(b64).TrimEnd('\0');
        int rtDec = 0;
        byte[] back = DrainAndFree(Framework_DecodeDataBase64(b64Text, ref rtDec), rtDec);
        Assert.That(back, Is.EqualTo(arbitrary), "base64 encode->decode is the identity on all byte values");
    }

    [Test]
    public void ComputeCRC32_matches_the_universal_IEEE_check_value()
    {
        var data = Encoding.ASCII.GetBytes("123456789");
        uint crc = Framework_ComputeCRC32(data, data.Length);
        Assert.That(crc, Is.EqualTo(0xCBF43926u), "standard CRC-32/IEEE check value for \"123456789\"");
    }

    [Test]
    public void ComputeMD5_matches_dotnet_MD5()
    {
        var data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog");
        IntPtr p = Framework_ComputeMD5(data, data.Length);   // pointer to a STATIC int[4] — do NOT free
        Assert.That(p, Is.Not.EqualTo(IntPtr.Zero));
        var raw = new byte[16];
        Marshal.Copy(p, raw, 0, 16);
        // MD5's standard digest is little-endian words, matching raylib's native uint32 byte layout directly.
        Assert.That(Convert.ToHexString(raw), Is.EqualTo(Convert.ToHexString(MD5.HashData(data))),
            "raylib ComputeMD5 bytes must equal the standard MD5 digest");
    }

    [Test]
    public void ComputeSHA1_marshals_a_deterministic_input_sensitive_static_digest()
    {
        // ⚠ raylib 5.5's ComputeSHA1 has UPSTREAM undefined behaviour (fixed only after 5.5 — raylib CHANGELOG:
        // "[rcore] REVIEWED: ComputeSHA1(), fix undefined behaviour (#5957)"), so on 5.5 it does NOT produce the
        // standard SHA1 digest (verified: it disagrees with .NET's SHA1 under every byte order). This binding is a
        // FAITHFUL raw passthrough of raylib 5.5 as-is — NOT a bug for us to fix (contrast ComputeMD5 above, which IS
        // standard and is asserted against .NET; a raylib upgrade past 5.5 makes this return the correct digest for
        // free). We therefore pin the binding CONTRACT — a non-null pointer to a stable, input-sensitive 20-byte static
        // buffer (never freed) — instead of a golden value we'd have to hardcode from the buggy (UB) output. The
        // hash-marshaling mechanism itself (static uint32[] pointer -> N bytes) is proven correct by ComputeMD5.
        var data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog");

        IntPtr p1 = Framework_ComputeSHA1(data, data.Length);   // pointer to a STATIC int[5] — do NOT free
        Assert.That(p1, Is.Not.EqualTo(IntPtr.Zero));
        var d1 = new byte[20]; Marshal.Copy(p1, d1, 0, 20);

        IntPtr p2 = Framework_ComputeSHA1(data, data.Length);
        var d2 = new byte[20]; Marshal.Copy(p2, d2, 0, 20);

        var other = Encoding.ASCII.GetBytes("a different message");
        IntPtr p3 = Framework_ComputeSHA1(other, other.Length);
        var d3 = new byte[20]; Marshal.Copy(p3, d3, 0, 20);

        Assert.Multiple(() =>
        {
            Assert.That(Array.Exists(d1, b => b != 0), Is.True, "SHA1 produces a non-empty digest");
            Assert.That(d2, Is.EqualTo(d1), "same input -> identical digest (deterministic static buffer)");
            Assert.That(d3, Is.Not.EqualTo(d1), "different input -> different digest (it actually hashes the data)");
        });
    }
}
