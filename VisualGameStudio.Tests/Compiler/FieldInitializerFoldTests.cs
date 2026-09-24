using System;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A class field whose initializer is an EXPRESSION rather than a bare literal.
///
/// <para>⛔ Every such shape was dropped SILENTLY and the field read its type's zero. Measured
/// before, on expressions that are arithmetically constant: <c>2 + 3</c>, <c>2 * 3 + 1</c>,
/// <c>(1 + 2) * 3</c> and <c>8 \ 2</c> each emitted a bare <c>public int N;</c> on C# and printed
/// <b>0</b> on JavaScript; <c>"a" &amp; "b"</c> gave <c>public string N;</c> and an empty string;
/// <c>True And False</c> and <c>1 &lt; 2</c> gave <c>public bool N;</c> and False. Only a bare
/// literal and unary +/- on one ever survived — <c>BuildConstantFieldInitializer</c> matched
/// <c>LiteralExpressionNode</c> and nothing else, and returned null for everything else, which the
/// field emission read as "no initializer".</para>
///
/// <para>⚠ The fix does not invent a second notion of constant: the field path now asks the SAME
/// helper module-scope globals ask (<c>TryFoldInitializerToConstant</c>, extracted from
/// <c>BuildModuleScopeInitializer</c> for this). <see cref="ModuleScopeInitializerTests"/> is its
/// sibling, and the two agreeing is the property — including where they agree to refuse.</para>
///
/// <para>⛔ Deleting the literal shortcut FIXED A SECOND, UNRELATED BUG. It coerced a Decimal
/// field's literal to a double, so <c>Public M As Decimal = 1.5</c> emitted
/// <c>public decimal M = 1.5;</c> and the real C#-backend build failed with <b>CS0664</b> — "use
/// an 'M' suffix". The general lowering emits <c>1.5m</c>. The shortcut was kept at first to make
/// this change additive, then removed once measurement showed no test could tell it from the fold
/// and the one shape where they DID differ was the shortcut being wrong.</para>
///
/// <para>⛔ What genuinely needs code to RUN is now REFUSED rather than dropped. A field
/// initializer that runs code would have to be lowered into every constructor on every backend,
/// which none of them does here; the old silent drop turned that into a field reading 0 with no
/// diagnostic anywhere. Measured, the behaviour CHANGES for these: <c>= Helper()</c> and
/// <c>= CInt(2.5)</c> compiled before and read <b>0</b>, and are compile errors now.</para>
///
/// <para>⚠ A <c>Const</c> inside a class PARSES as of 2026-09-18 (<see cref="ClassConstantTests"/>)
/// and lowers to a static field through this same helper. Referencing that named constant from
/// another initializer (<c>= K + 1</c>) is still refused, because the folder substitutes no named
/// constants — a SHARED limit rather than a class one, since module scope refuses the identical
/// shape. A <c>Structure</c> field initializer still does not parse ("Expected member name but
/// found Assignment"), which keeps the structure call site unreachable for initializers even
/// though it is wired up.</para>
/// </summary>
[TestFixture]
public class FieldInitializerFoldTests
{
    /// <summary>
    /// ⚠ The class is declared FIRST. When this fixture was written that was REQUIRED: with the
    /// module first, a member's TYPE did not resolve and the temp reading it decayed to Object —
    /// measured on a plain LITERAL initializer, so never about folding. That gap is FIXED for a
    /// top-level class (<see cref="ClassDeclarationOrderTests"/>), so the order here is now habit
    /// rather than necessity; it stays so this fixture keeps testing folding and nothing else.
    /// </summary>
    private static string Program(string field, string print) => $"""
        Class Box
         {field}
        End Class

        Module M
         Function Helper() As Integer
          Return 7
         End Function
         Sub Main()
          Dim c As New Box()
          {print}
         End Sub
        End Module
        """;

    // ====================================================================================
    // What now FOLDS — every one of these silently read zero before.
    // ====================================================================================

    /// <summary>
    /// ⚠ RUN, not merely compiled. A build that succeeds while the field stays 0 is exactly the
    /// failure this fixes, so asserting the emitted declaration alone would not hold it.
    ///
    /// <para>⛔ THREE backends here, not four. C++ carries these too but formats a Double
    /// differently, so it is asserted separately in
    /// <see cref="AFoldedFieldInitializer_ReachesTheCppProgram"/> against its own spelling.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Public N As Integer = 2 + 3", "PrintLine(CStr(c.N))", "5", TestName = "Fold_Arithmetic")]
    [TestCase("Public N As Integer = 2 * 3 + 1", "PrintLine(CStr(c.N))", "7", TestName = "Fold_Precedence")]
    [TestCase("Public N As Integer = (1 + 2) * 3", "PrintLine(CStr(c.N))", "9", TestName = "Fold_Nested")]
    [TestCase("Public N As Integer = 8 \\ 2", "PrintLine(CStr(c.N))", "4", TestName = "Fold_IntDiv")]
    [TestCase("Public N As String = \"a\" & \"b\"", "PrintLine(c.N)", "ab", TestName = "Fold_Concat")]
    [TestCase("Public N As Double = 7.0 / 2.0", "PrintLine(CStr(c.N))", "3.5", TestName = "Fold_RealDiv")]
    [TestCase("Public N As Double = 7 / 2", "PrintLine(CStr(c.N))", "3.5", TestName = "Fold_PromotedDiv")]
    public void AComputedFieldInitializer_FoldsAndReaches_EveryBackend(
        string field, string print, string expected)
    {
        var program = Program(field, print);

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected));
        });
    }

    /// <summary>
    /// ⚠ The BOOLEAN-valued folds, separately, because <c>CStr(Boolean)</c> does not agree across
    /// backends: JavaScript prints <c>true</c>/<c>false</c> where C# and MSIL print
    /// <c>True</c>/<c>False</c>. That is PRE-EXISTING and nothing to do with folding — measured on
    /// a plain local — so each backend is asserted against its own spelling rather than
    /// normalised to a shared one.
    ///
    /// <para>⛔ These matter beyond arithmetic: <c>True And False</c> goes through
    /// <c>FoldAnd</c> and <c>1 &lt; 2</c> through <c>TryFoldCompare</c>, which are different
    /// folders from the binary-arithmetic one every other case here exercises. Both read
    /// <b>False</b> before this change — and False is also what a dropped Boolean initializer
    /// leaves behind, so the <c>1 &lt; 2</c> case is the one that can tell folding from
    /// dropping.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Public N As Boolean = True And False", "False", "false", TestName = "Fold_BoolOp")]
    [TestCase("Public N As Boolean = 1 < 2", "True", "true", TestName = "Fold_Compare")]
    public void ABooleanFieldInitializer_Folds_InEachBackendsOwnSpelling(
        string field, string dotNet, string js)
    {
        var program = Program(field, "PrintLine(CStr(c.N))");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(dotNet + "\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(js));
        });
    }

    /// <summary>
    /// ⚠ C++ separately, because it spells a Boolean <c>True</c> — a long-recorded divergence,
    /// pinned as C++ actually behaves. (Its Double was <c>3.500000</c> until C++ got .NET's
    /// formatter; it is <c>3.5</c> now — CppDoubleFormattingTests.)
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Public N As Integer = 2 + 3", "PrintLine(CStr(c.N))", "5", TestName = "Cpp_Arithmetic")]
    [TestCase("Public N As Integer = (1 + 2) * 3", "PrintLine(CStr(c.N))", "9", TestName = "Cpp_Nested")]
    [TestCase("Public N As String = \"a\" & \"b\"", "PrintLine(c.N)", "ab", TestName = "Cpp_Concat")]
    [TestCase("Public N As Double = 7.0 / 2.0", "PrintLine(CStr(c.N))", "3.5", TestName = "Cpp_Double")]
    [TestCase("Public N As Boolean = 1 < 2", "PrintLine(CStr(c.N))", "True", TestName = "Cpp_Compare")]
    public void AFoldedFieldInitializer_ReachesTheCppProgram(
        string field, string print, string expected)
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program(field, print))),
            Is.EqualTo(expected + "\n"));
    }

    /// <summary>
    /// ⛔ The value must be in place BEFORE the constructor body, not merely somewhere: the body
    /// reads the field it is about to change. A fold that landed after the body would pass every
    /// case above and fail this one.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AFoldedInitializer_IsInPlaceBeforeTheConstructorBody()
    {
        var program = """
            Class Box
             Private _n As Integer = 2 + 3
             Public Sub New(v As Integer)
              _n = _n + v
             End Sub
             Public Function Read() As Integer
              Return _n
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box(3)
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """;

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("8\n"));
    }

    /// <summary>
    /// ⚠ Every LITERAL shape must still carry its value, now that they go through the general
    /// fold rather than a literal shortcut. This is the regression guard for deleting that
    /// shortcut — the shapes that already worked have to keep working.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ALiteralInitializer_StillReachesEveryBackend()
    {
        var program = Program(
            """
            Public I As Integer = 5
             Public S As String = "hi"
             Public D As Double = 2.5
             Public F As Single = 1.5
             Public B As Boolean = True
             Public Neg As Integer = -5
            """,
            "PrintLine(CStr(c.I) & \",\" & c.S & \",\" & CStr(c.Neg))");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5,hi,-5"));
        });
    }

    /// <summary>
    /// ⛔ A <c>Decimal</c> field literal emitted INVALID C# before this change, and the real
    /// C#-backend build failed: <c>public decimal M = 1.5;</c> is <b>CS0664</b>, "Literal of type
    /// double cannot be implicitly converted to type 'decimal'; use an 'M' suffix". Measured
    /// end to end through <c>BasicLang.exe build</c> with <c>TargetBackend=CSharp</c>.
    ///
    /// <para>⚠ Nothing to do with non-literal initializers — it is the LITERAL fast path this
    /// change deleted that was wrong, and deleting it is what fixes this. Kept as its own case so
    /// a future re-introduction of a literal shortcut has to answer for it.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ADecimalFieldLiteral_EmitsValidCSharp()
    {
        var program = Program("Public M As Decimal = 1.5", "PrintLine(CStr(c.M))");

        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty,
            "a Decimal field literal must emit C# that actually compiles");
    }

    // ====================================================================================
    // What is REFUSED — and that it is a diagnostic naming the field, not a crash.
    // ====================================================================================

    /// <summary>
    /// ⛔ An initializer that needs code to RUN cannot become a constant, and no backend here has
    /// anywhere to run it. Refused with a diagnostic naming the FIELD.
    ///
    /// <para>⚠ This is the one behaviour CHANGE for programs that used to compile: both of these
    /// built before and the field read <b>0</b>. Asserting the message rather than merely that
    /// something threw — a crash would also throw.</para>
    ///
    /// <para>⚠ <c>CInt(...)</c> is not a cast but an IRCall, so neither folding pass touches it —
    /// the same reason module scope refuses it, and for CInt a welcome one, because the backends
    /// do not agree on what it means.</para>
    /// </summary>
    [Test]
    [TestCase("Public N As Integer = Helper()", TestName = "Refuse_Call")]
    [TestCase("Public N As Integer = CInt(2.5)", TestName = "Refuse_CInt")]
    public void AFieldInitializerNeedingRuntimeCode_IsRefusedWithADiagnostic(string field)
    {
        var ex = Assert.Throws<Exception>(
            () => JsTestSupport.BuildModule(Program(field, "PrintLine(\"ok\")")));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.Contain("cannot be computed at compile time"));
            Assert.That(ex.Message, Does.Contain("'N'"),
                "the diagnostic must name the field, not just the shape:\n" + ex.Message);
            Assert.That(ex.Message, Does.Contain("constructor"),
                "and point at the way out:\n" + ex.Message);
            Assert.That(ex.Message, Does.Not.Contain("Object reference not set"),
                "asserting only that it throws would pass on a crash");
        });
    }

    /// <summary>
    /// ⚠ A field and a module-scope global must agree about what counts as constant — that is the
    /// whole reason both go through one helper. This pins the agreement on a shape they BOTH
    /// refuse, so a future change that loosens one without the other fails here.
    ///
    /// <para>⛔ Both diverge from the LOCAL path, which accepts <c>Helper()</c> and computes it at
    /// run time. That divergence is PRE-EXISTING for globals and now shared by fields; closing it
    /// means running initializer code in a constructor on every backend, which is its own
    /// change.</para>
    /// </summary>
    [Test]
    public void AFieldAndAGlobal_RefuseTheSameShape()
    {
        var field = Assert.Throws<Exception>(() => JsTestSupport.BuildModule(
            Program("Public N As Integer = Helper()", "PrintLine(\"ok\")")));

        var global = Assert.Throws<Exception>(() => JsTestSupport.BuildModule("""
            Module M
             Dim G As Integer = Helper()
             Function Helper() As Integer
              Return 7
             End Function
             Sub Main()
              PrintLine("ok")
             End Sub
            End Module
            """));

        Assert.Multiple(() =>
        {
            Assert.That(field.Message, Does.Contain("cannot be computed at compile time"));
            Assert.That(global.Message, Does.Contain("cannot be computed at compile time"));
            Assert.That(field.Message, Does.Contain("field"),
                "each names its own kind of declaration:\n" + field.Message);
            Assert.That(global.Message, Does.Contain("module-level"),
                "each names its own kind of declaration:\n" + global.Message);
        });
    }

    /// <summary>
    /// ⚠ The scratch function the fold lowers into must NOT reach the module — it is created
    /// outside <c>_module.CreateFunction</c> for exactly that reason, and a leak would put a stray
    /// <c>&lt;init&gt;N</c> function in every backend's output for every computed field.
    /// </summary>
    [Test]
    public void TheScratchFunction_DoesNotLeakIntoTheModule()
    {
        var module = JsTestSupport.BuildModule(
            Program("Public N As Integer = 2 + 3", "PrintLine(CStr(c.N))"));

        Assert.That(module.Functions.ConvertAll(f => f.Name), Has.None.Contains("<init>"),
            "the scratch function leaked into the module's function list");
    }
}
