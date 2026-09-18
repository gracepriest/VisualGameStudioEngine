using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Instance field initializers on the MSIL backend, which were dropped — silently.
///
/// <para>⛔ <c>Public N As Integer = 5</c> emitted the field and threw the 5 away, so the program
/// ran and read <b>0</b>. Nothing failed to assemble and nothing threw; the value was simply
/// gone. Measured before:</para>
///
/// <list type="bullet">
/// <item>implicit constructor — <c>0</c>, and a <c>String</c> field came out null.</item>
/// <item>explicit constructor — <c>0</c>.</item>
/// <item>a constructor that BUILDS on the value — <c>N = N + 3</c> over <c>= 5</c> answered
/// <b>3</b>, not 8, because it started from the zero.</item>
/// <item>two constructors — <c>0,3</c> rather than <c>5,8</c>.</item>
/// <item><c>Shared</c> — <b>5</b>, the one shape that already worked, because
/// <c>GenerateClassStaticConstructor</c> does this for the type initializer.</item>
/// </list>
///
/// <para>⚠ The same program is right on JavaScript (<c>5,hi</c>) and C# (which emits
/// <c>public int N = 5;</c>). ⛔ <b>C++ had the SAME gap</b> — it was not fixed in this change, and
/// was fixed straight after in <c>CppFieldInitializerTests</c> /
/// <c>CppCodeGenerator.FieldInitializer</c>. The two fixes are shaped DIFFERENTLY: there the
/// values are IN-CLASS member initializers, because the emitted class often has no constructor at
/// all; here they are constructor stores, because IL has no such thing.</para>
///
/// <para>⚠ NOT a backend gap, and shared by everyone: a NON-LITERAL initializer was dropped in
/// the IR — <c>Public N As Integer = 2 + 3</c> read 0 on JavaScript and MSIL alike and C# emitted
/// <c>public int N;</c>. A front-end fix, not this one, and made straight after in
/// <c>FieldInitializerFoldTests</c> / <c>IRBuilder.TryFoldInitializerToConstant</c>. It is why
/// every case in THIS fixture uses a plain literal.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilFieldInitializerTests
{
    /// <summary>
    /// ⛔ The headline, with a String beside the Integer: a reference-typed field's initializer was
    /// dropped the same way, leaving null rather than a wrong number.
    /// </summary>
    [Test]
    public void AnInstanceFieldInitializer_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public N As Integer = 5
             Public S As String = "hi"
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.N) & "," & c.S)
             End Sub
            End Module
            """), Is.EqualTo("5,hi\n"));
    }

    /// <summary>
    /// ⚠ A class with a DECLARED constructor never reaches the generated default one, so both
    /// paths need the initialization — which is why it lives in a helper both call rather than
    /// inline in either.
    /// </summary>
    [Test]
    public void AnExplicitConstructor_StillGetsTheInitializers()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public N As Integer = 5
             Public Sub New()
              PrintLine("ctor")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """), Is.EqualTo("ctor\n5\n"));
    }

    /// <summary>
    /// ⛔ The case that shows the initializers must run BEFORE the constructor body rather than
    /// merely somewhere: the body reads the field it is about to change. This answered <b>3</b> —
    /// the constructor added to a zero — and the right answer is 8. A fix that appended the
    /// initializers after the body would pass the plain test above and fail this one.
    /// </summary>
    [Test]
    public void TheInitializersRunBeforeTheConstructorBody()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public N As Integer = 5
             Public Sub New(v As Integer)
              N = N + v
             End Sub
            End Class

            Module M
             Sub Main()
              Dim c As New Box(3)
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """), Is.EqualTo("8\n"));
    }

    /// <summary>⚠ Every constructor gets them, not just the first one emitted.</summary>
    [Test]
    public void EveryConstructor_GetsTheInitializers()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public N As Integer = 5
             Public Sub New()
             End Sub
             Public Sub New(v As Integer)
              N = N + v
             End Sub
            End Class

            Module M
             Sub Main()
              Dim a As New Box()
              Dim b As New Box(3)
              PrintLine(CStr(a.N) & "," & CStr(b.N))
             End Sub
            End Module
            """), Is.EqualTo("5,8\n"));
    }

    /// <summary>
    /// ⚠ AFTER the base call, which is where VB runs field initializers — so a base constructor
    /// observes its OWN fields already set. Asserted by having the base constructor print the
    /// field it declared: initializing after the body, or before the base call, changes what this
    /// line says.
    /// </summary>
    [Test]
    public void TheInitializersRunAfterTheBaseCall()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public B As Integer = 1
             Public Sub New()
              PrintLine("base sees B=" & CStr(B))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              PrintLine("derived")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """), Is.EqualTo("base sees B=1\nderived\n"));
    }

    /// <summary>
    /// ⛔ The half that was already there must survive. A sized array FIELD gets its storage in
    /// the same loop, and an initialized field must not cost it — the C++ backend's own note
    /// records that leaving array fields unsized turned "does not build" into "builds and
    /// access-violates".
    /// </summary>
    [Test]
    public void ASizedArrayField_IsStillAllocated()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public A(3) As Integer
             Public N As Integer = 5
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.A(0) = 7
              PrintLine(CStr(c.A(0)) & "," & CStr(c.N))
             End Sub
            End Module
            """), Is.EqualTo("7,5\n"));
    }

    /// <summary>
    /// ⚠ <c>Shared</c> is untouched: it was already right, through the type initializer, and a
    /// static field is not an instance's to create. Without that guard the instance constructor
    /// would emit <c>stfld</c> against a static field, which does not verify.
    /// </summary>
    [Test]
    public void ASharedFieldInitializer_IsLeftToTheTypeInitializer()
    {
        const string program = """
            Class Box
             Public Shared Total As Integer = 5
             Public N As Integer = 7
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(Box.Total) & "," & CStr(c.N))
             End Sub
            End Module
            """;

        var il = CompileToIl(program);

        Assert.Multiple(() =>
        {
            Assert.That(RunExpectingSuccess(program), Is.EqualTo("5,7\n"));
            Assert.That(il, Does.Contain("stsfld int32 'Box'::'Total'"),
                "the Shared one goes through the type initializer:\n" + il);
            Assert.That(il, Does.Not.Contain("stfld int32 'Box'::'Total'"),
                "and never through an instance constructor — stfld on a static field does not "
                + "verify:\n" + il);
        });
    }

    /// <summary>
    /// ⚠ The IL, because the ORDER is the property and a run only sees its effect. The store must
    /// sit between the base call and the body.
    /// </summary>
    [Test]
    public void TheConstructorStoresTheInitializer_AfterTheBaseCall()
    {
        var il = CompileToIl("""
            Class Box
             Public N As Integer = 5
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """);

        var ctor = il.Substring(il.IndexOf("instance void .ctor() cil managed"));
        var baseCall = ctor.IndexOf("call instance void [mscorlib]System.Object::.ctor()");
        var store = ctor.IndexOf("stfld int32 'Box'::'N'");

        Assert.Multiple(() =>
        {
            Assert.That(baseCall, Is.GreaterThan(-1), il);
            Assert.That(store, Is.GreaterThan(-1), "the initializer must be stored at all:\n" + il);
            Assert.That(store, Is.GreaterThan(baseCall),
                "and after the base constructor call, which is where VB runs it:\n" + il);
        });
    }
}
