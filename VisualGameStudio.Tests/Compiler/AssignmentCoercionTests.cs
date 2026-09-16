using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The store half of the numeric coercion <see cref="ReturnCoercionTests"/> covers for returns.
///
/// <para>⛔ It was characterized separately rather than assumed to mirror the return case, and it
/// does not: a return is ONE IR node, a store is four target kinds plus the declaration site, and
/// what each backend did was different again. Measured for
/// <c>Dim d As Integer = 7 / 2</c>, <c>e = 7 / 2</c>, <c>a(0) = 7 / 2</c> and a module-level
/// <c>G = 7 / 2</c>:</para>
///
/// <list type="bullet">
/// <item><b>C#</b> — five separate CS0266s. Does not build.</item>
/// <item><b>MSIL</b> — <c>dim=1074528256 asn=0 arr=0 glob=0</c> and then a <b>SEGFAULT</b>: raw
/// float64 bit patterns read as int32.</item>
/// <item><b>JavaScript</b> — <c>3.5</c> at every one of the four sites.</item>
/// <item><b>C++</b> — right at all four, by narrowing implicitly.</item>
/// </list>
///
/// <para>⚠ The declared type has to come from the TARGET NODE. <c>GetOrCreateVariable</c> is
/// handed <c>value.Type</c>, and <c>TryRenameToVariable</c> then renames the Double temp to the
/// target outright, so the local's declared Integer never enters the picture at all.</para>
/// </summary>
[TestFixture]
public class AssignmentCoercionTests
{
    /// <summary>
    /// Four store sites in one program: a declaration with an initializer, a plain assignment, an
    /// array element, and a module-level variable. They are asserted together because they share
    /// one insertion point — splitting them would suggest four independent fixes.
    /// </summary>
    private const string FourSitesProgram = """
        Module M
         Dim G As Integer
         Sub Main()
          Dim d As Integer = 7 / 2
          Dim e As Integer
          e = 7 / 2
          Dim a(3) As Integer
          a(0) = 7 / 2
          G = 7 / 2
          PrintLine("dim=" & CStr(d) & " asn=" & CStr(e) & " arr=" & CStr(a(0)) & " glob=" & CStr(G))
         End Sub
        End Module
        """;

    /// <summary>
    /// ⛔ The C# half did not build — five CS0266s. Roslyn in-process is what catches that; the
    /// BasicLang build reports success either way, because it only writes the source.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_Compiles_AtEveryStoreSite()
    {
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(FourSitesProgram), Is.Empty,
            "every CS0266 here is one store site still missing its conversion");
    }

    /// <summary>
    /// ⛔ JavaScript printed <c>3.5</c> at all four sites — it has no types to disagree about, so
    /// nothing narrowed unless the cast does.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void JavaScript_NarrowsAtEveryStoreSite()
    {
        Assert.That(JavaScriptExecutionTests.RunJs(FourSitesProgram),
            Is.EqualTo("dim=3 asn=3 arr=3 glob=3"));
    }

    /// <summary>
    /// ⛔ MSIL printed <c>dim=1074528256 asn=0 arr=0 glob=0</c> and then SEGFAULTED — the float64
    /// bit pattern stored straight into an int32 slot. This is the loudest of the four backends
    /// and the only one that took the process down.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void Msil_NarrowsAtEveryStoreSite_InsteadOfSegfaulting()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess(FourSitesProgram),
            Is.EqualTo("dim=3 asn=3 arr=3 glob=3\n"));
    }

    /// <summary>
    /// A field of a user class — the fourth target kind, split out because MSIL cannot run it for
    /// an unrelated reason (<c>MissingFieldException: Box.F</c>, a pre-existing field-emission gap
    /// that has nothing to do with coercion; asserting MSIL here would pin someone else's defect).
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AClassFieldStore_IsNarrowed()
    {
        const string program = """
            Class Box
             Public F As Integer
            End Class

            Module M
             Sub Main()
              Dim b As New Box()
              b.F = 7 / 2
              PrintLine(CStr(b.F))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("3"));
        });
    }

    /// <summary>
    /// ⛔ <c>n /= 4</c> needed a SECOND fix, and the coercion alone did nothing for it. The
    /// compound path typed its <c>IRBinaryOp</c> from the TARGET (<c>currentValue.Type</c>), so
    /// the result claimed Integer, the coercion saw no mismatch — and the optimizer then
    /// constant-folded Integer 10 ÷ 4 to the <b>Double</b> 2.5. C# emitted <c>n = 2.5;</c>
    /// (CS0266) and JavaScript printed 2.5 from a variable declared <c>As Integer</c>.
    ///
    /// <para>VB's <c>/=</c> is floating division exactly as <c>/</c> is, so the operands are
    /// widened the same way <c>WidenDivisionOperand</c> does for the binary form — a Double-typed
    /// result over two Integer operands still divides as integers on the C-family backends. The
    /// narrowing back to Integer is then the ordinary store coercion.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void CompoundDivideAssign_DividesInFloatingPointThenNarrows()
    {
        const string program = """
            Module M
             Sub Main()
              Dim n As Integer = 10
              n /= 4
              PrintLine(CStr(n))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty,
                "n = 2.5 assigned to an int is CS0266");
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("2"),
                "10 / 4 is 2.5 in floating point, narrowed to 2 — not 2.5, and not integer-divided");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("2\n"));
        });
    }

    /// <summary>
    /// ⚠ The widening is for <c>/=</c> ALONE. Every other compound operator on two Integers stays
    /// integral, and a version of this change that widened "compound assignment" generally would
    /// silently route <c>*=</c>, <c>+=</c> and <c>-=</c> through Double.
    ///
    /// <para>⛔ The first version of this test used <c>\=</c>, on the theory that integer division
    /// was the counter-case to pin. It is not reachable: <c>n \= 2</c> does not PARSE ("Unexpected
    /// token in expression: '\'"), even though <c>IRBuilder</c>'s compound-operator switch has a
    /// <c>\=</c> → <c>IntDiv</c> case. That case is dead until the parser learns the operator, so
    /// the exclusion cannot be exercised and is expressed as a positive condition
    /// (<c>op == BinaryOpKind.Div</c>) rather than a branch nothing reaches.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void OtherCompoundOperators_StayIntegral()
    {
        const string program = """
            Module M
             Sub Main()
              Dim a As Integer = 7
              a *= 2
              Dim b As Integer = 7
              b += 2
              Dim c As Integer = 7
              c -= 2
              PrintLine(CStr(a) & "," & CStr(b) & "," & CStr(c))
             End Sub
            End Module
            """;

        // ⛔ The VALUES alone do not discriminate: integer arithmetic is exact in a Double, so
        // `a *= 2` gives 14 whether or not it detoured through floating point. The difference is
        // structural, and it costs more than tidiness — measured with the guard widened to every
        // operator, `a = 14;` becomes `a = (int)((double)(a) * (double)(2));`, so CONSTANT FOLDING
        // is lost and an exact integer multiply becomes a run-time Double round trip.
        var csharp = ReturnCoercionTests.EmitCSharpForTest(program);

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("14,9,5"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("14,9,5\n"));
            Assert.That(csharp, Does.Not.Contain("(double)"),
                "no compound operator other than /= may route Integer operands through Double:\n"
                + csharp);
        });
    }

    /// <summary>
    /// ⚠ A WIDENING store must keep working — the coercion is not narrowing-only, and typing it
    /// that way would leave `Dim w As Double = 7` holding an Integer on the backends that care.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AWideningStore_StillWorks()
    {
        const string program = """
            Module M
             Sub Main()
              Dim w As Double = 7
              PrintLine(CStr(w / 2))
             End Sub
            End Module
            """;

        Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("3.5"),
            "7 stored as a Double, so the division is floating — 3.5, not 3");
    }

    /// <summary>
    /// ⚠ A numeric LITERAL is re-typed at compile time rather than wrapped in a cast, and the
    /// narrowing TRUNCATES — <c>Dim a As Integer = 7.9</c> is 7 on all three runnable backends.
    ///
    /// <para>⛔ Truncating is chosen to match what the RUN-TIME cast does. Rounding the literal
    /// instead would make this 8 while the same value reaching the same variable through a
    /// variable stayed 7 — the constant-folded and non-folded paths of one expression giving
    /// different answers, which is worse than either answer on its own. (VB itself would say 8,
    /// for the same banker's-rounding reason
    /// <see cref="ReturnCoercionTests.TheBackendsAgreeOnTruncation_WhichIsNotYetVbsBankersRounding"/>
    /// records; that is the one decision, taken once, across the whole narrowing surface.)</para>
    ///
    /// <para>The widening half is load-bearing, not cosmetic: measured on the previous commit,
    /// <c>Dim c As Double = 7</c> on MSIL stored the int32 bit pattern into a float64 slot and
    /// <c>c + 0.5</c> printed <b>3.5E-323</b>.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ANumericLiteral_IsRetypedInPlace_AndNarrowsByTruncating()
    {
        const string program = """
            Module M
             Sub Main()
              Dim a As Integer = 7.9
              Dim b As Integer = 7.1
              Dim c As Double = 7
              PrintLine(CStr(a) & "," & CStr(b) & "," & CStr(c + 0.5))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7,7,7.5"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("7,7,7.5\n"),
                "7.5 and not 3.5E-323 — the widened literal is a real Double");
        });
    }

    /// <summary>
    /// ⚠ The guard, at a store rather than a return: a non-numeric target must not acquire a
    /// numeric cast. JavaScript is where this is observable, because a <c>Bitcast</c> has no
    /// numeric lowering there and the build fails outright.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ANonNumericStore_IsLeftAlone()
    {
        const string program = """
            Module M
             Sub Main()
              Dim s As String = "hi"
              Dim o As Object = 7
              s = "there"
              PrintLine(s & "," & CStr(o))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("there,7"));
        });
    }
}
