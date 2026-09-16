using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The third and last site of the numeric coercion — passing an argument — after
/// <see cref="ReturnCoercionTests"/> (returns) and <see cref="AssignmentCoercionTests"/> (stores).
///
/// <para>⛔ <b>I deferred this twice on the grounds that it "needs overload resolution". That was
/// wrong twice over.</b> The analyzer ALREADY resolves the callee and records its parameter list —
/// the same <c>Symbol.Parameters</c> both argument loops were reading <c>IsByRef</c> from. And
/// BasicLang has no overloading at all to resolve: a second <c>Sub Show</c> is rejected with
/// "Subroutine 'Show' is already defined in this scope".</para>
///
/// <para>Measured for <c>Take(7 / 2)</c> and friends, before the fix:</para>
/// <list type="bullet">
/// <item><b>C#</b> — eight CS1503s. Does not build.</item>
/// <item><b>MSIL</b> — <c>MissingMethodException: Take(Double)</c>: the call site spells its
/// signature from the ARGUMENT's type, so it looks for a method nobody declared.</item>
/// <item><b>JavaScript</b> — <c>3.5</c> at every site.</item>
/// <item><b>C++</b> — right everywhere, by narrowing implicitly.</item>
/// </list>
///
/// <para>⚠ FIVE call shapes, reached through THREE different arms of
/// <c>Visit(CallExpressionNode)</c> plus <c>Visit(NewExpressionNode)</c>. Patching the obvious two
/// left <c>Box.Shr(7 / 2)</c> pushing a float64 at a correctly-spelled <c>Box::Shr(int32)</c>,
/// which the CLR rejects as an invalid program — a static member call reaches neither the
/// plain-identifier arm nor the instance arm.</para>
/// </summary>
[TestFixture]
public class ArgumentCoercionTests
{
    /// <summary>
    /// Every argument site except the <c>Shared</c> one, which JavaScript cannot lower for an
    /// unrelated pre-existing reason (see <see cref="ASharedMethodArgument_IsCoerced"/>).
    /// </summary>
    private const string FourSitesProgram = """
        Class Box
         Public Sub Inst(n As Integer)
          PrintLine("inst=" & CStr(n))
         End Sub
         Public Sub New(n As Integer)
          PrintLine("ctor=" & CStr(n))
         End Sub
        End Class

        Module M
         Sub Take(n As Integer)
          PrintLine("mod=" & CStr(n))
         End Sub
         Function Ret(n As Integer) As Integer
          Return n
         End Function
         Sub Main()
          Take(7 / 2)
          Dim b As New Box(7 / 2)
          b.Inst(7 / 2)
          PrintLine("fn=" & CStr(Ret(7 / 2)))
         End Sub
        End Module
        """;

    /// <summary>⛔ Eight CS1503s before the fix — the emitted C# did not build.</summary>
    [Test]
    public void TheEmittedCSharp_Compiles_AtEveryArgumentSite()
    {
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(FourSitesProgram), Is.Empty,
            "every CS1503 here is one argument site still passing the wrong type");
    }

    [Test]
    [Category("Integration")]
    public void JavaScript_NarrowsAtEveryArgumentSite()
    {
        Assert.That(JavaScriptExecutionTests.RunJs(FourSitesProgram),
            Is.EqualTo("mod=3\nctor=3\ninst=3\nfn=3"));
    }

    /// <summary>
    /// ⛔ MSIL could not even BIND the call: it spells the signature from the argument's type, so
    /// <c>Take(7 / 2)</c> looked for <c>Void Combined.Take(Double)</c>.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void Msil_PassesTheDeclaredType_InsteadOfFailingToBind()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess(FourSitesProgram),
            Is.EqualTo("mod=3\nctor=3\ninst=3\nfn=3\n"));
    }

    /// <summary>
    /// ⛔ The third arm. A <c>Shared</c> method reached as <c>Box.Shr(…)</c> goes through the
    /// STATIC member arm of <c>Visit(CallExpressionNode)</c> — not the instance arm, not the
    /// plain-identifier arm. Measured with only the other two patched: MSIL emitted
    /// <c>call void Box::Shr(int32)</c> (the signature right, because it is spelled from the
    /// declaration) with a <b>float64</b> on the stack, and the CLR rejected the whole program.
    ///
    /// <para>⚠ MSIL only. JavaScript has no lowering for a Shared method on a user class
    /// ("neither declared by this program nor supported by JavaScriptStdLib") and C++ emits an
    /// undeclared identifier for it — both pre-existing gaps unrelated to coercion.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedMethodArgument_IsCoerced()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess("""
            Class Box
             Public Shared Sub Shr(n As Integer)
              PrintLine("shr=" & CStr(n))
             End Sub
            End Class

            Module M
             Sub Main()
              Box.Shr(7 / 2)
             End Sub
            End Module
            """), Is.EqualTo("shr=3\n"));
    }

    /// <summary>
    /// ⛔ <b>ByRef must NOT be coerced.</b> A coerced argument is a NEW value, so the callee would
    /// write back into a temporary and the caller's variable would never change — trading a build
    /// error for a silently dropped mutation, which is a strictly worse failure.
    ///
    /// <para>⛔ The MISMATCHED shape is what discriminates, and the first version of this test
    /// missed it: with <c>ByRef n As Integer</c> and an Integer argument the types agree, so the
    /// coercion is a no-op and the guard never fires. It takes <c>ByRef n As Double</c> with an
    /// Integer argument. Measured with the guard removed, the C++ call site goes from
    /// <c>Bump(v)</c> to <c>Bump(t0)</c> — and where <c>Bump(v)</c> does not compile (a
    /// pre-existing ByRef type-mismatch gap), <c>Bump(t0)</c> COMPILES, RUNS and prints
    /// <b>41</b>: the increment written back into a temporary nobody reads. A build error traded
    /// for a silently dropped mutation.</para>
    ///
    /// <para>⚠ Structural, because that is where the property lives. JavaScript refuses ByRef
    /// outright (BL7002) and MSIL fails a ByRef call with InvalidProgramException both before and
    /// after this change — neither is coercion's doing, and neither can show this.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AByRefArgument_IsNotCoerced_SoTheWriteBackSurvives()
    {
        // Types AGREE: the write-back works end to end, and must keep working.
        const string matching = """
            Module M
             Sub Bump(ByRef n As Integer)
              n = n + 1
             End Sub
             Sub Main()
              Dim v As Integer = 41
              Bump(v)
              PrintLine(CStr(v))
             End Sub
            End Module
            """;

        // Types DISAGREE: the coercion must still keep its hands off the argument.
        const string mismatched = """
            Module M
             Sub Bump(ByRef n As Double)
              n = n + 1
             End Sub
             Sub Main()
              Dim v As Integer = 41
              Bump(v)
              PrintLine(CStr(v))
             End Sub
            End Module
            """;

        var cpp = BclE2E.CompileToCppOptimized(mismatched);

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(matching), Is.Empty);
            Assert.That(ReturnCoercionTests.EmitCSharpForTest(matching), Does.Contain("ref "),
                "the matching case must still pass by reference");
            Assert.That(cpp, Does.Contain("Bump(v)"),
                "a mismatched ByRef must still pass the CALLER'S variable; Bump(t0) compiles, "
                + "runs, and throws the write-back away:\n" + cpp);
        });
    }

    /// <summary>
    /// ⚠ The guard, at an argument. <c>PrintLine</c> and <c>CStr</c> both take <c>Object</c>, so
    /// every program in this fixture already exercises the non-numeric path — but only
    /// incidentally. This asserts it directly: an Object parameter must not acquire a numeric
    /// cast, which on JavaScript is the difference between building and not (a Bitcast has no
    /// numeric lowering there).
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ANonNumericParameter_IsLeftAlone()
    {
        const string program = """
            Module M
             Sub Describe(o As Object)
              PrintLine("got:" & CStr(o))
             End Sub
             Sub Label(s As String)
              PrintLine("lbl:" & s)
             End Sub
             Sub Main()
              Describe(7)
              Label("hi")
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("got:7\nlbl:hi"));
        });
    }

    /// <summary>
    /// ⚠ A constructor is the one site with NO resolved symbol — measured, <c>GetNodeSymbol</c> is
    /// null on a <c>NewExpressionNode</c> — so its parameter types come from the IR class's own
    /// constructors, selected by ARGUMENT COUNT. This pins that the selection is real: two
    /// constructors of different arity, each coercing its own arguments.
    ///
    /// <para>⛔ The first version of this test used two constructors of the SAME arity, to pin the
    /// ambiguity guard. That shape does not survive semantic analysis: the analyzer does not
    /// resolve constructor overloads at all, binding <c>New Box(3)</c> to the LAST declared one
    /// and then rejecting the argument ("Argument 1 of type 'Integer' is not compatible with
    /// parameter 's' of type 'String'"). So the ambiguity branch is unreachable today. It is kept
    /// anyway, and that is a deliberate exception to the rule that untestable code comes out: it
    /// makes the coercion do NOTHING in the ambiguous case, so unlike a speculative arm that acts,
    /// it cannot produce a wrong answer if the front end ever learns constructor overloads.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ConstructorArgumentsAreCoerced_PerConstructorArity()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess("""
            Class Box
             Public Sub New(n As Integer)
              PrintLine("one=" & CStr(n))
             End Sub
             Public Sub New(a As Integer, b As Integer)
              PrintLine("two=" & CStr(a) & "," & CStr(b))
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New Box(7 / 2)
              Dim q As New Box(7 / 2, 9 / 2)
             End Sub
            End Module
            """), Is.EqualTo("one=3\ntwo=3,4\n"));
    }
}
