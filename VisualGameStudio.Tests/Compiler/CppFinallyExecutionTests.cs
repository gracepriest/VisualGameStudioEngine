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
    /// EXACTLY ONCE and IN ORDER. The finally body is emitted TWICE — a normal copy and an
    /// exception copy — so a fix letting both run would still satisfy the two tests above (11
    /// and 12 would simply become 21 and 22). Printing catches that directly: a second copy
    /// shows up as a second "finally" line.
    ///
    /// <para>This ALSO regression-tests a separate bug it used to be written around. A
    /// <c>Console.WriteLine</c> inside a Finally produced an UNTYPED temp, and the shared base
    /// <c>ICodeGenerator.MapType</c> answers a null type with the literal string
    /// <c>"object"</c> — a C# type name — which reached the generated C++ verbatim as
    /// <c>object t2 = {};</c> and failed g++ with <c>'object' was not declared in this
    /// scope</c>, from a build BasicLang reported as successful. So this test used an integer
    /// accumulator instead. It no longer has to: <c>CppCodeGenerator.MapType</c> now answers
    /// null with <c>void*</c>, this backend's own mapping for Object.</para>
    /// </summary>
    [Test]
    public void Finally_RunsExactlyOnce_AndAfterTheBody()
    {
        var output = Run(@"
Sub Main()
    Try
        Console.WriteLine(""body"")
    Finally
        Console.WriteLine(""finally"")
    End Try
    Console.WriteLine(""after"")
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("body\nfinally\nafter"),
            "two 'finally' lines mean both emitted copies ran; a missing one means neither "
            + "did; and a build failure here is the untyped-temp defect returning.");
    }
}
