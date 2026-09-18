using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Properties on the MSIL backend, which did not work in ANY shape.
///
/// <para>⛔ Measured before, compile → ilasm → run:</para>
///
/// <list type="bullet">
/// <item>auto (<c>Public Property Alpha As Integer</c>) — ilasm REFUSED the file:
/// "Invalid Set method of property 'Alpha'".</item>
/// <item>explicit (<c>Get</c>/<c>Set</c> bodies) — assembled, then
/// <c>MissingFieldException: Field not found: 'Box.N'</c>.</item>
/// <item><c>ReadOnly</c> with a computed getter — same MissingFieldException.</item>
/// <item>auto, touched from inside its own class — ilasm REFUSED.</item>
/// <item><c>Shared</c> auto — ilasm REFUSED.</item>
/// </list>
///
/// <para>Every other backend ran the same auto-property: JavaScript 7, C++ 7, and C# emits a real
/// <c>public int Alpha { get; set; }</c>.</para>
///
/// <para>⛔ THREE independent defects, each masking the next:</para>
///
/// <list type="number">
/// <item>The <c>.property</c> block was written unconditionally while each accessor METHOD was
/// gated on <c>prop.Getter != null</c>. An auto property carries neither accessor in the IR, so
/// the block named methods that were never emitted — and nothing declared storage for the value
/// either.</item>
/// <item>Every property ACCESS lowered to <c>ldfld</c>/<c>stfld</c> on the property's own name,
/// bypassing the accessors. Proved by deleting the <c>.property</c> block from the emitted IL by
/// hand: it then assembled and died with MissingFieldException. For a computed getter a field
/// read cannot be right whatever it names.</item>
/// <item>An explicit accessor's body never got a <c>.locals init</c> — <c>.maxstack</c> was
/// written BEFORE <c>InitializeMethodContext</c>, so the slot tables did not exist yet — and
/// <c>Set(value As Integer)</c> emitted <c>stloc.0</c> into a method with no locals directive:
/// <b>InvalidProgramException</b>.</item>
/// </list>
///
/// <para>⚠ TWO gaps here are PRE-EXISTING and deliberately not asserted, both verified on the
/// parent commit:</para>
///
/// <list type="bullet">
/// <item><b>Instance field initializers are never emitted.</b> <c>Public N As Integer = 5</c>
/// prints <b>0</b> on master with no property anywhere — the constructor only calls the base. Any
/// test here that seeded state with a field initializer would be pinning that instead, so the
/// computed-getter case seeds through a method.</item>
/// <item><b>Inherited members do not resolve.</b> <c>Derived.Tag</c> where <c>Tag</c> is on
/// <c>Base</c> types its temporary <c>object</c> and boxes as <c>System.Object</c> — on master
/// too, for a plain FIELD (<c>ldfld object 'Derived'::'Tag'</c>). The front end does not walk the
/// base chain for a member's type. The property variant fails the same way; this change neither
/// fixes nor worsens it.</item>
/// </list>
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilPropertyTests
{
    // ====================================================================================
    // The shapes, each measured broken in a different way.
    // ====================================================================================

    /// <summary>⛔ The headline: ilasm refused the whole file.</summary>
    [Test]
    public void AnAutoProperty_RoundTrips()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Property Alpha As Integer
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Alpha = 7
              PrintLine(CStr(c.Alpha))
             End Sub
            End Module
            """), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// ⛔ The explicit form. Its accessors WERE emitted, and the setter still could not run:
    /// <c>stloc.0</c> with no <c>.locals init</c> is an InvalidProgramException.
    /// </summary>
    [Test]
    public void AnExplicitProperty_RoundTrips()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Private _n As Integer
             Public Property N As Integer
              Get
               Return _n
              End Get
              Set(value As Integer)
               _n = value
              End Set
             End Property
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.N = 7
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// ⛔ The case that makes the access-side fix necessary rather than cosmetic, and the one that
    /// discriminates: the getter COMPUTES. A field read of <c>Doubled</c> cannot produce 10
    /// whatever storage it names, so this passes only when the access became
    /// <c>callvirt … get_Doubled()</c>.
    ///
    /// <para>⚠ Seeded through a method, NOT a field initializer — see the fixture note; a field
    /// initializer is never emitted and would make this read 0 for an unrelated reason.</para>
    /// </summary>
    [Test]
    public void AComputedGetter_IsCalled_NotReadAsAField()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Private _n As Integer
             Public Sub Seed(v As Integer)
              _n = v
             End Sub
             Public ReadOnly Property Doubled As Integer
              Get
               Return _n * 2
              End Get
             End Property
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Seed(5)
              PrintLine(CStr(c.Doubled))
             End Sub
            End Module
            """), Is.EqualTo("10\n"));
    }

    /// <summary>
    /// ⛔ A class reaching its OWN property by bare name. This emitted
    /// <c>// WARNING: Unknown local 'Alpha'</c> and pushed nothing, so the <c>add</c> ran an
    /// operand short and the result went into a temporary — the CLR rejected the method.
    /// </summary>
    [Test]
    public void AClassReachesItsOwnProperty_ByBareName()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Property Alpha As Integer
             Public Sub Bump()
              Alpha = Alpha + 1
             End Sub
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Alpha = 7
              c.Bump()
              PrintLine(CStr(c.Alpha))
             End Sub
            End Module
            """), Is.EqualTo("8\n"));
    }

    /// <summary>⚠ And through <c>Me</c>, which is the other spelling of the same thing.</summary>
    [Test]
    public void AClassReachesItsOwnProperty_ThroughMe()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Property Alpha As Integer
             Public Function Read() As Integer
              Return Me.Alpha
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Alpha = 7
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// ⚠ <c>Shared</c>, where the receiver is a TYPE NAME and there is nothing to push — the same
    /// split <c>TryResolveStaticField</c> makes for a Shared field, and the reason the accessor
    /// emission has a static arm rather than one shape for both.
    /// </summary>
    [Test]
    public void ASharedProperty_RoundTrips()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Shared Property Total As Integer
            End Class

            Module M
             Sub Main()
              Box.Total = 7
              PrintLine(CStr(Box.Total))
             End Sub
            End Module
            """), Is.EqualTo("7\n"));
    }

    /// <summary>⚠ <c>WriteOnly</c>, the half with no getter to declare.</summary>
    [Test]
    public void AWriteOnlyProperty_RoundTrips()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Private _n As Integer
             Public WriteOnly Property N As Integer
              Set(value As Integer)
               _n = value
              End Set
             End Property
             Public Function Peek() As Integer
              Return _n
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.N = 7
              PrintLine(CStr(c.Peek()))
             End Sub
            End Module
            """), Is.EqualTo("7\n"));
    }

    // ====================================================================================
    // The IL, for the properties a run cannot see.
    // ====================================================================================

    /// <summary>
    /// ⛔ The <c>.property</c> block and the methods must agree about what exists. Declaring a
    /// <c>.set</c> for a <c>ReadOnly</c> property is exactly the shape ilasm refused, so both
    /// halves are asserted: the accessor that exists is declared, and the one that does not is
    /// not.
    /// </summary>
    [Test]
    public void ThePropertyBlock_DeclaresOnlyAccessorsThatExist()
    {
        var il = CompileToIl("""
            Class Box
             Private _n As Integer
             Public ReadOnly Property Doubled As Integer
              Get
               Return _n * 2
              End Get
             End Property
            End Class

            Module M
             Sub Main()
              PrintLine("hi")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain(".get instance int32 'Box'::get_Doubled()"), il);
            Assert.That(il, Does.Not.Contain(".set"),
                "a ReadOnly property has no setter to name:\n" + il);
            Assert.That(il, Does.Not.Contain("set_Doubled"), il);
        });
    }

    /// <summary>
    /// ⛔ The shape that makes the block's two conditions have to be the ones that decide the
    /// METHODS, rather than a second opinion: a property with only a <c>Get</c> block and NO
    /// <c>ReadOnly</c> keyword. <c>IsReadOnly</c> is false, so asking IT whether to declare a
    /// <c>.set</c> answers yes and names a <c>set_N</c> that is never emitted — which is the
    /// original defect in its other form, and what ilasm refuses.
    ///
    /// <para>⚠ This fixture's <c>ReadOnly</c> case does NOT hold that: there both conditions
    /// agree, so the wrong one passes too. Found by mutation, not by reading.</para>
    /// </summary>
    [Test]
    public void APropertyWithOnlyAGetter_DeclaresOnlyAGet()
    {
        const string program = """
            Class Box
             Private _n As Integer
             Public Sub Seed(v As Integer)
              _n = v
             End Sub
             Public Property N As Integer
              Get
               Return _n
              End Get
             End Property
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Seed(7)
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """;

        var il = CompileToIl(program);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain(".get instance int32 'Box'::get_N()"), il);
            Assert.That(il, Does.Not.Contain("set_N"),
                "no setter was emitted, so none may be declared:\n" + il);
            Assert.That(RunExpectingSuccess(program), Is.EqualTo("7\n"));
        });
    }

    /// <summary>
    /// ⚠ An auto property's storage and its two trivial accessors. The backing field is spelled
    /// the way the C# and VB compilers spell it, so it reads as generated and cannot collide with
    /// a user field — the angle brackets are not identifier characters, which is what makes it
    /// safe and also why it has to be quoted.
    /// </summary>
    [Test]
    public void AnAutoProperty_GetsABackingFieldAndAccessors()
    {
        var il = CompileToIl("""
            Class Box
             Public Property Alpha As Integer
            End Class

            Module M
             Sub Main()
              PrintLine("hi")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain(".field private int32 '<Alpha>k__BackingField'"), il);
            Assert.That(il, Does.Contain("ldfld int32 'Box'::'<Alpha>k__BackingField'"),
                "the synthesized getter reads it:\n" + il);
            Assert.That(il, Does.Contain("stfld int32 'Box'::'<Alpha>k__BackingField'"),
                "and the synthesized setter writes it:\n" + il);
        });
    }

    /// <summary>
    /// ⛔ The guard. A plain FIELD must stay a field — routing every member access through an
    /// accessor would name <c>get_N</c> on a class that has no such method, trading one
    /// MissingFieldException for a MissingMethodException.
    ///
    /// <para>⚠ The class declares a property AND a field on purpose. With only a field the class
    /// has no properties at all, so a lookup that matched the wrong one — or matched anything —
    /// would have nothing to match and the test would pass for the wrong reason.</para>
    /// </summary>
    [Test]
    public void APlainField_IsStillAField()
    {
        const string program = """
            Class Box
             Public Property Alpha As Integer
             Public N As Integer
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.N = 7
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """;

        var il = CompileToIl(program);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("stfld int32 'Box'::'N'"), il);
            Assert.That(il, Does.Contain("ldfld int32 'Box'::'N'"), il);
            Assert.That(il, Does.Not.Contain("get_N"),
                "a field has no accessor to call:\n" + il);
            Assert.That(RunExpectingSuccess(program), Is.EqualTo("7\n"));
        });
    }

    /// <summary>
    /// ⚠ The access is a CALL, not a load — asserted on the IL because the run cannot tell the two
    /// apart for an auto property, whose accessors do exactly what the field access used to.
    /// </summary>
    [Test]
    public void APropertyAccess_IsAnAccessorCall()
    {
        var il = CompileToIl("""
            Class Box
             Public Property Alpha As Integer
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Alpha = 7
              PrintLine(CStr(c.Alpha))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("callvirt instance void 'Box'::set_Alpha(int32)"), il);
            Assert.That(il, Does.Contain("callvirt instance int32 'Box'::get_Alpha()"), il);
            Assert.That(il, Does.Not.Contain("stfld int32 'Box'::'Alpha'"),
                "the property is not storage; that read named a field that does not exist:\n" + il);
        });
    }
}
