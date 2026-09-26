using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #168 — <c>For Each x In coll</c> with NO <c>As</c> clause, where <c>x</c> already names a
/// variable in scope, REUSES it (the owner's VB ruling; see
/// <c>docs/superpowers/decisions/0009-for-each-control-variable.md</c>). Every iteration assigns
/// the element to <c>x</c> through the ORDINARY assignment lowering, so coercion, field stores,
/// global stores and ByRef stores all come from that one path
/// (<c>SemanticAnalyzer.ForEachControlBinding</c> / <c>IRBuilder.Visit(ForEachLoopNode)</c>); after
/// the loop <c>x</c> holds the LAST element assigned (unchanged only when the collection is empty,
/// and holding the element being processed at an <c>Exit For</c>).
///
/// <para>Fixture names below echo the implementer's probe labels (<c>F#</c>/<c>G#</c>,
/// <c>S/t168/probes(2)/</c>) so the shape a test pins can be cross-checked against the measured
/// table in the ADR.</para>
///
/// <para>⚠ Kept to ONE shape per <see cref="FourBackends"/> call, matching the rest of the suite's
/// <c>For Each</c> fixtures.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // several legs redirect Console.Out
public class ForEachControlVariableReuseTests
{
    /// <summary>Analyze <paramref name="source"/> and return whether it refused to compile, plus
    /// every error message — front-end only, no backend involved.</summary>
    private static (bool HasErrors, string[] Messages) Analyze(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        return (!ok, analyzer.Errors.Select(e => e.Message).ToArray());
    }

    // ========================================================================================
    // REUSE — F1, F2, F6, F7, F8, F11, F12, F13, G1, G5, G16, G29, G4.
    // ========================================================================================

    /// <summary>F1 — a local, reused over a <c>List</c>. Measured wrong before task #168: 0.</summary>
    [Test]
    public void F1_LocalOverAList_IsReused()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim x As Integer = 0\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(5)\n" +
            " l.Add(9)\n" +
            " For Each x In l\n" +
            " Next\n" +
            " Console.WriteLine(x)\n" +
            "End Sub",
            "9");

    /// <summary>F2 — an ARRAY, and the body itself reads <c>x</c> (sums it). Measured wrong before
    /// task #168: "0,13" (the sum was right — the body's own per-iteration read always worked —
    /// only the POST-LOOP read of <c>x</c> was wrong, 0 instead of 2).</summary>
    [Test]
    public void F2_LocalOverAnArray_IsReused_AndTheBodyReadsItEachIteration()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 0\n" +
            "  Dim a() As Integer = {4, 7, 2}\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each x In a\n" +
            "   s = s + x\n" +
            "  Next\n" +
            "  PrintLine(CStr(x) & \",\" & CStr(s))\n" +
            " End Sub\n" +
            "End Module",
            "2,13");

    /// <summary>F6 — an Integer→Double conversion, from the ordinary assignment coercion (task
    /// #27/#28's uniform assignment coercion), not a special-cased store. Measured wrong before
    /// task #168: 0.75 (x stayed 0.5).</summary>
    [Test]
    public void F6_DoubleLocalOverIntegerElements_ConvertsThroughTheAssignmentCoercion()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Double = 0.5\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(3)\n" +
            "  l.Add(8)\n" +
            "  For Each x In l\n" +
            "  Next\n" +
            "  PrintLine(CStr(x + 0.25))\n" +
            " End Sub\n" +
            "End Module",
            "8.25");

    /// <summary>F7 — a MODULE GLOBAL, reused across a whole module procedure. Measured wrong
    /// before task #168: 0.</summary>
    [Test]
    public void F7_ModuleGlobal_IsReused()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Dim G As Integer = 0\n\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  For Each G In l\n" +
            "  Next\n" +
            "  PrintLine(CStr(G))\n" +
            " End Sub\n" +
            "End Module",
            "9");

    /// <summary>F8 — a CLASS FIELD, read bare inside one of the class's own methods. Measured
    /// wrong before task #168: 0. Proves the field store comes from the ordinary assignment
    /// lowering (<c>IRFieldStore</c>), not a store path IRBuilder had to special-case for
    /// <c>ForEachControlBinding</c>.</summary>
    [Test]
    public void F8_ClassField_IsReused()
        => FourBackends.RunsOnEveryBackend(
            "Class Box\n" +
            " Public K As Integer\n\n" +
            " Sub Walk(l As List(Of Integer))\n" +
            "  For Each K In l\n" +
            "  Next\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim b As New Box()\n" +
            "  b.Walk(l)\n" +
            "  PrintLine(CStr(b.K))\n" +
            " End Sub\n" +
            "End Module",
            "9");

    /// <summary>F11 — <c>Exit For</c> mid-loop. VB rule: <c>x</c> holds the element being
    /// processed AT the exit, since the element reaches <c>x</c> BEFORE any statement of that
    /// iteration runs (IRBuilder visits the reuse assignment before <c>node.Body.Accept</c>).
    /// Measured wrong before task #168: 0.</summary>
    [Test]
    public void F11_ExitFor_LeavesTheReusedVariableAtTheElementBeingProcessed()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 0\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  l.Add(12)\n" +
            "  For Each x In l\n" +
            "   If x > 6 Then Exit For\n" +
            "  Next\n" +
            "  PrintLine(CStr(x))\n" +
            " End Sub\n" +
            "End Module",
            "9");

    /// <summary>F12 — a BY-VALUE parameter, reused. Measured wrong before task #168: 1 (the
    /// parameter's own initial value, untouched).</summary>
    [Test]
    public void F12_ByValueParameter_IsReused()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Walk(x As Integer, l As List(Of Integer))\n" +
            "  For Each x In l\n" +
            "  Next\n" +
            "  PrintLine(CStr(x))\n" +
            " End Sub\n\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Walk(1, l)\n" +
            " End Sub\n" +
            "End Module",
            "9");

    /// <summary>F13 — the OPTIMIZER shape: a value read both BEFORE and AFTER the loop
    /// (<c>a = x + q</c> then <c>b = x + q</c>), which is exactly what CopyPropagation must NOT
    /// fold across the loop as if <c>x</c> were unchanged. Measured wrong before task #168: "3,3"
    /// (CopyPropagation folded the second read to the FIRST's value, since nothing in the
    /// pre-#168 IR ever wrote `x` at all). Run through the aggressive pipeline on every backend
    /// that can run it, so this exercises the optimizer, not only the non-optimizing helper.</summary>
    [Test]
    public void F13_TheReuseAssignment_BlocksCopyPropagationAcrossTheLoop()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 1\n" +
            "  Dim q As Integer = 2\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim a As Integer = x + q\n" +
            "  For Each x In l\n" +
            "  Next\n" +
            "  Dim b As Integer = x + q\n" +
            "  PrintLine(CStr(a) & \",\" & CStr(b))\n" +
            " End Sub\n" +
            "End Module",
            "3,11");

    /// <summary>G1 — NESTED loops, each reusing a DIFFERENT pre-existing variable (no name
    /// collision between them, so no BC30069). The outer body reads the inner's control variable
    /// AFTER the inner loop closes, proving the inner reuse's last-assigned value survives past
    /// its own <c>Next</c>.</summary>
    [Test]
    public void G1_NestedLoops_EachReusesADifferentPreExistingVariable()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 0\n" +
            "  Dim outer As New List(Of Integer)()\n" +
            "  outer.Add(1)\n" +
            "  outer.Add(2)\n" +
            "  Dim inner As New List(Of Integer)()\n" +
            "  inner.Add(10)\n" +
            "  inner.Add(20)\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each y In outer\n" +
            "   For Each x In inner\n" +
            "    s = s + x * y\n" +
            "   Next\n" +
            "   s = s + x\n" +
            "  Next\n" +
            "  PrintLine(CStr(x) & \",\" & CStr(s))\n" +
            " End Sub\n" +
            "End Module",
            "20,130");

    /// <summary>G5 — the body both READS and WRITES the reused variable. The write must be
    /// visible to the NEXT statement in the same iteration, and the READ each iteration must see
    /// the just-assigned element, not a stale value from the previous one.</summary>
    [Test]
    public void G5_TheBody_ReadsAndWritesTheReusedVariable()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 100\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each x In l\n" +
            "   s = s + x * 10\n" +
            "   x = x + 1\n" +
            "   s = s + x\n" +
            "  Next\n" +
            "  PrintLine(CStr(x) & \",\" & CStr(s))\n" +
            " End Sub\n" +
            "End Module",
            "10,156");

    /// <summary>G16 — a field INHERITED from a base class, read bare in a DERIVED class's own
    /// method. Reuse must resolve through <c>ResolveClassMember</c>'s base-class walk, the same
    /// chain a bare read of the field already uses.</summary>
    [Test]
    public void G16_InheritedField_IsReused()
        => FourBackends.RunsOnEveryBackend(
            "Class Base\n" +
            " Public K As Integer\n" +
            "End Class\n\n" +
            "Class Derived\n" +
            " Inherits Base\n" +
            " Sub Walk(l As List(Of Integer))\n" +
            "  For Each K In l\n" +
            "  Next\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim d As New Derived()\n" +
            "  d.Walk(l)\n" +
            "  PrintLine(CStr(d.K))\n" +
            " End Sub\n" +
            "End Module",
            "9");

    /// <summary>G29 — a NARROWING reuse (<c>Integer</c> local over <c>Double</c> elements) must
    /// round the SAME way the ordinary narrowing assignment <c>x = d</c> does — proved by
    /// comparing the loop's result against two plain assignments doing the identical narrowing
    /// (3.5 and 2.7, both rounded the ordinary way) side by side in the same program.</summary>
    [Test]
    public void G29_NarrowingReuse_RoundsTheSameWayAnOrdinaryNarrowingAssignmentDoes()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 0\n" +
            "  Dim l As New List(Of Double)()\n" +
            "  l.Add(3.5)\n" +
            "  l.Add(2.7)\n" +
            "  For Each x In l\n" +
            "  Next\n" +
            "  Dim w As Integer = 0\n" +
            "  Dim l2 As New List(Of Double)()\n" +
            "  l2.Add(3.5)\n" +
            "  For Each w In l2\n" +
            "  Next\n" +
            "  Dim d As Double = 2.7\n" +
            "  Dim y As Integer = 0\n" +
            "  y = d\n" +
            "  Dim e As Double = 3.5\n" +
            "  Dim v As Integer = 0\n" +
            "  v = e\n" +
            "  PrintLine(CStr(x) & \",\" & CStr(y) & \",\" & CStr(w) & \",\" & CStr(v))\n" +
            " End Sub\n" +
            "End Module",
            "3,3,4,4");

    /// <summary>G4 — a BYREF parameter, reused: the caller's own argument must see the last
    /// element. Runs on C#, C++ and MSIL, which is every backend that supports ByRef at all —
    /// JavaScript refuses a ByRef parameter by design (BL7002: "JavaScript has no reference
    /// parameters"), independent of task #168, so it is not asked here.</summary>
    [Test]
    public void G4_ByRefParameter_IsReused_OnEveryBackendThatSupportsByRef()
    {
        const string program =
            "Module M\n" +
            " Sub Walk(ByRef x As Integer, l As List(Of Integer))\n" +
            "  For Each x In l\n" +
            "  Next\n" +
            " End Sub\n\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim v As Integer = 1\n" +
            "  Walk(v, l)\n" +
            "  PrintLine(CStr(v))\n" +
            " End Sub\n" +
            "End Module";
        const string expected = "9";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    // ========================================================================================
    // CONTROLS — unchanged by task #168: F3, F4, G8, G11, G12.
    // ========================================================================================

    /// <summary>F3 — an EMPTY collection: nothing is ever assigned, so the pre-existing local is
    /// left exactly as it was (42), on every backend and both before and after task #168.</summary>
    [Test]
    public void F3_EmptyCollection_LeavesThePreExistingVariableUntouched()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 42\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  For Each x In l\n" +
            "  Next\n" +
            "  PrintLine(CStr(x))\n" +
            " End Sub\n" +
            "End Module",
            "42");

    /// <summary>F4 — both DECLARING forms untouched by task #168: <c>For Each y As Integer</c>
    /// (explicit type) and <c>For Each z</c> naming nothing already in scope. Neither reuses
    /// anything, so both still declare their own loop-scoped variable exactly as before.</summary>
    [Test]
    public void F4_DeclaringForms_AreUnaffected()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each y As Integer In l\n" +
            "   s = s + y\n" +
            "  Next\n" +
            "  For Each z In l\n" +
            "   s = s + z\n" +
            "  Next\n" +
            "  PrintLine(CStr(s))\n" +
            " End Sub\n" +
            "End Module",
            "28");

    /// <summary>G8 — <c>For Each x As Integer</c> STILL SHADOWS an existing <c>x</c> (42) — task
    /// #168 does not touch a declaring <c>As T</c> loop at all, and does not add VB's BC30616.
    /// The outer <c>x</c> (42) is untouched by the loop; the loop's OWN sum is 14.</summary>
    [Test]
    public void G8_ExplicitAsT_StillShadowsAnExistingVariable()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim x As Integer = 42\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each x As Integer In l\n" +
            "   s = s + x\n" +
            "  Next\n" +
            "  PrintLine(CStr(x) & \",\" & CStr(s))\n" +
            " End Sub\n" +
            "End Module",
            "42,14");

    /// <summary>G11 — a bare <c>For Each Foo</c> where <c>Foo</c> resolves only to a METHOD (a
    /// deliberate departure from strict VB: BasicLang flattens every procedure into global scope,
    /// stdlib names like <c>Val</c> included, so refusing a method name here would break ordinary
    /// programs — <c>CppCollectionTests.Cpp_DictionaryOperations_CompileAndRun</c>'s <c>For Each
    /// val In d.Values</c> is exactly this shape). A method name still DECLARES a new loop
    /// variable, as before task #168.</summary>
    [Test]
    public void G11_BareForEach_NamingAMethod_StillDeclares()
        => FourBackends.RunsOnEveryBackend(
            "Module M\n" +
            " Function Foo() As Integer\n" +
            "  Return 1\n" +
            " End Function\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  For Each Foo In l\n" +
            "  Next\n" +
            "  PrintLine(CStr(Foo()))\n" +
            " End Sub\n" +
            "End Module",
            "1");

    /// <summary>G12 — a bare <c>For Each Widget</c> where <c>Widget</c> resolves only to a TYPE —
    /// VB's own rule (Roslyn declares a fresh local when a name binds only to a type). Still
    /// DECLARES a new loop variable, as before task #168.</summary>
    [Test]
    public void G12_BareForEach_NamingAType_StillDeclares()
        => FourBackends.RunsOnEveryBackend(
            "Class Widget\n" +
            "End Class\n" +
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each Widget In l\n" +
            "   s = s + 1\n" +
            "  Next\n" +
            "  PrintLine(CStr(s))\n" +
            " End Sub\n" +
            "End Module",
            "1");

}

/// <summary>
/// Task #168, front-end-only half: DIAGNOSTICS (G9 Const, G10 property, G34 event, G2/G35 — an
/// enclosing loop's own control variable, VB's BC30069) plus the IR-LEVEL shape (F1: the built
/// <c>IRForEach</c> iterates a hidden name, and the body begins with the store to <c>x</c>).
///
/// <para>Deliberately its OWN fixture, with no <see cref="Category"/> attribute: every test here
/// runs the analyzer (and, for the IR-level one, <c>IRBuilder</c>) directly, with no backend
/// compiled or run — fast, and part of the non-Integration subset
/// (<c>--filter "TestCategory!=Integration"</c>), unlike <see cref="ForEachControlVariableReuseTests"/>.</para>
/// </summary>
[TestFixture]
public class ForEachControlVariableDiagnosticsTests
{
    /// <summary>Analyze <paramref name="source"/> and return whether it refused to compile, plus
    /// every error message — front-end only, no backend involved.</summary>
    private static (bool HasErrors, string[] Messages) Analyze(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        return (!ok, analyzer.Errors.Select(e => e.Message).ToArray());
    }

    [Test]
    public void G9_ConstantControlVariable_IsRefused()
    {
        var (hasErrors, messages) = Analyze(
            "Module M\n" +
            " Const K As Integer = 3\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  For Each K In l\n" +
            "  Next\n" +
            "  PrintLine(CStr(K))\n" +
            " End Sub\n" +
            "End Module");

        Assert.That(hasErrors, Is.True, "a constant control variable must be refused, not silently shadowed");
        Assert.That(messages, Has.Some.Contains(
            "'K' is a constant and cannot be used as a For Each control variable. " +
            "Use a variable, or declare a new one with 'For Each K As <type>'"),
            string.Join(" | ", messages));
    }

    [Test]
    public void G10_PropertyControlVariable_IsRefused()
    {
        var (hasErrors, messages) = Analyze(
            "Class Box\n" +
            " Public Property P As Integer\n\n" +
            " Sub Walk(l As List(Of Integer))\n" +
            "  For Each P In l\n" +
            "  Next\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  Dim b As New Box()\n" +
            "  b.Walk(l)\n" +
            "  PrintLine(CStr(b.P))\n" +
            " End Sub\n" +
            "End Module");

        Assert.That(hasErrors, Is.True, "a property control variable must be refused, not silently shadowed");
        Assert.That(messages, Has.Some.Contains(
            "'P' is a property and cannot be used as a For Each control variable. " +
            "Use a variable, or declare a new one with 'For Each P As <type>'"),
            string.Join(" | ", messages));
    }

    [Test]
    public void G34_EventControlVariable_IsRefused()
    {
        var (hasErrors, messages) = Analyze(
            "Class Box\n" +
            " Public Event Changed(v As Integer)\n" +
            " Sub Walk(l As List(Of Integer))\n" +
            "  For Each Changed In l\n" +
            "  Next\n" +
            " End Sub\n" +
            "End Class\n" +
            "Module M\n" +
            " Sub Main()\n" +
            "  PrintLine(\"x\")\n" +
            " End Sub\n" +
            "End Module");

        Assert.That(hasErrors, Is.True, "an event control variable must be refused, not silently shadowed");
        Assert.That(messages, Has.Some.Contains(
            "'Changed' is an event and cannot be used as a For Each control variable. " +
            "Use a variable, or declare a new one with 'For Each Changed As <type>'"),
            string.Join(" | ", messages));
    }

    /// <summary>G2 — a NESTED <c>For Each a</c> reusing the OUTER <c>For Each a</c>'s own control
    /// variable. VB's BC30069: the outer loop already emits <c>a</c> as ITS OWN iteration
    /// variable on every backend, so a nested store into it does not compile (C#'s CS1656) or
    /// does not run (JavaScript's "Assignment to constant") once reuse actually writes to it.</summary>
    [Test]
    public void G2_ReusingAnEnclosingForEachsControlVariable_IsRefused_BC30069()
    {
        var (hasErrors, messages) = Analyze(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim outer As New List(Of Integer)()\n" +
            "  outer.Add(1)\n" +
            "  outer.Add(2)\n" +
            "  Dim inner As New List(Of Integer)()\n" +
            "  inner.Add(10)\n" +
            "  inner.Add(20)\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each a In outer\n" +
            "   s = s + a\n" +
            "   For Each a In inner\n" +
            "    s = s + a\n" +
            "   Next\n" +
            "   s = s + a\n" +
            "  Next\n" +
            "  PrintLine(CStr(s))\n" +
            " End Sub\n" +
            "End Module");

        Assert.That(hasErrors, Is.True);
        Assert.That(messages, Has.Some.Contains(
            "For Each control variable 'a' is already in use by an enclosing For or For Each loop"),
            string.Join(" | ", messages));
    }

    /// <summary>G35 — the SAME BC30069 rule, but the enclosing loop is a COUNTED <c>For i</c>, not
    /// a <c>For Each</c>. The numeric <c>For</c> already reused an existing <c>i</c> before task
    /// #168 (it is what proved BasicLang's own scoping disagreed with itself, per the ADR) — a
    /// nested <c>For Each i</c> reusing THAT <c>i</c> is refused the identical way.</summary>
    [Test]
    public void G35_ReusingAnEnclosingCountedForsControlVariable_IsRefused_BC30069()
    {
        var (hasErrors, messages) = Analyze(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  l.Add(9)\n" +
            "  Dim s As Integer = 0\n" +
            "  For i = 1 To 2\n" +
            "   For Each i In l\n" +
            "    s = s + i\n" +
            "   Next\n" +
            "  Next\n" +
            "  PrintLine(CStr(s))\n" +
            " End Sub\n" +
            "End Module");

        Assert.That(hasErrors, Is.True);
        Assert.That(messages, Has.Some.Contains(
            "For Each control variable 'i' is already in use by an enclosing For or For Each loop"),
            string.Join(" | ", messages));
    }

    /// <summary>No error for the ordinary REUSE form (F1's shape) — the diagnostics above must
    /// not have widened to reject every bare <c>For Each</c> over an existing variable.</summary>
    [Test]
    public void ReuseForm_ReportsNoError()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " Dim x As Integer = 0\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(5)\n" +
            " For Each x In l\n" +
            " Next\n" +
            " Console.WriteLine(x)\n" +
            "End Sub");

        Assert.That(hasErrors, Is.False, string.Join(" | ", messages));
    }

    /// <summary>No error for the ordinary DECLARING form (F4's shape) — <c>As T</c> and a bare
    /// name resolving to nothing must both keep compiling cleanly.</summary>
    [Test]
    public void DeclareForms_ReportNoError()
    {
        var (hasErrors, messages) = Analyze(
            "Module M\n" +
            " Sub Main()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(5)\n" +
            "  For Each y As Integer In l\n" +
            "  Next\n" +
            "  For Each z In l\n" +
            "  Next\n" +
            " End Sub\n" +
            "End Module");

        Assert.That(hasErrors, Is.False, string.Join(" | ", messages));
    }

    // ========================================================================================
    // IR LEVEL — F1: the built IRForEach iterates a HIDDEN name, never `x` itself, and the loop
    // body's FIRST instruction is the assignment that stores the hidden element into `x`.
    // Structural, not a name-spelling pin, except for the hidden-name CONVENTION itself, which
    // the commit documents explicitly (the `__with` / `__lambda_N` family).
    // ========================================================================================

    [Test]
    public void F1_IRForEach_IteratesAHiddenVariable_AndTheBodyBeginsWithTheStoreToX()
    {
        const string source =
            "Sub Main()\n" +
            " Dim x As Integer = 0\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(5)\n" +
            " l.Add(9)\n" +
            " For Each x In l\n" +
            " Next\n" +
            " Console.WriteLine(x)\n" +
            "End Sub";

        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");

        var main = irModule.Functions.Single(f => string.Equals(f.Name, "Main", StringComparison.OrdinalIgnoreCase));
        var forEach = main.Blocks.SelectMany(b => b.Instructions).OfType<IRForEach>().Single();

        Assert.That(string.Equals(forEach.VariableName, "x", StringComparison.OrdinalIgnoreCase), Is.False,
            "the loop must iterate a HIDDEN element variable, not the reused control variable 'x' " +
            "directly — got: " + forEach.VariableName);
        Assert.That(forEach.VariableName, Does.StartWith("__foreach_"),
            "the hidden element variable follows the __with / __lambda_N convention — got: " + forEach.VariableName);

        var first = forEach.BodyBlock.Instructions.FirstOrDefault();
        var store = first as IRAssignment;
        Assert.That(store, Is.Not.Null,
            "the loop body must BEGIN with the assignment that stores the hidden element into 'x' — got: " + first);
        Assert.That(store!.Target.Name, Is.EqualTo("x").IgnoreCase,
            "the body's first instruction must target 'x' — got: " + store.Target.Name);
        Assert.That(store.Value, Is.Not.SameAs(store.Target),
            "the assignment's value must come from the hidden element, not from 'x' itself");
    }
}
