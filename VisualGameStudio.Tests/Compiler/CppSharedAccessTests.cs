using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Qualified <c>Shared</c> access on the C++ backend — <c>Box.K</c>, <c>Box.K = 5</c>,
/// <c>Box.Read()</c> — which did not compile at all.
///
/// <para>⛔ Every QUALIFIED form treated the class name as an OBJECT, while every unqualified and
/// in-class form already worked. Measured before:</para>
///
/// <list type="bullet">
/// <item><c>Box.K</c> emitted <c>Box-&gt;K</c> — <c>'Box' does not refer to a value</c>.</item>
/// <item><c>Box.K = 5</c> emitted <c>Box-&gt;K = 5</c> — <c>cannot use arrow operator on a
/// type</c>.</item>
/// <item><c>Box.Read()</c> emitted <c>Read()</c>, the qualifier DROPPED — <c>use of undeclared
/// identifier 'Read'</c>.</item>
/// </list>
///
/// <para>⚠ The class DECLARATION was always right — <c>static int32_t K;</c>, <c>static int32_t
/// Read()</c> — so only the USE site was ever wrong. That is why this is a lowering fix and not a
/// change to how classes are emitted.</para>
///
/// <para>⛔ The CALL had a different cause from the two field forms, and it is the sharp one.
/// <c>ResolveFlattenedFunctionName</c> exists to align a cross-module call (<c>Helpers.Print</c>)
/// with the flattened free function the backend emits. Class member bodies ALSO live in
/// <c>_module.Functions</c> under their bare names, so <c>Box.Read</c> found a free function called
/// <c>Read</c>, concluded it was a flattened module procedure, and threw the qualifier away. The
/// helper's premise — a qualifier naming a MODULE — simply does not hold for a class.</para>
///
/// <para>⛔ AND THE SEGMENTS MUST BE SANITIZED SEPARATELY. <c>SanitizeName</c> strips every
/// non-alphanumeric character, so handing it <c>"Box::Read"</c> yields <c>BoxRead</c> — a name that
/// exists nowhere, and a silent mis-emission rather than a compile error. The first draft of this
/// change returned the qualified string from <c>ResolveFlattenedFunctionName</c> and would have hit
/// exactly that; the JavaScript backend hit the same trap with dotted names.</para>
///
/// <para>⚠ MSIL is asserted alongside throughout: "C++ now agrees with the backend that has this
/// right" is the property, not "C++ prints 9".</para>
///
/// <para>⛔ INHERITED access is STILL BROKEN and is pinned below, for a FRONT-END reason rather
/// than this one. The emitted access is correct (<c>Base::K</c>, resolved to the declaring class)
/// but the IR types an inherited Shared read as <c>Object</c>, so the result temp is declared
/// <c>void*</c> and the file does not compile. Measured contrast: a DIRECT read declares
/// <c>int32_t t0</c>, an inherited one <c>void* t0</c>, with an identical access expression.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppSharedAccessTests
{
    /// <summary>⛔ The headline read: <c>'Box' does not refer to a value</c> before this.</summary>
    [Test]
    public void AQualifiedSharedFieldRead_CompilesAndRuns()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 9
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.K))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>
    /// ⛔ The WRITE, which failed differently — <c>cannot use arrow operator on a type</c> — and so
    /// needs its own case. It reads the value back through the class, which is the only way to see
    /// that the write landed on the static rather than somewhere else.
    /// </summary>
    [Test]
    public void AQualifiedSharedFieldWrite_LandsOnTheStatic()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 0
            End Class

            Module M
             Sub Main()
              Box.K = 5
              PrintLine(CStr(Box.K))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("5\n"));
        });
    }

    /// <summary>⛔ The qualified CALL, whose qualifier was thrown away by the module flattening.</summary>
    [Test]
    public void AQualifiedSharedMethodCall_CompilesAndRuns()
    {
        const string program = """
            Class Box
             Public Shared Function Read() As Integer
              Return 9
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>⚠ A Shared method reading a Shared field — both halves in one program.</summary>
    [Test]
    public void ASharedMethodReadingASharedField_Runs()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 9
             Public Shared Function R() As Integer
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.R()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>
    /// ⚠ A class <c>Const</c> read from OUTSIDE. This is the shape PR #48 recorded as still broken
    /// on C++ ("<c>t0 = Box-&gt;K;</c>, 'Box' does not refer to a value") — a class Const lowers to
    /// a static field, so it was the same defect wearing a different name, and it is closed by the
    /// same fix. The note in <c>ClassConstantTests</c> is corrected with this change.
    /// </summary>
    [Test]
    public void AClassConstant_IsReadableFromOutside()
    {
        const string program = """
            Class Box
             Public Const K As Integer = 9
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.K))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>⚠ A Shared PROPERTY takes the same qualifier, written and read.</summary>
    [Test]
    public void ASharedProperty_ReadsAndWritesThroughTheClass()
    {
        const string program = """
            Class Box
             Public Shared Property P As Integer
            End Class

            Module M
             Sub Main()
              Box.P = 7
              PrintLine(CStr(Box.P))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("7\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("7\n"));
        });
    }

    /// <summary>
    /// ⛔ TWO classes with the SAME Shared member name. A qualifier that resolved to the wrong
    /// class, or got flattened away, would make these collide — and the sum is what distinguishes
    /// "both resolved" from "one resolved twice".
    /// </summary>
    [Test]
    public void TwoClassesWithTheSameSharedName_StayDistinct()
    {
        const string program = """
            Class A
             Public Shared K As Integer = 5
            End Class

            Class B
             Public Shared K As Integer = 7
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(A.K + B.K))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("12\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("12\n"));
        });
    }

    /// <summary>
    /// ⚠ A LOCAL whose name matches a class must WIN — the same shadowing rule the native-BCL
    /// type-name arm already applies.
    ///
    /// <para>⛔ IT MUST READ THE CLASS STATIC THROUGH A SECOND PATH, and that is the whole design of
    /// this test. Writing the local and reading it straight back CANNOT distinguish the two
    /// locations: with the guard removed the emission becomes <c>Box::K = 3; t0 = Box::K;</c>, which
    /// writes the class static and reads the same wrong place back, so it still prints 3 and a
    /// write-then-read test passes on a completely broken lowering. Measured — the first draft of
    /// this test did exactly that and SURVIVED the drop-the-guard mutation. The helper function
    /// below reads <c>Box.K</c> from a scope with no local shadowing it, so the two locations can
    /// finally disagree: correct is <c>3,9</c>, and routing the local at the class gives
    /// <c>3,3</c>.</para>
    /// </summary>
    [Test]
    public void ALocalShadowingAClassName_Wins()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 9
            End Class

            Module M
             Structure Holder
              Public K As Integer
             End Structure
             Function PeekBoxK() As Integer
              Return Box.K
             End Function
             Sub Main()
              Dim Box As Holder
              Box.K = 3
              PrintLine(CStr(Box.K) & "," & CStr(PeekBoxK()))
             End Sub
            End Module
            """;

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("3,9\n"),
            "local | class static — the local must win, and the class static must be untouched");
    }

    /// <summary>
    /// ⚠ The regression that matters most: an INSTANCE member must still go through the object.
    /// Routing every member at the class would break every ordinary field, so this asserts the
    /// emitted text AND that two instances stay independent.
    /// </summary>
    [Test]
    public void AnInstanceField_StillGoesThroughTheObject()
    {
        const string program = """
            Class Box
             Public N As Integer = 0
             Public Sub Put(p As Integer)
              N = p
             End Sub
             Public Function Get1() As Integer
              Return N
             End Function
            End Class

            Module M
             Sub Main()
              Dim a As New Box()
              Dim b As New Box()
              a.Put(7)
              PrintLine(CStr(a.Get1()) & "," & CStr(b.Get1()))
             End Sub
            End Module
            """;

        var cpp = BclE2E.CompileToCppOptimized(program);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Not.Contain("Box::N"),
                "an instance field must not be reached through the class");
            Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("7,0\n"));
        });
    }

    /// <summary>
    /// ⛔ The emitted TEXT, because <c>Box-&gt;K</c> versus <c>Box::K</c> is the defect itself, and
    /// because <c>BoxRead()</c> — what a single SanitizeName over the qualified string would
    /// produce — compiles to nothing and would be caught nowhere else.
    /// </summary>
    [Test]
    public void TheEmittedCpp_UsesScopeResolution_NotArrowAndNotAFlattenedName()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public Shared K As Integer = 0
             Public Shared Function Read() As Integer
              Return 9
             End Function
            End Class

            Module M
             Sub Main()
              Box.K = 5
              PrintLine(CStr(Box.K + Box.Read()))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("Box::K"), "the field, scope-resolved");
            Assert.That(cpp, Does.Contain("Box::Read("), "and the call");
            Assert.That(cpp, Does.Not.Contain("Box->K"), "never through the arrow operator");
            Assert.That(cpp, Does.Not.Contain("BoxRead"),
                "and never flattened into one identifier — what sanitizing 'Box::Read' as a single "
                + "string would produce");
        });
    }

    // ------------------------------------------------- the base walk, proven by EXECUTION

    /// <summary>
    /// ✅ The one INHERITED shape that runs today, and the reason the base-class walk is not
    /// speculative. <c>Derived.K = 7</c> reaches a field declared on <c>Base</c>, so resolving the
    /// qualifier means following <c>Inherits</c>; a walk that looked only at <c>Derived</c>'s own
    /// fields would find nothing, emit no qualifier at all, and fall back to <c>Derived-&gt;K</c> —
    /// the original defect.
    ///
    /// <para>⚠ A WRITE is used deliberately. Every inherited READ is blocked upstream (the pin
    /// below), because a read needs a typed result temp and the IR types an inherited Shared read
    /// as <c>Object</c>. A write has no result temp, so it is the only inherited shape that reaches
    /// a running program — which is why the read-back goes through <c>Base.K</c> (a DIRECT read,
    /// correctly typed) rather than through <c>Derived.K</c>.</para>
    /// </summary>
    [Test]
    public void AnInheritedSharedFieldWrite_ReachesTheBaseStatic_AndRuns()
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized("""
            Class Base
             Public Shared K As Integer = 4
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Derived.K = 7
              PrintLine(CStr(Base.K))
             End Sub
            End Module
            """)), Is.EqualTo("7\n"),
            "the write went through Derived, the read through Base, and they agree — so the "
            + "qualifier resolved to the ONE static on Base rather than inventing a second one");
    }

    // ------------------------------------------------------- pin on what is STILL broken

    /// <summary>
    /// ✅ An INHERITED Shared member, PROMOTED from a pin on 2026-09-19. This was two halves: the
    /// access already resolved to the declaring class and emitted <c>Base::K</c>, while the front
    /// end typed the inherited Shared read <c>Object</c>, so its temp was declared <c>void*</c> and
    /// the file did not compile. The base-chain walk in the analyzer types it now, so the second
    /// half is a compile-and-RUN assertion; the form pin on the qualifier is kept.
    /// </summary>
    [Test]
    public void AnInheritedSharedField_IsReadThroughTheDerivedClass_AndRuns()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Base
             Public Shared K As Integer = 4
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Derived.K))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Match(@"=\s*Base::K;"),
                "this change's half IS correct: the access resolves to the DECLARING class. This "
                + "matches the ACCESS STATEMENT and not merely the text 'Base::K', because the "
                + "out-of-line static DEFINITION the backend emits — int32_t Base::K = 4; — "
                + "contains that text no matter what the access site does, and a bare Contain "
                + "check therefore pins nothing.\n\n"
                + "⚠ THIS HALF IS A FORM PIN, NOT A BEHAVIOUR PIN, and is labelled so rather than "
                + "dressed up as more. Emitting the WRITTEN class instead — Derived::K — was "
                + "measured to compile and produce the SAME answer, because C++ resolves a "
                + "qualified static through the base class. The declaring-class form is chosen "
                + "because the base walk has to run anyway to decide whether to qualify AT ALL "
                + "(that part IS behavioural — see the inherited-write case above), and because "
                + "it is what the other backends emit.");
            Assert.That(cpp, Does.Not.Match(@"void\*\s+t\d+"),
                "the inherited Shared read is typed from the DECLARING class's field, not Object — "
                + "a void* temp here is the old front-end mistyping come back");
            Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("4\n"),
                "and it compiles and runs, which the void* temp made impossible");
        });
    }

    /// <summary>
    /// ✅ A Shared SUB, which reaches <c>Visit(IRCall)</c> as a STATEMENT rather than as an
    /// expression. Every other call case here returns a value, so without this one the qualified
    /// call path is only ever exercised in its result-assigning form.
    /// </summary>
    [Test]
    public void AQualifiedSharedSubCall_RunsAsAStatement()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 4
             Public Shared Sub Bump()
              K = K + 5
             End Sub
            End Class

            Module M
             Sub Main()
              Box.Bump()
              PrintLine(CStr(Box.K))
             End Sub
            End Module
            """;

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"),
            "emitted as a bare Box::Bump(); statement — no result temp");
    }

    /// <summary>
    /// ✅ An INHERITED Shared SUB call, PROMOTED from a pin on 2026-09-19 — the second face of the
    /// same upstream defect as the inherited read above. The qualifier always resolved to
    /// <c>Base::Bump</c>, but the front end did not carry the inherited member's SIGNATURE, so it
    /// built an expression call and the backend bound the result: <c>t0 = Base::Bump();</c>, which
    /// C++ rejects for a void function. The base-chain walk finds the real Sub symbol now.
    /// </summary>
    [Test]
    public void AnInheritedSharedSubCall_RunsAsAStatement()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Base
             Public Shared K As Integer = 4
             Public Shared Sub Bump()
              K = K + 5
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Derived.Bump()
              PrintLine(CStr(Base.K))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Not.Match(@"t\d+\s*=\s*Base::Bump\(\);"),
                "the void Sub is emitted as a STATEMENT, not bound to a temp");
            Assert.That(cpp, Does.Match(@"(?<![=\w])\s*Base::Bump\(\);"),
                "and it still goes to the DECLARING class");
            Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("9\n"),
                "compiles and runs: Bump added 5 to the one shared K");
        });
    }
}
