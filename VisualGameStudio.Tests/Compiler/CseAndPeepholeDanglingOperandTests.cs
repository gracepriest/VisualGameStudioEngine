using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>CommonSubexpressionEliminationPass</c> and <c>PeepholeOptimizationPass</c>: two passes that
/// left a DANGLING OPERAND — a use pointing at an instruction the pass had just removed or
/// replaced. Same family as <see cref="AlgebraicSimplificationTests"/>, which pins the identical
/// omission in a third pass; the difference is that both of these are in
/// <c>AddStandardPasses</c>, so they run with NO optimizer flag on every shipping route, and both
/// broke four-line programs.
///
/// <para><b>D1 — CSE.</b> It carried a PRIVATE four-arm <c>ReplaceAllUses</c> covering only
/// <c>IRBinaryOp</c>, <c>IRUnaryOp</c>, <c>IRStore.Value</c> and <c>IRAssignment.Value</c>. Every
/// other consumer kind kept pointing at the merged-away node. Measured at HEAD 39b3f7e on
/// <c>Show(a + 7)</c> twice: C++ refused to compile ("use of undeclared identifier 't1'") and MSIL
/// assembled and threw <c>InvalidProgramException</c>. That private copy is gone; CSE now uses the
/// base class's single total walker, which is what <c>ConstantFoldingPass</c> and
/// <c>StrengthReductionPass</c> already used.</para>
///
/// <para><b>D2 — Peephole.</b> It replaced instructions at THREE sites (the binary-op arm, the
/// double-negation arm and the not-not arm) and called <c>ReplaceUses</c> at none of them. Worse
/// than an orphan alone: every arm returns an <c>IRAssignment</c>, which is not an
/// <c>IRValue</c>, so a temp destination also lost its DECLARATION. Measured on
/// <c>Show(a + 0)</c>: CS0103 on C#, undeclared identifier on C++, <c>ReferenceError</c> on
/// JavaScript.</para>
///
/// <para><b>And a silent miscompile on the same predicate.</b> Both passes decide "is this
/// destination a real variable?" and both decided it from the SPELLING of the name alone. Measured
/// at HEAD: a class member spelled <c>t0</c> assigned a duplicated expression had its write
/// removed outright by CSE and printed <c>T0=0</c> on all four backends. The gate is now
/// <c>!IsTempDestination(name) || NamedAfterVariable</c> — EITHER disjunct makes it a real
/// variable. ⚠ The peephole half of that gate guards a defect this FIX would otherwise have
/// introduced rather than one HEAD shipped (the pass did not consult the name at all before), and
/// the cases below say which is which. <see cref="CSharpFieldAssignmentCodeGenTests"/> already
/// pinned a member named <c>t0</c> for the BACKEND; the optimizer had no equivalent, and these
/// shapes are it.</para>
///
/// <para>⛔ <b>WHICH BACKEND SEES WHICH DEFECT — measured, and the reason the assertions below are
/// not symmetric.</b> C# and JavaScript are GREEN for D1 at HEAD: both re-materialise the orphan's
/// expression text inline, which happens to be right here. Only C++ and MSIL see it. MSIL is green
/// for D2 in most shapes (it declares locals from its own slot table, not from the IRValue
/// stream). Every case below still asserts all four backends — agreement is the property — but the
/// docstring names the leg that actually detects the defect, because a fixture that asserted only
/// the green legs would be a test that cannot fail.</para>
///
/// <para>⚠ Two cross-backend splits appear below and are NOT regressions. They were confirmed
/// against a control program (<c>ShowB(True)</c> / <c>ShowD(CDbl(3))</c>) that no pass touches:
/// C++ renders <c>CStr(Double)</c> as <c>3.000000</c>, and JavaScript renders <c>CStr(Boolean)</c>
/// in lower case. They are pinned as explicit per-backend expectations rather than normalised
/// away.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // compiles and runs C++, spawns node, assembles and runs IL
[NonParallelizable]         // the C# leg redirects Console.Out (see FourBackends)
public class CseAndPeepholeDanglingOperandTests
{
    // ====================================================================================
    // D1 — CSE, one consumer kind per case.
    // ====================================================================================

    /// <summary>
    /// ⛔ THE reported D1 shape: a duplicate binary op whose consumer is a CALL ARGUMENT.
    ///
    /// <para><b>Detected by C++ and MSIL.</b> At HEAD C++ emitted
    /// <c>t0 = a + 7; Show(t0); Show(t1);</c> and refused to compile; MSIL threw
    /// <c>InvalidProgramException</c>. C# and JavaScript printed the right answer by luck.</para>
    ///
    /// <para>⛔ It must be a CALL ARGUMENT and the duplicate must land on a TEMP. Both are
    /// measured near-misses: <c>IRStore.Value</c> was one of the four arms the private walker
    /// already had, so <c>arr(0) = a + b</c> was green at HEAD and proves nothing (pinned below as
    /// a control); and when the SECOND occurrence is the one assigned to a named variable, CSE
    /// takes its other branch and that was green too (likewise pinned below).</para>
    /// </summary>
    [Test]
    public void ADuplicateBinaryOp_ConsumedByACallArgument_IsRePointedNotOrphaned()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show(a + 7)
             Show(a + 7)
            End Sub
            Sub Main()
             Run(3)
            End Sub
            """, "V=10\nV=10");

    /// <summary>
    /// ⛔ <c>IRReturn.Value</c>, measured live as a missing arm. Independent of the call-argument
    /// case: a fixture that probed only call arguments leaves this one open, which is exactly what
    /// the shared walker's <c>IRReturn</c> arm is for.
    ///
    /// <para>The first occurrence is a NAMED local so it survives as the cached expression, and
    /// the second is the anonymous <c>Return</c> temp that CSE merges away.</para>
    /// </summary>
    [Test]
    public void ADuplicateBinaryOp_ConsumedByAReturn_IsRePointedNotOrphaned()
        => OnFourBackends("""
            Function F(p As Integer, q As Integer) As Integer
             Dim s As Integer = p * q
             PrintLine("S=" & CStr(s))
             Return p * q
            End Function
            Sub Main()
             PrintLine("R=" & CStr(F(3, 4)))
            End Sub
            """, "S=12\nR=12");

    /// <summary>⛔ <c>IRCompare.Left</c> — the duplicate feeds an <c>If</c> condition.</summary>
    [Test]
    public void ADuplicateBinaryOp_ConsumedByAComparison_IsRePointedNotOrphaned()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Show(a + b)
             If (a + b) > 0 Then
              PrintLine("POS")
             End If
            End Sub
            Sub Main()
             Run(1, 2)
            End Sub
            """, "V=3\nPOS");

    /// <summary>
    /// ⛔ <c>IRCast.Value</c>.
    ///
    /// <para>⚠ C++ prints <c>D=3.000000</c> where the other three print <c>D=3</c>. That is a
    /// PRE-EXISTING <c>CStr(Double)</c> formatting difference, not this fix: it reproduces on a
    /// control program with no optimizable expression in it at all. It is pinned per-backend
    /// rather than normalised, so that the day C++ changes its double formatting this case says
    /// so.</para>
    /// </summary>
    [Test]
    public void ADuplicateBinaryOp_ConsumedByACast_IsRePointedNotOrphaned()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub ShowD(p As Double)
             PrintLine("D=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Show(a + b)
             ShowD(CDbl(a + b))
            End Sub
            Sub Main()
             Run(1, 2)
            End Sub
            """, "V=3\nD=3", cppExpected: "V=3\nD=3.000000");

    /// <summary>⛔ <c>IRNewObject.Arguments</c> — the duplicate is a constructor argument.</summary>
    [Test]
    public void ADuplicateBinaryOp_ConsumedByAConstructorArgument_IsRePointedNotOrphaned()
        => OnFourBackends("""
            Class Box
             Public N As Integer
             Public Sub New(v As Integer)
              N = v
             End Sub
            End Class
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Show(a + b)
             Dim x As Box = New Box(a + b)
             Show(x.N)
            End Sub
            Sub Main()
             Run(1, 2)
            End Sub
            """, "V=3\nV=3");

    /// <summary>⛔ <c>IRInstanceMethodCall.Arguments</c>.</summary>
    [Test]
    public void ADuplicateBinaryOp_ConsumedByAnInstanceMethodArgument_IsRePointedNotOrphaned()
        => OnFourBackends("""
            Class Box
             Public N As Integer
             Public Sub Put(v As Integer)
              N = v
             End Sub
            End Class
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Dim x As Box = New Box()
             Show(a + b)
             x.Put(a + b)
             Show(x.N)
            End Sub
            Sub Main()
             Run(1, 2)
            End Sub
            """, "V=3\nV=3");

    /// <summary>
    /// ⛔ CONTROL, and the reason the cases above use a call argument. <c>IRStore.Value</c> was one
    /// of the FOUR arms the deleted private walker already covered, so this program was GREEN on
    /// all four backends at HEAD. It looks like proof that the defect was narrow and it is not
    /// evidence of anything — it is pinned only so that a future reader who reaches for
    /// array-subscript assignment sees, in the fixture, why that shape cannot detect D1.
    /// </summary>
    [Test]
    public void ADuplicateBinaryOp_StoredIntoAnArrayElement_WasAlreadyGreenAtHead()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Dim arr(3) As Integer
             Show(a + b)
             arr(0) = a + b
             Show(arr(0))
            End Sub
            Sub Main()
             Run(1, 2)
            End Sub
            """, "V=3\nV=3");

    /// <summary>
    /// ⛔ CONTROL, and the ORDER is the whole point. When the SECOND occurrence is the one assigned
    /// to a named variable, CSE takes its other branch — convert to an assignment, keep the write
    /// — and that branch was green on all four backends at HEAD.
    ///
    /// <para>⚠ Measured, and it is a genuine near-miss: reversing these two lines
    /// (<c>Dim s As Integer = a + b</c> FIRST, then <c>Show(a + b)</c>) puts the duplicate back on
    /// a TEMP and the program fails at HEAD exactly like the detectors above — C++ "use of
    /// undeclared identifier 't2'", MSIL <c>InvalidProgramException</c>. So "assigned to a named
    /// variable" is not by itself a safe shape; which OCCURRENCE is named is what decides the
    /// branch.</para>
    /// </summary>
    [Test]
    public void ADuplicateBinaryOp_WhoseSecondOccurrenceIsNamed_WasAlreadyGreenAtHead()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Show(a + b)
             Dim s As Integer = a + b
             PrintLine("S=" & CStr(s))
            End Sub
            Sub Main()
             Run(3, 4)
            End Sub
            """, "V=7\nS=7");

    // ====================================================================================
    // D2 — Peephole.
    // ====================================================================================

    /// <summary>
    /// ⛔ THE reported D2 shape, six lines and no flags.
    ///
    /// <para><b>Detected by C#, C++ and JavaScript — NOT by MSIL</b>, which declares locals from
    /// its own slot table and was right by luck. At HEAD this emitted <c>t0 = a;</c> against an
    /// undeclared <c>t0</c> (CS0103 / undeclared identifier / <c>ReferenceError</c>) and the
    /// orphaned consumer re-materialised <c>a + 0</c> inline.</para>
    ///
    /// <para>The MSIL leg is still asserted: it is the backend that must not regress while the
    /// other three are being fixed.</para>
    /// </summary>
    [Test]
    public void APeepholeRewrittenAddZero_LeavesNoUndeclaredTemp()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show(a + 0)
            End Sub
            Sub Main()
             Run(3)
            End Sub
            """, "V=3");

    /// <summary>
    /// ⛔ TWO rewritable binary ops in ONE block: both rewrites must land, and both consumers must
    /// be forwarded to the right value. At HEAD this produced TWO undeclared temps
    /// (<c>t0</c> and <c>t1</c> in the same C# method), so it fails one way at HEAD and a different
    /// way if the forwarding picks the removed temp's target instead of its value.
    ///
    /// <para>⚠ The two operands differ (<c>a</c> and <c>b</c>) deliberately: identical operands
    /// would be merged by CSE first and only one rewrite would reach the peephole pass.</para>
    ///
    /// <para>⚠ This does NOT pin the pass's loop-index step-back, although two occurrences look
    /// like the shape that would. Measured: the enclosing <c>do/while(changed)</c> loop re-runs and
    /// masks a missing step-back completely, here and even for two ADJACENT rewritable ops — see
    /// <c>CseAndPeepholeIrIdentityTests.TwoAdjacentTempRewrites_AreBothRemovedAndForwarded</c>,
    /// which records the measurement.</para>
    /// </summary>
    [Test]
    public void TwoPeepholeRewritesInOneBlock_AreBothApplied()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             Show(a + 0)
             Show(b + 0)
            End Sub
            Sub Main()
             Run(3, 5)
            End Sub
            """, "V=3\nV=5");

    /// <summary>⛔ <c>x - x -&gt; 0</c>, the same rewrite through a different arm.</summary>
    [Test]
    public void APeepholeRewrittenSelfSubtraction_LeavesNoUndeclaredTemp()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show(a - a)
            End Sub
            Sub Main()
             Run(4)
            End Sub
            """, "V=0");

    /// <summary>
    /// ⛔ EFFECTS ARE PRESERVED: <c>Show(Tag() * 0)</c> calls <c>Tag()</c> EXACTLY ONCE and prints
    /// <c>V=0</c>.
    ///
    /// <para>This is the case the orphan made worst. <c>x * 0</c> discards its operand, and the
    /// orphaned consumer re-rendered the discarded operand TREE inline — a value-preserving
    /// rewrite that was not effect-preserving, which is the hazard ADR-0001 records for
    /// <c>AlgebraicSimplificationPass</c>. Measured at HEAD: C# CS0103 <c>t1</c>, C++ two errors
    /// (including an ambiguous <c>operator=</c> from the re-rendered tree), JavaScript printed one
    /// <c>T</c> and then <c>ReferenceError</c>; only MSIL ran. So at HEAD the doubling was visible
    /// in the emitted text rather than in output — the program did not get far enough to print it
    /// — which is exactly why the assertion here is on the COUNTER and not on the number of
    /// <c>T</c> lines alone.</para>
    ///
    /// <para>The counter is read back through a separate statement so a doubled call cannot hide
    /// inside the same expression.</para>
    /// </summary>
    [Test]
    public void MultiplyingACallByZero_StillCallsItExactlyOnce()
        => OnFourBackends("""
            Dim Counter As Integer
            Function Tag() As Integer
             Counter = Counter + 1
             PrintLine("T")
             Return Counter
            End Function
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Main()
             Show(Tag() * 0)
             PrintLine("C=" & CStr(Counter))
            End Sub
            """, "T\nV=0\nC=1");

    /// <summary>
    /// ⛔ The DOUBLE-NEGATION arm — independent code from the binary arm, and it had the identical
    /// omission. Detected by C#, C++ and JavaScript; MSIL survives it.
    /// </summary>
    [Test]
    public void ThePeepholeDoubleNegationArm_LeavesNoUndeclaredTemp()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show(-(-a))
            End Sub
            Sub Main()
             Run(3)
            End Sub
            """, "V=3");

    /// <summary>
    /// ⛔ The NOT-NOT arm. Listed separately from double negation because the two arms are
    /// independent code: a fixture covering only <c>Neg</c> leaves <c>Not</c> open.
    ///
    /// <para>⚠ JavaScript prints <c>B=true</c> where the other three print <c>B=True</c>. A
    /// PRE-EXISTING <c>CStr(Boolean)</c> casing difference, reproduced on a control program with
    /// nothing optimizable in it; pinned per-backend rather than normalised away.</para>
    /// </summary>
    [Test]
    public void ThePeepholeNotNotArm_LeavesNoUndeclaredTemp()
        => OnFourBackends("""
            Sub ShowB(p As Boolean)
             PrintLine("B=" & CStr(p))
            End Sub
            Sub Run(a As Boolean)
             ShowB(Not (Not a))
            End Sub
            Sub Main()
             Run(True)
            End Sub
            """, "B=True", jsExpected: "B=true");

    /// <summary>
    /// ⛔ CONTROLS: <c>x * 1</c> and <c>x / 1</c> NEVER REACH the peephole pass.
    /// <c>StrengthReductionPass</c> runs first and rewrites them (to a shift, and to the operand),
    /// re-pointing correctly as it goes. Both were green at HEAD. They are pinned so that nobody
    /// reaches for an identity multiply or divide thinking it exercises D2 — it does not — and so
    /// that a change in pass ORDER, which would hand these shapes to the peephole pass, shows up
    /// here rather than in a user's program.
    /// </summary>
    [Test]
    [TestCase("a * 1", "V=6", TestName = "AnIdentityMultiply_IsHandledByStrengthReduction_NotPeephole")]
    [TestCase("a / 1", "V=6", TestName = "AnIdentityDivide_IsHandledByStrengthReduction_NotPeephole")]
    public void ShapesThatNeverReachThePeepholePass_WereAlwaysGreen(string expression, string expected)
        => OnFourBackends($"""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show({expression})
            End Sub
            Sub Main()
             Run(6)
            End Sub
            """, expected);

    // ====================================================================================
    // A user variable spelled like a temp. ⭐ The highest-value addition — nothing in the
    // suite covered it on the OPTIMIZER side, and it is the shape this fix's own first
    // draft broke.
    // ====================================================================================

    /// <summary>
    /// ⛔ A LOCAL spelled <c>t0</c>, written from <c>a + 0</c> and READ BACK.
    ///
    /// <para>⚠ <b>MEASURED GREEN AT HEAD (V=5, all four backends), and that is the point.</b> The
    /// peephole pass did not rewrite-and-re-point at all at HEAD, so this shape never reached the
    /// spelling test. The fix introduces the branch that DOES consult it — and with the gate
    /// written as <c>IsTempDestination(name)</c> alone, this program prints <c>V=0</c>: the name
    /// says "compiler temp", the write is deleted, and nothing fails to compile. That is a
    /// REGRESSION GUARD ON THE FIX rather than a reproduction of the reported bug, and it is here
    /// because the fix's own first draft failed it.</para>
    ///
    /// <para>The gate is <c>IsTempDestination(name) &amp;&amp; !NamedAfterVariable</c>: the name
    /// test is a test on SPELLING and a user may legitimately spell a variable <c>t0</c>, so the
    /// flag IRBuilder sets when it really did rename a result after a variable is the half that
    /// decides. The shape must READ THE VARIABLE BACK, because the failure is a plausible zero on
    /// every backend, not a diagnostic.</para>
    /// </summary>
    [Test]
    public void ALocalSpelledLikeATemp_KeepsItsPeepholeRewrittenWrite()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Dim t0 As Integer = a + 0
             Show(t0)
            End Sub
            Sub Main()
             Run(5)
            End Sub
            """, "V=5");

    /// <summary>
    /// ⛔ A class MEMBER spelled <c>t0</c>, the same guard one scope out. Also green at HEAD
    /// (<c>T0=5</c>); with the gate written on the name alone, <c>Set1</c> is emitted as an EMPTY
    /// METHOD BODY and <c>Set1(5)</c> prints <c>T0=0</c> — measured, on all four backends.
    ///
    /// <para><see cref="CSharpFieldAssignmentCodeGenTests.AMemberNamedLikeATemp_IsNotClobberedByTemps"/>
    /// covers the same name collision on the BACKEND side; the optimizer had no equivalent, and a
    /// member is a different storage location from a local, so it needs its own case rather than
    /// being assumed to follow.</para>
    /// </summary>
    [Test]
    public void AMemberSpelledLikeATemp_KeepsItsPeepholeRewrittenWrite()
        => OnFourBackends("""
            Class Timer
             Public t0 As Integer
             Public Sub Set1(n As Integer)
              t0 = n + 0
             End Sub
            End Class
            Sub Main()
             Dim x As Timer = New Timer()
             x.Set1(5)
             PrintLine("T0=" & CStr(x.t0))
            End Sub
            """, "T0=5");

    /// <summary>
    /// ⚠ REGRESSION BASELINE, NOT A DETECTOR, and the distinction is MEASURED. A LOCAL spelled
    /// <c>t0</c> assigned a duplicated expression is green with CSE's gate broken: the surviving
    /// temp CSE merges to is itself named <c>t0</c>, so <c>Show(t0)</c> resolves to it by NAME
    /// COINCIDENCE and prints the right answer even though the write to the user's local was
    /// deleted. Verified by re-running with the <c>NamedAfterVariable</c> conjunct removed — all
    /// four backends stayed green here, and only the MEMBER shape below changed.
    ///
    /// <para>It is kept because the shape must go on working and because a reader comparing it
    /// with the member case can see exactly why one detects the defect and the other cannot. The
    /// gate itself is pinned where a name cannot stand in for the storage location: by
    /// <see cref="AMemberSpelledLikeATemp_KeepsItsCseRewrittenWrite_CppExcluded"/> end to end, and
    /// backend-independently by
    /// <c>CseAndPeepholeIrIdentityTests.TheCseMerge_KeepsARealVariableAndRemovesATemp</c>.</para>
    /// </summary>
    [Test]
    public void ALocalSpelledLikeATemp_KeepsItsCseRewrittenWrite()
        => OnFourBackends("""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show(a + 7)
             Dim t0 As Integer = a + 7
             Show(t0)
            End Sub
            Sub Main()
             Run(3)
            End Sub
            """, "V=10\nV=10");

    /// <summary>
    /// ⛔ A class MEMBER spelled <c>t0</c> assigned a duplicated expression, through CSE — and
    /// unlike the peephole pair above this one is a LIVE HEAD DEFECT, not a guard on the fix.
    /// Measured at HEAD 39b3f7e: <c>T0=0</c> on ALL FOUR backends, silently, because CSE's own
    /// copy of the spelling test classified the member as a temp and removed its write outright.
    /// The fix repairs it because it is the same predicate on the same line.
    ///
    /// <para>⚠ <b>THREE BACKENDS, NOT FOUR. C++ is EXCLUDED and prints <c>T0=0</c> here BY A
    /// SEPARATE, PRE-EXISTING DEFECT</b> — a shadowing bug in <c>CppCodeGenerator</c> where the
    /// method's local <c>s</c> and the field <c>t0</c> collide in the emitted scope. It is
    /// measured at the fixed optimizer, not at HEAD, so it is not this change and not a
    /// regression; it lives in <c>CppCodeGenerator.cs</c>, which is outside the pass this fixture
    /// pins. The sibling case above (<see cref="ALocalSpelledLikeATemp_KeepsItsCseRewrittenWrite"/>)
    /// covers the same optimizer gate on a LOCAL and asserts all four backends, so C++ is not
    /// unasserted for this defect — only for this one shape.</para>
    ///
    /// <para>⚠ The C++ value is pinned at <c>T0=0</c> rather than skipped, so the day that
    /// shadowing bug is fixed this case fails and says so.</para>
    /// </summary>
    [Test]
    public void AMemberSpelledLikeATemp_KeepsItsCseRewrittenWrite_CppExcluded()
        => OnFourBackends("""
            Class Timer
             Public t0 As Integer
             Public Sub Set1(a As Integer)
              Dim s As Integer = a + 7
              t0 = a + 7
              PrintLine("S=" & CStr(s))
             End Sub
            End Class
            Sub Main()
             Dim x As Timer = New Timer()
             x.Set1(3)
             PrintLine("T0=" & CStr(x.t0))
            End Sub
            """, "S=10\nT0=10", cppExpected: "S=10\nT0=0");

    // ====================================================================================
    // Both entry points.
    // ====================================================================================

    /// <summary>
    /// ⛔ BOTH SHIPPING ENTRY POINTS, not just the test helper. A fix verified only through an
    /// in-fixture pipeline can still break the routes users take, which is why CLAUDE.md names
    /// them.
    ///
    /// <para><c>BasicCompiler.CompileFile</c> is what the CLI runs for
    /// <c>BasicLang.exe prog.bas --target=…</c>; <c>CompileProjectFiles</c> is what the CLI's
    /// <c>build</c> runs (<c>Program.cs</c>) AND what the IDE's build service delegates to
    /// (<c>CppProjectBuilder</c>). Both build their own <c>OptimizationPipeline</c> and run
    /// <c>AddStandardPasses</c> on the combined IR, so both are real subjects here.</para>
    ///
    /// <para>⚠ The SPAWNED <c>BasicLang.exe</c> leg (<c>CliTestHarness</c>) is deliberately not
    /// used: it is a Windows apphost that is not deployed on Linux, so every fixture built on it
    /// — including
    /// <c>CSharpFieldAssignmentExecutionTests.AMemberNamedLikeATemp_IsNotClobbered_AndCallsRunOnce</c>,
    /// the closest existing relative of these cases — already sits in this machine's baseline
    /// failure set. Calling the compiler's own entry points in process exercises the same engine
    /// and actually runs here.</para>
    /// </summary>
    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_LeavesNoDanglingOperand")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_LeavesNoDanglingOperand")]
    public void BothCompilerEntryPoints_ProduceCleanIrAndACorrectProgram(bool asProject)
    {
        const string source = """
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer)
             Show(a + 7)
             Show(a + 7)
             Show(a + 0)
            End Sub
            Sub Main()
             Run(3)
            End Sub
            """;

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_DanglingEntry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler();
            var result = asProject
                ? compiler.CompileProjectFiles(new[] { file })
                : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.Multiple(() =>
            {
                // The IR this entry point hands a backend is already past AddStandardPasses,
                // so the invariant is asserted on exactly what ships.
                Assert.That(OptimizerIr.DanglingOperandsOf(result.CombinedIR!), Is.Empty,
                    "a use still points at an instruction a standard pass removed");

                var cpp = new BasicLang.Compiler.CodeGen.CPlusPlus.CppCodeGenerator(
                    new BasicLang.Compiler.CodeGen.CPlusPlus.CppCodeGenOptions { GenerateComments = false })
                    .Generate(result.CombinedIR!);
                Assert.That(FourBackends.Norm(BclE2E.CompileRun(cpp)), Is.EqualTo("V=10\nV=10\nV=3"),
                    "C++ is the leg that refused to compile at HEAD for the CSE half");
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ====================================================================================
    // The four-backend leg.
    // ====================================================================================

    /// <summary>
    /// One program, all four backends compiled AND RUN, each through the STANDARD optimizer
    /// passes.
    ///
    /// <para>⛔ Deliberately NOT <see cref="FourBackends.RunsOnEveryBackend"/>: its JavaScript leg
    /// is <c>JavaScriptExecutionTests.RunJs</c>, which is <c>JsTestSupport.Compile</c> — the
    /// NON-optimizing path. Every defect in this fixture lives in a pass that leg never runs, so
    /// through it the JavaScript assertion would be green no matter what the optimizer did. This
    /// helper uses <c>JavaScriptOptimizedExecutionTests.RunOptimized</c> instead. The other three
    /// legs are the shared ones and already optimize.</para>
    ///
    /// <para><paramref name="cppExpected"/> and <paramref name="jsExpected"/> exist only for the
    /// two measured pre-existing cross-backend splits documented on the cases that use them. A
    /// case that passes one is making a claim about a DIFFERENT defect and says so in its
    /// docstring.</para>
    ///
    /// <para>⚠ ONE SHAPE PER TEST, deliberately: this is an <c>Assert.Multiple</c> and the C#
    /// leg is in-process Roslyn with no timeout, so several programs in one case would report the
    /// first one's compile error for all of them and a non-terminating program would hang the test
    /// host.</para>
    /// </summary>
    private static void OnFourBackends(
        string program, string expected, string? cppExpected = null, string? jsExpected = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))),
                Is.EqualTo(cppExpected ?? expected), "C++");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(program)),
                Is.EqualTo(expected), "MSIL");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program)),
                Is.EqualTo(jsExpected ?? expected), "JavaScript (OPTIMIZED — the unoptimized leg cannot see these passes)");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)),
                Is.EqualTo(expected), "C#");
        });
    }
}

/// <summary>
/// The same two fixes asserted on the IR itself, where the end-to-end shapes above cannot reach.
///
/// <para>⛔ <b>Why this fixture has to exist.</b> Three parts of the contract are INVISIBLE in
/// emitted output, every one of them for the reason
/// <see cref="AlgebraicSimplificationTests.TheConsumerIsRePointedAtTheReplacement_NotTheDiscardedNode"/>
/// already records: when a replacement carries the same NAME as the node it displaces, an orphaned
/// consumer resolves by NAME COINCIDENCE and the emitted text is identical whether or not the
/// re-point happened. Measured here too — dropping CSE's named-arm <c>ReplaceUses</c> leaves every
/// end-to-end shape in this file green. Only a reference-IDENTITY assertion has teeth.</para>
///
/// <list type="bullet">
/// <item>The CSE temp arm is scoped to the whole FUNCTION, not the current block. Every shape a
/// user can write today has its consumer in the same block as the duplicate, so a block-scoped
/// re-point is end-to-end invisible; only a hand-built two-block function reaches it.</item>
/// <item>The CSE NAMED arm re-points too, and its replacement carries the discarded node's name,
/// so it is invisible by construction.</item>
/// <item>The peephole rewrite is REMOVE-AND-FORWARD for a temp and KEEP-AND-RE-POINT for a real
/// variable. The "real variable" half of that is the whole of contract item 4, and its first
/// disjunct (<c>!IsTempDestination(name)</c> with <c>NamedAfterVariable</c> FALSE) is not
/// reachable from source: IRBuilder sets <c>NamedAfterVariable</c> on every user-named result it
/// produces, so a real-named non-flagged value can only be built by hand.</item>
/// </list>
/// </summary>
[TestFixture]
public class CseAndPeepholeIrIdentityTests
{
    private static readonly BasicLang.Compiler.SemanticAnalysis.TypeInfo IntType =
        new BasicLang.Compiler.SemanticAnalysis.TypeInfo("Integer", TypeKind.Primitive);

    /// <summary>
    /// ⛔ The CSE temp arm re-points a consumer in a LATER BLOCK.
    ///
    /// <para>The pass walks blocks one at a time with a fresh expression table per block, so the
    /// duplicate it merges is always in the block it is visiting — but a USE of that duplicate may
    /// be anywhere in the function. Narrowing the re-point to the current block is therefore
    /// silent on every program measured for this change and wrong on the first one that is not.
    /// The same widening is what <c>StrengthReductionPass</c> and
    /// <c>AlgebraicSimplificationPass</c> already do.</para>
    /// </summary>
    [Test]
    public void TheCseTempArm_RePointsAConsumerInALaterBlock()
    {
        var a = new IRVariable("a", IntType);
        var b = new IRVariable("b", IntType);

        var first = new IRBinaryOp("t0", BinaryOpKind.Add, a, b, IntType);
        var duplicate = new IRBinaryOp("t1", BinaryOpKind.Add, a, b, IntType);
        var ret = new IRReturn(duplicate);       // the consumer, in the NEXT block

        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");
        var next = new BasicBlock("next");
        entry.AddInstruction(first);
        entry.AddInstruction(duplicate);
        entry.AddInstruction(new IRBranch(next));
        next.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.Blocks.Add(next);
        function.EntryBlock = entry;

        RunPass(new CommonSubexpressionEliminationPass(), function);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Instructions.OfType<IRBinaryOp>().ToList(), Has.Count.EqualTo(1),
                "CSE did not merge the duplicate at all");
            Assert.That(ret.Value, Is.SameAs(first),
                "the consumer in the LATER block must hold the surviving node. Holding the merged-"
                + "away one is the defect: backends key temporaries by object identity, so the "
                + "orphan renders an identifier that is never declared — measured as "
                + "'use of undeclared identifier' on C++ and InvalidProgramException on MSIL.");
            Assert.That(ret.Value, Is.Not.SameAs(duplicate),
                "and specifically not the node that was removed from the block");
        });
    }

    /// <summary>
    /// ⛔ The CSE NAMED arm re-points its consumers at the new assignment's TARGET.
    ///
    /// <para>This arm keeps the write (the destination is a real variable) but the binary-op
    /// OBJECT still leaves the instruction stream, so its consumers are orphaned exactly as in the
    /// temp arm. They currently resolve by NAME COINCIDENCE — the target variable happens to be
    /// spelled the same — which is the accident <see cref="AlgebraicSimplificationTests"/>
    /// documents for <c>2 * x</c> and which fails the moment a backend keys a value by identity.
    /// Measured: removing this re-point leaves every end-to-end shape in this file GREEN.</para>
    /// </summary>
    [Test]
    public void TheCseNamedArm_RePointsConsumersAtTheAssignmentTarget()
    {
        var a = new IRVariable("a", IntType);
        var b = new IRVariable("b", IntType);

        var first = new IRBinaryOp("t0", BinaryOpKind.Add, a, b, IntType);
        var named = new IRBinaryOp("s", BinaryOpKind.Add, a, b, IntType);
        var ret = new IRReturn(named);           // the consumer holds the NODE, not a variable

        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");
        entry.AddInstruction(first);
        entry.AddInstruction(named);
        entry.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;

        RunPass(new CommonSubexpressionEliminationPass(), function);

        var assignment = entry.Instructions.OfType<IRAssignment>().SingleOrDefault();

        Assert.That(assignment, Is.Not.Null, "the named arm did not fire");
        Assert.Multiple(() =>
        {
            Assert.That(assignment!.Target.Name, Is.EqualTo("s"), "the write must be KEPT");
            Assert.That(assignment.Value, Is.SameAs(first), "and fed from the surviving expression");
            Assert.That(ret.Value, Is.SameAs(assignment.Target),
                "the consumer must hold the REPLACEMENT. Resolving by name coincidence is not the "
                + "same as being re-pointed, and only this assertion can tell them apart.");
            Assert.That(ret.Value, Is.Not.SameAs(named),
                "and specifically not the binary op that left the stream");
        });
    }

    /// <summary>
    /// ⛔ CONTRACT ITEM 4, both disjuncts and the negative case, on the peephole pass.
    ///
    /// <para><b>A destination is a real variable if EITHER the name is not a temp spelling OR
    /// <c>NamedAfterVariable</c> is set.</b> For a real variable the rewrite is
    /// KEEP-AND-RE-POINT: the <c>IRAssignment</c> stays in the block and consumers move to its
    /// target. For a genuine temp it is REMOVE-AND-FORWARD: the definition is dropped (an
    /// <c>IRAssignment</c> is not an <c>IRValue</c>, so leaving it would emit a write to a name no
    /// backend declares) and consumers are forwarded to the VALUE.</para>
    ///
    /// <para>⚠ The first case is the one no end-to-end shape can reach. IRBuilder sets
    /// <c>NamedAfterVariable</c> on every user-named result it emits — measured on
    /// <c>Dim s As Integer = a + 0</c>, which produces <c>IRBinaryOp("s")</c> with the flag SET —
    /// so a real-named value WITHOUT the flag exists only if a pass makes one. Dropping the name
    /// half of the gate is therefore invisible in every program that can be written today, and
    /// visible here.</para>
    ///
    /// <para>⚠ The second case is the measured regression: a member or local the user spelled
    /// <c>t0</c>. Dropping the flag half deletes its write and prints a plausible zero.</para>
    /// </summary>
    [Test]
    [TestCase("s", false, true, TestName = "ARealNameWithoutTheFlag_KeepsItsWrite")]
    [TestCase("t0", true, true, TestName = "ATempSpellingWithTheFlag_KeepsItsWrite")]
    [TestCase("t0", false, false, TestName = "AGenuineTemp_IsRemovedAndForwarded")]
    public void ThePeepholeRewrite_KeepsARealVariableAndRemovesATemp(
        string name, bool namedAfterVariable, bool expectKept)
    {
        var a = new IRVariable("a", IntType);
        var target = new IRBinaryOp(name, BinaryOpKind.Add, a, new IRConstant(0, IntType), IntType)
        {
            NamedAfterVariable = namedAfterVariable
        };
        var ret = new IRReturn(target);          // the consumer holds the NODE

        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");
        entry.AddInstruction(target);
        entry.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;

        RunPass(new PeepholeOptimizationPass(), function);

        var assignment = entry.Instructions.OfType<IRAssignment>().SingleOrDefault();

        if (expectKept)
        {
            Assert.That(assignment, Is.Not.Null,
                "the write to a REAL VARIABLE was removed — this is the silent miscompile: the "
                + "method body comes out empty and the variable keeps its old value, on every "
                + "backend, with nothing failing to compile");
            Assert.Multiple(() =>
            {
                Assert.That(assignment!.Target.Name, Is.EqualTo(name));
                Assert.That(assignment.Value, Is.SameAs(a), "x + 0 -> x");
                Assert.That(ret.Value, Is.SameAs(assignment.Target),
                    "and the consumer is re-pointed at the assignment's target");
                Assert.That(ret.Value, Is.Not.SameAs(target));
            });
        }
        else
        {
            Assert.That(assignment, Is.Null,
                "a temp destination must be REMOVED, not swapped for an assignment: the temp's "
                + "declaration exists only because an IRValue carried that name, and an "
                + "IRAssignment is not an IRValue, so swapping one in emits 't0 = a;' against an "
                + "undeclared t0 — CS0103 on C#, undeclared identifier on C++, ReferenceError on JS");
            Assert.Multiple(() =>
            {
                Assert.That(entry.Instructions, Has.Count.EqualTo(1), "only the consumer should remain");
                Assert.That(ret.Value, Is.SameAs(a),
                    "and the consumer is forwarded to the VALUE, not to the removed temp's target");
            });
        }
    }

    /// <summary>
    /// ⛔ CONTRACT ITEM 4 again, on CSE, which decides the same question with the same predicate
    /// one file-section away. The two passes must not drift: this is the same three cases.
    /// </summary>
    [Test]
    [TestCase("s", false, true, TestName = "Cse_ARealNameWithoutTheFlag_KeepsItsWrite")]
    [TestCase("t0", true, true, TestName = "Cse_ATempSpellingWithTheFlag_KeepsItsWrite")]
    [TestCase("t1", false, false, TestName = "Cse_AGenuineTemp_IsRemovedAndForwarded")]
    public void TheCseMerge_KeepsARealVariableAndRemovesATemp(
        string name, bool namedAfterVariable, bool expectKept)
    {
        var a = new IRVariable("a", IntType);
        var b = new IRVariable("b", IntType);

        var first = new IRBinaryOp("t0", BinaryOpKind.Add, a, b, IntType);
        var duplicate = new IRBinaryOp(name, BinaryOpKind.Add, a, b, IntType)
        {
            NamedAfterVariable = namedAfterVariable
        };
        var ret = new IRReturn(duplicate);

        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");
        entry.AddInstruction(first);
        entry.AddInstruction(duplicate);
        entry.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;

        RunPass(new CommonSubexpressionEliminationPass(), function);

        var assignment = entry.Instructions.OfType<IRAssignment>().SingleOrDefault();

        if (expectKept)
        {
            Assert.That(assignment, Is.Not.Null,
                "the write to a REAL VARIABLE was removed outright — measured as T0=0 on all four "
                + "backends for a member the user spelled t0, silently and with nothing failing "
                + "to compile");
            Assert.Multiple(() =>
            {
                Assert.That(assignment!.Target.Name, Is.EqualTo(name));
                Assert.That(assignment.Value, Is.SameAs(first),
                    "fed from the surviving expression rather than recomputed");
                Assert.That(ret.Value, Is.SameAs(assignment.Target),
                    "and the consumer is re-pointed at the target, not left on the discarded node");
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(assignment, Is.Null, "a genuine temp is merged away, not assigned");
                Assert.That(ret.Value, Is.SameAs(first),
                    "and its consumers are forwarded to the surviving expression");
                Assert.That(ret.Value, Is.Not.SameAs(duplicate));
            });
        }
    }

    /// <summary>
    /// ⚠ TWO ADJACENT rewritable temps, removed and forwarded in the SAME block — the shape the
    /// pass's loop-index arithmetic exists for. After a removal the instruction that shifted into
    /// the vacated slot must still be visited, so the index steps back.
    ///
    /// <para>⚠ <b>MEASURED: this does NOT detect a missing step-back, and nothing can.</b> The
    /// enclosing <c>do/while(changed)</c> re-runs the whole block whenever a rewrite fired, so a
    /// skipped instruction is simply picked up on the next sweep and the final IR is identical —
    /// only the number of inner sweeps differs, and that is not reported anywhere. Verified by
    /// removing the step-back and re-running this entire fixture: 49 passed, 0 failed. The
    /// step-back is a single-sweep optimisation, not a correctness gate, TODAY — it becomes
    /// load-bearing the moment the fixed-point loop is removed, which is why this case pins the
    /// OUTCOME (both removed, both forwarded) rather than the mechanism.</para>
    /// </summary>
    [Test]
    public void TwoAdjacentTempRewrites_AreBothRemovedAndForwarded()
    {
        var a = new IRVariable("a", IntType);
        var b = new IRVariable("b", IntType);
        var zero = new IRConstant(0, IntType);

        var firstTemp = new IRBinaryOp("t0", BinaryOpKind.Add, a, zero, IntType);
        var secondTemp = new IRBinaryOp("t1", BinaryOpKind.Add, b, zero, IntType);
        var consumer = new IRBinaryOp("t2", BinaryOpKind.Add, firstTemp, secondTemp, IntType);
        var ret = new IRReturn(consumer);

        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");
        entry.AddInstruction(firstTemp);
        entry.AddInstruction(secondTemp);
        entry.AddInstruction(consumer);
        entry.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;

        RunPass(new PeepholeOptimizationPass(), function);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Instructions.OfType<IRAssignment>(), Is.Empty,
                "both temp definitions must be REMOVED, not swapped for assignments");
            Assert.That(entry.Instructions, Has.Count.EqualTo(2),
                "only the consumer and the return should remain");
            Assert.That(consumer.Left, Is.SameAs(a), "the FIRST removal was forwarded");
            Assert.That(consumer.Right, Is.SameAs(b),
                "and so was the SECOND — an instruction that shifted into a vacated slot must "
                + "still be visited");
            Assert.That(ret.Value, Is.SameAs(consumer));
        });
    }

    /// <summary>
    /// ⚠ CONTRACT ITEM 2, structurally: neither pass may grow a private operand walker or a
    /// private copy of the temp-name test again. Both were deleted; the base class owns one of
    /// each, and <c>ModuleResolver</c>/<c>ModuleTypeWalker</c> in CLAUDE.md is the same rule —
    /// shared logic changes once, not per consumer.
    ///
    /// <para>⚠ This can only see a copy that reuses one of the deleted NAMES. A differently-named
    /// copy is caught by behaviour instead, by
    /// <see cref="OptimizerDanglingOperandInvariantTests"/> and by the identity cases above; this
    /// case is the cheap structural half. The positive half — that the SHARED members still exist
    /// under those names — is asserted too, so the pin cannot go quietly vacuous if they are
    /// renamed.</para>
    ///
    /// <para>⚠ <c>ConstantFoldingPass</c> is deliberately NOT included: it keeps its own
    /// <c>IsNamedVariable</c> on purpose (it answers differently for a user variable spelled
    /// <c>T5</c>, and reconciling the two is a behaviour change to be measured on its own).</para>
    /// </summary>
    [Test]
    public void NeitherPassCarriesItsOwnOperandWalkerOrTempNameTest()
    {
        const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        string[] Forbidden(Type t) => t.GetMethods(Declared)
            .Select(m => m.Name)
            .Where(n => n is "ReplaceAllUses" or "ReplaceUses" or "ReplaceUsesIn"
                     or "IsNamedVariable" or "IsTempDestination")
            .Distinct().ToArray();

        var shared = typeof(OptimizationPass).GetMethods(Declared).Select(m => m.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Forbidden(typeof(CommonSubexpressionEliminationPass)), Is.Empty,
                "CSE must use the base class's walker and temp-name test, not a copy — its private "
                + "four-arm ReplaceAllUses covered 4 of 26 consumer kinds and orphaned the rest");
            Assert.That(Forbidden(typeof(PeepholeOptimizationPass)), Is.Empty,
                "and so must the peephole pass");
            Assert.That(shared, Does.Contain("ReplaceUses").And.Contain("ReplaceUsesIn"),
                "the SHARED walker must still exist under this name, or the pins above are vacuous");
            Assert.That(shared, Does.Contain("IsTempDestination"),
                "and so must the shared temp-name test");
        });
    }

    /// <summary>Runs one pass over a hand-built single-function module.</summary>
    private static void RunPass(OptimizationPass pass, IRFunction function)
    {
        var module = new IRModule("IrIdentityProbe");
        module.Functions.Add(function);

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(pass);
        pipeline.Run(module);
    }
}

/// <summary>
/// ⛔ CONTRACT ITEM 1, as a PASS-LEVEL INVARIANT rather than a per-shape assertion — the same
/// shape of pin as <see cref="FunctionInliningDisabledTests"/>.
///
/// <para><b>After every pass in <c>AddStandardPasses</c>, no operand slot of a reachable
/// instruction may point at an instruction the pipeline removed.</b> That is the one sentence both
/// defects in this change violate, and it is checked here by walking the IR with REFLECTION rather
/// than with either of the compiler's own operand walkers — a walker with a missing arm would
/// otherwise be used to prove itself total.</para>
///
/// <para>The check is not vacuous: every program in the battery below has at least one instruction
/// removed by the pipeline (measured), so there is always something for a stale reference to point
/// at.</para>
/// </summary>
[TestFixture]
public class OptimizerDanglingOperandInvariantTests
{
    /// <summary>
    /// The battery. Each program is a shape one of the two passes rewrites, plus the controls; the
    /// property asserted is the same for all of them and does not depend on what they print.
    /// </summary>
    [Test]
    [TestCase("Show(a + 7)\n Show(a + 7)", TestName = "Invariant_CseDuplicateInACallArgument")]
    [TestCase("Show(a + 7)\n Dim q As Integer = a + 7\n Show(q)", TestName = "Invariant_CseDuplicateOnANamedDestination")]
    [TestCase("Show(a + 7)\n Dim t0 As Integer = a + 7\n Show(t0)", TestName = "Invariant_CseDuplicateOnATempSpelledName")]
    [TestCase("Show(a + 0)", TestName = "Invariant_PeepholeAddZero")]
    [TestCase("Show(a + 0)\n Show(b + 0)", TestName = "Invariant_PeepholeTwoRewritesInOneBlock")]
    [TestCase("Show(a - a)", TestName = "Invariant_PeepholeSelfSubtraction")]
    [TestCase("Show(a * 0)", TestName = "Invariant_PeepholeMultiplyByZero")]
    [TestCase("Show(-(-a))", TestName = "Invariant_PeepholeDoubleNegation")]
    [TestCase("Dim t0 As Integer = a + 0\n Show(t0)", TestName = "Invariant_PeepholeOnATempSpelledName")]
    [TestCase("Dim arr(3) As Integer\n Show(a + b)\n arr(0) = a + b\n Show(arr(0))", TestName = "Invariant_CseIntoAnArrayStore")]
    [TestCase("Show(a + b)\n If (a + b) > 0 Then\n  PrintLine(\"POS\")\n End If", TestName = "Invariant_CseIntoABranchCondition")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(a + 7)\n  Show(a + 7)\n Next", TestName = "Invariant_CseInsideALoop")]
    public void NoStandardPassLeavesAUsePointingAtAnInstructionItRemoved(string body)
    {
        var source = $"""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             {body}
            End Sub
            Sub Main()
             Run(3, 5)
            End Sub
            """;

        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var before = OptimizerIr.StreamInstructions(module);

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        var removed = OptimizerIr.Removed(before, module);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.Not.Empty,
                "no instruction was removed at all — the invariant below would be vacuous for "
                + "this shape, so it is not a useful battery entry");
            Assert.That(OptimizerIr.DanglingOperandsOf(module), Is.Empty,
                "a use still points at an instruction a standard pass removed. That is the whole "
                + "defect: backends key values by object identity, so the orphan is emitted as an "
                + "identifier nothing declares (C++, MSIL) or re-materialised inline, which "
                + "duplicates any effect in its operand tree (C#, JavaScript).");
        });
    }

    /// <summary>
    /// ⭐ THE BATTERY ABOVE, WIDENED TO <c>AddAggressivePasses()</c> — the second half of what
    /// <see cref="TheAggressivePipelineLeavesNoDanglingOperand_WasAPinnedPreExistingDefect"/>'s
    /// predecessor asked for. Contract item 1 is now asserted for the aggressive pipeline, not
    /// only the standard one.
    ///
    /// <para><b>The first twelve cases are the standard battery, unchanged.</b> ⛔ They are NOT
    /// the ones with teeth here, and it matters that this is written down: MEASURED with
    /// <c>InductionVariablePass</c> restored to <c>AddAggressivePasses</c>, ALL TWELVE stay green,
    /// including <c>Invariant_CseInsideALoop</c> — the only one with a loop — because none of them
    /// multiplies an induction variable by a constant. Widening the battery alone would have been
    /// green for the entire defect. The five added <c>AggrInvariant_</c> cases are the ones that
    /// reach an aggressive-only pass — FOUR of them go red under the defect, and the fifth
    /// (<c>TwoMultipliesOntoOneNamedLocal</c>) is a labelled CONTROL that stays green because that
    /// shape orphans nothing at all. Each says what it contributes.</para>
    ///
    /// <para>⚠ NO NON-VACUITY ASSERTION, deliberately, and the measurement is exact:
    /// <c>OptimizerIr.Removed</c> counts <b>1</b> for each of the first twelve shapes under BOTH
    /// pipelines, and <b>0</b> for all five <c>AggrInvariant_</c> loop shapes under BOTH pipelines.
    /// NOTHING is removed from a counted <c>For</c> with a multiply in it by any pass, standard or
    /// aggressive. A "something was removed" assertion would therefore fail on exactly the five
    /// cases this test exists for, and for a reason with nothing to do with the property. The
    /// battery above keeps its check because all twelve of ITS shapes satisfy it. Do not copy it
    /// down here.</para>
    ///
    /// <para>⚠ <b>THE CONCLUSION IS UNCHANGED; ITS STATED REASON WAS AND IS NO LONGER TRUE.</b>
    /// This note used to explain the zero as "the only aggressive-only pass that acts on those
    /// shapes is <c>LoopInvariantCodeMotionPass</c>, and it MOVES instructions rather than
    /// removing any". Since ADR-0003 that is not why: <c>LoopInvariantCodeMotionPass</c>,
    /// <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c> are all three UNREGISTERED, so NO
    /// aggressive-only pass touches these loop shapes at all — the count is zero because nothing
    /// runs on them, not because the thing that runs on them only moves. The zero itself is
    /// re-measured and unchanged.</para>
    ///
    /// <para>⚠ These are the same twelve source bodies as the battery above rather than a shared
    /// constant, because NUnit needs them as attribute arguments. If one is edited, edit both — a
    /// pipeline-specific divergence in the battery itself would be invisible.</para>
    /// </summary>
    [Test]
    [TestCase("Show(a + 7)\n Show(a + 7)", TestName = "AggrInvariant_CseDuplicateInACallArgument")]
    [TestCase("Show(a + 7)\n Dim q As Integer = a + 7\n Show(q)", TestName = "AggrInvariant_CseDuplicateOnANamedDestination")]
    [TestCase("Show(a + 7)\n Dim t0 As Integer = a + 7\n Show(t0)", TestName = "AggrInvariant_CseDuplicateOnATempSpelledName")]
    [TestCase("Show(a + 0)", TestName = "AggrInvariant_PeepholeAddZero")]
    [TestCase("Show(a + 0)\n Show(b + 0)", TestName = "AggrInvariant_PeepholeTwoRewritesInOneBlock")]
    [TestCase("Show(a - a)", TestName = "AggrInvariant_PeepholeSelfSubtraction")]
    [TestCase("Show(a * 0)", TestName = "AggrInvariant_PeepholeMultiplyByZero")]
    [TestCase("Show(-(-a))", TestName = "AggrInvariant_PeepholeDoubleNegation")]
    [TestCase("Dim t0 As Integer = a + 0\n Show(t0)", TestName = "AggrInvariant_PeepholeOnATempSpelledName")]
    [TestCase("Dim arr(3) As Integer\n Show(a + b)\n arr(0) = a + b\n Show(arr(0))", TestName = "AggrInvariant_CseIntoAnArrayStore")]
    [TestCase("Show(a + b)\n If (a + b) > 0 Then\n  PrintLine(\"POS\")\n End If", TestName = "AggrInvariant_CseIntoABranchCondition")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(a + 7)\n  Show(a + 7)\n Next", TestName = "AggrInvariant_CseInsideALoop")]
    // ⛔ The five that actually reach an aggressive-only pass. Every one uses `* 3`, never `* 2`:
    // MEASURED, `AlgebraicSimplificationPass` (aggressive pass 9) rewrites `2 * x` to `x + x`
    // before `InductionVariablePass` (pass 12) can see it, so a `* 2` case is green even with the
    // pass restored and proves nothing. `While` is out for the same class of reason — the pass
    // bails when no block is named `.inc`, which only a counted `For` produces.
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(i * 3)\n Next",
        TestName = "AggrInvariant_AnInductionVariableTimesAConstant")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(3 * i)\n Next",
        TestName = "AggrInvariant_AConstantTimesAnInductionVariable")]
    [TestCase("Dim i As Integer\n For i = 1 To b\n  Show(i * 3)\n Next",
        TestName = "AggrInvariant_AnInductionVariableLoopThatDoesNotStartAtZero")]
    // ⚠ This one is a CONTROL, not a case with teeth, and it matters that the difference is
    // written down: with the pass restored this shape leaves ZERO orphaned operands, because the
    // multiply's destination is a NAMED local that consumers reference by IRVariable rather than
    // by the instruction's identity. MEASURED — it stays GREEN under the defect while its sibling
    // `OptimizerMintedVariableTests.Declared_TwoMultipliesOntoOneNamedLocal_WhichLeavesNoOrphanAtAll`
    // goes red. That asymmetry is the evidence that contract item 2 is strictly stronger than
    // contract item 1, so this entry is kept to mark the blind spot rather than to guard it.
    [TestCase("Dim i As Integer\n Dim x As Integer\n For i = 0 To b\n  x = i * 3\n  Show(x)\n  x = i * 5\n  Show(x)\n Next",
        TestName = "AggrInvariant_TwoMultipliesOntoOneNamedLocal")]
    [TestCase("Dim i As Integer\n Dim j As Integer\n For i = 0 To 1\n  For j = 0 To 2\n   Show(j * 3)\n  Next\n Next",
        TestName = "AggrInvariant_AMultiplyOnTheInnerCounterOfANestedLoop")]
    public void NoAggressivePassLeavesAUsePointingAtAnInstructionItRemoved(string body)
    {
        var source = $"""
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(a As Integer, b As Integer)
             {body}
            End Sub
            Sub Main()
             Run(3, 5)
            End Sub
            """;

        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        Assert.That(OptimizerIr.DanglingOperandsOf(module), Is.Empty,
            "a use still points at an instruction an AGGRESSIVE pass removed or replaced. That is "
            + "contract item 1, widened from AddStandardPasses to the aggressive pipeline: "
            + "backends key values by object identity, so the orphan is emitted as an identifier "
            + "nothing declares (C++, MSIL) or re-materialised inline, which duplicates any effect "
            + "in its operand tree (C#, JavaScript). InductionVariablePass did exactly this and is "
            + "disabled for it; a pass that swaps an instruction owes the ReplaceUses call the "
            + "standard passes make.");
    }

    /// <summary>
    /// ⭐ WAS A PINNED DIVERGENCE; THE PIN FIRED AND THIS IS ITS POSITIVE COUNTERPART. Contract
    /// item 1 now holds for the AGGRESSIVE pipeline on this shape too, not only for
    /// <c>AddStandardPasses</c>.
    ///
    /// <para><b>What it used to pin.</b> <c>InductionVariablePass</c> (aggressive-only) replaced
    /// the <c>i * 3</c> binary op with an <c>IRAssignment</c> and never re-pointed its consumers —
    /// the same omission as CSE and the peephole pass, in a third pass that was out of scope at
    /// <c>67782af</c>. Measured under <c>--optimize</c> on this very shape: C# <c>CS0103</c> on
    /// BOTH <c>_div_t2</c> and <c>t2</c>, C++ "use of undeclared identifier", JavaScript
    /// <c>ReferenceError</c>, MSIL <c>InvalidProgramException</c> — all four backends, on both
    /// entry points, so it was an IR defect and not a backend one.</para>
    ///
    /// <para><b>What resolved it.</b> The pass is out of <c>AddAggressivePasses</c>. It could not
    /// be repaired in place: it also never initialises the derived induction variable, and placing
    /// that initialisation needs a loop preheader that nothing synthesises. (At <c>67782af</c>
    /// <c>ControlFlowGraph.IdentifyLoops</c> could not even supply a correct loop SET to place it
    /// against — measured: FOUR "natural loops" for a five-block function holding one loop, every
    /// one of them containing <c>entry</c>. ADR-0003 repaired that; it did NOT add a preheader,
    /// and it does not re-open this pass.) Repairing
    /// only the two defects this test was written about — the dangling uses and the missing
    /// <c>LocalVariables</c> entry — was measured to leave C# and JavaScript still broken here AND
    /// to turn two multiplies onto one local from a loud build failure into a SILENT wrong answer.
    /// The reasoning is recorded at the disabled <c>AddPass</c> line in <c>IROptimizer.cs</c>.</para>
    ///
    /// <para>⚠ This assertion is deliberately NOT paired with the battery's non-vacuity check.
    /// Nothing is removed from this shape by any aggressive pass, so "something was removed" is
    /// false here and would fail for a reason that has nothing to do with the property. Do not
    /// add it back. ⚠ The REASON has changed since this was written and the conclusion has not:
    /// it used to be that the only pass touching this shape,
    /// <c>LoopInvariantCodeMotionPass</c>, MOVED instructions rather than removing any; since
    /// ADR-0003 that pass — and <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c> with it — is
    /// UNREGISTERED, so no aggressive-only pass touches this shape at all.</para>
    ///
    /// <para>⭐ <b>CORRECTED, AND IT WAS FACTUALLY FALSE.</b> This paragraph used to read "Still
    /// open… under the aggressive pipeline C++ and MSIL run this loop ZERO times, because LICM
    /// moves the loop condition's definition into the loop's own latch." That was issue #114 and
    /// it is CLOSED: ADR-0003 unregisters LICM, and RE-MEASURED on the 13-shape CFG corpus all
    /// four backends run every loop shape the right number of times under both pipelines
    /// (<c>CfgLoopShapesAggressiveTests</c>, 104/104 cells). It remains true that such a defect
    /// would produce no dangling operand, so this invariant would be blind to it — that is the
    /// part worth keeping, and it is why the four-backend VALUE fixture exists separately.</para>
    /// </summary>
    [Test]
    public void TheAggressivePipelineLeavesNoDanglingOperand_WasAPinnedPreExistingDefect()
    {
        const string source = """
            Sub Show(p As Integer)
             PrintLine("V=" & CStr(p))
            End Sub
            Sub Run(n As Integer)
             Dim i As Integer
             For i = 0 To n
              Show(i * 3)
             Next
            End Sub
            Sub Main()
             Run(2)
            End Sub
            """;

        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        Assert.That(OptimizerIr.DanglingOperandsOf(module), Is.Empty,
            "an aggressive-only pass left a use pointing at an instruction it replaced. "
            + "InductionVariablePass did exactly this and is disabled for it; if it or another "
            + "loop pass has been re-enabled or added, it owes the same ReplaceUses call the "
            + "standard passes make — and, if it mints a variable, a LocalVariables entry and a "
            + "name the user cannot spell.");
    }
}

/// <summary>
/// The IR walk the invariant is checked with.
///
/// <para>⛔ REFLECTION, deliberately, and not <c>OptimizationPass.ReplaceUsesIn</c> or
/// <c>CSharpBackend.GetOperands</c>. Both of those are hand-written switches with one arm per node
/// kind, and a MISSING ARM is exactly the defect class this whole fixture is about — using one of
/// them here would let a walker certify its own totality. Reflection reads the slots off the type,
/// so a node kind nobody remembered is still visited.</para>
/// </summary>
internal static class OptimizerIr
{
    /// <summary>Every instruction currently IN a block of the module, by object identity.</summary>
    internal static HashSet<object> StreamInstructions(IRModule module)
    {
        var set = new HashSet<object>((IEqualityComparer<object>)ReferenceEqualityComparer.Instance);
        foreach (var f in module.Functions)
            foreach (var b in f.Blocks)
                foreach (var i in b.Instructions)
                    set.Add(i);
        return set;
    }

    /// <summary>The instructions that were in the stream before a pipeline ran and are not now.</summary>
    internal static HashSet<object> Removed(HashSet<object> before, IRModule module)
    {
        var after = StreamInstructions(module);
        return new HashSet<object>(before.Where(x => !after.Contains(x)),
            (IEqualityComparer<object>)ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Every operand reference, anywhere reachable from the instruction stream, that points at an
    /// instruction NOT in the stream — described for a failure message.
    ///
    /// <para>"Not in the stream" is the right test rather than "was removed": a definition is an
    /// instruction in a block, so a consumer holding an <c>IRValue</c> that no block contains is
    /// holding an orphan whatever produced it. Leaf operands (<c>IRVariable</c>,
    /// <c>IRConstant</c>) are never in the stream and are not definitions, so they are exempt —
    /// the check is on node kinds that only ever exist AS instructions.</para>
    /// </summary>
    internal static List<string> DanglingOperandsOf(IRModule module)
    {
        var stream = StreamInstructions(module);
        var cmp = (IEqualityComparer<object>)ReferenceEqualityComparer.Instance;
        var seen = new HashSet<object>(stream, cmp);
        var work = new Stack<object>(stream);
        var bad = new List<string>();

        while (work.Count > 0)
        {
            var node = work.Pop();
            foreach (var operand in Operands(node))
            {
                if (IsLeaf(operand)) continue;   // a leaf has no operand slots to walk into
                if (operand is IRValue && !stream.Contains(operand))
                    bad.Add($"{node.GetType().Name} -> orphaned "
                        + $"{operand.GetType().Name}(\"{(operand as IRValue)?.Name}\")");
                if (seen.Add(operand)) work.Push(operand);
            }
        }
        return bad.Distinct().ToList();
    }

    /// <summary>
    /// ⭐ CONTRACT ITEM 2's census: every <c>IRVariable</c> name that appears in an OPERAND SLOT
    /// of a reachable instruction but is in none of <c>IRFunction.LocalVariables</c>,
    /// <c>IRFunction.Parameters</c> or <c>IRModule.GlobalVariables</c>. Reported as
    /// <c>function::name</c>.
    ///
    /// <para><b>Why this, and not the emitted text.</b> A referenced-but-undeclared variable is
    /// what every backend turns into an identifier nothing declares — C# <c>CS0103</c>, C++ "use
    /// of undeclared identifier", JavaScript <c>ReferenceError</c>, MSIL
    /// <c>InvalidProgramException</c>. Catching it here needs no backend, no toolchain and no
    /// process, and it catches the shape where the destination is a NAMED local rather than a
    /// temp — which produces NO dangling operand at all, so
    /// <see cref="DanglingOperandsOf"/> is blind to it. MEASURED: with
    /// <c>InductionVariablePass</c> in the pipeline, <c>x = i * 3</c> in a counted loop gives
    /// <c>Main::_div_x</c> here and ZERO orphans.</para>
    ///
    /// <para>⚠ NOT VACUOUSLY ZERO ON EVERY PROGRAM, and a caller must know which. Two front-end
    /// shapes legitimately reference a name declared somewhere this set does not look, and are
    /// present before any pass runs: a CLASS FIELD read inside a method (measured:
    /// <c>Value</c>), and a <c>For Each</c> loop variable (measured: <c>x</c>). Callers therefore
    /// either use a battery free of both — and assert the PRE-optimization census is empty, so the
    /// post-optimization assertion is the full absolute one — or compare the two censuses as a
    /// DELTA. Do not "fix" this by widening the declared set to class fields: that would encode
    /// front-end internals into the check and weaken it.</para>
    /// </summary>
    internal static List<string> UndeclaredOperandNames(IRModule module)
    {
        var bad = new List<string>();
        foreach (var f in module.Functions)
        {
            if (f.IsExternal) continue;
            var declared = DeclaredNamesOf(module, f);
            foreach (var name in OperandVariableNames(f))
                if (!declared.Contains(name))
                    bad.Add($"{f.Name}::{name}");
        }
        return bad.Distinct().ToList();
    }

    /// <summary>
    /// Every variable NAME the module mentions at all, as <c>function::name</c>: operand-slot
    /// <c>IRVariable</c>s, the destination <c>Name</c> each instruction carries, and the declared
    /// locals and parameters. The union is deliberate — a pass that mints a name can attach it to
    /// any of the three, and the question this answers is "is this name NEW".
    /// </summary>
    internal static SortedSet<string> AllVariableNames(IRModule module)
    {
        var all = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in module.Functions)
        {
            if (f.IsExternal) continue;
            foreach (var n in OperandVariableNames(f)) all.Add($"{f.Name}::{n}");
            foreach (var b in f.Blocks)
                foreach (var i in b.Instructions)
                    if (i is IRValue v && !string.IsNullOrEmpty(v.Name)) all.Add($"{f.Name}::{v.Name}");
            foreach (var n in DeclaredNamesOf(module, f)) all.Add($"{f.Name}::{n}");
        }
        return all;
    }

    /// <summary>
    /// <c>LocalVariables</c> ∪ <c>Parameters</c> ∪ module globals — contract item 2's declared set,
    /// verbatim.
    /// </summary>
    private static SortedSet<string> DeclaredNamesOf(IRModule module, IRFunction function)
    {
        var declared = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var v in function.Parameters ?? new List<IRVariable>())
            if (v?.Name != null) declared.Add(v.Name);
        foreach (var v in function.LocalVariables ?? new List<IRVariable>())
            if (v?.Name != null) declared.Add(v.Name);
        foreach (var name in module.GlobalVariables?.Keys ?? Enumerable.Empty<string>())
            declared.Add(name);
        return declared;
    }

    /// <summary>
    /// Every <c>IRVariable</c> name reachable from one function's instruction stream, through the
    /// SAME reflection walk <see cref="DanglingOperandsOf"/> uses — one walker, for the reason its
    /// docstring gives.
    /// </summary>
    private static SortedSet<string> OperandVariableNames(IRFunction function)
    {
        var stream = new HashSet<object>(
            function.Blocks.SelectMany(b => b.Instructions).Cast<object>(),
            (IEqualityComparer<object>)ReferenceEqualityComparer.Instance);
        var seen = new HashSet<object>(stream, (IEqualityComparer<object>)ReferenceEqualityComparer.Instance);
        var work = new Stack<object>(stream);
        var names = new SortedSet<string>(StringComparer.Ordinal);

        while (work.Count > 0)
        {
            var node = work.Pop();
            foreach (var operand in Operands(node))
            {
                if (operand is IRVariable v && !string.IsNullOrEmpty(v.Name)) names.Add(v.Name);
                if (IsLeaf(operand)) continue;
                if (seen.Add(operand)) work.Push(operand);
            }
        }
        return names;
    }

    /// <summary>
    /// <c>IRVariable</c> and <c>IRConstant</c> are LEAVES: operands legitimately point at them
    /// with no defining instruction anywhere, and their own properties (a variable's declared
    /// initializer, for instance) are declaration data rather than operand slots in the stream.
    /// Every OTHER <c>IRValue</c> kind exists only as an instruction in a block, so a reference to
    /// one that no block holds is an orphan.
    /// </summary>
    private static bool IsLeaf(object node) => node is IRVariable or IRConstant;

    private static IEnumerable<object> Operands(object node)
    {
        if (node == null) yield break;

        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            // Back-references, not operands — following them would walk the whole function.
            if (p.Name is "ParentBlock" or "ParentFunction") continue;

            object? value;
            try { value = p.GetValue(node); } catch { continue; }
            if (value == null) continue;

            if (value is IRValue or IRPatternCase)
            {
                yield return value;
            }
            else if (value is System.Collections.IEnumerable items and not string)
            {
                foreach (var item in items)
                {
                    if (item is IRValue or IRPatternCase) { yield return item; continue; }
                    // Cases and phi operands are value tuples of (IRValue, BasicBlock).
                    var type = item?.GetType();
                    if (type is { IsGenericType: true } && type.Name.StartsWith("ValueTuple"))
                        foreach (var field in type.GetFields())
                            if (field.GetValue(item) is IRValue tupleValue)
                                yield return tupleValue;
                }
            }
        }
    }
}
