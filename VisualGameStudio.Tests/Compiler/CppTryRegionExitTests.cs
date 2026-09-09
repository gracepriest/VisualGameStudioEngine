using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A Try/Catch/Finally body whose normal exit is NOT its last emitted instruction.
///
/// <para>⛔ <b>WHY THIS FILE EXISTS.</b> <c>RegionEnd.FallThrough</c> was treated as
/// "<c>GotoEnd</c> minus the jump". It is not. <see cref="M:EmitRegionEnd"/> writes the jump at
/// the branch instruction's OWN position, and region blocks are emitted in CREATION order —
/// while IRBuilder creates an If's merge block (the block that ends up carrying the branch to
/// the try's EndBlock) BEFORE the blocks of its ElseIf/Else arms. So the moment a body contains
/// an If, its exit lands MID-region, <c>FallThrough</c> emits nothing there, and control falls
/// into the next block's label instead of leaving the construct.</para>
///
/// <para>The result compiles cleanly, exits no signal, and loops forever printing the wrong
/// arm. Measured: the mutant emitted "try-a" then "try-b" 81,483 times and was still going when
/// killed. Every one of these shapes is straight-line-equivalent to a test that already passed,
/// which is exactly why the gap survived — the full suite was green at 5437 tests with the
/// defect live.</para>
///
/// <para>⚠ These are EXECUTION tests with a hard timeout, not emission assertions. An emission
/// assertion cannot distinguish "the jump is missing" from "the jump is elsewhere", and the
/// failure mode here is non-termination, which only running the binary can observe.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppTryRegionExitTests
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
    /// The Try BODY holds the If. Regressed by a29d65b, which moved the body to FallThrough so a
    /// Finally would stop being skipped — correct for a straight-line body, fatal for this one.
    /// </summary>
    [Test]
    public void TryBodyWithIfElseIf_TakesOneArmAndRunsTheFinallyOnce()
    {
        var output = Run(@"
Sub Main()
    Dim a As Boolean = True
    Dim b As Boolean = True
    Try
        If a Then
            Console.WriteLine(""try-a"")
        ElseIf b Then
            Console.WriteLine(""try-b"")
        End If
    Finally
        Console.WriteLine(""fin"")
    End Try
    Console.WriteLine(""after"")
End Sub
");
        Assert.That(output, Is.EqualTo("try-a\nfin\nafter"),
            "a repeated 'try-b' is the merge block falling into the ElseIf arm — the region's "
            + "exit jump was dropped because it was not the last instruction emitted.");
    }

    /// <summary>
    /// The CATCH body holds the If, and the clause arms the §11.1 NetException ladder (an
    /// untyped Catch does too — IRBuilder types it "Exception", which is in the 12-name set).
    /// A BasicLang <c>Throw</c> carries an inheritance chain, so the LADDER copy is the one that
    /// actually runs; the per-clause copy below it is unreachable for this program.
    /// </summary>
    [Test]
    public void CatchBodyWithIfElseIf_TakesOneArmAndReachesTheCodeAfterTheTry()
    {
        var output = Run(@"
Sub Main()
    Dim a As Boolean = True
    Dim b As Boolean = True
    Try
        Throw New ArgumentException(""boom"")
    Catch ex As Exception
        If a Then
            Console.WriteLine(""a"")
        ElseIf b Then
            Console.WriteLine(""b"")
        End If
    End Try
    Console.WriteLine(""after"")
End Sub
");
        Assert.That(output, Is.EqualTo("a\nafter"),
            "'b' means the ElseIf arm ran although the If arm was taken; a missing 'after' "
            + "means the catch body never left the handler.");
    }

    /// <summary>
    /// Both at once, which is the shape that pins the finally-entry label: the catch body's exit
    /// must land on the label rather than on the end label, or the Finally is skipped — and it
    /// must land there via a jump emitted AT the branch, or the exit is lost entirely.
    /// </summary>
    [Test]
    public void CatchBodyWithIfElseIf_AndAFinally_RunsTheFinallyExactlyOnce()
    {
        var output = Run(@"
Sub Main()
    Dim a As Boolean = True
    Dim b As Boolean = True
    Try
        Throw New ArgumentException(""boom"")
    Catch ex As Exception
        If a Then
            Console.WriteLine(""a"")
        ElseIf b Then
            Console.WriteLine(""b"")
        End If
    Finally
        Console.WriteLine(""fin"")
    End Try
    Console.WriteLine(""after"")
End Sub
");
        Assert.That(output, Is.EqualTo("a\nfin\nafter"),
            "two 'fin' lines mean both emitted finally copies ran; none means the catch exit "
            + "jumped past the normal-path copy.");
    }

    /// <summary>
    /// A CONDITIONAL branch straight to the end block — <c>Exit Try</c>-shaped early exit. This
    /// went through EmitBranchArm, which used to hard-code the unsuffixed end label, so an early
    /// conditional exit skipped the Finally. The generator's own comment recorded that as an
    /// unsupported residual; retargeting the jump closes it, and this pins it closed.
    /// </summary>
    [Test]
    public void ConditionalEarlyExitFromTry_StillRunsTheFinally()
    {
        var output = Run(@"
Sub Main()
    Dim a As Boolean = True
    Try
        Console.WriteLine(""body"")
        If a Then
            Console.WriteLine(""taken"")
        End If
    Finally
        Console.WriteLine(""fin"")
    End Try
    Console.WriteLine(""after"")
End Sub
");
        Assert.That(output, Is.EqualTo("body\ntaken\nfin\nafter"),
            "a missing 'fin' is the conditional-exit-skips-the-Finally residual returning.");
    }
}
