using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 9 helper for tests that scan WHOLE generated C++ output with negative
/// assertions (Does.Not.Contain): the P1 BCL runtime bodies are now spliced into
/// EVERY generated program (both emission modes), so legitimate runtime text (e.g.
/// the ostream inserters' <c>v.ToString()</c>, or the word "Integer" in a body
/// comment) would otherwise trip scans aimed at USER-code lowering. Stripping the
/// verbatim spliced bodies keeps those assertions at full strength over the code
/// the test actually targets.
/// </summary>
internal static class CppGeneratedCode
{
    /// <summary>
    /// <paramref name="cpp"/> with the spliced P1 runtime bodies removed and line
    /// endings normalized to '\n' (the splice re-emits the consts line-by-line, so
    /// only line endings can differ from the source consts).
    /// </summary>
    internal static string WithoutBclRuntime(string cpp)
    {
        var norm = cpp.Replace("\r\n", "\n");
        norm = norm.Replace(CppBclRuntime.BclBody.Replace("\r\n", "\n"), "");
        norm = norm.Replace(CppDecimalRuntime.DecimalBody.Replace("\r\n", "\n"), "");
        return norm;
    }
}

/// <summary>
/// P1 Task 9 (spec §12 layer 1 + splice smokes): the native BCL runtime
/// (bl_bcltypes + bl_decimal bodies) is spliced UNCONDITIONALLY into BOTH emission
/// modes — the fast pins below guard presence without a compiler (the classic
/// both-modes drift trap), and the Integration smokes prove the spliced headers
/// actually compile and run inside a generated program in each mode. Task 11
/// extends this fixture with the per-type BL end-to-end battery.
/// </summary>
[TestFixture]
public class CppBclEndToEndTests
{
    // ------------------------------------------------------------------
    // Shared compile helpers (same idiom as CppCollectionTests).
    // ------------------------------------------------------------------

    /// <summary>Compile BL source to combined-mode C++ (no optimizer).</summary>
    private static string CompileToCpp(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
    }

    /// <summary>
    /// Compile BL source to combined-mode C++ THROUGH the standard optimizer passes —
    /// the CLI-equivalent path (repo law: validate codegen via the optimizer, not only
    /// the non-optimizing helper).
    /// </summary>
    private static string CompileToCppOptimized(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");

        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
    }

    /// <summary>Front half mirrors CppSplitEmissionTests.Split: real frontend, then GenerateSplit.</summary>
    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-bcl-splice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.name); File.WriteAllText(p, f.code); return p; }).ToList();
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            return new CppCodeGenerator().GenerateSplit(
                result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------
    // FAST both-modes pins (no compiler needed) — spec §12 layer 1.
    // ------------------------------------------------------------------

    /// <summary>
    /// Combined mode: a trivial program's single-string output carries the spliced
    /// native BCL runtime (both header bodies), unconditionally.
    /// </summary>
    [Test]
    public void Combined_TrivialProgram_CarriesSplicedBclRuntime()
    {
        var output = CompileToCpp("Sub Main()\n    Console.WriteLine(\"hi\")\nEnd Sub");
        Assert.That(output, Does.Contain("struct DateTime"));
        Assert.That(output, Does.Contain("struct Decimal"));
    }

    /// <summary>
    /// Split mode: BasicLangRuntime.g.h carries the spliced native BCL runtime —
    /// the both-modes drift guard for the combined pin above.
    /// </summary>
    [Test]
    public void Split_RuntimeHeader_CarriesSplicedBclRuntime()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"));
        var rt = r.Files["BasicLangRuntime.g.h"];
        Assert.That(rt, Does.Contain("struct DateTime"));
        Assert.That(rt, Does.Contain("struct Decimal"));
    }

    // ------------------------------------------------------------------
    // Integration splice smokes: the spliced headers must COMPILE (and run)
    // inside a generated program, in both emission modes.
    // ------------------------------------------------------------------

    /// <summary>
    /// Combined mode through the OPTIMIZER (CLI-equivalent path): a trivial program
    /// now carries the full spliced BCL runtime and must still compile and run —
    /// this is what catches generator-owned include-set gaps and any collision
    /// between the runtime bodies and the always-emitted preamble/user code.
    /// </summary>
    [Test, Category("Integration")]
    public void Combined_TrivialProgram_WithSplicedRuntime_CompilesAndRuns()
    {
        var output = CompileToCppOptimized("Sub Main()\n    Console.WriteLine(\"bcl-splice-ok\")\nEnd Sub");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRun(output, compiler.Value).Replace("\r\n", "\n");

        Assert.That(stdout, Is.EqualTo("bcl-splice-ok\n"));
    }

    /// <summary>
    /// Split mode: the runtime header (now carrying the BCL bodies) is included by
    /// multiple translation units — compiling and linking them together proves the
    /// splice is ODR-safe (every out-of-class definition in the bodies is inline)
    /// and that the split include set covers the bodies' needs.
    /// </summary>
    [Test, Category("Integration")]
    public void Split_TrivialProgram_WithSplicedRuntime_CompilesAndRuns()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var r = Split(emitMain: true,
            ("Logic.bas", "Function Score() As Integer\n    Return 7\nEnd Function"),
            ("App.bas", "Sub Main()\n    PrintLine Score()\nEnd Sub"));

        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRunFiles(r.Files, r.TranslationUnitFileNames, compiler.Value);

        Assert.That(stdout.Trim(), Is.EqualTo("7"));
    }

    // ------------------------------------------------------------------
    // Task 10: the flip. Behaviour that only became reachable when the six
    // types moved to NativeOwned and SByte to Bridged.
    // ------------------------------------------------------------------

    /// <summary>
    /// Spec §14.6: <c>cout &lt;&lt; uint8_t/int8_t</c> streams a CHARACTER — before this
    /// commit <c>Console.WriteLine(byteValued65)</c> printed 'A'. .NET (and the C#
    /// backend) print the NUMBER, so the console lowering widens Byte/SByte args.
    /// This is the ONE deliberately LIVE behaviour change of the Byte signedness fix.
    /// ALL cout surfaces are covered — Console.Write/WriteLine AND the VB Print/PrintLine
    /// statements — so the two cannot drift into printing the same value differently.
    /// </summary>
    [Test, Category("Integration")]
    public void BytePrinting_IsNumeric_NotCharacter_OnEveryPrintSurface()
    {
        var output = CompileToCppOptimized(@"
Sub Main()
    Dim b As Byte = 65
    Console.WriteLine(b)
    PrintLine b
    Print b
    Console.WriteLine("""")
    Dim s As SByte = -3
    Console.WriteLine(s)
    PrintLine s
End Sub");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRun(output, compiler.Value).Replace("\r\n", "\n");

        // Console.WriteLine / PrintLine / Print(no newline) + WriteLine("") / …
        Assert.That(stdout, Is.EqualTo("65\n65\n65\n-3\n-3\n"));
    }

    /// <summary>
    /// The Task 10 rider: EVERY Decimal→non-floating conversion on BOTH lowering
    /// routes (CType via IRCast, C* intrinsics via EmitStdLibCall). A raw
    /// <c>static_cast</c>/<c>std::to_string</c> on the BasicLang::Decimal struct is
    /// invalid C++, so each target has an explicit engine-backed lowering.
    /// NOTE — the <c>CInt</c> value pinned here (19, truncating) is the C++ backend's
    /// LONG-STANDING C*-intrinsic convention, shared with Double/Single; the C#
    /// backend emits Convert.ToInt32 (which rounds → 20). That divergence is
    /// PRE-EXISTING and not Decimal-specific; the cross-backend parity oracle
    /// (Task 13) must use CType, not CInt, for integral narrowing.
    /// </summary>
    [Test, Category("Integration")]
    public void DecimalConversions_ToIntegralStringAndBoolean_LowerThroughTheEngine()
    {
        var output = CompileToCppOptimized(@"
Sub Main()
    Dim d As Decimal = 19.99
    Console.WriteLine(CType(d, Integer))
    Console.WriteLine(CType(d, Long))
    Console.WriteLine(CType(d, String))
    Console.WriteLine(CInt(d))
    Console.WriteLine(CStr(d))
    Console.WriteLine(CDbl(d))
    Dim neg As Decimal = -2.9
    Console.WriteLine(CType(neg, Integer))
    Dim zero As Decimal = 0
    If CType(d, Boolean) Then
        Console.WriteLine(""nonzero"")
    End If
    If Not CBool(zero) Then
        Console.WriteLine(""zero"")
    End If
End Sub");

        // Every Decimal source goes through an engine member, never a raw cast.
        var userCode = CppGeneratedCode.WithoutBclRuntime(output);
        Assert.That(userCode, Does.Contain(").ToDouble()"),
            "Decimal→integral/Double must route through ToDouble():\n" + output);
        Assert.That(userCode, Does.Contain(").ToString()"),
            "Decimal→String must route through the engine's ToString():\n" + output);
        Assert.That(userCode, Does.Contain("IsZeroMag()"),
            "Decimal→Boolean must route through the engine's zero test:\n" + output);

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRun(output, compiler.Value).Replace("\r\n", "\n");

        Assert.That(stdout, Is.EqualTo(
            // truncate-toward-zero (matching .NET's (int)decimal), scale-preserving
            // ToString, VarR8FromDec ToDouble, and Convert.ToBoolean's `!= 0`.
            "19\n19\n19.99\n19\n19.99\n19.99\n-2\nnonzero\nzero\n"));
    }
}

/// <summary>
/// P1 Task 10, spec §4.1: the member-surface capability pass. These run the
/// CHECKER only (no C++ compiler), so they are fast, not Integration. Task 11
/// extends the set; this fixture pins the classes of diagnostic the flip
/// introduced plus the two riders it resolved.
/// </summary>
[TestFixture]
public class CppNativeBclDiagnosticTests
{
    private static CppCapabilityException AssertRejected(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "expected a C++ CAPABILITY rejection, but semantic analysis failed first: "
            + string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        return Assert.Throws<CppCapabilityException>(() =>
            new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(irModule));
    }

    /// <summary>A member outside the curated v1 surface names the type AND the member.</summary>
    [Test]
    public void UnknownNativeMember_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Console.WriteLine(d.ToBinary())
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("ToBinary"));
    }

    /// <summary>
    /// Guid.ToByteArray is deliberately NOT on the BL v1 surface (spec §5: its Byte()
    /// return has no pinned C++ mapping) — it must reject, not silently emit the
    /// native out-param overload.
    /// </summary>
    [Test]
    public void GuidToByteArray_IsNotOnTheBlSurface()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim g As Guid = Guid.NewGuid()
    Console.WriteLine(g.ToByteArray())
End Sub");
        Assert.That(ex.Message, Does.Contain("Guid").And.Contain("ToByteArray"));
    }

    /// <summary>An arity outside the surface's overload list is named explicitly.</summary>
    [Test]
    public void UnknownConstructorArity_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim g As New Guid()
    Console.WriteLine(g.ToString())
End Sub");
        Assert.That(ex.Message, Does.Contain("New Guid").And.Contain("1 argument"));
    }

    /// <summary>
    /// LEAK CLOSURE (spec §4.1): a Rejected type used ONLY in expression position never
    /// declares a typed slot, so CheckType never saw it and the construction reached
    /// codegen as an undefined C++ type name (BL6006-class raw failure).
    /// </summary>
    [Test]
    public void RejectedTypeInExpressionPosition_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Console.WriteLine(New Regex(""x""))
End Sub");
        Assert.That(ex.Message, Does.Contain("Regex").And.Contain("no C++ mapping"));
    }

    /// <summary>
    /// Task 10 rider (c): before the flip a user-defined `Class Guid` was name-rejected
    /// as an unmapped .NET type; after it, MapType would SILENTLY remap it to
    /// BasicLang::Guid. BasicLang has no namespace to disambiguate with, so the honest
    /// answer is an explicit rename request.
    /// </summary>
    [Test]
    public void UserTypeShadowingNativeBcl_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Class Guid
    Public X As Integer
End Class

Sub Main()
    Dim g As New Guid()
    g.X = 5
    Console.WriteLine(g.X)
End Sub");
        Assert.That(ex.Message, Does.Contain("'Guid' conflicts with a native BCL type"));
        Assert.That(ex.Message, Does.Contain("rename"));
    }

    /// <summary>
    /// Task 10 rider (a), rejection half: a conversion the engine does NOT define must
    /// fail here, never as a raw static_cast on a struct at the C++ compiler.
    /// </summary>
    [Test]
    public void UnsupportedNativeConversion_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Dim n As Integer = CType(d, Integer)
    Console.WriteLine(n)
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("Integer"));
        Assert.That(ex.Message, Does.Contain("not supported on the C++ backend"));
    }

    /// <summary>
    /// The C* intrinsics lower through EmitStdLibCall, NOT Visit(IRCast), so the IRCast
    /// gate above never saw them: <c>CStr(dt)</c> emitted <c>to_string(dt)</c> and
    /// <c>CDbl(dt)</c> emitted <c>static_cast&lt;double&gt;(dt)</c> — both clean through
    /// the BasicLang pipeline, both MSVC errors. Only Decimal was guarded (it has
    /// lowerings); the other five native types must reject.
    /// </summary>
    [TestCase("CStr", "String")]
    [TestCase("CDbl", "Double")]
    [TestCase("CInt", "Integer")]
    [TestCase("CBool", "Boolean")]
    public void ConversionIntrinsicOnNonDecimalNativeType_IsRejectedCleanly(
        string intrinsic, string targetName)
    {
        var ex = AssertRejected($@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Console.WriteLine({intrinsic}(d))
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain(targetName));
        Assert.That(ex.Message, Does.Contain("not supported on the C++ backend"));
    }

    /// <summary>
    /// A static call arrives as an IRCall with a DOTTED FunctionName — neither the
    /// IRInstanceMethodCall nor the IRFieldAccess arm saw it. An unknown static fell
    /// through the generator's surface dispatch to a flattened phantom function
    /// (<c>t0 = DateTimeFromBinary(5);</c> against a <c>void*</c> temp).
    /// </summary>
    [Test]
    public void UnknownStaticMethod_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Console.WriteLine(DateTime.FromBinary(5))
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("FromBinary"));
        Assert.That(ex.Message, Does.Contain("has no native member"));
    }

    /// <summary>
    /// GUARD for the caveat in the new IRCall arm: the generator DELIBERATELY lowers the
    /// parenthesized static-PROPERTY form through the same static dispatch as the
    /// paren-less one, so the member pass must not reject call syntax on a
    /// StaticProperty. Zero-arg static METHODS (Guid.NewGuid) ride the same path.
    /// </summary>
    [Test]
    public void ParenthesizedStaticProperty_IsAccepted()
    {
        var source = @"
Sub Main()
    Dim d As DateTime = DateTime.Now()
    Dim g As Guid = Guid.NewGuid()
    Console.WriteLine(d.Year)
    Console.WriteLine(g.ToString())
End Sub";
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        var output = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
        Assert.That(output, Does.Contain("BasicLang::DateTime::Now()"));
        Assert.That(output, Does.Contain("BasicLang::Guid::NewGuid()"));
    }
}
