using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0004 D1 — the interface-property accessor batch: the ADR-0002 <c>HasGetter</c>/
/// <c>HasSetter</c> flag fix (<c>IRBuilder.cs</c>), the C++ accessor-signature helper shared
/// between the interface declaration and the implementing class (<c>CppCodeGenerator.cs</c>,
/// <c>PropertyAccessorSignature</c>/<c>InterfaceAccessorOverride</c>), the MSIL
/// <c>newslot virtual final</c> marking decided per accessor (<c>MSILBackend.cs</c>), and the
/// shared <c>InterfaceImplementationLookup</c> both native-ish backends now ask.
///
/// <para><b>Scope, per the D1 contract.</b> Access through a CLASS-typed variable to a class
/// that implements an interface property must compile and run identically on C#, C++,
/// JavaScript and MSIL, under the standard pipeline, the aggressive pipeline, and (per
/// CLAUDE.md — a Release <c>.blproj</c> build takes the aggressive pipeline, same as
/// <c>--optimize</c>) a Release build. <see cref="FourBackends.RunsOnEveryBackend"/> and
/// <see cref="FourBackends.RunsOnEveryBackendAggressive"/> together cover exactly that: the two
/// pipelines a Release <c>.blproj</c> and the CLI can take. INTERFACE-typed access
/// (<c>Dim h As IHolder</c>) is explicitly OUT of this batch (own defect, own brief) and is
/// pinned known-failing here, not fixed.</para>
///
/// <para><b>Sub-step attribution</b> (measured against the family's own probe matrices,
/// <c>matrix-base/s1/s2/s3.txt</c> in the session scratchpad) — the three sub-steps this batch
/// will be committed as:</para>
/// <list type="bullet">
/// <item>step1 (C++ signature helper + <c>InterfaceImplementationLookup</c>): fixes C++ for
/// <see cref="AnInterfacePropertyWithExplicitEmptyAccessorBlocks_ReadOnly_RunsOnEveryBackend"/>
/// (P7) and <see cref="AnInterfacePropertyWithExplicitEmptyAccessorBlocks_ReadWrite_RunsOnEveryBackend"/>
/// (P11) — both already had <c>HasGetter</c>/<c>HasSetter</c> true before the flag fix, because
/// an explicit (empty) <c>Get</c>/<c>Set</c> block is not null, so these two shapes exercise the
/// C++ signature fix alone, with the flags untouched.</item>
/// <item>step2 (MSIL <c>newslot virtual final</c> per accessor): fixes MSIL for the same two
/// shapes, P10 and P11 (the ReadOnly-with-empty-Get and the ReadWrite-with-empty-accessors
/// cases) — same reasoning, the flags were already right for these two.</item>
/// <item>step3 (the IRBuilder flag fix): fixes EVERY bare <c>Property X As T</c> interface
/// property (no explicit accessor block) on C# outright — before step3 the flags were false for
/// every one of them, so C# refused the property declaration itself (CS0548) and every P-shape
/// but P7/P10/P11 failed to compile on C# at every earlier sub-step. Step3 also fixes P10 on
/// C++ (needs <see cref="InterfacePropertyType"/> below off the same flag-fix patch to know
/// <c>Count</c> is <c>Integer</c>, not the old class-kinded stand-in) and finishes P10/P11 on
/// MSIL together with step2's marking.</item>
/// </list>
///
/// <para>Marked Integration: every <see cref="FourBackends"/> call compiles and runs native C++
/// and spawns Node and <c>ilasm</c>/the CLR.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg (FourBackends) redirects Console.Out
public class InterfaceAccessorBatchTests
{
    // ====================================================================================
    // CONTRACT — class-typed access, all four backends, both pipelines.
    // ====================================================================================

    private const string PlainReadWrite =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"abc\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AnAutoImplementedReadWriteInterfaceProperty_RunsOnEveryBackend() // P1
        => FourBackends.RunsOnEveryBackend(PlainReadWrite, "abc");

    [Test]
    public void AnAutoImplementedReadWriteInterfaceProperty_RunsOnEveryBackendAggressive() // P1
        => FourBackends.RunsOnEveryBackendAggressive(PlainReadWrite, "abc");

    private const string AutoReadWriteIntegerProperty =
        "Interface IHolder\n" +
        "    Property Count As Integer\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Count As Integer\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Count = 41\n" +
        "        h.Count = h.Count + 1\n" +
        "        Console.WriteLine(h.Count)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>P5 — a plain read/write Integer-typed interface property (not String).</summary>
    [Test]
    public void AnAutoImplementedReadWriteIntegerInterfaceProperty_RunsOnEveryBackend() // P5
        => FourBackends.RunsOnEveryBackend(AutoReadWriteIntegerProperty, "42");

    [Test]
    public void AnAutoImplementedReadWriteIntegerInterfaceProperty_RunsOnEveryBackendAggressive() // P5
        => FourBackends.RunsOnEveryBackendAggressive(AutoReadWriteIntegerProperty, "42");

    private const string ReadOnlyIfaceReadWriteImpl =
        "Interface IHolder\n" +
        "    ReadOnly Property Count As Integer\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Count As Integer\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Count = 6\n" +
        "        Console.WriteLine(h.Count * 7)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>
    /// P9 — the interface declares ReadOnly but the implementing class is read/write, which is
    /// legal (a narrower interface contract). Also exercises <c>InterfacePropertyType</c>
    /// resolving <c>Integer</c> as a primitive, not the old class-kinded stand-in that made the
    /// C++ interface declaration pass by <c>const&amp;</c> against the class's by-value getter.
    /// </summary>
    [Test]
    public void AReadOnlyInterfaceProperty_WithAReadWriteImplementation_RunsOnEveryBackend() // P9
        => FourBackends.RunsOnEveryBackend(ReadOnlyIfaceReadWriteImpl, "42");

    [Test]
    public void AReadOnlyInterfaceProperty_WithAReadWriteImplementation_RunsOnEveryBackendAggressive() // P9
        => FourBackends.RunsOnEveryBackendAggressive(ReadOnlyIfaceReadWriteImpl, "42");

    private const string WriteOnlyIface =
        "Interface IHolder\n" +
        "    WriteOnly Property Slot As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"wa\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>P13 — WriteOnly interface, read/write implementation (reads the WriteOnly slot back).</summary>
    [Test]
    public void AWriteOnlyInterfaceProperty_WithAReadWriteImplementation_RunsOnEveryBackend() // P13
        => FourBackends.RunsOnEveryBackend(WriteOnlyIface, "wa");

    [Test]
    public void AWriteOnlyInterfaceProperty_WithAReadWriteImplementation_RunsOnEveryBackendAggressive() // P13
        => FourBackends.RunsOnEveryBackendAggressive(WriteOnlyIface, "wa");

    private const string ReadOnlyExplicitEmptyGet =
        "Interface IHolder\n" +
        "    ReadOnly Property Count As Integer\n" +
        "        Get\n" +
        "        End Get\n" +
        "    End Property\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Count As Integer\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Count = 6\n" +
        "        Console.WriteLine(h.Count * 7)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>
    /// P10 — an EXPLICIT empty <c>Get</c> block on a ReadOnly interface property. This shape's
    /// flags were already right before the ADR-0002 flag fix (an explicit body, even an empty
    /// one, is not null) — see the class doc's sub-step attribution: this is the C++/MSIL
    /// signature-and-virtual fix alone, not the flag fix.
    /// </summary>
    [Test]
    public void AnInterfacePropertyWithExplicitEmptyAccessorBlocks_ReadOnly_RunsOnEveryBackend() // P10
        => FourBackends.RunsOnEveryBackend(ReadOnlyExplicitEmptyGet, "42");

    [Test]
    public void AnInterfacePropertyWithExplicitEmptyAccessorBlocks_ReadOnly_RunsOnEveryBackendAggressive() // P10
        => FourBackends.RunsOnEveryBackendAggressive(ReadOnlyExplicitEmptyGet, "42");

    private const string ReadWriteExplicitEmptyAccessors =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "        Get\n" +
        "        End Get\n" +
        "        Set(value As String)\n" +
        "        End Set\n" +
        "    End Property\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"es\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>P11 — explicit empty Get AND Set blocks. Same pre-flag-fix shape as P10, for both accessors.</summary>
    [Test]
    public void AnInterfacePropertyWithExplicitEmptyAccessorBlocks_ReadWrite_RunsOnEveryBackend() // P11
        => FourBackends.RunsOnEveryBackend(ReadWriteExplicitEmptyAccessors, "es");

    [Test]
    public void AnInterfacePropertyWithExplicitEmptyAccessorBlocks_ReadWrite_RunsOnEveryBackendAggressive() // P11
        => FourBackends.RunsOnEveryBackendAggressive(ReadWriteExplicitEmptyAccessors, "es");

    private const string UserClassTypedProperty =
        "Class Node\n" +
        "    Public V As Integer\n" +
        "End Class\n\n" +
        "Interface IHolder\n" +
        "    Property Other As Node\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Other As Node\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        Dim n As Node = New Node()\n" +
        "        n.V = 9\n" +
        "        h.Other = n\n" +
        "        Console.WriteLine(h.Other.V)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>P12 — the interface property's type is itself a user-defined class, not a primitive.</summary>
    [Test]
    public void AnInterfacePropertyTypedAsAUserClass_RunsOnEveryBackend() // P12
        => FourBackends.RunsOnEveryBackend(UserClassTypedProperty, "9");

    [Test]
    public void AnInterfacePropertyTypedAsAUserClass_RunsOnEveryBackendAggressive() // P12
        => FourBackends.RunsOnEveryBackendAggressive(UserClassTypedProperty, "9");

    private const string ImplementedViaBaseClass =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class BaseHolder\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Class Holder\n" +
        "    Inherits BaseHolder\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"in\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>
    /// P15 — the interface is implemented by a BASE class; the derived class (accessed here)
    /// never says <c>Implements</c> itself. Pins that <c>InterfaceImplementationLookup</c> walks
    /// the base-class chain, not just the accessed class's own <c>Implements</c> list.
    /// </summary>
    [Test]
    public void AnInterfacePropertyImplementedThroughABaseClass_RunsOnEveryBackend() // P15
        => FourBackends.RunsOnEveryBackend(ImplementedViaBaseClass, "in");

    [Test]
    public void AnInterfacePropertyImplementedThroughABaseClass_RunsOnEveryBackendAggressive() // P15
        => FourBackends.RunsOnEveryBackendAggressive(ImplementedViaBaseClass, "in");

    // ------------------------------------------------------------- Q3/Q4/Q5: non-string/int types

    private const string DoubleProperty =
        "Interface IHolder\n" +
        "    Property D As Double\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property D As Double\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.D = 1.25\n" +
        "        h.D = h.D * 2.0\n" +
        "        Console.WriteLine(h.D)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void ADoubleTypedInterfaceProperty_RunsOnEveryBackend() // Q3
        => FourBackends.RunsOnEveryBackend(DoubleProperty, "2.5");

    [Test]
    public void ADoubleTypedInterfaceProperty_RunsOnEveryBackendAggressive() // Q3
        => FourBackends.RunsOnEveryBackendAggressive(DoubleProperty, "2.5");

    private const string BooleanProperty =
        "Interface IHolder\n" +
        "    Property B As Boolean\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property B As Boolean\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.B = True\n" +
        "        If h.B Then\n" +
        "            Console.WriteLine(\"yes\")\n" +
        "        Else\n" +
        "            Console.WriteLine(\"no\")\n" +
        "        End If\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void ABooleanTypedInterfaceProperty_RunsOnEveryBackend() // Q4
        => FourBackends.RunsOnEveryBackend(BooleanProperty, "yes");

    [Test]
    public void ABooleanTypedInterfaceProperty_RunsOnEveryBackendAggressive() // Q4
        => FourBackends.RunsOnEveryBackendAggressive(BooleanProperty, "yes");

    private const string StringConcatProperty =
        "Interface IHolder\n" +
        "    Property Name As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Name As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Name = \"ab\"\n" +
        "        h.Name = h.Name & \"cd\"\n" +
        "        Console.WriteLine(h.Name)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AStringTypedInterfaceProperty_RunsOnEveryBackend() // Q5
        => FourBackends.RunsOnEveryBackend(StringConcatProperty, "abcd");

    [Test]
    public void AStringTypedInterfaceProperty_RunsOnEveryBackendAggressive() // Q5
        => FourBackends.RunsOnEveryBackendAggressive(StringConcatProperty, "abcd");

    // ====================================================================================
    // Explicit Get/Set implementation (P3/P4/P8) — runs on all four. C++ was a known gap until
    // property reads and writes were routed through get_/set_ (task #148).
    // ====================================================================================

    private const string ExplicitGetSetImpl = // P4
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Private _v As String\n" +
        "    Public Property Slot As String\n" +
        "        Get\n" +
        "            Return _v\n" +
        "        End Get\n" +
        "        Set(value As String)\n" +
        "            _v = value\n" +
        "        End Set\n" +
        "    End Property\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"gs\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AnExplicitGetSetImplementation_RunsOnCSharp() // P4
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(ExplicitGetSetImpl)), Is.EqualTo("gs"));

    [Test]
    public void AnExplicitGetSetImplementation_RunsOnJavaScript() // P4
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(ExplicitGetSetImpl)), Is.EqualTo("gs"));

    [Test]
    public void AnExplicitGetSetImplementation_RunsOnMsil() // P4
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(ExplicitGetSetImpl)), Is.EqualTo("gs"));

    /// <summary>
    /// P4 on C++. Pinned known-failing until task #148: a read or write of a property with
    /// EXPLICIT Get/Set bodies lowered to a field access, and the class has no member of that
    /// name (measured: "no member named 'Slot' in 'Holder'"). It now calls get_Slot/set_Slot.
    /// </summary>
    [Test]
    public void AnExplicitGetSetImplementation_RunsOnCpp() // P4
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ExplicitGetSetImpl))),
            Is.EqualTo("gs"));

    private const string WriteOnlyExplicitSetImpl = // P3
        "Interface IHolder\n" +
        "    WriteOnly Property Slot As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Private _v As String\n" +
        "    Public WriteOnly Property Slot As String\n" +
        "        Set(value As String)\n" +
        "            _v = value\n" +
        "        End Set\n" +
        "    End Property\n" +
        "    Public Function Peek() As String\n" +
        "        Return _v\n" +
        "    End Function\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        h.Slot = \"wo\"\n" +
        "        Console.WriteLine(h.Peek())\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AWriteOnlyExplicitSetImplementation_RunsOnCSharp() // P3
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(WriteOnlyExplicitSetImpl)), Is.EqualTo("wo"));

    [Test]
    public void AWriteOnlyExplicitSetImplementation_RunsOnJavaScript() // P3
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(WriteOnlyExplicitSetImpl)), Is.EqualTo("wo"));

    [Test]
    public void AWriteOnlyExplicitSetImplementation_RunsOnMsil() // P3
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(WriteOnlyExplicitSetImpl)), Is.EqualTo("wo"));

    [Test]
    public void AWriteOnlyExplicitSetImplementation_RunsOnCpp() // P3
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(WriteOnlyExplicitSetImpl))),
            Is.EqualTo("wo"));

    private const string ReadOnlyExplicitGetImpl = // P8
        "Interface IHolder\n" +
        "    ReadOnly Property Slot As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public ReadOnly Property Slot As String\n" +
        "        Get\n" +
        "            Return \"rg\"\n" +
        "        End Get\n" +
        "    End Property\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AReadOnlyExplicitGetImplementation_RunsOnCSharp() // P8
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(ReadOnlyExplicitGetImpl)), Is.EqualTo("rg"));

    [Test]
    public void AReadOnlyExplicitGetImplementation_RunsOnJavaScript() // P8
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(ReadOnlyExplicitGetImpl)), Is.EqualTo("rg"));

    [Test]
    public void AReadOnlyExplicitGetImplementation_RunsOnMsil() // P8
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(ReadOnlyExplicitGetImpl)), Is.EqualTo("rg"));

    [Test]
    public void AReadOnlyExplicitGetImplementation_RunsOnCpp() // P8
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ReadOnlyExplicitGetImpl))),
            Is.EqualTo("rg"));

    // ====================================================================================
    // Structure-typed interface property (Q1) — C#/C++ OK; MSIL known-wrong; JS BL7005.
    // ====================================================================================

    private const string StructureProperty =
        "Structure Pt\n" +
        "    Public X As Integer\n" +
        "    Public Y As Integer\n" +
        "End Structure\n\n" +
        "Interface IHolder\n" +
        "    Property P As Pt\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property P As Pt\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        Dim v As Pt\n" +
        "        v.X = 3\n" +
        "        v.Y = 4\n" +
        "        h.P = v\n" +
        "        Console.WriteLine(h.P.X + h.P.Y)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AStructureTypedInterfaceProperty_RunsOnCSharp() // Q1
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(StructureProperty)), Is.EqualTo("7"));

    [Test]
    public void AStructureTypedInterfaceProperty_RunsOnCpp() // Q1
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(StructureProperty))),
            Is.EqualTo("7"));

    /// <summary>
    /// Q1 pinned known-wrong on MSIL: a <c>Structure</c>-typed interface property is treated as
    /// a class (heap reference), and the generated program throws
    /// <c>NullReferenceException</c> rather than printing 7. This REPRODUCES WITHOUT AN
    /// INTERFACE — it is a pre-existing MSIL Structure defect the interface batch exposes, not
    /// one it introduces, and it is out of the D1 batch's scope.
    /// </summary>
    [Test]
    public void AStructureTypedInterfaceProperty_ThrowsNullReferenceOnMsil() // Q1
    {
        var run = MsilHarness.Run(StructureProperty);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed));
        Assert.That(run.Output, Does.Contain("NullReferenceException"));
    }

    /// <summary>Q1 pinned known-failing on JS: BL7005, a Structure has no JS lowering (value semantics).</summary>
    [Test]
    public void AStructureTypedInterfaceProperty_IsRejectedOnJavaScript_BL7005() // Q1
    {
        var module = JsTestSupport.BuildModule(StructureProperty);
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7005"));
    }

    // ====================================================================================
    // P6 — interface-TYPED access (Dim h As IHolder). C#, JS and C++ pass; MSIL is pinned
    // known-failing.
    // ====================================================================================

    private const string InterfaceTypedAccess =
        "Interface IHolder\n" +
        "    Property Slot As String\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property Slot As String\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As IHolder = New Holder()\n" +
        "        h.Slot = \"if\"\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AccessThroughAnInterfaceTypedVariable_RunsOnCSharp()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(InterfaceTypedAccess)), Is.EqualTo("if"));

    [Test]
    public void AccessThroughAnInterfaceTypedVariable_RunsOnJavaScript()
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(InterfaceTypedAccess)), Is.EqualTo("if"));

    /// <summary>
    /// Pinned known-failing until task #148: the read and write lowered to FIELD access through
    /// the interface pointer, and an interface declares accessors, never storage (measured:
    /// "no member named 'Slot' in 'IHolder'"). Both now go through the pure-virtual accessors.
    /// </summary>
    [Test]
    public void AccessThroughAnInterfaceTypedVariable_RunsOnCpp()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(InterfaceTypedAccess))),
            Is.EqualTo("if"));

    /// <summary>
    /// Known-failing on MSIL: the interface's property TYPE is still built as the old
    /// class-kinded stand-in for field access purposes here (<c>ldfld</c> against a slot the
    /// interface metadata never declares as a field) — measured: <c>MissingFieldException:
    /// Field not found: 'IHolder.Slot'</c>.
    /// </summary>
    [Test]
    public void AccessThroughAnInterfaceTypedVariable_ThrowsMissingFieldOnMsil()
    {
        var run = MsilHarness.Run(InterfaceTypedAccess);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed));
        Assert.That(run.Output, Does.Contain("MissingFieldException"));
    }

    // ====================================================================================
    // The MSIL ReadOnly-auto-property-assigned-in-constructor defect (control C1 — NO
    // interface involved at all). Pinned per the task: a pre-existing MSIL defect this batch
    // does not fix, distinct from the interface-flag work.
    // ====================================================================================

    private const string ReadOnlyAutoPropertyAssignedInCtor =
        "Class Holder\n" +
        "    Public ReadOnly Property Slot As String\n" +
        "    Public Sub New()\n" +
        "        Slot = \"ro\"\n" +
        "    End Sub\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "        Dim h As Holder = New Holder()\n" +
        "        Console.WriteLine(h.Slot)\n" +
        "    End Sub\n" +
        "End Module";

    [Test]
    public void AReadOnlyAutoPropertyAssignedInTheConstructor_RunsOnCSharp() // C1
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(ReadOnlyAutoPropertyAssignedInCtor)), Is.EqualTo("ro"));

    [Test]
    public void AReadOnlyAutoPropertyAssignedInTheConstructor_RunsOnCpp() // C1
        => Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ReadOnlyAutoPropertyAssignedInCtor))),
            Is.EqualTo("ro"));

    [Test]
    public void AReadOnlyAutoPropertyAssignedInTheConstructor_RunsOnJavaScript() // C1
        => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(ReadOnlyAutoPropertyAssignedInCtor)), Is.EqualTo("ro"));

    /// <summary>
    /// Control: NO interface anywhere in this program. Pins that the MSIL
    /// <c>MissingMethodException: Method not found: 'Void Holder.set_Slot(...)'</c> seen on
    /// several interface-property shapes is a PRE-EXISTING, more general MSIL defect (a ReadOnly
    /// auto-property assigned from inside the constructor emits no callable setter at all, on
    /// MSIL, whether or not an interface is involved) — not something the ADR-0004 D1 batch
    /// introduced or is scoped to fix.
    /// </summary>
    [Test]
    public void AReadOnlyAutoPropertyAssignedInTheConstructor_ThrowsMissingMethodOnMsil() // C1
    {
        var run = MsilHarness.Run(ReadOnlyAutoPropertyAssignedInCtor);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed));
        Assert.That(run.Output, Does.Contain("MissingMethodException"));
        Assert.That(run.Output, Does.Contain("set_Slot"));
    }
}
