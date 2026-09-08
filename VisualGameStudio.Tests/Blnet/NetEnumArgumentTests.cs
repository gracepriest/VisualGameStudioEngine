using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Net;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// §8.3's enum row — P2a-2 Task 8c-3. An enum MEMBER argument crosses the native boundary as
/// its UNDERLYING INTEGRAL, folded to a compile-time constant.
///
/// <para><b>The fold is decided by the SLOT, not by the expression.</b> The analyzer folds an
/// enum-literal argument only when the winner's parameter at that index is enum-typed, so the
/// overload-probe spelling still says <c>FileMode</c> — which matters because the probe
/// synthesizes C# that must compile, and <c>File.Open(a0, System.Int32)</c> is CS1503.</para>
///
/// <para>⛔ THREE THINGS THIS MUST NOT BREAK, each measured green BEFORE the change and pinned
/// below: the C#-backend path (§6.3's preservation row), the ungated property-WRITE path
/// (<c>fi.Attributes = FileAttributes.ReadOnly</c> ships on the handle wire today and has NO
/// analyzer gate in front of it), and the refusal for enum VARIABLES — which is NARROWED, never
/// deleted. Deleting it is the miscompile: with the wire now Scalar, a non-literal enum
/// argument lowers as a <c>BasicLang::NetRef</c> into an <c>int32_t</c> slot, and NetRef has no
/// integral conversion.</para>
/// </summary>
[TestFixture]
public class NetEnumArgumentTests
{
    private static readonly Lazy<BasicLang.Net.NetTypeResolver> SharedResolver =
        NetStubHarness.SharedResolver;

    private static (SemanticAnalyzer Analyzer, IRModule Module) BuildIR(
        string source, bool nativeBackend = true)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        return (analyzer, new IRBuilder(analyzer).Build(ast, "TestModule"));
    }

    private static SemanticAnalyzer AnalyzeForFindings(string source, bool nativeBackend = true)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend);
        analyzer.Analyze(ast);
        return analyzer;
    }

    private static string Findings(SemanticAnalyzer a) =>
        string.Join(" | ", a.NetDiagnostics.Select(d => d.Code + ": " + d.Message));

    private const string LiteralArgument = @"
Using System.IO

Sub Main()
    File.SetAttributes(""a.txt"", FileAttributes.ReadOnly)
End Sub
";

    private const string VariableArgument = @"
Using System.IO

Sub Main()
    Dim fi As New FileInfo(""a.txt"")
    File.SetAttributes(""a.txt"", fi.Attributes)
End Sub
";

    private const string PropertyWrite = @"
Using System.IO

Sub Main()
    Dim fi As New FileInfo(""a.txt"")
    fi.Attributes = FileAttributes.ReadOnly
End Sub
";

    // ====================================================================================
    // The resolver half
    // ====================================================================================

    [Test]
    public void EnumMemberConstant_ReturnsTheUnderlyingPrimitive_NotTheEnum()
    {
        var value = SharedResolver.Value.EnumMemberConstant(
            "System.Text.RegularExpressions.RegexOptions", "IgnoreCase");

        Assert.That(value, Is.EqualTo(1));

        // ⛔ THE TYPE IS THE POINT, not just the number. Both the C++ and JavaScript constant
        // emitters end in Value.ToString(), and Visit(IRConstant) is a no-op on both — a value
        // still boxed as the ENUM renders as the bare identifier "IgnoreCase", an undeclared
        // name emitted from a GREEN build. Is.EqualTo(1) alone passes on the enum box.
        Assert.That(value, Is.TypeOf<int>(),
            "the constant must be coerced to the underlying CLR primitive at capture");
    }

    [Test]
    public void EnumMemberConstant_IsCaseInsensitive_LikeTheRestOfTheLanguage()
    {
        // TryTypeNetEnumArgument admits the member with OrdinalIgnoreCase, so an Ordinal
        // compare in the resolver would make the fold silently vanish for `FileMode.open`
        // — no diagnostic, just a member access that stays a handle.
        Assert.That(
            SharedResolver.Value.EnumMemberConstant(
                "System.Text.RegularExpressions.RegexOptions", "ignorecase"),
            Is.EqualTo(1));
    }

    [Test]
    public void EnumMemberConstant_IsNullForANonEnumOrAnUnknownMember()
    {
        Assert.That(SharedResolver.Value.EnumMemberConstant("System.String", "Empty"), Is.Null);
        Assert.That(
            SharedResolver.Value.EnumMemberConstant(
                "System.Text.RegularExpressions.RegexOptions", "NoSuchMember"),
            Is.Null);
    }

    // ====================================================================================
    // The analyzer half
    // ====================================================================================

    [Test]
    public void AnEnumMemberArgument_DrawsNoFinding_OnTheNativePath()
    {
        var analyzer = AnalyzeForFindings(LiteralArgument);

        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "an enum MEMBER has a value the native boundary can produce; it must lower, not "
            + "refuse. Findings: " + Findings(analyzer));
    }

    [Test]
    public void AnEnumVARIABLEArgument_IsStillRefused_WithTheNarrowedMessage()
    {
        // THE SCOPE PIN. Deleting the refusal instead of narrowing it is the exact miscompile
        // this task is sequenced to avoid — a NetRef in an int32_t slot, in generated C++ the
        // user never wrote.
        var analyzer = AnalyzeForFindings(VariableArgument);
        var findings = Findings(analyzer);

        Assert.That(findings, Does.Contain("BL6019"));
        Assert.That(findings, Does.Contain("only an enum MEMBER"),
            "the message must say WHY a variable cannot cross, not just that it cannot");
    }

    [Test]
    public void TheCSharpPath_IsUnchanged_ByTheFold()
    {
        // §6.3's preservation row. The recording site is inside an already native-gated
        // method, so the C# path must see neither a fold nor a new diagnostic.
        var analyzer = AnalyzeForFindings(LiteralArgument, nativeBackend: false);

        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "the C# backend judges these calls late, via csc; a new finding here would be a "
            + "regression on a program that compiles clean. Findings: " + Findings(analyzer));
    }

    // ====================================================================================
    // The lowering half
    // ====================================================================================

    [Test]
    public void TheFoldedArgument_CrossesAsItsUnderlyingInteger()
    {
        var (_, module) = BuildIR(LiteralArgument);
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);

        Assert.That(cpp, Does.Contain("\"a.txt\", 1)"),
            "FileAttributes.ReadOnly is 1; the argument must be that literal, not a handle");

        // ⛔ Boxing pin. CppCodeGenerator's constant emitter ends in Value.ToString(), so an
        // enum-boxed constant emits the bare identifier `ReadOnly` — an undeclared name, from
        // a build that reported success.
        Assert.That(cpp, Does.Not.Contain(", ReadOnly)"),
            "an enum-boxed constant would render as a bare undeclared identifier");
    }

    [Test]
    public void AFoldedEnumMember_MintsNoShimExport()
    {
        // Before this change FileMode.Open emitted a real GCHandle-returning export. The
        // suppression is free ONLY because NetSurfaceCollector is IR-driven and IRBuilder now
        // returns before constructing an IRFieldAccess — so this reddens if E13's arm is moved
        // below the emission, while the VALUE assertions above would all still pass.
        var (_, module) = BuildIR(LiteralArgument);
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);

        Assert.That(cpp, Does.Not.Contain("FileAttributes_ReadOnly"),
            "a compile-time constant must not cost an export, a proxy slot and an AOT publish");
    }

    [Test]
    public void ThePropertyWritePath_StillLowersOnTheHandleWire()
    {
        // NON-REGRESSION on a shape that ships TODAY and has NO analyzer gate: the value
        // parameter of a synthesized setter comes from NetAccessorSynthesis, not Roslyn, and is
        // deliberately NOT given an underlying type. Propagating it there would put a NetRef in
        // an int32_t slot with nothing in front of it.
        var analyzer = AnalyzeForFindings(PropertyWrite);

        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "fi.Attributes = FileAttributes.ReadOnly compiles clean today and must keep doing "
            + "so. Findings: " + Findings(analyzer));
    }
}
