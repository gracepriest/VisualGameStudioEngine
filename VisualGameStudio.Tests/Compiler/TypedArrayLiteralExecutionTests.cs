using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 24a, Task 4 — `New T() { … }` RUNS on all three backends (spec §9, "Lowering" and the
/// per-backend rows). Before this fixture the JavaScript backend had no array-literal lowering at
/// all (`Visit(IRArrayAlloc)` / `Visit(IRArrayStore)` threw NotYet, and the alloca that an
/// array-typed local's initializer stores through had no expression arm), and the C# backend
/// rendered a store's non-literal value BY NAME, so the `IRCast` Task 3 wraps a non-literal element
/// in came out as an undeclared temp (CS0103).
/// </summary>
[TestFixture]
[Category("Integration")]
public class TypedArrayLiteralExecutionTests
{
    private static void RequireNode()
    {
        // ⛔ FAIL loudly, never Assert.Ignore: these are the first tests of a backend arm that has
        // never run, and a pass-by-absence is what this project calls a false green.
        Assert.That(BasicLang.Runtime.NodeLocator.Find(), Is.Not.Null, "node is required for this row");
    }

    private const string SumTyped = "Sub Main()\nDim a() As Integer = New Integer() {1, 2, 3}\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";
    private const string SumBare  = "Sub Main()\nDim a() As Integer = {1, 2, 3}\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";
    // ⚠ The Double program prints the NUMBER ALONE. The C++ backend DELIBERATELY refuses floating-point
    // string concatenation (CppCodeGenerator.cs — `StringifyForText` has no Single/Double arm;
    // `"SUM " & aDouble` becomes `std::string("SUM ") + aDouble`, the intended build break),
    // while its print arm formats a Double .NET-style (CppBclEndToEndTests → `19.99`).
    // C# prints `3` for 3.0 and node prints `3`, so one expected string serves all three.
    // ⛔ `i` is a PARAMETER, never `Dim i As Integer = 2`. The optimizer folds IRCast(IRConstant)
    // (IROptimizer.cs) and ReplaceUses reaches IRArrayStore.Value, so a constant `i` folds the cast
    // away and every optimized row goes green WITHOUT ever emitting a cast into a store — the shape
    // §9 mandates and the C# backend could not render. A same-file bare `Sub` called from `Main`
    // works on all three backends (the cross-FILE call is the broken one).
    // ⚠ NOT `Sub Run`: `Run(exePath As String, arguments As String)` is a BasicLang BUILTIN, and the
    // analyzer refuses `Run(2)` with "expects 2 argument(s), got 1" before any backend is reached
    // (measured; see BuiltinCollisionTests for the general trap).
    private const string SumDouble = "Sub Main()\nSumFrom(2)\nEnd Sub\nSub SumFrom(i As Integer)\nDim a() As Double = New Double() {1, i}\nDim total As Double = 0\nFor Each x As Double In a\ntotal = total + x\nNext\nConsole.WriteLine(total)\nEnd Sub";
    // ⛔ A CALL as an element. On the C# backend an element's use was never COUNTED
    // (`GetOperands` had no IRArrayStore arm), so `Bump()` had use-count 0, was emitted as a bare
    // statement for its side effect, and the store then re-rendered it inline: measured through
    // the real CLI, `Bump(); Bump(); var t2 = new int[2]; t2[0] = Bump(); t2[1] = Bump();` —
    // a green build that calls Bump FOUR times. Before Task 4 the same program was CS0103
    // (`t2[0] = t0;`, loud); Task 4's EmitExpression turned it silent. The counter is a
    // top-level Dim (measured: `private static int counter = 0;` on C#).
    private const string BumpTwice = "Dim counter As Integer = 0\nSub Main()\nDim a() As Integer = New Integer() {Bump(), Bump()}\nConsole.WriteLine(\"N \" & counter)\nEnd Sub\nFunction Bump() As Integer\ncounter = counter + 1\nReturn counter\nEnd Function";
    // The cast-in-store's twin on Visit(IRIndexerStore): IRBuilder coerces the value before every
    // assignment-target arm, so `l(0) = i` into a List(Of Double) carries an IRCast, which the
    // indexer store must render as an expression (measured: `l[0] = (double)(i);`).
    private const string IndexerStoreCast = "Sub Main()\nPut(2)\nEnd Sub\nSub Put(i As Integer)\nDim l As New List(Of Double)()\nl.Add(0)\nl(0) = i\nConsole.WriteLine(l(0))\nEnd Sub";
    // The M4 shape — the literal passed straight to a call, AFTER its stores — is the stated reason
    // the JS `Expr` arm answers the BOUND name (measured emission: `const t0 = new Array(2);
    // t0[0] = 1; t0[1] = 2; Show(t0);`). ⚠ The callee is declared BEFORE Main, and its parameter
    // uses the bracket spelling `a[] As Integer` (the only one that parses): measured, a callee
    // declared AFTER the call site has its array parameter typed as plain `Integer` at that call
    // ("cannot convert from 'Integer[]' to 'Integer'") for a literal AND a variable argument alike —
    // a front-end declaration-order defect, not this row's subject.
    private const string SumViaCall = "Sub Show(a[] As Integer)\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub\nSub Main()\nShow(New Integer() {1, 2})\nEnd Sub";

    [TestCase(SumTyped, "SUM 6"), TestCase(SumBare, "SUM 6"), TestCase(SumDouble, "3"), TestCase(BumpTwice, "N 2"), TestCase(SumViaCall, "SUM 3")]
    public void JavaScript_RunsTheLiteral(string source, string expected)
    {
        RequireNode();
        // ⛔ BOTH routes. `RunJs` compiles through `JsTestSupport.Compile`, which runs NO optimizer pass,
        // while every shipping route runs `AddStandardPasses()` unconditionally (JsTestSupport.cs
        // says so in its own doc); the new `Expr` arm's `Bound()` branch depends on the alloc
        // still sitting in block.Instructions after those passes, which only the optimized run shows
        // (CLAUDE.md: never the non-optimizing helper alone).
        Assert.That(JavaScriptExecutionTests.RunJs(source), Is.EqualTo(expected), "non-optimized");
        Assert.That(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(source)).Trim(),
            Is.EqualTo(expected), "optimized — the IR a user gets");
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "3"), TestCase(BumpTwice, "N 2"), TestCase(IndexerStoreCast, "2"), TestCase(SumViaCall, "SUM 3")]
    public void CSharp_RunsTheLiteral(string source, string expected)
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(source);   // the real CLI, optimizer on
        Assert.That(CSharpRun.CompileAndRun(csharp).Trim(), Is.EqualTo(expected));
    }

    /// <summary>
    /// The C# backend rendered a store's non-literal value BY NAME (<c>t1[1] = t0;</c>, with the
    /// cast temp <c>t0</c> declared nowhere — CS0103, measured through the real CLI). The stored
    /// element must be the cast EXPRESSION. Measured rendering: <c>(double)(i)</c>.
    /// </summary>
    [Test]
    public void CSharp_Emission_CastsTheStoredNonLiteral()
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(SumDouble);   // the real CLI, optimizer on

        Assert.That(csharp, Does.Contain("[1] = (double)(i);"),
            "the non-literal element must be stored through its cast, not a temp name.\n--- generated C# ---\n" + csharp);
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "3"), TestCase(BumpTwice, "N 2"), TestCase(SumViaCall, "SUM 3")]
    public void Cpp_RunsTheLiteral(string source, string expected)
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Trim(), Is.EqualTo(expected));
    }
}
