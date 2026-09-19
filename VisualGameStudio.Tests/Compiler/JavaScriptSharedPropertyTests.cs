using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Shared</c> PROPERTIES on the JavaScript backend, whose accessors were emitted without
/// <c>static</c>.
///
/// <para>⛔ <c>EmitProperty</c> consulted <c>prop.IsStatic</c> for an AUTO-property — which becomes
/// a plain field — but not for explicit accessors. So a <c>Shared</c> property got INSTANCE
/// accessors, and nothing reached them: reading <c>Box.P</c> answered <b>undefined</b> (the getter
/// lives on the prototype, not on the class), and <c>Box.P = 7</c> never called the setter at all —
/// it quietly created a plain own-property on the class object.</para>
///
/// <para>⛔ A READ-WRITE Shared property therefore LOOKED CORRECT WHILE DOING NOTHING, which is
/// why the decisive test below counts setter calls instead of just reading the value back. The
/// write created <c>Box.P</c> and the read handed that same value straight back, so the property
/// appeared to work; meanwhile the backing field was never touched and the setter never ran.
/// Measured on the unpatched backend: value / backing field / call count came out
/// <c>8|0|0</c> where every other working backend gives <c>8|8|2</c>. The leading 8 is the whole
/// trap — it is the accidental own-property, not the property.</para>
///
/// <para>⚠ MSIL is the oracle here and agrees on every shape below. C++ is not asserted: it cannot
/// compile a qualified <c>Shared</c> access at all (<c>'Box' does not refer to a value</c>), a
/// long-recorded pre-existing gap, so it cannot serve as one.</para>
///
/// <para>⚠ The INHERITED case asserts JavaScript alone, deliberately — it is still broken on MSIL
/// (<c>NullReferenceException</c>), so asserting MSIL there would pin a defect as the contract.</para>
///
/// <para>⛔ A SEPARATE AND MORE SEVERE DEFECT IS PINNED AT THE BOTTOM, not fixed: an assignment
/// whose right-hand side COMPUTES something and does not mention the target field is silently
/// discarded. It is not a property bug — it hits ordinary methods, instance fields included — and
/// it is why the setters in these tests are written as a plain copy plus a self-referencing
/// increment, both of which lower correctly. See that test for the measurements.</para>
/// </summary>
[TestFixture]
public class JavaScriptSharedPropertyTests
{
    /// <summary>
    /// ⛔ THE decisive case. Counts setter CALLS and reads the BACKING FIELD, because reading the
    /// property alone cannot tell a working accessor from a plain own-property that happens to hold
    /// the last value written. Measured <c>8|0|0</c> before, <c>8|8|2</c> after and on MSIL.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedPropertysSetter_ActuallyRuns()
    {
        const string program = """
            Class Box
             Private Shared _v As Integer = 0
             Public Shared Calls As Integer = 0
             Public Shared Property P As Integer
              Get
               Return _v
              End Get
              Set(value As Integer)
               _v = value
               Calls = Calls + 1
              End Set
             End Property
             Public Shared Function Raw() As Integer
              Return _v
             End Function
            End Class

            Module M
             Sub Main()
              Box.P = 7
              Box.P = 8
              PrintLine(CStr(Box.P) & "|" & CStr(Box.Raw()) & "|" & CStr(Box.Calls))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("8|8|2"),
                "value | backing field | setter call count — a bypassed setter gives 8|0|0");
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("8|8|2"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("8|8|2\n"));
        });
    }

    /// <summary>⛔ The plainest symptom: a Shared ReadOnly property read from outside was undefined.</summary>
    [Test]
    [Category("Integration")]
    public void ASharedReadOnlyProperty_IsReadableFromOutside()
    {
        const string program = """
            Class Box
             Private Shared _v As Integer = 9
             Public Shared ReadOnly Property P As Integer
              Get
               Return _v
              End Get
             End Property
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.P))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("9"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>
    /// ⚠ WriteOnly is the shape with no getter to mask the defect — it printed the field's initial
    /// value, 0, because the setter was never reached.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedWriteOnlyPropertys_SetterReachesTheField()
    {
        const string program = """
            Class Box
             Public Shared K As Integer = 0
             Public Shared WriteOnly Property P As Integer
              Set(value As Integer)
               K = value
              End Set
             End Property
            End Class

            Module M
             Sub Main()
              Box.P = 7
              PrintLine(CStr(Box.K))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7"), "was 0");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("7\n"));
        });
    }

    /// <summary>⚠ Read unqualified from an INSTANCE method — a different resolution path to the same accessor.</summary>
    [Test]
    [Category("Integration")]
    public void ASharedProperty_IsReadableUnqualifiedFromAnInstanceMethod()
    {
        const string program = """
            Class Box
             Private Shared _v As Integer = 9
             Public Shared ReadOnly Property P As Integer
              Get
               Return _v
              End Get
             End Property
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

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>⚠ And from a <c>Shared</c> method, where <c>this</c> is the class.</summary>
    [Test]
    [Category("Integration")]
    public void ASharedProperty_IsReadableFromASharedMethod()
    {
        const string program = """
            Class Box
             Private Shared _v As Integer = 9
             Public Shared ReadOnly Property P As Integer
              Get
               Return _v
              End Get
             End Property
             Public Shared Function Read() As Integer
              Return P
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
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
        });
    }

    /// <summary>
    /// ⚠ An inherited Shared property resolves through the static side of the prototype chain.
    ///
    /// <para>⛔ JAVASCRIPT ALONE, deliberately: this shape is still broken on MSIL
    /// (<c>NullReferenceException</c>, measured), so asserting MSIL would pin a defect.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnInheritedSharedProperty_IsReadableOnTheDerivedName()
    {
        const string program = """
            Class Base
             Private Shared _v As Integer = 4
             Public Shared ReadOnly Property P As Integer
              Get
               Return _v
              End Get
             End Property
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Derived.P))
             End Sub
            End Module
            """;

        Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("4"), "was undefined");
    }

    /// <summary>⛔ The emitted TEXT: both accessors carry <c>static</c>, which is the defect itself.</summary>
    [Test]
    public void TheEmittedAccessors_AreStatic()
    {
        var js = JsTestSupport.Compile("""
            Class Box
             Private Shared _v As Integer = 0
             Public Shared Property P As Integer
              Get
               Return _v
              End Get
              Set(value As Integer)
               _v = value
              End Set
             End Property
            End Class

            Module M
             Sub Main()
              Box.P = 7
              PrintLine(CStr(Box.P))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("static get P()"), js);
            Assert.That(js, Does.Contain("static set P(value)"), js);
        });
    }

    // ------------------------------------------------------------------ regression guards

    /// <summary>
    /// ⚠ THE guard that matters most: an INSTANCE property must NOT become static. Marking every
    /// accessor static would break every ordinary property, so this asserts the emitted text AND
    /// that two instances stay independent.
    ///
    /// <para>⛔ C++ is NOT asserted, and not for want of trying: it does not emit an explicit
    /// property as a member at all — the generated code says <c>no member named 'P' in 'Box'</c>
    /// and does not compile. A pre-existing gap, measured on this exact program.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnInstanceProperty_StaysNonStatic_AndPerInstance()
    {
        const string program = """
            Class Box
             Private _v As Integer = 0
             Public Property P As Integer
              Get
               Return _v
              End Get
              Set(value As Integer)
               _v = value
              End Set
             End Property
            End Class

            Module M
             Sub Main()
              Dim a As New Box()
              Dim b As New Box()
              a.P = 7
              PrintLine(CStr(a.P) & "," & CStr(b.P))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            var js = JsTestSupport.Compile(program);
            Assert.That(js, Does.Contain("get P()"), js);
            Assert.That(js, Does.Not.Contain("static get P()"),
                "an instance accessor must not be static:\n" + js);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7,0"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("7,0"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("7,0\n"));
        });
    }

    /// <summary>
    /// ⚠ A Shared AUTO-property keeps working. This is the arm that ALREADY consulted
    /// <c>IsStatic</c> — it lowers to a plain static field, not to accessors — so it is the
    /// baseline proving the fix changed only the explicit-accessor path.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedAutoProperty_StillWorks()
    {
        const string program = """
            Class Box
             Public Shared Property P As Integer
            End Class

            Module M
             Sub Main()
              Box.P = 5
              PrintLine(CStr(Box.P))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("5\n"));
        });
    }

    /// <summary>
    /// ⚠ A <c>Shared</c> accessor body is emitted in STATIC CONTEXT, so an unqualified call to an
    /// INSTANCE sibling is not rewritten to <c>this.Inst()</c> — inside a JS static <c>this</c> is
    /// the class, and that call would be a TypeError wearing the shape of working code.
    ///
    /// <para>⛔ This is the same invalid-VB shape pinned for METHODS (BC30469, "Reference to a
    /// non-shared member requires an object reference") that the front end wrongly accepts. The
    /// accessor path needs its own case: the static-context flag is passed separately for
    /// properties, and dropping it left every other test in this fixture GREEN — measured, which
    /// is how this gap was found.</para>
    /// </summary>
    [Test]
    public void ASharedAccessorBody_IsEmittedInStaticContext()
    {
        var js = JsTestSupport.Compile("""
            Class Box
             Public Function Inst() As Integer
              Return 5
             End Function
             Public Shared ReadOnly Property P As Integer
              Get
               Return Inst()
              End Get
             End Property
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.P))
             End Sub
            End Module
            """);

        Assert.That(js, Does.Not.Contain("this.Inst"),
            "`this` is the CLASS inside a static accessor — this.Inst() would be a TypeError:\n" + js);
    }

    // ------------------------------------------------------- pin on a SEPARATE, unfixed defect

    /// <summary>
    /// ⛔ PINNED AS BROKEN — a DIFFERENT defect, more severe than the one this fixture fixes, and
    /// deliberately NOT fixed here.
    ///
    /// <para>An assignment whose right-hand side COMPUTES something and does not mention the
    /// target field is SILENTLY DISCARDED. <c>_v = value * 2</c> emits
    /// <c>const _v = (value &lt;&lt; 1);</c> — a fresh local — so the write goes nowhere and the
    /// field keeps its old value. Nothing fails; the program prints a plausible number.</para>
    ///
    /// <para>⛔ IT IS NOT A PROPERTY BUG. Measured in an ordinary method, on an INSTANCE field as
    /// well as a Shared one: <c>K = p * 2</c> emits <c>const K = (p &lt;&lt; 1)</c> and prints the
    /// old value. The trigger is precise — <c>K = 7</c>, <c>K = p</c>, <c>K = 3 * 2</c> and
    /// <c>K = K + 1</c> all lower CORRECTLY; only a computed right-hand side that survives folding
    /// and does not name the target is lost.</para>
    ///
    /// <para>⛔ AND IT IS THE OPTIMIZER THAT CAUSES IT — the sharpest part of this finding, and the
    /// reason a green suite never caught it. The NON-OPTIMIZING path lowers this correctly and
    /// answers <c>14|14</c>. Under the standard passes every shipping route runs, <c>value * 2</c>
    /// is strength-reduced to <c>value &lt;&lt; 1</c>, the rewritten value loses the marking that
    /// says it is named after a variable, and <c>Bind</c> then treats it as a temp and declares a
    /// <c>const</c>. Both paths are asserted below precisely because they DISAGREE; asserting only
    /// the non-optimizing one would have reported this as working. This is exactly the hazard
    /// CLAUDE.md names.</para>
    ///
    /// <para>⛔ AND IT IS NOT JAVASCRIPT-ONLY. C++ (14) and MSIL (14) are both correct; JavaScript
    /// discards the write under the optimizer, and C# emits an <b>empty method body</b> — the
    /// statement vanishes entirely, on both paths. Two backends right, two wrong in different
    /// ways.</para>
    ///
    /// <para>⚠ This test pins what each path ACTUALLY DOES today, so the gap cannot be mistaken for
    /// working. The optimized assertion goes RED when the defect is fixed, which is the signal to
    /// change it to <c>14|14</c> rather than delete it. Note the accessor fix in this PR moves the
    /// optimized answer from <c>7|0</c> to <c>0|0</c>: both are wrong, and the old <c>7</c> was the
    /// bypassed-setter illusion rather than a working write.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASetterWithAComputedBody_IsDiscardedByTheOptimizer_SeparateDefect()
    {
        const string program = """
            Class Box
             Private Shared _v As Integer = 0
             Public Shared Property P As Integer
              Get
               Return _v
              End Get
              Set(value As Integer)
               _v = value * 2
              End Set
             End Property
             Public Shared Function Raw() As Integer
              Return _v
             End Function
            End Class

            Module M
             Sub Main()
              Box.P = 7
              PrintLine(CStr(Box.P) & "|" & CStr(Box.Raw()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("14|14"),
                "the NON-OPTIMIZING path is CORRECT — which is why a green suite missed this");
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("0|0"),
                "PINNED DEFECT: under the standard passes every shipping route runs, the "
                + "strength-reduced write is discarded. Correct is 14|14 — see the doc comment. "
                + "If this now reads 14|14 the defect is fixed: change this expectation rather "
                + "than deleting the test.");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("14|14\n"),
                "MSIL gets this right on both paths, which is what makes it a JS/C# defect");
        });
    }
}
