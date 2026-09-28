using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Pins step 6 of the (now committed) fix set: <c>CSharpBackend.Visit(IRForEach)</c> gives a
/// colliding <c>For Each</c> variable a FRESH, scope-safe C# name for its body
/// (<c>ForEachVariableCollides</c> / <c>FreshForEachVariableName</c>), and restores the name's
/// outer meaning once the body closes.
///
/// <para>⛔⛔ <b>REVERSED BY TASK #168 — read this before touching a shape in this file.</b> Every
/// test below used to exercise a bare <c>For Each x In coll</c> where <c>x</c> ALREADY named an
/// outer local, parameter, module global or class field, and asserted that BasicLang's OWN scoping
/// SHADOWED it: the loop got a fresh, body-scoped <c>x</c>, and <c>x</c> read back its OUTER value
/// once the loop closed. <c>docs/superpowers/decisions/0009-for-each-control-variable.md</c>
/// reverses that: the owner ruled for VB's rule, and a bare <c>For Each x</c> over an EXISTING
/// variable now REUSES it — every iteration assigns the element to it, and after the loop it holds
/// the LAST element assigned (unchanged only when the collection is empty). Every value in this
/// file that used to read "back to the outer value" now reads "the last element the loop assigned."
/// See the ADR for the F-probe table this reverses.</para>
///
/// <para>⭐ <b>What SURVIVES from step 6, and why this file is not deleted.</b> The
/// <c>ForEachVariableCollides</c> / <c>FreshForEachVariableName</c> rename machinery this family
/// was written for is untouched — it is simply no longer REACHED by a bare <c>For Each x</c>, which
/// now lowers to a hidden iteration variable plus an ordinary assignment (never a C# <c>foreach
/// (T x …)</c> header that could collide with anything). It is still reached by <c>For Each x As T
/// In coll</c>: that form ALWAYS declares its own loop-scoped <c>x</c> (task #168 does not touch it,
/// and does not add VB's BC30616), so it can still shadow an outer <c>x</c> and still needs the
/// rename to avoid C#'s CS0136. Every shape here that existed to pin the rename, and not the (now
/// reversed) reuse question, keeps doing that with an explicit <c>As T</c> — see
/// <see cref="OuterLoopVariablesPlainName_IsTakenForAnInnerLoopsFreshName"/>.</para>
///
/// <para>⚠ Kept to ONE shape per <see cref="FourBackends"/> call (<c>Assert.Multiple</c> inside).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ForEachVariableRenameFixTests
{
    /// <summary>
    /// Analyze <paramref name="source"/> and return whether it refused to compile, plus every
    /// error message — the front-end-only path task #168's diagnostics use (no backend involved,
    /// so this is fast and works for every one of them identically).
    /// </summary>
    private static (bool HasErrors, string[] Messages) Analyze(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        return (!ok, analyzer.Errors.Select(e => e.Message).ToArray());
    }

    // ====================================================================================
    // S5 — the headline shape: `n` declared outside (99), then `For Each n In l` with NO `As`.
    // C# used to refuse this at CS0136 before step 6's rename; step 6 fixed that by shadowing.
    // Task #168 changes the ANSWER, not the mechanism: `n` no longer shadows — it is REUSED, so it
    // reads back the LAST element the loop assigned (3, the last of {1,2,3}), not 99.
    // ====================================================================================

    [Test]
    public void OuterLocalCollision_IsReused_AndHoldsTheLastElementAfterTheLoop()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim n As Integer = 99\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In l\n" +
            "  s = s + n\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(n)\n" +
            "End Sub",
            "6\n3"); // was "6\n99" before task #168

    [Test]
    public void OuterLocalCollision_IsReused_AndHoldsTheLastElementAfterTheLoop_Aggressive()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Sub Main()\n" +
            " Dim n As Integer = 99\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In l\n" +
            "  s = s + n\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(n)\n" +
            "End Sub",
            "6\n3"); // was "6\n99" before task #168

    /// <summary>
    /// S5b — NESTED loops sharing the SAME variable name, bare (no <c>As</c>). Before task #168
    /// this shadowed and the outer `n` survived the inner loop untouched (360, this test's OLD
    /// value). Task #168 makes reusing an ENCLOSING loop's own control variable an ERROR — VB's
    /// BC30069 — because that loop already emits `n` as ITS OWN iteration variable on every
    /// backend; a nested store into it does not compile (C#'s CS1656) or does not run (JavaScript's
    /// "Assignment to constant") once reuse actually writes to it. See the commit message on
    /// <c>a454a8cf</c> and ADR-0009. <see cref="NestedForEach_SameVariableName_WithAsT_StillShadows"/>
    /// is the sibling that keeps this shape compiling and running, via <c>As T</c>.
    /// </summary>
    [Test]
    public void NestedForEach_SameVariableName_IsRefused_BC30069()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(1)\n" +
            " outer.Add(2)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(10)\n" +
            " inner.Add(20)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In outer\n" +
            "  For Each n In inner\n" +
            "   s = s + n\n" +
            "  Next\n" +
            "  s = s + n * 100\n" +
            " Next\n" +
            "End Sub");

        Assert.That(hasErrors, Is.True, "a nested For Each reusing an enclosing loop's own control variable must be refused");
        Assert.That(messages, Has.Some.Contains(
            "For Each control variable 'n' is already in use by an enclosing For or For Each loop"),
            string.Join(" | ", messages));
    }

    /// <summary>
    /// The <c>As T</c> sibling of <see cref="NestedForEach_SameVariableName_IsRefused_BC30069"/>:
    /// the SAME nested shape, but the inner loop declares its own <c>n</c> explicitly
    /// (<c>For Each n As Integer In inner</c>) rather than naming the outer's bare <c>n</c>. That
    /// bypasses reuse (and so bypasses BC30069) entirely — a declaring <c>For Each x As T</c> may
    /// still shadow, exactly as it always could (VB's BC30616 is deliberately not reported; see
    /// <see cref="OuterLoopVariablesPlainName_IsTakenForAnInnerLoopsFreshName"/> for why this needs
    /// the C# rename machinery). This reproduces this family's ORIGINAL numbers exactly — 360 —
    /// because a shadowing <c>As T</c> loop behaves exactly as it did before task #168.
    /// </summary>
    [Test]
    public void NestedForEach_SameVariableName_WithAsT_StillShadows()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(1)\n" +
            " outer.Add(2)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(10)\n" +
            " inner.Add(20)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In outer\n" +
            "  For Each n As Integer In inner\n" +
            "   s = s + n\n" +
            "  Next\n" +
            "  s = s + n * 100\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            "End Sub",
            "360");

    /// <summary>S5c — collision with a PARAMETER, not a local: `SumAll`'s own bare `For Each n In l`
    /// now REUSES its by-value parameter `n` (7). After the loop `n` holds the last element
    /// assigned (2, the last of {1,2}), not the original parameter value — `s * 1000 + n` =
    /// 3000 + 2 = 3002 (was 3007, `n` = 7, before task #168).</summary>
    [Test]
    public void ParameterCollision_ReusesTheParameter_WhichHoldsTheLastElementAfterTheLoop()
        => FourBackends.RunsOnEveryBackend(
            "Function SumAll(n As Integer, l As List(Of Integer)) As Integer\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In l\n" +
            "  s = s + n\n" +
            " Next\n" +
            " Return s * 1000 + n\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Console.WriteLine(SumAll(7, l))\n" +
            "End Sub",
            "3002"); // was "3007" before task #168

    /// <summary>
    /// ⭐ S5d — THE shape this family's docstring used to measure by number. `N` (capital) and the
    /// loop variable `n` (lowercase) are the SAME identifier under BasicLang's case-insensitivity,
    /// so a bare `For Each n` REUSES `N` (task #168 says reuse is resolved exactly like a bare read
    /// — <c>ResolveBareName</c> — which is case-insensitive). It also seeds a real user local
    /// literally named `n_1` (9), which is exactly the fresh name `FreshForEachVariableName` would
    /// have tried first under the OLD shadowing rename — irrelevant now, since a bare reuse never
    /// asks that machinery at all (it lowers to a hidden variable, never a C# `foreach (T n …)`
    /// header).
    ///
    /// <para>Loop sum is UNCHANGED by task #168 (43: n*n + n_1 for n in {3,4} → (9+9)+(16+9) =
    /// 18+25 = 43) because each iteration's <c>n</c> still reads that iteration's element. What
    /// changes is what <c>N + n_1</c> reads AFTER the loop: <c>N</c> is no longer untouched (14,
    /// this test's OLD value) — it holds the last element REUSE assigned, 4, so <c>N + n_1</c> =
    /// 4 + 9 = 13 (VB's own answer, measured on C# and MSIL).</para>
    ///
    /// <para>⛔ <b>Only TWO of the four backends can be asked here — task #124, pre-existing.</b>
    /// C++ does not case-fold identifiers at all: the store lowers to `n = …`, which C++ sees as an
    /// UNDECLARED identifier (`N` is what got declared) — a compile failure. JavaScript does not
    /// case-fold either, and under this harness's ES-module (strict-mode) semantics that is a
    /// RUNTIME <c>ReferenceError: n is not defined</c>, not a silently-wrong value — `n` was never
    /// `let`-declared (only `N` was), and strict mode refuses the implicit global a non-strict
    /// script would have created. (A plain `n = 5` assignment to a `Dim N` fails the identical way,
    /// independent of <c>For Each</c> — the implementer's probes <c>G38</c>/<c>G39</c> measure it in
    /// a loose, non-strict harness, where it instead creates a silent global and prints a stale
    /// value; the two harnesses disagree on SYMPTOM, not on the underlying gap.) Neither is task
    /// #168's to fix; see ADR-0009's consequences.</para>
    /// </summary>
    [Test]
    public void CaseDifferingCollision_ReusesCaseInsensitively_OnTheBackendsThatCaseFold()
    {
        const string program =
            "Sub Main()\n" +
            " Dim N As Integer = 5\n" +
            " Dim n_1 As Integer = 9\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In l\n" +
            "  s = s + n * n + n_1\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(N + n_1)\n" +
            "End Sub";
        const string vbAnswer = "43\n13"; // was "43\n14" before task #168

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(vbAnswer), "C#");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(vbAnswer), "MSIL");

            // Known gap, task #124 (not #168's): JavaScript does not case-fold, so the reuse
            // store's target `n` was never declared (only `N` was) — under this harness's strict
            // ES-module semantics that throws at run time rather than silently misbehaving.
            var (exitCode, _, stderr) = JavaScriptExecutionTests.RunNodeScriptForOutcome(JsTestSupport.Compile(program));
            Assert.That(exitCode, Is.Not.Zero, "JavaScript — known gap #124 (case folding) was expected to throw");
            Assert.That(stderr, Does.Contain("n is not defined"), stderr);
        });

        // Known gap, task #124: C++ does not case-fold either, and here it cannot even compile —
        // the store lowers to `n = ...`, and only `N` was ever declared.
        var cpp = BclE2E.CompileToCppOptimized(program);
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var (compiled, output) = VisualGameStudio.Tests.Native.CppCompile.TryCompile(cpp, compiler!.Value);
        Assert.That(compiled, Is.False, "C++ does not case-fold identifiers — known gap #124");
        Assert.That(output, Does.Contain("n"), output);
    }

    /// <summary>The aggressive-pipeline sibling of
    /// <see cref="CaseDifferingCollision_ReusesCaseInsensitively_OnTheBackendsThatCaseFold"/> — same
    /// shape, same split (C#/MSIL correct; JavaScript throws; C++ does not compile), through the
    /// aggressive optimizer passes on every leg that can run at all.</summary>
    [Test]
    public void CaseDifferingCollision_ReusesCaseInsensitively_OnTheBackendsThatCaseFold_Aggressive()
    {
        const string program =
            "Sub Main()\n" +
            " Dim N As Integer = 5\n" +
            " Dim n_1 As Integer = 9\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In l\n" +
            "  s = s + n * n + n_1\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(N + n_1)\n" +
            "End Sub";
        const string vbAnswer = "43\n13";

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(program)), Is.EqualTo(vbAnswer), "C#");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunAggressiveExpectingSuccess(program)), Is.EqualTo(vbAnswer), "MSIL");
            var (exitCode, _, stderr) = JavaScriptExecutionTests.RunNodeScriptForOutcome(JsTestSupport.CompileAggressive(program));
            Assert.That(exitCode, Is.Not.Zero, "JavaScript — known gap #124 (case folding) was expected to throw");
            Assert.That(stderr, Does.Contain("n is not defined"), stderr);
        });

        var cpp = BclE2E.CompileToCppAggressive(program);
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var (compiled, _) = VisualGameStudio.Tests.Native.CppCompile.TryCompile(cpp, compiler!.Value);
        Assert.That(compiled, Is.False, "C++ does not case-fold identifiers — known gap #124");
    }

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_CaseDifferingCollision")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_CaseDifferingCollision")]
    public void BothCompilerEntryPoints_CaseDifferingCollision(bool asProject)
    {
        const string source =
            "Sub Main()\n" +
            " Dim N As Integer = 5\n" +
            " Dim n_1 As Integer = 9\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n In l\n" +
            "  s = s + n * n + n_1\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(N + n_1)\n" +
            "End Sub";
        const string expected = "43\n13"; // was "43\n14" before task #168

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_ForEachRenameEntry_" + System.IO.Path.GetRandomFileName());
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

    /// <summary>S5f — TWO SIBLING (not nested) bare loops reusing the same outer local, in
    /// sequence, plus a read of `n` after both. Each loop's own hidden variable is independent
    /// (the analyzer keys <c>ForEachControlBindings</c> by node reference, one entry per loop), so
    /// the per-iteration prints are unaffected by task #168 (1, 2, then 11, 12 — each loop still
    /// assigns its OWN element to the reused `n` before its own body runs). What changes is the
    /// FINAL read: `n` is no longer restored to the original outer value (99, this test's OLD
    /// final line) — it holds the last element the SECOND loop assigned, 2.</summary>
    [Test]
    public void TwoSiblingForEachLoops_EachReuseIndependentlyLeavesTheVariableAtItsOwnLastElement()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim n As Integer = 99\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  Console.WriteLine(n)\n" +
            " Next\n" +
            " For Each n In l\n" +
            "  Console.WriteLine(n + 10)\n" +
            " Next\n" +
            " Console.WriteLine(n)\n" +
            "End Sub",
            "1\n2\n11\n12\n2"); // was "1\n2\n11\n12\n99" before task #168

    // ====================================================================================
    // S5e — a CLASS FIELD named `n`, and a MODULE GLOBAL named `n`, each shadowed — now REUSED —
    // by a bare `For Each n`. Both used to be framed as "not a collision" (the C# rename never
    // touched them, since ForEachVariableCollides checks only locals/parameters/open loop
    // variables) — true, but beside the point after task #168: a field or global is one of the
    // FOUR kinds of "existing variable" a bare For Each reuses (ADR-0009), same as a local.
    // ====================================================================================

    /// <summary>
    /// A CLASS FIELD `n`, reused by `Run`'s own bare `For Each n In l`. Before task #168 the field
    /// was untouched by the (shadowing) loop: `n` stayed 50, `s` summed to 3, and `Return s + n` =
    /// 53 (this test's OLD value). Now the field IS the loop's control variable: each iteration
    /// stores the element into it, so after the loop it holds 2 (the last of {1,2}) — `Return s +
    /// n` = 3 + 2 = 5. The field write proves the fix's whole point: a field store, not just a
    /// local's, comes from the ordinary assignment lowering IRBuilder now runs at the top of the
    /// loop body (<c>ForEachControlBinding.Assignment</c>).
    /// </summary>
    [Test]
    public void AForEachVariableSharingAClassFieldsName_ReusesTheField_LeavingItAtTheLastElement()
    {
        const string program =
            "Class Acc\n" +
            " Public n As Integer\n" +
            " Public Function Run(l As List(Of Integer)) As Integer\n" +
            "  n = 50\n" +
            "  Dim s As Integer = 0\n" +
            "  For Each n In l\n" +
            "   s = s + n\n" +
            "  Next\n" +
            "  Return s + n\n" +
            " End Function\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim a As New Acc()\n" +
            " Console.WriteLine(a.Run(l))\n" +
            "End Sub";
        const string expected = "5"; // was "53" before task #168
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    // ====================================================================================
    // TEXT-LEVEL — what the emitted C# looks like, not just the value it prints.
    // ====================================================================================

    /// <summary>
    /// ⛔ REPLACES the old "a module global is not a collision" pin (see the class docstring): a
    /// module global IS now reused, so the shape that pin used no longer emits `foreach (int n in
    /// l)` at all — it lowers to a hidden iteration variable plus an assignment into `n`
    /// (<see cref="AForEachVariableReusingAModuleGlobalsName_AllBackendsAgree"/> pins THAT). What
    /// this test keeps is the other half of the ORIGINAL contract, now genuinely about the
    /// DECLARING form: a bare `For Each n In l` naming NOTHING already in scope still declares its
    /// own loop-scoped `n`, and the C# backend still emits that plain source name verbatim — no
    /// hidden variable, no rename, because there is nothing here to reuse OR to collide with.
    /// </summary>
    [Test]
    public void ADeclaringForEach_WithNothingToReuseOrCollideWith_EmitsThePlainNameVerbatim()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  Console.WriteLine(n)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(csharp, Does.Contain("foreach (int n in l)"),
            "a For Each that declares its own variable (nothing pre-exists to reuse) must keep "
            + "its plain source name — got:\n" + csharp);
    }

    /// <summary>
    /// ⛔ REPLACES the old "a module global is not a collision, so the name is emitted verbatim"
    /// pin (see the class docstring) — false after task #168, on the SAME source: a module global
    /// named `n` is now REUSED by a bare `For Each n`, so all four backends agree on the printed
    /// sequence — one element assigned to `n` per iteration, in program order — even though the
    /// C# text underneath is no longer `foreach (int n in l)` (it is a hidden variable plus an
    /// assignment; see <see cref="ADeclaringForEach_WithNothingToReuseOrCollideWith_EmitsThePlainNameVerbatim"/>
    /// for the shape that still emits that). Per the family's own guidance (ADR-0009 / task #168's
    /// test-writer brief): pin BEHAVIOUR here, not the hidden name's spelling.
    /// </summary>
    [Test]
    public void AForEachVariableReusingAModuleGlobalsName_AllBackendsAgree()
        => FourBackends.RunsOnEveryBackend(
            "Dim n As Integer = 7\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  Console.WriteLine(n)\n" +
            " Next\n" +
            "End Sub",
            "1\n2"); // was a text-only pin (verbatim "foreach (int n in l)") before task #168

    /// <summary>
    /// ⛔ MUTANT (a) — the analyzer always declares a fresh variable (task #168's PRE-fix
    /// behaviour). This test alone would NOT catch it (there is nothing here to reuse). What
    /// catches it: every reuse test above, and every one in <c>ForEachControlVariableReuseTests</c>
    /// — a mutant that always says "declare fresh" reproduces the ORIGINAL #168 defect exactly
    /// (measured: <c>Dim x = 0 : For Each x In {5, 9} : Next</c> left <c>x</c> at 0). This test is
    /// the negative control proving those are testing a REAL reuse, not something a stray rename
    /// would also break: the emitted text here must be unchanged whether or not reuse exists at
    /// all, since nothing here has anything to reuse.
    /// </summary>
    [Test]
    public void ANonCollidingForEachVariable_IsUnaffectedByCollisionDetectionAtAll()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " For Each z In l\n" +
            "  Console.WriteLine(z)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(csharp, Does.Contain("foreach (int z in l)"));
    }

    // ====================================================================================
    // FreshForEachVariableName's Taken() — task #125 investigation, STILL LIVE after task #168,
    // but ONLY for a declaring `For Each x As T` (a bare For Each never reaches this machinery any
    // more: see the class docstring). Both shapes below are rewritten with an explicit `As Integer`
    // on every loop that needs to go through the OLD declaring-and-maybe-renaming path, so the
    // machinery they were written to pin is still the thing under test — not `#168`'s reuse.
    // ====================================================================================

    /// <summary>
    /// ⭐ S5g — task #125, kept alive under <c>As T</c>. The outer loop declares `n_1` (nothing
    /// collides — kept plain, unrenamed). The inner loop declares `n` `As Integer`, which SHADOWS
    /// the real outer local `n` (99) — a collision C# refuses without a rename (CS0136) — and
    /// `FreshForEachVariableName`'s first candidate for `n` is `n_1`, which is EXACTLY the outer
    /// loop's own (unrenamed, plain-spelled) `foreach` header name, still open while the inner loop
    /// is emitted. This is unchanged from the shape's ORIGINAL (pre-#168) form and ORIGINAL value —
    /// task #168 does not touch a declaring `As T` loop at all.
    ///
    /// <para>⛔ MEASURED WITH THE <c>_openForEachVariables</c> CLAUSE REMOVED (task #125): the
    /// inner loop is ALSO spelled <c>foreach (int n_1 in l)</c>, nested directly inside the outer
    /// <c>foreach (int n_1 in l)</c> — C# refuses a nested redeclaration of the same name
    /// (<c>CS0136</c>), so the program does not compile at all.</para>
    /// </summary>
    [Test]
    public void OuterLoopVariablesPlainName_IsTakenForAnInnerLoopsFreshName()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim n As Integer = 99\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n_1 As Integer In l\n" +
            "  For Each n As Integer In l\n" +
            "   s = s + n * 10 + n_1\n" +
            "  Next\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(n)\n" +
            "End Sub",
            "66\n99");

    [Test]
    public void OuterLoopVariablesPlainName_IsTakenForAnInnerLoopsFreshName_Aggressive()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Sub Main()\n" +
            " Dim n As Integer = 99\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim s As Integer = 0\n" +
            " For Each n_1 As Integer In l\n" +
            "  For Each n As Integer In l\n" +
            "   s = s + n * 10 + n_1\n" +
            "  Next\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(n)\n" +
            "End Sub",
            "66\n99");
}
