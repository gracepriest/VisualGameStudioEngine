using System;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A <c>Const</c> declared inside a Class.
///
/// <para>⛔ It did not PARSE. <c>Private Const K As Integer = 9</c> inside a Class was
/// "Unexpected token in class: 'Const'" — while the suggestion that same error throws has always
/// listed <c>Const</c> among a Class's valid members. <c>ParseClassMember</c> had arms for
/// Property, Event, Operator, Function, Sub, Dim, a bare-identifier field and every nested type,
/// and none for Const; the module-level member parser has had the identical three-line arm all
/// along.</para>
///
/// <para>⛔ A PARSER-ONLY fix would have been WORSE THAN THE ERROR, which is why this change goes
/// further. Measured with only the parser arm added: the constant reached
/// <c>Visit(ConstantDeclarationNode)</c> with no current function, took its MODULE-SCOPE branch,
/// and was emitted as a GLOBAL. On C++ that put <c>int32_t K = 9;</c> after the class, so a method
/// reading it failed with "use of undeclared identifier 'K'" — and two classes each declaring
/// <c>Const K</c> emitted two globals of that name, "redefinition of 'K'". A class constant is a
/// MEMBER; the flat global table has no room for that.</para>
///
/// <para>⚠ So the IR builder lowers it to a STATIC FIELD carrying the folded value — what a VB
/// class Const is, one per type rather than per instance. That reuses the static-member path every
/// backend already has (C++'s out-of-class definition, MSIL's type initializer) instead of
/// teaching each one a new member kind, and the value goes through the SAME
/// <c>BuildConstantFieldInitializer</c> every other field uses, so a Const and a field agree about
/// what counts as constant — including refusing the same things.</para>
///
/// <para>⚠ It stays a <c>ConstantDeclarationNode</c> rather than being desugared to a Shared field
/// in the parser, because CONSTNESS IS REAL and enforced: assigning to a module or local Const is
/// already "Cannot assign to constant", and a class Const that quietly became a writable static
/// field would be the one scope where that check vanished. Asserted below.</para>
///
/// <para>⛔ TWO shapes inherited PRE-EXISTING <c>Shared</c> defects and were NOT this change's,
/// each verified on a plain <c>Shared</c> field with this change stashed. <b>BOTH have since been
/// FIXED</b>, each in the backend that owned the defect rather than here:</para>
///
/// <list type="bullet">
/// <item><b>JavaScript read it as <c>undefined</c> — FIXED.</b> The class emitted
/// <c>static K = 9;</c> and the method read <c>this.K</c>, which is undefined for a static in JS.
/// It was the JS backend's static lowering rather than the Const path, exactly as recorded, and
/// fixing that there fixed this: a class Const now emits <c>return Box.K;</c> and runs 9, so the
/// headline test below asserts all FOUR backends. See <c>JavaScriptSharedMemberTests</c>.</item>
/// <item><b>Reading it from OUTSIDE (<c>Box.K</c>) did not compile on C++ — FIXED.</b> It emitted
/// <c>t0 = Box-&gt;K;</c>, "'Box' does not refer to a value" — the class name treated as an object.
/// Identical for a plain <c>Shared</c> field, which is what it always was: the long-recorded
/// C++ Shared-access gap, not a Const one. Qualified access now lowers to <c>t0 = Box::K;</c> and
/// runs 9; <c>CppSharedAccessTests.AClassConstant_IsReadableFromOutside</c> asserts this exact
/// shape, so it is covered there rather than duplicated here.</item>
/// </list>
/// </summary>
[TestFixture]
public class ClassConstantTests
{
    private static string Program(string member, string body) => $"""
        Class Box
         {member}
         Public Function Read() As Integer
          {body}
         End Function
        End Class

        Module M
         Sub Main()
          Dim c As New Box()
          PrintLine(CStr(c.Read()))
         End Sub
        End Module
        """;

    /// <summary>
    /// ⛔ The headline: a class Const read from a method in that class. This did not parse at all
    /// before.
    ///
    /// <para>⚠ ALL FOUR backends. JavaScript was excluded here for the pre-existing static-read
    /// defect recorded in the fixture header — the class Const emitted correctly and the method
    /// then read <c>this.K</c>, so the constant reached the program and was lost on the way out.
    /// That is fixed in the JS backend (<c>JavaScriptSharedMemberTests</c>), so the exclusion is
    /// gone and JS is asserted with the rest. Running them, rather than only compiling, is what
    /// distinguishes a constant that reaches the program from one that is declared and lost.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Private Const K As Integer = 9", TestName = "Private")]
    [TestCase("Public Const K As Integer = 9", TestName = "Public")]
    public void AClassConstant_IsReadableFromAMethod(string member)
    {
        var program = Program(member, "Return K");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("9\n"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("9\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("9"));
        });
    }

    /// <summary>
    /// ⛔ The sharp one. With the parser arm alone, two classes each declaring <c>Const K</c>
    /// emitted two globals called <c>K</c> and C++ refused the file with "redefinition of 'K'".
    /// This is what proves the constant became a MEMBER rather than a global — and no run-only
    /// test can show it, because the program never built.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TwoClassesMayDeclareTheSameConstantName()
    {
        const string program = """
            Class A
             Public Const K As Integer = 1
             Public Function Read() As Integer
              Return K
             End Function
            End Class

            Class B
             Public Const K As Integer = 2
             Public Function Read() As Integer
              Return K
             End Function
            End Class

            Module M
             Sub Main()
              Dim a As New A()
              Dim b As New B()
              PrintLine(CStr(a.Read()) & "," & CStr(b.Read()))
             End Sub
            End Module
            """;

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("1,2\n"));
    }

    /// <summary>
    /// ⛔ The emitted C++ TEXT, because "is a member" is the property and a run only sees its
    /// effect. A class Const must be a static MEMBER with an out-of-class definition — never a
    /// namespace-scope global, which is what it was with the parser arm alone.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AClassConstant_IsAStaticMember_NotAGlobal()
    {
        var cpp = BclE2E.CompileToCppOptimized(Program("Public Const K As Integer = 9", "Return K"));

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("static int32_t K;"),
                "the in-class declaration:\n" + cpp);
            Assert.That(cpp, Does.Contain("int32_t Box::K = 9;"),
                "and its out-of-class definition:\n" + cpp);
            Assert.That(cpp, Does.Not.Contain("\nint32_t K = 9;"),
                "it must NOT also be emitted as a namespace-scope global:\n" + cpp);
        });
    }

    /// <summary>
    /// ⚠ The value goes through the same folding every other field initializer uses, so a
    /// constant EXPRESSION works and is not restricted to a bare literal.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AClassConstant_TakesAFoldedExpression()
    {
        var program = Program("Public Const K As Integer = 2 + 3", "Return K");

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
    }

    /// <summary>
    /// ⚠ And it REFUSES exactly what a field initializer refuses, through the same helper — a
    /// value needing code to run is a diagnostic, not a silently dropped constant.
    /// </summary>
    [Test]
    public void AClassConstant_NeedingRuntimeCode_IsRefused()
    {
        var ex = Assert.Throws<Exception>(() => JsTestSupport.BuildModule("""
            Class Box
             Public Const K As Integer = Helper()
            End Class

            Module M
             Function Helper() As Integer
              Return 4
             End Function
             Sub Main()
              PrintLine("ok")
             End Sub
            End Module
            """));

        Assert.That(ex.Message, Does.Contain("cannot be computed at compile time"), ex.Message);
    }

    /// <summary>
    /// ⛔ CONSTNESS SURVIVES. This is why the parser keeps a ConstantDeclarationNode instead of
    /// desugaring to a Shared field: assigning to a module or local Const is already refused, and
    /// a class Const must be refused the same way rather than becoming the one writable scope.
    /// </summary>
    [Test]
    public void AssigningToAClassConstant_IsRefused()
    {
        // ⚠ OptionalConstructorTests.Analyze, not CompileEmittedCSharpForTest: that helper asserts
        // analysis SUCCEEDS, which is the opposite of what this pins. Reused rather than copied so
        // there is one way to ask the analyzer for its diagnostics.
        var errors = OptionalConstructorTests.Analyze("""
            Class Box
             Public Const K As Integer = 9
             Public Sub Bump()
              K = 5
             End Sub
            End Class

            Module M
             Sub Main()
              PrintLine("ok")
             End Sub
            End Module
            """);

        Assert.That(errors, Has.Some.Contains("Cannot assign to constant"),
            string.Join("\n", errors));
    }
}
