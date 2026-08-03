using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 7a Step 3 — the STUB-RUNTIME run proofs: generated BasicLang programs whose
/// lowered proxy calls execute against a test-emitted FAKE <c>g_net</c> (the P0 harness
/// trick — no shim, no AOT publish; phase 5 goes live in 7b).
///
/// <para><b>The harness itself lives in <see cref="NetStubHarness"/></b> (Task-8 Step 0, I5)
/// — shared, because Tasks 9/10/11 all need run-level proofs and a copied harness drifts.
/// This fixture is the SCENARIOS.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class NetProxyStubRunTests
{
    private static readonly Lazy<NetTypeResolver> SharedResolver = NetStubHarness.SharedResolver;

    private const string RegexFullName = NetStubHarness.RegexFullName;

    private static (string Cpp, NetSurface Surface) CompileWithSurface(string source) =>
        NetStubHarness.CompileWithSurface(source);

    private static string StubTranslationUnit(
        IEnumerable<NetStubHarness.StubSlot> slots, string shimSetup = "") =>
        NetStubHarness.StubTranslationUnit(slots, shimSetup);

    private static string RunWithStub(string cpp, NetSurface surface, string stubTu) =>
        NetStubHarness.RunWithStub(cpp, surface, stubTu);

    private static NetMemberDescriptor Winner(
        string typeFullName, NetCallForm form, string member, params string[] args) =>
        NetStubHarness.Winner(typeFullName, form, member, args);

    // ====================================================================================
    // 1. The sequence proof: ctor → instance → static, arguments and the receiver handle.
    // ====================================================================================

    [Test]
    public void StaticInstanceCtorAndStringReturn_RecordTheExpectedSequence()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim ok = r.IsMatch("aaa")
              If ok Then
               Console.WriteLine("MATCHED")
              End If
              Dim s = Regex.Escape("x+y")
              Console.WriteLine(s)
             End Sub
            End Module
            """);

        var ctor = NetNameMangler.Mangle(
            Winner(RegexFullName, NetCallForm.Constructor, ".ctor", "System.String"));
        var isMatch = NetNameMangler.Mangle(
            Winner(RegexFullName, NetCallForm.Instance, "IsMatch", "System.String"));
        var escape = NetNameMangler.Mangle(
            Winner(RegexFullName, NetCallForm.Static, "Escape", "System.String"));

        var stub = StubTranslationUnit(
            new[]
            {
                // §8.2: the ctor export hands back a fresh handle — 42 here, and the
                // instance call receiving self=42 is the proof the handle FLOWED from the
                // ctor result into the receiver NetRef.
                new NetStubHarness.StubSlot(ctor,
                    "[](const char* a0, uint64_t* result) -> int32_t {"
                    + " std::printf(\"CALL ctor(%s)\\n\", a0); *result = 42; return 0; }"),
                new NetStubHarness.StubSlot(isMatch,
                    "[](uint64_t self, const char* a0, int32_t* result) -> int32_t {"
                    + " std::printf(\"CALL IsMatch(self=%llu,%s)\\n\","
                    + " (unsigned long long)self, a0); *result = 1; return 0; }"),
                // P0 string ownership: the callee hands back an allocated buffer; the
                // proxy copies it and frees through g_shim.free_ — the FREE line is the
                // ownership proof.
                new NetStubHarness.StubSlot(escape,
                    "[](const char* a0, char** result) -> int32_t {"
                    + " std::printf(\"CALL Escape(%s)\\n\", a0);"
                    + " *result = stub_strdup(\"ESCAPED\"); return 0; }"),
            },
            shimSetup:
            "        BasicLang::blnet::g_shim.free_ = [](void* p) {"
            + " std::printf(\"FREE\\n\"); std::free(p); };");

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo(
            "CALL ctor(a+)\n"
            + "CALL IsMatch(self=42,aaa)\n"
            + "MATCHED\n"
            + "CALL Escape(x+y)\n"
            + "FREE\n"
            + "ESCAPED\n"),
            "the recorded call sequence, arguments, receiver handle, planted Boolean, "
            + "transfer-buffer string and its blnet_free must all match");
    }

    // ====================================================================================
    // 2. A non-OK status surfaces as a catchable NetException with the planted chain.
    // ====================================================================================

    [Test]
    public void NonOkStatus_SurfacesAsACatchableNetException_WithThePlantedChain()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Try
               Dim ok = r.IsMatch("boom")
               Console.WriteLine("NOT REACHED")
              Catch e As InvalidOperationException
               Console.WriteLine("CAUGHT: " & e.Message)
              End Try
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var ctor = NetNameMangler.Mangle(
            Winner(RegexFullName, NetCallForm.Constructor, ".ctor", "System.String"));
        var isMatch = NetNameMangler.Mangle(
            Winner(RegexFullName, NetCallForm.Instance, "IsMatch", "System.String"));

        var stub = StubTranslationUnit(
            new[]
            {
                new NetStubHarness.StubSlot(ctor,
                    "[](const char*, uint64_t* result) -> int32_t { *result = 7; return 0; }"),
                // Status 5 = BLNET_E_MANAGED_EXCEPTION territory; the exact value is
                // irrelevant — any non-OK status must route through NetCheckTyped.
                new NetStubHarness.StubSlot(isMatch,
                    "[](uint64_t, const char*, int32_t*) -> int32_t { return 5; }"),
            },
            shimSetup:
            "        BasicLang::blnet::g_shim.free_ = [](void* p) { std::free(p); };\n"
            + "        BasicLang::blnet::g_shim.last_error ="
            + " [](char** type, char** message) -> int32_t {\n"
            + "            *type = stub_strdup(\"System.InvalidOperationException;"
            + "System.SystemException;System.Exception\");\n"
            + "            *message = stub_strdup(\"stub boom\");\n"
            + "            return 0;\n"
            + "        };");

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo(
            "CAUGHT: stub boom\n"
            + "done\n"),
            "a non-OK status must become a BasicLang::NetException carrying the planted "
            + "inheritance chain (§11.1 — the typed ladder matched InvalidOperationException) "
            + "whose what() is the RAW managed message (parity: ex.Message is the managed "
            + "message verbatim), and execution must continue after the Try");
    }

    // ====================================================================================
    // 3. DateTime crosses through the Task-6 converters — bit-pattern assert in the stub.
    // ====================================================================================

    [Test]
    public void DateTimeArgument_CrossesThroughToNetDatetime_BitPatternExact()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim d As New DateTime(2026, 8, 2)
              Dim u = TimeZoneInfo.ConvertTimeToUtc(d)
              Console.WriteLine(u.Hour)
             End Sub
            End Module
            """);

        var convert = NetNameMangler.Mangle(
            Winner("System.TimeZoneInfo", NetCallForm.Static, "ConvertTimeToUtc",
                   "System.DateTime"));

        // The .NET-computed wire vectors (never round-trip-only — a symmetric bug passes a
        // round trip): outbound 2026-08-02T00:00:00 Unspecified = raw ticks, kind bits 00;
        // planted inbound = 2026-08-02T03:04:05 Utc = ticks | kind 01 in the top two bits.
        var expectedOutbound = (ulong)new DateTime(2026, 8, 2).Ticks;
        var planted = (ulong)new DateTime(2026, 8, 2, 3, 4, 5).Ticks | (1UL << 62);

        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(convert,
                "[](uint64_t a0, uint64_t* result) -> int32_t {"
                + " std::printf(\"DT:%016llx\\n\", (unsigned long long)a0);"
                + $" *result = {planted.ToString(System.Globalization.CultureInfo.InvariantCulture)}ULL;"
                + " return 0; }"),
        });

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo(
            $"DT:{expectedOutbound:x16}\n"
            + "3\n"),
            "outbound: to_net_datetime must present the EXACT P1 dateData bit pattern "
            + "(62-bit ticks | 2-bit kind) the .NET side computes; inbound: "
            + "from_net_datetime must reconstruct the planted Utc 03:04:05 instant");
    }

    // ====================================================================================
    // 4. Property get/set through the getter slot and the synthesized set_X slot.
    // ====================================================================================

    [Test]
    public void PropertyGetAndSet_RouteThroughTheAccessorSlots()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim st As Stream
              st.Position = 5
              Dim p = st.Position
              Console.WriteLine(p)
             End Sub
            End Module
            """);

        var position = SharedResolver.Value.GetMembers("System.IO.Stream")
            .Single(m => m.Name == "Position" && m.Kind == NetMemberCategory.Property);
        var getter = NetNameMangler.Mangle(position);
        var setter = NetNameMangler.Mangle(NetAccessorSynthesis.SetterFor(position));

        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(setter,
                "[](uint64_t self, int64_t a0) -> int32_t {"
                + " std::printf(\"SET(self=%llu,%lld)\\n\","
                + " (unsigned long long)self, (long long)a0); return 0; }"),
            new NetStubHarness.StubSlot(getter,
                "[](uint64_t self, int64_t* result) -> int32_t {"
                + " std::printf(\"GET(self=%llu)\\n\", (unsigned long long)self);"
                + " *result = 99; return 0; }"),
        });

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo(
            "SET(self=0,5)\n"
            + "GET(self=0)\n"
            + "99\n"),
            "the WRITE routes through the synthesized set_X slot (receiver, value) and the "
            + "READ through the getter-shaped property slot; the never-assigned receiver is "
            + "the 0 handle (§8.2 — it must cross as 0, never reach a table)");
    }

    // ====================================================================================
    // 5. The proxy-name agreement pin: a wrong slot name is a COMPILE failure, never a
    //    silently-misbound call.
    // ====================================================================================

    [Test]
    public void MismatchedSlotName_FailsTheNativeCompile()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim s = Regex.Escape("x")
              Console.WriteLine(s)
             End Sub
            End Module
            """);

        var escape = NetNameMangler.Mangle(
            Winner(RegexFullName, NetCallForm.Static, "Escape", "System.String"));

        // The stub fills a NONEXISTENT slot name — as a wrong mangle would. g_net has no
        // such member, so the stub TU cannot compile: name agreement between the lowering,
        // the bindings struct and the shim exports is enforced by the C++ type system, not
        // by convention.
        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(escape + "_wrong",
                "[](const char*, char** result) -> int32_t {"
                + " *result = stub_strdup(\"x\"); return 0; }"),
        });

        var failure = Assert.Throws<AssertionException>(() => RunWithStub(cpp, surface, stub));
        Assert.That(failure!.Message, Does.Contain("compilation failed").IgnoreCase,
            "a slot-name mismatch must surface as a C++ COMPILE failure");
    }
}
