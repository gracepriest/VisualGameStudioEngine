using System;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Pins step 7 of the uncommitted fix set: <c>IRBuilder</c>'s static-call argument-building arm
/// now ALSO consults the user callee's OWN declared parameters
/// (<c>staticCalleeSymbol.Parameters[..].IsByRef</c>) when marking <c>call.ByRefArguments</c>,
/// not only <c>NetRefKind</c> (the .NET-interop marshalling list, which records nothing for a
/// plain VB <c>ByRef</c>).
///
/// <para>⛔ <b>The bug, and why it was SILENT everywhere but C#.</b> <c>Util.Bump(v)</c> against
/// <c>Public Shared Sub Bump(ByRef n As Integer)</c> reached IR claiming a BY-VALUE call — no
/// by-ref marker anywhere. On C#, that surfaced LOUDLY: the backend still spells a declared
/// <c>ref</c> parameter's signature with <c>ref</c> (from the DECLARATION, correctly), so the
/// mismatched call site was a build break (<c>CS1620</c>) — visible immediately. On C++ and MSIL,
/// the false claim was trusted by the OPTIMIZER too: <c>ConstantPropagationPass</c> (or an
/// equivalent copy-forward) kept the caller's PRE-CALL value of <c>v</c> live across the call and
/// used it for anything read afterward — a SILENT wrong answer with a clean build and a clean
/// run. Measured: <c>b=42</c> where <c>43</c> is correct
/// (<see cref="AByRefWriteThroughASharedMethod_TheOptimizerMustNotCarryTheOldValueAcrossTheCall"/>),
/// and a two-argument in-place <c>Swap</c> that silently did nothing
/// (<see cref="SwapThroughASharedMethod_BothArgumentsWriteBackThroughTheCall"/>).</para>
///
/// <para>⚠ ORACLE RULE, same as <c>MsilByRefTests</c>: JavaScript refuses EVERY ByRef by design
/// (<c>BL7002</c>, checked at the declaration), so it is never part of the numeric oracle here —
/// each running case asserts C#, C++ and MSIL agree, and separately asserts the JavaScript
/// refusal.</para>
///
/// <para>⚠ Kept to ONE shape per multi-backend call (<c>Assert.Multiple</c> inside <c>FourBackends</c>
/// helpers and hand-rolled equivalents here alike).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class StaticMethodByRefFixTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    /// <summary>C#, C++ and MSIL run and agree; JavaScript refuses the declaration by design —
    /// the same pattern <c>MsilByRefTests.AgreesOnThreeBackends</c> uses.</summary>
    private static void AgreesOnThreeBackends(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(() => JavaScriptExecutionTests.RunJs(program),
                Throws.Exception.With.Message.Contains("ByRef"), "JavaScript refuses ByRef by design");
        });
    }

    private static void AgreesOnThreeBackendsAggressive(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(FourBackends.RunEmittedCSharpAggressive(program)), Is.EqualTo(expected), "C#");
            Assert.That(Norm(MsilHarness.RunAggressiveExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(() => JavaScriptExecutionTests.RunJs(program),
                Throws.Exception.With.Message.Contains("ByRef"), "JavaScript refuses ByRef by design");
        });
    }

    // ====================================================================================
    // S6 — the headline shape: a plain ByRef write through a Shared method. ⭐ Also asserted by
    // MsilByRefTests.ByRefOnASharedMethod (the PROMOTED, renamed sibling of this shape); kept
    // here too as this family's own anchor.
    // ====================================================================================

    [Test]
    public void AByRefWriteThroughASharedMethod_IsObservedByTheCaller()
        => AgreesOnThreeBackends("""
            Class Util
             Public Shared Sub Bump(ByRef n As Integer)
              n = n + 1
             End Sub
            End Class
            Sub Main()
             Dim v As Integer = 41
             Util.Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "42");

    // ====================================================================================
    // S6b — ⭐ THE SHAPE THAT PROVES THE OPTIMIZER KEEPS NO FACTS ACROSS THE CALL. `a` reads `v`
    // BEFORE the ByRef call, `b` reads it AFTER — they must differ. ⛔ MEASURED AT THE DEFECT:
    // a=42 b=42 on C++ and MSIL (the false "no ByRef" claim let a copy-forward pass treat `v` as
    // still 41 after the call). Standard AND aggressive pipelines both need this, since either
    // could reintroduce the same false fact through a different pass.
    // ====================================================================================

    [Test]
    public void AByRefWriteThroughASharedMethod_TheOptimizerMustNotCarryTheOldValueAcrossTheCall()
        => AgreesOnThreeBackends("""
            Class Util
             Public Shared Sub Bump(ByRef n As Integer)
              n = n + 1
             End Sub
            End Class
            Sub Main()
             Dim v As Integer = 41
             Dim a As Integer = v + 1
             Util.Bump(v)
             Dim b As Integer = v + 1
             PrintLine("a=" & CStr(a) & " b=" & CStr(b))
            End Sub
            """, "a=42 b=43");

    [Test]
    public void AByRefWriteThroughASharedMethod_TheOptimizerMustNotCarryTheOldValueAcrossTheCall_Aggressive()
        => AgreesOnThreeBackendsAggressive("""
            Class Util
             Public Shared Sub Bump(ByRef n As Integer)
              n = n + 1
             End Sub
            End Class
            Sub Main()
             Dim v As Integer = 41
             Dim a As Integer = v + 1
             Util.Bump(v)
             Dim b As Integer = v + 1
             PrintLine("a=" & CStr(a) & " b=" & CStr(b))
            End Sub
            """, "a=42 b=43");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_TheOptimizerMustNotCarryTheOldValueAcrossTheCall")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_TheOptimizerMustNotCarryTheOldValueAcrossTheCall")]
    public void BothCompilerEntryPoints_TheOptimizerMustNotCarryTheOldValueAcrossTheCall(bool asProject)
    {
        const string source =
            "Class Util\n" +
            " Public Shared Sub Bump(ByRef n As Integer)\n" +
            "  n = n + 1\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim v As Integer = 41\n" +
            " Dim a As Integer = v + 1\n" +
            " Util.Bump(v)\n" +
            " Dim b As Integer = v + 1\n" +
            " Console.WriteLine(\"a=\" & CStr(a) & \" b=\" & CStr(b))\n" +
            "End Sub";
        const string expected = "a=42 b=43";

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_StaticByRefEntry_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, source);

            // Aggressive: the Release .blproj / `--optimize` route, where the optimizer trusting
            // the false claim actually shipped broken.
            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR))),
                Is.EqualTo(expected),
                (asProject ? "CompileProjectFiles" : "CompileFile")
                + " with OptimizeAggressive printed the wrong answer — the route a Release build "
                + "takes, and where the silent b=42 shipped.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ====================================================================================
    // S6d — Swap: TWO ByRef arguments on the SAME call, both writing back. ⛔ MEASURED AT THE
    // DEFECT: silently did nothing (p and q kept their pre-call values on C++/MSIL) — the same
    // false-claim mechanism, doubled.
    // ====================================================================================

    [Test]
    public void SwapThroughASharedMethod_BothArgumentsWriteBackThroughTheCall()
        => AgreesOnThreeBackends("""
            Class Util
             Public Shared Sub Swap(ByRef a As Integer, ByRef b As Integer)
              Dim t As Integer = a
              a = b
              b = t
             End Sub
             Public Shared Sub Show(x As Integer)
              PrintLine(CStr(x))
             End Sub
            End Class
            Sub Main()
             Dim p As Integer = 1
             Dim q As Integer = 2
             Dim s1 As Integer = p * 10 + q
             Util.Swap(p, q)
             Dim s2 As Integer = p * 10 + q
             Util.Show(s1)
             Util.Show(s2)
            End Sub
            """, "12\n21");

    [Test]
    public void SwapThroughASharedMethod_BothArgumentsWriteBackThroughTheCall_Aggressive()
        => AgreesOnThreeBackendsAggressive("""
            Class Util
             Public Shared Sub Swap(ByRef a As Integer, ByRef b As Integer)
              Dim t As Integer = a
              a = b
              b = t
             End Sub
             Public Shared Sub Show(x As Integer)
              PrintLine(CStr(x))
             End Sub
            End Class
            Sub Main()
             Dim p As Integer = 1
             Dim q As Integer = 2
             Dim s1 As Integer = p * 10 + q
             Util.Swap(p, q)
             Dim s2 As Integer = p * 10 + q
             Util.Show(s1)
             Util.Show(s2)
            End Sub
            """, "12\n21");

    // ====================================================================================
    // MUTANT (g) — `|| userByRef` removed from `IRBuilder`'s static-call arm (falling back to
    // ONLY `refKind != NetRefKind.None`, the pre-fix behaviour). Every test above is this
    // mutant's kill: the call site would go back to claiming a BY-VALUE call for every one of
    // them, reproducing the CS1620 build break on C# — failing the "C#" leg inside
    // AgreesOnThreeBackends/AgreesOnThreeBackendsAggressive for every test in this file.
    // ====================================================================================
}
