using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The C#/JavaScript legs of the inherited-implementation shapes in
/// <see cref="InheritedInterfacePropertyForwardingCppMsilTests"/> (G5, G9, G10, G11, G12), plus
/// the two shapes ADR-0002/ADR-0005 D1 leave BasicLang unable to diagnose as invalid VB (G3, N2,
/// tracked as task #132) and the MSIL <c>newslot</c>-per-accessor no-regression guard N1.
///
/// <para><b>Why C#/JS are split from C++/MSIL for the SAME five programs.</b> C# refuses every
/// one of them until the ADR-0002 <c>HasGetter</c>/<c>HasSetter</c> flag fix lands — a bare
/// <c>Property Slot As String</c> interface property with both flags false compiles to a C#
/// property declaration with NEITHER accessor (<c>CS0548: property or indexer must have at
/// least one accessor</c>) — which is exactly the flags sub-step's own patch
/// (<c>IRBuilder.cs</c>/<c>IRNodes.cs</c>). JavaScript already runs every one of these shapes at
/// every sub-step (measured: <c>matrix-head.txt</c> through <c>matrix-flags.txt</c>, no JS cell
/// ever reads other than <c>RAN OK</c> for G5/G9/G10/G11/G12) — it is asserted here anyway,
/// alongside C#, rather than moved into the msil-committed sibling file, because
/// <c>FourBackends</c>'s own convention is one program's C# and JS legs travel together (see
/// <see cref="InterfaceAccessorBatchTests"/>), and because this file's FIRST fully-green
/// sub-step is flags regardless of JS's earlier readiness.</para>
///
/// <para><b>N1, G3, N2 also gate on flags</b> — measured in <c>matrix-flags.txt</c> only:</para>
/// <list type="bullet">
/// <item>N1's C# leg is <c>COMPILE-FAIL</c> (CS0548, the same flag defect) at every sub-step
/// before flags; its MSIL leg is <c>RAN OK</c> at EVERY sub-step including head — a pure
/// no-regression guard for the msil sub-step's per-accessor <c>newslot</c> decision, which a
/// naive "always newslot" or "never newslot" rule would each get wrong for a DIFFERENT reason
/// than what N1 exercises: <c>D.Slot</c> here is a plain (non-<c>Overrides</c>) property that
/// also fills <c>IHolder.Slot</c>, shadowing <c>B</c>'s <c>Overridable</c> one — <c>D.Slot</c>
/// must take a NEW vtable slot (<c>newslot</c>) so <c>b.Slot</c> (through the base-typed
/// variable) keeps reading <c>B</c>'s own, unrelated backing field, not <c>D</c>'s.</item>
/// <item>G3 and N2 are programs BasicLang accepts but C# rejects (<c>CS0535</c>/<c>CS0506</c>
/// respectively) — invalid VB.NET that BasicLang's front end does not yet diagnose (task #132,
/// out of scope for this batch). Their MSIL legs are <c>RAN OK</c> at head/cpp/msil/types and
/// only become the measured <c>TypeLoadException</c> once the flags patch's own accessor-flag
/// change takes effect — before flags, the class's own property (not an interface accessor at
/// all, since the flags were false) happened to run correctly by accident. These are PINNED, not
/// fixed: they assert the measured failure text, not the <c>.exp</c>-file "if it worked" value,
/// and assert nothing about C++ or JavaScript (both already run these programs, per
/// <c>matrix-flags.txt</c> — legal-looking programs that merely expose the diagnostic gap, not
/// backend defects).</item>
/// </list>
///
/// <para>Marked Integration: spawns Node and <c>ilasm</c>/the CLR; the C# leg is in-process
/// Roslyn and redirects <c>Console.Out</c> (<see cref="FourBackends.RunEmittedCSharpText"/>).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // in-process Roslyn legs redirect Console.Out
public class InheritedInterfacePropertyForwardingFlagsTests
{
    // ====================================================================================
    // C#/JS legs of G5, G9, G10, G11, G12 — same programs as
    // InheritedInterfacePropertyForwardingCppMsilTests; see that file for the C++/MSIL legs.
    // ====================================================================================

    private const string DirectBaseImplementation = // G5
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
    public void ADirectBaseClassMemberFillsAnInheritedInterfaceProperty_RunsOnCSharp() // G5
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(DirectBaseImplementation)), Is.EqualTo("g5"));

    [Test]
    public void ADirectBaseClassMemberFillsAnInheritedInterfaceProperty_RunsOnJavaScript() // G5
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(DirectBaseImplementation)), Is.EqualTo("g5"));

    private const string DirectBaseImplementationExplicitEmptyGet = // G9
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

    [Test]
    public void AnInheritedBaseMemberFillsAnExplicitEmptyGetInterfaceProperty_RunsOnCSharp() // G9
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(DirectBaseImplementationExplicitEmptyGet)), Is.EqualTo("g9"));

    [Test]
    public void AnInheritedBaseMemberFillsAnExplicitEmptyGetInterfaceProperty_RunsOnJavaScript() // G9
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(DirectBaseImplementationExplicitEmptyGet)), Is.EqualTo("g9"));

    private const string TwoLevelsUpBaseImplementation = // G10
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
    public void ABaseDeclaredTwoLevelsUpFillsAnInheritedInterfaceProperty_RunsOnCSharp() // G10
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(TwoLevelsUpBaseImplementation)), Is.EqualTo("g10"));

    [Test]
    public void ABaseDeclaredTwoLevelsUpFillsAnInheritedInterfaceProperty_RunsOnJavaScript() // G10
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(TwoLevelsUpBaseImplementation)), Is.EqualTo("g10"));

    private const string BaseAlsoImplementsSameInterface = // G11
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
    public void ABaseThatAlsoImplementsTheSameInterface_RunsOnCSharp() // G11
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(BaseAlsoImplementsSameInterface)), Is.EqualTo("g11|g11"));

    [Test]
    public void ABaseThatAlsoImplementsTheSameInterface_RunsOnJavaScript() // G11
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(BaseAlsoImplementsSameInterface)), Is.EqualTo("g11|g11"));

    private const string DirectBaseImplementationReadOnlyBareInteger = // G12
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
    public void ADirectBaseMemberFillsABareReadOnlyIntegerInterfaceProperty_RunsOnCSharp() // G12
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(DirectBaseImplementationReadOnlyBareInteger)), Is.EqualTo("42"));

    [Test]
    public void ADirectBaseMemberFillsABareReadOnlyIntegerInterfaceProperty_RunsOnJavaScript() // G12
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(DirectBaseImplementationReadOnlyBareInteger)), Is.EqualTo("42"));

    // ====================================================================================
    // N1 — kills "MSIL never marks a shadowing interface-implementing accessor newslot" (and
    // its mirror, "always newslot" would not distinguish this from an ordinary override). B
    // declares Slot Overridable; D shadows it (no Overrides) while also implementing IHolder.
    // Correct: b.Slot (through the BASE-typed variable) must keep reading B's own backing
    // field, untouched by D — "d|" not "d|d".
    // ====================================================================================

    private const string ShadowingImplementationOverAnOverridableBase =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class B\n" +
        "    Public Overridable Property Slot As String\n" +
        "End Class\n\n" +
        "Class D\n" +
        "    Inherits B\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim d As D = New D()\n" +
        "        d.Slot = \"d\"\n" +
        "        Dim b As B = d\n" +
        "        Console.WriteLine(d.Slot & \"|\" & b.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AShadowingInterfaceImplementation_RunsOnCSharp() // N1
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(ShadowingImplementationOverAnOverridableBase)), Is.EqualTo("d|"));

    /// <summary>
    /// N1 on MSIL — the shape this fixture exists to guard: without <c>newslot</c> on
    /// <c>D.Slot</c>'s implementing accessor, it takes <c>B.Slot</c>'s vtable slot instead of a
    /// new one, and <c>b.Slot</c> answers <c>"d"</c> too (<c>"d|d"</c>). Measured <c>RAN OK</c>
    /// with the right answer at EVERY sub-step already (head through flags) — this is a
    /// no-regression guard, not a shape this batch fixes.
    /// </summary>
    [Test]
    public void AShadowingInterfaceImplementation_RunsOnMsil() // N1
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(ShadowingImplementationOverAnOverridableBase)), Is.EqualTo("d|"));

    // ====================================================================================
    // G3, N2 — invalid VB.NET that BasicLang's front end does not diagnose (task #132). Pinned
    // on MSIL as the measured TypeLoadException at flags; NOT asserted to run anywhere. C#'s
    // rejection (which VB.NET/C# both agree is correct) is pinned too, by error code.
    // ====================================================================================

    /// <summary>
    /// G3 — <c>BaseHolder</c> is <c>MustInherit</c>, lists <c>Implements IHolder</c>, and
    /// declares no <c>Slot</c> member at all (not even abstract); only the CONCRETE subclass
    /// <c>Holder</c> declares one. C#/VB.NET both require the class that lists an interface (or
    /// an ancestor of it, if abstract) to account for every member — a member on a DESCENDANT
    /// does not satisfy it. BasicLang currently accepts this program (no analyzer diagnostic);
    /// task #132 is teaching the front end to reject it as C# does, at which point this pin
    /// becomes a compile-error assertion of its own.
    /// </summary>
    private const string InterfaceImplementedByAbstractBaseSatisfiedOnlyByConcreteSubclass =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "MustInherit Class BaseHolder\n" +
        "    Implements IHolder\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits BaseHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"g3\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AnInterfaceSatisfiedOnlyByADescendantOfTheImplementingClass_FailsToCompileOnCSharp_CS0535() // G3
        => Assert.That(
            ReturnCoercionTests.CompileEmittedCSharpForTest(InterfaceImplementedByAbstractBaseSatisfiedOnlyByConcreteSubclass),
            Has.Some.Contains("CS0535"));

    /// <summary>
    /// Pinned, not fixed: task #132. MSIL loads <c>BaseHolder</c> with <c>get_Slot</c> declared
    /// (to implement <c>IHolder</c>) but not defined on that type, and the CLR refuses to load
    /// it — measured verbatim: <c>System.TypeLoadException: Method 'get_Slot' in type
    /// 'BaseHolder' from assembly 'Combined' ... does not have an implementation.</c>
    /// </summary>
    [Test]
    public void AnInterfaceSatisfiedOnlyByADescendantOfTheImplementingClass_ThrowsTypeLoadOnMsil() // G3
    {
        var run = MsilHarness.Run(InterfaceImplementedByAbstractBaseSatisfiedOnlyByConcreteSubclass);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed));
        Assert.That(run.Output, Does.Contain("TypeLoadException"));
        Assert.That(run.Output, Does.Contain("get_Slot"));
        Assert.That(run.Output, Does.Contain("BaseHolder"));
    }

    /// <summary>
    /// N2 — <c>D</c> implements <c>IHolder</c> with a plain (non-<c>Overridable</c>) <c>Slot</c>;
    /// <c>E</c> inherits <c>D</c> and declares <c>Public Overrides Property Slot</c>. VB.NET (and
    /// the C# this lowers to) both require the member being overridden to be
    /// <c>Overridable</c>/<c>virtual</c> — <c>D.Slot</c> is not, because nothing marked it so; it
    /// only implicitly fills an interface slot. BasicLang currently accepts this program; task
    /// #132 covers rejecting it, same as G3.
    /// </summary>
    private const string OverridesOfANonVirtualInterfaceImplementingProperty =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class D\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Class E\n" +
        "    Inherits D\n" +
        "    Public Overrides Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim e As E = New E()\n" +
        "        e.Slot = \"e\"\n" +
        "        Console.WriteLine(e.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void OverridingANonVirtualInterfaceImplementingProperty_FailsToCompileOnCSharp_CS0506() // N2
        => Assert.That(
            ReturnCoercionTests.CompileEmittedCSharpForTest(OverridesOfANonVirtualInterfaceImplementingProperty),
            Has.Some.Contains("CS0506"));

    /// <summary>
    /// Pinned, not fixed: task #132. MSIL emits <c>E.Slot</c>'s accessors <c>final</c> over
    /// <c>D.Slot</c>'s NON-virtual ones (a <c>.override</c>/final method implementation with no
    /// virtual base slot to bind to), and the CLR refuses to load it — measured verbatim:
    /// <c>System.TypeLoadException: Declaration referenced in a method implementation cannot be
    /// a final method.  Type: 'E'.</c>
    /// </summary>
    [Test]
    public void OverridingANonVirtualInterfaceImplementingProperty_ThrowsTypeLoadOnMsil() // N2
    {
        var run = MsilHarness.Run(OverridesOfANonVirtualInterfaceImplementingProperty);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed));
        Assert.That(run.Output, Does.Contain("TypeLoadException"));
        Assert.That(run.Output, Does.Contain("final method"));
        Assert.That(run.Output, Does.Contain("'E'"));
    }
}
