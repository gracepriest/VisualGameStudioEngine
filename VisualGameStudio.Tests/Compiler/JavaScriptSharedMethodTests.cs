using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Calling a <c>Shared</c> method on the JavaScript backend.
///
/// <para>⛔ THIS WAS THREE DEFECTS, NOT ONE, and the loud one hid the other two. Measured across
/// nine call shapes before any change, against MSIL (which has all of this right since the
/// Shared-member work) and C++:</para>
///
/// <list type="number">
/// <item><b>Qualified <c>Box.Read()</c> was REFUSED</b> — "no lowering for 'Box.Read'".
/// <c>CallTarget</c>'s dotted arm knew exactly two names, <c>Console.WriteLine</c> and
/// <c>Console.Write</c>, and threw on everything else. Loud, and therefore safe.</item>
/// <item><b>An unqualified SIBLING call COMPILED and emitted the bare name</b> — <c>Helper()</c>
/// inside the class — which is a <c>ReferenceError</c>, because a member body is not a top-level
/// function. Nothing failed until the program ran.</item>
/// <item><b><c>obj.SharedMethod()</c> COMPILED and emitted <c>obj.Read()</c></b> — a
/// <c>TypeError</c>, because a JS static does not live on the instance. Also silent until run.</item>
/// </list>
///
/// <para>⛔ Defect 2 IS NOT SHARED-SPECIFIC. An unqualified call to an INSTANCE sibling was
/// equally broken, through the identical path and with the identical symptom — measured. Fixing
/// only the Shared half would have left the same one-line hole open for every instance method, so
/// both are fixed here and both are asserted below.</para>
///
/// <para>⚠ Cross-checked against <b>MSIL</b> rather than C++ for the qualified shapes: C++ has the
/// SAME pre-existing gap there (<c>Box.Read()</c> emits an undeclared identifier and the file does
/// not compile), so it cannot serve as the oracle. Where C++ does work — the sibling and
/// instance-receiver shapes — it is asserted too.</para>
///
/// <para>⚠ <c>Derived.Tag()</c> for a Shared method on <c>Base</c> now runs correctly HERE and is
/// still broken on MSIL (<c>NullReferenceException</c>, a front-end mistyping recorded in
/// HANDOFF). So that one case asserts JavaScript alone, deliberately.</para>
///
/// <para>⛔ TWO shapes are left BROKEN ON PURPOSE, both verified pre-existing and unchanged:
/// an unqualified INSTANCE call from inside a <c>Shared</c> member (invalid VB — BC30469 — that
/// this front end wrongly accepts), and a module function whose name collides with a class
/// method (dropped from emission entirely, on JavaScript AND MSIL). Each is pinned below as what
/// it actually does, so neither is mistaken for working.</para>
/// </summary>
[TestFixture]
public class JavaScriptSharedMethodTests
{
    // ---------------------------------------------------------------- defect 1: refused outright

    /// <summary>⛔ The headline: a qualified Shared call, which did not compile at all.</summary>
    [Test]
    [Category("Integration")]
    public void AQualifiedSharedCall_Runs()
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
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("9"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"),
                "MSIL is the oracle here — C++ has the same pre-existing gap");
        });
    }

    /// <summary>
    /// ⚠ A Shared SUB, so the call is a statement rather than a value — a different emission path
    /// from a Function — and it writes a Shared field, so the effect has to be observable
    /// afterwards through the class.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AQualifiedSharedSub_RunsAndItsWriteIsVisible()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 0
             Public Shared Sub Bump()
              K = 7
             End Sub
            End Class

            Module M
             Sub Main()
              Box.Bump()
              PrintLine(CStr(Box.K))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("7\n"));
        });
    }

    /// <summary>⚠ Arguments reach the static — a call that resolves but drops them still "works".</summary>
    [Test]
    [Category("Integration")]
    public void AQualifiedSharedCall_PassesItsArguments()
    {
        const string program = """
            Class Box
             Public Shared Function Add(a As Integer, b As Integer) As Integer
              Return a + b
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Add(2, 3)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("5\n"));
        });
    }

    /// <summary>⚠ A Shared method qualified by its OWN class, from inside that class.</summary>
    [Test]
    [Category("Integration")]
    public void ASharedMethod_MayQualifyBySelfFromInsideTheClass()
    {
        const string program = """
            Class Box
             Public Shared Function Helper() As Integer
              Return 5
             End Function
             Public Shared Function Read() As Integer
              Return Box.Helper()
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Read()))
             End Sub
            End Module
            """;

        Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
    }

    /// <summary>⚠ Recursion through the method's own unqualified name.</summary>
    [Test]
    [Category("Integration")]
    public void ASharedMethod_RecursesThroughItsOwnName()
    {
        const string program = """
            Class Box
             Public Shared Function Fact(n As Integer) As Integer
              If n <= 1 Then
               Return 1
              End If
              Return n * Fact(n - 1)
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Fact(5)))
             End Sub
            End Module
            """;

        Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("120"));
    }

    // ------------------------------------------- defect 2: bare name, ReferenceError at run time

    /// <summary>
    /// ⛔ SILENT BEFORE: this compiled and emitted <c>const t0 = Helper();</c>, then died in Node
    /// with "Helper is not defined". A build reported success either way.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnUnqualifiedSiblingCall_FromAnInstanceMethod_ReachesTheStatic()
    {
        const string program = """
            Class Box
             Public Shared Function Helper() As Integer
              Return 5
             End Function
             Public Function Read() As Integer
              Return Helper()
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
            Assert.That(JsTestSupport.Compile(program), Does.Contain("Box.Helper()"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("5"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("5\n"));
        });
    }

    /// <summary>
    /// ⛔ THE HALF THAT IS NOT ABOUT Shared AT ALL. An unqualified call to an INSTANCE sibling had
    /// the identical defect — bare <c>Helper()</c>, ReferenceError — because it takes the identical
    /// path. Fixing only the Shared side would have left every instance method broken the same way,
    /// so this is asserted beside it rather than left to a later PR.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnUnqualifiedSiblingCall_ToAnInstanceMethod_ResolvesThroughThis()
    {
        const string program = """
            Class Box
             Public Function Helper() As Integer
              Return 5
             End Function
             Public Function Read() As Integer
              Return Helper()
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
            Assert.That(JsTestSupport.Compile(program), Does.Contain("this.Helper()"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
        });
    }

    /// <summary>⚠ The same unqualified call made from a Shared member rather than an instance one.</summary>
    [Test]
    [Category("Integration")]
    public void AnUnqualifiedSiblingCall_FromASharedMethod_ReachesTheStatic()
    {
        const string program = """
            Class Box
             Public Shared Function Helper() As Integer
              Return 5
             End Function
             Public Shared Function Read() As Integer
              Return Helper()
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Read()))
             End Sub
            End Module
            """;

        Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
    }

    // --------------------------------------------- defect 3: obj.SharedMethod(), TypeError at run

    /// <summary>
    /// ⛔ SILENT BEFORE: <c>c.Read()</c> emitted verbatim, then "c.Read is not a function". Legal
    /// BasicLang, and every other backend runs it.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedMethod_CalledThroughAnInstance_ReachesTheStatic()
    {
        const string program = """
            Class Box
             Public Shared Function Read() As Integer
              Return 9
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
            Assert.That(JsTestSupport.Compile(program), Does.Contain("Box.Read()"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>
    /// ⛔ THE RECEIVER STILL RUNS. Rewriting <c>Make().Read()</c> to <c>Box.Read()</c> would be a
    /// silent behaviour change — the object is never built and <c>Make</c>'s output never appears.
    /// VB evaluates the receiver, and so does MSIL (it evaluates, then pops).
    ///
    /// <para>⛔ JAVASCRIPT ALONE, and NOT because the other backends were not tried. MSIL cannot
    /// ASSEMBLE this program at all: a module function returning a user class emits
    /// <c>Box 'Make'()</c> where ilasm requires <c>class Box</c>, and the file is rejected with a
    /// syntax error. That is a separate pre-existing MSIL gap; asserting it here would have pinned
    /// a broken oracle, which is what the first draft of this test did.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedMethod_CalledThroughAnInstance_StillEvaluatesTheReceiver()
    {
        const string program = """
            Class Box
             Public Shared Function Read() As Integer
              Return 9
             End Function
            End Class

            Module M
             Function Make() As Box
              PrintLine("made")
              Return New Box()
             End Function
             Sub Main()
              PrintLine(CStr(Make().Read()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("made\n9"),
                "the receiver's own output must still appear, and before the result");
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("made\n9"),
                "and the optimizer must not drop the receiver as an unused value");
        });
    }

    /// <summary>
    /// ⚠ A receiver that is NOT a bare identifier rides a comma expression, which keeps the
    /// evaluation and its order. Reached through a FIELD receiver (<c>this.B</c>) — the common
    /// case binds the receiver to a temp first, so without this shape the comma branch would never
    /// run and would be untestable code that ACTS.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ANonSimpleReceiver_RidesACommaExpression()
    {
        const string program = """
            Class Box
             Public Shared Function Read() As Integer
              Return 9
             End Function
            End Class

            Class Holder
             Public B As Box
             Public Function Go() As Integer
              Return B.Read()
             End Function
            End Class

            Module M
             Sub Main()
              Dim h As New Holder()
              h.B = New Box()
              PrintLine(CStr(h.Go()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JsTestSupport.Compile(program), Does.Contain("(this.B, Box.Read())"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
        });
    }

    // ---------------------------------------------------------------------------- inheritance

    /// <summary>
    /// ⚠ An inherited Shared method resolves to its DECLARING class: <c>Derived.Tag()</c> emits
    /// <c>Base.Tag()</c>. JavaScript would survive the written spelling too (statics resolve up
    /// the prototype chain), but naming the declaring class is what the method actually belongs
    /// to, and it keeps this agreeing with the field side, where naming the wrong class silently
    /// creates a second static.
    ///
    /// <para>⛔ JAVASCRIPT ALONE, deliberately: this shape is still BROKEN on MSIL
    /// (<c>NullReferenceException</c>, measured) for a separate front-end reason. Asserting MSIL
    /// here would pin a defect as though it were the contract.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnInheritedSharedMethod_ResolvesToItsDeclaringClass()
    {
        const string program = """
            Class Base
             Public Shared Function Tag() As Integer
              Return 4
             End Function
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Derived.Tag()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JsTestSupport.Compile(program), Does.Contain("Base.Tag()"));
            Assert.That(JsTestSupport.Compile(program), Does.Not.Contain("Derived.Tag()"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("4"));
        });
    }

    // -------------------------------------------------------- pins on what is STILL broken

    /// <summary>
    /// ⛔ PINNED AS BROKEN, ON PURPOSE. An unqualified call to an INSTANCE sibling from inside a
    /// <c>Shared</c> member is invalid VB — BC30469, "Reference to a non-shared member requires an
    /// object reference" — and this front end WRONGLY ACCEPTS it. Measured: MSIL compiles it and
    /// dies with <c>MissingMethodException</c>.
    ///
    /// <para>It is deliberately NOT rewritten to <c>this.Inst()</c>: inside a JS static <c>this</c>
    /// is the class, so that would be a TypeError wearing the shape of working code. Leaving it
    /// alone keeps the FRONT-END gap visible rather than papering it over in the backend. This
    /// test pins the non-rewrite; it goes red the day the front end learns to refuse the program,
    /// which is where the fix belongs.</para>
    /// </summary>
    [Test]
    public void AnUnqualifiedInstanceCall_FromASharedMember_IsNotRewrittenToThis()
    {
        var js = JsTestSupport.Compile("""
            Class Box
             Public Function Inst() As Integer
              Return 5
             End Function
             Public Shared Function Read() As Integer
              Return Inst()
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Read()))
             End Sub
            End Module
            """);

        Assert.That(js, Does.Not.Contain("this.Inst"),
            "`this` is the CLASS inside a static — a this.Inst() call would be a TypeError:\n" + js);
    }

    /// <summary>
    /// ⚠ Inside the class, the class's own Shared method WINS over a module function of the same
    /// name — VB's scoping, and the half this change is responsible for.
    ///
    /// <para>⛔ The module-level half is a SEPARATE PRE-EXISTING defect and is NOT fixed here:
    /// a module function whose name collides with a class method is dropped from emission
    /// entirely. Measured identically before and after this change (zero <c>function Tag</c>
    /// emitted), and broken on MSIL too (<c>MissingMethodException</c>). Only the in-class
    /// resolution is asserted, so this test cannot be read as a claim that the collision works.</para>
    /// </summary>
    [Test]
    public void InsideTheClass_ASharedMethodWins_OverAModuleFunctionOfTheSameName()
    {
        var js = JsTestSupport.Compile("""
            Class Box
             Public Shared Function Tag() As Integer
              Return 1
             End Function
             Public Function Read() As Integer
              Return Tag()
             End Function
            End Class

            Module M
             Function Tag() As Integer
              Return 2
             End Function
             Sub Main()
              Dim c As New Box()
              PrintLine(CStr(c.Read()))
             End Sub
            End Module
            """);

        Assert.That(js, Does.Contain("Box.Tag()"),
            "the nearer declaration — the class's own Shared method — wins:\n" + js);
    }
}
