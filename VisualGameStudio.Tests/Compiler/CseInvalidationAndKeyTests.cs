using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
// `TypeInfo` is ambiguous with System.Reflection.TypeInfo, which this file needs for the
// private-member pins; the compiler's TypeInfo is the one meant everywhere below.
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

// ============================================================================================
//  CommonSubexpressionEliminationPass — TWO defects in the same three lines.
//
//  D1  The pass keyed candidate expressions on OPERAND NAMES and never invalidated an entry when
//      an operand was REDEFINED, so `a = p + q` / `p = Seed(100)` / `b = p + q` rewrote the second
//      binop to `IRAssignment(b, a)` and `b` got the value computed BEFORE `p` changed.
//
//  D2  The key encoding `$"{Op}_{Left.Name}_{Right.Name}"` joined with an UNESCAPED `_`, and a
//      BasicLang identifier may contain `_`. `p + q_r` and `p_q + r` both keyed `Add_p_q_r`, so
//      two UNRELATED expressions were merged.
//
//  ============================================================================================
//  ⛔⛔ WHICH BACKEND CAN KILL WHICH ASSERTION — READ THIS BEFORE EDITING ANY CASE BELOW.
//
//  This family is the counter-example to "assert it on C# and move on". MEASURED, per leg, at the
//  defective tree and at the fixed tree:
//
//   • REDEFINITION shapes (D1): C++, JavaScript and MSIL printed the STALE answer; **C# printed
//     the CORRECT answer with the defect fully present.** C#'s inline-always policy re-emits the
//     binop's expression TEXT (`b = p + q;`) instead of honouring the `IRAssignment` the pass
//     wrote, so the merge is invisible to it. A C#-only fixture for D1 is VACUOUS. C# is still
//     asserted here — the property is "the backends agree", and a C# leg that started honouring
//     the IR would be a real change — but it proves NOTHING about D1 on its own, and every such
//     assertion below says so in its own failure message.
//
//   • The ByRef shape: JavaScript is excluded. It REFUSES ByRef by design (BL7002, measured —
//     "ByRef parameter 'k' in Sub 'Bump' cannot be lowered to JavaScript"). C++ and MSIL are the
//     two load-bearing legs there, and they are exactly the two nobody checks first.
//
//   • KEY-INJECTIVITY shapes (D2): ⭐ ALL FOUR legs kill, C# INCLUDED, because the two merged
//     expressions differ in TEXT — so inline-always faithfully re-emits the WRONG text. This
//     sharpens ADR-0001's trap note: C#'s inlining rescues a bad merge only when the two
//     expressions are textually identical. It is not general immunity.
//
//   • "The merge that must STILL happen": NO backend can kill by value. Over-killing produces no
//     wrong answer, only a silently deleted optimization. Those are asserted STRUCTURALLY, as
//     `ModificationCount` from a SINGLE `pass.Run` — see <see cref="CseInvalidationDecisionTests"/>.
//
//   • The CASE-DIFFERING shape is asserted STRUCTURALLY ONLY, and not by value on any backend.
//     MEASURED: C++ does not case-fold identifiers and refuses to compile it ("use of undeclared
//     identifier 'P'"); JavaScript does not case-fold either and emits a SEPARATE `P`, printing
//     `b=4` — the very same wrong number CSE's defect produced, so the JS leg cannot distinguish
//     the two causes. Only MSIL and C# print `b=106`, and C# is vacuous here. Those are live
//     PRE-EXISTING backend defects unrelated to CSE; see docs/HANDOFF.md.
//
//  ⚠ `FourBackends.RunsOnEveryBackend`'s JavaScript leg is the NON-OPTIMIZING path. CSE is a
//    STANDARD pass, so the JS leg here is `JavaScriptOptimizedExecutionTests.RunOptimized`
//    (`JsTestSupport.CompileOptimized` = `AddStandardPasses`). The aggressive runner is the wrong
//    pipeline for this defect.
//
//  ⚠ Both execution harnesses use `Assert.Multiple`, and the C# leg is in-process Roslyn with no
//    timeout: ONE shape per test.
// ============================================================================================

/// <summary>
/// The shapes, as source. Each is a MEASURED near-miss away from proving nothing — the comments
/// on each one say which way.
/// </summary>
internal static class CseShapes
{
    /// <summary>
    /// A mutable module global plus a function that writes it and returns the new value. Every
    /// redefinition shape is built on <c>Seed</c> rather than on a literal DELIBERATELY:
    ///
    /// <para>⛔ MEASURED — <c>p = 50</c> and <c>p = r</c> are GREEN AT THE DEFECTIVE TREE.
    /// <c>CopyPropagationPass</c> runs before CSE and rewrites the operand out of the key, which
    /// accidentally masks the defect. A literal or copy redefinition tests NOTHING. The
    /// redefinition must be a CALL RESULT or <c>p = p + 10</c>.</para>
    /// </summary>
    internal const string Seeder = """
        Dim Counter As Integer

        Function Seed(k As Integer) As Integer
         Counter = Counter + k
         Return Counter
        End Function

        Sub Show(v As Integer)
         PrintLine("S=" & CStr(v))
        End Sub

        """;

    /// <summary>
    /// THE REPORTED SHAPE. <c>p</c> is redefined by a call BETWEEN the two uses.
    ///
    /// <para>⚠ Moving <c>p = Seed(100)</c> ABOVE <c>Dim a</c> makes the merge LEGAL and the
    /// program green — see <see cref="RedefinitionBeforeBothUses"/>, which is that exact
    /// off-by-one-statement shape kept as a control.</para>
    /// </summary>
    internal const string Reported = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         p = Seed(100)
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// A SELF-redefinition: <c>p = p + 10</c> lowers to ONE <c>IRBinaryOp</c> renamed <c>p</c>
    /// that both READS and WRITES <c>p</c>, with no <c>IRAssignment</c> anywhere. The entry
    /// recorded for <c>a = p + q</c> must die here.
    ///
    /// <para>⛔ THIS SHAPE DOES **NOT** TEST KILL-ORDERING, although it looks as though it should
    /// and was originally expected to. MEASURED with a kill-BEFORE-lookup mutant applied: this
    /// program still prints the right answer and still merges nothing. Killing first does leave
    /// the binop's own stale record alive — but nothing in this block ever LOOKS THAT RECORD UP,
    /// so the staleness is unobservable. <see cref="SelfRedefinitionRecordIsReused"/> is the shape
    /// that actually distinguishes the two orders; see its docstring.</para>
    /// </summary>
    internal const string SelfRedefinition = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         p = p + 10
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// ⭐⭐ THE SHAPE THAT ACTUALLY TESTS KILL-ORDERING — and the only one in the fixture that
    /// does. The difference from <see cref="SelfRedefinition"/> is the LAST statement: the
    /// self-redefining binop's own record is LOOKED UP AGAIN.
    ///
    /// <para><c>p = p + 10</c> records the key <c>Add|p|const_10|Integer</c> against a value that
    /// is already stale — it was computed from the OLD <c>p</c>. The fix orders the step
    /// use → record → kill, so the kill that immediately follows removes THAT ENTRY TOO (its read
    /// set contains <c>p</c>, and <c>p</c> is what this instruction redefines). Kill FIRST and the
    /// entry is recorded after the kill, survives, and <c>c = p + 10</c> is rewritten to
    /// <c>c = p</c>.</para>
    ///
    /// <para>⛔ MEASURED, kill-before-lookup mutant applied: 1 merge instead of 0, and C++,
    /// JavaScript and MSIL print <c>c=11</c> where 21 is correct. C# prints <c>c=21</c> — vacuous,
    /// as everywhere in the redefinition family. Correct output is <c>a=4</c> and <c>c=21</c>
    /// (<c>p</c> is 11 after the self-redefinition, so <c>c = 11 + 10</c>).</para>
    ///
    /// <para>⚠ Deleting the <c>c</c> line, or changing its <c>+ 10</c> to anything else, turns
    /// this back into <see cref="SelfRedefinition"/> and silently stops testing the ordering.</para>
    /// </summary>
    internal const string SelfRedefinitionRecordIsReused = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         p = p + 10
         Dim c As Integer = p + 10
         PrintLine("a=" & CStr(a))
         PrintLine("c=" & CStr(c))
        End Sub
        """;

    /// <summary>The RIGHT operand slot, not the left — the read set must cover both.</summary>
    internal const string RightOperandRedefinition = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         q = Seed(100)
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// The TEMP arm of the merge — the duplicate is a call argument, so the pass REMOVES the
    /// instruction (<c>RemoveAt(i); i--</c>) instead of converting it to an assignment. That is
    /// also the arm behind the index bug the fix closed: invalidating on
    /// <c>block.Instructions[i]</c> after the decrement could index −1.
    /// </summary>
    internal const string TempDestination = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Show(p + q)
         p = Seed(100)
         Show(p + q)
        End Sub
        """;

    /// <summary>
    /// ⛔ NO SYNTACTIC REDEFINITION EXISTS ANYWHERE IN <c>Main</c> — the write to <c>Counter</c>
    /// happens inside <c>Seed</c>. A fixture that scans the block for a write will not find one.
    /// Only "any call kills an entry that reads a MUTABLE global" covers it.
    /// </summary>
    internal const string MutableGlobalAcrossACall = Seeder + """
        Sub Main()
         Dim q As Integer = Seed(2)
         Dim a As Integer = Counter + q
         Dim z As Integer = Seed(100)
         Dim b As Integer = Counter + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// The ByRef argument: <c>Bump(p)</c> writes <c>p</c> through a reference parameter. There is
    /// no <c>IRAssignment</c> and no rename for that write in this block.
    /// <para>⛔ JavaScript cannot run this shape at all — BL7002.</para>
    /// </summary>
    internal const string ByRefArgument = """
        Dim Counter As Integer

        Sub Bump(ByRef k As Integer)
         Counter = Counter + 100
         k = Counter
        End Sub

        Function Seed(k As Integer) As Integer
         Counter = Counter + k
         Return Counter
        End Function

        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         Bump(p)
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// BasicLang identifiers are CASE-INSENSITIVE, so <c>P = Seed(100)</c> redefines <c>p</c>.
    /// MEASURED at IR level: the redefinition lowers to <c>IRCall("P")</c> while both binops read
    /// <c>IRVariable("p")</c> — so the kill must compare case-INsensitively.
    ///
    /// <para>⛔ ASSERTED STRUCTURALLY ONLY. C++ refuses to compile this program ("use of
    /// undeclared identifier 'P'") and JavaScript emits a separate <c>P</c> and prints <c>b=4</c>,
    /// which is the SAME wrong number the CSE defect produced — so neither leg can attribute a
    /// failure. Both are live pre-existing backend case-folding defects, recorded in
    /// docs/HANDOFF.md, and neither is CSE's.</para>
    /// </summary>
    internal const string CaseDifferingRedefinition = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         P = Seed(100)
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    // ---------------------------------------------------------------------- key injectivity

    /// <summary>
    /// ⛔⛔ THE UNDERSCORES ARE LOAD-BEARING. <c>q_r</c> and <c>p_q</c> exist so that the OLD key
    /// <c>$"{Op}_{Left.Name}_{Right.Name}"</c> renders <c>Add_p_q_r</c> for BOTH <c>p + q_r</c>
    /// and <c>p_q + r</c>. Renaming them to <c>qr</c> / <c>pq</c> while tidying silently disarms
    /// this test and it will pass against the defect.
    ///
    /// <para>Correct output is <c>a=12</c> (1+11) and <c>b=1222</c> (111+1111). The defect printed
    /// <c>b=12</c> on ALL FOUR backends.</para>
    /// </summary>
    internal const string KeyCollisionInteger = """
        Dim Counter As Integer

        Function Seed(k As Integer) As Integer
         Counter = Counter + k
         Return Counter
        End Function

        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q_r As Integer = Seed(10)
         Dim p_q As Integer = Seed(100)
         Dim r As Integer = Seed(1000)
         Dim a As Integer = p + q_r
         Dim b As Integer = p_q + r
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// ⛔⛔ THE UNDERSCORES ARE LOAD-BEARING, twice over: in the IDENTIFIER <c>b_c</c> and in the
    /// STRING LITERAL <c>"a_b"</c> — <c>IRConstant</c> names itself <c>const_{value}</c>, so the
    /// literal's underscore lands in the key too. <c>"a" &amp; b_c</c> and <c>"a_b" &amp; c</c>
    /// both rendered <c>Concat_const_a_b_c</c> under the old encoding.
    ///
    /// <para>Correct output is <c>s1=aB1</c> and <c>s2=a_bC2</c>. The defect printed
    /// <c>s2=aB1</c> on ALL FOUR backends.</para>
    /// </summary>
    internal const string KeyCollisionString = """
        Dim Counter As Integer

        Function Mk(k As String) As String
         Counter = Counter + 1
         Return k & CStr(Counter)
        End Function

        Sub Main()
         Dim b_c As String = Mk("B")
         Dim c As String = Mk("C")
         Dim s1 As String = "a" & b_c
         Dim s2 As String = "a_b" & c
         PrintLine("s1=" & s1)
         PrintLine("s2=" & s2)
        End Sub
        """;

    // ---------------------------------------------------------------------- MUST STILL MERGE

    /// <summary>
    /// ⛔ THE CONTROL THAT STOPS EVERY "0 MERGES" ROW BEING VACUOUS. A pass that never merges
    /// anything satisfies every "do not merge across a redefinition" assertion trivially. This
    /// shape has no redefinition at all and MUST still merge.
    /// </summary>
    internal const string NoRedefinition = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// ⛔⛔ THE <c>Const</c> EXEMPTION, WHICH IS WHAT KEEPS THE PASS WORTH HAVING. A call kills
    /// entries that read a MUTABLE global; a <c>Const</c> global is exempt. Drop the exemption and
    /// this merge — and every merge the repo's sample games make — vanishes SILENTLY, with no
    /// wrong answer on any backend to notice it by. That is why this row is asserted as a COUNT.
    ///
    /// <para><c>px</c> is a PARAMETER rather than a local initialised from a literal, so constant
    /// folding cannot collapse the expression and hide the question.</para>
    /// </summary>
    internal const string ConstGlobalAcrossACall = """
        Const TILE As Integer = 40

        Sub Draw(v As Integer)
         PrintLine("D=" & CStr(v))
        End Sub

        Sub Plot(px As Integer)
         Dim a As Integer = px * TILE
         Draw(a)
         Dim b As Integer = px * TILE
         Draw(b)
        End Sub

        Sub Main()
         Plot(3)
        End Sub
        """;

    /// <summary>Only parameters: no global at all, so a call must not kill it either.</summary>
    internal const string ParametersAcrossACall = """
        Sub Draw(v As Integer)
         PrintLine("D=" & CStr(v))
        End Sub

        Sub Plot(px As Integer, py As Integer)
         Dim a As Integer = px + py
         Draw(a)
         Dim b As Integer = px + py
         Draw(b)
        End Sub

        Sub Main()
         Plot(3, 4)
        End Sub
        """;

    /// <summary>
    /// ⚠ <see cref="Reported"/> WITH THE REDEFINITION MOVED ONE STATEMENT UP. The merge is now
    /// LEGAL and must still happen. Kept because it is the exact edit that turns the reported
    /// fixture into a no-op, and a reader tidying the shapes is one line away from making it.
    /// </summary>
    internal const string RedefinitionBeforeBothUses = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         p = Seed(100)
         Dim a As Integer = p + q
         Dim b As Integer = p + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    // ---------------------------------------------------------------------- non-candidates

    /// <summary>CONTRACT ITEM 4: scope is per BASIC BLOCK. The second use is inside an <c>If</c>.</summary>
    internal const string CrossBlock = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Integer = p + q
         If q > 0 Then
          Dim b As Integer = p + q
          PrintLine("b=" & CStr(b))
         End If
         PrintLine("a=" & CStr(a))
        End Sub
        """;

    /// <summary>
    /// ⚠ <c>IRUnaryOp</c> was CORRECT at the defective tree and is not a candidate arm — the pass
    /// only ever considers <c>IRBinaryOp</c>. Pinned as a non-candidate so a future author does
    /// not "extend" CSE to unary without also extending invalidation.
    /// </summary>
    internal const string UnaryOperand = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim a As Integer = -p
         p = Seed(100)
         Dim b As Integer = -p
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>⚠ <c>IRCompare</c>, likewise: correct at the defective tree, not a candidate.</summary>
    internal const string CompareOperand = Seeder + """
        Sub Main()
         Dim p As Integer = Seed(1)
         Dim q As Integer = Seed(2)
         Dim a As Boolean = p > q
         p = Seed(100)
         Dim b As Boolean = p > q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;

    /// <summary>
    /// ⭐ WHY THE <c>default:</c> ARM OF <c>ReadsCallVisible</c> IS UNREACHABLE IN PRACTICE, as a
    /// shape. A field read lowers to its OWN instruction carrying a FRESHLY MINTED temp name —
    /// MEASURED, <c>IRFieldAccess("t1")</c> and <c>IRFieldAccess("t3")</c> for the two reads of
    /// <c>c.V</c>. Two such operands therefore never key alike, so an entry recorded through the
    /// conservative default arm can never be matched by a second lookup and the arm can never
    /// change a decision. This shape merges NOTHING before or after the fix: it is a CONTROL.
    /// </summary>
    internal const string FieldReads = """
        Class Box
         Public V As Integer
         Public Sub Bump()
          V = V + 100
         End Sub
        End Class

        Sub Main()
         Dim c As New Box()
         c.V = 1
         Dim q As Integer = 3
         Dim a As Integer = c.V + q
         c.Bump()
         Dim b As Integer = c.V + q
         PrintLine("a=" & CStr(a))
         PrintLine("b=" & CStr(b))
        End Sub
        """;
}

/// <summary>
/// The four backends, all through the STANDARD pipeline — the one CSE actually ships in.
/// </summary>
internal static class CseBackends
{
    private static string Cpp(string p) => FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(p)));

    /// <summary>⚠ The OPTIMIZED JS runner. <c>JavaScriptExecutionTests.RunJs</c> runs NO optimizer.</summary>
    private static string Js(string p) => FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(p));

    private static string Msil(string p) => FourBackends.Norm(Tests.Msil.MsilHarness.RunExpectingSuccess(p));

    private static string CSharp(string p) => FourBackends.Norm(FourBackends.RunEmittedCSharp(p));

    private const string CsVacuous =
        "C# — ⛔ VACUOUS FOR THIS DEFECT: measured, C# printed this same correct answer with the "
        + "defect fully present, because inline-always re-emits the binop's expression TEXT "
        + "instead of honouring the IRAssignment the merge wrote. The load-bearing legs are C++, "
        + "JavaScript and MSIL.";

    /// <summary>All four legs; C# is asserted but proves nothing about a redefinition defect.</summary>
    internal static void RedefinitionShape(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo(expected), "C++ — load-bearing");
            Assert.That(Js(program), Is.EqualTo(expected), "JavaScript (optimized) — load-bearing");
            Assert.That(Msil(program), Is.EqualTo(expected), "MSIL — load-bearing");
            Assert.That(CSharp(program), Is.EqualTo(expected), CsVacuous);
        });
    }

    /// <summary>
    /// C++ and MSIL only. ⛔ JavaScript is EXCLUDED because it REFUSES ByRef by design — measured,
    /// BL7002 "ByRef parameter 'k' in Sub 'Bump' cannot be lowered to JavaScript". C# is asserted
    /// for agreement and, as everywhere in the redefinition family, proves nothing.
    /// </summary>
    internal static void ByRefShape(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo(expected), "C++ — load-bearing (JS excluded: BL7002 refuses ByRef)");
            Assert.That(Msil(program), Is.EqualTo(expected), "MSIL — load-bearing (JS excluded: BL7002 refuses ByRef)");
            Assert.That(CSharp(program), Is.EqualTo(expected), CsVacuous);
        });
    }

    /// <summary>
    /// ⭐ All four legs, C# INCLUDED AND LOAD-BEARING. A key collision merges two expressions that
    /// differ in TEXT, so inline-always re-emits the WRONG text and C# prints the wrong answer too.
    /// This is the only place in the family where the usual oracle earns its keep.
    /// </summary>
    internal static void KeyCollisionShape(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo(expected), "C++ — load-bearing");
            Assert.That(Js(program), Is.EqualTo(expected), "JavaScript (optimized) — load-bearing");
            Assert.That(Msil(program), Is.EqualTo(expected), "MSIL — load-bearing");
            Assert.That(CSharp(program), Is.EqualTo(expected),
                "C# — ⭐ LOAD-BEARING HERE, unlike the redefinition shapes: the two merged "
                + "expressions differ in TEXT, so inlining reproduces the wrong one.");
        });
    }
}

/// <summary>
/// ⭐ THE DECISION, not the answer: how many merges the pass makes on each shape, from a SINGLE
/// <c>pass.Run</c>.
///
/// <para>⛔ WHY THIS FIXTURE EXISTS AT ALL. Over-killing — invalidating an entry that did not need
/// it — produces NO wrong answer on ANY backend. It silently deletes the optimization. There is no
/// value assertion that can see it, so the "must still merge" rows below are the ONLY thing
/// standing between this pass and a repair that quietly turns it off. They are also what stops
/// every "0 merges" row being satisfied trivially by a pass that never merges anything.</para>
///
/// <para>⚠ <c>ModificationCount</c> must be read from a SINGLE <c>pass.Run</c>. After
/// <c>OptimizationPipeline.Run</c> it is ALWAYS 0 — the pipeline iterates to a fixed point and the
/// last run of each pass is by definition the one that changed nothing.</para>
///
/// <para>⚠ Running CSE ALONE also means no <c>CopyPropagationPass</c> and no
/// <c>ConstantFoldingPass</c> have touched the IR, which is deliberate: those two are what mask
/// the defect on a literal or copy redefinition. The counts here are the pass's own decision.</para>
/// </summary>
[TestFixture]
public class CseInvalidationDecisionTests
{
    private static int Merges(string program)
    {
        var module = JsTestSupport.BuildModule(program, sourceFilePath: "prog.bas");
        var pass = new CommonSubexpressionEliminationPass();
        pass.Run(module);
        return pass.ModificationCount;
    }

    // ---- MUST NOT MERGE -------------------------------------------------------------------
    [TestCase(nameof(CseShapes.Reported), 0, TestName = "Decision_ReportedCallRedefinition_DoesNotMerge")]
    [TestCase(nameof(CseShapes.SelfRedefinition), 0, TestName = "Decision_SelfRedefinition_DoesNotMerge")]
    [TestCase(nameof(CseShapes.SelfRedefinitionRecordIsReused), 0, TestName = "Decision_SelfRedefinitionRecordIsReused_DoesNotMerge")]
    [TestCase(nameof(CseShapes.RightOperandRedefinition), 0, TestName = "Decision_RightOperandRedefinition_DoesNotMerge")]
    [TestCase(nameof(CseShapes.TempDestination), 0, TestName = "Decision_TempDestination_DoesNotMerge")]
    [TestCase(nameof(CseShapes.MutableGlobalAcrossACall), 0, TestName = "Decision_MutableGlobalAcrossACall_DoesNotMerge")]
    [TestCase(nameof(CseShapes.ByRefArgument), 0, TestName = "Decision_ByRefArgument_DoesNotMerge")]
    [TestCase(nameof(CseShapes.CaseDifferingRedefinition), 0, TestName = "Decision_CaseDifferingRedefinition_DoesNotMerge")]
    [TestCase(nameof(CseShapes.KeyCollisionInteger), 0, TestName = "Decision_KeyCollisionInteger_DoesNotMerge")]
    [TestCase(nameof(CseShapes.KeyCollisionString), 0, TestName = "Decision_KeyCollisionString_DoesNotMerge")]
    [TestCase(nameof(CseShapes.CrossBlock), 0, TestName = "Decision_CrossBlock_DoesNotMerge")]
    [TestCase(nameof(CseShapes.UnaryOperand), 0, TestName = "Decision_UnaryIsNotACandidate")]
    [TestCase(nameof(CseShapes.CompareOperand), 0, TestName = "Decision_CompareIsNotACandidate")]
    [TestCase(nameof(CseShapes.FieldReads), 0, TestName = "Decision_FieldReadsMintFreshTempsAndNeverKeyAlike")]
    // ---- ⭐ MUST STILL MERGE — the rows that make every row above non-vacuous --------------
    [TestCase(nameof(CseShapes.NoRedefinition), 1, TestName = "Decision_NoRedefinition_STILL_MERGES")]
    [TestCase(nameof(CseShapes.ConstGlobalAcrossACall), 1, TestName = "Decision_ConstGlobalAcrossACall_STILL_MERGES")]
    [TestCase(nameof(CseShapes.ParametersAcrossACall), 1, TestName = "Decision_ParametersAcrossACall_STILL_MERGES")]
    [TestCase(nameof(CseShapes.RedefinitionBeforeBothUses), 1, TestName = "Decision_RedefinitionBeforeBothUses_STILL_MERGES")]
    public void CseMakesExactlyThisManyMergesTest(string shapeName, int expectedMerges)
    {
        var program = (string)typeof(CseShapes)
            .GetField(shapeName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.That(Merges(program), Is.EqualTo(expectedMerges),
            expectedMerges == 0
                ? $"{shapeName}: CSE must make NO merge here. A merge is a MISCOMPILE — see the "
                  + "shape's own docstring for what it reads and what redefines it."
                : $"{shapeName}: CSE must STILL make exactly {expectedMerges} merge(s) here. "
                  + "⛔ Zero is NOT a safe failure: over-killing produces no wrong answer on any "
                  + "backend, it silently deletes the optimization, and this assertion is the only "
                  + "thing that can see it.");
    }
}

/// <summary>
/// The redefinition shapes, COMPILED AND RUN. See the per-backend killability block at the top of
/// this file: C++, JavaScript and MSIL are the load-bearing legs; C# printed the correct answer
/// with the defect present and is asserted only for agreement.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public class CseInvalidationExecutionTests
{
    /// <summary>
    /// THE REPORTED SHAPE. <c>b</c> must be 106 — <c>p</c> was 1 when <c>a</c> was computed and
    /// 103 afterwards, so <c>b = 103 + 3</c>. The defect printed <c>b=4</c> on C++, JavaScript and
    /// MSIL.
    /// </summary>
    [Test]
    public void MergeDoesNotCrossACallRedefinitionTest() =>
        CseBackends.RedefinitionShape(CseShapes.Reported, "a=4\nb=106");

    /// <summary>
    /// A SELF-redefinition through ONE instruction that both reads and writes <c>p</c>, with no
    /// <c>IRAssignment</c> at all. <c>b</c> must be 14 (<c>p</c> becomes 11, <c>q</c> is 3).
    /// <para>⛔ This shape does NOT test kill-ORDERING — measured, it survives a
    /// kill-before-lookup mutant. <see cref="MergeDoesNotReuseASelfRedefiningBinopsOwnStaleRecordTest"/>
    /// is the one that does.</para>
    /// </summary>
    [Test]
    public void MergeDoesNotCrossASelfRedefinitionTest() =>
        CseBackends.RedefinitionShape(CseShapes.SelfRedefinition, "a=4\nb=14");

    /// <summary>
    /// ⭐⭐ KILL-ORDERING, the assertion that distinguishes use→record→kill from kill→use→record.
    /// <c>c</c> must be 21. With the kill moved before the lookup the self-redefining binop's own
    /// stale record survives and is matched: C++, JavaScript and MSIL print <c>c=11</c>.
    /// <para>⚠ The shape ABOVE does not test this — measured, it survives that mutant. See
    /// <see cref="CseShapes.SelfRedefinitionRecordIsReused"/>.</para>
    /// </summary>
    [Test]
    public void MergeDoesNotReuseASelfRedefiningBinopsOwnStaleRecordTest() =>
        CseBackends.RedefinitionShape(CseShapes.SelfRedefinitionRecordIsReused, "a=4\nc=21");

    /// <summary>The right operand slot. <c>q</c> becomes 103, <c>p</c> stays 1, so <c>b=104</c>.</summary>
    [Test]
    public void MergeDoesNotCrossARedefinitionOfTheRightOperandTest() =>
        CseBackends.RedefinitionShape(CseShapes.RightOperandRedefinition, "a=4\nb=104");

    /// <summary>The temp arm — the duplicate is a call argument and the instruction is REMOVED.</summary>
    [Test]
    public void MergeIntoATempDestinationDoesNotCrossARedefinitionTest() =>
        CseBackends.RedefinitionShape(CseShapes.TempDestination, "S=4\nS=106");

    /// <summary>
    /// A MUTABLE global read across a call, with NO syntactic redefinition in the block at all.
    /// <c>Counter</c> is 2 when <c>a</c> is computed and 102 afterwards, so <c>b=104</c>.
    /// </summary>
    [Test]
    public void MergeReadingAMutableGlobalDiesAtAnyCallTest() =>
        CseBackends.RedefinitionShape(CseShapes.MutableGlobalAcrossACall, "a=4\nb=104");

    /// <summary>
    /// The ByRef write. <c>Bump(p)</c> sets <c>p</c> to 103, so <c>b=106</c>.
    /// <para>⛔ C++ and MSIL only. JavaScript is EXCLUDED: it refuses ByRef with BL7002, measured,
    /// so it cannot run this program at all — and those two are the only legs that can see this
    /// defect, since C# is vacuous for the whole redefinition family.</para>
    /// </summary>
    [Test]
    public void MergeReadingAByRefArgumentDiesAtThatCallTest() =>
        CseBackends.ByRefShape(CseShapes.ByRefArgument, "a=4\nb=106");
}

/// <summary>
/// D2 — the key encoding. ⭐ The only assertions in this change where the C# backend proves
/// something, because the two wrongly-merged expressions differ in TEXT.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public class CseKeyInjectivityExecutionTests
{
    /// <summary>
    /// <c>p + q_r</c> and <c>p_q + r</c>. ⛔ Do not rename <c>q_r</c> or <c>p_q</c> — the
    /// underscores are what made the old keys collide, and renaming them disarms the test
    /// silently. The defect printed <c>b=12</c> on all four backends.
    /// </summary>
    [Test]
    public void ExpressionsWhoseOldKeysCollidedAreNotMergedIntegerTest() =>
        CseBackends.KeyCollisionShape(CseShapes.KeyCollisionInteger, "a=12\nb=1222");

    /// <summary>
    /// <c>"a" &amp; b_c</c> and <c>"a_b" &amp; c</c>. ⛔ The underscore in the LITERAL matters as
    /// much as the one in the identifier: <c>IRConstant</c> names itself <c>const_{value}</c>.
    /// The defect printed <c>s2=aB1</c> on all four backends.
    /// </summary>
    [Test]
    public void ExpressionsWhoseOldKeysCollidedAreNotMergedStringTest() =>
        CseBackends.KeyCollisionShape(CseShapes.KeyCollisionString, "s1=aB1\ns2=a_bC2");
}

/// <summary>
/// The key encoding and the call-visibility predicate, asserted DIRECTLY.
///
/// <para>⚠ Both members are private statics reached by reflection, and both pins below cover
/// distinctions that NO BasicLang program currently reaches — measured, and stated per test. They
/// are here because the alternative is leaving two arms of the repair with no coverage at all,
/// and because a key that is injective only over the inputs today's front end happens to produce
/// is not injective.</para>
/// </summary>
[TestFixture]
public class CseKeyEncodingUnitTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo DblType = new TypeInfo("Double", TypeKind.Primitive);

    private static MethodInfo Private(string name)
    {
        var m = typeof(CommonSubexpressionEliminationPass).GetMethod(
            name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(m, Is.Not.Null,
            $"CommonSubexpressionEliminationPass.{name} is gone. It is not merely renamed as far as "
            + "this fixture can tell: the contract it carried (see this fixture's docstring) now "
            + "lives somewhere else and must be re-pinned there, not deleted.");
        return m!;
    }

    private static string Key(BinaryOpKind op, IRValue left, IRValue right, TypeInfo type) =>
        (string)Private("ExpressionKey").Invoke(null, new object[] { new IRBinaryOp("t", op, left, right, type) })!;

    private static IRVariable V(string n) => new IRVariable(n, IntType);

    /// <summary>
    /// ⛔ THE SECOND DEFECT, DIRECTLY. The old encoding was
    /// <c>$"{Operation}_{Left.Name}_{Right.Name}"</c> with an UNESCAPED <c>_</c> delimiter, and
    /// BasicLang identifiers may contain <c>_</c>. Length-prefixing each part makes it injective.
    ///
    /// <para>Every pair below is two DIFFERENT expressions that must get DIFFERENT keys. The first
    /// two pairs are the measured live collisions; the operation and result-type pairs pin the
    /// other two parts of the key.</para>
    /// </summary>
    [Test]
    public void ExpressionKeyIsInjectiveOverAllFourPartsTest()
    {
        Assert.Multiple(() =>
        {
            // ⭐ The measured collision: `p + q_r` vs `p_q + r`. Both were `Add_p_q_r`.
            Assert.That(Key(BinaryOpKind.Add, V("p"), V("q_r"), IntType),
                Is.Not.EqualTo(Key(BinaryOpKind.Add, V("p_q"), V("r"), IntType)),
                "OPERAND SPLIT: `p + q_r` and `p_q + r` are different expressions. The old key "
                + "joined the parts with a bare '_' and rendered both as Add_p_q_r.");

            // ⭐ The same collision through IRConstant, whose Name is `const_{value}`.
            Assert.That(Key(BinaryOpKind.Concat, new IRConstant("a", IntType), V("b_c"), IntType),
                Is.Not.EqualTo(Key(BinaryOpKind.Concat, new IRConstant("a_b", IntType), V("c"), IntType)),
                "OPERAND SPLIT THROUGH A LITERAL: `\"a\" & b_c` and `\"a_b\" & c`. IRConstant names "
                + "itself const_{value}, so a literal's underscore lands in the key too.");

            // The operation must be part of the key at all.
            Assert.That(Key(BinaryOpKind.Add, V("p"), V("q"), IntType),
                Is.Not.EqualTo(Key(BinaryOpKind.Mul, V("p"), V("q"), IntType)),
                "OPERATION: `p + q` and `p * q` must not share a key.");

            // ⚠ THE RESULT TYPE. MEASURED: no BasicLang program currently produces two binops with
            // the same operation and the same operand NAMES but different result types — within one
            // basic block a name denotes one variable, so the operands determine the result type,
            // and `Dim d As Double = p + q` lowers to an Integer IRBinaryOp plus an IRCast rather
            // than to a Double IRBinaryOp. This pin is therefore DEFENSIVE: it is what keeps the
            // key injective if that ever stops being true, and it is asserted here rather than
            // through a program because no program can express it.
            Assert.That(Key(BinaryOpKind.Add, V("p"), V("q"), IntType),
                Is.Not.EqualTo(Key(BinaryOpKind.Add, V("p"), V("q"), DblType)),
                "RESULT TYPE: Operation plus operand names does not determine the result type, and "
                + "an entry is substituted for its match BY NAME. ⚠ No program reaches this today "
                + "(measured) — the pin is defensive, see the comment above it.");
        });
    }

    /// <summary>
    /// ⚠ THE CONSERVATIVE <c>default:</c> ARM of <c>ReadsCallVisible</c>. Anything whose shape the
    /// walk does not enumerate — a field load, an allocation, a nested call used directly as a
    /// binop operand — must count as storage a CALL can write, because over-killing costs
    /// optimization while keeping a stale entry MISCOMPILES.
    ///
    /// <para>⚠ MEASURED: no program can currently reach this arm in a way that changes a decision.
    /// Every non-variable operand lowers to its own instruction carrying a FRESHLY MINTED temp name
    /// (<c>IRFieldAccess("t1")</c> and <c>IRFieldAccess("t3")</c> for two reads of <c>c.V</c>), so
    /// an entry recorded through this arm can never be matched by a second lookup and is never
    /// substituted. <see cref="CseShapes.FieldReads"/> is that measurement as a program, pinned at
    /// 0 merges. The arm is asserted here rather than through a program for that reason — it is
    /// the polarity that matters, and flipping it is invisible from outside.</para>
    /// </summary>
    [Test]
    public void UnrecognisedOperandShapesAreTreatedAsCallVisibleTest()
    {
        var readsCallVisible = Private("ReadsCallVisible");
        bool Reads(IRValue v) => (bool)readsCallVisible.Invoke(null, new object[] { v })!;

        Assert.Multiple(() =>
        {
            Assert.That(Reads(new IRFieldAccess("t1", V("c"), "V", IntType)), Is.True,
                "an unenumerated operand shape (here a field load) must be treated as storage a "
                + "call can write — the conservative default");

            Assert.That(Reads(new IRVariable("g", IntType) { IsGlobal = true }), Is.True,
                "a MUTABLE global is call-visible");
            Assert.That(Reads(new IRVariable("k", IntType) { IsByRef = true }), Is.True,
                "a ByRef operand is call-visible");

            // ⛔ THE EXEMPTION THAT KEEPS THE PASS WORTH HAVING. Drop it and every merge reading a
            // Const global dies at the next call, silently. CseInvalidationDecisionTests'
            // ConstGlobalAcrossACall row is the same property as a program.
            Assert.That(Reads(new IRVariable("TILE", IntType) { IsGlobal = true, IsConst = true }), Is.False,
                "a Const global is EXEMPT — a call cannot change it, and killing on it would take "
                + "every merge the repo's sample games make");

            Assert.That(Reads(V("local")), Is.False, "a plain local is not call-visible");
            Assert.That(Reads(new IRConstant(7, IntType)), Is.False, "a literal is not call-visible");
            Assert.That(Reads(null), Is.False, "a missing operand is not call-visible");
        });
    }
}

/// <summary>
/// CONTRACT ITEM 2 as the implementer measured it: the merges CSE makes on the repo's sample games
/// must SURVIVE the fix. Over-killing is invisible by value, so this is a COUNT from a single
/// <c>pass.Run</c>.
///
/// <para>⛔⛔ READ THIS BEFORE TRUSTING THE NUMBERS. <b>None of the three sample programs compiles
/// today.</b> Measured through the CLI at this commit:</para>
/// <list type="bullet">
/// <item><c>Samples/Platformer/Main.bas</c> — 2 SEMANTIC errors (line 276, "cannot convert from
/// 'Double' to 'Single'" twice). The parse is clean, so its IR is faithful; in particular
/// <c>TILE_SIZE</c> really is <c>IsGlobal=true, IsConst=true</c> and its merges really do depend on
/// the <c>Const</c> exemption.</item>
/// <item><c>Samples/SpaceShooter/Main.bas</c> — PARSE errors:
/// <c>Const SCREEN_WIDTH = 800</c> has no <c>As</c> clause. The parser records the error and
/// synchronizes past the whole <c>Const</c> block, so those identifiers reach the IR as
/// <c>IsGlobal=false, IsConst=false</c> — MEASURED. SpaceShooter's 5 merges therefore read plain
/// locals and say NOTHING about the <c>Const</c> exemption.</item>
/// <item><c>Samples/Pong/Main.bas</c> has NO ROW HERE AT ALL: it does not reach the IR. With the
/// parse damage above, <c>IRBuilder</c> THROWS on it — "the module-level variable 'ballVY' has an
/// initializer that cannot be computed at compile time". There is no CSE count to pin. "The repo's
/// sample programs" is therefore TWO programs, not three.</item>
/// </list>
///
/// <para>This fixture builds the IR the way the measurement did — front end run, its verdict
/// IGNORED — which is deliberately NOT what <c>JsTestSupport.BuildModule</c> does (that helper
/// throws on a rejected front end, on purpose, and it is right to). The front-end status is
/// asserted alongside the counts so the numbers' provenance is part of the test rather than a
/// comment: if a sample is ever FIXED, this fails loudly and the counts must be re-measured rather
/// than silently drifting.</para>
///
/// <para>⚠ The robust, program-independent form of this contract item is
/// <see cref="CseInvalidationDecisionTests"/>'s <c>ConstGlobalAcrossACall</c> and
/// <c>ParametersAcrossACall</c> rows. Prefer those; this fixture records the corpus measurement.</para>
/// </summary>
[TestFixture]
public class CseSampleCorpusTests
{
    private static string FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <param name="parseClean">Whether the PARSER accepts the sample today.</param>
    /// <param name="analyzeClean">Whether the SEMANTIC ANALYZER accepts it today.</param>
    [TestCase("Platformer", 6, true, false, TestName = "Corpus_Platformer_Makes6Merges")]
    [TestCase("SpaceShooter", 5, false, false, TestName = "Corpus_SpaceShooter_Makes5Merges")]
    public void SampleGameMergesSurviveTheFixTest(string sample, int expectedMerges, bool parseClean, bool analyzeClean)
    {
        var path = FindRepoFile("Samples", sample, "Main.bas");
        Assert.That(path, Is.Not.Null, $"Samples/{sample}/Main.bas not found from {AppContext.BaseDirectory}");

        var source = File.ReadAllText(path);
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        bool analyzed = analyzer.Analyze(ast);

        // ⛔ DELIBERATELY IGNORING BOTH VERDICTS — see the fixture docstring. Every other
        // fixture in this file goes through JsTestSupport.BuildModule, which THROWS on a rejected
        // front end on purpose. That is why the two front-end assertions below exist: they are
        // what keeps this bypass honest.
        var module = new IRBuilder(analyzer).Build(ast, "Corpus");

        var pass = new CommonSubexpressionEliminationPass();
        pass.Run(module);

        Assert.Multiple(() =>
        {
            Assert.That(pass.ModificationCount, Is.EqualTo(expectedMerges),
                $"{sample}: CSE must still make exactly {expectedMerges} merge(s). ⛔ A LOWER number "
                + "is not a safe failure — over-killing deletes the optimization and no backend "
                + "prints a wrong answer for it. A HIGHER number means a merge the invalidation "
                + "should have killed.");

            Assert.That(parser.Errors.Count == 0, Is.EqualTo(parseClean),
                $"{sample}: the PARSER's verdict on this sample changed. The merge count above was "
                + "measured on the IR built from THIS front-end state, with the verdict ignored. "
                + "Re-measure the count before updating it — do not just change the number.");
            Assert.That(analyzed, Is.EqualTo(analyzeClean),
                $"{sample}: the SEMANTIC ANALYZER's verdict on this sample changed. Re-measure the "
                + "merge count above before updating it.");
        });
    }
}
