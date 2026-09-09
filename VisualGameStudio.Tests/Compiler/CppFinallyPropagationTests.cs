using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A Finally runs when an exception PROPAGATES OUT of the Try — chip task_f3c04b9b.
///
/// <para>The three fixtures next door cover exceptions that are CAUGHT. This one covers the
/// path where no clause matches and the exception keeps going. VB.NET runs the Finally on its
/// way out; the C++ backend skipped it entirely.</para>
///
/// <para>⛔ <b>WHY IT HAPPENS, and why it is not a missing goto.</b> The §11.1 ladder is one
/// <c>catch (const BasicLang::NetException&amp;)</c> handler whose last statement is a bare
/// <c>throw;</c>, reached when no arm matched. The Finally's exception copy lives in a
/// <c>catch (...)</c> handler of the SAME try block. C++ [except.handle]/3: the handlers of a
/// try block are not considered for an exception thrown from within one of that block's own
/// handlers. So the rethrow flies straight past the sibling <c>catch (...)</c> — the finally
/// body was emitted, and simply never reached.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppFinallyPropagationTests
{
    private static string Run(string source) =>
        BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Replace("\r\n", "\n").Trim();

    [OneTimeSetUp]
    public void RequireCppCompiler()
    {
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("no C++ toolchain found — this fixture compiles and runs native code.");
    }

    /// <summary>
    /// The caller's Catch proves the exception still propagates, and the ORDER proves the
    /// Finally ran on the way out rather than being swallowed. Exact equality also pins it to
    /// exactly one run: a third emitted copy would show as a second "fin".
    /// </summary>
    [Test]
    public void UnmatchedCatchClause_RunsTheFinallyBeforeThePropagation()
    {
        var output = Run(@"
Sub Main()
    Try
        Inner()
    Catch outer As Exception
        Console.WriteLine(""caught-outer"")
    End Try
    Console.WriteLine(""after"")
End Sub

Sub Inner()
    Try
        Throw New ArgumentException(""boom"")
    Catch e As InvalidOperationException
        Console.WriteLine(""wrong-clause"")
    Finally
        Console.WriteLine(""fin"")
    End Try
End Sub
");
        Assert.That(output, Is.EqualTo("fin\ncaught-outer\nafter"),
            "a missing 'fin' means the ladder's bare `throw;` flew past the sibling catch(...) "
            + "holding the finally copy; 'wrong-clause' means clause matching broke; two 'fin' "
            + "lines mean more than one emitted copy ran.");
    }

    /// <summary>
    /// The same path, with CONTROL FLOW in the Finally. Separate test because it needs a
    /// different property: the propagation copy's own exit must be emitted AT the branch, not
    /// after the region. A Finally body containing an If puts its branch to the EndBlock in the
    /// merge block, which is created before the ElseIf arms and therefore emitted before them —
    /// so a copy written under RegionEnd.FallThrough drops that exit and falls into the next
    /// arm. See EmitRegionEnd; the same shape was measured as an 81,483-line infinite loop in
    /// the try body.
    /// </summary>
    [Test]
    public void UnmatchedCatchClause_WithControlFlowInTheFinally_TakesOneArmAndPropagates()
    {
        var output = Run(@"
Sub Main()
    Try
        Inner()
    Catch outer As Exception
        Console.WriteLine(""caught-outer"")
    End Try
    Console.WriteLine(""after"")
End Sub

Sub Inner()
    Dim a As Boolean = True
    Dim b As Boolean = True
    Try
        Throw New ArgumentException(""boom"")
    Catch e As InvalidOperationException
        Console.WriteLine(""wrong-clause"")
    Finally
        If a Then
            Console.WriteLine(""fin-a"")
        ElseIf b Then
            Console.WriteLine(""fin-b"")
        End If
    End Try
End Sub
");
        Assert.That(output, Is.EqualTo("fin-a\ncaught-outer\nafter"),
            "a repeated 'fin-b' is the dropped mid-region exit falling into the ElseIf arm; a "
            + "missing 'fin-a' means the propagation copy never ran at all.");
    }
}
