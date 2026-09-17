using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Calling a <c>Module</c> member by its qualified name.
///
/// <para>⛔⛔ <b>This was a runtime failure from a clean build.</b> <c>IRBuilder.Visit(ModuleNode)</c>
/// treats a module as purely organisational, so its members become TOP-LEVEL functions carrying
/// only a <c>ModuleName</c> string — there is no container in the IR. The call site kept the
/// qualifier, so <c>M.Go()</c> emitted a member call on an <c>M</c> that is declared nowhere:
/// <c>ReferenceError: M is not defined</c>, from a build that reported success and contained every
/// string a reader would look for.</para>
///
/// <para>⚠ <b>Every test here RUNS the emitted script.</b> A codegen assertion is what missed this
/// in the first place — `function Go()` and `M.Go()` both being present looks right until you
/// notice they contradict each other. One test below does assert on the text, and only to pin
/// which of the two shapes came out; the rest prove the program works.</para>
/// </summary>
[TestFixture]
public class JavaScriptModuleCallTests
{
    [Test]
    public void AQualifiedModuleCall_Runs()
    {
        Assert.That(JavaScriptExecutionTests.RunJs("""
            Public Module M
                Public Sub Go()
                    Console.WriteLine("called")
                End Sub
            End Module

            Sub Main()
                M.Go()
            End Sub
            """), Is.EqualTo("called"));
    }

    [Test]
    public void AQualifiedModuleCall_DropsTheQualifier()
    {
        // The one text assertion, pinning WHICH shape is emitted. `M` is not a value and never
        // becomes one, so the qualifier cannot survive into the output.
        var js = JsTestSupport.Compile("""
            Public Module M
                Public Sub Go()
                    Console.WriteLine("called")
                End Sub
            End Module

            Sub Main()
                M.Go()
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("Go();"));
            Assert.That(js, Does.Not.Contain("M.Go()"), "M is declared nowhere in the output");
        });
    }

    [Test]
    public void AQualifiedModuleFunction_ReturnsItsValue()
    {
        // A Function, not a Sub: the rewrite has to preserve the result, not just the call.
        Assert.That(JavaScriptExecutionTests.RunJs("""
            Public Module Maths
                Public Function Twice(n As Integer) As Integer
                    Return n * 2
                End Function
            End Module

            Sub Main()
                Console.WriteLine(Maths.Twice(21))
            End Sub
            """), Is.EqualTo("42"));
    }

    [Test]
    public void AnUnqualifiedModuleCall_StillRuns()
    {
        // The shape that always worked. It must keep working — the rewrite only adds a case.
        Assert.That(JavaScriptExecutionTests.RunJs("""
            Public Module M
                Public Sub Go()
                    Console.WriteLine("called")
                End Sub
            End Module

            Sub Main()
                Go()
            End Sub
            """), Is.EqualTo("called"));
    }

    [Test]
    public void ALocalSharingTheModulesName_IsNotRewritten()
    {
        // ⛔⛔ THE RISK OF THE REWRITE, and the reason it checks scope before anything else. A real
        // value named `M` is in scope here, so `M.Go()` is an ordinary member call on that object.
        // Rewriting it would silently redirect the program to a same-named free function — a wrong
        // answer rather than a crash, which is worse than the bug being fixed.
        Assert.That(JavaScriptExecutionTests.RunJs("""
            Public Module M
                Public Sub Go()
                    Console.WriteLine("module")
                End Sub
            End Module

            Public Class Thing
                Public Sub Go()
                    Console.WriteLine("object")
                End Sub
            End Class

            Sub Main()
                Dim M As New Thing()
                M.Go()
            End Sub
            """), Is.EqualTo("object"));
    }

    [Test]
    public void AMethodTheModuleDoesNotDeclare_IsLeftAlone()
    {
        // The rewrite is keyed on the module DECLARING that member. A name it does not declare is
        // not this case, and must not be quietly turned into a call to a free function that
        // happens to share the spelling.
        var js = JsTestSupport.Compile("""
            Public Module M
                Public Sub Go()
                    Console.WriteLine("go")
                End Sub
            End Module

            Sub Stop2()
                Console.WriteLine("stop")
            End Sub

            Sub Main()
                Go()
            End Sub
            """);

        Assert.That(js, Does.Contain("function Stop2()"),
            "an unrelated free function is emitted normally");
    }
}
