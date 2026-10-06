using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #123 — ConstantFoldingPass.TryFoldCompare compares two numeric constants of DIFFERENT widths in
//  the wider one, as VB does (Double beats Single beats every integral type; two integral types meet
//  in Long). Before it, the fold knew only same-type pairs and answered a mixed pair with CompareGt /
//  CompareLt's "false" and Equals' "unequal boxes": `3.14 <= 3` folded to True, and #123's own
//  SubstituteFoldedConstGlobals turned `Const D = 3.14 : Const E As Boolean = D <= 3` from a clear
//  refusal into that wrong answer.
//
//  ⭐ EVERY expectation below is vbc's, measured (Option Strict Off, S/t123/mx): each row's six letters
//  are `=  <>  <  <=  >  >=` for `a op b`. The literal form and the Const form gave vbc identical
//  answers for every row. ⚠ The Single rows are the ones a "just use Double" fold gets wrong:
//  VB rounds the integer TO Single first, so 16777217 = 16777216F is TRUE, and widens a Single
//  EXACTLY, so CSng(0.1) = 0.1 is FALSE.
//
//  ⚠ Object boxes: for two NUMERIC boxes VB's late-bound compare widens the same way, so the fold is
//  right there too (MsilObjectBoxingExecutionTests.L11). A boxed String against a number is NOT a
//  numeric pair and the fold still says "unequal" where VB converts the String. That no longer reaches an OBJECT operand (#214, fixed: CopyPropagationPass.KeepsLateBinding leaves the Object variable in the
//  compare, so its backend's late-bound comparison answers and the fold never sees the pair); it is still what a TYPED or literal String-against-number pair gets (`"20" = 20`, `s = i`), a front-end gap.
// ================================================================================================

/// <summary>#123: the fold compares mixed-width numeric constants the way VB does.</summary>
[TestFixture]
public class MixedNumericCompareFoldTests
{
    private static readonly CompareKind[] Ops =
        { CompareKind.Eq, CompareKind.Ne, CompareKind.Lt, CompareKind.Le, CompareKind.Gt, CompareKind.Ge };

    private static readonly string[] OpText = { "=", "<>", "<", "<=", ">", ">=" };

    /// <summary>`b op' a` asks the same question as `a op b`.</summary>
    private static CompareKind Mirror(CompareKind k) => k switch
    {
        CompareKind.Lt => CompareKind.Gt,
        CompareKind.Le => CompareKind.Ge,
        CompareKind.Gt => CompareKind.Lt,
        CompareKind.Ge => CompareKind.Le,
        _ => k,
    };

    /// <summary>(type of a, a, type of b, b, vbc's answers for = &lt;&gt; &lt; &lt;= &gt; &gt;=).</summary>
    private static readonly (string TA, string A, string TB, string B, string Vb)[] Table =
    {
        ("Integer", "2", "Long", "3L", "FTTTFF"),
        ("Integer", "3", "Long", "3L", "TFFTFT"),
        ("Integer", "4", "Long", "3L", "FTFFTT"),
        ("Integer", "-4", "Long", "-3L", "FTTTFF"),
        ("Integer", "2", "Single", "2.5F", "FTTTFF"),
        ("Integer", "3", "Single", "3.0F", "TFFTFT"),
        ("Integer", "3", "Single", "2.5F", "FTFFTT"),
        ("Integer", "-3", "Single", "-2.5F", "FTTTFF"),
        ("Integer", "16777217", "Single", "16777216.0F", "TFFTFT"),   // rounded TO Single: equal
        ("Integer", "16777217", "Single", "16777218.0F", "FTTTFF"),
        ("Integer", "3", "Double", "3.14", "FTTTFF"),
        ("Integer", "3", "Double", "3.0", "TFFTFT"),
        ("Integer", "4", "Double", "3.14", "FTFFTT"),
        ("Integer", "-3", "Double", "-2.5", "FTTTFF"),
        ("Long", "3L", "Single", "3.5F", "FTTTFF"),
        ("Long", "3L", "Single", "3.0F", "TFFTFT"),
        ("Long", "4L", "Single", "3.5F", "FTFFTT"),
        ("Long", "16777217L", "Single", "16777216.0F", "TFFTFT"),      // rounded TO Single: equal
        ("Long", "3L", "Double", "3.5", "FTTTFF"),
        ("Long", "3L", "Double", "3.0", "TFFTFT"),
        ("Long", "-4L", "Double", "-3.5", "FTTTFF"),
        ("Long", "9007199254740993L", "Double", "9007199254740992.0", "TFFTFT"),   // 2^53 + 1 rounds TO Double
        ("Single", "0.1F", "Double", "0.1", "FTFFTT"),                 // widened EXACTLY: 0.100000001490116 > 0.1
        ("Single", "0.5F", "Double", "0.5", "TFFTFT"),
        ("Single", "2.5F", "Double", "3.0", "FTTTFF"),
        ("Single", "-0.1F", "Double", "-0.1", "FTTTFF"),
        ("Double", "3.14", "Integer", "3", "FTFFTT"),
        ("Double", "3.0", "Integer", "3", "TFFTFT"),
        ("Single", "2.5F", "Integer", "3", "FTTTFF"),
        ("Byte", "200", "Double", "199.5", "FTFFTT"),
        ("Byte", "200", "Integer", "200", "TFFTFT"),
        ("Short", "-300", "Single", "-299.5F", "FTTTFF"),
        ("Short", "-300", "Double", "-300.0", "TFFTFT"),
        ("UInteger", "4000000000", "Integer", "3", "FTFFTT"),
        ("UInteger", "4000000000", "Double", "4000000000.0", "TFFTFT"),
    };

    private static IEnumerable<TestCaseData> Rows() => Table.Select(r =>
        new TestCaseData(r.TA, r.A, r.TB, r.B, r.Vb).SetName($"{r.TA}_{r.A}_vs_{r.TB}_{r.B}".Replace('.', 'p').Replace('-', 'm')));

    /// <summary>The CLR value a constant of that BasicLang type carries, from its literal spelling.</summary>
    private static object Value(string type, string literal)
    {
        var text = literal.TrimEnd('L', 'F');
        var inv = CultureInfo.InvariantCulture;
        // ⛔ Each arm is boxed ON ITS OWN. A switch expression over int/long/float/double arms has a natural type
        // (double), and every value would silently become a Double — a table of same-type pairs that proves nothing.
        return type switch
        {
            "Integer" => (object)int.Parse(text, inv),
            "Long" => (object)long.Parse(text, inv),
            "Single" => (object)float.Parse(text, inv),
            "Double" => (object)double.Parse(text, inv),
            "Byte" => (object)byte.Parse(text, inv),
            "Short" => (object)short.Parse(text, inv),
            "UInteger" => (object)uint.Parse(text, inv),
            _ => throw new ArgumentException(type),
        };
    }

    /// <summary>The table really is MIXED: every row's two values have different CLR types.</summary>
    [Test]
    public void EveryRow_PairsTwoDifferentClrTypes()
    {
        Assert.Multiple(() =>
        {
            foreach (var r in Table)
                Assert.That(Value(r.TA, r.A).GetType(), Is.Not.EqualTo(Value(r.TB, r.B).GetType()), $"{r.TA} {r.A} / {r.TB} {r.B}");
        });
    }

    private static readonly TypeInfo Bool = new("Boolean", TypeKind.Primitive);

    /// <summary>Folds ONE compare of two constants with the real pass; null when the pass declines.</summary>
    private static bool? Fold(CompareKind op, object a, string ta, object b, string tb)
    {
        var module = new IRModule("fold");
        var fn = new IRFunction("F", new TypeInfo("Void", TypeKind.Void));
        var block = fn.CreateBlock("entry");
        block.Instructions.Add(new IRCompare("t0", op,
            new IRConstant(a, new TypeInfo(ta, TypeKind.Primitive)),
            new IRConstant(b, new TypeInfo(tb, TypeKind.Primitive)), Bool));
        module.Functions.Add(fn);

        new ConstantFoldingPass().Run(module);

        return block.Instructions[0] is IRConstant { Value: bool folded } ? folded : null;
    }

    /// <summary>
    /// The pass itself, every operator, in BOTH operand orders (`b op' a` must agree with `a op b`), against vbc.
    /// </summary>
    [TestCaseSource(nameof(Rows))]
    public void TheFold_ComparesMixedWidths_InTheWiderType_AsVbDoes(string ta, string a, string tb, string b, string vb)
    {
        var va = Value(ta, a);
        var vbv = Value(tb, b);
        Assert.Multiple(() =>
        {
            for (int i = 0; i < Ops.Length; i++)
            {
                var expected = vb[i] == 'T';
                Assert.That(Fold(Ops[i], va, ta, vbv, tb), Is.EqualTo(expected), $"{a} {OpText[i]} {b}");
                Assert.That(Fold(Mirror(Ops[i]), vbv, tb, va, ta), Is.EqualTo(expected), $"{b} (mirrored {OpText[i]}) {a}");
            }
        });
    }

    /// <summary>
    /// The same rows through the front end and the IR builder: module Consts compared in a module-level initializer,
    /// which #123's SubstituteFoldedConstGlobals folds (the shape that turned a refusal into a wrong answer).
    /// </summary>
    [TestCaseSource(nameof(Rows))]
    public void AModuleInitializerOverTwoConsts_FoldsLikeVb(string ta, string a, string tb, string b, string vb)
    {
        var lines = new List<string> { $"Const A1 As {ta} = {a}", $"Const B1 As {tb} = {b}" };
        for (int i = 0; i < Ops.Length; i++)
            lines.Add($"Dim r{i} As Boolean = A1 {OpText[i]} B1");
        lines.Add("Sub Main()\n    Console.WriteLine(r0)\nEnd Sub");
        var module = JsTestSupport.BuildModule(string.Join("\n", lines) + "\n");

        Assert.Multiple(() =>
        {
            for (int i = 0; i < Ops.Length; i++)
                Assert.That(((IRConstant)module.GlobalVariables[$"r{i}"].InitialValue).Value, Is.EqualTo(vb[i] == 'T'),
                    $"Dim r As Boolean = {a} As {ta} {OpText[i]} {b} As {tb}");
        });
    }

    /// <summary>The reported blocker, in D1's untyped spelling, module Const and module Dim (vbc: False True False True False).</summary>
    [Test]
    public void TheBlocker_UntypedConstComparedWithAnIntegerLiteral()
    {
        var module = JsTestSupport.BuildModule(
            "Const D = 3.14\nConst N = -3\nConst E As Boolean = D <= 3\nConst F As Boolean = D > 3\nConst G As Boolean = 3 >= D\n"
            + "Const H As Boolean = N < -2.5\nDim gm As Boolean = D <= 3\nSub Main()\n    Console.WriteLine(E)\nEnd Sub\n");

        object Initial(string n) => ((IRConstant)module.GlobalVariables[n].InitialValue).Value;
        Assert.Multiple(() =>
        {
            Assert.That(Initial("E"), Is.EqualTo(false), "D <= 3");
            Assert.That(Initial("F"), Is.EqualTo(true), "D > 3");
            Assert.That(Initial("G"), Is.EqualTo(false), "3 >= D");
            Assert.That(Initial("H"), Is.EqualTo(true), "N < -2.5");
            Assert.That(Initial("gm"), Is.EqualTo(false), "Dim gm = D <= 3");
        });
    }

    /// <summary>
    /// ⛔ An integral pair a Long cannot hold is NOT folded: VB compares a ULong past Long.MaxValue with a Long in
    /// Decimal, which the fold does not do. (BasicLang source cannot spell that constant — the lexer refuses the
    /// literal and a hex literal wraps to Long — so this is asserted at the pass.)
    /// </summary>
    [Test]
    public void AULongPastLongMaxValue_IsNotFolded()
    {
        Assert.Multiple(() =>
        {
            foreach (var op in Ops)
            {
                Assert.That(Fold(op, ulong.MaxValue, "ULong", -1L, "Long"), Is.Null, $"ULong.MaxValue {op} -1L");
                Assert.That(Fold(op, -1, "Integer", ulong.MaxValue, "ULong"), Is.Null, $"-1 {op} ULong.MaxValue");
            }
            // ...while one that fits is an ordinary integral pair.
            Assert.That(Fold(CompareKind.Gt, (ulong)long.MaxValue, "ULong", -1L, "Long"), Is.True, "Long.MaxValue As ULong > -1L");
        });
    }

    /// <summary>
    /// Same-type pairs and non-numeric pairs keep the answers they had. ⚠ The String row is NOT VB's answer — "20" = 20 is
    /// True in VB, converted — and is asserted as the PASS's answer: the fold sees two CLR values, not VB's conversion. #214
    /// no longer routes an Object operand here (copy propagation keeps it, so the late-bound comparison answers); a typed or
    /// literal pair still arrives, because the front end inserts no VB conversion for it. TryFoldCompare is unchanged.
    /// </summary>
    [Test]
    public void SameTypeAndNonNumericPairs_AreUnchanged()
    {
        var str = "String";
        Assert.Multiple(() =>
        {
            Assert.That(Fold(CompareKind.Lt, 3.0, "Double", 3.14, "Double"), Is.True, "Double/Double");
            Assert.That(Fold(CompareKind.Eq, 7, "Integer", 7, "Integer"), Is.True, "Integer/Integer");
            Assert.That(Fold(CompareKind.Eq, "20", str, 20, "Integer"), Is.False, "the pass still answers \"20\" = 20 with Equals (VB: True; an Object operand no longer reaches it, #214)");
            Assert.That(Fold(CompareKind.Ne, "20", str, 20, "Integer"), Is.True, "the pass still answers \"20\" <> 20 with Equals (VB: False; an Object operand no longer reaches it, #214)");
            Assert.That(Fold(CompareKind.Lt, 1.5m, "Decimal", 2, "Integer"), Is.Null, "Decimal is never folded");
        });
    }
}

/// <summary>
/// #123: the mixed-width compare fold, EXECUTED — the forms the pass reaches through the shipping pipeline (a local
/// Const, a propagated local Dim, a module Const), on every backend, against vbc's output.
/// </summary>
[TestFixture]
[Category("Integration")]
public class MixedNumericCompareFoldExecutionTests
{
    // vbc: False True False True False | False True True | pos
    private const string Program = """
        Const D = 3.14
        Const N = -3
        Const E As Boolean = D <= 3
        Const F As Boolean = D > 3
        Const G As Boolean = 3 >= D
        Const H As Boolean = N < -2.5
        Dim gm As Boolean = D <= 3

        Sub Main()
            Console.WriteLine(E)
            Console.WriteLine(F)
            Console.WriteLine(G)
            Console.WriteLine(H)
            Console.WriteLine(gm)
            Const LD = 3.14
            Const LI = 3
            Console.WriteLine(LD <= LI)
            Console.WriteLine(LI = LD)
            Console.WriteLine(LD > LI)
            Dim x As Double = 12
            If x > 0 Then
                Console.WriteLine("pos")
            Else
                Console.WriteLine("nonpos")
            End If
        End Sub
        """;

    private const string Vb = "False\nTrue\nFalse\nTrue\nFalse\nFalse\nFalse\nTrue\npos";

    [Test]
    public void TheMixedCompareProgram_RunsLikeVb_OnEveryBackend()
    {
        // ⚠ RunsOnEveryBackend's JavaScript leg runs NO optimizer, so it never reaches the fold: the
        // standard-pipeline JavaScript run is asserted separately, and it is the one that can fail.
        Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Vb),
            "JavaScript, standard pipeline");
        FourBackends.RunsOnEveryBackend(Program, Vb);
    }

    [Test]
    public void TheMixedCompareProgram_RunsLikeVb_OnEveryBackend_Aggressive()
    {
        FourBackends.RunsOnEveryBackendAggressive(Program, Vb);
    }
}
