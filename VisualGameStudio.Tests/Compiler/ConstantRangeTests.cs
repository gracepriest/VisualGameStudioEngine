using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// VB's <b>BC30439</b>, "Constant expression not representable in type 'X'" — a value the compiler
/// can work out now, stored somewhere it does not fit.
///
/// <para>⛔ Nothing said anything, and the four backends did four things. Measured for
/// <c>Dim b As Byte = 300</c> at LOCAL scope:</para>
///
/// <list type="bullet">
/// <item><b>C#</b> emitted <c>b = 300;</c> against a <c>byte</c> — <b>CS0031</b>, so the program
/// does not build at all. The BasicLang build still reported success, because it only writes the
/// source.</item>
/// <item><b>JavaScript</b> printed <b>300</b>. A JS number has no width to overflow, so nothing
/// narrowed.</item>
/// <item><b>C++</b> printed <b>44</b>, narrowing implicitly at the declaration.</item>
/// <item><b>MSIL</b> also 44 — <c>ldc.i4 300</c> into a <c>uint8</c> slot. Run, not inferred from
/// the IL: ilasm is here, via the <c>runtime.linux-x64.microsoft.netcore.ilasm</c> package that
/// <c>MsilHarness</c> looks for.</item>
/// </list>
///
/// <para>⚠ MODULE scope was the ONE place they agreed, on the wrap (44), because
/// <c>IRBuilder.NarrowModuleScopeConstant</c> folds it there. <c>docs/HANDOFF.md</c> generalized
/// that agreement to both scopes — "measured on both" — and was wrong about locals, where two
/// backends disagree and a third does not compile. Agreeing on a wrap was never the goal: real VB
/// rejects every one of these, so the fix is in the FRONT END, ahead of all four backends and both
/// scopes at once.</para>
///
/// <para>⚠ The same divergence sat at five more sites, all measured the same way —
/// <c>b = 300</c>, <c>a(0) = 300</c>, <c>x.F = 300</c>, <c>Return 300</c> and <c>Take(300)</c>,
/// where C# adds CS0221 and CS1503 to the tally. They are checked here together, because a
/// language where <c>Dim b As Byte = 300</c> is refused and <c>b = 300</c> is not has no rule at
/// all.</para>
/// </summary>
[TestFixture]
public class ConstantRangeTests
{
    // ====================================================================================
    // The headline: the shape that meant four different things.
    // ====================================================================================

    /// <summary>
    /// ⛔ The defect. Before this, C# would not build, JavaScript said 300 and C++ said 44.
    /// </summary>
    [Test]
    public void ALocalDeclaration_OutOfRange_IsRefused()
    {
        Assert.That(Analyze("""
            Module M
             Sub Main()
              Dim b As Byte = 300
             End Sub
            End Module
            """),
            Has.Some.Contains(
                "Constant expression not representable in type 'Byte': the initializer for "
                + "variable 'b' is 300, but 'Byte' holds 0 through 255."));
    }

    /// <summary>
    /// ⚠ The same declaration at MODULE scope. It reaches a different narrowing path in the IR
    /// builder (<c>NarrowModuleScopeConstant</c> rather than <c>TryConvertConstant</c>), which is
    /// why the two scopes disagreed at all — and why the check is on the one AST node both come
    /// through rather than on either path.
    /// </summary>
    [Test]
    public void AModuleScopeDeclaration_OutOfRange_IsRefused()
    {
        Assert.That(Analyze("""
            Module M
             Dim G As Byte = 300
             Sub Main()
              PrintLine(CStr(G))
             End Sub
            End Module
            """),
            Has.Some.Contains("Constant expression not representable in type 'Byte'"));
    }

    // ====================================================================================
    // The boundaries, per type — the table is the fix, so the table is what gets tested.
    // ====================================================================================

    /// <summary>
    /// ⚠ Each type's first legal and first illegal value on both ends. An off-by-one anywhere in
    /// the range table is a program that either will not build or silently wraps, so this is
    /// exhaustive rather than representative.
    /// </summary>
    [TestCase("Byte", "0", true)]
    [TestCase("Byte", "255", true)]
    [TestCase("Byte", "256", false)]
    [TestCase("Byte", "-1", false)]
    [TestCase("SByte", "-128", true)]
    [TestCase("SByte", "127", true)]
    [TestCase("SByte", "128", false)]
    [TestCase("SByte", "-129", false)]
    [TestCase("Short", "-32768", true)]
    [TestCase("Short", "32767", true)]
    [TestCase("Short", "32768", false)]
    [TestCase("Short", "-32769", false)]
    [TestCase("UShort", "0", true)]
    [TestCase("UShort", "65535", true)]
    [TestCase("UShort", "65536", false)]
    [TestCase("UShort", "-1", false)]
    [TestCase("Integer", "2147483647", true)]
    [TestCase("Integer", "2147483648", false)]
    [TestCase("Integer", "3000000000", false)]
    [TestCase("UInteger", "4294967295", true)]
    [TestCase("UInteger", "4294967296", false)]
    [TestCase("UInteger", "-1", false)]
    public void TheRangeOfEachIntegralType_IsExact(string type, string literal, bool fits)
    {
        var errors = Analyze($"""
            Module M
             Sub Main()
              Dim v As {type} = {literal}
             End Sub
            End Module
            """);

        var refused = errors.Any(e => e.Contains("Constant expression not representable"));

        Assert.That(refused, Is.EqualTo(!fits),
            $"'Dim v As {type} = {literal}' should {(fits ? "be accepted" : "be refused")}; "
            + "errors: " + string.Join(" | ", errors));
    }

    // ====================================================================================
    // The check runs AFTER the narrowing rounds — and rounds the way the narrowing does.
    // ====================================================================================

    /// <summary>
    /// ⛔ <c>255.4</c> is a LEGAL Byte and <c>255.6</c> is not, because the narrowing rounds:
    /// 255.4 becomes 255, 255.6 becomes 256. Comparing the raw value instead would refuse
    /// <c>255.4</c> — a program every backend handles correctly today — which is what makes the
    /// 255.4 case, not the 255.6 one, the half that discriminates.
    ///
    /// <para>⚠ <c>-0.5</c> is the OTHER half: half-to-even sends it to -0, which is 0 and legal,
    /// while away-from-zero would send it to -1 and refuse it. That is the rounding mode
    /// <c>IRBuilder.TryConvertConstant</c> uses, and the two have to agree or a program that
    /// compiles gets a different answer than one that does not.</para>
    /// </summary>
    [TestCase("255.4", true)]
    [TestCase("255.6", false)]
    [TestCase("-0.5", true)]
    [TestCase("255.5", false)]
    public void AFractionalConstant_IsCheckedAfterHalfToEvenRounding(string literal, bool fits)
    {
        var errors = Analyze($"""
            Module M
             Sub Main()
              Dim v As Byte = {literal}
             End Sub
            End Module
            """);

        Assert.That(errors.Any(e => e.Contains("Constant expression not representable")),
            Is.EqualTo(!fits), "errors: " + string.Join(" | ", errors));
    }

    // ====================================================================================
    // What counts as a constant expression.
    // ====================================================================================

    /// <summary>
    /// ⚠ A folded EXPRESSION, not just a literal. <c>Dim b As Byte = 100 + 200</c> diverged
    /// exactly like the bare literal did (300 on JavaScript, 44 on C++), so checking literals
    /// alone would have left the same bug reachable by writing the sum out.
    /// </summary>
    [TestCase("100 + 200", false)]
    [TestCase("100 + 20", true)]
    [TestCase("300 - 100", true)]
    [TestCase("15 * 17", true)]
    [TestCase("16 * 16", false)]
    public void AConstantExpression_IsFoldedBeforeTheCheck(string expression, bool fits)
    {
        var errors = Analyze($"""
            Module M
             Sub Main()
              Dim v As Byte = {expression}
             End Sub
            End Module
            """);

        Assert.That(errors.Any(e => e.Contains("Constant expression not representable")),
            Is.EqualTo(!fits), "errors: " + string.Join(" | ", errors));
    }

    /// <summary>
    /// ⚠ A <c>Const</c> DECLARATION is checked in its own right, not only where it is used —
    /// which is the site VB names BC30439 for. Measured before: <c>Const KB As Byte = 300</c>
    /// narrowed to 44 on JavaScript, where the <c>Dim</c> spelling of the same thing kept 300, so
    /// the two shapes of one declaration did not even agree with each other.
    ///
    /// <para>⛔ A separate test from <see cref="AConstReference_FoldsToItsValue"/> on purpose:
    /// that one folds an IN-RANGE <c>Const</c> through to a narrower USE, and passes with this
    /// site removed. Only a <c>Const</c> that overflows its OWN declared type holds it.</para>
    /// </summary>
    [Test]
    public void AConstDeclaration_OutOfRange_IsRefused()
    {
        Assert.That(Analyze("""
            Module M
             Const KB As Byte = 300
             Sub Main()
              PrintLine(CStr(KB))
             End Sub
            End Module
            """),
            Has.Some.Contains(
                "Constant expression not representable in type 'Byte': the value of constant "
                + "'KB' is 300, but 'Byte' holds 0 through 255."));
    }

    /// <summary>
    /// ⚠ A reference to a <c>Const</c> folds through to its value.
    /// </summary>
    [Test]
    public void AConstReference_FoldsToItsValue()
    {
        Assert.That(Analyze("""
            Module M
             Const N As Integer = 300
             Sub Main()
              Dim b As Byte = N
             End Sub
            End Module
            """),
            Has.Some.Contains("Constant expression not representable in type 'Byte'"));
    }

    /// <summary>
    /// ⛔ The guard that keeps this a CONSTANT-expression rule. <c>Dim b As Byte = i</c> is a
    /// run-time conversion whose value the compiler does not know, and VB does not report BC30439
    /// for it either — a version of this check that fired on anything numeric would refuse a legal
    /// program.
    /// </summary>
    [Test]
    public void ANonConstantInitializer_IsNotRefused()
    {
        Assert.That(Analyze("""
            Module M
             Sub Main()
              Dim i As Integer = 300
              Dim b As Byte = i
              PrintLine(CStr(b))
             End Sub
            End Module
            """),
            Is.Empty);
    }

    /// <summary>
    /// ⚠ A COMPOUND assignment is not a constant expression — <c>c += 300</c> depends on
    /// <c>c</c> — so only <c>=</c> is checked. Without that guard this refuses a legal program,
    /// which is the failure mode worth a test of its own.
    /// </summary>
    [Test]
    public void ACompoundAssignment_IsNotRefused()
    {
        Assert.That(Analyze("""
            Module M
             Sub Main()
              Dim c As Byte = 200
              c += 300
              PrintLine(CStr(c))
             End Sub
            End Module
            """),
            Is.Empty);
    }

    // ====================================================================================
    // Every other store site — one rule, or no rule.
    // ====================================================================================

    /// <summary>
    /// ⛔ The five sites that are not a declaration: a plain assignment, an array element, a
    /// field, a return and an argument. Each was CS0031/CS0221/CS1503 on C#, 300 on JavaScript and
    /// 44 on C++. They are asserted together because they are one rule; splitting them would
    /// suggest five independent fixes.
    /// </summary>
    [Test]
    public void EveryOtherStoreSite_IsChecked()
    {
        var errors = Analyze("""
            Class Box
             Public F As Byte
            End Class

            Module M
             Function Give() As Byte
              Return 300
             End Function
             Sub Take(v As Byte)
              PrintLine("arg=" & CStr(v))
             End Sub
             Sub Main()
              Dim b As Byte = 0
              b = 300
              Dim a(3) As Byte
              a(0) = 300
              Dim x As New Box()
              x.F = 300
              Take(300)
              PrintLine(CStr(Give()))
             End Sub
            End Module
            """);

        var refusals = errors.Where(e => e.Contains("Constant expression not representable")).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(5),
                "one per site — assignment, array element, field, return, argument:\n"
                + string.Join("\n", errors));
            Assert.That(refusals, Has.Some.Contains("the returned value is 300"));
            Assert.That(refusals, Has.Some.Contains("argument 1 is 300"));
            Assert.That(refusals.Count(e => e.Contains("the assigned value is 300")), Is.EqualTo(3),
                "the variable, the array element and the field are three separate stores");
        });
    }

    // ====================================================================================
    // Single — a range check, not a precision one.
    // ====================================================================================

    /// <summary>
    /// ⛔ <c>Dim s As Single = 1.0E+40</c> has no Single to round to. Measured before: JavaScript
    /// printed <b>Infinity</b>, and the C++ backend emitted <c>s = Infinityf;</c>, which does not
    /// compile ("use of undeclared identifier 'Infinityf'").
    ///
    /// <para>⚠ Losing PRECISION is not losing the value: <c>Dim s As Single = 0.1</c> is legal VB
    /// and stays legal. A check written against representability-as-written rather than range
    /// would refuse it.</para>
    /// </summary>
    [TestCase("1.0E+40", false)]
    [TestCase("-1.0E+40", false)]
    [TestCase("0.1", true)]
    [TestCase("3.0E+38", true)]
    public void SingleIsCheckedForRange_NotForPrecision(string literal, bool fits)
    {
        var errors = Analyze($"""
            Module M
             Sub Main()
              Dim s As Single = {literal}
             End Sub
            End Module
            """);

        Assert.That(errors.Any(e => e.Contains("Constant expression not representable")),
            Is.EqualTo(!fits), "errors: " + string.Join(" | ", errors));
    }

    /// <summary>
    /// ⚠ Double has room for the values that overflow Single, and is not checked at all.
    /// </summary>
    [Test]
    public void ADoubleTarget_IsLeftAlone()
    {
        Assert.That(Analyze("""
            Module M
             Sub Main()
              Dim d As Double = 1.0E+300
             End Sub
            End Module
            """),
            Is.Empty);
    }

    /// <summary>
    /// ⚠ The guard that keeps this away from everything that is not a number: a String target and
    /// an Object target (where 300 boxes, and boxing has no range).
    /// </summary>
    [Test]
    public void ANonNumericTarget_IsLeftAlone()
    {
        Assert.That(Analyze("""
            Module M
             Sub Main()
              Dim s As String = "hi"
              Dim o As Object = 300
              PrintLine(s & CStr(o))
             End Sub
            End Module
            """),
            Is.Empty);
    }

    // ====================================================================================
    // The positive side: an in-range program still reaches every backend.
    // ====================================================================================

    /// <summary>
    /// ⚠ The check is a refusal, so the thing it can break is a program that should COMPILE. This
    /// runs one: every boundary value the table admits, through the backends that execute here.
    ///
    /// <para>⛔ The C# leg is Roslyn in-process rather than a run, because that is the assertion
    /// this defect needs — the old out-of-range output was not merely wrong, it did not build, and
    /// the BasicLang build reports success either way since it only writes the source.</para>
    ///
    /// <para>⛔ The SByte locals are not called <c>neg</c>/<c>pos</c>, and that is not style: a
    /// local named <c>neg</c> emits <c>[2] int8 neg</c> and ilasm rejects it ("syntax error at
    /// token 'neg'"), because <c>neg</c> is an IL instruction. A pre-existing MSIL gap — the
    /// backend does not escape a local whose name is an IL keyword — unrelated to constant
    /// ranges and not fixed here.</para>
    ///
    /// <para>⚠ 255.4 is deliberately NOT asserted as a VALUE here. A narrow local still carries
    /// its Double constant on JavaScript (<c>TryConvertConstant</c> declines Byte on purpose —
    /// see <c>docs/HANDOFF.md</c>), so JS prints 255.4 where C++ prints 255. That is a separate,
    /// pre-existing gap; what this pins is that the program is still accepted.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnInRangeProgram_StillBuildsAndRuns()
    {
        const string program = """
            Module M
             Sub Main()
              Dim lo As Byte = 0
              Dim hi As Byte = 255
              Dim minS As SByte = -128
              Dim maxS As SByte = 127
              Dim big As Integer = 2147483647
              PrintLine(CStr(lo) & "," & CStr(hi) & "," & CStr(minS) & "," & CStr(maxS) & "," & CStr(big))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program),
                Is.EqualTo("0,255,-128,127,2147483647"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program),
                Is.EqualTo("0,255,-128,127,2147483647\n"));
        });
    }

    // ====================================================================================
    // Helper.
    // ====================================================================================

    /// <summary>
    /// The analyzer's errors for one program, through the shared helper so this fixture cannot
    /// drift from the other diagnostic fixtures about what counts as an error.
    /// </summary>
    private static string[] Analyze(string source) => OptionalConstructorTests.Analyze(source);
}
