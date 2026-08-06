using System;
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
/// P2a-2 Task 8c — the §6.4 conversion rows at a RESOLVED CALL SITE.
///
/// <para><b>What this fixture exists to prevent.</b> Before Task 8c, four rows
/// (Decimal, Guid, DateTimeOffset, StringBuilder) shared one <c>IsMultiSlot</c> flag that the
/// lowering read as "refuse". <c>NetMarshalTable</c> carried converter NAMES for all four and
/// <c>NetConversionPairTests</c> proved the converters themselves correct in both directions —
/// so every signal short of driving a program said the feature was wired. Nothing called them.
/// These tests drive real BasicLang through parse → analyze → lower, which is the only level at
/// which "does this row cross the boundary" has an answer.</para>
///
/// <para><b>Why the split is 2-and-2 and not 4.</b> The flag was covering three unrelated facts.
/// Guid needs ONE slot whose C type differs by direction; StringBuilder needs one slot in one
/// DIRECTION; Decimal (4) and DateTimeOffset (2) need more slots than a proxy parameter has.
/// Only the last is a structural limit, so only the last still refuses.</para>
/// </summary>
[TestFixture]
public class NetConversionRowLoweringTests
{
    /// <summary>
    /// A probe assembly rather than the shared framework resolver: the BCL has no convenient
    /// STATIC member taking a Guid or a StringBuilder by value, and an instance member would put
    /// a §6.4 value in the RECEIVER position — a different path, with a different refusal.
    /// </summary>
    private const string ProbeSource = @"
namespace ConvProbe {
    public static class Rows {
        public static int Gid(System.Guid g) => 1;
        public static int Sb(System.Text.StringBuilder b) => b.Length;
        public static int Dec(decimal d) => 1;
        public static int Dto(System.DateTimeOffset d) => 1;
        public static System.Guid MakeGid() => System.Guid.Empty;
        public static System.Text.StringBuilder MakeSb() => null;
        public static int Both(System.Guid g, System.Text.StringBuilder b) => 1;
    }
}";

    private static ProbeAssembly _probe;
    private static Lazy<NetTypeResolver> _resolver;

    [OneTimeSetUp]
    public void SetUp()
    {
        _probe = new ProbeAssembly("BlnetConvRowProbe", ProbeSource);
        _resolver = new Lazy<NetTypeResolver>(() => NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { _probe.Path })));
    }

    [OneTimeTearDown]
    public void TearDown() => _probe?.Dispose();

    private const string Preamble = "Using ConvProbe\n\nModule M\n Sub Main()\n  ";
    private const string Postamble = "\n End Sub\nEnd Module";

    private static (SemanticAnalyzer Analyzer, IRModule Module) BuildIR(string body)
    {
        var parser = new Parser(new Lexer(Preamble + body + Postamble).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => _resolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        return (analyzer, new IRBuilder(analyzer).Build(ast, "TestModule"));
    }

    /// <summary>Lowers, asserting the analyzer raised no §6.4 refusal on the way.</summary>
    private static string Lower(string body)
    {
        var (analyzer, module) = BuildIR(body);
        Assert.That(analyzer.NetDiagnostics.Where(d => !d.IsWarning), Is.Empty,
            "unexpected analyzer refusal: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
    }

    /// <summary>
    /// The refusal a row produces, from EITHER layer. The analyzer refuses positionally where it
    /// can (it knows the parameter's type from the descriptor); the generator is the
    /// defense-in-depth layer for the shapes only lowering can see. A test that looked at one
    /// layer would pass while the other silently changed.
    /// </summary>
    private static string RefusalFor(string body)
    {
        SemanticAnalyzer analyzer;
        IRModule module;
        try
        {
            (analyzer, module) = BuildIR(body);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var refusal = analyzer.NetDiagnostics.FirstOrDefault(d => !d.IsWarning);
        if (refusal != null) return refusal.Message;

        try
        {
            new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                .Generate(module);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return null;
    }

    // ====================================================================================
    // The two rows Task 8c lowers
    // ====================================================================================

    /// <summary>
    /// Guid crosses as 16 borrowed bytes. The buffer must be a PROLOGUE declaration, not an
    /// inline expression, because <c>to_net_guid</c> returns <c>void</c> and fills its second
    /// argument — there is no expression to write.
    /// </summary>
    [Test]
    public void GuidArgument_DeclaresTheBufferThenFillsIt()
    {
        var cpp = Lower("Dim g As Guid = Guid.Empty\n  Dim r = Rows.Gid(g)");

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Match(@"std::uint8_t\s+_bl_net_arg0\[16\]\s*;"),
                "the 16-byte buffer is declared as a statement before the call:\n" + cpp);
            Assert.That(cpp, Does.Match(@"BasicLang::net::to_net_guid\(\s*g\s*,\s*_bl_net_arg0\s*\)"),
                "to_net_guid writes THROUGH the buffer — a Guid is a VALUE on this backend and "
                + "must not be dereferenced:\n" + cpp);
            Assert.That(cpp, Does.Match(@"Gid[A-Za-z0-9_]*\(\s*_bl_net_arg0\s*\)"),
                "and the buffer itself is what crosses the slot:\n" + cpp);
        });
    }

    /// <summary>
    /// ⛔ The lifetime test, and the reason StringBuilder cannot be an expression.
    /// <c>to_net_stringbuilder</c> returns an OWNING <c>std::string</c> BY VALUE. The tempting
    /// one-liner <c>to_net_stringbuilder(*b).c_str()</c> compiles, passes a smoke test, and is a
    /// use-after-free: the temporary dies at the end of its full-expression while the callee
    /// still holds the pointer. The named temporary is what makes the borrow outlive the call.
    /// </summary>
    [Test]
    public void StringBuilderArgument_BindsAnOwningTemporaryThatOutlivesTheCall()
    {
        var cpp = Lower("Dim b As New StringBuilder()\n  Dim r = Rows.Sb(b)");

        Assert.Multiple(() =>
        {
            Assert.That(cpp,
                Does.Match(@"std::string\s+_bl_net_arg0\s*=\s*BasicLang::net::to_net_stringbuilder\(\s*\*b\s*\)\s*;"),
                "a NAMED temporary holds the converted string. §8.5's two-layer std makes a "
                + "BasicLang StringBuilder a shared_ptr, and the converter takes a reference, "
                + "so the argument is DEREFERENCED:\n" + cpp);
            Assert.That(cpp, Does.Match(@"Sb[A-Za-z0-9_]*\(\s*_bl_net_arg0\.c_str\(\)\s*\)"),
                "and .c_str() is taken from the NAMED temporary, never from the call:\n" + cpp);
            Assert.That(cpp, Does.Not.Match(@"to_net_stringbuilder\([^;]*\)\.c_str\(\)"),
                "the dangling one-liner must never be emitted:\n" + cpp);
        });
    }

    /// <summary>
    /// Two conversion arguments in ONE call. The temporaries are keyed by parameter index
    /// precisely so they cannot collide — a single shared name would have the second converter
    /// overwrite the first argument's buffer before the call ran.
    /// </summary>
    [Test]
    public void TwoConversionArguments_GetIndependentTemporaries()
    {
        var cpp = Lower(
            "Dim g As Guid = Guid.Empty\n  Dim b As New StringBuilder()\n"
            + "  Dim r = Rows.Both(g, b)");

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Match(@"std::uint8_t\s+_bl_net_arg0\[16\]"),
                "parameter 0's buffer:\n" + cpp);
            Assert.That(cpp, Does.Match(@"std::string\s+_bl_net_arg1\s*="),
                "parameter 1's temporary is a DIFFERENT name:\n" + cpp);
            Assert.That(cpp, Does.Match(@"Both[A-Za-z0-9_]*\(\s*_bl_net_arg0\s*,\s*_bl_net_arg1\.c_str\(\)\s*\)"),
                "and both cross in declaration order:\n" + cpp);
        });
    }

    // ====================================================================================
    // The rows that still refuse — each for its OWN reason, stated in its own message
    // ====================================================================================

    [Test]
    public void DecimalArgument_StillRefuses_NamingItsSlotCount()
    {
        var refusal = RefusalFor("Dim d As Decimal = 1\n  Dim r = Rows.Dec(d)");

        Assert.That(refusal, Is.Not.Null, "a Decimal argument must not lower");
        Assert.That(refusal, Does.Contain("4"),
            "the refusal names how many slots the row needs — the count is the reason, and "
            + "'not a single slot' said nothing about which rows were close to working: "
            + refusal);
    }

    [Test]
    public void DateTimeOffsetArgument_StillRefuses_NamingItsSlotCount()
    {
        var refusal = RefusalFor("Dim t As DateTimeOffset = DateTimeOffset.Now\n  Dim r = Rows.Dto(t)");

        Assert.That(refusal, Is.Not.Null, "a DateTimeOffset argument must not lower");
        Assert.That(refusal, Does.Contain("2"), "…and names its two slots: " + refusal);
    }

    /// <summary>
    /// The RESULT direction of a row whose converter fills a caller-owned buffer. A proxy has
    /// ONE result out-pointer, so the buffer would have to become an extra argument — the same
    /// structural limit the multi-slot rows hit. The ARGUMENT direction of this very row lowers,
    /// which is why the message says so.
    /// </summary>
    [Test]
    public void GuidResult_StillRefuses_EvenThoughTheArgumentDirectionLowers()
    {
        var refusal = RefusalFor("Dim g = Rows.MakeGid()");

        Assert.That(refusal, Is.Not.Null, "a Guid RESULT must not lower");
        Assert.That(refusal, Does.Contain("ARGUMENT"),
            "the refusal must point at the direction that DOES work, or it reads as "
            + "'Guid is unsupported' when half of it is not: " + refusal);
    }

    /// <summary>
    /// ⛔ The regression this fixture was most needed for. StringBuilder's result direction used
    /// to be refused as a SIDE EFFECT of the row being flagged multi-slot. Once the row lowered
    /// outbound, the only thing between a StringBuilder result and <c>dest = (expr);</c> — an
    /// assignment with no conversion at all — was the deliberately-null inbound converter, which
    /// nothing had ever been asked to check.
    /// </summary>
    [Test]
    public void StringBuilderResult_RefusesOnItsOwnDirectionality_NotOnSlotCount()
    {
        var refusal = RefusalFor("Dim b = Rows.MakeSb()");

        Assert.That(refusal, Is.Not.Null,
            "§6.4 gives StringBuilder ONE direction; a result must refuse rather than emit an "
            + "unconverted assignment");
        Assert.That(refusal, Does.Not.Contain("slots"),
            "and it must refuse for the RIGHT reason — slot count is not why: " + refusal);

        // ⛔ Pins the DIRECTIONALITY guard specifically, on wording only it produces.
        // MEASURED: StringBuilder is also the OwningTemp converter form, so the buffer-shape
        // guard further down catches this row too — and its message likewise contains the word
        // "direction". Asserting on that word therefore proved nothing: disabling the
        // null-converter check left all nine tests green. "to-net" appears in one message only,
        // which is what makes this line a guard rather than a restatement.
        Assert.That(refusal, Does.Contain("to-net"),
            "the refusal must name the reason unique to this row — §6.4 defines it in ONE "
            + "direction — rather than the buffer-shape reason it happens to share: " + refusal);
    }

    // ====================================================================================
    // The table invariant the split rests on
    // ====================================================================================

    /// <summary>
    /// <c>HasByValueScalarSlot</c> replaced <c>IsMultiSlot || CWire is null</c> as §8.3's ByRef
    /// gate. That rewrite is only safe if it answers the same for every row — and the two rows that
    /// changed are exactly the ones that gained a CWire, whose slot is a POINTER. A ByRef one
    /// would be a pointer-to-pointer with no specified ownership.
    /// </summary>
    [Test]
    public void OnlyByValueScalarRows_AdmitAByRefSlot()
    {
        Assert.Multiple(() =>
        {
            foreach (var row in NetMarshalTable.WireRows.Values)
            {
                var pointerShaped = row.CWire != null && row.CWire.EndsWith("*", StringComparison.Ordinal);
                Assert.That(row.HasByValueScalarSlot,
                    Is.EqualTo(row.CWire != null && !pointerShaped),
                    $"'{row.NetFullName}': a row admits a ByRef slot exactly when it has ONE "
                    + "by-value scalar slot. Guid and StringBuilder have a CWire now but it is "
                    + "a pointer, so they must stay refused.");
            }

            Assert.That(NetMarshalTable.WireRows["System.Guid"].HasByValueScalarSlot, Is.False,
                "the row this rewrite could most easily have let through");
            Assert.That(
                NetMarshalTable.WireRows["System.Text.StringBuilder"].HasByValueScalarSlot,
                Is.False, "and the other one");
        });
    }

    /// <summary>
    /// The slot count now says only what it means. Decimal and DateTimeOffset are the ONLY rows
    /// needing more than one slot; a fifth appearing here means someone flagged a row to make it
    /// refuse, which is the conflation Task 8c undid.
    /// </summary>
    [Test]
    public void ExactlyTwoRowsAreMultiSlot()
    {
        Assert.That(NetMarshalTable.MultiSlotConversionPairs,
            Is.EquivalentTo(new[] { "System.Decimal", "System.DateTimeOffset" }));
    }

    // ====================================================================================
    // The EMITTER positions, which a call-site refusal does not reach
    // ====================================================================================

    private static NetMemberDescriptor Returning(string netType) => new(
        "Get", "MyLib.Holder", NetMemberCategory.Method, true, 0, netType,
        Array.Empty<NetParameterDescriptor>());

    private static NetSurface SurfaceReturning(string netType) =>
        new(new[] { Returning(netType) }, new[] { "MyLib.Holder" });

    /// <summary>
    /// ⛔ REGRESSION GUARD for the hole Task 8c-1 opened.
    ///
    /// <para>Giving Guid and StringBuilder a by-value wire form made them
    /// <c>WireKind.Scalar</c> in both emitters. The call site refuses them in the RESULT
    /// position — but a call site is not the only way a member reaches an emitter. A
    /// <c>&lt;NetProxy&gt;</c> DECLARED TYPE projects every member it has, called or not, so a
    /// Guid-returning member walks straight into <c>PlanMember</c>.</para>
    ///
    /// <para>Before Task 8c-1 these rows were <c>Wire.Handle</c> and the result position was
    /// sound (<c>*result = ToHandle(rv_)</c>). After it, the shim's Scalar arm emits
    /// <c>*result = rv_;</c> — a <c>System.Guid</c> assigned to a <c>byte*</c> — because
    /// <c>ToWire</c> has no arm for the row. That is ill-typed GENERATED C# with no BasicLang
    /// diagnostic attached: the user sees a compiler error inside a file they never wrote.</para>
    ///
    /// <para>An emitter must refuse loudly at PLAN time instead. The refusal is the contract;
    /// emitting something that happens not to compile is not.</para>
    /// </summary>
    [TestCase("System.Guid")]
    [TestCase("System.Text.StringBuilder")]
    public void AByValuePointerRow_InTheRESULTPosition_RefusesAtPlanTime(string netType)
    {
        var surface = SurfaceReturning(netType);

        var proxy = Assert.Throws<NotSupportedException>(
            () => NetProxyEmitter.EmitBindings(surface),
            "the C emitter must refuse a §6.4 pointer row in the result position rather than "
            + "emit a slot whose out-pointer it has no conversion for");

        var shim = Assert.Throws<NotSupportedException>(
            () => NetShimGenerator.Emit(surface, "Shim"),
            "and the C# emitter must refuse it too — this is the side that would otherwise "
            + "emit `*result = rv_;` and fail to compile inside generated source");

        Assert.Multiple(() =>
        {
            Assert.That(proxy.Message, Does.Contain(netType));
            Assert.That(shim.Message, Does.Contain(netType));
        });
    }

    /// <summary>
    /// The same widening reached §8.4's delegate gate. <c>RequireBlittableScalar</c> admits on
    /// <c>Kind == WireKind.Scalar</c> ALONE — no table consult — so making these rows Scalar
    /// silently opened a gate whose entire job is to stay shut until §8.4 specifies a
    /// marshaling contract for non-scalars.
    /// </summary>
    [TestCase("System.Guid")]
    [TestCase("System.Text.StringBuilder")]
    public void AByValuePointerRow_IsStillRejectedByTheDelegateGate(string netType)
    {
        // A §8.4 delegate PARAMETER whose invoke signature names the row — the shape that
        // reaches RequireBlittableScalar through the real entry point.
        var member = new NetMemberDescriptor(
            "Run", "MyLib.Holder", NetMemberCategory.Method, true, 0, "System.Void",
            new[]
            {
                new NetParameterDescriptor(
                    NetRefKind.None, "MyLib.Callback", netType + "(" + netType + ")"),
            });

        var surface = new NetSurface(new[] { member }, new[] { "MyLib.Holder" });

        var ex = Assert.Throws<NotSupportedException>(
            () => NetShimGenerator.Emit(surface, "Shim"),
            "§8.4 v1 admits blittable scalars only; a pointer-shaped §6.4 row is not one, and "
            + "the gate must not admit it merely because it shares a WireKind with Integer");

        Assert.That(ex.Message, Does.Contain(netType));
    }

    /// <summary>A throwaway on-disk assembly the resolver can read real metadata from.</summary>
    private sealed class ProbeAssembly : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "blnet-convrow-" + Guid.NewGuid().ToString("N"));

        internal string Path { get; }

        internal ProbeAssembly(string name, string source)
        {
            System.IO.Directory.CreateDirectory(_dir);
            Path = System.IO.Path.Combine(_dir, name + ".dll");

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                name,
                new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
                NetTypeResolverTestRefs.FrameworkPaths.Select(
                    p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            Microsoft.CodeAnalysis.Emit.EmitResult emit;
            using (var stream = System.IO.File.Create(Path))
                emit = compilation.Emit(stream);

            Assert.That(emit.Success, Is.True, "probe assembly failed to build: "
                + string.Join("\n", emit.Diagnostics.Where(
                    d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_dir, recursive: true); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
