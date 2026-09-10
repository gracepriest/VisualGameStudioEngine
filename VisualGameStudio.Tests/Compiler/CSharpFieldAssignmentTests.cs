using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An UNQUALIFIED field assignment inside a class method — <c>Total = Total + x</c> — on the
/// C# backend.
///
/// <para>⛔ <b>MEASURED: the statement was DROPPED.</b> For
/// <c>Public Sub Bump(x As Integer) : Total = Total + x : End Sub</c> the C# backend emitted
/// <c>public void Bump(int x) { }</c> — an empty body — from a build that reported success,
/// while C++ emitted <c>Total = Total + x;</c> and JavaScript <c>this.Total = …</c>.</para>
///
/// <para>Why: IRBuilder lowers the assignment as an IRBinaryOp RENAMED to <c>Total</c> (no
/// separate IRAssignment), and the backend decides "is this value a statement?" with
/// <c>IsNamedDestination</c>, which knew parameters, locals and globals — never the class's
/// own fields. A field-named result therefore looked like an unused SSA temp and was skipped.
/// The same skip emptied every lambda body that assigned a captured local or a field
/// (<see cref="JavaScriptMethodLambdaTests"/>, <see cref="SubLambdaStatementTests"/>).</para>
///
/// <para>Found while proving the lambda-in-method IR fix cross-backend; the C# execution legs
/// of those fixtures printed <c>0</c> for an accumulator that C++ and JS both summed.</para>
/// </summary>
[TestFixture]
public class CSharpFieldAssignmentCodeGenTests
{
    private const string Program =
        "Class Counter\nPublic Total As Integer\n" +
        "Public Sub Bump(x As Integer)\nTotal = Total + x\nEnd Sub\nEnd Class\n" +
        "Sub Main()\nDim c As New Counter()\nc.Bump(5)\nConsole.WriteLine(c.Total)\nEnd Sub";

    private static string Cs(string source) =>
        new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(source));

    [Test]
    public void UnqualifiedFieldAssignment_InAMethod_IsEmitted()
        => Assert.That(Cs(Program), Does.Contain("Total = Total + x;"),
            "the method body was emitted EMPTY — the field-named result was skipped as a temp");

    [Test]
    public void UnqualifiedFieldRead_InAMethod_IsEmitted()
        => Assert.That(Cs(
                "Class Counter\nPublic Total As Integer\n" +
                "Public Function Twice() As Integer\nReturn Total * 2\nEnd Function\nEnd Class\n" +
                "Sub Main()\nDim c As New Counter()\nConsole.WriteLine(c.Twice())\nEnd Sub"),
            Does.Contain("return Total * 2;"));

    /// <summary>A parameter or local of the same name still SHADOWS the field (it is checked first).</summary>
    [Test]
    public void ALocalNamedLikeAField_StillShadowsIt()
        => Assert.That(Cs(
                "Class Counter\nPublic Total As Integer\n" +
                "Public Sub Bump(Total As Integer)\nTotal = Total + 1\nEnd Sub\nEnd Class\n" +
                "Sub Main()\nDim c As New Counter()\nc.Bump(5)\nEnd Sub"),
            Does.Contain("Total = Total + 1;"));

    // ⚠ No inherited-field case: the ANALYZER rejects `Total = Total + x` in a derived class
    // when Total is declared on the base ("Arithmetic operator '+' requires numeric operands;
    // Cannot assign value of type 'Integer' to 'Total'") — a front-end limitation on every
    // backend, not a codegen one. The member-name walk here already covers bases for the day
    // the analyzer resolves them.

    // ---------------------------------------------------------------- review findings

    /// <summary>
    /// ⛔ A lambda assignment whose VALUE IS A CALL: IRBuilder renames the call to the target
    /// exactly as it renames a binary op, and the lambda emitter's named-destination branch
    /// excluded IRCall — so `Total = Add(Total, x)` emitted `Add(Total, x);` and never stored.
    /// </summary>
    [Test]
    public void LambdaAssignment_FromACall_IsEmitted()
        => Assert.That(Cs(
                "Class Counter\nPublic Total As Integer\n" +
                "Public Function Add(a As Integer, b As Integer) As Integer\nReturn a + b\nEnd Function\n" +
                "Public Sub AddAll(items As List(Of Integer))\nitems.ForEach(Sub(x As Integer) Total = Add(Total, x))\nEnd Sub\n" +
                "End Class\nSub Main()\nEnd Sub"),
            Does.Contain("Total = Add(Total, x);"));

    /// <summary>
    /// ⛔ A member named like an SSA temp (`t0`): the member-name arm of IsNamedDestination matched
    /// every temp of that name, so an ordinary intermediate `t0 = n * 2` was emitted as a write to
    /// the field, and a call temp was emitted as `t0 = Bump();` and then inlined AGAIN at its use.
    /// Only a value IRBuilder actually renamed after a variable is a destination.
    /// </summary>
    [Test]
    public void AMemberNamedLikeATemp_IsNotClobberedByTemps()
    {
        var cs = Cs(
            "Class Timer\nPublic t0 As Integer\nPrivate calls As Integer\n" +
            "Public Function Twice(n As Integer) As Integer\nReturn n * 2\nEnd Function\n" +
            "Public Function Bump() As Integer\ncalls = calls + 1\nReturn calls\nEnd Function\n" +
            "Public Function Bumps() As Integer\nReturn Bump() + 1\nEnd Function\n" +
            "End Class\nSub Main()\nEnd Sub");

        Assert.That(cs, Does.Not.Contain("t0 = n * 2;"), "a temp clobbered the field");
        Assert.That(cs, Does.Not.Contain("t0 = Bump();"), "a call temp was evaluated once as a field write and again at its use");
    }
}

/// <summary>And it runs, through the CLI — the shipping route.</summary>
[TestFixture]
[Category("Integration")]   // builds and runs with dotnet
public class CSharpFieldAssignmentExecutionTests
{
    [Test]
    public void UnqualifiedFieldAssignment_ActuallyMutatesTheField()
        => Assert.That(CliTestHarness.CompileRunCSharp(
                "Class Counter\nPublic Total As Integer\n" +
                "Public Sub Bump(x As Integer)\nTotal = Total + x\nEnd Sub\nEnd Class\n" +
                "Sub Main()\nDim c As New Counter()\nc.Bump(5)\nc.Bump(2)\nConsole.WriteLine(c.Total)\nEnd Sub").Trim(),
            Is.EqualTo("7"));

    [Test]
    public void LambdaAssignment_FromACall_ActuallyStores()
        => Assert.That(CliTestHarness.CompileRunCSharp(
                "Class Counter\nPublic Total As Integer\n" +
                "Public Function Add(a As Integer, b As Integer) As Integer\nReturn a + b\nEnd Function\n" +
                "Public Sub AddAll(items As List(Of Integer))\nitems.ForEach(Sub(x As Integer) Total = Add(Total, x))\nEnd Sub\n" +
                "End Class\n" +
                "Sub Main()\nDim c As New Counter()\nDim xs As New List(Of Integer)()\nxs.Add(1)\nxs.Add(2)\nxs.Add(3)\n" +
                "c.AddAll(xs)\nConsole.WriteLine(c.Total)\nEnd Sub").Trim(),
            Is.EqualTo("6"));

    [Test]
    public void AMemberNamedLikeATemp_IsNotClobbered_AndCallsRunOnce()
        => Assert.That(CliTestHarness.CompileRunCSharp(
                "Class Timer\nPublic t0 As Integer\nPrivate calls As Integer\n" +
                "Public Function Twice(n As Integer) As Integer\nReturn n * 2\nEnd Function\n" +
                "Public Function Bump() As Integer\ncalls = calls + 1\nReturn calls\nEnd Function\n" +
                "Public Function Bumps() As Integer\nReturn Bump() + 1\nEnd Function\n" +
                "End Class\n" +
                "Sub Main()\nDim t As New Timer()\nt.t0 = 100\nConsole.WriteLine(t.Twice(5))\nConsole.WriteLine(t.t0)\n" +
                "Console.WriteLine(t.Bumps())\nConsole.WriteLine(t.Bump())\nEnd Sub").Replace("\r\n", "\n").Trim(),
            Is.EqualTo("10\n100\n2\n2"));
}
