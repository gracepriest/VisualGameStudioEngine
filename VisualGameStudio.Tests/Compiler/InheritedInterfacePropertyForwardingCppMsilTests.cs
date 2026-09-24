using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0004 D1's inherited-implementation case: an interface property filled by a member the
/// implementing class INHERITS from a base class, rather than declaring itself — the shape
/// <c>InterfaceImplementationLookup.InheritedInterfaceAccessors</c> exists for
/// (<c>InterfaceImplementationLookup.cs</c>), consumed by the C++ forwarder emission
/// (<c>CppCodeGenerator.cs</c>) and the MSIL stub emission (<c>MSILBackend.cs</c>).
///
/// <para><b>C++ and MSIL legs only, on purpose.</b> C# and JavaScript already ran every one of
/// these shapes correctly before this batch (an inherited member satisfying an interface is
/// ordinary C#/JS semantics with nothing to forward) — see
/// <see cref="InheritedInterfacePropertyForwardingFlagsTests"/> for those two legs, which is
/// gated on the D1 flag-fix commit for reasons that do not apply here. This file's four shapes
/// (G5, G10, G11, G12) needed NOTHING from this batch on C++/MSIL — they already ran correctly
/// at every measured sub-step (head through flags) and are here purely as NO-REGRESSION guards
/// against <c>InterfaceImplementationLookup</c> ever narrowing back to "own <c>Implements</c>
/// only". G9 is the one shape that actually moves: C++ is wrong at head (a pre-existing
/// <c>construct_at</c> compile failure, unrelated to this batch, that the cpp sub-step's
/// unrelated C++ fixes happen to also clear) and right from the cpp sub-step on; MSIL throws
/// <c>TypeLoadException: Method 'get_Slot' in type 'Holder' ... does not have an implementation</c>
/// through the msil sub-step and is right only once the msil sub-step's <c>newslot virtual
/// final</c> per-accessor stub lands.</para>
///
/// <para><b>Sub-step attribution</b> (measured against the family's probe matrices,
/// <c>matrix-head/cpp/msil/types/flags.txt</c> in the session scratchpad,
/// <c>scratchpad/f111f/full/summary.txt</c>):</para>
/// <list type="bullet">
/// <item>G5, G10, G11, G12 — C++ and MSIL: <c>RAN OK</c> at every sub-step, head included. Pure
/// no-regression guards; this file could be committed at ANY sub-step, but it needs G9, which
/// needs msil.</item>
/// <item>G9 — C++: <c>COMPILE-FAIL</c> at head, <c>RAN OK</c> from cpp on. MSIL:
/// <c>RUN-FAIL</c> (TypeLoadException) through msil's own sub-step matrix at head/cpp,
/// <c>RAN OK</c> from msil on (i.e. only once the msil patch itself is applied).</item>
/// </list>
/// <para>So this whole file's first fully-green sub-step is <b>msil</b> — committed there.</para>
///
/// <para>Marked Integration: every assertion here compiles and runs native C++ and spawns
/// <c>ilasm</c>/the CLR.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class InheritedInterfacePropertyForwardingCppMsilTests
{
    // G5 — a plain read/write interface property filled by a DIRECT base class member; the
    // derived class (accessed here) declares no Slot itself and no Implements clause of its own
    // effect beyond what the base already provides.
    private const string DirectBaseImplementation =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class BaseHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits BaseHolder\n" +
        "    Implements IHolder\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"g5\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void ADirectBaseClassMemberFillsAnInheritedInterfaceProperty_RunsOnCpp() // G5
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(DirectBaseImplementation))),
            Is.EqualTo("g5"));

    [Test]
    public void ADirectBaseClassMemberFillsAnInheritedInterfaceProperty_RunsOnCppAggressive() // G5
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(DirectBaseImplementation))),
            Is.EqualTo("g5"));

    [Test]
    public void ADirectBaseClassMemberFillsAnInheritedInterfaceProperty_RunsOnMsil() // G5
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(DirectBaseImplementation)), Is.EqualTo("g5"));

    [Test]
    public void ADirectBaseClassMemberFillsAnInheritedInterfaceProperty_RunsOnMsilAggressive() // G5
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(DirectBaseImplementation)), Is.EqualTo("g5"));

    // G9 — a ReadOnly interface property with an EXPLICIT empty Get block, filled by a
    // (read/write) DIRECT base member. The explicit-empty-Get shape is the one this batch's
    // sibling (InterfaceAccessorBatchTests' P10) already fixed for the OWN-declared case; G9
    // pins the same shape through inherited-member forwarding.
    private const string DirectBaseImplementationExplicitEmptyGet =
        "Interface IHolder\n" +
        "    ReadOnly Property Slot As String\n" +
        "        Get\n" +
        "        End Get\n" +
        "    End Property\n" +
        "End Interface\n\n" +
        "Class BaseHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits BaseHolder\n" +
        "    Implements IHolder\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"g9\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>
    /// G9 on C++: known-broken at head (measured: <c>no matching function for call to
    /// 'construct_at'</c>, a pre-existing compile failure unrelated to interface forwarding)
    /// through head; right from the cpp sub-step on. Asserted here as a plain pass, since this
    /// file is committed at msil (after cpp), where it already holds.
    /// </summary>
    [Test]
    public void AnInheritedBaseMemberFillsAnExplicitEmptyGetInterfaceProperty_RunsOnCpp() // G9
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(DirectBaseImplementationExplicitEmptyGet))),
            Is.EqualTo("g9"));

    [Test]
    public void AnInheritedBaseMemberFillsAnExplicitEmptyGetInterfaceProperty_RunsOnCppAggressive() // G9
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(DirectBaseImplementationExplicitEmptyGet))),
            Is.EqualTo("g9"));

    /// <summary>
    /// G9 on MSIL: measured <c>TypeLoadException: Method 'get_Slot' in type 'Holder' from
    /// assembly 'Combined' ... does not have an implementation</c> at every sub-step BEFORE msil
    /// (head, cpp) — <c>BaseHolder.get_Slot</c> is not <c>virtual</c>, so <c>Holder</c>, which is
    /// the class that lists <c>Implements IHolder</c>, fails to load. Right only once the msil
    /// sub-step's forwarder stub (marked <c>newslot virtual final</c>) is emitted in
    /// <c>Holder</c> itself.
    /// </summary>
    [Test]
    public void AnInheritedBaseMemberFillsAnExplicitEmptyGetInterfaceProperty_RunsOnMsil() // G9
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(DirectBaseImplementationExplicitEmptyGet)), Is.EqualTo("g9"));

    [Test]
    public void AnInheritedBaseMemberFillsAnExplicitEmptyGetInterfaceProperty_RunsOnMsilAggressive() // G9
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(DirectBaseImplementationExplicitEmptyGet)), Is.EqualTo("g9"));

    // G10 — the base that declares Slot is TWO levels up (Holder : MiddleHolder : BaseHolder),
    // with MiddleHolder declaring nothing at all. Kills a "walk only the direct base" narrowing
    // of InterfaceImplementationLookup.FindInheritedProperty.
    private const string TwoLevelsUpBaseImplementation =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class BaseHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Class MiddleHolder\n" +
        "    Inherits BaseHolder\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits MiddleHolder\n" +
        "    Implements IHolder\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"g10\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void ABaseDeclaredTwoLevelsUpFillsAnInheritedInterfaceProperty_RunsOnCpp() // G10
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(TwoLevelsUpBaseImplementation))),
            Is.EqualTo("g10"));

    [Test]
    public void ABaseDeclaredTwoLevelsUpFillsAnInheritedInterfaceProperty_RunsOnCppAggressive() // G10
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(TwoLevelsUpBaseImplementation))),
            Is.EqualTo("g10"));

    [Test]
    public void ABaseDeclaredTwoLevelsUpFillsAnInheritedInterfaceProperty_RunsOnMsil() // G10
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(TwoLevelsUpBaseImplementation)), Is.EqualTo("g10"));

    [Test]
    public void ABaseDeclaredTwoLevelsUpFillsAnInheritedInterfaceProperty_RunsOnMsilAggressive() // G10
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(TwoLevelsUpBaseImplementation)), Is.EqualTo("g10"));

    // G11 — the BASE class ALSO implements IHolder itself (with its own accessors), and the
    // derived class re-lists Implements IHolder too, redundantly. Both a base-typed and a
    // derived-typed variable must read/write the same backing member.
    private const string BaseAlsoImplementsSameInterface =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class BaseHolder\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits BaseHolder\n" +
        "    Implements IHolder\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"g11\"\n" +
        "        Dim b As BaseHolder = h\n" +
        "        Console.WriteLine(h.Slot & \"|\" & b.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void ABaseThatAlsoImplementsTheSameInterface_RunsOnCpp() // G11
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(BaseAlsoImplementsSameInterface))),
            Is.EqualTo("g11|g11"));

    [Test]
    public void ABaseThatAlsoImplementsTheSameInterface_RunsOnCppAggressive() // G11
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(BaseAlsoImplementsSameInterface))),
            Is.EqualTo("g11|g11"));

    [Test]
    public void ABaseThatAlsoImplementsTheSameInterface_RunsOnMsil() // G11
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(BaseAlsoImplementsSameInterface)), Is.EqualTo("g11|g11"));

    [Test]
    public void ABaseThatAlsoImplementsTheSameInterface_RunsOnMsilAggressive() // G11
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(BaseAlsoImplementsSameInterface)), Is.EqualTo("g11|g11"));

    // G12 — a ReadOnly Integer interface property (no explicit Get block — a BARE ReadOnly
    // declaration), filled by a direct base's read/write Integer property. Unlike G9, this
    // shape has no explicit accessor block, so it needed nothing from either the C++ signature
    // fix or the MSIL newslot fix beyond what G5/G10/G11 already needed — it is a no-regression
    // guard, like them, not a moved shape.
    private const string DirectBaseImplementationReadOnlyBareInteger =
        "Interface IHolder\n" +
        "    ReadOnly Property Count As Integer\n" +
        "End Interface\n\n" +
        "Class BaseHolder\n" +
        "    Public Property Count As Integer\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits BaseHolder\n" +
        "    Implements IHolder\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Count = 20\n" +
        "        Console.WriteLine(h.Count + 22)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void ADirectBaseMemberFillsABareReadOnlyIntegerInterfaceProperty_RunsOnCpp() // G12
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(DirectBaseImplementationReadOnlyBareInteger))),
            Is.EqualTo("42"));

    [Test]
    public void ADirectBaseMemberFillsABareReadOnlyIntegerInterfaceProperty_RunsOnCppAggressive() // G12
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(DirectBaseImplementationReadOnlyBareInteger))),
            Is.EqualTo("42"));

    [Test]
    public void ADirectBaseMemberFillsABareReadOnlyIntegerInterfaceProperty_RunsOnMsil() // G12
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(DirectBaseImplementationReadOnlyBareInteger)), Is.EqualTo("42"));

    [Test]
    public void ADirectBaseMemberFillsABareReadOnlyIntegerInterfaceProperty_RunsOnMsilAggressive() // G12
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(DirectBaseImplementationReadOnlyBareInteger)), Is.EqualTo("42"));
}
