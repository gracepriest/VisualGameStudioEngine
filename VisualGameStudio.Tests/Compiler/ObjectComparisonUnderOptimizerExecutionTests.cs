using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #214, RUN. A comparison with an Object operand stays VB's LATE-BOUND comparison (ADR-0012) when the optimizer runs.
//
//  ⛔ THE BUG. `Dim s As Object = "20" : Console.WriteLine(s = 20)` printed False on C#, JavaScript and MSIL at the CLI, with --optimize and from a Release project; vbc prints True. `"abc" = 20`, which throws
//  InvalidCastException in VB, printed False. Both backends that implement the late-bound comparison (MSIL since #177, C# since #211) decide that from the operand's IR TYPE, and CopyPropagationPass took the type
//  away: it replaced the Object variable with its recorded copy, the String constant "20", which carries its OWN type, so the compare became `"20" = 20` and ConstantFoldingPass answered the pair with Equals ("unequal").
//  The fix is `CopyPropagationPass.KeepsLateBinding` (IROptimizer.cs): a comparison operand that is an Object comparand is not replaced by a copy that is not one. TryFoldCompare is unchanged; declining the fold was
//  measured and is worse (C# CS0019, MSIL still wrong, a typed `Boolean = 1` the fold answers right by luck turning wrong).
//  ⭐ #300: the Nothing LITERAL is held back too. #214 exempted it (the fold's String arm answered `Nothing = ""` True where JavaScript's own `===` would not), and that exemption was the whole of a wrong answer:
//  `Dim n As Object = Nothing : n = 0` became the constant pair `Nothing = 0`, which the fold answered False, so `n = 0`, `n < 1` and `n = False` printed False on C#, JavaScript and MSIL at every entry point (vbc: True).
//  Since #215 JavaScript's late-bound `__blCompareObject` answers the kept variable as vbc does, so the exemption had no reason left; the `AnObjectHoldingNothing_…` / `AnObjectAssignedNothing_…` rows assert the result.
//
//  ⭐ THE ORACLE IS vbc. Every `vb` below is what the SDK's vbc prints for the program wrapped in a VB Module (S/t214/tw-programs, run through S/t136/tools/vbv2.py by the test-writer: every answer matched the
//  implementer's own `.exp` in S/t214/probes). Never what a backend printed.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): C# and MSIL each run through the real CLI (standard passes), the real CLI with `--optimize`
//  (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj and the IDE call), PLUS the in-process standard and aggressive emitters. ⛔ Every C# RUN is HANG-SAFE
//  (`CSharpProcessRunner`, a child process with a time limit): the in-process runner has none. The C# legs run first and their failures are reported before the MSIL legs, which SKIP (not fail) where ilasm is missing.
//  The JavaScript rows go through `JavaScriptOptimizedExecutionTests.RunOptimized` (the STANDARD pipeline the CLI always runs; `JavaScriptExecutionTests.RunJs` runs no optimizer at all and would never reach the pass),
//  and #300's two also through the real CLI, the CLI with `--optimize` and `CompileProjectFiles`, as C# and MSIL do.
//  The FAST half is `ObjectComparisonUnderOptimizerShapeTests` below: what the compiler WRITES, no process.
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from a plain source copy of the fix outside the worktree and run against a copy of the test output with its BasicLang.dll swapped; the cases that go red, measured):
//    M1 no guard (propagation as before the fix)                -> ELEVEN: `AnObjectString_AgainstAnIntegerOnTheRight` / `…OnTheLeft` / `…ADoubleAndAnInteger` / `…ABoolean`, `AnObjectBoolean_…`, `AnObjectChar_…`,
//                                                                    `AnObjectComparison_AsAnIfCondition` (`ne`), `AnObjectStringThatIsNotANumber_…` (prints False, does not throw), `JavaScript_AnObjectString_GreaterThanAnInteger`
//                                                                    (False), the fast `AnObjectOperand_KeepsItsLateBoundCall_…` (no call left) and the moved pin `MsilObjectBoxing…L11b_…`. The three controls and the Nothing row stay green.
//    M2 the Nothing literal is exempt again (#214's original rule, which #300 removed)
//                                                               -> since #300: FIVE, #300's own: `AnObjectHoldingNothing_AgainstZeroOneEmptyAndFalse` and `AnObjectAssignedNothing_ConstantOnTheLeftAndAnIfCondition` (C#, every
//                                                                    leg prints `False | False | True | False` / `True | False | ge`), their two `JavaScript_…` twins (the same on every JavaScript leg) and the fast
//                                                                    `AnObjectHoldingNothing_KeepsItsLateBoundCalls_…` (no call left). `Control_NothingAgainstTypedIntegers` and every other row stay green (measured over this
//                                                                    fixture and the JavaScriptLateBound / CSharpLateBound / MsilObjectBoxing / CopyPropagation filters, 209 cases).
//                                                                    History: when #214 landed M2 was killed by the JavaScript `n = ""` row alone (`JavaScript_TheNothingLiteral_StillPropagates`, renamed by #300 `…_AnObjectHoldingNothing_EqualsTheEmptyString`); #215's helper made JavaScript
//                                                                    answer `n = ""` True with the exemption and without it, and from then until #300 NO committed test killed M2, because the only shapes where M2 and #214 differed
//                                                                    (`n = 0`, `n < 1`, `n = False`) were the ones the exemption answered WRONG: M2 was right there, and asserting vbc's answer would have failed on #214 itself.
//    M3 only the LEFT operand is guarded (`20 = s` is not)      -> `AnObjectString_AgainstAnIntegerOnTheLeft` (False | False) and the fast `AnObjectOperand_KeepsItsLateBoundCall_…`. Two cases.
//    M4 every constant is exempt, not only the Nothing literal  -> the same ELEVEN as M1 (the String "20" propagates again).
//    M5 the VARIABLE's own type is ignored (a typed one is held back too) -> `Control_ATypedBooleanAgainstATypedOne` ONLY (`b = j` keeps both variables: C# CS0019 `bool == int`, MSIL answers True where vbc says False).
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or a defect that is another task's). Each is the same before and after #214:
//    * (WAS: JavaScript's `===` on an Object, #215.) FIXED by #215: JavaScript's Object comparison is VB's late-bound `__blCompareObject` now, so an Object "2.5" `= 2.5`, an Object True `= -1`, an Object "True" `= True`
//      and `If o = 20` print vbc's answer on JavaScript through every entry point. Asserted by `JavaScriptLateBoundComparisonExecutionTests` and by the moved pin `MsilObjectBoxingExecutionTests.L11b_…` (positive on
//      JavaScript too). This fixture's own two JavaScript rows are unchanged and still right. What JavaScript still owes is a BOXED `Is` (primitives have no box identity), named in that fixture's header.
//    * (WAS: an Object holding Nothing compared with a number, `Dim n As Object = Nothing : n = 0` False on every backend.) FIXED by #300 (above): the Nothing literal is no longer propagated into the comparison, so
//      the fold never sees `Nothing = 0` and each backend's late-bound comparison answers vbc's True. (Declining the FOLD of a Nothing-against-value pair instead was measured by #214 and turned C#'s `n <= 1` wrong.)
//    * A TYPED String or Boolean compared with a number (`"20" = 20`, `s = i`, `b = i` over -1) is wrong on all four backends: the front end inserts no VB conversion for the pair. (Its ONE line that is right,
//      `b = j` over 1, IS asserted below, because a fix that declines the fold turned it wrong.)
//    * Object ARITHMETIC (`o + 1`) is refused by the front end ("Arithmetic operator '+' requires numeric operands"), so there is no program to run.
//    * C++ has no Object at all ("Object has no C++ mapping"); LLVM has no console and cannot link what it emits.
//
//  ⚠ Named "…ExecutionTests" and its JavaScript rows spawn Node (RunOptimized), so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>#214 — an Object comparison under the optimizer, RUN on C# and MSIL through every entry point against vbc, and on JavaScript through the standard pipeline.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the CLI legs spawn dotnet and the runners spawn children; keep the machine to this fixture
public class ObjectComparisonUnderOptimizerExecutionTests
{
    // ---- the programs (S/t214/tw-programs, one file each; vbc's answer is the `vb` of the row that uses it) ----

    /// <summary>L11b: an Object "20" against an Integer constant on the RIGHT. vbc: `True | False`.</summary>
    internal const string StrTwentyOnTheRight = """
        Sub Main()
            Dim s As Object = "20"
            Console.WriteLine(s = 20)
            Console.WriteLine(s <> 20)
        End Sub
        """;

    /// <summary>The same Object with the constants on the LEFT, `20 = s` and `19 < s`. vbc: `True | True`.</summary>
    internal const string StrTwentyOnTheLeft = """
        Sub Main()
            Dim s As Object = "20"
            Console.WriteLine(20 = s)
            Console.WriteLine(19 < s)
        End Sub
        """;

    /// <summary>An Object "2.5" against a Double and an Integer. vbc: `True | True`.</summary>
    internal const string StrDouble = """
        Sub Main()
            Dim s As Object = "2.5"
            Console.WriteLine(s = 2.5)
            Console.WriteLine(s > 2)
        End Sub
        """;

    /// <summary>The ordering half of <see cref="StrDouble"/> alone: the one Object-against-number line JavaScript's own coercion answers right. vbc: `True`.</summary>
    internal const string StrDoubleGreater = """
        Sub Main()
            Dim s As Object = "2.5"
            Console.WriteLine(s > 2)
        End Sub
        """;

    /// <summary>An Object holding True against 1, -1 and 0 (VB converts True to -1). vbc: `False | True | True`.</summary>
    internal const string BoolInt = """
        Sub Main()
            Dim b As Object = True
            Console.WriteLine(b = 1)
            Console.WriteLine(b = -1)
            Console.WriteLine(b < 0)
        End Sub
        """;

    /// <summary>An Object holding the String "True" against the Boolean True. vbc: `True | False`.</summary>
    internal const string StrBool = """
        Sub Main()
            Dim s As Object = "True"
            Console.WriteLine(s = True)
            Console.WriteLine(s <> True)
        End Sub
        """;

    /// <summary>An Object holding the Char "A"c against the String "A". vbc: `True | False`. (JavaScript refuses a Char literal, BL7004, so this row is C# and MSIL.)</summary>
    internal const string CharStr = """
        Sub Main()
            Dim c As Object = "A"c
            Console.WriteLine(c = "A")
            Console.WriteLine(c <> "A")
        End Sub
        """;

    /// <summary>`If o = 20` as a CONDITION, not an operand of WriteLine. vbc: `eq`.</summary>
    internal const string IfCondition = """
        Sub Main()
            Dim o As Object = "20"
            If o = 20 Then
                Console.WriteLine("eq")
            Else
                Console.WriteLine("ne")
            End If
        End Sub
        """;

    /// <summary>`"abc" = 20` on an Object: VB converts the String to Double and THROWS. vbc: `Unhandled exception. System.InvalidCastException: Conversion from string "abc" to type 'Double' is not valid.`</summary>
    internal const string AbcEqualsTwenty = """
        Sub Main()
            Dim s As Object = "abc"
            Console.WriteLine(s = 20)
        End Sub
        """;

    /// <summary>CONTROL: String against String, on an Object and on a typed String. Unchanged. vbc: `True | True | True | False`.</summary>
    internal const string StrStrControl = """
        Sub Main()
            Dim a As Object = "abc"
            Console.WriteLine(a = "abc")
            Console.WriteLine(a <> "abd")
            Dim t As String = "x"
            Console.WriteLine(t = "x")
            Console.WriteLine(t = "y")
        End Sub
        """;

    /// <summary>CONTROL: mixed-width numeric pairs, on Objects and on typed locals. Unchanged (the fold's #123 rule). vbc: `True | True | False | True`.</summary>
    internal const string NumericControl = """
        Sub Main()
            Dim o As Object = 20
            Console.WriteLine(o = 20.0)
            Dim d As Object = 2.5
            Console.WriteLine(d > 2)
            Dim i As Integer = 3
            Dim x As Double = 3.14
            Console.WriteLine(x <= i)
            Dim l As Long = 5
            Console.WriteLine(i < l)
        End Sub
        """;

    /// <summary>A TYPED Boolean True against a typed Integer 1: a typed pair the fix must leave propagating. vbc: `False` (True is -1).</summary>
    internal const string TypedBooleanAgainstOne = """
        Sub Main()
            Dim b As Boolean = True
            Dim j As Integer = 1
            Console.WriteLine(b = j)
        End Sub
        """;

    /// <summary>An Object holding the Nothing LITERAL against "". vbc: `True`.</summary>
    internal const string NothingEqualsEmpty = """
        Sub Main()
            Dim n As Object = Nothing
            Console.WriteLine(n = "")
        End Sub
        """;

    // ---- #300's programs (S/t300/tprobes, each run through S/t136/tools/vbv2.py; vbc's answer is in the summary) ----

    /// <summary>#300: an Object holding the Nothing LITERAL against 0, 1, "" and False; Nothing is the other operand's default. vbc: `True | True | True | True`. Before #300: `False | False | True | False` on C#, JavaScript and MSIL.</summary>
    internal const string NothingAgainstValues = """
        Sub Main()
            Dim n As Object = Nothing
            Console.WriteLine(n = 0)
            Console.WriteLine(n < 1)
            Console.WriteLine(n = "")
            Console.WriteLine(n = False)
        End Sub
        """;

    /// <summary>#300: an Object holding 5 and then ASSIGNED Nothing, against 0 with the constant on the LEFT and in an `If o &lt; 1` condition. vbc: `True | True | lt`. Before #300: `True | False | ge`.</summary>
    internal const string AssignedNothing = """
        Sub Main()
            Dim o As Object = 5
            Console.WriteLine(o = 5)
            o = Nothing
            Console.WriteLine(0 = o)
            If o < 1 Then
                Console.WriteLine("lt")
            Else
                Console.WriteLine("ge")
            End If
        End Sub
        """;

    /// <summary>#300 CONTROL: Nothing against TYPED Integers. No Object operand, so nothing is late-bound and copy propagation is as before (its emission was byte-identical before and after #300). vbc: `True | True | True`.</summary>
    internal const string NothingAgainstTypedIntegers = """
        Sub Main()
            Dim i As Integer = 0
            Console.WriteLine(i = Nothing)
            Dim j As Integer = 1
            Console.WriteLine(j > Nothing)
            Dim k As Integer = Nothing
            Console.WriteLine(k = 0)
        End Sub
        """;

    // ---- plumbing ----

    private static string Norm(string s) => TempExec.Norm(s);

    /// <summary>The message of a failed assertion without the emitted program (which can be long): the first lines, up to the "--- emitted ---" / "--- generated IL ---" marker.</summary>
    private static string Brief(string message)
        => string.Join(" // ", message.Replace("\r\n", "\n").Split('\n').TakeWhile(l => !l.StartsWith("--- ")).Take(4));

    private static void Check(List<string> failures, string leg, string vb, Func<string> run)
    {
        try
        {
            var got = Norm(run());
            if (got != Norm(vb))
                failures.Add($"{leg}: printed [{got.Replace("\n", " | ")}] where vbc prints [{Norm(vb).Replace("\n", " | ")}]");
        }
        catch (Exception ex) when (ex is AssertionException or MultipleAssertException)
        {
            // A compile failure (CS0019) or a crash: the first line names it.
            failures.Add($"{leg}: {Brief(ex.Message)}");
        }
    }

    /// <summary>
    /// ⭐ C# and MSIL, EVERY entry point, each printing vbc's answer: the CLI, the CLI with `--optimize`, `CompileProjectFiles` and the in-process standard and aggressive emitters. A failure in one leg is collected, not
    /// thrown, so every other leg still reports and the message names it. The C# legs are asserted BEFORE the MSIL legs, and the MSIL legs skip (never fail) where ilasm is missing.
    /// </summary>
    private static void AssertCSharpAndMsilPrint(string source, string vb)
    {
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
            Check(failures, $"C# {entry}", vb, () => TempExec.Run(Bk.CSharp, entry, source, hangSafe: true));
        Check(failures, "C# standard passes (in process)", vb, () => CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(source)));
        Check(failures, "C# aggressive passes (in process)", vb, () => CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(source)));
        Assert.That(failures, Is.Empty, "on C#:\n" + string.Join("\n", failures));

        MsilHarness.RequireIlasm(); // a skip, after the C# legs have passed
        foreach (var entry in Enum.GetValues<EntryPoint>())
            Check(failures, $"MSIL {entry}", vb, () => TempExec.Run(Bk.Msil, entry, source));
        Check(failures, "MSIL standard passes (in process)", vb, () => MsilHarness.RunExpectingSuccess(source));
        Check(failures, "MSIL aggressive passes (in process)", vb, () => MsilHarness.RunAggressiveExpectingSuccess(source));
        Assert.That(failures, Is.Empty, "on MSIL:\n" + string.Join("\n", failures));
    }

    // ---- C# and MSIL: an Object operand against a constant ----

    /// <summary>
    /// (1-7) An Object compared with a constant prints vbc's answer on C# and MSIL, through every entry point: a String "20" against an Integer (the constant on the right AND on the left), "2.5" against a Double and an
    /// Integer, a Boolean against 1 / -1 / 0, the String "True" against True, a Char against a String, and an `If` condition. Before: every row printed the Equals answer ("unequal") the fold gave `"20" = 20`.
    /// </summary>
    [TestCase(StrTwentyOnTheRight, "True\nFalse", TestName = "AnObjectString_AgainstAnIntegerOnTheRight")]
    [TestCase(StrTwentyOnTheLeft, "True\nTrue", TestName = "AnObjectString_AgainstAnIntegerOnTheLeft")]
    [TestCase(StrDouble, "True\nTrue", TestName = "AnObjectString_AgainstADoubleAndAnInteger")]
    [TestCase(BoolInt, "False\nTrue\nTrue", TestName = "AnObjectBoolean_AgainstOneMinusOneAndZero")]
    [TestCase(StrBool, "True\nFalse", TestName = "AnObjectString_AgainstABoolean")]
    [TestCase(CharStr, "True\nFalse", TestName = "AnObjectChar_AgainstAString")]
    [TestCase(IfCondition, "eq", TestName = "AnObjectComparison_AsAnIfCondition")]
    public void AnObjectComparedWithAConstant_PrintsVbcsAnswer_OnCSharpAndMsil(string source, string vb)
        => AssertCSharpAndMsilPrint(source, vb);

    /// <summary>
    /// (8) `"abc" = 20` on an Object THROWS InvalidCastException, as vbc does, on C# and MSIL through every entry point. Before: it printed False, the fold's answer for a String and an Integer. The program prints
    /// nothing before it dies, so a leg that prints `False` or exits cleanly fails.
    /// </summary>
    [Test]
    public void AnObjectStringThatIsNotANumber_AgainstANumber_ThrowsInvalidCast_AsVbcDoes()
    {
        var failures = new List<string>();

        void CSharp(string leg, Func<string> emit)
        {
            try
            {
                var run = CSharpProcessRunner.Run(emit());
                if (run.Outcome != CSharpRunOutcome.Crashed || !run.Error.Contains("System.InvalidCastException") || run.Output.Trim().Length != 0)
                    failures.Add($"{leg}: {run.Outcome}, exit {run.ExitCode}, printed [{run.Output.Trim()}], stderr [{Brief(run.Error)}] — vbc throws System.InvalidCastException and prints nothing");
            }
            catch (Exception ex) when (ex is AssertionException or MultipleAssertException) { failures.Add($"{leg}: {Brief(ex.Message)}"); }
        }

        void Msil(string leg, Func<string> emit)
        {
            try
            {
                var run = MsilHarness.RunIl(emit(), "T");
                if (run.Outcome != MsilHarness.MsilOutcome.RunFailed || !run.Output.Contains("System.InvalidCastException") || run.Output.Contains("False"))
                    failures.Add($"{leg}: {run.Outcome}, [{Brief(run.Output)}] — vbc throws System.InvalidCastException and prints nothing");
            }
            catch (Exception ex) when (ex is AssertionException or MultipleAssertException) { failures.Add($"{leg}: {Brief(ex.Message)}"); }
        }

        foreach (var entry in Enum.GetValues<EntryPoint>())
            CSharp($"C# {entry}", () => TempExec.Emit(Bk.CSharp, entry, AbcEqualsTwenty));
        CSharp("C# standard passes (in process)", () => ReturnCoercionTests.EmitCSharpForTest(AbcEqualsTwenty));
        CSharp("C# aggressive passes (in process)", () => ReturnCoercionTests.EmitCSharpAggressiveForTest(AbcEqualsTwenty));
        Assert.That(failures, Is.Empty, "on C#:\n" + string.Join("\n", failures));

        MsilHarness.RequireIlasm();
        foreach (var entry in Enum.GetValues<EntryPoint>())
            Msil($"MSIL {entry}", () => TempExec.Emit(Bk.Msil, entry, AbcEqualsTwenty));
        Msil("MSIL standard passes (in process)", () => MsilHarness.CompileToIl(AbcEqualsTwenty, "T"));
        Msil("MSIL aggressive passes (in process)", () => MsilHarness.CompileToIl(AbcEqualsTwenty, "T", aggressive: true));
        Assert.That(failures, Is.Empty, "on MSIL:\n" + string.Join("\n", failures));
    }

    // ---- C# and MSIL: the controls ----

    /// <summary>
    /// (9-11, and #300's 15) The CONTROLS: what the fix must not change. A String against a String (an Object and a typed local), mixed-width numeric pairs on Objects and on typed locals, and a TYPED Boolean True against a typed
    /// Integer 1, which the fold answers right (False) and which a fix that declined the fold turned wrong on MSIL (and made CS0019 on C#). #300 adds Nothing against TYPED Integers: no Object operand, nothing held back.
    /// </summary>
    [TestCase(StrStrControl, "True\nTrue\nTrue\nFalse", TestName = "Control_StringAgainstString")]
    [TestCase(NumericControl, "True\nTrue\nFalse\nTrue", TestName = "Control_MixedWidthNumericPairs")]
    [TestCase(TypedBooleanAgainstOne, "False", TestName = "Control_ATypedBooleanAgainstATypedOne")]
    [TestCase(NothingAgainstTypedIntegers, "True\nTrue\nTrue", TestName = "Control_NothingAgainstTypedIntegers")]
    public void TheControls_StayAsTheyWere_OnCSharpAndMsil(string source, string vb)
        => AssertCSharpAndMsilPrint(source, vb);

    // ---- #300: an Object holding Nothing, C#, MSIL and JavaScript through every entry point ----

    /// <summary>
    /// (16-17) #300. An Object holding Nothing compared with a value prints vbc's answer on C# and MSIL through every entry point: Nothing is the other operand's default, so `n = 0`, `n &lt; 1`, `n = ""` and `n = False` are
    /// all True, and so are `0 = o` and `o &lt; 1` once `o` is ASSIGNED Nothing. Before #300 the Nothing literal was propagated into each comparison and the fold answered `Nothing = 0` / `Nothing &lt; 1` / `Nothing = False`
    /// False (`False | False | True | False` and `True | False | ge`). ⛔ M2 (the exemption back) fails both.
    /// </summary>
    [TestCase(NothingAgainstValues, "True\nTrue\nTrue\nTrue", TestName = "AnObjectHoldingNothing_AgainstZeroOneEmptyAndFalse")]
    [TestCase(AssignedNothing, "True\nTrue\nlt", TestName = "AnObjectAssignedNothing_ConstantOnTheLeftAndAnIfCondition")]
    public void AnObjectHoldingNothing_ComparedWithAValue_PrintsVbcsAnswer_OnCSharpAndMsil(string source, string vb)
        => AssertCSharpAndMsilPrint(source, vb);

    /// <summary>
    /// (18-19) #300 on JavaScript, the same two programs through every entry point (the CLI, the CLI with `--optimize`, `CompileProjectFiles`) and the in-process standard pipeline: the kept Object variable reaches #215's
    /// `__blCompareObject`, which answers vbc. Before #300 the same `False | False | True | False` and `True | False | ge` as C# and MSIL. ⛔ M2 fails both.
    /// </summary>
    [TestCase(NothingAgainstValues, "True\nTrue\nTrue\nTrue", TestName = "JavaScript_AnObjectHoldingNothing_AgainstZeroOneEmptyAndFalse")]
    [TestCase(AssignedNothing, "True\nTrue\nlt", TestName = "JavaScript_AnObjectAssignedNothing_ConstantOnTheLeftAndAnIfCondition")]
    public void JavaScript_AnObjectHoldingNothing_ComparedWithAValue_PrintsVbcsAnswer_EveryEntryPoint(string source, string vb)
    {
        TempExec.RequireTool(Bk.JavaScript); // a skip, outside any multiple-assertion block
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
            Check(failures, $"JavaScript {entry}", vb, () => TempExec.Run(Bk.JavaScript, entry, source));
        Check(failures, "JavaScript standard passes (in process)", vb, () => JavaScriptOptimizedExecutionTests.RunOptimized(source));
        Assert.That(failures, Is.Empty, "on JavaScript:\n" + string.Join("\n", failures));
    }

    // ---- JavaScript, the standard pipeline ----

    /// <summary>
    /// (12-13) JavaScript, through the optimizer-running pipeline. `Object "2.5" > 2` is right now (JavaScript's own coercion answers it once the fold no longer sees `"2.5" > 2`), and an Object holding Nothing
    /// `= ""` is True. (Until #300 that row was `…_TheNothingLiteral_StillPropagates`: the literal was propagated and the fold's String arm answered `Nothing = ""`; since #300 the variable is kept and #215's
    /// `__blCompareObject` answers it, the same True.) The rest of an Object comparison on JavaScript is #215's `JavaScriptLateBoundComparisonExecutionTests` (see the header).
    /// </summary>
    [TestCase(StrDoubleGreater, "True", TestName = "JavaScript_AnObjectString_GreaterThanAnInteger")]
    [TestCase(NothingEqualsEmpty, "True", TestName = "JavaScript_AnObjectHoldingNothing_EqualsTheEmptyString")]
    public void JavaScript_StandardPipeline_PrintsVbcsAnswer(string source, string vb)
        => Assert.That(Norm(JavaScriptOptimizedExecutionTests.RunOptimized(source)), Is.EqualTo(Norm(vb)),
            $"JavaScript (optimizer-running pipeline) must print vbc's [{Norm(vb).Replace("\n", " | ")}]");
}

/// <summary>
/// #214 SHAPE — the fast half. Nothing spawns: the C# the compiler writes, through the standard passes, the aggressive ones and <c>CompileProjectFiles</c>. An Object operand is not replaced by its constant, so the
/// late-bound call is still there to be made. (The runs above are what say the calls answer right; this is what runs in the fast subset.)
/// </summary>
[TestFixture]
public class ObjectComparisonUnderOptimizerShapeTests
{
    private const string LateBoundCall = "Operators.ConditionalCompareObject";

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// (14) An Object "20" compared with a constant on the right (`s = 20`, `s &lt;&gt; 20`) and on the left (`20 = s`, `19 &lt; s`) keeps its late-bound call on every entry point: two calls for each program. Without the
    /// guard the String "20" replaces `s`, the fold answers the pair and NO call is left (nothing to count).
    /// </summary>
    [Test]
    public void AnObjectOperand_KeepsItsLateBoundCall_WithAConstantOnEitherSide()
    {
        var failures = new List<string>();
        foreach (var (label, source) in new[]
        {
            ("constant on the right", ObjectComparisonUnderOptimizerExecutionTests.StrTwentyOnTheRight),
            ("constant on the left", ObjectComparisonUnderOptimizerExecutionTests.StrTwentyOnTheLeft),
        })
        foreach (var (name, text) in new (string, string)[]
        {
            ("standard", ReturnCoercionTests.EmitCSharpForTest(source)),
            ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest(source)),
            ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source)),
        })
        {
            var calls = Count(text, LateBoundCall);
            if (calls != 2)
                failures.Add($"{label}, {name}: {calls} late-bound calls where the two comparisons make two:\n{text}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// (20) #300: the Nothing LITERAL is not propagated into an Object comparison either. An Object holding Nothing compared with 0, 1, "" and False keeps all four late-bound calls on C# (the standard passes, the
    /// aggressive ones, <c>CompileProjectFiles</c>) and all four <c>__blCompareObject</c> calls on JavaScript (the standard pipeline); Nothing against TYPED Integers makes none on either. With #214's exemption back (M2)
    /// the literal replaces <c>n</c>, the fold answers every pair and no call is left.
    /// </summary>
    [Test]
    public void AnObjectHoldingNothing_KeepsItsLateBoundCalls_NothingAgainstATypedIntegerMakesNone()
    {
        const string JsHelper = "__blCompareObject(";
        var failures = new List<string>();
        foreach (var (label, source, expected) in new[]
        {
            ("an Object holding Nothing", ObjectComparisonUnderOptimizerExecutionTests.NothingAgainstValues, 4),
            ("Nothing against typed Integers", ObjectComparisonUnderOptimizerExecutionTests.NothingAgainstTypedIntegers, 0),
        })
        {
            foreach (var (name, text) in new (string, string)[]
            {
                ("standard", ReturnCoercionTests.EmitCSharpForTest(source)),
                ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest(source)),
                ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source)),
            })
            {
                var calls = Count(text, LateBoundCall);
                if (calls != expected)
                    failures.Add($"{label}, C# {name}: {calls} late-bound calls where {expected} are due:\n{text}");
            }

            var js = JsTestSupport.CompileOptimized(source);
            var jsCalls = Count(js, JsHelper) - Count(js, "function " + JsHelper);
            if (jsCalls != expected)
                failures.Add($"{label}, JavaScript standard pipeline: {jsCalls} {JsHelper}…) calls where {expected} are due:\n{js}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
