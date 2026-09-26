using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A property read through an INTERFACE-typed variable takes the property's declared type. The
/// analyzer resolved an interface's property TYPE but never registered the property as a MEMBER,
/// so <c>s.Area</c> typed as Object: <c>Dim t As String = s.Area</c> was refused as Object→String
/// (where the real Integer→String refusal belongs), and on C++ the value landed in a <c>void*</c>.
/// </summary>
[TestFixture]
public class InterfacePropertyMemberTypeTests
{
    private const string Shape = """
        Interface IShape
            ReadOnly Property Area As Integer
            Property Label As String
        End Interface
        Class Sq
            Implements IShape
            Public ReadOnly Property Area As Integer
                Get
                    Return 16
                End Get
            End Property
            Public Property Label As String
        End Class

        """;

    private static string Errors(string body)
    {
        var parser = new Parser(new Lexer(Shape + "Sub Main()\n    Dim s As IShape = New Sq()\n    " + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    [TestCase("Dim n As Integer = s.Area")]
    [TestCase("Dim t As String = s.Label")]
    [TestCase("s.Label = \"x\"")]
    public void AnInterfacePropertyRead_HasItsDeclaredType(string body)
        => Assert.That(Errors(body), Is.Empty);

    [Test]
    public void AMismatch_NamesTheDeclaredType_NotObject()
    {
        var errors = Errors("Dim t As String = s.Area");
        Assert.That(errors, Does.Contain("'Integer'"));
        Assert.That(errors, Does.Not.Contain("'Object'"));
    }
}

/// <summary>
/// Properties with Get/Set bodies on C++ (task #148). The IR carries every property read as an
/// IRFieldAccess and every write as an IRFieldStore, BY NAME; C++ emitted them as member
/// accesses, and a class with a Get/Set property has no member of that name — only
/// <c>get_X()</c>/<c>set_X()</c> — so every such program failed to compile on C++ alone.
/// Routing the access through the accessors also exposed two latent accessor defects nothing had
/// reached: a <c>const</c> getter could not call a method, and a getter returning
/// <c>const std::string&amp;</c> returned a reference to a destroyed temporary (a segfault).
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class PropertyAccessorExecutionTests
{
    private const string Program = """
        Interface IShape
            ReadOnly Property Area As Integer
            Property Label As String
        End Interface

        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Sound & "!"
            End Function
        End Class

        Class Dog
            Inherits Animal
            Public Overrides ReadOnly Property Sound As String
                Get
                    Return "Woof"
                End Get
            End Property
        End Class

        Class Sq
            Implements IShape
            Private _side As Integer
            Private _label As String = "sq"
            Public Sub New(s As Integer)
                _side = s
            End Sub
            Public ReadOnly Property Area As Integer
                Get
                    Return _side * _side
                End Get
            End Property
            Public Property Label As String
                Get
                    Return _label
                End Get
                Set(value As String)
                    _label = value & "!"
                End Set
            End Property
        End Class

        Class Counter
            Private Shared _n As Integer
            Public Shared Property N As Integer
                Get
                    Return _n
                End Get
                Set(value As Integer)
                    _n = value
                End Set
            End Property
            Private _hits As Integer
            Public Function Bump() As Integer
                _hits = _hits + 1
                Return _hits
            End Function
            Public ReadOnly Property Next1 As Integer
                Get
                    Return Bump()
                End Get
            End Property
            Private _inner As Counter
            Public Property Inner As Counter
                Get
                    Return _inner
                End Get
                Set(value As Counter)
                    _inner = value
                End Set
            End Property
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Probe() As Integer
                V = 2
                Return V + Next1
            End Function
        End Class

        Sub Main()
            Dim a As Animal = New Dog()
            Console.WriteLine(a.Sound)
            Console.WriteLine(a.Speak())
            Dim s As IShape = New Sq(4)
            Dim area As Integer = s.Area
            Console.WriteLine(area + 1)
            s.Label = "box"
            Console.WriteLine(s.Label)
            Counter.N = 3
            Counter.N += 2
            Console.WriteLine(Counter.N)
            Dim c As New Counter()
            Console.WriteLine(c.Next1 + c.Next1)
            c.Inner = New Counter()
            Console.WriteLine(c.Inner.Next1)
            c.V += 1
            Console.WriteLine(c.V)
            Console.WriteLine(c.Probe())
        End Sub
        """;

    // Sound twice; Area + 1; Label through the setter; Shared 3 then += 2; Next1 = 1 then 2;
    // the inner counter's first hit; V: 0 + 1 -> set 10; Probe: V = 2 -> 20, plus the 3rd hit.
    private const string Expected = "Woof\nWoof!\n17\nbox!\n5\n3\n1\n10\n23";

    // ⚠ Three backends, not four, STILL. Task #175 fixed the gap this comment used to describe —
    // MSIL now calls IShape's own get_Area()/set_Label() instead of lowering `s.Area`/`s.Label` to
    // a field load — but this Program cannot go on MSIL anyway: `Counter.Probe()` does a BARE
    // (unqualified) property write, `V = 2`, and MSIL emits that write against the FIRST class
    // declared in the module (`Animal`) instead of the enclosing class (`Counter`) — a separate,
    // pre-existing MSIL defect, task #176, with NO interface involved. Measured: the run now gets
    // past every interface access and dies later, inside `Probe()`, with "MissingFieldException:
    // Field not found: 'Animal.V'". See Msil_BarePropertyWriteInALaterClass_TargetsAnEarlierClass_
    // PinnedForTask176 below, and its repro S/t175/pin/V6.bas.

    [Test]
    public void StandardPipeline_RunsOnCSharpCppAndJavaScript()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected), "JavaScript");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected), "C#");
        });

    [Test]
    public void AggressivePipeline_RunsOnCSharpCppAndJavaScript()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(Program))), Is.EqualTo(Expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(Program)), Is.EqualTo(Expected), "JavaScript");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(Program)), Is.EqualTo(Expected), "C#");
        });

    /// <summary>
    /// ⛔ PINNED KNOWN GAP, task #176 (re-pinned from task #175, now fixed — see the comment
    /// above): a BARE property write inside a class's own method is emitted against the FIRST
    /// class the module declares, not the class the method belongs to. `Counter.Probe()` does
    /// `V = 2`, unqualified, and MSIL targets `Animal` (declared before `Counter`) instead of
    /// `Counter` itself — a `MissingFieldException` naming the wrong class's field, at run time.
    /// No interface is involved. Asserts today's failure so it goes RED the day MSIL fixes the
    /// bare-write target — then delete this pin and assert MSIL in the two tests above.
    ///
    /// <para>⚠ Checked on <c>MsilRun.Output</c> (the raw process text), not on the message of an
    /// exception caught around <see cref="Msil.MsilHarness.RunExpectingSuccess"/>: this Program
    /// prints eight lines before the crash, so <c>MsilRun.Detail</c> — the harness's "first line
    /// of output" summary, meant for a program that fails before printing anything — is just
    /// "Woof", and a report built from it never shows the exception at all.</para>
    /// </summary>
    [Test]
    public void Msil_BarePropertyWriteInALaterClass_TargetsAnEarlierClass_PinnedForTask176()
    {
        // Skip, not fail, where ilasm is absent.
        Msil.MsilHarness.RequireIlasm();
        var run = Msil.MsilHarness.Run(Program);
        Assert.That(run.Outcome, Is.EqualTo(Msil.MsilHarness.MsilOutcome.RunFailed), run.Output);
        Assert.That(run.Output, Does.Contain("MissingFieldException").And.Contain("Animal.V"),
            "task #176 — if MSIL no longer targets the wrong class here, the bare-write gap is "
            + "fixed: delete this pin and add MSIL back to both pipelines above.");
    }
}
