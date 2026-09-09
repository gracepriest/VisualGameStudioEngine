using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Exit For</c> inside a <c>For Each</c> actually LEAVES the loop on the C++ backend —
/// chip task_4cc381f1.
///
/// <para>It did not. It lowered to <c>continue;</c>, so <c>Exit For</c> silently behaved as
/// <c>Continue For</c>: a loop that must print 1,2 printed 1,2,4 — compiled, ran, exited 0, no
/// warning. The tell in the emitted C++ was a <c>foreach0_end:</c> label that nothing ever
/// jumped to.</para>
///
/// <para><b>The root cause was in the IR, not the backend.</b>
/// <c>IRBuilder.Visit(ExitStatementNode)</c> emitted a plain <c>IRBranch(BreakTarget)</c> —
/// byte-identical to the branch that ends an ordinary iteration. With the two indistinguishable,
/// every backend had to guess the difference back from block position, and the obvious guess is
/// wrong: an <c>If</c> inside the body produces a merge block that ALSO branches to the loop's
/// end block and must stay <c>continue</c>. <c>IRBranch.IsLoopExit</c> carries the distinction
/// the front end always knew.</para>
///
/// <para>⛔ These RUN. <c>CppCollectionTests</c>-style emission assertions cannot see this class
/// of bug: the loop, the body and the end label were all emitted correctly and the program still
/// computed the wrong answer. stdout is the oracle.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppExitForExecutionTests
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
    /// The measured repro. 4 in the output is the whole bug: the loop kept going past the exit.
    /// </summary>
    [Test]
    public void ExitFor_InsideForEach_LeavesTheLoop()
    {
        var output = Run(@"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    l.Add(3)
    l.Add(4)
    For Each n As Integer In l
        If n = 3 Then
            Exit For
        End If
        Console.WriteLine(n)
    Next
    Console.WriteLine(""done"")
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("1\n2\ndone"),
            "a 4 in the output means Exit For lowered to continue; — it kept iterating.");
    }

    /// <summary>
    /// ⛔ THE CASE THAT BREAKS THE OBVIOUS FIX. The <c>If</c>'s merge block ALSO branches to the
    /// loop's end block, and that branch is the natural end of an iteration — it must stay
    /// <c>continue</c>. A positional rule that treats every non-entry branch as an exit turns
    /// this loop into a one-iteration loop, which no emission test would notice either.
    /// </summary>
    [Test]
    public void ForEach_WithAnIfButNoExit_StillIteratesEveryElement()
    {
        var output = Run(@"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    l.Add(3)
    For Each n As Integer In l
        If n = 2 Then
            Console.WriteLine(""two"")
        End If
        Console.WriteLine(n)
    Next
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("1\ntwo\n2\n3"),
            "stopping early means an ordinary end-of-iteration branch was turned into a break.");
    }

    /// <summary>An unconditional exit on the first element — the loop body runs once, partially.</summary>
    [Test]
    public void ExitFor_Unconditional_LeavesImmediately()
    {
        var output = Run(@"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    For Each n As Integer In l
        Console.WriteLine(n)
        Exit For
    Next
    Console.WriteLine(""after"")
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("1\nafter"));
    }

    /// <summary>
    /// A NESTED For Each: the inner Exit For must leave only the inner loop. The break target is
    /// whichever loop is innermost on the builder's loop stack, so getting this wrong exits both.
    /// </summary>
    [Test]
    public void ExitFor_InANestedForEach_LeavesOnlyTheInnerLoop()
    {
        var output = Run(@"
Sub Main()
    Dim outer As New List(Of Integer)()
    outer.Add(1)
    outer.Add(2)
    Dim inner As New List(Of Integer)()
    inner.Add(10)
    inner.Add(20)
    For Each o As Integer In outer
        For Each i As Integer In inner
            Exit For
        Next
        Console.WriteLine(o)
    Next
End Sub
");
        Assert.That(output.Trim(), Is.EqualTo("1\n2"),
            "only 1 means the inner Exit For escaped the OUTER loop too.");
    }
}
