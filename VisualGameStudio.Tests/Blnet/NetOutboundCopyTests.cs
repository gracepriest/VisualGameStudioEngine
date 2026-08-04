using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 10 — spec §8.6, native BasicLang collections crossing OUTBOUND.
///
/// <para><b>The representation rule this whole fixture is organised around, stated once.</b> A
/// .NET <c>T[]</c> VALUE is ALWAYS a handle — §8.5 governs consuming it, and Task 9 shipped
/// that. Copying happens ONLY when a NATIVE array (a <c>std::vector&lt;T&gt;</c>, which has no
/// <c>GCHandle</c> and therefore no handle at all) sits on the other side of an assignment or a
/// parameter. §8.6's four-row table is exactly that rule enumerated, and the four tests under
/// "the four-row table" are one test per row — including row 1, whose correct behavior is that
/// NOTHING is copied. A fixture that only tested the copying rows would pass just as happily
/// with a lowering that copied every array it saw.</para>
///
/// <para><b>§14.11, and why its SCOPING is tested rather than just its existence.</b> The
/// by-value copy is one-way: mutations a .NET callee makes are invisible to the caller's
/// <c>std::vector</c>, where the C# backend WOULD see them. <c>ref</c>/<c>out</c> slots are
/// EXEMPT — they are read back. Both halves are asserted, and the run-level proof asserts them
/// in ONE program, because a divergence tested only in the direction that diverges cannot
/// distinguish "correctly one-way" from "the write-back is broken everywhere".</para>
///
/// <para><b>Naming provenance:</b> every expected proxy name is RE-DERIVED from the seam
/// production uses — <c>NetArrayCopy</c>'s form table for the copy helpers,
/// <c>NetTypeResolver.ResolveOverload</c> + <c>NetNameMangler.Mangle</c> for members — never a
/// hard-coded string.</para>
/// </summary>
[TestFixture]
public class NetOutboundCopyTests
{
    private static readonly Lazy<NetTypeResolver> SharedResolver = NetStubHarness.SharedResolver;

    /// <summary>
    /// A resolver over the framework closure PLUS the fixture's probe assembly, built once.
    /// §8.6's <c>ref T[]</c> row has no non-generic BCL member to reach it — <c>Array.Resize</c>
    /// is generic and §7.2 omits generic methods outright — so the probe is not convenience, it
    /// is the only way to exercise the row at all below the Integration tier.
    /// </summary>
    private static readonly Lazy<NetTypeResolver?> ProbeResolver = new(() =>
    {
        var dir = NetShimPipelineFixture.NewTempDir("blnet-t10-probe-");
        try
        {
            var probe = NetShimPipelineFixture.EmitProbeAssembly(dir);
            return NetTypeResolver.Create(
                NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }).ToList());
        }
        catch (Exception)
        {
            return null;   // no net8.0 reference pack on this machine — see RequireProbe
        }
    });

    private static NetTypeResolver RequireProbe()
    {
        var resolver = ProbeResolver.Value;
        if (resolver == null)
            Assert.Ignore("No net8.0 reference pack; the §8.6 probe assembly cannot be built.");
        return resolver!;
    }

    private const string ConvertFullName = "System.Convert";
    private const string BagFullName = "Aot.Probe.Bag";
    private const string SinkFullName = "Blnet.T10.Sink";

    /// <summary>
    /// A throwaway assembly declaring ONE member per §8.6 element form, plus the ref/out array
    /// pair. It exists so <see cref="NetStubHarness.CompileShim"/> has real members for the
    /// generated wrappers to call: the copy helpers are emitted per element form in the surface's
    /// SIGNATURES, so a surface that reaches all thirteen forms has to name thirteen members, and
    /// no framework type offers them.
    ///
    /// <para>Compiled against <c>NetTypeResolverTestRefs.FrameworkPaths</c> — the SAME set the
    /// shim compile uses — so there is no reference-pack-versus-implementation-assembly mismatch
    /// to produce a CS0012 unrelated to what is under test.</para>
    /// </summary>
    private static readonly Lazy<string> SinkAssembly = new(() =>
    {
        var forms = NetArrayCopy.Forms;
        var members = string.Join("\n", forms.Select((f, i) =>
            $"        public static void Take{i}({CsArray(f)} v) {{ }}"));
        var source = $$"""
            namespace Blnet.T10
            {
                public static class Sink
                {
            {{members}}
                    public static void Fill(ref int[] v) { v = new int[0]; }
                    public static void Emit(out int[] v) { v = new int[0]; }
                }
            }
            """;

        var dir = NetShimPipelineFixture.NewTempDir("blnet-t10-sink-");
        var path = Path.Combine(dir, "BlnetT10Sink.dll");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "BlnetT10Sink",
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(
                p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        using (var stream = File.Create(path))
        {
            var emit = compilation.Emit(stream);
            Assert.That(emit.Success, Is.True,
                "the fixture's own sink assembly failed to compile, so nothing below means "
                + "anything:\n" + source + "\n"
                + string.Join("\n", emit.Diagnostics.Where(
                    d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        }
        return path;
    });

    /// <summary>A §8.6 form's element as C# array syntax — provenance for the sink's signatures.</summary>
    private static string CsArray(NetArrayCopyForm form) => form.ElementFullName switch
    {
        "System.Boolean" => "bool[]",
        "System.SByte" => "sbyte[]",
        "System.Byte" => "byte[]",
        "System.Int16" => "short[]",
        "System.UInt16" => "ushort[]",
        "System.Int32" => "int[]",
        "System.UInt32" => "uint[]",
        "System.Int64" => "long[]",
        "System.UInt64" => "ulong[]",
        "System.Single" => "float[]",
        "System.Double" => "double[]",
        "System.Char" => "char[]",
        "System.String" => "string[]",
        _ => throw new InvalidOperationException(
            "§8.6 gained element form '" + form.ElementFullName + "' — add it to this fixture's "
            + "sink so the emitted shim for it is still compile-checked."),
    };

    // ------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------

    private static SemanticAnalyzer Analyze(string source, NetTypeResolver resolver)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => resolver, nativeBackend: true);
        analyzer.Analyze(ast);
        return analyzer;
    }

    /// <param name="optimize">
    /// Run the standard optimizer pipeline before codegen — the CLI-equivalent path, and the
    /// repo law that codegen is validated through the optimizer and not only through the
    /// non-optimizing helper.
    /// </param>
    private static string CompileToCpp(
        string source, NetTypeResolver? resolver = null, bool optimize = false)
    {
        resolver ??= SharedResolver.Value;
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => resolver, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "unexpected findings: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        if (optimize)
        {
            var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(module);
        }
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
    }

    /// <summary>
    /// The SPLIT emission path with the optimizer on — what <c>CppProjectBuilder</c> actually
    /// drives (and therefore what both the CLI and the IDE drive). Distinct from
    /// <see cref="CompileToCpp"/>'s combined <c>Generate</c>: the two disagree about whether a
    /// call's NetRef temp survives into the emitted code, which is exactly the §8.6 row-2 hazard
    /// <c>EffectiveCppType</c> exists for.
    /// </summary>
    private static string SplitCpp(string source, NetTypeResolver resolver)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => resolver, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "Program");
        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        var split = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .GenerateSplit(module, "Probe", new[] { module }, emitMain: true);
        return string.Join("\n", split.Files.Values);
    }

    /// <summary>The §8.6 form for a .NET element, asserted present — fixture provenance.</summary>
    private static NetArrayCopyForm Form(string elementFullName)
    {
        Assert.That(NetArrayCopy.TryGetFormForElement(elementFullName, out var form), Is.True,
            $"fixture provenance: §8.6 must declare a copy form for '{elementFullName}'");
        return form!;
    }

    private static IEnumerable<string> Findings(SemanticAnalyzer analyzer, string code) =>
        analyzer.NetDiagnostics.Where(d => d.Code == code).Select(d => d.Message);

    // ====================================================================================
    // 1. The four-row table (spec §8.6).
    // ====================================================================================

    /// <summary>
    /// ROW 1 — <c>Dim a = obj.GetValues()</c> (inferred) KEEPS THE HANDLE. No copy at all.
    ///
    /// <para>The negative half is the load-bearing one: an implementation that copied on every
    /// .NET array it saw would satisfy rows 2-4 and silently break the whole representation
    /// rule, because indexing would then hit a native snapshot instead of the managed array
    /// §8.5's synthetic exports reach.</para>
    /// </summary>
    [Test]
    public void Row1_InferredResult_KeepsTheHandleAndCopiesNothing()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim A = Convert.FromBase64String("AQID")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("BasicLang::NetRef A"),
            "§8.6 row 1: an INFERRED .NET array result keeps the handle — the local must declare "
            + "as NetRef, exactly as Task 9 left it:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("bl_net_array_read_"),
            "§8.6 row 1 copies NOTHING. A readback here means the lowering copies on sight "
            + "rather than on the DECLARATION, which detaches indexing from the managed "
            + "array:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("bl_net_array_new_"),
            "nor is there anything to copy IN — no native array exists in this program:\n" + cpp);
    }

    /// <summary>
    /// ROW 2 — <c>Dim a() As Byte = obj.GetValues()</c> (DECLARED native array) materializes by
    /// copy: a one-way snapshot into the <c>std::vector</c>.
    /// </summary>
    [Test]
    public void Row2_DeclaredNativeArray_MaterializesByCopy()
    {
        var form = Form("System.Byte");
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim A() As Byte = Convert.FromBase64String("AQID")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("std::vector<uint8_t> A"),
            "§8.6 row 2: the DECLARATION wins — the local is a native array, not a handle:\n" + cpp);
        Assert.That(cpp, Does.Contain("BasicLang::net::" + form.ReadExportName + "("),
            $"the handle must materialize through §8.6's readback proxy '{form.ReadExportName}'. "
            + "Without it the generated C++ assigns a BasicLang::NetRef to a std::vector, which "
            + "is a compile error in code the user never wrote:\n" + cpp);
    }

    /// <summary>
    /// ROW 3 — passing a NATIVE array to a .NET parameter COPIES IN, through the RAII guard the
    /// Step-0 seam contract requires, and does NOT read back (§14.11's one-way divergence).
    /// </summary>
    [Test]
    public void Row3_ByValueNativeArray_CopiesInThroughAnRaiiGuard_AndNeverReadsBack()
    {
        var form = Form("System.Byte");
        var winner = NetStubHarness.Winner(
            ConvertFullName, NetCallForm.Static, "ToBase64String", "System.Byte[]");

        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim B(2) As Byte
              Dim S = Convert.ToBase64String(B)
              Console.WriteLine(S)
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Match(
                @"BasicLang::NetRef (blnet_t\d+) = BasicLang::net::" + form.NewExportName + @"\(B\);"),
            "§8.6 row 3: the copy-in must land in a BasicLang::NetRef PROLOGUE local — that is "
            + "the RAII release slot the Step-0 seam contract requires, because NetCheckTyped "
            + "throws from inside the call statement and a trailing release would be skipped:\n"
            + cpp);
        Assert.That(cpp, Does.Match(
                @"BasicLang::net::" + NetNameMangler.Mangle(winner) + @"\(blnet_t\d+\)"),
            "the proxy call must take the GUARD, not the std::vector:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("bl_net_array_read_"),
            "§14.11: a BY-VALUE array argument is ONE-WAY. A readback here would make the "
            + "divergence disappear — and with it Task 13's parity program, which needs the "
            + "by-value case to have exactly one correct expected output:\n" + cpp);
    }

    /// <summary>
    /// ⛔ The copy-in guard must be BRACE-SCOPED, because this backend flattens all control flow
    /// to labels + <c>goto</c> in one function scope: an unbraced
    /// <c>BasicLang::NetRef t0 = …;</c> emitted inside an <c>If</c> is a declaration the
    /// branch's own forward jump crosses, and C++ rejects the translation unit outright
    /// ("jump to label 'if0_end' crosses initialization of 'NetRef t0'").
    ///
    /// <para>Every other §8.6 fixture places its call at straight-line statement level, so this
    /// is the shape that pins the region. The run-level proof of the same seam lives in
    /// <c>NetProxyStubRunTests.ArgumentPrologue_InsideBranchAndLoop_CompilesUnderGotoLowering</c>;
    /// this one keeps §8.6's own row honest without paying for a compile.</para>
    /// </summary>
    [Test]
    public void Row3_CopyInInsideABranch_IsBraceScopedForGotoLowering()
    {
        var form = Form("System.Byte");

        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim B(2) As Byte
              Dim Go As Boolean = True
              If Go Then
               Dim S = Convert.ToBase64String(B)
               Console.WriteLine(S)
              End If
             End Sub
            End Module
            """, optimize: true);

        Assert.That(cpp, Does.Match(
                @"\{\s*BasicLang::NetRef blnet_t\d+ = BasicLang::net::"
                + form.NewExportName + @"\(B\);"),
            "the copy-in guard must open its own brace-scoped region. Unbraced, the enclosing "
            + "If's `goto` crosses this initialization and the emitted C++ does not compile — "
            + "and the guard would also outlive the call it belongs to:\n" + cpp);

        // A loop has a different label topology (a back-edge as well as a forward exit), so it
        // gets its own pin rather than riding on the branch case.
        var looped = CompileToCpp("""
            Module M
             Sub Main()
              Dim B(2) As Byte
              For I As Integer = 0 To 1
               Dim S = Convert.ToBase64String(B)
               Console.WriteLine(S)
              Next
             End Sub
            End Module
            """, optimize: true);

        Assert.That(looped, Does.Match(
                @"\{\s*BasicLang::NetRef blnet_t\d+ = BasicLang::net::"
                + form.NewExportName + @"\(B\);"),
            "the same region must be braced inside a loop body — and per-call scoping is also "
            + "what lets the guard release at the end of each ITERATION instead of accumulating "
            + "one live handle per iteration until the function returns:\n" + looped);
    }

    /// <summary>
    /// ROW 4 — a <c>ref</c>/<c>out</c> array slot copies in AND reads back. §8.6 makes this row
    /// the EXEMPTION from §14.11, so the write-back is the feature, not an optimisation.
    /// </summary>
    [Test]
    public void Row4_RefArraySlot_CopiesInAndReadsBack()
    {
        var resolver = RequireProbe();
        var form = Form("System.Int32");

        var cpp = CompileToCpp("""
            Using Aot.Probe

            Module M
             Sub Main()
              Dim A(2) As Integer
              Bag.Fill(A)
              Console.WriteLine(A(0))
             End Sub
            End Module
            """, resolver);

        Assert.That(cpp, Does.Match(
                @"BasicLang::NetRef (blnet_t\d+) = BasicLang::net::" + form.NewExportName + @"\(A\);"),
            "a ref array slot still COPIES IN — the callee may read the incoming array:\n" + cpp);
        Assert.That(cpp, Does.Match(
                @"A = BasicLang::net::" + form.ReadExportName + @"\(blnet_t\d+\);"),
            "§8.6's ref/out row is EXEMPT from §14.11's one-way rule: the slot must be read back "
            + "into the native vector after the call. Without this line §12.1's parity program "
            + "has two different correct expected outputs and cannot be authored:\n" + cpp);
    }

    /// <summary>
    /// The write-back is emitted AFTER the call statement, not before it — the ordering the
    /// Step-0 seam makes structural. Asserted as an ordering rather than as two independent
    /// `Contains`, because both lines being present in the wrong order is a real, silent
    /// failure: the caller would read back the array it just sent.
    /// </summary>
    [Test]
    public void Row4_TheWriteBackFollowsTheCall()
    {
        var resolver = RequireProbe();
        var form = Form("System.Int32");
        var cpp = CompileToCpp("""
            Using Aot.Probe

            Module M
             Sub Main()
              Dim A(2) As Integer
              Bag.Fill(A)
              Console.WriteLine(A(0))
             End Sub
            End Module
            """, resolver);

        var copyIn = cpp.IndexOf(form.NewExportName, StringComparison.Ordinal);
        var call = cpp.IndexOf("Bag_Fill", StringComparison.Ordinal);
        var readBack = cpp.IndexOf(form.ReadExportName, StringComparison.Ordinal);

        Assert.That(call, Is.GreaterThan(copyIn),
            "the copy-in is a PROLOGUE statement and must precede the call:\n" + cpp);
        Assert.That(readBack, Is.GreaterThan(call),
            "the readback is a WRITE-BACK statement and must follow the call — emitted before "
            + "it, the program would read the array back before the callee ever saw it:\n" + cpp);
    }

    // ====================================================================================
    // 2. The v1 limit — one BL6019 per §8.6 refusal row, each naming the offending type.
    // ====================================================================================

    [Test]
    public void Bl6019_NativeListOutbound_NamesTheCollectionAndTheParameter()
    {
        var resolver = RequireProbe();
        var analyzer = Analyze("""
            Using Aot.Probe

            Module M
             Sub Main()
              Dim L As New List(Of Integer)
              L.Add(1)
              Console.WriteLine(Bag.Count(L))
             End Sub
            End Module
            """, resolver);

        var findings = Findings(analyzer, "BL6019").ToList();
        Assert.That(findings, Is.Not.Empty,
            "a native BasicLang List crossing outbound has no handle and no §8.6 v1 copy — it "
            + "must be a POSITIONED BL6019, not the generator's positionless refusal. Findings: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(findings.Single(), Does.Contain("List").And.Contain("Bag.Count"),
            "the message must name the offending TYPE and the member whose parameter it could "
            + "not fill (spec §8.6): " + findings.Single());
    }

    [Test]
    public void Bl6019_NativeArrayAgainstANonArrayParameter_SaysWhatWouldWork()
    {
        var resolver = RequireProbe();
        var analyzer = Analyze("""
            Using Aot.Probe

            Module M
             Sub Main()
              Dim A(2) As Integer
              Console.WriteLine(Bag.Count(A))
             End Sub
            End Module
            """, resolver);

        var findings = Findings(analyzer, "BL6019").ToList();
        Assert.That(findings, Is.Not.Empty,
            "a native array has no handle, so an IEnumerable(Of Integer) slot has nothing to "
            + "receive — §8.6 copies in only against a matching T[] parameter. Findings: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(findings.Single(), Does.Contain("IEnumerable").And.Contain("T[] parameter"),
            "the message must name the parameter type it could not fill AND teach the fix: "
            + findings.Single());
    }

    /// <summary>
    /// §8.6's NESTED element row — <c>List(Of List(Of Integer))</c> and jagged arrays — pinned at
    /// the seam that decides it, because no BasicLang call site can reach the analyzer's gate
    /// with one today.
    ///
    /// <para><b>Why not a program.</b> Measured, not assumed: a native
    /// <c>Dim Rows(1) As List(Of Integer)</c> types as <c>List[]</c> whose ELEMENT has no generic
    /// arguments at all — <c>SemanticAnalyzer.ResolveTypeReference</c>'s array arm calls
    /// <c>ResolveTypeName(typeRef.Name)</c> and drops <c>typeRef.GenericArguments</c>. The
    /// argument is then untypeable and draws BL6017 ("no .NET type the analyzer can present")
    /// long before §8.6 is consulted. That is a PRE-EXISTING analyzer gap about arrays of
    /// generics, wholly unrelated to this task, and writing a §8.6 test that passed because of
    /// it would be a test that proves nothing and breaks when the gap is fixed.</para>
    ///
    /// <para>What IS guaranteed here is the property that matters: a nested spelling has no copy
    /// form at any level, so no helper is ever minted for one and no call site can silently copy
    /// it. The reachable half of the same rule is
    /// <see cref="Bl6019_HandleElementType_IsRefusedEvenThoughTheElementItselfCanCross"/>.</para>
    /// </summary>
    [Test]
    public void NestedElementTypes_HaveNoCopyFormAndMintNoHelper()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NetArrayCopy.TryGetFormForArray("System.Int32[][]", out _), Is.False,
                "a jagged array is not a §8.6 v1 element.");
            Assert.That(
                NetArrayCopy.TryGetFormForArray(
                    "System.Collections.Generic.List<System.Int32>[]", out _), Is.False,
                "the spec's own List(Of List(Of Integer)) shape is not a §8.6 v1 element.");
            Assert.That(NetArrayCopy.TryGetFormForArray("System.Int32[]", out _), Is.True,
                "control: the admitted row must still answer true, or the three assertions "
                + "above pass for the trivial reason that nothing does.");
        });

        var nested = new NetSurface(
            new[]
            {
                new NetMemberDescriptor(
                    "CountLists", BagFullName, NetMemberCategory.Method,
                    isStatic: true, arity: 0, "System.Int32",
                    new[]
                    {
                        new NetParameterDescriptor(
                            NetRefKind.None, "System.Collections.Generic.List<System.Int32>[]"),
                    }),
            },
            Array.Empty<string>());

        Assert.That(NetArrayCopy.RequiredForms(nested), Is.Empty,
            "a nested element type must mint NO copy helper — the absence of the export is what "
            + "makes a silent copy impossible rather than merely unlikely.");
    }

    /// <summary>
    /// An element type that is itself a .NET HANDLE — <c>Regex()</c> against a
    /// <c>Regex[]</c> parameter. Distinct from the nested row: the element is perfectly
    /// marshalable ON ITS OWN (§8.3's handle row), and it is only the PER-ELEMENT copy §8.6 has
    /// no wire form for.
    /// </summary>
    [Test]
    public void Bl6019_HandleElementType_IsRefusedEvenThoughTheElementItselfCanCross()
    {
        var resolver = RequireProbe();
        var analyzer = Analyze("""
            Using Aot.Probe
            Using System.Text.RegularExpressions

            Module M
             Sub Main()
              Dim Rx(1) As Regex
              Console.WriteLine(Bag.CountRx(Rx))
             End Sub
            End Module
            """, resolver);

        var findings = Findings(analyzer, "BL6019").ToList();
        Assert.That(findings, Is.Not.Empty,
            "an array whose ELEMENTS are handles has no per-element wire form — §8.6 copies "
            + "values, and a handle is not one. Findings: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(findings.Single(),
            Does.Contain("Regex[]").And.Contain("themselves .NET handles"),
            "the message must name the parameter type and say WHY its element cannot cross — a "
            + "Regex crosses fine on its own, which is the confusing part: " + findings.Single());
    }

    /// <summary>
    /// A MISMATCHED element type never reaches §8.6's gate at all, and that is the right answer:
    /// C# has no implicit conversion from <c>int[]</c> to <c>byte[]</c>, so overload resolution
    /// refuses first with BL6017. Pinned as a NEGATIVE — the §8.6 mismatch arm is
    /// defence-in-depth behind the generator, and claiming an analyzer test for it would be
    /// claiming a path that does not exist.
    /// </summary>
    [Test]
    public void MismatchedElementType_IsCaughtByOverloadResolutionBeforeSection86()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim A(2) As Integer
              Dim S = Convert.ToBase64String(A)
              Console.WriteLine(S)
             End Sub
            End Module
            """, SharedResolver.Value);

        Assert.That(Findings(analyzer, "BL6017"), Is.Not.Empty,
            "an Integer() against Convert.ToBase64String's Byte[] slot has no matching overload "
            + "— the earlier, better diagnostic. Findings: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(Findings(analyzer, "BL6019"), Is.Empty,
            "§8.6 must NOT also fire here: two findings for one mistake is noise, and the "
            + "overload answer is the one that names the real problem.");
    }

    /// <summary>
    /// The v1 limit is a LIMIT, not a blanket refusal: the admitted row still resolves clean.
    /// Without this, every BL6019 test above would also pass with a lowering that refused every
    /// array.
    /// </summary>
    [Test]
    public void TheAdmittedRow_DrawsNoFindingAtAll()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim B(2) As Byte
              Dim S = Convert.ToBase64String(B)
              Console.WriteLine(S)
             End Sub
            End Module
            """, SharedResolver.Value);

        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "a Byte() against a Byte[] slot is §8.6's v1 admitted row and must draw NOTHING: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
    }

    // ====================================================================================
    // 3. The emitted halves — §12.4 and the wire shapes.
    // ====================================================================================

    /// <summary>
    /// §12.4, extended to §8.6: the copy helpers are ORDINARY proxy-table slots and ordinary shim
    /// exports, so the two name sets stay equal. Made a side channel instead, they would have
    /// been exempt from the ONE invariant holding the two halves of this boundary together.
    /// </summary>
    [Test]
    public void ArrayCopyHelpers_AreOrdinarySlotsAndExports_SoSection124StillHolds()
    {
        var surface = ArraySurface();

        var slots = NetProxyEmitter.EmitBindings(surface).SlotNames;
        var exports = NetShimGenerator.SurfaceDerivedExportNames(surface);

        Assert.That(slots, Is.EquivalentTo(exports),
            "§12.4: every proxy-table slot must have an identically-named shim export and vice "
            + "versa. Slots: " + string.Join(", ", slots) + " | Exports: "
            + string.Join(", ", exports));

        var form = Form("System.Int32");
        Assert.That(slots, Does.Contain(form.NewExportName).And.Contain(form.ReadExportName),
            "the §8.6 pair for the surface's Int32[] parameter must actually be in the set — an "
            + "empty-vs-empty comparison above would pass vacuously.");
    }

    /// <summary>
    /// The helper set is derived from the SIGNATURES in the surface, so a surface with no array
    /// anywhere emits no helpers at all — the same used-only discipline §7.1 applies to members.
    /// </summary>
    [Test]
    public void ASurfaceWithNoArray_EmitsNoCopyHelpers()
    {
        var surface = new NetSurface(
            new[]
            {
                new NetMemberDescriptor(
                    "Twice", "Aot.Probe.Widget", NetMemberCategory.Method,
                    isStatic: false, arity: 0, "System.Int32",
                    Array.Empty<NetParameterDescriptor>()),
            },
            Array.Empty<string>());

        Assert.That(NetArrayCopy.RequiredForms(surface), Is.Empty);
        Assert.That(NetProxyEmitter.EmitBindings(surface).Text,
            Does.Not.Contain("bl_net_array_new_"),
            "a surface with no array in any signature must not pay for §8.6 at all.");
    }

    /// <summary>
    /// The C and C# halves of a copy pair must agree on the element WIDTH. They are separate
    /// code — <c>NetProxyEmitter</c> writes the C slot, <c>NetShimGenerator</c> the C# export —
    /// and a mismatch is not a compile error anywhere: it is a silent buffer misread across the
    /// ABI, which is the one failure mode this boundary cannot afford. Pinned row by row.
    /// </summary>
    [Test]
    public void EveryCopyFormsWireWidthAgreesAcrossTheTwoEmitters()
    {
        var widths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["int8_t"] = "sbyte", ["uint8_t"] = "byte",
            ["int16_t"] = "short", ["uint16_t"] = "ushort",
            ["int32_t"] = "int", ["uint32_t"] = "uint",
            ["int64_t"] = "long", ["uint64_t"] = "ulong",
            ["float"] = "float", ["double"] = "double",
            ["const char*"] = "byte*", ["char*"] = "byte*",
        };

        Assert.Multiple(() =>
        {
            foreach (var form in NetArrayCopy.Forms)
            {
                // A generated header must not warn. Prefixing `const` onto a row whose wire
                // element is ALREADY a pointer produces `const const char**` — a duplicate
                // cv-qualifier that MSVC reports as C4114 and clang/gcc flag too, out of a header
                // the user cannot edit and fatal under -Werror.
                Assert.That(form.CSourcePointer, Does.Not.Contain("const const"),
                    $"§8.6 form '{form.Suffix}' emits a duplicate const in its copy-in buffer "
                    + $"parameter: '{form.CSourcePointer}'.");
                Assert.That(widths.ContainsKey(form.CWireIn), Is.True,
                    $"§8.6 form '{form.Suffix}' declares an inbound wire element "
                    + $"'{form.CWireIn}' with no known C# counterpart — NetShimGenerator.CsPointer "
                    + "would throw at emission time. Add the row to BOTH.");
                Assert.That(widths.ContainsKey(form.CWireOut), Is.True,
                    $"§8.6 form '{form.Suffix}' declares an outbound wire element "
                    + $"'{form.CWireOut}' with no known C# counterpart.");
            }
        });
    }

    /// <summary>
    /// The emitted C# for a copy pair must actually compile against the reference pack — the
    /// cheapest possible proof that the format strings in <see cref="NetArrayCopy.Forms"/>
    /// produce valid C#. Every row is exercised, including the three whose per-element
    /// conversions differ (Boolean, Char, String).
    /// </summary>
    [Test]
    public void EveryCopyFormsEmittedShimCompiles()
    {
        var members = NetArrayCopy.Forms.Select((f, i) => new NetMemberDescriptor(
            "Take" + i, SinkFullName, NetMemberCategory.Method,
            isStatic: true, arity: 0, "System.Void",
            new[] { new NetParameterDescriptor(NetRefKind.None, f.ArrayFullName) })).ToList();

        var exports = NetShimGenerator.EmitExports(new NetSurface(members, Array.Empty<string>()));

        Assert.Multiple(() =>
        {
            foreach (var form in NetArrayCopy.Forms)
            {
                Assert.That(exports, Does.Contain(form.NewExportName),
                    $"§8.6 form '{form.Suffix}' contributed no copy-IN export.");
                Assert.That(exports, Does.Contain(form.ReadExportName),
                    $"§8.6 form '{form.Suffix}' contributed no readback export.");
            }
        });

        NetStubHarness.AssertShimCompiles(new[] { SinkAssembly.Value }, exports,
            "the generated §8.6 copy helpers must be valid C#. A format string in "
            + "NetArrayCopy.Forms that produces uncompilable text would otherwise surface only "
            + "at the end of a ~27 s AOT publish.");
    }

    /// <summary>
    /// §8.6 row 4's wire shape, on the native side: a ByRef HANDLE parameter must be a MUTABLE
    /// <c>NetRef&amp;</c>. <c>CppParamType + "&amp;"</c> would be <c>const NetRef&amp; &amp;</c>,
    /// which reference-collapses back to <c>const NetRef&amp;</c> — the parameter would silently
    /// stay read-only and the write-back would not compile.
    /// </summary>
    [Test]
    public void RefArraySlot_EmitsAMutableNetRefParameter_NotAConstOne()
    {
        var proxies = NetProxyEmitter.Emit(RefArraySurface(), "Stub.dll")[
            NetProxyEmitter.ProxiesFileName];

        Assert.That(proxies, Does.Contain("BasicLang::blnet::NetRef& a0"),
            "§8.6's ref array slot needs a mutable NetRef& — a const one cannot be written "
            + "back into:\n" + proxies);
        Assert.That(proxies, Does.Contain("a0 = BasicLang::blnet::NetRef(blnet_a0);"),
            "the proxy must adopt whatever handle the callee left in the slot. Assigning a fresh "
            + "NetRef releases the handle this frame minted, which is exactly why the ownership "
            + "objection §8.3 raises for a general ByRef handle does not apply here:\n" + proxies);
    }

    /// <summary>
    /// §8.6 row 4's wire shape, managed side: the decoded handle is an <c>object?</c>, and
    /// <c>ref object</c> does not bind to a <c>ref T[]</c> parameter (CS1503). The shim must
    /// therefore carry a SECOND, typed local — and the pointer slot must be read THROUGH.
    /// </summary>
    [Test]
    public void RefArraySlot_ShimDecodesThroughThePointerAndPassesATypedLocal()
    {
        var exports = NetShimGenerator.EmitExports(RefArraySurface());

        Assert.That(exports, Does.Contain("if ((*a0) != 0)"),
            "a ByRef handle slot arrives as a POINTER; the §8.2 guarded decode must read through "
            + "it rather than comparing the pointer itself to 0:\n" + exports);
        Assert.That(exports, Does.Contain("global::System.Int32[] a0__ = (global::System.Int32[])a0_!;"),
            "the typed local is not tidiness — `ref object` does not bind to `ref int[]`:\n" + exports);
        Assert.That(exports, Does.Contain("ref a0__"), exports);
        Assert.That(exports, Does.Contain("*a0 = ToHandle(a0__);"),
            "the callee's (possibly replaced) array must be handed back through the slot:\n" + exports);

        NetStubHarness.AssertShimCompiles(new[] { SinkAssembly.Value }, exports,
            "the generated ref-array wrapper must be valid C#.");
    }

    /// <summary>
    /// An <c>out</c> array slot must NOT read the caller's value — .NET ignores it, and the
    /// generated shim assigns before the call. Same rule §8.3's scalar <c>out</c> row already
    /// follows; asserted here because the handle row takes a different code path to get there.
    /// </summary>
    [Test]
    public void OutArraySlot_DoesNotReadTheCallersValue()
    {
        var exports = NetShimGenerator.EmitExports(RefArraySurface(NetRefKind.Out));

        Assert.That(exports, Does.Contain("global::System.Int32[] a0__ = default!;"),
            "an `out` parameter's incoming value is ignored by the callee, so the shim must not "
            + "cast the decoded handle into it:\n" + exports);
        Assert.That(exports, Does.Contain("out a0__"), exports);

        NetStubHarness.AssertShimCompiles(new[] { SinkAssembly.Value }, exports,
            "the generated out-array wrapper must be valid C#.");
    }

    /// <summary>
    /// The whole four-row table again through the OPTIMIZER and the SPLIT emitter — the shape
    /// <c>CppProjectBuilder</c> drives, and therefore the shape the CLI and the IDE both drive.
    /// The repo law is that codegen is validated through the optimizer and not only through the
    /// non-optimizing helper; this fixture would otherwise test one emission mode out of two.
    ///
    /// <para>It does NOT reproduce the temp-fusion hazard <c>EffectiveCppType</c> answers —
    /// measured: both emission modes keep the NetRef temp here, and only the full project build
    /// fuses it. That case is pinned by
    /// <c>NetShimPipelineTests.Section86_ByValueCopyIsOneWay_ButARefSlotReadsBack</c>, and the
    /// gap is recorded rather than papered over.</para>
    /// </summary>
    [Test]
    public void TheFourRowsSurviveTheOptimizerAndTheSplitEmitter()
    {
        var resolver = RequireProbe();
        var int32 = Form("System.Int32");
        var str = Form("System.String");

        const string program = """
            Using Aot.Probe

            Module M
             Sub Main()
              Dim A() As Integer = {1, 2, 3}
              Console.WriteLine(Bag.Sum(A))
              Bag.Fill(A)
              Dim V() As Integer = Bag.Values()
              Console.WriteLine(Bag.Sum(V))
              Dim N() As String = Bag.Names()
              Console.WriteLine(Bag.Join(N))
             End Sub
            End Module
            """;

        foreach (var (label, cpp) in new[]
        {
            ("optimized combined", CompileToCpp(program, resolver, optimize: true)),
            ("optimized split", SplitCpp(program, resolver)),
        })
        {
            Assert.Multiple(() =>
            {
                Assert.That(cpp, Does.Match(
                        @"BasicLang::NetRef blnet_t\d+ = BasicLang::net::" + int32.NewExportName + @"\(A\);"),
                    label + ": row 3's copy-in guard:\n" + cpp);
                Assert.That(cpp, Does.Match(
                        @"A = BasicLang::net::" + int32.ReadExportName + @"\(blnet_t\d+\);"),
                    label + ": row 4's write-back:\n" + cpp);
                Assert.That(cpp, Does.Contain("BasicLang::net::" + int32.ReadExportName + "("),
                    label + ": row 2's Integer materialization:\n" + cpp);
                Assert.That(cpp, Does.Contain("BasicLang::net::" + str.ReadExportName + "("),
                    label + ": row 2's String materialization:\n" + cpp);
                Assert.That(cpp, Does.Contain("BasicLang::net::" + str.NewExportName + "("),
                    label + ": row 3's String copy-in:\n" + cpp);
            });
        }
    }

    // ------------------------------------------------------------------------------------
    // Fixture surfaces
    // ------------------------------------------------------------------------------------

    private static NetSurface ArraySurface() => new(
        new[]
        {
            new NetMemberDescriptor(
                "Sum", BagFullName, NetMemberCategory.Method,
                isStatic: true, arity: 0, "System.Int32",
                new[] { new NetParameterDescriptor(NetRefKind.None, "System.Int32[]") }),
        },
        Array.Empty<string>());

    private static NetSurface RefArraySurface(NetRefKind refKind = NetRefKind.Ref) => new(
        new[]
        {
            new NetMemberDescriptor(
                refKind == NetRefKind.Out ? "Emit" : "Fill",
                SinkFullName, NetMemberCategory.Method,
                isStatic: true, arity: 0, "System.Void",
                new[] { new NetParameterDescriptor(refKind, "System.Int32[]") }),
        },
        Array.Empty<string>());
}
