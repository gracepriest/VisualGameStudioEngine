using System;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Pins step 6 of the uncommitted fix set: <c>CSharpBackend.Visit(IRForEach)</c> now gives a
/// colliding <c>For Each</c> variable a FRESH, scope-safe C# name for its body
/// (<c>ForEachVariableCollides</c> / <c>FreshForEachVariableName</c>), and restores the name's
/// outer meaning once the body closes.
///
/// <para>⛔ <b>Two distinct defects, in the SAME direction.</b> (1) BasicLang scopes a
/// <c>For Each</c> variable to its body and lets it SHADOW an outer local, a parameter, or an
/// enclosing loop's own variable — C# refuses that outright with <c>CS0136</c>, so the emitted
/// program did not compile at all. (2) BasicLang is CASE-INSENSITIVE where C# is not: a local
/// <c>N</c> beside <c>For Each n</c> compiled cleanly on C# (no CS0136, since the names differ by
/// case) but the loop body then read the OUTER <c>N</c> through C#'s case-insensitive name LOOKUP
/// — a silent WRONG ANSWER, not a compile failure, and the one this family's docstring calls out
/// by number: measured 68 where 43 is correct (<see cref="CaseDifferingCollision_WithARealLocalNamedLikeTheFreshName"/>).</para>
///
/// <para>⚠ A MODULE GLOBAL or a CLASS FIELD is explicitly NOT a collision — a C# local is allowed
/// to shadow a field, and BasicLang's own scoping treats them differently from a local/parameter
/// (<c>ForEachVariableCollides</c> checks only <c>_currentFunction.LocalVariables</c> and
/// <c>.Parameters</c>, plus other OPEN <c>For Each</c> variables — never module globals or class
/// members). <see cref="AForEachVariableSharingAClassFieldsName_ComputesCorrectly"/> and
/// <see cref="APlainForEach_WithNoCollisionAtAll_EmitsTheNameVerbatim"/> pin that half.</para>
///
/// <para>⚠ Kept to ONE shape per <see cref="FourBackends"/> call (<c>Assert.Multiple</c> inside).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ForEachVariableRenameFixTests
{
    // ====================================================================================
    // S5 — the headline shape: `n` declared outside (99), then `For Each n In l`. C# used to
    // refuse this at CS0136; all four backends now agree, and `n` reads back 99 afterward — the
    // rename must be WITHDRAWN once the loop body closes (mutant (f)'s kill).
    // ====================================================================================

    [Test]
    public void OuterLocalCollision_NameResolvesBackAfterTheLoop()
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
            "6\n99");

    [Test]
    public void OuterLocalCollision_NameResolvesBackAfterTheLoop_Aggressive()
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
            "6\n99");

    /// <summary>
    /// S5b — NESTED loops sharing the SAME variable name: the outer `n` binding must be restored
    /// after the INNER loop closes, so the outer body's own read of `n` (after the inner `Next`)
    /// still means the outer element, not a stale inner one. outer {1,2} x inner {10,20}, each
    /// outer element ALSO adds `n * 100` (the outer `n`, post-restore) — (1+2)*(10+20) + (1+2)*100
    /// = 90 + 300 = wait, computed directly: per outer n, s += sum(inner) then s += n*100; total
    /// = (10+20)+(10+20) + 1*100+2*100 = 60 + 300 = 360.
    /// </summary>
    [Test]
    public void NestedForEach_SameVariableName_OuterBindingRestoredAfterTheInnerLoop()
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
            "  For Each n In inner\n" +
            "   s = s + n\n" +
            "  Next\n" +
            "  s = s + n * 100\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            "End Sub",
            "360");

    /// <summary>S5c — collision with a PARAMETER, not a local: `SumAll(n As Integer, l ...)`'s
    /// own `For Each n In l` shadows its OWN parameter, and the parameter's value (7) must
    /// survive the loop to be added back after it (`s * 1000 + n` = 3000 + 7 = 3007).</summary>
    [Test]
    public void ParameterCollision()
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
            "3007");

    /// <summary>
    /// ⭐ S5d — THE shape this family's docstring measures by number. `N` (capital) and the loop
    /// variable `n` (lowercase) are the SAME identifier under BasicLang's case-insensitivity, so
    /// this is the CASE-DIFFERING collision — not a same-spelling one. It ALSO seeds a real user
    /// local literally named `n_1` (9), which is exactly the fresh name
    /// <c>FreshForEachVariableName</c> would try FIRST for a variable named `n` — so its
    /// candidate-checking loop (not "the first candidate is always free") is what keeps this
    /// correct.
    ///
    /// <para>⛔ MEASURED AT THE DEFECT: this printed <c>68</c> (not <c>43</c>) — a SILENT wrong
    /// answer, not a compile failure, because BasicLang's own case-insensitive name lookup quietly
    /// resolved the loop body's <c>n</c> reads to the OUTER <c>N</c> (5) instead of the loop's own
    /// element. Correct: loop sums <c>n*n + n_1</c> for n in {3,4} → (9+9)+(16+9) = 18+25 = 43;
    /// and <c>N + n_1</c> after the loop is untouched by the loop, 5+9=14.</para>
    /// </summary>
    [Test]
    public void CaseDifferingCollision_WithARealLocalNamedLikeTheFreshName()
        => FourBackends.RunsOnEveryBackend(
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
            "End Sub",
            "43\n14");

    [Test]
    public void CaseDifferingCollision_WithARealLocalNamedLikeTheFreshName_Aggressive()
        => FourBackends.RunsOnEveryBackendAggressive(
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
            "End Sub",
            "43\n14");

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
        const string expected = "43\n14";

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

    /// <summary>S5f — TWO SIBLING (not nested) loops reusing the same name, in sequence, plus a
    /// read of the OUTER `n` after both. Each loop's rename must be independently withdrawn: the
    /// second loop must not inherit a stale rename left by the first, and the final read must see
    /// the ORIGINAL outer value, not either loop's.</summary>
    [Test]
    public void TwoSiblingForEachLoops_EachRenameIndependentlyWithdrawn()
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
            "1\n2\n11\n12\n99");

    // ====================================================================================
    // S5e — a CLASS FIELD named `n`, shadowed by `For Each n`. A field is explicitly NOT a
    // collision (ForEachVariableCollides checks only locals/parameters/open loop variables), so
    // this must NOT be renamed — C# permits a local to shadow a field, which is why this compiles
    // and runs correctly either way; what this shape actually discriminates is whether
    // ForEachVariableCollides wrongly treats a field as one (it would still likely compile with a
    // rename too, but APlainForEach_WithNoCollisionAtAll_EmitsTheNameVerbatim and
    // AForEachVariableSharingAModuleGlobalsName_EmitsThePlainNameVerbatim below pin the TEXT,
    // which a rename would visibly change).
    //
    // ⚠ JavaScript is NOT part of the oracle here: a separate, pre-existing, unrelated JS-backend
    // defect on a field/For-Each-variable name COLLISION prints 150 instead of 53 (measured on
    // the working tree, both pipelines) — not this family's, and not fixed here. C#, C++ and MSIL
    // agree.
    // ====================================================================================

    [Test]
    public void AForEachVariableSharingAClassFieldsName_ComputesCorrectly()
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
        const string expected = "53";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    // ====================================================================================
    // TEXT-LEVEL — the emitted C# ITSELF, not just the value it prints. A value-only assertion
    // cannot tell "correctly renamed" apart from "correct by coincidence"; these read the emitted
    // source and look for the literal spelling the fixture's own contract names.
    // ====================================================================================

    /// <summary>
    /// ⛔ A plain <c>For Each n In l</c> with NO collision anywhere (no outer local, no
    /// parameter, no sibling/enclosing loop of the same name — only a module GLOBAL of that
    /// name, which is not a collision either) must emit the SOURCE name verbatim:
    /// <c>foreach (int n in l)</c>, not a renamed <c>n_1</c> or similar. This is the contract's
    /// own measured example ("a plain For Each must emit foreach (int n in l)").
    /// </summary>
    [Test]
    public void APlainForEach_WithNoCollisionAtAll_EmitsTheNameVerbatim()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Dim n As Integer = 7\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  Console.WriteLine(n)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(csharp, Does.Contain("foreach (int n in l)"),
            "a For Each variable that collides with nothing must keep its plain source name — "
            + "got:\n" + csharp);
    }

    /// <summary>
    /// The module-global sibling of <see cref="AForEachVariableSharingAClassFieldsName_ComputesCorrectly"/>:
    /// a MODULE-LEVEL <c>n</c> (not a class field), shadowed by <c>For Each n</c> inside a
    /// module-level <c>Sub</c>. Same non-collision rule, same "keep the plain name" assertion,
    /// this time at module scope.
    /// </summary>
    [Test]
    public void AForEachVariableSharingAModuleGlobalsName_EmitsThePlainNameVerbatim()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Dim n As Integer = 7\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  Console.WriteLine(n)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(csharp, Does.Contain("foreach (int n in l)"),
            "a module global of the same name is not a collision — the loop variable must keep "
            + "its plain source name — got:\n" + csharp);
    }

    /// <summary>
    /// ⛔ MUTANT (d) — <c>ForEachVariableCollides</c> hardcoded to always return <c>false</c>.
    /// This test alone would NOT catch it (there is nothing here to collide with). What catches
    /// it: <see cref="OuterLocalCollision_NameResolvesBackAfterTheLoop"/>,
    /// <see cref="ParameterCollision"/> and <see cref="CaseDifferingCollision_WithARealLocalNamedLikeTheFreshName"/>
    /// would each go back to CS0103/CS0136 (a mutant that always says "no collision" reproduces
    /// the ORIGINAL pre-step-6 defect exactly), failing those tests' C# leg. This test is the
    /// negative control that proves those three are testing REAL collisions, not something a
    /// stray rename would also break: the emitted text here must be UNCHANGED whether or not
    /// collision detection runs at all, since nothing here collides.
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
    // FreshForEachVariableName's Taken() — task #125 investigation. Two of its four clauses
    // turned out to be DEAD (deleted, not merely untested): the
    // `_currentFunction.LocalVariables`/`.Parameters` clause is REDUNDANT with `_declaredIdentifiers`
    // (populated with every local, parameter and global of the current method — checked earlier
    // in the same `||` chain, so it already covers what that clause covered) and the
    // `_forEachRenames.Values` clause is REDUNDANT with `_issuedForEachNames` (an ACTIVE rename's
    // target is always an earlier fresh name this same method body already issued, so
    // `_issuedForEachNames` already contains it). Confirmed by mutation: deleting either clause
    // changed NOTHING in this whole file's 13-plus tests before they were removed from the
    // production source — there is no BasicLang program that could have killed them, which is why
    // they are gone rather than merely untested.
    //
    // The ONE clause Taken() still needs beyond `_declaredIdentifiers`/`_currentClassMemberNames`/
    // `_issuedForEachNames` is `_openForEachVariables` — an OUTER For Each loop's variable is a
    // C# `foreach` header's own declared name, never added to `_declaredIdentifiers`, so nothing
    // else in the chain would catch a fresh name colliding with it. S5g below is that shape.
    // ====================================================================================

    /// <summary>
    /// ⭐ S5g — task #125. THE test that kills removing the <c>_openForEachVariables</c> clause
    /// from <c>Taken()</c>. The OUTER loop's variable is <c>n_1</c> (no collision — nothing else
    /// is named <c>n_1</c>, so it keeps its plain name and is NOT itself renamed). The INNER
    /// loop's variable is <c>n</c>, which DOES collide (a real outer local <c>Dim n As Integer =
    /// 99</c>), so <c>FreshForEachVariableName</c> tries its first candidate for <c>n</c>:
    /// <c>n_1</c> — which is EXACTLY the outer loop's own (unrenamed, plain-spelled) variable,
    /// still open while the inner loop is being emitted.
    ///
    /// <para>⛔ MEASURED WITH THE <c>_openForEachVariables</c> CLAUSE REMOVED: <c>Taken()</c> does
    /// not recognise <c>n_1</c> as taken (it is a `foreach` header's block-scoped name, not a
    /// declared local, parameter or class member, and not an earlier fresh name of THIS rename —
    /// it is the OUTER loop's own plain name), so the inner loop is ALSO spelled
    /// <c>foreach (int n_1 in l)</c>, nested directly inside the outer
    /// <c>foreach (int n_1 in l)</c> — C# refuses a nested redeclaration of the same name
    /// (<c>CS0136</c>), so the program does not compile at all.</para>
    ///
    /// <para>Correct: outer <c>n_1</c> over {1,2}, inner <c>n</c> over {1,2} each time,
    /// <c>s += n*10 + n_1</c> → for n_1=1: (10+1)+(20+1)=32; for n_1=2: (10+2)+(20+2)=34; total
    /// 66. The outer real local <c>n</c> (99) is untouched by any of it.</para>
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
            " For Each n_1 In l\n" +
            "  For Each n In l\n" +
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
            " For Each n_1 In l\n" +
            "  For Each n In l\n" +
            "   s = s + n * 10 + n_1\n" +
            "  Next\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            " Console.WriteLine(n)\n" +
            "End Sub",
            "66\n99");
}
