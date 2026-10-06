using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// #203, the fast half (see <see cref="OperandEvaluationOrderExecutionTests"/> for the defect, the oracle, the mutants and the known gaps). The fix copies a bare field / global read into a
/// <c>__snap{n}</c> carrier ONLY when a later operand may write it. This pins the other direction, which the run tests cannot see: where nothing can write, NO carrier is emitted. Mutant M5 counts a plain
/// field READ (<c>o.K</c>, an <c>IRFieldAccess</c> the analyzer resolved to a field) as a call that may write, so <c>K + o.K</c> grows a carrier; the program still prints the right answer, only its text
/// changes, which is why nothing that runs can kill it.
/// </summary>
[TestFixture]
public class OperandEvaluationOrderTextTests
{
    /// <summary>
    /// Three expressions with a bare field on the left and nothing that can write on the right: a field of ANOTHER object (`K + o.K + L * o.L`), a literal (`K + 1`) and a second field (`K + L`). `Bump`,
    /// which does write K, exists but is never called inside these expressions.
    /// </summary>
    private const string NothingCanWrite = """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function WithOthersField(o As C) As Integer
                Return K + o.K + L * o.L
            End Function
            Public Function WithLiteral() As Integer
                Return K + 1
            End Function
            Public Function WithField() As Integer
                Return K + L
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.WithOthersField(New C()))
            Console.WriteLine(c.WithLiteral())
            Console.WriteLine(c.WithField())
        End Sub
        """;

    /// <summary>The control that keeps the check from being vacuous: the same class, one expression that DOES have a later write.</summary>
    private const string ALaterCallWrites = """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function WithOthersField(o As C) As Integer
                Return K + o.K + L * o.L
            End Function
            Public Function WithCall() As Integer
                Return K + Bump()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.WithOthersField(New C()))
            Console.WriteLine(c.WithCall())
        End Sub
        """;

    /// <summary>The emitted text of <paramref name="source"/> where a carrier would show: C++ and JavaScript through the standard passes, and C#, C++ and JavaScript through <c>CompileProjectFiles</c> (aggressive).</summary>
    private static List<(string Label, string Text)> Texts(string source) => new()
    {
        ("C++, standard passes", BclE2E.CompileToCppOptimized(source)),
        ("JavaScript, standard passes", JsTestSupport.CompileOptimized(source)),
        ("C#, CompileProjectFiles", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source)),
        ("C++, CompileProjectFiles", TempExec.Emit(Bk.Cpp, EntryPoint.ProjectRelease, source)),
        ("JavaScript, CompileProjectFiles", TempExec.Emit(Bk.JavaScript, EntryPoint.ProjectRelease, source)),
    };

    /// <summary>The carriers the IR builder declared: every <c>__snap*</c> local of every function, by function.</summary>
    private static List<string> Carriers(string source)
        => TempIr.Functions(TempIr.Build(source))
            .SelectMany(f => f.LocalVariables.Where(v => v.Name.StartsWith("__snap")).Select(v => $"{f.Name}:{v.Name}"))
            .ToList();

    /// <summary>
    /// `K + o.K + L * o.L`, `K + 1` and `K + L` emit NO <c>__snap</c> carrier, in the IR or in the C++, JavaScript or C# text, through the standard and the aggressive passes — while `K + Bump()` in the SAME class
    /// emits exactly one, in the IR and in every text (so the absence above is not a renamed carrier). M5 (a field read counted as a call) puts one on `K + o.K`.
    /// </summary>
    [Test]
    public void AReadWithNoLaterWrite_EmitsNoCarrier_WhereALaterCallDoes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Carriers(NothingCanWrite), Is.Empty, "the IR of K + o.K + L * o.L, K + 1 and K + L declares a carrier");
            foreach (var (label, text) in Texts(NothingCanWrite))
                Assert.That(text, Does.Not.Contain("__snap"), $"{label}: a read with nothing that can write it was copied");

            var carriers = Carriers(ALaterCallWrites);
            Assert.That(carriers.Count(c => c.Contains("WithCall")), Is.EqualTo(1), "the IR of K + Bump() should declare one carrier, in the function that calls Bump: [" + string.Join(", ", carriers) + "]");
            Assert.That(carriers, Has.Count.EqualTo(1), "and none for K + o.K + L * o.L: [" + string.Join(", ", carriers) + "]");
            foreach (var (label, text) in Texts(ALaterCallWrites))
                Assert.That(text, Does.Contain("__snap"), $"{label}: K + Bump() emitted no carrier");
        });
    }
}
