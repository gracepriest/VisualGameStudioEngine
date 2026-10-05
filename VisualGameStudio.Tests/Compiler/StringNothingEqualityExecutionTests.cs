using System;
using System.Collections.Generic;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  #206 (and #205) — a String `=` / `<>` reads Nothing as "" the VB way, on every backend.
//
//  WHAT WAS WRONG. VB answers a String `=` / `<>` with Operators.CompareString(a, b, TextCompare:=False):
//  ordinal, with Nothing read as "". BasicLang did not follow it. `Nothing = ""` folded to False; at run
//  time `s = ""` was False for an unassigned String on C#, JavaScript and MSIL; C++ did not compile
//  `s = Nothing` (a std::string against nullptr); and MSIL compared Strings with `ceq`, a REFERENCE
//  compare, so two equal strings built separately were unequal (#205). The owner ruled that BasicLang
//  adopts VB's rule.
//
//  THE FIX is one rule, IRCompare.IsStringEquality / ReadsNothingAsEmpty, read by the optimizer's fold
//  and by the C#, JavaScript, C++ and MSIL lowering (a Select Case over a String and a When guard
//  included). `Is` / `IsNot` and `Case Is Nothing` are NOT part of it: they stay reference identity
//  (ADR-0011).
//
//  THE ORACLE IS vbc. Every expected value below is the `.exp` of probes p01-p16, each compiled with
//  vbc and run (S/t206/probes). No backend is the oracle. A row that merges two probes runs each as its
//  own Sub, in the order named, and its expectation is the two answers one after the other.
//
//  HOW A ROW RUNS. Each row is one program run on all four backends (C#, C++, JavaScript, MSIL), each
//  through the real CLI, the CLI with --optimize and BasicCompiler.CompileProjectFiles with
//  OptimizeAggressive (`TempExec.AssertMatchesInEveryEntryPoint`, C# through CSharpProcessRunner in a
//  child process with a time limit). A backend whose tool is missing (no C++ compiler, no Node, no
//  ilasm) is SKIPPED, never failed: the row still runs the others, a failure on any of them fails the
//  row, and a row that skipped a leg ends Ignored with the legs named, so it cannot read as a green run
//  of a leg that never happened.
//
//  MUTANTS (rebuilt from the fix and swapped in; each is killed by the rows named):
//    M1  the optimizer's String fold switched off      -> P01_P02, P03_P04, P05, P12 (all four backends,
//                                                         through the CLI, -O and the project build: the
//                                                         constant propagator hands the fold an unassigned
//                                                         String's Nothing and the fold answers False)
//                                                         and the hand-built-IR row in the Fold fixture below
//    M2  MSIL lowers a String equality with `ceq` again -> P08 on MSIL (a reference compare: False | True |
//                                                         differ | False | other)
//    M3  C# `Case Is Nothing` widened to also match ""  -> P16 on C# (the last Select: `t = ""` is not
//                                                         `Is Nothing`, and prints "isnothing")
//
//  KNOWN GAPS — listed here, deliberately NOT tested (none is #206; each is what the ruling leaves):
//    - String `<`, `>`, `<=` and `>=` are refused by the front end (BL3001). This predates the change,
//      and the ruling covers `=` / `<>` only (probes p06 and p14 stay refused).
//    - MSIL does not support `String.Empty` (p09), so `String.Empty = Nothing` does not run there.
//    - On C# and JavaScript `Dim s As String` starts as "", not Nothing, so `s Is Nothing` is False for a
//      String never assigned there; VB says True (p13). Task #286.
//    - On C++ `Is Nothing` on a String is emptiness (ADR-0011 D3): `"" Is Nothing` is True there.
//      P16 and P16r pin that one line each, by name, for C++ only.
//    - On C# a Select with BOTH `Case ""` and `Case Is Nothing` keeps its old labels and its old answer:
//      C# refuses a case an earlier one already covers (CS8120), so the widening to `case "" or null` is
//      withheld. P16g pins the old answer, and that the program still compiles, for C# only.
//
//  ⚠ Named "…ExecutionTests" on purpose: it RUNS under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #206 / #205 RUN: a String `=` / `<>` reads Nothing as "" and compares ordinally, on C#, C++, JavaScript
/// and MSIL, through every entry point: a folded constant, an unassigned String, a String field never
/// assigned, a Function returning Nothing, a Select Case over a String, two equal strings built separately.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class StringNothingEqualityExecutionTests
{
    // ---- the programs (each line of vbc's answer beside it) ---------------------------------------

    /// <summary>p01 + p02 — `Nothing = ""` and `"" = Nothing`, `=` and `&lt;&gt;`: the constants fold. ⛔ Kills M1.</summary>
    private const string P01P02 = """
        Sub P01()
            Console.WriteLine(Nothing = "")
            Console.WriteLine(Nothing <> "")
        End Sub
        Sub P02()
            Console.WriteLine("" = Nothing)
            Console.WriteLine("" <> Nothing)
        End Sub
        Sub Main()
            P01()
            P02()
        End Sub
        """;

    private const string P01P02Expected = "True\nFalse\nTrue\nFalse";

    /// <summary>p03 + p04 — a String never assigned against "", with `=` and with `&lt;&gt;`, in an If and a While condition. ⛔ Kills M1.</summary>
    private const string P03P04 = """
        Sub P03()
            Dim s As String
            Console.WriteLine(s = "")
            If s = "" Then Console.WriteLine("eq") Else Console.WriteLine("ne")
            Dim t As String = Nothing
            Console.WriteLine("" = t)
        End Sub
        Sub P04()
            Dim s As String
            Console.WriteLine(s <> "")
            If s <> "" Then Console.WriteLine("ne") Else Console.WriteLine("eq")
            Dim n As Integer = 0
            While s <> "" And n < 3
                n = n + 1
            End While
            Console.WriteLine(n)
        End Sub
        Sub Main()
            P03()
            P04()
        End Sub
        """;

    private const string P03P04Expected = "True\neq\nTrue\nFalse\neq\n0";

    /// <summary>p05 — `s = Nothing` and `s &lt;&gt; Nothing` for Nothing, "" and "x" (C++ did not compile `s = Nothing`). ⛔ Kills M1.</summary>
    private const string P05 = """
        Sub Main()
            Dim s As String
            Console.WriteLine(s = Nothing)
            s = ""
            Console.WriteLine(s = Nothing)
            Console.WriteLine(s <> Nothing)
            s = "x"
            Console.WriteLine(s = Nothing)
        End Sub
        """;

    private const string P05Expected = "True\nTrue\nFalse\nFalse";

    /// <summary>p12 — two String variables, one Nothing and one "", in both orders. ⛔ Kills M1.</summary>
    private const string P12 = """
        Sub Main()
            Dim n As String = Nothing
            Dim e As String = ""
            Console.WriteLine(n = e)
            Console.WriteLine(n <> e)
            Console.WriteLine(e = n)
        End Sub
        """;

    private const string P12Expected = "True\nFalse\nTrue";

    /// <summary>p08 — #205: two equal strings built separately are equal (MSIL compared references), in `=`, `&lt;&gt;`, an If, a call result and a Select Case. ⛔ Kills M2.</summary>
    private const string P08 = """
        Function Twice(x As String) As String
            Return x & x
        End Function
        Sub Main()
            Dim a As String = "abab"
            Dim b As String = Twice("ab")
            Console.WriteLine(a = b)
            Console.WriteLine(a <> b)
            If a = b Then Console.WriteLine("same") Else Console.WriteLine("differ")
            Console.WriteLine(Twice("x") = "xx")
            Select Case Twice("q")
                Case "qq"
                    Console.WriteLine("qq")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub
        """;

    private const string P08Expected = "True\nFalse\nsame\nTrue\nqq";

    /// <summary>p10 — a String field nobody assigned.</summary>
    private const string P10 = """
        Class Holder
            Public Name As String
        End Class
        Sub Main()
            Dim h As New Holder()
            Console.WriteLine(h.Name = "")
            Console.WriteLine(h.Name <> "")
        End Sub
        """;

    private const string P10Expected = "True\nFalse";

    /// <summary>p11 — a Function that returns Nothing As String, compared in `=`, `&lt;&gt;` and an If.</summary>
    private const string P11 = """
        Function GetNone() As String
            Return Nothing
        End Function
        Sub Main()
            Console.WriteLine(GetNone() = "")
            Console.WriteLine(GetNone() <> "")
            If GetNone() = "" Then Console.WriteLine("empty")
        End Sub
        """;

    private const string P11Expected = "True\nFalse\nempty";

    /// <summary>
    /// p07 + p15 — `Select Case s` over a String that is Nothing: `Case ""`, `Case Is = ""`, `Case Nothing` on a "",
    /// `Case Is &lt;&gt; ""`, and a `Case "a", ""` list.
    /// </summary>
    private const string P07P15 = """
        Function GetNone() As String
            Return Nothing
        End Function
        Sub P07()
            Dim s As String
            Select Case s
                Case ""
                    Console.WriteLine("empty")
                Case Else
                    Console.WriteLine("else")
            End Select
            Dim t As String = ""
            Select Case t
                Case Nothing
                    Console.WriteLine("nothing")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub P15()
            Dim s As String = GetNone()
            Select Case s
                Case ""
                    Console.WriteLine("empty")
                Case Else
                    Console.WriteLine("else")
            End Select
            Dim t As String = ""
            Select Case s
                Case Is = ""
                    Console.WriteLine("is-empty")
                Case Else
                    Console.WriteLine("else")
            End Select
            Select Case t
                Case Nothing
                    Console.WriteLine("nothing")
                Case Else
                    Console.WriteLine("else")
            End Select
            Select Case s
                Case Is <> ""
                    Console.WriteLine("not-empty")
                Case Else
                    Console.WriteLine("else")
            End Select
            Select Case s
                Case "a", ""
                    Console.WriteLine("a-or-empty")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub Main()
            P07()
            P15()
        End Sub
        """;

    private const string P07P15Expected = "empty\nnothing\nempty\nis-empty\nnothing\nelse\na-or-empty";

    /// <summary>
    /// p16 — a `When` guard over a String equality, and `Case Is Nothing` on a String subject: a Nothing is `Is Nothing`
    /// and a "" is not. ⛔ Kills M3 on C# (the last Select).
    /// <para>⚠ C++ prints "isnothing" for that last Select where VB prints "else": a C++ String has no null state, so
    /// `"" Is Nothing` is True (ADR-0011 D3). That one line is pinned for C++ by name, and every other line of the program
    /// holds there.</para>
    /// </summary>
    private const string P16 = """
        Function GetNone() As String
            Return Nothing
        End Function
        Sub Main()
            Dim s As String = GetNone()
            Dim k As Integer = 1
            Select Case k
                Case 1 When s = ""
                    Console.WriteLine("guard")
                Case Else
                    Console.WriteLine("noguard")
            End Select
            Select Case k
                Case 1 When s <> Nothing
                    Console.WriteLine("guard2")
                Case Else
                    Console.WriteLine("noguard2")
            End Select
            Select Case s
                Case Is Nothing
                    Console.WriteLine("isnothing")
                Case Else
                    Console.WriteLine("else")
            End Select
            Dim t As String = ""
            Select Case t
                Case Is Nothing
                    Console.WriteLine("isnothing")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        """;

    private const string P16Expected = "guard\nnoguard2\nisnothing\nelse";
    private const string P16ExpectedCpp = "guard\nnoguard2\nisnothing\nisnothing";

    /// <summary>
    /// e1 — the C# guard: a Select over a String with `Case ""` and THEN `Case Is Nothing`, on a Nothing. C# refuses a case that an
    /// earlier one already covers (CS8120), so the widening of `Case ""` to `case "" or null` is withheld when a Select has two
    /// labels that meet "" or Nothing. vbc picks `Case ""`; C# keeps its old labels, and picks the second.
    /// </summary>
    private const string P16g = """
        Function GetNone() As String
            Return Nothing
        End Function
        Sub Main()
            Dim s As String = GetNone()
            Select Case s
                Case ""
                    Console.WriteLine("empty")
                Case Is Nothing
                    Console.WriteLine("isnothing")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        """;

    private const string P16gExpected = "empty";
    private const string P16gExpectedCSharp = "isnothing";

    /// <summary>
    /// e2 — the same two labels the other way round, on a Nothing and on a "". Here the old labels ARE vbc's answer on C#, JavaScript
    /// and MSIL, so the guard costs nothing there.
    /// <para>⚠ C++ prints "isnothing" for the "" where VB prints "empty": a C++ String has no null state, so `"" Is Nothing` is True
    /// (ADR-0011 D3, the same line P16 pins).</para>
    /// </summary>
    private const string P16r = """
        Function GetNone() As String
            Return Nothing
        End Function
        Sub Main()
            Dim s As String = GetNone()
            Select Case s
                Case Is Nothing
                    Console.WriteLine("isnothing")
                Case ""
                    Console.WriteLine("empty")
                Case Else
                    Console.WriteLine("else")
            End Select
            Dim t As String = ""
            Select Case t
                Case Is Nothing
                    Console.WriteLine("isnothing")
                Case ""
                    Console.WriteLine("empty")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        """;

    private const string P16rExpected = "isnothing\nempty";
    private const string P16rExpectedCpp = "isnothing\nisnothing";

    // ---- the rows ---------------------------------------------------------------------------------

    /// <summary>
    /// Every program that has ONE answer on every backend, on every backend, through every entry point. Before #206 each of these
    /// printed a wrong answer on at least three backends (36 of 192 probe cells matched vbc), or did not compile on C++.
    /// </summary>
    [TestCase(P01P02, P01P02Expected, TestName = "P01_P02_NothingLiteralAgainstEmptyString_FoldedConstant_BothOrders")]
    [TestCase(P03P04, P03P04Expected, TestName = "P03_P04_UnassignedStringAgainstEmpty_EqualsAndNotEquals")]
    [TestCase(P05, P05Expected, TestName = "P05_StringAgainstTheNothingLiteral")]
    [TestCase(P12, P12Expected, TestName = "P12_NothingStringAndEmptyString_TwoVariables_BothOrders")]
    [TestCase(P08, P08Expected, TestName = "P08_TwoEqualStringsBuiltSeparately_AreEqual_Issue205")]
    [TestCase(P10, P10Expected, TestName = "P10_StringFieldNeverAssigned")]
    [TestCase(P11, P11Expected, TestName = "P11_FunctionReturningNothingAsString")]
    [TestCase(P07P15, P07P15Expected, TestName = "P07_P15_SelectCaseOverANothingString_CaseEmptyAndCaseNothing")]
    public void AStringEquality_ReadsNothingAsEmpty_AndPrintsVbcsAnswer_OnEveryBackend(string source, string expected)
        => OnEveryBackend(TestContext.CurrentContext.Test.Name, (backend, label) => TempExec.AssertMatchesInEveryEntryPoint(backend, source, expected, label, hangSafe: true));

    /// <summary>
    /// p16 — a `When` guard and `Case Is Nothing`. ⛔ Kills M3 (C#). C++ alone differs on the last line, by ADR-0011 D3 (see the header).
    /// </summary>
    [Test]
    public void P16_WhenGuardsAndCaseIsNothing_IsNothingStaysReferenceIdentity()
        => OnEveryBackend(TestContext.CurrentContext.Test.Name,
            (backend, label) => TempExec.AssertMatchesInEveryEntryPoint(backend, P16, backend == Bk.Cpp ? P16ExpectedCpp : P16Expected, label, hangSafe: true));

    /// <summary>
    /// The C# guard: `Case ""` followed by `Case Is Nothing` still COMPILES on C# (a widened first label would be CS8120) and keeps its
    /// old answer; every other backend prints vbc's "empty".
    /// </summary>
    [Test]
    public void P16g_CaseEmptyThenCaseIsNothing_CSharpKeepsItsOldArm_AndStillCompiles()
        => OnEveryBackend(TestContext.CurrentContext.Test.Name,
            (backend, label) => TempExec.AssertMatchesInEveryEntryPoint(backend, P16g, backend == Bk.CSharp ? P16gExpectedCSharp : P16gExpected,
                label + (backend == Bk.CSharp ? " (C# keeps its old arm: a widened `Case \"\"` is CS8120. A different answer here means the widening moved — update this pin, do not delete it)" : ""),
                hangSafe: true));

    /// <summary>The two labels the other way round: `Case Is Nothing` first, so the old labels are vbc's answer (C++ differs on the "" line, ADR-0011 D3).</summary>
    [Test]
    public void P16r_CaseIsNothingThenCaseEmpty_PrintsVbcsAnswer_OnEveryBackend()
        => OnEveryBackend(TestContext.CurrentContext.Test.Name,
            (backend, label) => TempExec.AssertMatchesInEveryEntryPoint(backend, P16r, backend == Bk.Cpp ? P16rExpectedCpp : P16rExpected, label, hangSafe: true));

    /// <summary>
    /// Runs <paramref name="leg"/> once per backend. A failure on any backend fails the row (after every backend has been tried);
    /// a backend whose tool is missing is skipped by the existing gates and named; a row that skipped any leg ends Ignored, after
    /// its failures have been thrown, so a skip can never hide one.
    /// </summary>
    private static void OnEveryBackend(string label, Action<Bk, string> leg)
    {
        var failures = new List<string>();
        var skipped = new List<string>();
        var ran = new List<string>();
        foreach (var backend in TempExec.Backends(Bk.All))
        {
            try
            {
                leg(backend, label);
                ran.Add(backend.ToString());
            }
            catch (IgnoreException ex)
            {
                skipped.Add($"{backend} ({ex.Message})");
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (skipped.Count > 0)
            Assert.Ignore($"ran and passed on [{string.Join(", ", ran)}]; skipped: {string.Join("; ", skipped)}");
    }
}

/// <summary>
/// #206, the optimizer's half, FAST (no process spawned, in the fast subset): the constant fold answers a String equality the VB way,
/// on hand-built IR so that nothing but the fold can be the reason. The constant propagator hands it an unassigned String's Nothing, and
/// before #206 `Nothing = ""` folded to False and `s = ""` printed False for a String never assigned on every backend.
/// ⛔ Kills M1 (the fold switched off) without running anything.
/// </summary>
[TestFixture]
public class StringNothingEqualityFoldTests
{
    private static readonly TypeInfo Bool = new("Boolean", TypeKind.Primitive);
    private static readonly TypeInfo Str = new("String", TypeKind.Primitive);

    /// <summary>The Nothing LITERAL: typed Object, as IRBuilder makes it (a propagated one keeps the type of the String it was stored into).</summary>
    private static readonly TypeInfo ObjectType = new("Object", TypeKind.Class);

    /// <summary>Folds ONE compare of two constants with the real pass; null when the pass declines.</summary>
    private static bool? Fold(CompareKind op, object a, TypeInfo ta, object b, TypeInfo tb)
    {
        var module = new IRModule("fold");
        var fn = new IRFunction("F", new TypeInfo("Void", TypeKind.Void));
        var block = fn.CreateBlock("entry");
        block.Instructions.Add(new IRCompare("t0", op, new IRConstant(a, ta), new IRConstant(b, tb), Bool));
        module.Functions.Add(fn);

        new ConstantFoldingPass().Run(module);

        return block.Instructions[0] is IRConstant { Value: bool folded } ? folded : null;
    }

    [Test]
    public void TheFold_ReadsNothingAsEmptyString_ForEqualsAndNotEquals_InBothOrders()
    {
        Assert.Multiple(() =>
        {
            // the literal Nothing (typed Object) against ""
            Assert.That(Fold(CompareKind.Eq, null, ObjectType, "", Str), Is.True, "Nothing = \"\"");
            Assert.That(Fold(CompareKind.Eq, "", Str, null, ObjectType), Is.True, "\"\" = Nothing");
            Assert.That(Fold(CompareKind.Ne, null, ObjectType, "", Str), Is.False, "Nothing <> \"\"");
            Assert.That(Fold(CompareKind.Ne, "", Str, null, ObjectType), Is.False, "\"\" <> Nothing");

            // a propagated Nothing keeps the String type of the variable it was stored into: s = "" for a String never assigned
            Assert.That(Fold(CompareKind.Eq, null, Str, "", Str), Is.True, "s = \"\" (s Nothing)");
            Assert.That(Fold(CompareKind.Ne, null, Str, "", Str), Is.False, "s <> \"\" (s Nothing)");
            Assert.That(Fold(CompareKind.Eq, null, Str, null, ObjectType), Is.True, "s = Nothing (s Nothing)");

            // against a non-empty String Nothing is still unequal, and two equal Strings are equal (ordinal)
            Assert.That(Fold(CompareKind.Eq, null, Str, "x", Str), Is.False, "s = \"x\" (s Nothing)");
            Assert.That(Fold(CompareKind.Eq, "ab", Str, "ab", Str), Is.True, "\"ab\" = \"ab\"");
            Assert.That(Fold(CompareKind.Eq, "ab", Str, "AB", Str), Is.False, "\"ab\" = \"AB\" is ordinal, not a text compare");
        });
    }
}
