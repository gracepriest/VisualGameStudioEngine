using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan review CRITICAL / S10 — BasicLang follows VB, and the C# backend diverged on four Decimal rules: a narrowing
/// truncated (EmitCastText rounded only Double/Single), CShort/CByte of an out-of-range Decimal did not throw,
/// boxed-Decimal `=` compared references (spec D19 says values — owner-approved), and CType of a boxed Integer to
/// Decimal threw InvalidCastException. Expectations are VB's, written here; the backend is fixed to meet them.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CSharpDecimalVbRulesTests
{
    private CultureInfo _saved = CultureInfo.CurrentCulture;
    [SetUp] public void Invariant() { _saved = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }
    [TearDown] public void Restore() => CultureInfo.CurrentCulture = _saved;

    private const string Id = "Function Id(d As Decimal) As Decimal\n Return d\nEnd Function\n";

    private static string Overflow(string expr) =>
        $" Try\n  Console.WriteLine({expr})\n Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n End Try\n";

    [Test]
    public void E1_NarrowingRoundsHalfToEven() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id +
        "Sub Main()\n Console.WriteLine(CType(Id(2.5D), Integer))\n Console.WriteLine(CType(Id(3.5D), Integer))\n" +
        " Console.WriteLine(CType(Id(-2.5D), Integer))\n" +
        // NOTE: `Dim j As Integer = Id(3.5D)` was dropped from the plan's row: the analyzer REFUSES an
        // implicit Decimal -> Integer narrowing ("Use CInt() for explicit conversion") on every target,
        // so it never reaches a backend. Explicit CType is the measured path (plan Task 20A Step 1 note).
        Overflow("CType(Id(2147483648D), Integer)") + "End Sub\n")),
        Is.EqualTo("2\n4\n-2\noverflow"));

    [Test]
    public void E4_CShortAndCByte_OutOfRange_Overflow() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id +
        "Sub Main()\n Console.WriteLine(CShort(Id(2.5D)))\n Console.WriteLine(CByte(Id(255.4D)))\n" +
        Overflow("CShort(Id(40000D))") + Overflow("CByte(Id(256D))") + Overflow("CByte(Id(-1D))") + "End Sub\n")),
        Is.EqualTo("2\n255\noverflow\noverflow\noverflow"));

    /// <summary>A boxed value the optimizer cannot see through — a constant `Dim o As Object = 5` is copy-propagated
    /// and FOLDED (measured on the spec base `14c2e17d`: `o = 5` emits `Console.WriteLine(true)`, and `o = 5.0` emits
    /// `Console.WriteLine(false)` where VB says True — a separate finding, recorded below).</summary>
    private const string Box = "Function Box(n As Integer) As Object\n Return n\nEnd Function\n";

    [Test]
    public void E2_E3_Boxing() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id + Box +
        "Public Class Thing\nEnd Class\n" +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n Console.WriteLine(o = p)\n Console.WriteLine(o <> p)\n" +
        " Dim i As Object = Box(5)\n Console.WriteLine(CType(i, Decimal))\n Dim five As Object = Id(5D)\n Console.WriteLine(i = five)\n" +
        " Console.WriteLine(i = Id(5D))\n" +
        " Dim t As Object = New Thing()\n Try\n  Console.WriteLine(CType(t, Decimal))\n Catch ex As InvalidCastException\n  Console.WriteLine(\"invalid cast\")\n End Try\n" +
        " Dim a As Object = New Thing()\n Dim b As Object = a\n Console.WriteLine(a = b)\nEnd Sub\n")),
        Is.EqualTo("True\nFalse\n5\nTrue\nTrue\ninvalid cast\nTrue"),
        "row 5 is Object vs a TYPED Decimal (the helper, value comparison); the last row: two references to one object stay equal");

    /// <summary>
    /// ⛔ Scope guard (plan re-review): an Object compared with a NON-Decimal primitive is emitted exactly as before.
    /// MEASURED on the spec base `14c2e17d` (CLI `--target=csharp`, `Dim o As Object = Box(5)`): the ANALYZER ACCEPTS all
    /// three shapes and emits `o == 5`, `o == 5.0`, `o == Convert.ToInt64(5)` — so the refusal is csc's CS0019, and it
    /// must stay csc's (never a silently-false comparison: two boxed ints are never reference-equal). Uses the
    /// non-asserting Roslyn compile (FourBackends.TryCompileCSharp) — an Assert inside RunEmittedCSharpText is
    /// recorded by NUnit even when caught.
    /// </summary>
    [TestCase("5")]
    [TestCase("5.0")]
    [TestCase("CLng(5)")]
    public void ObjectVersusAnotherPrimitive_IsStillRefusedByCsc(string other)
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Box +
            "Sub Main()\n Dim o As Object = Box(5)\n Console.WriteLine(o = " + other + ")\nEnd Sub\n");
        var diagnostics = FourBackends.TryCompileCSharp(cs);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Contain("__BlObjectEquals"));
            Assert.That(diagnostics.Any(d => d.Id == "CS0019"), Is.True,
                "csc still refuses it, as on the base:\n" + string.Join("\n", diagnostics));
        });
    }

    /// <summary>Two Objects holding DIFFERENT boxes of the same Integer: unchanged from today (reference compare → False).
    /// A known divergence from VB (True), recorded as the follow-up "VB value equality for boxed numbers in
    /// general" — pinned here so the Decimal widening cannot drift into it unnoticed.</summary>
    [Test]
    public void TwoObjectsHoldingEqualBoxedIntegers_AreUnchanged() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(
        "Function Box(n As Integer) As Object\n Return n\nEnd Function\n" +
        "Sub Main()\n Dim a As Object = Box(5)\n Dim b As Object = Box(5)\n Console.WriteLine(a = b)\nEnd Sub\n")),
        Is.EqualTo("False"));

    /// <summary>
    /// Mutation guard for the <c>Visit(IRCompare)</c> render site: a comparison bound to a NAMED temp
    /// bypasses the inline renderer. Both the Object/Object case (reference compare, pinned above) and
    /// the Object/Decimal case (value compare, spec D19) must agree through this site too.
    /// </summary>
    [Test]
    public void E2_NamedTemp_ComparesBoxedDecimalsByValue() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id + Box +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n" +
        " Dim eq As Boolean = (o = p)\n Dim ne As Boolean = (o <> p)\n Console.WriteLine(eq)\n Console.WriteLine(ne)\n" +
        " Dim x As Object = Box(5)\n Dim y As Object = Box(5)\n Dim same As Boolean = (x = y)\n Console.WriteLine(same)\nEnd Sub\n")),
        Is.EqualTo("True\nFalse\nFalse"));

    /// <summary>The helper is private to the class that uses it, so a comparison inside a USER class needs
    /// its own copy — and it must still compare boxed Decimals by value.</summary>
    [Test]
    public void E2_ComparisonInsideAUserClass_ComparesByValue() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(
        "Public Class Thing\n Public Shared Function Same(a As Object, b As Object) As Boolean\n  Return a = b\n End Function\nEnd Class\n" +
        "Sub Main()\n Console.WriteLine(Thing.Same(1.10D, 1.1D))\n Console.WriteLine(Thing.Same(1, 1))\nEnd Sub\n")),
        Is.EqualTo("True\nFalse"));
}
