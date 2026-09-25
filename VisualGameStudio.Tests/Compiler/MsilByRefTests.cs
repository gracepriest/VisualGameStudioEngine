using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>ByRef</c> parameters on MSIL — write-through, argument storage, refusals.
///
/// <para>⛔ TWO SEPARATE DEFECTS lived behind "MSIL fails every ByRef call", and only one of them
/// is ByRef's own. <c>MSILCodeGenerator.EmitStoreLocal</c> had NO <c>starg</c> arm at all: it
/// walked locals, then instance fields, then properties, then static fields, then module globals,
/// and fell off the end for ANY parameter with <c>// WARNING: Cannot store to 'n'</c>, leaving the
/// computed value on the evaluation stack for <c>ret</c> to reject as an invalid program. That is
/// defect (A) — <c>Sub Bump(n As Integer) : n = n + 1</c> threw <c>InvalidProgramException</c>
/// with no ByRef anywhere in sight, and <see cref="MsilParameterWriteTests"/> covers it on its
/// own. Defect (B), ByRef itself, is this fixture: the signature needs <c>&amp;</c>, a read needs
/// <c>ldind</c>, a write needs <c>stind</c>, and the CALL SITE needs the argument's ADDRESS rather
/// than its value — none of which is reachable until (A) lets a store be emitted at all.</para>
///
/// <para><b>The contract.</b> A ByRef parameter is a managed pointer. Writing it changes the
/// CALLER's storage; writing a ByVal parameter changes only the callee's copy (and must work —
/// see <see cref="MsilParameterWriteTests"/> again). A ByRef argument must name storage with an
/// address — a local, a ByVal or ByRef parameter of the CALLER, an instance field, a
/// <c>Shared</c> field, a module global, or an array element — and nothing else: a literal, an
/// expression or a property has no address and is REFUSED at codegen rather than silently passed
/// by value and its write-back dropped. A ByRef argument's type must match the parameter's
/// EXACTLY, because a managed pointer cannot be coerced. A parameter shadows a same-named field or
/// module global for the STORE exactly as it does for the load.</para>
///
/// <para>⚠ ORACLE RULE — <see cref="FourBackends.RunsOnEveryBackend"/> is NOT usable here.
/// JavaScript refuses every ByRef by design (<c>BL7002</c>, checked at the declaration, so a
/// function with a ByRef parameter never lowers at all). Every running case here therefore asserts
/// C#, C++ and MSIL agree, and separately asserts the JavaScript refusal — the shape
/// <c>ModuleProcedureCallTests.ByRef_ThroughAQualifiedCall_IsMarked</c> already used, copied here
/// as <see cref="AgreesOnThreeBackends"/>. One case (<see cref="ByRefOnASharedMethod_OracleIsCppOnly"/>)
/// drops C# too, for a reason its own docstring gives.</para>
///
/// <para>⚠ Kept to ONE shape per test — the harnesses aggregate several backends' worth of
/// assertions internally (<c>Assert.Multiple</c>), so two shapes in one test would mis-attribute a
/// failure onto the wrong backend.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilByRefTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    /// <summary>
    /// C#, C++ and MSIL run and agree; JavaScript refuses the declaration by design.
    ///
    /// <para><paramref name="cppExpected"/> is an escape hatch for a C++-only spelling. It was
    /// used for <c>CStr(Double)</c>, which printed six decimals on C++ (<c>42.500000</c>) until
    /// C++ got .NET's formatter (CppDoubleFormattingTests); no case needs it now. Omit it and
    /// all three backends are held to the SAME string.</para>
    /// </summary>
    private static void AgreesOnThreeBackends(string program, string expected, string cppExpected = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(cppExpected ?? expected), "C++");
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(() => JavaScriptExecutionTests.RunJs(program),
                Throws.Exception.With.Message.Contains("ByRef"), "JavaScript refuses ByRef by design");
        });
    }

    /// <summary>
    /// A CODEGEN refusal on MSIL. ⚠ <c>MsilHarness.Run</c>'s <c>GenerateFailed</c> outcome is an
    /// oracle for exactly this — a <c>ForeignFeatureException</c> out of the generator, not a
    /// parse or semantic error — and every program below reaches <c>Analyze</c> successfully, so
    /// that front-end/back-end NUnit-4 booking hazard (see <c>docs/HANDOFF.md</c>) does not apply.
    /// </summary>
    private static void RefusedByMsil(string program, string mustSay)
    {
        var r = MsilHarness.Run(program);
        Assert.That(r.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.GenerateFailed), r.Report);
        Assert.That(r.Detail, Does.Contain(mustSay), r.Detail);
    }

    // ========================================================================================
    // THE HEADLINE — a plain ByRef write-through, by declared type. The `ldind`/`stind` opcode
    // is chosen per width (GetIndirectSuffix), so an Integer-only case cannot tell a hardcoded
    // `i4` apart from a correct one; each of the six is its own shape.
    // ========================================================================================

    [Test]
    public void ByRefInteger_WriteIsObservedByTheCaller()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "42");

    /// <summary>
    /// ⚠ The arithmetic deliberately CROSSES THE 32-BIT BOUNDARY — <c>9000000 * 1000000 =
    /// 9000000000000</c>, which does not fit in the low 4 bytes a hardcoded <c>stind.i4</c>
    /// would write. Measured: a small increment (<c>n = n + 1</c> on 41) does NOT discriminate —
    /// it never touches the high word, so a mutant that hardcodes the WRITE suffix to
    /// <c>stind.i4</c> still writes the correct low 32 bits and the test passes regardless. Do
    /// not "simplify" this back to a small increment; it would silently stop testing the width.
    /// </summary>
    [Test]
    public void ByRefLong_UsesTheI8IndirectSuffix()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Long)
             n = n * 1000000
            End Sub
            Sub Main()
             Dim v As Long = 9000000
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "9000000000000");

    /// <summary>A ByRef Double — all three backends print 42.5 (C++ printed 42.500000 before it got
    /// .NET's formatter).</summary>
    [Test]
    public void ByRefDouble_UsesTheR8IndirectSuffix()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Double)
             n = n + 1.5
            End Sub
            Sub Main()
             Dim v As Double = 41.0
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "42.5");

    [Test]
    public void ByRefBoolean_UsesTheI1IndirectSuffix()
        => AgreesOnThreeBackends("""
            Sub Flip(ByRef b As Boolean)
             b = True
            End Sub
            Sub Main()
             Dim v As Boolean = False
             Flip(v)
             PrintLine(CStr(v))
            End Sub
            """, "True");

    /// <summary>Reference-typed: falls to <c>GetIndirectSuffix</c>'s default <c>ref</c> arm.</summary>
    [Test]
    public void ByRefString_UsesTheRefIndirectSuffix()
        => AgreesOnThreeBackends("""
            Sub Shout(ByRef s As String)
             s = s & "!"
            End Sub
            Sub Main()
             Dim v As String = "hi"
             Shout(v)
             PrintLine(v)
            End Sub
            """, "hi!");

    /// <summary>
    /// A user class RESEATED through the pointer, not merely mutated in place — the callee
    /// assigns a brand-new instance to the ByRef parameter itself, so a correct write-through
    /// means the CALLER'S variable now points at the new object.
    /// </summary>
    [Test]
    public void ByRefUserClass_CanBeReseatedByTheCallee()
        => AgreesOnThreeBackends("""
            Class Box
             Public V As Integer = 0
            End Class
            Sub Reseat(ByRef b As Box)
             b = New Box()
             b.V = 9
            End Sub
            Sub Main()
             Dim x As New Box()
             x.V = 1
             Reseat(x)
             PrintLine(CStr(x.V))
            End Sub
            """, "9");

    // ========================================================================================
    // WHAT THE ARGUMENT IS — TryResolveByRefTarget's ladder, one storage kind per test. Each of
    // these is measured to discriminate a specific wrong opcode from the right one.
    // ========================================================================================

    /// <summary>
    /// ⭐ arg = an ARRAY ELEMENT. <c>a(0)</c> lowers to an <c>IRLoad</c> over an
    /// <c>IRGetElementPtr</c> — a managed pointer already parked in a temp — followed by a load
    /// of that pointer's VALUE. The pointer (the GEP) is the address; the loaded copy beside it
    /// is not, and passing the copy would write a throwaway temp while <c>a(0)</c> stayed 41.
    /// </summary>
    [Test]
    public void ArrayElementArgument_PassesTheElementsAddress_NotItsLoadedCopy()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Dim a(3) As Integer
             a(0) = 41
             Bump(a(0))
             PrintLine(CStr(a(0)))
            End Sub
            """, "42");

    /// <summary>⭐ arg = an INSTANCE FIELD — <c>ldflda</c>, reached through <c>Me</c>.</summary>
    [Test]
    public void InstanceFieldArgument_UsesLdflda()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Class Holder
             Public F As Integer = 41
             Public Sub Go()
              Bump(F)
             End Sub
            End Class
            Sub Main()
             Dim h As New Holder()
             h.Go()
             PrintLine(CStr(h.F))
            End Sub
            """, "42");

    /// <summary>⭐ arg = a <c>Shared</c> FIELD — <c>ldsflda</c>, the same opcode a module global
    /// takes; a separate arm from the instance case above (which needs <c>Me</c> under it).</summary>
    [Test]
    public void SharedFieldArgument_UsesLdsflda()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Class Counter
             Public Shared Total As Integer = 41
             Public Shared Sub Go()
              Bump(Total)
             End Sub
            End Class
            Sub Main()
             Counter.Go()
             PrintLine(CStr(Counter.Total))
            End Sub
            """, "42");

    /// <summary>⭐ arg = a MODULE GLOBAL — the same <c>ldsflda</c> arm a <c>Shared</c> field
    /// takes, keyed through <c>_moduleGlobals</c> rather than a class's static-field table.</summary>
    [Test]
    public void ModuleGlobalArgument_UsesLdsflda()
        => AgreesOnThreeBackends("""
            Dim G As Integer = 41
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Bump(G)
             PrintLine(CStr(G))
            End Sub
            """, "42");

    /// <summary>
    /// ⭐ arg = the CALLER'S OWN ByVal PARAMETER. It IS local storage — VB lets a ByVal parameter
    /// be passed on by reference — so this needs <c>ldarga</c>, a DIFFERENT opcode from the next
    /// test's bare <c>ldarg</c>. The callee's write reaches the caller's copy of the argument.
    /// </summary>
    [Test]
    public void CallersByValParameterArgument_UsesLdarga()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Relay(m As Integer)
             Bump(m)
             PrintLine(CStr(m))
            End Sub
            Sub Main()
             Relay(41)
            End Sub
            """, "42");

    /// <summary>
    /// ⭐ arg = the CALLER'S OWN ByRef PARAMETER, ONE LEVEL of nesting. The slot already HOLDS a
    /// managed pointer, so this must stay a bare <c>ldarg</c> — <c>ldarga</c> would hand the
    /// callee a pointer to the RELAY'S argument slot instead of the original variable, and the
    /// write would land one level short. This is a SILENT WRONG ANSWER if gotten backwards: the
    /// program still assembles and runs, it just prints 41 instead of 42.
    /// </summary>
    [Test]
    public void CallersByRefParameterArgument_StaysABareLdarg_NestedOnce()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Relay(ByRef m As Integer)
             Bump(m)
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Relay(v)
             PrintLine(CStr(v))
            End Sub
            """, "42");

    /// <summary>
    /// ⭐ The RECURSIVE form of the same arm — a ByRef parameter passed to a call of ITSELF, five
    /// levels deep. A one-level-short bug compounds here instead of merely hiding: 5 → 4 → 3 → 2
    /// → 1 → 0 is only reached if every recursive call resolves the address correctly.
    /// </summary>
    [Test]
    public void CallersByRefParameterArgument_StaysABareLdarg_Recursive()
        => AgreesOnThreeBackends("""
            Sub CountDown(ByRef n As Integer)
             If n > 0 Then
              n = n - 1
              CountDown(n)
             End If
            End Sub
            Sub Main()
             Dim v As Integer = 5
             CountDown(v)
             PrintLine(CStr(v))
            End Sub
            """, "0");

    // ========================================================================================
    // INTERACTIONS — shapes that need more than the argument ladder alone to go wrong.
    // ========================================================================================

    /// <summary>
    /// ⭐ TWO written ByRef parameters PLUS a TEMPORARY in the same method. Measured: two ByRef
    /// parameters alone do not discriminate the scratch-slot pre-pass (<c>AllocateByRefStoreScratch</c>)
    /// from one that under-advances <c>_localIndices.Count</c> — their own indices still come out
    /// distinct either way. A temporary AFTER them does: an <c>ilasm</c> "Local var slot 1: type
    /// conflict" if the pre-pass and the store scratch disagree about how many slots the ByRef
    /// writes already claimed. The <c>PrintLine(CStr(...))</c> calls are what allocate the temp.
    /// </summary>
    [Test]
    public void TwoWrittenByRefParametersPlusATemporary_DoNotCollideOnALocalsSlot()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef a As Integer, ByRef d As Double)
             a = a + 1
             d = d + 0.5
             PrintLine(CStr(a))
             PrintLine(CStr(d))
            End Sub
            Sub Main()
             Dim x As Integer = 1
             Dim y As Double = 41.0
             Bump(x, y)
             PrintLine(CStr(x))
             PrintLine(CStr(y))
            End Sub
            """, "2\n41.5\n2\n41.5");

    /// <summary>⭐ A ByRef parameter used as a counted <c>For</c> INDUCTION VARIABLE inside the
    /// callee — both defects at once: the loop write needs the parameter STORE arm (defect A),
    /// and each write needs to land through the pointer (defect B).</summary>
    [Test]
    public void ByRefParameterAsInductionVariable_BothDefectsAtOnce()
        => AgreesOnThreeBackends("""
            Sub RunToThree(ByRef n As Integer)
             For n = 1 To 3
             Next
            End Sub
            Sub Main()
             Dim v As Integer = 99
             RunToThree(v)
             PrintLine(CStr(v))
            End Sub
            """, "4");

    /// <summary>⭐ A ByRef write inside a <c>Try</c>/<c>Catch</c>. Exception-handling regions are
    /// a SEPARATE emission path (<c>EmitRegionBody</c>) from an ordinary block, and this backend's
    /// history has three passes (locals allocation, the try body, the catch body) sharing one
    /// <c>.locals</c> table — this shape is what would show them disagreeing.</summary>
    [Test]
    public void ByRefWriteInsideTryCatch_NoExceptionThrown()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             Try
              n = n + 1
             Catch ex As Exception
              n = -1
             End Try
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "42");

    /// <summary>
    /// ⭐ A ByRef parameter on a <c>Shared</c> METHOD. ⭐ PROMOTED — renamed from
    /// <c>ByRefOnASharedMethod_OracleIsCppOnly</c>, which is no longer true: the analyzer used to
    /// record NO by-ref marker for a <c>Type.SharedMethod</c> call (only <c>NetRefKind</c>, the
    /// .NET-interop marshalling list, was consulted), so the C# backend emitted raw CS1620
    /// ("argument must be passed with the 'ref' keyword") and, worse, the OPTIMIZER trusted the
    /// false by-value claim and kept the caller's pre-call value live across the call — a SILENT
    /// wrong answer on C++ and MSIL (see <see cref="ByRefOnASharedMethod_TheOptimizerKeepsNoFactsAcrossTheCall"/>).
    /// <c>IRBuilder</c> now also reads the user callee's OWN declared parameters
    /// (<c>staticCalleeSymbol.Parameters[..].IsByRef</c>), so all three .NET-observable backends
    /// agree. This shape is precisely what proves the DECLARATION, not the call site, must key
    /// whether an argument is loaded by address on MSIL: keying on <c>IRCall.ByRefArguments</c>
    /// instead would spell the signature <c>(int32&amp;)</c> from the declaration while loading
    /// the argument as a value from the call site — an invalid program.
    /// </summary>
    [Test]
    public void ByRefOnASharedMethod()
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

    /// <summary>
    /// ⭐ S6b — THE SHAPE THAT PROVES THE OPTIMIZER KEEPS NO FACTS ACROSS THE CALL, not just that
    /// the call writes back correctly. <c>a</c> is computed from <c>v</c> BEFORE
    /// <c>Util.Bump(v)</c>, <c>b</c> AFTER. ⛔ MEASURED, at the defect: <c>a=42 b=42</c> on C++
    /// and MSIL — <c>ConstantPropagationPass</c> (or an equivalent copy-forward) trusted the false
    /// "no ByRef here" claim from <c>IRBuilder</c> and propagated <c>v</c>'s PRE-CALL value
    /// (41, so <c>v+1=42</c>) into the read of <c>b</c> too, silently dropping the call's
    /// write-back from the second computation. Correct is <c>a=42 b=43</c> — the two reads must
    /// differ. Run through BOTH the standard pipeline (<see cref="AgreesOnThreeBackends"/>, via
    /// <c>FourBackends.RunEmittedCSharp</c> / <c>BclE2E.CompileToCppOptimized</c> /
    /// <c>MsilHarness.RunExpectingSuccess</c>, all standard-pass) and the aggressive one, since
    /// either pipeline could re-introduce the same false fact through a different pass.
    /// </summary>
    [Test]
    public void ByRefOnASharedMethod_TheOptimizerKeepsNoFactsAcrossTheCall()
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

    /// <summary>The aggressive-pipeline sibling of the test above — same shape, same reasoning:
    /// a different pass could re-introduce the same false fact under <c>--optimize</c>.</summary>
    [Test]
    public void ByRefOnASharedMethod_TheOptimizerKeepsNoFactsAcrossTheCall_Aggressive()
    {
        const string program = """
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
            """;
        const string expected = "a=42 b=43";
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(FourBackends.RunEmittedCSharpAggressive(program)), Is.EqualTo(expected), "C#");
            Assert.That(Norm(MsilHarness.RunAggressiveExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(() => JavaScriptExecutionTests.RunJs(program),
                Throws.Exception.With.Message.Contains("ByRef"), "JavaScript refuses ByRef by design");
        });
    }

    // ========================================================================================
    // PER-METHOD RESET — the ByRef tables (`_byRefParams`, `_byRefStoreScratch`) are cleared at
    // the top of EVERY method-emitting entry point. A leaked entry would make the NEXT method's
    // same-named ByVal parameter get dereferenced as if it were still a pointer.
    // ========================================================================================

    /// <summary>⭐ Module-function reset — a ByRef Sub followed by an unrelated Sub whose
    /// parameter happens to share the ByRef one's name.</summary>
    [Test]
    public void ByRefStateDoesNotLeakIntoTheNextModuleFunction()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Relabel(n As Integer)
             n = n + 10
             PrintLine(CStr(n))
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Bump(v)
             PrintLine(CStr(v))
             Relabel(5)
            End Sub
            """, "42\n15");

    /// <summary>⭐ The CLASS-MEMBER reset — a different site (<c>InitializeMethodContext</c>) from
    /// the module-function one above, so it needs its own case.</summary>
    [Test]
    public void ByRefStateDoesNotLeakBetweenClassMembers()
        => AgreesOnThreeBackends("""
            Class Worker
             Public Sub Bump(ByRef n As Integer)
              n = n + 1
             End Sub
             Public Sub Relabel(n As Integer)
              n = n + 10
              PrintLine(CStr(n))
             End Sub
            End Class
            Sub Main()
             Dim w As New Worker()
             Dim v As Integer = 41
             w.Bump(v)
             PrintLine(CStr(v))
             w.Relabel(5)
            End Sub
            """, "42\n15");

    // ========================================================================================
    // REFUSALS — every shape below has exactly one address-free alternative (push the value,
    // let the callee write a throwaway copy), and that alternative ASSEMBLES AND RUNS. Refusing
    // loudly is the point; message-checked via MsilHarness.Run + GenerateFailed.
    // ========================================================================================

    [Test]
    public void LiteralArgument_IsRefused() =>
        RefusedByMsil("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Bump(41)
             PrintLine("done")
            End Sub
            """, "a literal has no address");

    /// <summary>
    /// ⚠ <c>Bump(v + 1)</c> would constant-fold to the LITERAL arm before this ever runs — this
    /// uses <c>v * w</c>, two variables, so the expression survives to the temporary arm.
    /// </summary>
    [Test]
    public void UnfoldableExpressionArgument_IsRefused() =>
        RefusedByMsil("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Dim w As Integer = 2
             Bump(v * w)
             PrintLine(CStr(v))
            End Sub
            """, "cannot be passed by reference");

    [Test]
    public void QualifiedPropertyArgument_IsRefused() =>
        RefusedByMsil("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Class Holder
             Private _x As Integer = 41
             Public Property X As Integer
              Get
               Return _x
              End Get
              Set(value As Integer)
               _x = value
              End Set
             End Property
            End Class
            Sub Main()
             Dim h As New Holder()
             Bump(h.X)
             PrintLine(CStr(h.X))
            End Sub
            """, "an expression's value lives in a temporary");

    /// <summary>⭐ A BARE property name, from inside the declaring class. Since ADR-0007 IRBuilder
    /// lowers a bare Get/Set property to the node its qualified form produces (an IRFieldAccess
    /// on <c>Me</c>), so it reaches the ladder as a TEMPORARY and is refused exactly as
    /// <see cref="QualifiedPropertyArgument_IsRefused"/> is — and, being no longer a name at all,
    /// it cannot fall through to a same-named module global. (The ladder's own "is a PROPERTY"
    /// arm still guards a bare plain AUTO-property, which stays a variable.)</summary>
    [Test]
    public void BarePropertyNameArgument_IsRefused() =>
        RefusedByMsil("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Class Holder
             Private _x As Integer = 41
             Public Property X As Integer
              Get
               Return _x
              End Get
              Set(value As Integer)
               _x = value
              End Set
             End Property
             Public Sub Go()
              Bump(X)
             End Sub
            End Class
            Sub Main()
             Dim h As New Holder()
             h.Go()
             PrintLine(CStr(h.X))
            End Sub
            """, "an expression's value lives in a temporary");

    /// <summary>A managed pointer cannot be coerced, so a mismatched declared type is refused
    /// rather than routed through a widening temporary — both directions.</summary>
    [Test]
    public void WideningMismatch_IntegerArgumentIntoAByRefDoubleParameter_IsRefused() =>
        RefusedByMsil("""
            Sub Bump(ByRef n As Double)
             n = n + 1
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "but the parameter is declared float64 ByRef");

    [Test]
    public void NarrowingMismatch_DoubleArgumentIntoAByRefIntegerParameter_IsRefused() =>
        RefusedByMsil("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Dim v As Double = 41.5
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "but the parameter is declared int32 ByRef");

    // ========================================================================================
    // READ-SIDE PINS — the read path through a ByRef parameter was never broken by this fix;
    // these pin that it still is not.
    // ========================================================================================

    /// <summary>Read BEFORE and AFTER the call, twice — pins that the optimizer does not
    /// copy-propagate a value across a ByRef call boundary.</summary>
    [Test]
    public void VariableIsReadAcrossTheCall_TwiceInARow()
        => AgreesOnThreeBackends("""
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Sub Main()
             Dim v As Integer = 41
             PrintLine(CStr(v))
             Bump(v)
             PrintLine(CStr(v))
             Bump(v)
             PrintLine(CStr(v))
            End Sub
            """, "41\n42\n43");

    /// <summary>The callee only READS through the ByRef parameter — this passed even before the
    /// fix (only a WRITE ever failed), and it pins that the read path stayed correct.</summary>
    [Test]
    public void CalleeOnlyReadsThroughTheByRefParameter()
        => AgreesOnThreeBackends("""
            Sub Peek(ByRef n As Integer)
             PrintLine(CStr(n))
            End Sub
            Sub Main()
             Dim v As Integer = 41
             Peek(v)
            End Sub
            """, "41");

    // ========================================================================================
    // ⛔ PIN, NOT THIS FAMILY'S — a SHARED FRONT-END GAP that happens to be visible from here.
    // ========================================================================================

    /// <summary>
    /// ⛔ PINNED SHARED FRONT-END GAP. A ByRef parameter on a CONSTRUCTOR loses its marker in
    /// <c>IRBuilder.Visit(ConstructorNode)</c> (<c>BasicLang/IRBuilder.cs:1885</c>), which builds
    /// every ctor parameter as <c>new IRVariable(param.Name, paramType) { IsParameter = true }</c>
    /// and never copies <c>IsByRef</c> — unlike every OTHER parameter site in that file. No
    /// backend ever sees a ByRef constructor parameter, so this is measured wrong and IDENTICAL
    /// on MSIL and C++: both print 41 (the pre-increment value) for the caller's variable, then 42
    /// for the field the constructor set from its own local copy. Correct would be 42 / 42.
    /// <b>MSIL introduces no divergence of its own here</b> — it agrees with C++ before and after
    /// this family's fix, so this pin does not belong to the ByRef family this fixture covers; it
    /// is recorded here, at the cause, rather than silently promoted or dropped. ⚠ ONLY C++ and
    /// MSIL are asserted — they are what the cause names and what was measured. C# and JavaScript
    /// are deliberately NOT asserted here: nothing above claims what they do with this shape, and
    /// guessing would risk pinning an unmeasured claim.
    /// </summary>
    [Test]
    public void ConstructorByRefParameter_IsAPinnedSharedFrontEndGap_NotThisFamilys()
    {
        const string program = """
            Class Holder
             Public V As Integer = 0
             Public Sub New(ByRef n As Integer)
              n = n + 1
              V = n
             End Sub
            End Class
            Sub Main()
             Dim v As Integer = 41
             Dim h As New Holder(v)
             PrintLine(CStr(v))
             PrintLine(CStr(h.V))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo("41\n42"), "C++");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("41\n42"), "MSIL");
        });
    }
}
