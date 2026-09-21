using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A counted <c>For</c> whose induction variable is INFERRED — <c>For i = 1 To 3</c>, no
/// <c>As Type</c> — on ALL FOUR backends. Despite how the gap was originally briefed (as an
/// MSIL-only defect, chip task_4cc381f1's sibling), the implementer's own measurement showed it
/// is shared IR-level breakage, not a backend-specific one:
///
/// <list type="table">
/// <item><term>C#</term><description><c>CS0103: The name 'i' does not exist in the current
/// context</c></description></item>
/// <item><term>JavaScript</term><description><c>ReferenceError: i is not defined</c></description></item>
/// <item><term>C++</term><description><c>error: use of undeclared identifier 'i'</c></description></item>
/// <item><term>MSIL</term><description><c>InvalidProgramException</c> (<c>// WARNING: Unknown
/// local 'i'</c> in the emitted IL)</description></item>
/// </list>
///
/// <para>⛔ THE CAUSE. There is no <c>IRFor</c> node — a counted <c>For</c> lowers to ordinary
/// blocks (a compare, a conditional branch, an increment), and every backend writes its
/// declarations from <c>IRFunction.LocalVariables</c>. <c>IRBuilder.Visit(ForLoopNode)</c>
/// registered the induction variable there only inside the EXPLICIT-type arm (<c>For i As
/// Integer = ...</c>), so the ordinary, far more common inferred spelling produced a loop over a
/// variable nothing had declared. This is NOT the <c>For Each</c> situation
/// (<see cref="MsilForEachTests"/>'s defect (B)): there, <c>IRBuilder</c> deliberately keeps the
/// element variable OUT of <c>LocalVariables</c> because <c>foreach</c>/<c>for(:of)</c> declares
/// it in the target language's own loop header. A counted <c>For</c> has no such header; every
/// backend just emits a bare assignment, so it needs the declaration IRBuilder was skipping.</para>
///
/// <para><b>The fix — and why it needs a guard.</b> The induction variable now gets a
/// function-scoped local, typed from an explicit <c>As Type</c> when present and from the start
/// value otherwise, UNLESS the name already resolves to storage: a declared local, a live SSA
/// binding (which also covers a parameter), a module global (keyed through
/// <c>_moduleGlobals</c> exactly as <c>GetOrCreateVariable</c> keys it, with the bare
/// <c>_globalVariables</c> fallback it consults next), or a field/property of the enclosing class
/// (<c>IsCurrentClassMember</c>). ⚠ The module-global arm was DELETED during development as
/// apparently redundant and that deletion was a REGRESSION — see
/// <c>InferredCountedFor_OverAModuleGlobal_DrivesTheGlobal_NotAShadow</c>. Without that guard, <c>For Total = 1 To 3</c> over a class
/// FIELD would acquire a same-named local that shadows the member, and the loop would silently
/// stop mutating it — measured by the implementer's own mutation sweep, which produced
/// <c>CS0136</c> for exactly this shape when the guard was disabled.</para>
///
/// <para>⚠ EVERY CASE ASSERTS ALL FOUR BACKENDS (<c>FourBackends.RunsOnEveryBackend</c>) unless
/// its own docstring says otherwise — the property this family fixes is shared IR breakage, so
/// "every backend agrees" is the point, not "MSIL agrees with C#".</para>
///
/// <para>⚠ Kept to ONE shape per test, matching <see cref="MsilForEachTests"/>'s own rule:
/// <c>FourBackends.RunsOnEveryBackend</c> and <c>MsilHarness.RunExpectingSuccess</c> each use
/// <c>Assert.Multiple</c> / aggregate several backends' worth of assertions internally, so mixing
/// two different program shapes into one test would mis-attribute one shape's failure onto
/// another's backend.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CountedForVariableTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);

    // ========================================================================================
    // THE HEADLINE — an inferred counted For simply runs, on every backend. Before the fix NONE
    // of them did.
    // ========================================================================================

    [Test]
    public void InferredCountedFor_SumsOneToThree_OnEveryBackend()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For i = 1 To 3\n" +
            "  total = total + i\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "6");

    // ========================================================================================
    // RISKY EDGE 1 — `For <field> = 1 To 3`. The guard's IsCurrentClassMember arm is what stops
    // the loop shadowing the member instead of mutating it; the field is read back through a
    // METHOD call from Main (not a qualified indexer — no relation to the front-end
    // `g.Items(0)` gap), a genuinely different scope than the one that ran the loop.
    // ========================================================================================

    [Test]
    public void CountedFor_OverAClassField_MutatesTheFieldNotAShadow()
        => RunsOnEveryBackend(
            "Class Counter\n" +
            " Public Total As Integer = 0\n" +
            " Public Sub Run()\n" +
            "  For Total = 1 To 3\n" +
            "  Next\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim c As New Counter()\n" +
            " c.Run()\n" +
            " PrintLine(CStr(c.Total))\n" +
            "End Sub",
            // The induction variable's value AFTER the loop is one past the end (the increment
            // that makes the condition finally false) — 4, not 3. Confirmed against the
            // ordinary `Dim i = 100 : For i = 1 To 3` case below, which leaves the same 4.
            "4");

    // ========================================================================================
    // RISKY EDGE 2 — `Dim i As Integer = 100` then `For i = 1 To 3` reuses an EXISTING local.
    // This already worked on all four backends before the fix (the explicit-local case never
    // needed the new registration) and must keep doing so with ONE slot, not two.
    // ========================================================================================

    [Test]
    public void CountedFor_OverAnExistingLocal_KeepsOneSlot()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim i As Integer = 100\n" +
            " For i = 1 To 3\n" +
            " Next\n" +
            " PrintLine(CStr(i))\n" +
            "End Sub",
            "4");

    // ========================================================================================
    // RISKY EDGE 3 — two INFERRED loops sharing a name in one function must not register a
    // second local (LocalVariables already has "i" from the first loop by the time the second
    // one runs the guard).
    // ========================================================================================

    [Test]
    public void TwoInferredLoopsSharingAName_DoNotDoubleRegister()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For i = 1 To 2\n" +
            "  total = total + i\n" +
            " Next\n" +
            " For i = 1 To 3\n" +
            "  total = total + i\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // (1+2) + (1+2+3) = 3 + 6 = 9
            "9");

    // ========================================================================================
    // RISKY EDGE 4 — a counted For nested inside a For Each, and the reverse. The For Each
    // element variable is deliberately NOT in LocalVariables (a different, older defect family);
    // these pin that the two loop kinds' storage does not interfere either direction.
    // ========================================================================================

    [Test]
    public void CountedFor_NestedInsideAForEach_DoesNotInterfereWithTheElementVariable()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(10)\n" +
            " l.Add(20)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  For i = 1 To 2\n" +
            "   total = total + i\n" +
            "  Next\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // Per outer pass: inner adds 1+2=3, then + n. Pass 1 (n=10): running total 3, then
            // 13. Pass 2 (n=20): +1=14, +2=16, then +20=36.
            "36");

    [Test]
    public void ForEach_NestedInsideACountedFor_DoesNotInterfereWithTheInductionVariable()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim total As Integer = 0\n" +
            " For i = 1 To 2\n" +
            "  For Each n In l\n" +
            "   total = total + n\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // (1+2) summed once per outer pass, two passes -> 6.
            "6");

    // ========================================================================================
    // RISKY EDGE 5 — class method / constructor / property Get.
    // ========================================================================================

    [Test]
    public void CountedFor_InsideAClassInstanceMethod()
        => RunsOnEveryBackend(
            "Class Summer\n" +
            " Public Function SumUpTo(n As Integer) As Integer\n" +
            "  Dim total As Integer = 0\n" +
            "  For i = 1 To n\n" +
            "   total = total + i\n" +
            "  Next\n" +
            "  Return total\n" +
            " End Function\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim s As New Summer()\n" +
            " PrintLine(CStr(s.SumUpTo(4)))\n" +
            "End Sub",
            "10");

    [Test]
    public void CountedFor_InsideAConstructor()
        => RunsOnEveryBackend(
            "Class Summer\n" +
            " Public Total As Integer\n" +
            " Public Sub New(n As Integer)\n" +
            "  Dim sum As Integer = 0\n" +
            "  For i = 1 To n\n" +
            "   sum = sum + i\n" +
            "  Next\n" +
            "  Total = sum\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim s As New Summer(4)\n" +
            " PrintLine(CStr(s.Total))\n" +
            "End Sub",
            "10");

    /// <summary>
    /// ⛔ C# CANNOT COMPILE THIS SHAPE, independent of a counted <c>For</c>'s own fix: a property
    /// <c>Get</c> accessor's body hoists its locals through a path that misses anything declared
    /// inside ANY loop (measured with a bare classic <c>For</c> too, not only a counted one) —
    /// <c>CS0103</c> on the loop variable and every local the loop's body touches. A separate,
    /// pre-existing, general C#-backend gap; MSIL and JavaScript are the oracles here instead.
    /// MSIL alone was independently confirmed correct for this exact program before this fixture
    /// existed.
    /// </summary>
    [Test]
    public void CountedFor_InsideAPropertyGetter()
    {
        const string program =
            "Class Box\n" +
            " Private _items As List(Of Integer)\n" +
            " Public Sub Seed(l As List(Of Integer))\n" +
            "  _items = l\n" +
            " End Sub\n" +
            " Public ReadOnly Property Total As Integer\n" +
            "  Get\n" +
            "   Dim sum As Integer = 0\n" +
            "   For i = 0 To _items.Count - 1\n" +
            "    sum = sum + _items(i)\n" +
            "   Next\n" +
            "   Return sum\n" +
            "  End Get\n" +
            " End Property\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim b As New Box()\n" +
            " b.Seed(l)\n" +
            " PrintLine(CStr(b.Total))\n" +
            "End Sub";
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo("7"), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("7"), "MSIL");
        });
    }

    // ========================================================================================
    // RISKY EDGE 6 — `For <parameter> = 1 To 3`. Used to be an INDEPENDENT MSIL-only pin; closed
    // below — MSIL now runs it with the other three, like every other case in this fixture.
    // ========================================================================================

    /// <summary>
    /// ⛔ PIN CLOSED. This was
    /// <c>CountedFor_OverAParameter_CSharpAndJavaScriptAgree_MsilIsAPinnedPreexistingGap</c>: C#
    /// and JavaScript printed 4 while MSIL threw <c>InvalidProgramException</c> on this exact
    /// program, pinned as a divergence rather than folded into the shared assertion. The cause
    /// was never <c>For</c>-specific — <c>MSILCodeGenerator.EmitStoreLocal</c> had no
    /// <c>starg</c> arm at all, so writing to ANY parameter (ByRef or not) walked off the end of
    /// the local/field/property/static-field ladder with <c>// WARNING: Cannot store to 'n'</c>
    /// and left the computed value on the stack for <c>ret</c> to choke on. Fixed alongside the
    /// ByRef family that shares the same root cause (see <c>MsilParameterWriteTests</c> /
    /// <c>MsilByRefTests</c>). Now runs on all four backends and prints 4, one past the end, the
    /// same as every other case in this fixture.
    /// </summary>
    [Test]
    public void CountedFor_OverAParameter_RunsOnEveryBackend_IncludingMsil()
        => RunsOnEveryBackend(
            "Sub Bump(n As Integer)\n" +
            " For n = 1 To 3\n" +
            " Next\n" +
            " PrintLine(CStr(n))\n" +
            "End Sub\n\n" +
            "Sub Main()\n" +
            " Bump(0)\n" +
            "End Sub",
            "4");

    // ========================================================================================
    // RISKY EDGE 7 — Step, negative Step, Exit For, zero iterations, variable bounds, writing
    // the induction variable in the body.
    // ========================================================================================

    [Test]
    public void CountedFor_ZeroIterations_WhenStartIsPastEnd()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For i = 5 To 1\n" +
            "  total = total + 1\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "0");

    [Test]
    public void CountedFor_NegativeStep_CountsDown()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim s As String = \"\"\n" +
            " For i = 5 To 1 Step -2\n" +
            "  s = s & CStr(i) & \",\"\n" +
            " Next\n" +
            " PrintLine(s)\n" +
            "End Sub",
            "5,3,1,");

    /// <summary>
    /// ⚠ <c>Exit For</c> in a COUNTED loop was not the <c>For Each</c> no-op defect. ⛔ <b>But the
    /// counted loop was not simply fine either, and the sentence that used to stand here said it
    /// was.</b> What already worked on every backend including C#, before and after this family's
    /// change, is exactly the shape below: an <c>Exit For</c> inside an <c>If … End If</c>.
    /// Measured at f20435d, the SAME <c>Exit For</c> written elsewhere in a counted loop did not:
    /// as the body's LAST statement it made the emitted C# loop FOREVER, and inside a
    /// <c>Select Case</c> arm it totalled 7 instead of 3. Both are pinned in
    /// <see cref="CSharpLoopExitTests"/>. This case pins only that the induction variable's new
    /// storage does not disturb the shape that did work.
    /// </summary>
    [Test]
    public void CountedFor_ExitFor_LeavesTheLoop_NotANoOp()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For i = 1 To 10\n" +
            "  If i = 4 Then\n" +
            "   Exit For\n" +
            "  End If\n" +
            "  total = total + i\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // 1+2+3, exits before adding 4.
            "6");

    [Test]
    public void CountedFor_VariableBounds_NotJustLiterals()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim lo As Integer = 2\n" +
            " Dim hi As Integer = 5\n" +
            " Dim total As Integer = 0\n" +
            " For i = lo To hi\n" +
            "  total = total + i\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // 2+3+4+5 = 14
            "14");

    [Test]
    public void CountedFor_InductionVariableWrittenInTheBody_WriteIsObservable()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For i = 1 To 3\n" +
            "  i = i + 10\n" +
            "  total = total + i\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // i=1 -> 11 (total 11); next iteration's condition re-reads the WRITTEN i (11),
            // fails 11+1<=3, loop runs exactly once.
            "11");

    [Test]
    public void MixedExplicitAndInferredNesting_EachGetsCorrectStorage()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For i As Integer = 1 To 2\n" +
            "  For j = 1 To 2\n" +
            "   total = total + i * j\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            // i=1: j=1,2 -> 1+2=3. i=2: j=1,2 -> 2+4=6. Total 9.
            "9");

    // ========================================================================================
    // RISKY EDGE 8 — CROSS-FAMILY: an indexed write inside an inferred counted For exercises
    // BOTH fixes (this one's storage for `i`, and MsilIndexerStoreTests's set_Item emission) in
    // one program.
    // ========================================================================================

    [Test]
    public void IndexedWriteInsideACountedFor_ExercisesBothFixesAtOnce()
        => RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(0)\n" +
            " l.Add(0)\n" +
            " For i = 0 To 1\n" +
            "  l(i) = 9\n" +
            " Next\n" +
            " PrintLine(CStr(l(0)) & \"/\" & CStr(l(1)))\n" +
            "End Sub",
            "9/9");

    // ========================================================================================
    // A MODULE GLOBAL — a regression this fixture caught mid-development, and the matched pair
    // that pins the fix's actual shape rather than the first, too-broad attempt at it.
    // ========================================================================================

    /// <summary>
    /// <c>For g = 1 To 3</c> (INFERRED, no <c>As Type</c>) declares nothing — it drives whatever
    /// <c>g</c> already denotes, exactly as the field case above
    /// (<c>CountedFor_OverAClassField_MutatesTheFieldNotAShadow</c>) drives the field. Read back
    /// from a DIFFERENT <c>Sub</c> than the one that ran the loop, so a shadow (if one existed)
    /// could not go unnoticed by both sides landing in the same scope.
    ///
    /// <para>⛔ THIS WAS A REGRESSION THIS FIXTURE CAUGHT, not one of the family's originally
    /// flagged edges. <c>ResolvesToExistingStorage</c>'s live-SSA-version arm only sees a version
    /// for <c>g</c> in whichever function already touched it, not in every function that can
    /// reach the module global — so the guard saw no existing storage in <c>Bump</c>, registered
    /// a fresh local there, and every backend's own local declaration then shadowed the global
    /// for the rest of <c>Bump</c>; the write never reached it. First pinned here as the
    /// then-current (wrong) behaviour — printing <c>0</c> — with a note asking to flip it once
    /// fixed. The implementer rebuilt the PRE-FIX generator from this exact commit, confirmed
    /// <c>4</c> pre-fix / <c>0</c> regressed / <c>4</c> now on all three backends below (plus two
    /// further shapes this test alone could not see: the corruption is also observable from
    /// INSIDE <c>Bump</c> itself, and from a global assigned before the loop runs), and closed
    /// the gap by giving <c>ResolvesToExistingStorage</c> a <c>_moduleGlobals</c>-keyed arm. This
    /// is that flip.</para>
    /// </summary>
    [Test]
    public void InferredCountedFor_OverAModuleGlobal_DrivesTheGlobal_NotAShadow()
    {
        const string program =
            "Dim g As Integer = 0\n\n" +
            "Sub Bump()\n" +
            " For g = 1 To 3\n" +
            " Next\n" +
            "End Sub\n\n" +
            "Sub Main()\n" +
            " Bump()\n" +
            " PrintLine(CStr(g))\n" +
            "End Sub";
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo("4"), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo("4"), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("4"), "MSIL");
        });
    }

    /// <summary>
    /// The matched pair to the test above, and the reason the fix needs TWO arms rather than
    /// one. <c>For g As Integer = 1 To 3</c> (EXPLICIT <c>As Type</c>) DECLARES <c>g</c> — VB's
    /// rule for an inline-typed counted <c>For</c> is that it introduces a loop-scoped variable,
    /// which SHADOWS a same-named field or module global, exactly as every backend already
    /// treated it before any of this family's changes. So this prints <c>0</c>: the loop-scoped
    /// shadow runs 1..3 and is discarded when <c>Bump</c> returns, leaving the real module global
    /// untouched at its initial value.
    ///
    /// <para>⛔ A single storage-resolving guard shared by both spellings CANNOT get both this
    /// test and the one above right at once — one says "drive the existing global", the other
    /// says "always shadow it", for the same name in the same position. The first version of the
    /// module-global fix put both spellings behind <c>ResolvesToExistingStorage</c> and would
    /// have made THIS shape wrongly print <c>4</c> the moment the module-global arm was added;
    /// the shipped fix keeps the explicit-type arm unconditional (its own local, deduped only
    /// against itself) and routes only the inferred spelling through the storage guard. Proof
    /// this test is load-bearing: mutant <c>f13-explicit-shares-inferred-guard</c> — which
    /// collapses the two arms back into one, precisely the mistake that caused the regression the
    /// pair above pins — survives every other test in this fixture and is killed only by this
    /// one, failing exactly its three legs.</para>
    /// </summary>
    [Test]
    public void ExplicitlyTypedCountedFor_OverAModuleGlobalsName_DeclaresAShadow_NotThePair()
    {
        const string program =
            "Dim g As Integer = 0\n\n" +
            "Sub Bump()\n" +
            " For g As Integer = 1 To 3\n" +
            " Next\n" +
            "End Sub\n\n" +
            "Sub Main()\n" +
            " Bump()\n" +
            " PrintLine(CStr(g))\n" +
            "End Sub";
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo("0"), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo("0"), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("0"), "MSIL");
        });
    }
}
