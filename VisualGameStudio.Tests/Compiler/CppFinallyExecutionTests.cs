using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The C++ backend actually RUNS a <c>Finally</c> block — chip task_43884478.
///
/// <para>It did not. On BOTH paths. A Try/Finally that must yield 11 yielded 1, and a caught
/// one that must yield 12 yielded 2 — compiled, ran, exited 0, no warning. The try body's
/// normal exit emitted <c>goto try0_end;</c>, which jumped clean over the normal-path finally
/// copy sitting between the last catch and the end label.</para>
///
/// <para>⛔ <b>WHY THIS FILE EXISTS AT ALL.</b>
/// <c>CppBackendTests.Cpp_TryFinally_EmitsFinallyOnBothPaths</c> was GREEN throughout — it
/// counts SUBSTRINGS in the generated text and never invokes a C++ compiler, so it verified
/// that the finally body was EMITTED twice while control flow skipped both copies. Emission is
/// not execution. Keep that test as a cheap companion; this one is the gate.</para>
///
/// <para>⚠ Known residual, pre-existing and deliberately not claimed fixed: a CONDITIONAL
/// branch to the end block still emits a direct <c>goto</c>, so an early conditional exit from
/// a Try can still skip its Finally. The finally-duplication design's own comment already
/// records that shape as unsupported. These tests cover the straight-line paths.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppFinallyExecutionTests
{
    private static string Run(string source) =>
        BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Replace("\r\n", "\n");

    [OneTimeSetUp]
    public void RequireCppCompiler()
    {
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("no C++ toolchain found — this fixture compiles and runs native code.");
    }

    /// <summary>
    /// The accumulator matters: it makes the Finally's effect OBSERVABLE in the return value.
    /// A test that only checked "did the program run" passes with the Finally skipped.
    /// </summary>
    [Test]
    public void Finally_RunsOnTheNormalPath()
    {
        var output = Run(@"
Function Norm(n As Integer) As Integer
    Dim acc As Integer = n
    Try
        acc = acc + 1
    Finally
        acc = acc + 10
    End Try
    Return acc
End Function

Sub Main()
    Console.WriteLine(Norm(0))
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("11"),
            "1 means the Try body ran and the Finally was skipped — the goto jumped past the "
            + "normal-path finally copy.");
    }

    [Test]
    public void Finally_RunsAfterACaughtException()
    {
        var output = Run(@"
Function Caught(n As Integer) As Integer
    Dim acc As Integer = n
    Try
        Throw New ArgumentException(""boom"")
    Catch e As Exception
        acc = acc + 2
    Finally
        acc = acc + 10
    End Try
    Return acc
End Function

Sub Main()
    Console.WriteLine(Caught(0))
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("12"),
            "2 means the catch body ran and the Finally was skipped.");
    }

    /// <summary>
    /// EXACTLY ONCE, not merely "at least once". The finally body is emitted TWICE — a normal
    /// copy and an exception copy — so a fix that let both run would satisfy the two tests
    /// above (11 and 12 would become 21 and 22, but a laxer assertion would not notice).
    /// Counting catches that; the counter also proves the Try body ran first.
    ///
    /// <para>⛔ The Finally deliberately does NOT contain a <c>Console.WriteLine</c>. That
    /// emits an <c>Object</c>-typed temp which lowers to a nonexistent C++ <c>object</c> type
    /// — <c>'object' was not declared in this scope</c> — from a build BasicLang reports as
    /// successful. Confirmed PRE-EXISTING by A/B against the un-fixed generator, and chipped
    /// separately. Using a WriteLine here would fail this test for an unrelated reason.</para>
    /// </summary>
    [Test]
    public void Finally_RunsExactlyOnce_AndAfterTheBody()
    {
        var output = Run(@"
Function Count(n As Integer) As Integer
    Dim acc As Integer = n
    Try
        acc = acc * 10
    Finally
        acc = acc + 1
    End Try
    Return acc
End Function

Sub Main()
    Console.WriteLine(Count(1))
End Sub
");
        // 1 * 10 = 10, then +1 once = 11. Twice would be 12; Finally-before-body would be 20.
        Assert.That(output.Trim(), Is.EqualTo("11"),
            "12 means both emitted copies ran; 20 means the Finally ran before the Try body; "
            + "10 means it did not run at all.");
    }
}
