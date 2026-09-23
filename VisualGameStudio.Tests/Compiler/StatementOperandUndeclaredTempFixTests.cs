using System;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Pins step 2 of the uncommitted fix set: the four <c>CSharpBackend</c> sites that used to call
/// <c>GetValueName</c> directly on a STATEMENT operand — <c>Visit(IRArrayStore)</c>,
/// <c>Visit(IRIndexerStore)</c>, <c>Visit(IRYield)</c> and <c>Visit(IRForEach)</c>'s
/// <c>forEach.Collection</c> — now call <c>EmitExpression</c> directly instead.
///
/// <para>⛔ <b>The bug, and why every shape here was CS0103 before.</b> <c>GetValueName</c> is only
/// valid for a value already materialised as a DECLARED local. An inlined temp (exactly one use,
/// per the use-count census step 1 made total) has no declaration, so <c>For Each n In Make()</c>,
/// <c>l(Idx()) = ...</c>, <c>Yield Tag()</c> and an array-literal element that is a call each
/// emitted a bare, undeclared <c>tN</c>. <c>EmitExpression</c> instead regenerates the
/// call/expression text in place at each of the four sites, unconditionally.</para>
///
/// <para>⭐ <b>REVISION (task #125 investigation).</b> An intermediate version of this fix routed
/// these four sites through a small wrapper, <c>EmitOperand</c>, that special-cased a value
/// already named after a DECLARED variable (<c>GetValueName</c>, not re-evaluated) versus an
/// inlined temp (<c>EmitExpression</c>). That wrapper is GONE — deleted, not merely bypassed —
/// because the special case was wrong in the OPPOSITE direction from what it looked like it was
/// guarding against: <see cref="TheNamedBranch_WouldHaveReadACseMergedVariable_AfterItWasReassigned"/>
/// measures a shape (<c>C4</c>) where honouring a value's declared name at these four sites reads
/// a variable the OPTIMIZER's common-subexpression-elimination pass had already MERGED two
/// distinct expressions onto and the program had since REASSIGNED — printing <c>0</c> where
/// <c>3</c> is correct. <c>EmitExpression</c> unconditionally, exactly like every other operand
/// consumer (a call argument, an <c>IRStore</c>), does not have this hazard: it always regenerates
/// the expression text at the point of use rather than trusting a name that may have drifted.</para>
///
/// <para>⚠ Every program below prints a side-effecting <c>Console.WriteLine</c> inside the
/// function supplying the operand, so a shape that silently evaluates the call an EXTRA time
/// (rather than merely failing to compile) is visible in the transcript, not just in the final
/// value — the discriminator CLAUDE.md and docs/HANDOFF.md call out repeatedly for this family.</para>
///
/// <para>⚠ Each shape's backend set matches what was independently measured for it — several of
/// these programs hit OTHER, unrelated, pre-existing gaps on one backend (a foreign array-literal
/// lowering gap on JavaScript, a nested-generic unbox gap on MSIL); those are named in the shape's
/// own comment and are not this family's to fix or paper over.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class StatementOperandUndeclaredTempFixTests
{
    // ====================================================================================
    // S2 — For Each over a call's result (IRForEach.Collection). All four backends agree.
    // ====================================================================================

    [Test]
    public void ForEachOverACallsResult()
        => FourBackends.RunsOnEveryBackend(
            "Function Make() As List(Of Integer)\n" +
            " Console.WriteLine(\"make\")\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Return l\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In Make()\n" +
            "  total = total + n\n" +
            " Next\n" +
            " Console.WriteLine(total)\n" +
            "End Sub",
            "make\n7");

    [Test]
    public void ForEachOverACallsResult_Aggressive()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Function Make() As List(Of Integer)\n" +
            " Console.WriteLine(\"make\")\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Return l\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In Make()\n" +
            "  total = total + n\n" +
            " Next\n" +
            " Console.WriteLine(total)\n" +
            "End Sub",
            "make\n7");

    // ====================================================================================
    // S3a — an IRIndexerStore whose INDEX is a call's result (l(Idx()) = ...). All four agree.
    // ====================================================================================

    [Test]
    public void IndexerStore_IndexIsACallsResult()
        => FourBackends.RunsOnEveryBackend(
            "Function Idx() As Integer\n" +
            " Console.WriteLine(\"idx\")\n" +
            " Return 1\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " l(Idx()) = l(1) * 10\n" +
            " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "idx\n2,30");

    /// <summary>
    /// S3b — the SAME index-is-a-call shape, but the call runs once PER ITERATION inside a
    /// <c>For Each</c> — the discriminator for a fix that only worked for a call evaluated once.
    /// "idx" must appear exactly twice (once per element of <c>other</c>), never a third time.
    /// </summary>
    [Test]
    public void IndexerStore_IndexIsACallsResult_InsideAForEach()
        => FourBackends.RunsOnEveryBackend(
            "Function Idx() As Integer\n" +
            " Console.WriteLine(\"idx\")\n" +
            " Return 1\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim other As New List(Of Integer)()\n" +
            " other.Add(5)\n" +
            " other.Add(7)\n" +
            " For Each k In other\n" +
            "  l(Idx()) = l(1) + k\n" +
            " Next\n" +
            " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "idx\nidx\n2,15");

    /// <summary>S3c — read-modify-write through a VARIABLE index: <c>l(i) = l(i) * 10</c>. The
    /// VALUE operand is a BinaryOp result (a temp), not a call, and was CS0103 before this fix
    /// too — the defect is not specific to calls, any un-materialised temp triggers it.</summary>
    [Test]
    public void IndexerStore_ReadModifyWrite_ThroughAVariableIndex()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim i As Integer = 1\n" +
            " l(i) = l(i) * 10\n" +
            " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "2,30");

    /// <summary>S3d — the same read-modify-write shape through a Dictionary, inside a counted
    /// <c>For</c> loop (three iterations, each re-reading and re-writing the same key).</summary>
    [Test]
    public void IndexerStore_ReadModifyWrite_OnADictionary_InsideACountedLoop()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim d As New Dictionary(Of String, Integer)()\n" +
            " d.Add(\"a\", 5)\n" +
            " Dim i As Integer = 0\n" +
            " For i = 1 To 3\n" +
            "  d(\"a\") = d(\"a\") + 1\n" +
            " Next\n" +
            " Console.WriteLine(d(\"a\"))\n" +
            "End Sub",
            "8");

    // ====================================================================================
    // G5 — an array LITERAL whose first element is a call ({Tag(), 2}), an IRArrayStore-adjacent
    // shape through array-literal lowering. ⚠ JavaScript is NOT part of the oracle: a separate,
    // pre-existing gap ("IRArrayAlloc lowering is not implemented yet" — a BL70xx candidate, not
    // this family's) refuses the program outright on that backend alone; C#, C++ and MSIL agree.
    // ====================================================================================

    [Test]
    public void ArrayLiteral_FirstElementIsACall()
    {
        const string program =
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim a() As Integer = {Tag(), 2}\n" +
            " Console.WriteLine(a(0) + a(1))\n" +
            "End Sub";
        const string expected = "tag\n5";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    // ====================================================================================
    // R1 — `t` is seeded from a call, reassigned (`t = t + 5`), and then read TWICE as a store
    // operand (`l(0) = t`, `l(1) = t + Seed(10)`). "seed" must print exactly TWICE total — once
    // for the initial Seed(1), once for Seed(10) — never a third time. `t` is a plain DECLARED
    // variable here (not a raw CSE-merged temp), so EmitExpression's IRVariable arm resolves it
    // by name either way and this shape does NOT exercise the C4 hazard below — it pins the
    // ORIGINAL CS0103 defect this family fixes, nothing about the deleted EmitOperand wrapper.
    // All four backends agree.
    // ====================================================================================

    [Test]
    public void ArrayStoreValue_IsADeclaredVariableReassignedFromACall()
        => FourBackends.RunsOnEveryBackend(
            "Function Seed(v As Integer) As Integer\n" +
            " Console.WriteLine(\"seed\")\n" +
            " Return v\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(0)\n" +
            " l.Add(0)\n" +
            " Dim t As Integer = Seed(1)\n" +
            " t = t + 5\n" +
            " l(0) = t\n" +
            " l(1) = t + Seed(10)\n" +
            " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "seed\nseed\n6,16");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_ArrayStoreValue_IsADeclaredVariableReassignedFromACall")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_ArrayStoreValue_IsADeclaredVariableReassignedFromACall")]
    public void BothCompilerEntryPoints_ArrayStoreValue(bool asProject)
    {
        const string source =
            "Function Seed(v As Integer) As Integer\n" +
            " Console.WriteLine(\"seed\")\n" +
            " Return v\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(0)\n" +
            " l.Add(0)\n" +
            " Dim t As Integer = Seed(1)\n" +
            " t = t + 5\n" +
            " l(0) = t\n" +
            " l(1) = t + Seed(10)\n" +
            " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub";
        const string expected = "seed\nseed\n6,16";

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_StatementOperandEntry_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, source);

            var compiler = new BasicLang.Compiler.BasicCompiler(new BasicLang.Compiler.CompilerOptions());
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR))),
                Is.EqualTo(expected),
                (asProject ? "CompileProjectFiles" : "CompileFile") + " printed the wrong answer.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ====================================================================================
    // R2 — nested For Each (List(Of List(Of Integer))) whose OUTER collection is a call's
    // result. ⚠ MSIL is NOT part of the oracle: a separate, pre-existing gap (a nested-generic
    // "unbox.any ... syntax error at token '<'") refuses this shape on MSIL alone; C#, C++ and
    // JavaScript agree.
    // ====================================================================================

    [Test]
    public void NestedForEach_OuterCollectionIsACallsResult()
    {
        const string program =
            "Function Make() As List(Of List(Of Integer))\n" +
            " Console.WriteLine(\"make\")\n" +
            " Dim outer As New List(Of List(Of Integer))()\n" +
            " Dim a As New List(Of Integer)()\n" +
            " a.Add(1)\n" +
            " a.Add(2)\n" +
            " Dim b As New List(Of Integer)()\n" +
            " b.Add(10)\n" +
            " outer.Add(a)\n" +
            " outer.Add(b)\n" +
            " Return outer\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim s As Integer = 0\n" +
            " For Each row In Make()\n" +
            "  For Each v In row\n" +
            "   s = s + v\n" +
            "  Next\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            "End Sub";
        const string expected = "make\n13";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
        });
    }

    // ====================================================================================
    // R3 — three shapes at once: a Dictionary read-modify-write from a call
    // (d(k) = d(k) + Tag(4)), plus an array literal whose elements are calls, ONE of which is
    // itself multiplied ({Tag(1), Tag(2) * 3}). ⚠ JavaScript is NOT part of the oracle: the same
    // array-literal lowering gap as G5. C#, C++ and MSIL agree.
    // ====================================================================================

    [Test]
    public void DictionaryReadModifyWriteFromACall_AndAnArrayLiteralOfCalls()
    {
        const string program =
            "Function Tag(v As Integer) As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return v\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim d As New Dictionary(Of String, Integer)()\n" +
            " d.Add(\"a\", 1)\n" +
            " Dim k As String = \"a\"\n" +
            " d(k) = d(k) + Tag(4)\n" +
            " Dim arr() As Integer = {Tag(1), Tag(2) * 3}\n" +
            " Console.WriteLine(CStr(d(\"a\")) & \",\" & CStr(arr(0) + arr(1)))\n" +
            "End Sub";
        const string expected = "tag\ntag\ntag\n5,7";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    // ====================================================================================
    // L1 — a class method's `For Each d In items.Select(...)`: the Select result is a temp read
    // by IRForEach.Collection inside a CLASS METHOD BODY, not Main. Already the full-Program/
    // Expected shape promoted onto JavaScriptMethodLambdaTests.LambdaInsideAMethod_TheMethodKeepsItsOwnBody_CSharp
    // (C#) and _JavaScript (the JS leg); not duplicated here — see that fixture for this exact
    // program and its "added\n6\n2\n4\n6" expectation.
    // ====================================================================================

    // ====================================================================================
    // C4 — ⭐ task #125. THE test that kills re-introducing a "prefer the declared name" guard
    // (the deleted EmitOperand) at these four sites. `p`/`q` are seeded from calls; `a = p + q`
    // and `l(0) = p + q` are the SAME textual expression at TWO of these sites — the shape a
    // guard would treat as "already named" (multi-use, so CSE merges them onto one shared temp)
    // and read back BY NAME. `a` is then REASSIGNED (`a = Seed(0)`) before `l(0) = p + q` runs.
    //
    // ⛔ MEASURED WITH THE GUARD (an EmitOperand equivalent restored): `l(0)` printed <c>0</c>
    // — CSE's merged temp, read by name, now carrying `a`'s reassigned value — where <c>3</c>
    // (the correct sum of `p`+`q`, recomputed) is right. MEASURED ON THE CURRENT TREE (EmitExpression
    // unconditionally): C# prints the correct `3,0` on every entry point, because EmitExpression
    // recomputes `p + q` from its operands at each of the two sites rather than trusting a name a
    // COMMON-SUBEXPRESSION-ELIMINATION merge may have made stale.
    //
    // ⚠ C++, JavaScript (through EITHER optimizing pipeline) and MSIL are NOT fixed by this —
    // measured wrong (`0,0`) on all three, because THEIR OWN store-operand codegen still reads the
    // CSE-merged, since-reassigned temp by name. This is task #125,
    // CommonSubexpressionEliminationPass merging two structurally-identical pure expressions
    // (`p + q`) without accounting for an intervening reassignment of the variable one of them
    // feeds — an optimizer defect, not this family's, and NOT fixed here. Pinned as KNOWN-WRONG
    // below so the pin flips LOUDLY (a test starts FAILING, not silently passing) the day #125 is
    // fixed — see LoopPassesDisabledTests / InductionVariableDisabledTests for this repo's existing
    // "still broken, pinned" convention.
    // ====================================================================================

    private const string C4 =
        "Function Seed(v As Integer) As Integer\n" +
        " Console.WriteLine(\"seed\")\n" +
        " Return v\n" +
        "End Function\n\n" +
        "Sub Main()\n" +
        " Dim p As Integer = Seed(1)\n" +
        " Dim q As Integer = Seed(2)\n" +
        " Dim l As New List(Of Integer)()\n" +
        " l.Add(0)\n" +
        " Dim a As Integer = p + q\n" +
        " a = Seed(0)\n" +
        " l(0) = p + q\n" +
        " Console.WriteLine(CStr(l(0)) & \",\" & CStr(a))\n" +
        "End Sub";

    private const string C4Correct = "seed\nseed\nseed\n3,0";

    /// <summary>⭐ THE MUTANT KILL for "re-add a declared-name-preferring guard at these four
    /// sites". Asserted as the LITERAL correct value, not cross-backend agreement — the other
    /// three backends are, separately, pinned WRONG below (task #125).</summary>
    [Test]
    public void TheNamedBranch_WouldHaveReadACseMergedVariable_AfterItWasReassigned()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(C4)), Is.EqualTo(C4Correct),
            "C#, standard pipeline — must recompute `p + q` at the l(0) store rather than reading "
            + "a CSE-merged, since-reassigned name. If this fails with '0,0' instead of '3,0', a "
            + "guard that prefers a declared name over re-evaluating the expression has come back "
            + "at one of Visit(IRArrayStore)/Visit(IRIndexerStore)/Visit(IRYield)/Visit(IRForEach).");

    [Test]
    public void TheNamedBranch_WouldHaveReadACseMergedVariable_AfterItWasReassigned_Aggressive()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(C4)), Is.EqualTo(C4Correct),
            "C#, aggressive pipeline — same reasoning, same kill.");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_TheNamedBranch_WouldHaveReadACseMergedVariable")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_TheNamedBranch_WouldHaveReadACseMergedVariable")]
    public void BothCompilerEntryPoints_TheNamedBranch_WouldHaveReadACseMergedVariable(bool asProject)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_C4Entry_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, C4);

            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR))),
                Is.EqualTo(C4Correct),
                (asProject ? "CompileProjectFiles" : "CompileFile") + " printed the wrong answer.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    /// <summary>
    /// ⛔ PINNED WRONG — task #125, <c>CommonSubexpressionEliminationPass</c>. C++ merges
    /// `p + q` at `Dim a` and at `l(0) = p + q` onto one shared value, and its OWN store-operand
    /// codegen reads that merged value back by name — after `a` was reassigned to `Seed(0)`'s
    /// result. MEASURED: <c>0,0</c>, not the correct <c>3,0</c>. Not this family's to fix; if
    /// someone fixes #125 this test goes RED — that is progress, not a regression: update or
    /// delete the pin, per this repo's "still broken, pinned" convention
    /// (see <c>LoopPassesDisabledTests</c> / <c>InductionVariableDisabledTests</c>).
    /// </summary>
    [Test]
    public void CppStillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(C4))), Is.EqualTo("seed\nseed\nseed\n0,0"),
            "if this changed, task #125 (CommonSubexpressionEliminationPass merging p+q across a "
            + "reassignment) may be fixed — update or delete this pin, do not just widen it");

    /// <summary>⛔ PINNED WRONG — task #125, same mechanism as C++. JavaScript ONLY under an
    /// OPTIMIZING pipeline (CSE is a standard pass); the plain, non-optimizing
    /// <c>JavaScriptExecutionTests.RunJs</c> leg runs no CSE at all and prints the correct `3,0`
    /// by having nothing to merge — that is NOT evidence #125 is fixed on JS, it is evidence this
    /// one leg never exercises the optimizer.</summary>
    [Test]
    public void JavaScriptOptimizedStillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(C4)), Is.EqualTo("seed\nseed\nseed\n0,0"),
            "if this changed, task #125 may be fixed on the JS backend's optimizing path — update "
            + "or delete this pin, do not just widen it");

    /// <summary>⛔ PINNED WRONG — task #125, same mechanism. MSIL runs its optimizer by default in
    /// both entry points this suite uses (docs/HANDOFF.md), so no separate "optimized" leg is
    /// needed to observe it.</summary>
    [Test]
    public void MsilStillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(C4)), Is.EqualTo("seed\nseed\nseed\n0,0"),
            "if this changed, task #125 may be fixed on MSIL — update or delete this pin, do not "
            + "just widen it");

    // ⚠ C6 (adds `Console.WriteLine(p + q)` before the final print, and stores a LITERAL `0` at
    // l(0) instead of `p + q`) was considered and NOT added: it does not touch any of the four
    // EmitOperand-era sites at all (its only IRArrayStore.Value is the constant 0, which always
    // takes the EmitConstant branch regardless of the guard; its extra print is an ordinary call
    // argument, which was never routed through EmitOperand/GetValueName either). It would not be
    // a distinct kill of the guard-reintroduction mutant — C4 already is.
}
