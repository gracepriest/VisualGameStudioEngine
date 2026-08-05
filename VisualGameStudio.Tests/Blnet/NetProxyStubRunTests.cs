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

    private static (string Cpp, NetSurface Surface) CompileWithSurface(
        string source, bool optimize = false) =>
        NetStubHarness.CompileWithSurface(source, optimize);

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
    // 5. §8.3's ref/out POINTER SLOT — the value the callee writes back reaches the caller.
    // ====================================================================================

    /// <summary>
    /// <c>Int32.TryParse("42", n)</c>: the canonical <c>out</c> shape, end to end. The slot's C
    /// signature carries <c>int32_t* a1</c>, the proxy takes <c>int32_t&amp;</c> and does the
    /// &amp;-taking itself, and the value the stub plants must arrive in the BasicLang local.
    ///
    /// <para>Printing <c>n</c> AFTER the call is the whole oracle: a lowering that passed a
    /// temporary, or that dropped the write-back, still compiles and still returns True — it
    /// just leaves <c>n</c> at zero.</para>
    /// </summary>
    [Test]
    public void OutParameter_WritesBackIntoTheBasicLangLocal()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim n As Integer
              Dim ok = Int32.TryParse("42", n)
              If ok Then
               Console.WriteLine(n)
              End If
             End Sub
            End Module
            """);

        var tryParse = NetNameMangler.Mangle(
            Winner("System.Int32", NetCallForm.Static, "TryParse",
                   "System.String", "System.Int32"));

        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(tryParse,
                "[](const char* a0, int32_t* a1, int32_t* result) -> int32_t {"
                + " std::printf(\"TRYPARSE(%s)\\n\", a0);"
                + " *a1 = 42; *result = 1; return 0; }"),
        });

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo("TRYPARSE(42)\n42\n"),
            "the out slot must carry the callee's write back into the BasicLang local. A '0' on "
            + "the second line means the write-back was dropped — the proxy wrote its temporary "
            + "and nothing copied it out, or the call site passed a copy instead of the "
            + "variable. Nothing else about this program would go red for either bug.");
    }

    /// <summary>
    /// The same out-parameter call moved INSIDE a branch and a loop — the shape the Task-10
    /// review flagged for the goto-lowering hazard.
    ///
    /// <para><b>Recorded because it is the NEGATIVE result, and the negative result is the
    /// useful one.</b> A plain <c>Integer</c> ByRef row emits <b>no prologue at all</b>: §8.3's
    /// pointer slot hands the proxy the native local directly (<c>…TryParse("42", n)</c>),
    /// because for that row the native variable ALREADY is an lvalue of the wire type. With no
    /// declaration between the label and the jump there is nothing for a <c>goto</c> to cross,
    /// so this shape was never broken and this test cannot fail for the reason it looks like it
    /// tests. It is kept as a coverage pin for branch/loop write-back, and the comment stands so
    /// the next author does not mistake it for the goto regression test.</para>
    ///
    /// <para>The rows that DO emit a prologue are the ones whose native representation differs
    /// from their wire — Char, §6.4's pairs — and §8.6's array copy-in guard. The braced-region
    /// pin for those lives in
    /// <c>NetOutboundCopyTests.Row3_CopyInInsideABranch_IsBraceScopedForGotoLowering</c>, which
    /// does fail when the region is unbraced.</para>
    /// </summary>
    [Test]
    public void OutParameter_InsideBranchAndLoop_WritesBackEveryIteration()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim n As Integer
              Dim go As Boolean = True
              If go Then
               Dim ok = Int32.TryParse("42", n)
               Console.WriteLine(n)
              End If
              For i As Integer = 0 To 1
               Dim ok2 = Int32.TryParse("42", n)
               Console.WriteLine(n)
              Next
             End Sub
            End Module
            """);

        var tryParse = NetNameMangler.Mangle(
            Winner("System.Int32", NetCallForm.Static, "TryParse",
                   "System.String", "System.Int32"));

        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(tryParse,
                "[](const char* a0, int32_t* a1, int32_t* result) -> int32_t {"
                + " std::printf(\"TRYPARSE(%s)\\n\", a0);"
                + " *a1 = 42; *result = 1; return 0; }"),
        });

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo(
            "TRYPARSE(42)\n42\n"
            + "TRYPARSE(42)\n42\n"
            + "TRYPARSE(42)\n42\n"),
            "an out slot inside a branch or a loop must write back on EVERY iteration — a "
            + "lowering that hoisted the write-back out of the loop would print 42 once and 0 "
            + "after.");
    }

    /// <summary>
    /// The SAME out-parameter scenario through the OPTIMIZER — the repo law that codegen is
    /// validated through the optimizer and the CLI, not only through the non-optimizing helper.
    /// The program is shaped to make the optimizer's copy facts matter: <c>n</c> is given a
    /// value the ByRef call then overwrites, and the result is READ through a comparison rather
    /// than printed, so a stale copy changes the BRANCH rather than the spelling.
    /// </summary>
    // Was [Ignore]d for chip task_807f81e3 — CopyPropagationPass did not kill copy facts on a
    // ByRef write, so `n` kept the fact from `Dim n = 0` and `If n = 42` folded against the
    // stale 0, printing STALE. Fixed by InvalidateByRefWrites in that pass; the chip's other
    // half (the C++ backend dropping ByRef from the SIGNATURE) landed in the same change.
    // Never was .NET-specific — user ByRef Subs hit it identically.
    [Test]
    public void OutParameter_SurvivesTheOptimizer()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim n = 0
              Dim ok = Int32.TryParse("42", n)
              If n = 42 Then
               Console.WriteLine("FRESH")
              Else
               Console.WriteLine("STALE")
              End If
             End Sub
            End Module
            """, optimize: true);

        var tryParse = NetNameMangler.Mangle(
            Winner("System.Int32", NetCallForm.Static, "TryParse",
                   "System.String", "System.Int32"));

        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(tryParse,
                "[](const char* a0, int32_t* a1, int32_t* result) -> int32_t {"
                + " std::printf(\"TRYPARSE(%s)\\n\", a0);"
                + " *a1 = 42; *result = 1; return 0; }"),
        });

        Assert.That(RunWithStub(cpp, surface, stub).Replace("\r\n", "\n"),
            Is.EqualTo("TRYPARSE(42)\nFRESH\n"),
            "the value written through the out slot must survive optimization. 'STALE' means a "
            + "pass kept a copy fact for `n` across a call that writes it — see the Ignore "
            + "reason and chip task_807f81e3.");
    }

    // ====================================================================================
    // 6. §8.3's Char row at RUN time — the byte >= 0x80 case an emit assertion cannot reach.
    // ====================================================================================

    /// <summary>
    /// <b>The <c>unsigned char</c> hop, proven rather than asserted.</b> Emit-level tests pin
    /// the CAST TEXT, which looks equally right whichever way it is written; only a value with
    /// its high bit set separates them. Native <c>char</c> is SIGNED here, so 0xC8 is −56 and a
    /// bare <c>static_cast&lt;uint16_t&gt;</c> sign-extends it to <c>0xFFC8</c> — a wrong UTF-16
    /// code unit handed to .NET, silently, which is exactly the class §12.1 exists to catch.
    ///
    /// <para>The round trip is deliberate: the first call plants 0x00C8 inbound (narrowed to the
    /// 1-byte native Char per §14.10) and the second sends it back out, so the printed wire
    /// value is the outbound encoding of a byte that is negative as a native <c>char</c>.</para>
    /// </summary>
    [Test]
    public void CharAboveSeven_FBits_ZeroExtendsOutbound_NotSignExtends()
    {
        var (cpp, surface) = CompileWithSurface("""
            Module M
             Sub Main()
              Dim c = Convert.ToChar(200)
              Dim n = Convert.ToInt32(c)
              Console.WriteLine(n)
             End Sub
            End Module
            """);

        var toChar = NetNameMangler.Mangle(
            Winner("System.Convert", NetCallForm.Static, "ToChar", "System.Int32"));
        var toInt32 = NetNameMangler.Mangle(
            Winner("System.Convert", NetCallForm.Static, "ToInt32", "System.Char"));

        var stub = StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(toChar,
                "[](int32_t a0, uint16_t* result) -> int32_t {"
                + " *result = (uint16_t)a0; return 0; }"),
            new NetStubHarness.StubSlot(toInt32,
                "[](uint16_t a0, int32_t* result) -> int32_t {"
                + " std::printf(\"WIRE:%04x\\n\", (unsigned)a0);"
                + " *result = (int32_t)a0; return 0; }"),
        });

        var output = RunWithStub(cpp, surface, stub);

        Assert.That(output.Replace("\r\n", "\n"), Is.EqualTo("WIRE:00c8\n200\n"),
            "the outbound Char must ZERO-extend: 0x00c8. 'WIRE:ffc8' means the `unsigned char` "
            + "hop was dropped and a signed native char sign-extended onto the UTF-16 wire — "
            + "every byte >= 0x80 would reach .NET as U+FFxx.");
    }

    // ====================================================================================
    // 7. The proxy-name agreement pin: a wrong slot name is a COMPILE failure, never a
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
