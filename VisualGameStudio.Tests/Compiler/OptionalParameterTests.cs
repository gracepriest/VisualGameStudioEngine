using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// What happens when a call leaves a trailing <c>Optional</c> parameter out.
///
/// <para>⛔ <b>One backend of four was right, and it was right by accident.</b> The C# backend
/// emits the default into the SIGNATURE (<c>int b = 5</c>) and lets csc fill it, so nothing in
/// the compiler ever produced the value. Measured for the program below, before the fix:</para>
///
/// <list type="bullet">
/// <item><b>C#</b> — <c>before:1,5 after:2,5 fnb:8 fna:9 inst:5,5</c>. Correct.</item>
/// <item><b>JavaScript</b> — <c>before:1,undefined after:2,undefined fnb:0 fna:0
/// inst:5,undefined</c>. The emitted function is <c>function Before(a, b)</c>, with no default,
/// and the two Functions printed <b>0</b>: <c>a + undefined</c> is NaN, which <c>CStr</c> renders
/// as 0 — a wrong answer that looks like a plausible one.</item>
/// <item><b>C++</b> — five compile errors: four "too few arguments to function" and
/// <c>no matching function for call to 'Box::Inst(int)'</c>. Does not build.</item>
/// <item><b>MSIL</b> — <c>MissingMethodException: Void Combined.Before(Int32)</c>. The call site
/// spells its signature from the ARGUMENTS, so it looked for a method nobody declared.</item>
/// </list>
///
/// <para>⚠ The fix fills the omitted arguments at the CALL, not at the declaration. A
/// declaration-side fix is three separate per-backend mechanisms (a JS default parameter, a C++
/// default argument, an MSIL <c>[opt]</c> + <c>.param</c> plus a caller that knows to use it);
/// the call site is one place, and the C# backend then passes the value explicitly instead of
/// relying on csc — the same program either way.</para>
///
/// <para>⚠ WHICH symbol carries the default depends on where the callee is declared, and all four
/// analyzer sites that record it were proved load-bearing by ablation — see the table on
/// <c>Symbol.DefaultValueExpression</c>. That is why this fixture calls a callee declared before
/// the caller, one declared after, and one in another FILE: they are three different resolution
/// paths, and patching only the obvious one leaves the other two silently broken.</para>
/// </summary>
[TestFixture]
public class OptionalParameterTests
{
    /// <summary>
    /// Five call shapes over two arms of <c>Visit(CallExpressionNode)</c> and two different symbol
    /// sources: a Sub and a Function declared BEFORE the caller (whose defaults come from the
    /// declaration's own parameter symbols), a Sub and a Function declared AFTER it (from the
    /// signature pre-pass's), and an instance method on a class. The third arm — a <c>Shared</c>
    /// method — is <see cref="ASharedMethodCall_FillsItsDefault"/>, because only MSIL can run one.
    /// </summary>
    private const string FiveShapesProgram = """
        Class Box
         Public Sub Inst(a As Integer, Optional b As Integer = 5)
          PrintLine("inst:" & CStr(a) & "," & CStr(b))
         End Sub
        End Class

        Module M
         Sub Before(a As Integer, Optional b As Integer = 5)
          PrintLine("before:" & CStr(a) & "," & CStr(b))
         End Sub
         Function FnBefore(a As Integer, Optional b As Integer = 5) As Integer
          Return a + b
         End Function
         Sub Main()
          Before(1)
          After(2)
          PrintLine("fnb:" & CStr(FnBefore(3)))
          PrintLine("fna:" & CStr(FnAfter(4)))
          Dim x As New Box()
          x.Inst(5)
         End Sub
         Sub After(a As Integer, Optional b As Integer = 5)
          PrintLine("after:" & CStr(a) & "," & CStr(b))
         End Sub
         Function FnAfter(a As Integer, Optional b As Integer = 5) As Integer
          Return a + b
         End Function
        End Module
        """;

    private const string FiveShapesExpected =
        "before:1,5\nafter:2,5\nfnb:8\nfna:9\ninst:5,5";

    /// <summary>
    /// ⛔ <c>before:1,undefined</c> at every site, and <b>0</b> from both Functions — <c>a + b</c>
    /// with <c>b</c> undefined is NaN, which <c>CStr</c> prints as 0. A wrong answer that looks
    /// like a plausible one.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void JavaScript_FillsTheOmittedDefault_InsteadOfUndefined()
    {
        Assert.That(JavaScriptExecutionTests.RunJs(FiveShapesProgram),
            Is.EqualTo(FiveShapesExpected));
    }

    /// <summary>
    /// ⛔ MSIL could not even BIND: <c>MissingMethodException: Void Combined.Before(Int32)</c>.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void Msil_FillsTheOmittedDefault_InsteadOfFailingToBind()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess(FiveShapesProgram),
            Is.EqualTo(FiveShapesExpected + "\n"));
    }

    /// <summary>
    /// ⛔ C++ did not compile at all — "too few arguments to function" at each of the four module
    /// calls and "no matching function for call to 'Box::Inst(int)'" at the instance one. This
    /// RUNS it rather than only emitting, because the emitted text alone never showed the defect:
    /// the declaration <c>void Before(int32_t a, int32_t b)</c> looks perfectly ordinary.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void Cpp_CompilesAndRuns_WithAnOmittedOptional()
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(FiveShapesProgram)),
            Is.EqualTo(FiveShapesExpected + "\n"));
    }

    /// <summary>
    /// ⚠ The one backend that was already right must STAY right. C# is the only place the change
    /// could regress rather than fix: it now receives an explicit argument for a parameter that
    /// also carries a default in its signature, which is legal but had to be checked rather than
    /// assumed.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_StillCompiles_AndPassesTheDefaultExplicitly()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(FiveShapesProgram);

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(FiveShapesProgram), Is.Empty);
            Assert.That(csharp, Does.Contain("Before(1, 5)"),
                "the value is produced by the compiler now, not left to csc:\n" + csharp);
        });
    }

    // ====================================================================================
    // The cross-FILE resolution path — its own symbol source, and unreachable from a single
    // source string.
    // ====================================================================================

    /// <summary>
    /// ⛔ A callee in ANOTHER FILE of the same project resolves to a SIBLING SIGNATURE symbol,
    /// built by <c>BuildSiblingSignatureParameters</c> from the other unit's AST — not to the
    /// symbol the declaration's own visit produces. Measured by ablation: with that one site left
    /// out and the other three in place, every shape above still passes and this call alone goes
    /// back to <c>Helpers.Greet(1)</c>.
    ///
    /// <para>⚠ It goes through <c>CompileProjectFiles</c> and its combined IR because that is the
    /// only way to reach the path — every other harness here takes one source string, and a single
    /// file cannot express "declared in another unit".</para>
    ///
    /// <para>⛔ STRUCTURAL, and it says less than the tests above, because a cross-file call
    /// reaches only ONE backend today. Measured: JavaScript refuses it outright ("no lowering for
    /// 'Helpers.Greet' … neither declared by this program nor supported by JavaScriptStdLib") and
    /// MSIL emits <c>call void Combined::HelpersGreet(int32, object)</c> against a method declared
    /// <c>void Greet(int32 a, int32 b)</c> — the qualified name mangled into the method name and
    /// the signature spelled from the arguments. Both are pre-existing cross-file gaps unrelated
    /// to Optional parameters. So on the one backend that can compile this, C#, the fill changes
    /// the emitted TEXT and not the behaviour (csc fills the same default from the signature); the
    /// assertion is that the IR a cross-file call produces matches the IR a same-file call
    /// produces, so the day those two backends learn cross-file calls they do not inherit a
    /// silently different one.</para>
    /// </summary>
    [Test]
    public void ACalleeInAnotherFile_GetsItsDefaultToo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bloptional-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Helpers.bas"), """
                Module Helpers
                 Public Sub Greet(a As Integer, Optional b As Integer = 5)
                  PrintLine("greet:" & CStr(a) & "," & CStr(b))
                 End Sub
                End Module
                """);
            File.WriteAllText(Path.Combine(dir, "Main.bas"), """
                Module App
                 Sub Main()
                  Greet(1)
                 End Sub
                End Module
                """);

            var result = new BasicCompiler().CompileProjectFiles(new[]
            {
                Path.Combine(dir, "Main.bas"),
                Path.Combine(dir, "Helpers.bas"),
            });

            Assert.That(result.AllErrors.Select(e => e.ToString()), Is.Empty);
            Assert.That(result.CombinedIR, Is.Not.Null);

            var csharp = new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR);

            Assert.That(csharp, Does.Contain("Helpers.Greet(1, 5)"),
                "with the sibling-signature site ablated this is Helpers.Greet(1):\n" + csharp);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    // ====================================================================================
    // What must NOT happen.
    // ====================================================================================

    /// <summary>
    /// ⚠ Filling starts at the FIRST omitted position, and a supplied argument is never replaced.
    /// Two optionals, called three ways.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void SuppliedArgumentsAreNeverReplaced()
    {
        const string program = """
            Module M
             Sub Two(a As Integer, Optional b As Integer = 5, Optional c As String = "z")
              PrintLine("two:" & CStr(a) & "," & CStr(b) & "," & c)
             End Sub
             Sub Main()
              Two(2)
              Two(2, 8)
              Two(2, 8, "q")
             End Sub
            End Module
            """;

        const string expected = "two:2,5,z\ntwo:2,8,z\ntwo:2,8,q";

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected),
                "before the fix: two:2,undefined,undefined / two:2,8,undefined / two:2,8,q");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
        });
    }

    /// <summary>
    /// ⛔ The filled default is COERCED to the parameter's declared type, exactly like an argument
    /// the caller wrote. It is not cosmetic: <c>Optional d As Double = 7</c> is an Integer literal,
    /// and MSIL stored the int32 bit pattern straight into a float64 slot — measured on the
    /// previous commit for the same shape reached through a variable, <c>d + 0.5</c> printed
    /// <b>3.5E-323</b>.
    ///
    /// <para>⚠ The default is a general EXPRESSION, not only a literal, so it is built through
    /// the same <c>BuildExpressionValue</c> the declaration site uses — <c>Optional e As Integer =
    /// 2 + 3</c> arrives as 5, where JavaScript printed <c>undefined</c> before.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheDefaultIsCoercedToItsParameterType_AndMayBeAnExpression()
    {
        const string program = """
            Module M
             Sub Dbl(a As Integer, Optional d As Double = 7)
              PrintLine("dbl:" & CStr(d + 0.5))
             End Sub
             Sub Expr(a As Integer, Optional e As Integer = 2 + 3)
              PrintLine("expr:" & CStr(e))
             End Sub
             Sub Main()
              Dbl(1)
              Expr(1)
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("dbl:7.5\nexpr:5"),
                "before the fix: dbl:NaN / expr:undefined");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program),
                Is.EqualTo("dbl:7.5\nexpr:5\n"),
                "7.5 and not 3.5E-323 — the filled Integer literal is widened to a real Double");
        });
    }

    /// <summary>
    /// ⚠ A <c>Shared</c> method on a user class is the third arm of <c>Visit(CallExpressionNode)</c>
    /// — neither the instance arm nor the plain-identifier one — and needed its own insertion.
    ///
    /// <para>⚠ MSIL only. JavaScript has no lowering for a Shared method on a user class ("neither
    /// declared by this program nor supported by JavaScriptStdLib") and C++ emits an undeclared
    /// identifier for it; both are pre-existing gaps unrelated to Optional parameters, already
    /// recorded by <see cref="ArgumentCoercionTests.ASharedMethodArgument_IsCoerced"/>.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedMethodCall_FillsItsDefault()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess("""
            Class Box
             Public Shared Sub Shr(a As Integer, Optional b As Integer = 5)
              PrintLine("shr:" & CStr(a) & "," & CStr(b))
             End Sub
            End Class

            Module M
             Sub Main()
              Box.Shr(6)
             End Sub
            End Module
            """), Is.EqualTo("shr:6,5\n"));
    }

    /// <summary>
    /// ⚠ A filled argument is added to <c>ByRefArguments</c> too, because <c>IRCall</c> documents
    /// the two as "indexed in lockstep" and three backends index one by the other's position.
    ///
    /// <para>⛔ STRUCTURAL because nothing else can see it. Measured with the flag not appended:
    /// every behavioural test in this fixture still passes, on all four backends — each consumer
    /// treats a MISSING entry as by-value, which is the right answer for a filled default, so the
    /// desync is invisible right up until a consumer stops guarding its index. This asserts the
    /// invariant itself rather than a symptom of breaking it.</para>
    /// </summary>
    [Test]
    public void AFilledArgumentKeepsTheByRefListInLockstep()
    {
        var module = JsTestSupport.BuildModule("""
            Module M
             Sub One(a As Integer, Optional b As Integer = 5)
              PrintLine(CStr(a) & "," & CStr(b))
             End Sub
             Sub Main()
              One(1)
             End Sub
            End Module
            """);

        var calls = module.Functions
            .SelectMany(f => f.Blocks)
            .SelectMany(b => b.Instructions)
            .OfType<BasicLang.Compiler.IR.IRCall>()
            .Where(c => c.FunctionName == "One")
            .ToList();

        Assert.That(calls, Has.Count.EqualTo(1), "expected exactly one call to One");
        Assert.That(calls[0].Arguments, Has.Count.EqualTo(2), "the default must be filled");
        Assert.That(calls[0].ByRefArguments, Has.Count.EqualTo(calls[0].Arguments.Count),
            "ByRefArguments is indexed in lockstep with Arguments");
        Assert.That(calls[0].ByRefArguments[1], Is.False,
            "a filled default is a fresh temporary — there is nothing to write back into");
    }

    // ====================================================================================
    // Two shapes this deliberately does NOT fix, pinned so they surface rather than drift.
    // ====================================================================================

    // ⚠ The pin that used to live here — a constructor with an omitted Optional being REFUSED by
    // the analyzer — is gone because that gap is closed. The shape now works, and
    // OptionalConstructorTests covers it; this fixture keeps only the call shapes.

    /// <summary>
    /// ⛔ <c>Optional ByRef</c> does not work on ANY backend, before or after this change, and is
    /// pinned rather than papered over. Measured on the four backends for
    /// <c>Sub Bump(a As Integer, Optional ByRef n As Integer = 5)</c> called as <c>Bump(1)</c>:
    /// C# emits <c>ref int n = 5</c>, which is CS1741; JavaScript refuses ByRef outright (BL7002);
    /// MSIL emits no <c>&amp;</c> at all and the CLR rejects the program; C++ went from "too few
    /// arguments" to "cannot bind non-const lvalue reference … to an rvalue", a different compile
    /// error for the same unusable shape.
    ///
    /// <para>⚠ So there is deliberately NO by-ref guard in the fill. A guard would change no
    /// observable behaviour on any backend — the declaration side is broken first — and would be
    /// code no test could kill. The shape needs the declaration fixed before the call site
    /// matters.</para>
    /// </summary>
    [Test]
    public void AnOptionalByRefParameter_IsBrokenOnTheDeclarationSideFirst()
    {
        const string program = """
            Module M
             Sub Bump(a As Integer, Optional ByRef n As Integer = 5)
              n = n + 1
             End Sub
             Sub Main()
              Bump(1)
             End Sub
            End Module
            """;

        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(program);

        Assert.That(errors, Has.Some.Contains("CS1741"),
            "a ref parameter cannot carry a default; until the DECLARATION is emitted differently, "
            + "what the call site passes cannot make this build:\n" + string.Join("\n", errors));
    }

}
