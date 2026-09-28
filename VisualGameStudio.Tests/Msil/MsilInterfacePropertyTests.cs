using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Compiler;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Task #175: reading and writing a property through an INTERFACE-typed receiver (<c>s.Area</c>,
/// <c>s As IShape</c>) on the MSIL backend.
///
/// <para>⛔ <b>What was wrong.</b> <c>Visit(IRFieldAccess)</c>/<c>Visit(IRFieldStore)</c> resolved
/// a property only through <c>TryResolveProperty</c>, which looks in <c>_module.Classes</c>. An
/// interface receiver missed every arm and fell to the plain <c>ldfld</c>/<c>stfld</c> arm, naming
/// storage an interface cannot have (<c>'IShape'::'Area'</c>). <c>ilasm</c> does not resolve
/// member references, so every such program ASSEMBLED and died at run time with
/// <c>MissingFieldException</c>, on both pipelines, at every entry point. See <c>git show
/// 63f348e5</c> — <c>TryResolveInterfaceProperty</c>, <c>EmitInterfacePropertyGet</c>/
/// <c>EmitInterfacePropertySet</c>, and the shared <c>EmitAccessorGet</c>/<c>EmitAccessorSet</c>.</para>
///
/// <para>The programs below are the probes from the fix's own measurement (S/t175/probes,
/// S/t175/edge). I5b is I5 with the <c>Object</c> local removed, isolating the interface fix
/// from task #177's own (separate) box-into-Object gap; I5 itself was excluded here for that
/// reason until #177 DONE (2026-09-28: MSIL now boxes a value type into an <c>Object</c> slot),
/// after which it was promoted alongside I5b rather than left out.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // FourBackends/MsilHarness.Run compile+assemble+spawn ilasm and dotnet
[NonParallelizable]         // FourBackends' C# leg redirects Console.Out
public class MsilInterfacePropertyTests
{
    // ====================================================================================
    // Contract: I1, I2, I3, I4, I6, I5b — FourBackends also re-confirms C#/JS/C++ are
    // unaffected, and covers the "on MSIL, on both pipelines" requirement as its MSIL leg.
    // ====================================================================================

    // I1 — a ReadOnly read, plus a Get/Set property written and read.
    private const string I1 = """
        Interface IShape
            ReadOnly Property Area As Integer
            Property Label As String
        End Interface

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

        Sub Main()
            Dim s As IShape = New Sq(4)
            Dim area As Integer = s.Area
            Console.WriteLine(area + 1)
            s.Label = "box"
            Console.WriteLine(s.Label)
        End Sub
        """;

    private const string I1Expected = "17\nbox!";

    [Test]
    public void I1_ReadOnlyReadAndGetSetProperty_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(I1, I1Expected);

    [Test]
    public void I1_ReadOnlyReadAndGetSetProperty_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(I1, I1Expected);

    // I2 — a receiver returned by a function, an interface-typed parameter, and a
    // read-modify-write.
    private const string I2 = """
        Interface IShape
            Property Name As String
            ReadOnly Property Area As Integer
        End Interface

        Class Sq
            Implements IShape
            Public Property Name As String
            Public ReadOnly Property Area As Integer
                Get
                    Return 9
                End Get
            End Property
        End Class

        Function Make() As IShape
            Dim q As New Sq()
            q.Name = "made"
            Return q
        End Function

        Sub Show(n As IShape)
            Console.WriteLine(n.Name)
        End Sub

        Sub Main()
            Dim s As IShape = Make()
            Console.WriteLine(s.Name)
            s.Name = s.Name & "+"
            Show(s)
            Console.WriteLine(s.Area * 2)
            Console.WriteLine(Make().Area)
        End Sub
        """;

    private const string I2Expected = "made\nmade+\n18\n9";

    [Test]
    public void I2_FunctionReturnedReceiverAndReadModifyWrite_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(I2, I2Expected);

    [Test]
    public void I2_FunctionReturnedReceiverAndReadModifyWrite_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(I2, I2Expected);

    // I3 — an auto-property: a plain write, +=, and a write inside a loop.
    private const string I3 = """
        Interface ICounter
            Property Count As Integer
        End Interface

        Class C
            Implements ICounter
            Public Property Count As Integer
        End Class

        Sub Main()
            Dim c As ICounter = New C()
            c.Count = 5
            c.Count += 2
            c.Count = c.Count * 10
            For i As Integer = 1 To 3
                c.Count = c.Count + i
            Next
            Console.WriteLine(c.Count)
        End Sub
        """;

    private const string I3Expected = "76";

    [Test]
    public void I3_AutoPropertyPlusEqualsAndLoopWrite_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(I3, I3Expected);

    [Test]
    public void I3_AutoPropertyPlusEqualsAndLoopWrite_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(I3, I3Expected);

    // I4 — a chained access, an interface-typed FIELD receiver, and a lambda (ClosureLowering).
    private const string I4 = """
        Interface IBox
            Property Inner As IBox
            Property V As Integer
        End Interface

        Class Box
            Implements IBox
            Public Property Inner As IBox
            Public Property V As Integer
        End Class

        Class Holder
            Private _b As IBox
            Public Sub New(b As IBox)
                _b = b
            End Sub
            Public Function Total() As Integer
                Return _b.V + _b.Inner.V
            End Function
        End Class

        Sub Main()
            Dim b As IBox = New Box()
            b.V = 1
            b.Inner = New Box()
            b.Inner.V = 41
            Dim h As New Holder(b)
            Console.WriteLine(h.Total())
            Dim f = Function() b.V + 100
            Console.WriteLine(f())
        End Sub
        """;

    private const string I4Expected = "42\n101";

    [Test]
    public void I4_ChainedAccessFieldReceiverAndLambda_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(I4, I4Expected);

    [Test]
    public void I4_ChainedAccessFieldReceiverAndLambda_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(I4, I4Expected);

    // I6 — two implementors (an auto-property; a Get/Set that decorates its value) behind one
    // IShape parameter, so the call must dispatch through the interface. Kills the mutant that
    // resolves the accessor from an implementing CLASS instead of the interface: that mutant
    // collapses both implementors onto the first one's class token and prints "4x | 4x | 8".
    private const string I6 = """
        Interface IShape
            ReadOnly Property Area As Integer
            Property Tag As String
        End Interface

        Class Sq
            Implements IShape
            Public ReadOnly Property Area As Integer
                Get
                    Return 4
                End Get
            End Property
            Public Property Tag As String
        End Class

        Class Tri
            Implements IShape
            Private _t As String = "t"
            Public ReadOnly Property Area As Integer
                Get
                    Return 3
                End Get
            End Property
            Public Property Tag As String
                Get
                    Return _t
                End Get
                Set(value As String)
                    _t = "<" & value & ">"
                End Set
            End Property
        End Class

        Function Describe(s As IShape) As String
            s.Tag = "x"
            Return CStr(s.Area) & s.Tag
        End Function

        Sub Main()
            Dim a As IShape = New Sq()
            Dim b As IShape = New Tri()
            Console.WriteLine(Describe(a))
            Console.WriteLine(Describe(b))
            Console.WriteLine(a.Area + b.Area)
        End Sub
        """;

    private const string I6Expected = "4x\n3<x>\n7";

    [Test]
    public void I6_TwoImplementorsBehindOneInterfaceParameter_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(I6, I6Expected);

    [Test]
    public void I6_TwoImplementorsBehindOneInterfaceParameter_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(I6, I6Expected);

    // I5 — a WriteOnly write, a ReadOnly read, and the read boxed into an Object local through
    // the interface property getter (EmitInterfacePropertyGet -> EmitCoerceToSlot). Task #177
    // DONE, 2026-09-28: this used to hit the (then unrelated, pre-existing) MSIL box-into-Object
    // gap and was deliberately excluded from this fixture — see I5b below, still kept as the
    // isolated interface-only control. #177's fix makes I5 itself pass too, at both pipelines,
    // so it is promoted here rather than staying a pin of a failure.
    private const string I5 = """
        Interface IShape
            ReadOnly Property Area As Double
            WriteOnly Property Scale As Double
        End Interface

        Class Circle
            Implements IShape
            Private _r As Double = 1.0
            Public ReadOnly Property Area As Double
                Get
                    Return _r * _r * 3.0
                End Get
            End Property
            Public WriteOnly Property Scale As Double
                Set(value As Double)
                    _r = _r * value
                End Set
            End Property
        End Class

        Sub Main()
            Dim s As IShape = New Circle()
            s.Scale = 2.0
            Console.WriteLine(s.Area)
            Dim o As Object = s.Area
            Console.WriteLine(o)
        End Sub
        """;

    private const string I5Expected = "12\n12";

    // C++ refuses (Object has no C++ mapping), so this cannot go through FourBackends — MSIL
    // only, both pipelines, matching E1's own idiom below. C#/JS already agreed before #177.
    [Test]
    public void I5_ReadOnlyReadBoxedIntoAnObjectLocal_RunsOnMsil() =>
        Assert.That(FourBackends.Norm(RunExpectingSuccess(I5)), Is.EqualTo(I5Expected));

    [Test]
    public void I5_ReadOnlyReadBoxedIntoAnObjectLocal_RunsOnMsilAggressive() =>
        Assert.That(FourBackends.Norm(RunAggressiveExpectingSuccess(I5)), Is.EqualTo(I5Expected));

    // I5b — the same shape WITHOUT the `Dim o As Object = s.Area` local, isolating the interface
    // property fix (#175) from #177's own box-into-Object gap. Kept as the interface-only
    // control even now that I5 itself also passes.
    private const string I5b = """
        Interface IShape
            ReadOnly Property Area As Double
            WriteOnly Property Scale As Double
        End Interface

        Class Circle
            Implements IShape
            Private _r As Double = 1.0
            Public ReadOnly Property Area As Double
                Get
                    Return _r * _r * 3.0
                End Get
            End Property
            Public WriteOnly Property Scale As Double
                Set(value As Double)
                    _r = _r * value
                End Set
            End Property
        End Class

        Sub Main()
            Dim s As IShape = New Circle()
            s.Scale = 2.0
            Console.WriteLine(s.Area)
            Dim o As Double = s.Area
            Console.WriteLine(o)
        End Sub
        """;

    private const string I5bExpected = "12\n12";

    [Test]
    public void I5b_WriteOnlyWriteThenReadOnlyReadOfADouble_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(I5b, I5bExpected);

    [Test]
    public void I5b_WriteOnlyWriteThenReadOnlyReadOfADouble_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(I5b, I5bExpected);

    // E1 — a box on write: an Object-typed interface property assigned an Integer then a String,
    // plus Boolean and String interface properties, all accessed with mismatched case. The C++
    // backend has no mapping for Object at all (measured on I5's `Dim o As Object` local), so this
    // shape cannot go through FourBackends — MSIL only, both pipelines.
    private const string E1 = """
        Interface IBag
            Property Tag As Object
            Property Flag As Boolean
            Property Ch As String
        End Interface

        Class Bag
            Implements IBag
            Public Property Tag As Object
            Public Property Flag As Boolean
            Public Property Ch As String
        End Class

        Sub Main()
            Dim b As IBag = New Bag()
            b.Tag = 5
            Console.WriteLine(b.Tag)
            b.Tag = "s"
            Console.WriteLine(b.Tag)
            b.flag = True
            If b.FLAG Then Console.WriteLine("yes")
            b.ch = "c"
            Console.WriteLine(b.Ch & b.ch)
        End Sub
        """;

    private const string E1Expected = "5\ns\nyes\ncc";

    [Test]
    public void E1_BoxOnWriteIntoAnObjectTypedInterfaceProperty_RunsOnMsil() =>
        Assert.That(FourBackends.Norm(RunExpectingSuccess(E1)), Is.EqualTo(E1Expected));

    [Test]
    public void E1_BoxOnWriteIntoAnObjectTypedInterfaceProperty_RunsOnMsilAggressive() =>
        Assert.That(FourBackends.Norm(RunAggressiveExpectingSuccess(E1)), Is.EqualTo(E1Expected));

    // ====================================================================================
    // An IL-shape assertion: I1's IL calls the interface's own accessors and touches no
    // 'IShape' field at all. Fast — CompileToIl only, no ilasm/dotnet — but kept in this
    // Category("Integration") fixture beside the programs it reuses; see
    // MsilInterfacePropertyCompileTests for the checks that need no ilasm either.
    // ====================================================================================

    [Test]
    public void I1_Il_CallsTheInterfacesOwnAccessors_NoFieldOpOnIShape()
    {
        var il = CompileToIl(I1);
        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("callvirt instance int32 'IShape'::get_Area()"));
            Assert.That(il, Does.Contain("callvirt instance void 'IShape'::set_Label(string)"));
            Assert.That(il, Does.Contain("callvirt instance string 'IShape'::get_Label()"));
            // The pre-fix bug named storage the interface cannot have: `ldfld`/`stfld` against a
            // quoted member name on 'IShape' ('IShape'::'Area', 'IShape'::'Label'). A method call
            // (get_Area(), set_Label(...)) is unaffected by this check.
            Assert.That(il, Does.Not.Contain("'IShape'::'Area'"));
            Assert.That(il, Does.Not.Contain("'IShape'::'Label'"));
        });
    }

    // ====================================================================================
    // Class output unchanged: a CLASS-typed receiver, with two implementors of the same
    // interface in scope, still emits the CLASS token — never the interface's.
    // ====================================================================================

    private const string TwoImplementorsClassTypedRead = """
        Interface IShape
            ReadOnly Property Area As Integer
        End Interface

        Class Sq
            Implements IShape
            Public ReadOnly Property Area As Integer
                Get
                    Return 4
                End Get
            End Property
        End Class

        Class Tri
            Implements IShape
            Public ReadOnly Property Area As Integer
                Get
                    Return 3
                End Get
            End Property
        End Class

        Sub Main()
            Dim s As Sq = New Sq()
            Console.WriteLine(s.Area)
        End Sub
        """;

    [Test]
    public void ClassTypedReceiver_WithTwoImplementorsInScope_StillNamesTheClass()
    {
        var il = CompileToIl(TwoImplementorsClassTypedRead);
        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("callvirt instance int32 'Sq'::get_Area()"));
            Assert.That(il, Does.Not.Contain("callvirt instance int32 'IShape'::get_Area()"));
        });
    }
}

/// <summary>
/// The compile-only half of task #175: nothing here spawns <c>ilasm</c> or <c>dotnet</c>
/// (<see cref="MsilHarness.CompileToIl"/> only), so unlike <see cref="MsilInterfacePropertyTests"/>
/// this fixture is not <c>[Category("Integration")]</c> and runs in the fast subset.
/// </summary>
[TestFixture]
public class MsilInterfacePropertyCompileTests
{
    /// <summary>
    /// <see cref="MsilHarness.CompileToIl"/>, WITHOUT the front end's own gate
    /// (<c>Assert.That(analyzer.Analyze(ast), Is.True, …)</c>) — the analyzer runs and its errors
    /// are still THERE (nothing suppresses <c>SemanticAnalyzer.CheckPropertyRead</c>/
    /// <c>CheckPropertyWrite</c>, task #178), but a refused program is still handed to
    /// <c>IRBuilder</c>/<c>MSILCodeGenerator</c> regardless, the way E3/E4 reached the backend's
    /// own refusal BEFORE #178 existed. This is the one path left, post-#178, that can still
    /// exercise <c>EmitInterfacePropertyGet</c>/<c>Set</c>'s own <c>ForeignFeatureException</c>
    /// backstop (MSILBackend.cs:3949/3970) — a checked front end never reaches it any more.
    /// </summary>
    private static string CompileToIlFromUncheckedIr(string source, string moduleName = "MsilProbe")
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);   // errors intentionally IGNORED — see the summary above

        var module = new IRBuilder(analyzer).Build(ast, moduleName);
        return new MSILCodeGenerator().Generate(module);
    }

    // E3 — a read of a WriteOnly interface property.
    private const string E3ReadOfWriteOnly = """
        Interface IShape
            WriteOnly Property Scale As Double
        End Interface

        Class C
            Implements IShape
            Public WriteOnly Property Scale As Double
                Set(value As Double)
                End Set
            End Property
        End Class

        Sub Main()
            Dim s As IShape = New C()
            Console.WriteLine(s.Scale)
        End Sub
        """;

    /// <summary>
    /// Task #178 MOVED this pin: the front end now refuses E3 itself, with BC30524 naming
    /// 'Scale' — the MSIL <c>ForeignFeatureException</c> this used to assert is no longer what a
    /// checked compile of this program reaches at all. Asserted through
    /// <see cref="MsilHarness.CompileToIl"/> (the same front-end seam the CLI's own
    /// <c>--target=msil</c> uses — see its own <c>Assert.That(analyzer.Analyze(ast), Is.True, …)</c>),
    /// which is what the shift from "the backend refuses" to "the front end refuses first" means
    /// operationally: <c>CompileToIl</c> now throws on the semantic-error assertion, not on
    /// <c>MSILCodeGenerator</c>. <see cref="E3_BackstopStillThrows_WhenFedUncheckedIr"/> below
    /// keeps the backend's OWN refusal covered, from the one place left that can still reach it.
    /// </summary>
    [Test]
    public void E3_ReadOfAWriteOnlyInterfaceProperty_RefusedByTheFrontEnd_WithBC30524()
    {
        var parser = new Parser(new Lexer(E3ReadOfWriteOnly).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var match = analyzer.Errors.FirstOrDefault(e => e.ErrorCode == "BC30524");
        Assert.That(match, Is.Not.Null,
            "expected BC30524; got: " + string.Join(" | ", analyzer.Errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
        Assert.That(match!.Message, Does.Contain("'Scale'"));
    }

    /// <summary>The MSIL backend's own WriteOnly-interface-read refusal (MSILBackend.cs:3949) is
    /// still THERE and still throws — only reachable, post-#178, from IR the front end's own gate
    /// was bypassed for (<see cref="CompileToIlFromUncheckedIr"/>), never from a checked
    /// compile.</summary>
    [Test]
    public void E3_BackstopStillThrows_WhenFedUncheckedIr()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => CompileToIlFromUncheckedIr(E3ReadOfWriteOnly));
        Assert.That(ex!.Message, Does.Contain("IShape").And.Contain("Scale"));
    }

    // E4 — a write to a ReadOnly interface property.
    private const string E4WriteToReadOnly = """
        Interface IShape
            ReadOnly Property Area As Integer
        End Interface

        Class C
            Implements IShape
            Public ReadOnly Property Area As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Sub Main()
            Dim s As IShape = New C()
            s.Area = 4
            Console.WriteLine(s.Area)
        End Sub
        """;

    /// <summary>Task #178 MOVED this pin the same way as E3's above: the front end now refuses
    /// E4 itself, with BC30526 naming 'Area'.</summary>
    [Test]
    public void E4_WriteToAReadOnlyInterfaceProperty_RefusedByTheFrontEnd_WithBC30526()
    {
        var parser = new Parser(new Lexer(E4WriteToReadOnly).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var match = analyzer.Errors.FirstOrDefault(e => e.ErrorCode == "BC30526");
        Assert.That(match, Is.Not.Null,
            "expected BC30526; got: " + string.Join(" | ", analyzer.Errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
        Assert.That(match!.Message, Does.Contain("'Area'"));
    }

    /// <summary>The MSIL backend's own ReadOnly-interface-write refusal (MSILBackend.cs:3970),
    /// same idiom as <see cref="E3_BackstopStillThrows_WhenFedUncheckedIr"/>.</summary>
    [Test]
    public void E4_BackstopStillThrows_WhenFedUncheckedIr()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => CompileToIlFromUncheckedIr(E4WriteToReadOnly));
        Assert.That(ex!.Message, Does.Contain("IShape").And.Contain("Area"));
    }

    // E6 — a receiver carrying type arguments against a non-generic interface. The front end lets
    // `s As IShape(Of Integer)` name a non-generic interface (measured); emitting the bare
    // 'IShape' token for it would quietly bind to a type the program did not write, so the
    // backend refuses instead. `Show` is declared but never called — the whole module is
    // compiled regardless.
    private const string E6GenericArguments = """
        Interface IShape
            ReadOnly Property Area As Integer
        End Interface

        Sub Show(s As IShape(Of Integer))
            Console.WriteLine(s.Area)
        End Sub

        Sub Main()
            Console.WriteLine("ok")
        End Sub
        """;

    [Test]
    public void E6_ReceiverWithTypeArgumentsAgainstANonGenericInterface_ThrowsForeignFeatureNamingTheMember()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => CompileToIl(E6GenericArguments));
        Assert.That(ex!.Message, Does.Contain("IShape").And.Contain("Area"));
    }
}
