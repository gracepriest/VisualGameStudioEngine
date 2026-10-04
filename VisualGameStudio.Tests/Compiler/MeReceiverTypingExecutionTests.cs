using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #176 — one `Me` per member, typed as its own class. Compiled-and-RUN sibling of
//  MeReceiverTypingTests (the IR-level fixture). Sources are copied verbatim from the fix's own
//  probe/edge corpus (S/t176/probes/*.bas, S/t176/edge/*.bas — see this session's scratchpad),
//  each expected value cross-checked against that probe's own .exp and against
//  S/t176/matrix-final.txt cell by cell before being pinned here.
//
//  Expected strings are taken from the .exp files, which were measured on C#/JS, never on MSIL —
//  MSIL is the backend the bug was IN, so its "expected" answer is exactly the other backends'
//  agreed answer, not a separately-measured MSIL number.
// ================================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MeReceiverTypingExecutionTests
{
    // ---- V6 family: Animal (bare or explicit Sound) then Counter (bare V = 2 : Return V), the
    //      exact shape MSIL got wrong — plus V6g's third class, and the V6d/V6e/V6f controls that
    //      were ALWAYS fine (field, method, reversed order) and must stay fine. All four backends,
    //      both pipelines: matrix-final.txt shows every cell RAN OK, unchanged from C#/C++/JS's own
    //      pre-fix answer. ----

    private const string V6 = """
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

        Class Counter
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
                Return V
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string V6Expected = "20";

    /// <summary>Same as <see cref="V6"/>, but Animal uses `Me.Sound` EXPLICITLY — the shape that
    /// kills mutant M2 (only the accessor/bare site reverted): the explicit path never touches
    /// `_variableVersions["Me"]`, so with M2 alone Counter's bare `V = 2` types correctly and this
    /// probe stays green under it. See X7 below for a shape M2 does catch.</summary>
    private const string V6b = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
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
                Return V
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string V6bExpected = "20";

    /// <summary>Counter's bare property is READ only, never written in this method — the write
    /// happens through the qualified `c.V = 3` in Main.</summary>
    private const string V6c = """
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

        Class Counter
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
                Return V
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            c.V = 3
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string V6cExpected = "30";

    /// <summary>Control: Counter's bare member is a plain FIELD, not an accessor-backed property
    /// — never affected by the bug (fields lower to a field access by name regardless of `Me`'s
    /// type; MSIL always got that right).</summary>
    private const string V6d = """
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

        Class Counter
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
                _v = 7
                Return _v
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string V6dExpected = "7";

    /// <summary>Control: a bare METHOD call, not a property.</summary>
    private const string V6e = """
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

        Class Counter
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Twice(n As Integer) As Integer
                Return n * 2
            End Function
            Public Function Probe() As Integer
                Return Twice(4)
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string V6eExpected = "8";

    /// <summary>Control: classes REVERSED — Counter declared first. Never broken (Counter's own
    /// bare write was always against the FIRST class, i.e. itself).</summary>
    private const string V6f = """
        Class Counter
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
                Return V
            End Function
        End Class


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
        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string V6fExpected = "20";

    /// <summary>THREE classes: Animal, Counter, Third — each with its own bare property, Third
    /// declared and used LAST. Also exercises Animal's own bare `Sound` after both Counter and
    /// Third have run, at the very end of Main.</summary>
    private const string V6g = """
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

        Class Counter
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
                Return V
            End Function
        End Class


        Class Third
            Public Property W As Integer
            Public Function Go() As Integer
                W = 5
                Return W + 1
            End Function
        End Class
        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
            Dim t As New Third()
            Console.WriteLine(t.Go())
            Dim a As New Animal()
            Console.WriteLine(a.Speak())
        End Sub
        """;
    private const string V6gExpected = "20\n6\n...!";

    [TestCase(nameof(V6))] [TestCase(nameof(V6b))] [TestCase(nameof(V6c))]
    [TestCase(nameof(V6d))] [TestCase(nameof(V6e))] [TestCase(nameof(V6f))] [TestCase(nameof(V6g))]
    public void V6Family_StandardPipeline_AllFourBackendsAgree(string which)
    {
        var (source, expected) = V6Probe(which);
        FourBackends.RunsOnEveryBackend(source, expected);
    }

    [TestCase(nameof(V6))] [TestCase(nameof(V6b))] [TestCase(nameof(V6c))]
    [TestCase(nameof(V6d))] [TestCase(nameof(V6e))] [TestCase(nameof(V6f))] [TestCase(nameof(V6g))]
    public void V6Family_AggressivePipeline_AllFourBackendsAgree(string which)
    {
        var (source, expected) = V6Probe(which);
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    private static (string Source, string Expected) V6Probe(string which) => which switch
    {
        nameof(V6) => (V6, V6Expected),
        nameof(V6b) => (V6b, V6bExpected),
        nameof(V6c) => (V6c, V6cExpected),
        nameof(V6d) => (V6d, V6dExpected),
        nameof(V6e) => (V6e, V6eExpected),
        nameof(V6f) => (V6f, V6fExpected),
        nameof(V6g) => (V6g, V6gExpected),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    // ---- Edge probes that run on all four backends, both pipelines (S/t176/edge/*.bas) --------

    private const string X3 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
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
                Dim f = Function() V + 1
                V = 3
                Dim a = f()
                V = 2
                Return a * 1000 + f()
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string X3Expected = "31021";

    private const string X4 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Base
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
        End Class

        Class Derived
            Inherits Base
            Public Function Probe() As Integer
                V = 2
                Return V + Me.V
            End Function
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.Probe())
        End Sub
        """;
    private const string X4Expected = "40";

    private const string X5 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Box
            Private Shared _t As Integer
            Public Shared Property T As Integer
                Get
                    Return _t
                End Get
                Set(value As Integer)
                    _t = value * 3
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
                T = 2
                V = 1
                Return T + V
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            Console.WriteLine(b.Probe())
            Console.WriteLine(Box.T)
        End Sub
        """;
    private const string X5Expected = "16\n6";

    /// <summary>A `Structure` declared BETWEEN the two classes — must not disturb `Me`'s typing.
    /// ⛔ The JAVASCRIPT leg is excluded here: a `Structure` declared ANYWHERE in the file refuses
    /// JavaScript compilation entirely (BL7005, "Structure … cannot be lowered to JavaScript") —
    /// measured on this exact probe, both pre- and post-#176-fix, all three entry points. That is
    /// a pre-existing, unrelated front-end restriction, not a #176 regression.</summary>
    private const string X6d = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure

        Class Counter
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
                V = 3
                Return V
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Counter().Probe())
        End Sub
        """;
    private const string X6dExpected = "30";

    private const string X7 = """
        Module M1
            Public Function One() As Integer
                Return 1
            End Function
        End Module

        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Module M2
            Public Function Two() As Integer
                Return 2
            End Function
        End Module

        Class Counter
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
                V = One() + Two()
                Return V + Me.V
            End Function
        End Class

        Class Third
            Private _w As Integer
            Public Property W As Integer
                Get
                    Return _w
                End Get
                Set(value As Integer)
                    _w = value + 100
                End Set
            End Property
            Public Function Go() As Integer
                W = 5
                Return Me.W + W
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Counter().Probe())
            Console.WriteLine(New Third().Go())
            Console.WriteLine(New Animal().Speak())
        End Sub
        """;
    private const string X7Expected = "60\n210\n...!";

    private const string X8 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Twice(n As Integer) As Integer
                Return n * 2
            End Function
            Public Function Probe() As Integer
                Me.V = 2
                Return Me.V + Me.Twice(4)
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Counter().Probe())
        End Sub
        """;
    private const string X8Expected = "28";

    private const string X9 = """
        Module M1
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        End Module

        Module M2
            Class Counter
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
                    Return V
                End Function
            End Class
        End Module

        Sub Main()
            Console.WriteLine(New Counter().Probe())
        End Sub
        """;
    private const string X9Expected = "20";

    private const string X10 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Sub New()
                V = 4
            End Sub
            Public ReadOnly Property Doubled As Integer
                Get
                    Return V * 2
                End Get
            End Property
        End Class

        Sub Main()
            Console.WriteLine(New Counter().Doubled)
        End Sub
        """;
    private const string X10Expected = "80";

    private const string X11 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
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
                Dim f = Function() Me.V + 1
                V = 2
                Return f()
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Counter().Probe())
        End Sub
        """;
    private const string X11Expected = "21";

    private const string X12 = """
        Class Outer
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Class Inner
                Public Function One() As Integer
                    Return 1
                End Function
            End Class
            Public Function Probe() As Integer
                V = 2
                Return Me.V
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Outer().Probe())
        End Sub
        """;
    private const string X12Expected = "20";

    [TestCase(nameof(X3))] [TestCase(nameof(X4))] [TestCase(nameof(X5))] [TestCase(nameof(X7))]
    [TestCase(nameof(X8))] [TestCase(nameof(X9))] [TestCase(nameof(X10))] [TestCase(nameof(X11))]
    [TestCase(nameof(X12))]
    public void EdgeProbes_StandardPipeline_AllFourBackendsAgree(string which)
    {
        var (source, expected) = EdgeProbe(which);
        FourBackends.RunsOnEveryBackend(source, expected);
    }

    [TestCase(nameof(X3))] [TestCase(nameof(X4))] [TestCase(nameof(X5))] [TestCase(nameof(X7))]
    [TestCase(nameof(X8))] [TestCase(nameof(X9))] [TestCase(nameof(X10))] [TestCase(nameof(X11))]
    [TestCase(nameof(X12))]
    public void EdgeProbes_AggressivePipeline_AllFourBackendsAgree(string which)
    {
        var (source, expected) = EdgeProbe(which);
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    private static (string Source, string Expected) EdgeProbe(string which) => which switch
    {
        nameof(X3) => (X3, X3Expected),
        nameof(X4) => (X4, X4Expected),
        nameof(X5) => (X5, X5Expected),
        nameof(X7) => (X7, X7Expected),
        nameof(X8) => (X8, X8Expected),
        nameof(X9) => (X9, X9Expected),
        nameof(X10) => (X10, X10Expected),
        nameof(X11) => (X11, X11Expected),
        nameof(X12) => (X12, X12Expected),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    // X6d: three backends only — JavaScript refuses any Structure declaration (see the const's
    // own doc comment), unrelated to #176.
    [Test]
    public void X6d_StandardPipeline_CSharpCppAndMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(X6d)), Is.EqualTo(X6dExpected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(X6d))), Is.EqualTo(X6dExpected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(X6d)), Is.EqualTo(X6dExpected), "MSIL");
        });

    [Test]
    public void X6d_AggressivePipeline_CSharpCppAndMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(X6d)), Is.EqualTo(X6dExpected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(X6d))), Is.EqualTo(X6dExpected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(X6d)), Is.EqualTo(X6dExpected), "MSIL");
        });

    // ---- X1/X2: `Me` flows into a context typed as a VALUE (an argument; the `Is` operand). C++
    //      excluded — a pre-existing, unrelated gap (task #200): the backend passes `Me`/`this` as
    //      a raw `Counter *` where the callee's parameter is `std::shared_ptr<Counter>`, so it
    //      fails to COMPILE ("no viable conversion from 'Counter *' to 'std::shared_ptr<Counter>'")
    //      on every entry point, unchanged before and after #176's fix (measured). Also
    //      demonstrates M2's kill shape at the C#/JS/MSIL level: X1 threads `Me` through
    //      `Helper.Describe(Me)`, a call visited AFTER Animal's own explicit `Me.Sound` has
    //      already (mis)established `_variableVersions["Me"]` under M2. ----

    private const string X1 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Helper
            Public Shared Function Describe(o As Counter) As Integer
                If o Is Nothing Then
                    Return 0
                End If
                Return 7
            End Function
        End Class

        Class Counter
            Private _w As Integer
            Public Property W As Integer
                Get
                    Return _w
                End Get
                Set(value As Integer)
                    _w = value + 1
                End Set
            End Property
            Private Function Read(c As Counter) As Integer
                Return c.W * 10
            End Function
            Private Sub Bump(c As Counter)
                c.W = c.W + 10
            End Sub
            Public Function Probe() As Integer
                W = 4
                Bump(Me)
                Return Read(Me) * 100 + Helper.Describe(Me)
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
            Console.WriteLine(New Animal().Speak())
        End Sub
        """;
    private const string X1Expected = "16007\n...!";

    private const string X2 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Same(o As Counter) As Boolean
                Return o Is Me
            End Function
            Public Function Probe() As Integer
                V = 2
                If Me IsNot Nothing AndAlso Same(Me) Then
                    Return V
                End If
                Return -1
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
            If c.Same(New Counter()) Then
                Console.WriteLine(1)
            Else
                Console.WriteLine(0)
            End If
        End Sub
        """;
    private const string X2Expected = "20\n0";

    [TestCase(nameof(X1))] [TestCase(nameof(X2))]
    public void X1X2_StandardPipeline_CSharpJsAndMsilAgree(string which)
    {
        var (source, expected) = X1X2Probe(which);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL");
        });
    }

    [TestCase(nameof(X1))] [TestCase(nameof(X2))]
    public void X1X2_AggressivePipeline_CSharpJsAndMsilAgree(string which)
    {
        var (source, expected) = X1X2Probe(which);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(source)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), "MSIL");
        });
    }

    private static (string Source, string Expected) X1X2Probe(string which) => which switch
    {
        nameof(X1) => (X1, X1Expected),
        nameof(X2) => (X2, X2Expected),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    // ---- X3b: a Sub lambda WRITES a bare property, a Function lambda READS it. C# was EXCLUDED
    //      and pinned (task #136, widened by this measurement to cover a Sub lambda's bare property
    //      WRITE, not only a For-Each variable capture): it printed 1021, not 31021 — the `g()` Sub
    //      lambda's write to `V` was not observed by the later `f()` reads, on BOTH pipelines,
    //      unchanged before and after #176's fix. ⭐ MOVED PIN: #136 writes the lambda body, and C#
    //      prints 31021 on both pipelines like the other three backends. ----

    private const string X3b = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
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
                Dim f = Function() V + 1
                Dim g = Sub() V = 3
                g()
                Dim a = f()
                V = 2
                Return a * 1000 + f()
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Console.WriteLine(c.Probe())
        End Sub
        """;
    private const string X3bExpected = "31021";

    [Test]
    public void X3b_StandardPipeline_AllFourBackendsAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(X3b))), Is.EqualTo(X3bExpected), "C# (#136: a Sub lambda's write to the property is written)");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(X3b))), Is.EqualTo(X3bExpected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(X3b)), Is.EqualTo(X3bExpected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(X3b)), Is.EqualTo(X3bExpected), "MSIL");
        });

    [Test]
    public void X3b_AggressivePipeline_AllFourBackendsAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(X3b))), Is.EqualTo(X3bExpected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(X3b))), Is.EqualTo(X3bExpected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(X3b)), Is.EqualTo(X3bExpected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(X3b)), Is.EqualTo(X3bExpected), "MSIL");
        });

    // ---- X12b: the SAME nested-class shape as X12, but Outer.Probe also CONSTRUCTS `New
    //      Inner()`. C++ excluded — a pre-existing, unrelated nested-class emission-order defect:
    //      `Inner` is referenced before the generated C++ class body for it is complete
    //      ("member access into incomplete type"), on every entry point, unchanged before and
    //      after #176's fix. ----

    private const string X12b = """
        Class Outer
            Class Inner
                Public Function One() As Integer
                    Return 1
                End Function
            End Class
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
                Return Me.V + New Inner().One()
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Outer().Probe())
        End Sub
        """;
    private const string X12bExpected = "21";

    [Test]
    public void X12b_StandardPipeline_CSharpJsAndMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(X12b)), Is.EqualTo(X12bExpected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(X12b)), Is.EqualTo(X12bExpected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(X12b)), Is.EqualTo(X12bExpected), "MSIL");
        });

    [Test]
    public void X12b_AggressivePipeline_CSharpJsAndMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(X12b)), Is.EqualTo(X12bExpected), "C#");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(X12b)), Is.EqualTo(X12bExpected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(X12b)), Is.EqualTo(X12bExpected), "MSIL");
        });

    // ---- A Release .blproj leg on MSIL (BasicCompiler.CompileProjectFiles under the CLI's own
    //      "-c Release" -> OptimizeAggressive mapping) — a different code path than
    //      MsilHarness.CompileToIl(aggressive: true)'s direct AggressivePipeline.Apply call. Same
    //      pattern as MsilBinaryOperandCoercionTests.BuildReleaseMsilAndRun /
    //      MsilValueToStringExecutionTests.BuildReleaseMsilAndRun. ----

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-mert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private string BuildReleaseMsilAndRun(string basSource)
    {
        File.WriteAllText(Path.Combine(_projectDir, "Main.bas"), basSource);
        File.WriteAllText(Path.Combine(_projectDir, "App.blproj"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>MSIL</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(_projectDir, "App.blproj"), "-c", "Release" },
            _projectDir,
            timeoutMs: 120_000);
        Assert.That(buildExit, Is.EqualTo(0),
            $"CLI Release MSIL build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

        var ilFiles = Directory.GetFiles(_projectDir, "App.il", SearchOption.AllDirectories);
        Assert.That(ilFiles, Is.Not.Empty,
            $"CLI build claimed success but produced no App.il.\nSTDOUT:\n{buildOut}");

        return MsilHarness.RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App");
    }

    [TestCase(nameof(V6))] [TestCase(nameof(V6g))]
    public void ReleaseBlprojBuild_Msil_PrintsTheExpectedText(string which)
    {
        var (source, expected) = V6Probe(which);
        Assert.That(FourBackends.Norm(BuildReleaseMsilAndRun(source)), Is.EqualTo(expected));
    }
}
