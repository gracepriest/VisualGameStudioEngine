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
/// <para><b>The harness shape.</b> The real <c>blnet_startup.g.cpp</c> is EXCLUDED from the
/// translation-unit set; a per-test <c>stub.g.cpp</c> takes its place, defining
/// <c>g_net</c>, filling the slots with recording C++ lambdas (canned statuses/values,
/// printf to stdout so ordering interleaves verifiably with program output — C++ streams
/// are stdio-synchronized by default), and filling the minimal <c>g_shim</c> members the
/// runtime paths under test touch (<c>free_</c> for string-return ownership,
/// <c>last_error</c> for the NetException chain). Slot names are re-derived per test from
/// the SAME seams production uses (resolver descriptors → <see cref="NetNameMangler"/>),
/// so a mangling or overload-selection drift breaks the C++ COMPILE — which is exactly the
/// proxy-name-mismatch pin.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class NetProxyStubRunTests
{
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    private const string RegexFullName = "System.Text.RegularExpressions.Regex";

    private static (string exe, string argsTemplate) RequireCompiler()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null)
            Assert.Ignore("No C++ compiler available for run tests.");
        return compiler!.Value;
    }

    // ------------------------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------------------------

    private static (string Cpp, NetSurface Surface) CompileWithSurface(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "unexpected findings: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
        var surface = NetSurfaceCollector.Collect(
            new[] { module }, null, () => SharedResolver.Value,
            new List<NetReferenceDiagnostic>());
        Assert.That(surface.IsNonEmpty, Is.True, "the program under test must draw a surface");
        return (cpp, surface);
    }

    /// <summary>
    /// One stub slot: the mangled name plus the full C++ lambda text (its signature must
    /// match the slot's C ABI — a mismatch fails the compile, which is the pin).
    /// </summary>
    private sealed record StubSlot(string SlotName, string Lambda);

    private static string StubTranslationUnit(
        IEnumerable<StubSlot> slots, string shimSetup = "")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#include \"blnet.h\"");
        sb.AppendLine("#include \"blnet_runtime.hpp\"   /* g_shim — the members the stub fills */");
        sb.AppendLine("#include \"blnet_bindings.g.hpp\"");
        sb.AppendLine("#include <cstdio>");
        sb.AppendLine("#include <cstdlib>");
        sb.AppendLine("#include <cstring>");
        sb.AppendLine();
        sb.AppendLine("/* THE definition the bindings header declares extern; the REAL");
        sb.AppendLine("   blnet_startup.g.cpp is excluded from this build on purpose. */");
        sb.AppendLine("BlnetProxyTable g_net{};");
        sb.AppendLine();
        sb.AppendLine("static char* stub_strdup(const char* s) {");
        sb.AppendLine("    size_t n = std::strlen(s) + 1;");
        sb.AppendLine("    char* p = (char*)std::malloc(n);");
        sb.AppendLine("    std::memcpy(p, s, n);");
        sb.AppendLine("    return p;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("namespace {");
        sb.AppendLine("struct StubInit {");
        sb.AppendLine("    StubInit() {");
        if (!string.IsNullOrEmpty(shimSetup))
            sb.AppendLine(shimSetup);
        foreach (var slot in slots)
            sb.AppendLine($"        g_net.{slot.SlotName} = {slot.Lambda};");
        sb.AppendLine("    }");
        sb.AppendLine("};");
        sb.AppendLine("StubInit g_stub_init;");
        sb.AppendLine("} /* anonymous namespace */");
        return sb.ToString();
    }

    private static string RunWithStub(string cpp, NetSurface surface, string stubTu)
    {
        var compiler = RequireCompiler();
        var artifacts = NetProxyEmitter.Emit(surface, "Stub.dll");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in artifacts)
        {
            if (kv.Key == NetProxyEmitter.StartupFileName)
                continue;   // the stub replaces the real startup TU
            files[kv.Key] = kv.Value;
        }
        files["prog.g.cpp"] = cpp;
        files["stub.g.cpp"] = stubTu;
        return CppCompile.CompileAndRunFiles(
            files, new[] { "prog.g.cpp", "stub.g.cpp" }, compiler);
    }

    private static NetMemberDescriptor Winner(
        string typeFullName, NetCallForm form, string member, params string[] args)
    {
        var result = SharedResolver.Value.ResolveOverload(typeFullName, form, member, args);
        Assert.That(result.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            $"fixture provenance: {typeFullName}.{member}({string.Join(", ", args)}) must resolve");
        return result.Member!;
    }

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
                new StubSlot(ctor,
                    "[](const char* a0, uint64_t* result) -> int32_t {"
                    + " std::printf(\"CALL ctor(%s)\\n\", a0); *result = 42; return 0; }"),
                new StubSlot(isMatch,
                    "[](uint64_t self, const char* a0, int32_t* result) -> int32_t {"
                    + " std::printf(\"CALL IsMatch(self=%llu,%s)\\n\","
                    + " (unsigned long long)self, a0); *result = 1; return 0; }"),
                // P0 string ownership: the callee hands back an allocated buffer; the
                // proxy copies it and frees through g_shim.free_ — the FREE line is the
                // ownership proof.
                new StubSlot(escape,
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
                new StubSlot(ctor,
                    "[](const char*, uint64_t* result) -> int32_t { *result = 7; return 0; }"),
                // Status 5 = BLNET_E_MANAGED_EXCEPTION territory; the exact value is
                // irrelevant — any non-OK status must route through NetCheckTyped.
                new StubSlot(isMatch,
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
            new StubSlot(convert,
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
            new StubSlot(setter,
                "[](uint64_t self, int64_t a0) -> int32_t {"
                + " std::printf(\"SET(self=%llu,%lld)\\n\","
                + " (unsigned long long)self, (long long)a0); return 0; }"),
            new StubSlot(getter,
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
            new StubSlot(escape + "_wrong",
                "[](const char*, char** result) -> int32_t {"
                + " *result = stub_strdup(\"x\"); return 0; }"),
        });

        var failure = Assert.Throws<AssertionException>(() => RunWithStub(cpp, surface, stub));
        Assert.That(failure!.Message, Does.Contain("compilation failed").IgnoreCase,
            "a slot-name mismatch must surface as a C++ COMPILE failure");
    }
}
