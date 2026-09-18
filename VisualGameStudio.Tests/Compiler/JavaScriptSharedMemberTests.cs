using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Shared</c> members on the JavaScript backend, which were read and written through
/// <c>this</c>.
///
/// <para>⛔ The class emitted <c>static K = 9;</c> and its methods emitted <c>this.K</c> — and
/// <c>this.K</c> is <b>undefined</b> for a JS static. So a read answered <c>undefined</c>, and a
/// write silently created an INSTANCE property instead of touching the static at all. Shared did
/// not mean shared.</para>
///
/// <para>⛔ A SINGLE-INSTANCE probe HIDES the write half, which is why the decisive test below uses
/// two objects: after <c>this.K = 7</c> the same object reads its own new instance property back
/// as 7 and everything looks right. Measured across two instances, <c>a.Bump()</c> then
/// <c>b.Read()</c> printed <b>undefined</b> on JavaScript where C++ printed <b>7</b>.</para>
///
/// <para>⚠ Every behavioural case runs under BOTH the non-optimizing helper and the OPTIMIZED
/// pipeline every shipping route uses, per CLAUDE.md — a member reference is exactly the kind of
/// thing copy propagation reasons about, so a green non-optimizing run is not proof here.</para>
///
/// <para>⚠ Both halves now go through one helper (<c>MemberReference</c>), so a read and a write
/// cannot disagree about where a member lives, and it resolves to the DECLARING class rather than
/// the current one — JS resolves a static read up the prototype chain, but <c>Derived.K = 7</c>
/// would create a NEW static on Derived and leave Base's untouched.</para>
///
/// <para>⛔ That declaring-class walk cannot be exercised from BasicLang source TODAY, and the
/// mutation dropping it SURVIVES: an inherited member is not nameable at all — <c>Return K</c> for
/// a Base's <c>Shared K</c> is "Undefined identifier 'K'", exactly as a <c>Protected</c> instance
/// field is ("Undefined identifier 'P'"). It is kept because the sibling it must agree with,
/// <c>MemberNames</c>, walks the same chain: if only that one did, then the day inherited members
/// resolve, <c>_memberNames</c> would hold the inherited static while the owners map did not, and
/// the read would fall back to <c>this.K</c> — silently reintroducing this exact bug for inherited
/// statics.</para>
///
/// <para>⚠ A <c>Shared</c> METHOD call is a separate PRE-EXISTING gap, unchanged here:
/// <c>Box.Read()</c> is "JavaScript backend: no lowering for 'Box.Read'".</para>
///
/// <para>⛔ MUTATIONS — four kills, one recorded survivor. Routing the read back to <c>this</c>
/// kills 4 cases; routing the WRITE back kills 1; dropping the <c>IsStatic</c> filter kills the
/// instance guard; dropping properties from the owners map kills the property case. The write
/// mutation SURVIVED the fixture's first draft, which is how the second write site was found —
/// there are two, and <c>K = 7</c> exercises only one of them.</para>
/// </summary>
[TestFixture]
public class JavaScriptSharedMemberTests
{
    /// <summary>
    /// ⛔ THE decisive case: two instances. A Shared field written through one object must be
    /// visible from another, which is the whole meaning of Shared — and the shape a
    /// single-instance test cannot distinguish from a per-instance property.
    ///
    /// <para>⚠ C++ asserted alongside, because "JavaScript now agrees with the other backends" is
    /// the property, not "JavaScript prints 7".</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TwoInstances_ShareASharedField()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 0
             Public Sub Bump()
              K = 7
             End Sub
             Public Function Read() As Integer
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              Dim a As New Box()
              Dim b As New Box()
              a.Bump()
              PrintLine(CStr(b.Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7"),
                "a Shared write through one instance must be visible from another");
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("7"),
                "and under the optimizer every shipping route runs");
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("7\n"),
                "and JavaScript must agree with C++");
        });
    }

    /// <summary>
    /// ⛔ The read half on its own: this answered <c>undefined</c>, the plainest possible symptom.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedField_IsReadableFromAnInstanceMethod()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 9
             Public Function Read() As Integer
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("9"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
        });
    }

    /// <summary>
    /// ⛔ A COMPOUND assignment, which is a SECOND write site and not the one above. A plain
    /// <c>K = 7</c> lowers through the lvalue path; <c>K = K + 1</c> produces an IRBinaryOp that
    /// IRBuilder renames after the variable, so it lands in <c>Bind</c>'s member arm instead.
    /// Measured: mutating only <c>Bind</c> left every other test in this fixture GREEN, so without
    /// this case that arm is untested.
    ///
    /// <para>⛔ Its pre-change symptom is the worst kind — no crash, no NaN reaching the output, a
    /// plausible number. <c>this.K = ((this.K + 1) | 0)</c> computes <c>undefined + 1</c> = NaN,
    /// and <c>NaN | 0</c> is <b>0</b>: a counter that silently reads 0 forever. Measured on the
    /// unpatched backend, two Bumps printed <b>0</b> where every other backend prints 2.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ACompoundSharedAssignment_AccumulatesAcrossInstances()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 0
             Public Sub Bump()
              K = K + 1
             End Sub
             Public Function Read() As Integer
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              Dim a As New Box()
              Dim b As New Box()
              a.Bump()
              b.Bump()
              PrintLine(CStr(b.Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JsTestSupport.Compile(program), Does.Contain("Box.K = ((Box.K + 1) | 0)"),
                "the compound write lands on the class, both sides of it");
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("2"),
                "two increments through two instances accumulate");
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("2"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("2\n"));
        });
    }

    /// <summary>
    /// ⛔ The emitted TEXT, because <c>this.K</c> versus <c>Box.K</c> is the defect itself and a
    /// run only shows its consequence. Both the read and the write are asserted: they are separate
    /// emission sites, and fixing one alone leaves the other silently wrong.
    /// </summary>
    [Test]
    public void TheEmittedJavaScript_ReferencesTheClass_NotThis()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 0
             Public Sub Bump()
              K = 7
             End Sub
             Public Function Read() As Integer
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Bump()
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """;

        foreach (var js in new[] { JsTestSupport.Compile(program), JsTestSupport.CompileOptimized(program) })
        {
            Assert.Multiple(() =>
            {
                Assert.That(js, Does.Contain("Box.K = 7"), "the WRITE site:\n" + js);
                Assert.That(js, Does.Contain("return Box.K"), "the READ site:\n" + js);
                Assert.That(js, Does.Not.Contain("this.K"),
                    "a static is never reached through this:\n" + js);
            });
        }
    }

    /// <summary>
    /// ⚠ The regression guard that matters most: an INSTANCE member must still go through
    /// <c>this</c>. Routing everything at the class would break every ordinary field, so this
    /// asserts the emitted text AND that two instances stay independent.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnInstanceField_StillUsesThis_AndStaysPerInstance()
    {
        const string program = """
            Class Box
             Private _n As Integer = 0
             Public Sub Bump()
              _n = 7
             End Sub
             Public Function Read() As Integer
              Return _n
             End Function
            End Class

            Module M
             Sub Main()
              Dim a As New Box()
              Dim b As New Box()
              a.Bump()
              PrintLine(CStr(a.Read()) & "," & CStr(b.Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JsTestSupport.Compile(program), Does.Contain("this._n"),
                "an instance field still resolves through this");
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7,0"),
                "and the two instances stay independent");
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("7,0"));
        });
    }

    /// <summary>
    /// ⚠ A Shared PROPERTY takes the same path — it is in the owners map beside fields and events,
    /// so it must not be left reaching through <c>this</c> either.
    /// </summary>
    [Test]
    public void ASharedProperty_AlsoReferencesTheClass()
    {
        const string program = """
            Class Box
             Public Shared Property P As Integer
             Public Function Read() As Integer
              Return P
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """;

        var js = JsTestSupport.Compile(program);
        Assert.That(js, Does.Contain("return Box.P"), js);
    }

    /// <summary>
    /// ⚠ A LOCAL of the same name still wins. The shadowing check runs before the member lookup,
    /// and routing members at the class must not reach past a declared local.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ALocalShadowingASharedName_StaysLocal()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 9
             Public Function Read() As Integer
              Dim K As Integer = 3
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("3"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("3"));
        });
    }
}
