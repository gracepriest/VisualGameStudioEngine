using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Instance field initializers on the C++ backend, which were dropped — silently.
///
/// <para>⛔ Every one of them, at every access level. Measured on a class with five initialized
/// public fields, C++ printed <c>0,,0.000000,0.000000,False</c> where JavaScript printed
/// <c>5,hi,2.5,1.5,true</c> — Integer, String, Double, Single and Boolean alike, so not one
/// type's problem. A constructor that built on the value inherited the zero: <c>_n = _n + 3</c>
/// over <c>= 5</c> answered <b>3</b> rather than 8. A sized array field beside an initialized one
/// gave <c>7,0</c> — the array worked, the initializer did not.</para>
///
/// <para>⛔ The cause was one helper with a narrower job than its callers assumed: all three field
/// loops in <c>GenerateClass</c> asked <c>FieldArrayInitializer</c>, which only ever produced a
/// SIZED-ARRAY form and never consulted <c>IRField.Initializer</c>. The STATIC path
/// (<c>EmitStaticMemberInitializationsCore</c>) did read it, which is where the expression to emit
/// comes from — so an out-of-class static definition and an in-class instance one cannot disagree
/// about how a constant is spelled.</para>
///
/// <para>⚠ The MSIL half of this defect was fixed first, separately: there the initializers belong
/// in the CONSTRUCTOR, after the base call. Here they are IN-CLASS member initializers, because
/// the emitted class often has no constructor at all and C++ runs in-class initializers before
/// any constructor body, in declaration order — which is VB's rule too.</para>
///
/// <para>⚠ THREE shapes cannot be run end to end on this backend, each PRE-EXISTING and verified
/// before the change:</para>
///
/// <list type="bullet">
/// <item>A <c>Shared</c> field ACCESS does not compile: <c>Box.Total</c> emits
/// <c>t0 = Box-&gt;Total;</c> — "'Box' does not refer to a value".</item>
/// <item>A <c>Protected</c> field is not visible from a derived class ("Undefined identifier"):
/// the analyzer does not inherit Protected members into scope, so that site is pinned on the
/// emitted TEXT.</item>
/// <item>A <c>Structure</c> field initializer does not PARSE at all.</item>
/// </list>
///
/// <para>⚠ Two differences from JavaScript in the headline output are NOT this fix's and stay:
/// <c>CStr(Double)</c> prints <c>2.500000</c> on C++ and <c>CStr(Boolean)</c> prints <c>True</c>,
/// both long-recorded formatting divergences. The tests assert C++'s own spelling rather than
/// normalising it away.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppFieldInitializerTests
{
    /// <summary>
    /// ⛔ The headline, across five types, because the drop was not type-specific: an Integer read
    /// 0, a String read empty, a Double and a Single read 0, and a Boolean read False.
    /// </summary>
    [Test]
    public void EveryFieldInitializerRuns_WhateverTheType()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public N As Integer = 5
             Public S As String = "hi"
             Public D As Double = 2.5
             Public F As Single = 1.5
             Public B As Boolean = True
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.N) & "," & c.S & "," & CStr(c.D) & "," & CStr(c.F) & "," & CStr(c.B))
             End Sub
            End Module
            """);

        Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("5,hi,2.500000,1.500000,True\n"),
            "2.500000 and True are C++'s own CStr spellings — a separate, pre-existing "
            + "divergence from JavaScript's 2.5 and true, deliberately not normalised here");
    }

    /// <summary>
    /// ⛔ The case that shows the value has to be there BEFORE the constructor body, not merely
    /// somewhere: the body reads the field it is about to change. This answered <b>3</b> — the
    /// constructor added to a zero — and the right answer is 8.
    /// </summary>
    [Test]
    public void TheInitializerIsInPlaceBeforeTheConstructorBody()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Private _n As Integer = 5
             Public Sub New(v As Integer)
              _n = _n + v
             End Sub
             Public Function Read() As Integer
              Return _n
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box(3)
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """);

        Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("8\n"));
    }

    /// <summary>
    /// ⛔ The half that was already there must survive. A sized array field gets its storage from
    /// the same helper, and an initialized field must not cost it — this printed <c>7,0</c>
    /// before: the array worked and the initializer did not.
    /// </summary>
    [Test]
    public void ASizedArrayField_StillGetsItsStorage()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
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
            """);

        Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("7,5\n"));
    }

    /// <summary>
    /// ⚠ ALL THREE access levels, asserted on the emitted text because two of them cannot be run:
    /// a <c>Protected</c> field is not visible from a derived class on this front end, and the
    /// three loops in <c>GenerateClass</c> are separate copies — fixing one is not fixing the
    /// others.
    /// </summary>
    [Test]
    public void AllThreeAccessLevels_CarryTheirInitializer()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Private Priv As Integer = 1
             Protected Prot As Integer = 2
             Public Pub As Integer = 3
             Public Function Read() As Integer
              Return Priv
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.Read()) & "," & CStr(c.Pub))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("int32_t Priv = 1;"), "private:\n" + cpp);
            Assert.That(cpp, Does.Contain("int32_t Prot = 2;"), "protected:\n" + cpp);
            Assert.That(cpp, Does.Contain("int32_t Pub = 3;"), "public:\n" + cpp);
            Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("1,3\n"),
                "and the two that CAN be read agree");
        });
    }

    /// <summary>
    /// ⛔ A <c>Shared</c> field must NOT get an in-class initializer — that is not legal C++ for a
    /// non-const static, and the out-of-class definition
    /// (<c>EmitStaticMemberInitializationsCore</c>) is what carries its value. Asserted on the
    /// text because a Shared field ACCESS does not compile on this backend at all
    /// (<c>t0 = Box-&gt;Total;</c>, "'Box' does not refer to a value"), so nothing here can run
    /// it.
    /// </summary>
    [Test]
    public void ASharedField_IsInitializedOutOfClass_NotInIt()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public Shared Total As Integer = 5
             Public N As Integer = 7
            End Class

            Module M
             Sub Main()
              PrintLine("hi")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("static int32_t Total;"),
                "the in-class declaration carries no initializer:\n" + cpp);
            Assert.That(cpp, Does.Contain("int32_t Box::Total = 5;"),
                "the value lives in the out-of-class definition:\n" + cpp);
            Assert.That(cpp, Does.Contain("int32_t N = 7;"),
                "while the instance field beside it does get one:\n" + cpp);
        });
    }

    /// <summary>
    /// ⚠ A field with NO initializer and no array size must stay bare — the helper returns an
    /// empty string, and adding <c>= {}</c> or a default would be churn on every field in every
    /// class this backend emits.
    /// </summary>
    [Test]
    public void AFieldWithNoInitializer_StaysBare()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public N As Integer
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.N = 7
              PrintLine(CStr(c.N))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("int32_t N;"), cpp);
            Assert.That(cpp, Does.Not.Contain("int32_t N ="), cpp);
            Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("7\n"));
        });
    }
}
