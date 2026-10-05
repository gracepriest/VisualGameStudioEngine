using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;
using VisualGameStudio.Tests.Native;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Task #177, execution — MSIL: boxing a value into an <c>Object</c> slot, converting out of
/// one, and comparing Objects late-bound, taken all the way to a running program.
/// <see cref="MsilObjectBoxingTests"/> is the fast-subset sibling that pins the emitted IL text.
///
/// <para>Expected strings are the implementer's measured <c>.exp</c> files
/// (<c>S/t177/{probes,edge,cmp,lb}/*.exp</c>), which hold C#'s answer — or VB's own, named per
/// probe where C# itself refuses or diverges. They are never re-derived from what MSIL prints,
/// the backend under test.</para>
///
/// <para><b>MSIL runs at all three entry points</b> for every probe below:
/// <see cref="RunExpectingSuccess"/> (the CLI, standard pipeline), <see cref="RunAggressiveExpectingSuccess"/>
/// (CLI <c>--optimize</c>, aggressive pipeline) and <see cref="BuildReleaseMsilAndRun"/> (a Release
/// <c>.blproj</c> build through <c>BasicCompiler.CompileProjectFiles</c> — a different code path
/// than <c>MsilHarness.CompileToIl</c>'s direct pipeline call, per CLAUDE.md's "test both entry
/// points"). Same pattern as <see cref="MsilValueToStringExecutionTests"/> /
/// <see cref="MsilBinaryOperandCoercionTests"/>.</para>
///
/// <para><b>C++ refuses every probe here by design</b> ("Object has no C++ mapping") — a
/// pre-existing, total, unrelated gap, asserted once per probe via <c>Throws.Exception</c>, never
/// a skipped test.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class MsilObjectBoxingExecutionTests
{
    // ====================================================================================
    // Shared helpers.
    // ====================================================================================

    private static string Norm(string s) => FourBackends.Norm(s);

    /// <summary>C++ refuses every probe in this fixture: <c>Object</c> has no C++ mapping at
    /// all, a pre-existing, total, unrelated gap.</summary>
    private static void AssertCppRefuses(string program) =>
        Assert.That(() => BclE2E.CompileToCppOptimized(program), Throws.Exception,
            "C++ — Object has no C++ mapping at all (pre-existing, unrelated to #177)");

    /// <summary>MSIL, all three entry points, all answering <paramref name="expected"/>.</summary>
    private void AssertMsilAllEntryPoints(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL CLI");
            Assert.That(Norm(RunAggressiveExpectingSuccess(program)), Is.EqualTo(expected), "MSIL CLI --optimize");
            Assert.That(Norm(BuildReleaseMsilAndRun(program)), Is.EqualTo(expected), "MSIL Release .blproj");
        });
    }

    /// <summary>The common shape: C# and MSIL(x3) agree with <paramref name="expected"/>, JS
    /// agrees too unless <paramref name="jsRuns"/> is false (a by-design JS refusal unrelated to
    /// #177 — Long/BL7003, Char/BL7004), C++ always refuses.</summary>
    private void AssertContractProbe(string program, string expected, bool jsRuns = true)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            if (jsRuns)
                Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            AssertCppRefuses(program);
        });
        AssertMsilAllEntryPoints(program, expected);
    }

    // ====================================================================================
    // The Release .blproj entry point (the CLI's "-c Release" -> OptimizeAggressive mapping,
    // Program.cs), a different path through the compiler than MsilHarness.CompileToIl's direct
    // AggressivePipeline.Apply call. The CLI's MSIL build step only emits the .il — no ilasm
    // call of its own — so ilasm/dotnet still run through MsilHarness.RunIl.
    // ====================================================================================

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-msilbox-" + Guid.NewGuid().ToString("N"));
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

        return RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App");
    }

    // ====================================================================================
    // 1. O1-O7 — the implementer's own contract probes (S/t177/probes).
    // ====================================================================================

    private const string O1 = """
        Function F() As Double
            Return 12.0
        End Function
        Sub Main()
            Dim d As Double = 12.0
            Dim o As Object = d
            Console.WriteLine(o)
            Dim p As Object = F()
            Console.WriteLine(p)
        End Sub
        """;

    [Test]
    public void O1_ValueLocalAndFunctionResultIntoObject() => AssertContractProbe(O1, "12\n12");

    private const string O2 = """
        Interface IShape
            ReadOnly Property Area As Integer
        End Interface
        Class Sq
            Implements IShape
            Public ReadOnly Property Area As Integer
                Get
                    Return 16
                End Get
            End Property
        End Class
        Sub Show(o As Object)
            Console.WriteLine(o)
        End Sub
        Function Get1(s As Sq) As Object
            Return s.Area
        End Function
        Sub Main()
            Dim s As New Sq()
            Show(s.Area)
            Console.WriteLine(Get1(s))
            Dim o As Object = s.Area
            Console.WriteLine(o)
            Console.WriteLine(CStr(s.Area) & "!")
        End Sub
        """;

    [Test]
    public void O2_IntegerIntoObjectParameter_ReturnAndLocal() => AssertContractProbe(O2, "16\n16\n16\n16!");

    private const string O3 = """
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
            Dim b As New Bag()
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

    [Test]
    public void O3_InterfacePropertySetter_BoxesIntoObjectAutoProperty() => AssertContractProbe(O3, "5\ns\nyes\ncc");

    private const string O4 = """
        Class Box
            Public O As Object
        End Class
        Sub Main()
            Dim b As New Box()
            b.O = 3.5
            Console.WriteLine(b.O)
            Dim t As Object = True
            Console.WriteLine(t)
            Dim c As Object = "x"c
            Console.WriteLine(c)
            Dim n As Long = 9000000000
            Dim ln As Object = n
            Console.WriteLine(ln)
        End Sub
        """;

    [Test]
    public void O4_FieldBooleanCharLong_IntoObject() => AssertContractProbe(O4, "3.5\nTrue\nx\n9000000000", jsRuns: false);

    private const string O5 = """
        Sub Main()
            Dim arr(2) As Object
            arr(0) = 5
            arr(1) = "five"
            Console.WriteLine(arr(0))
            Console.WriteLine(arr(1))
            Dim l As New List(Of Object)()
            l.Add(7)
            l.Add(2.5)
            Console.WriteLine(l(0))
            Console.WriteLine(l(1))
        End Sub
        """;

    [Test]
    public void O5_ArrayElementAndListOfObject_Box() => AssertContractProbe(O5, "5\nfive\n7\n2.5");

    private const string O6 = """
        Sub Show(o As Object)
            Console.WriteLine(o)
        End Sub
        Function Pick(k As Integer) As Object
            If k = 1 Then Return 42
            Return False
        End Function
        Sub Main()
            Show(True)
            Show("q"c)
            Show(1.25)
            Console.WriteLine(Pick(1))
            Console.WriteLine(Pick(2))
        End Sub
        """;

    [Test]
    public void O6_BooleanCharDoubleArgument_AndReturnFromAsObject() =>
        AssertContractProbe(O6, "True\nq\n1.25\n42\nFalse", jsRuns: false);

    private const string O7 = """
        Sub Main()
            Dim o As Object = 20
            Dim n As Integer = CInt(o) + 1
            Console.WriteLine(n)
            Dim d As Object = 1.5
            Dim x As Double = CDbl(d) * 2
            Console.WriteLine(x)
        End Sub
        """;

    [Test]
    public void O7_UnboxingConversions_CIntAndCDbl() => AssertContractProbe(O7, "21\n3");

    // ====================================================================================
    // 2. Edge probes that now run clean on MSIL (S/t177/edge).
    // ====================================================================================

    private const string E01 = """
        Function Classify(k As Integer) As Object
            Dim o As Object = Nothing
            If k = 1 Then
                o = 10
            ElseIf k = 2 Then
                o = 2.5
            Else
                Select Case k
                    Case 3
                        o = True
                    Case 4, 5
                        o = "four-five"
                    Case Else
                        o = 7L
                End Select
            End If
            Return o
        End Function
        Sub Main()
            For k As Integer = 1 To 6
                Console.WriteLine(Classify(k))
            Next
            Dim n As Integer = 3
            Dim r As Object
            Select Case n
                Case 3
                    r = n * 2
                Case Else
                    r = 0
            End Select
            Console.WriteLine(r)
        End Sub
        """;

    [Test]
    public void E01_AssignInIfAndSelectCase() =>
        // JS refuses: the Long literal 7L (BL7003), a pre-existing, unrelated restriction.
        AssertContractProbe(E01, "10\n2.5\nTrue\nfour-five\nfour-five\n7\n6", jsRuns: false);

    private const string E02 = """
        Function Make() As Object
            Return 20
        End Function
        Sub Main()
            Dim o As Object = Make()
            If o = 20 Then
                Console.WriteLine("eq")
            Else
                Console.WriteLine("ne")
            End If
            Select Case o
                Case 20
                    Console.WriteLine("twenty")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub
        """;

    [Test]
    public void E02_CompareObjectInIfAndSelectCase_JsAndMsilAgree()
    {
        // C# itself refuses this program (CS0019 — see the #211 pin group below), so it is not
        // part of this assertion; JS and MSIL both agree with VB's late-bound answer.
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E02)), Is.EqualTo("eq\ntwenty"), "JavaScript");
            AssertCppRefuses(E02);
        });
        AssertMsilAllEntryPoints(E02, "eq\ntwenty");
    }

    private const string E03 = """
        Sub SetIt(ByRef o As Object, v As Integer)
            o = v * 3
        End Sub
        Sub SetD(ByRef o As Object)
            o = 1.5
        End Sub
        Sub Main()
            Dim a As Object = Nothing
            SetIt(a, 4)
            Console.WriteLine(a)
            SetD(a)
            Console.WriteLine(a)
            Dim b As Object = "s"
            SetIt(b, 5)
            Console.WriteLine(CInt(b) + 1)
        End Sub
        """;

    [Test]
    public void E03_ObjectByRef() =>
        // JS refuses: a ByRef Object parameter (BL7002), a pre-existing, unrelated restriction.
        AssertContractProbe(E03, "12\n1.5\n16", jsRuns: false);

    private const string E04 = """
        Sub Main()
            Dim d As New Dictionary(Of String, Object)()
            d("a") = 5
            d.Add("b", 2.5)
            d("c") = True
            d("s") = "str"
            Console.WriteLine(d("a"))
            Console.WriteLine(d("b"))
            Console.WriteLine(d("c"))
            Console.WriteLine(d("s"))
            Console.WriteLine(CInt(d("a")) + 1)
            Console.WriteLine(CDbl(d("b")) * 2)
            Console.WriteLine(d.Count)
        End Sub
        """;

    [Test]
    public void E04_DictionaryOfStringObject() =>
        AssertContractProbe(E04, "5\n2.5\nTrue\nstr\n6\n5\n4");

    private const string E06 = """
        Sub Main()
            Dim i As Object = 42
            Dim d As Object = 2.5
            Dim b As Object = False
            Console.WriteLine(CStr(i))
            Console.WriteLine(CStr(d))
            Console.WriteLine(CStr(b))
            Console.WriteLine(i.ToString())
            Console.WriteLine(d.ToString())
            Console.WriteLine(b.ToString())
            Console.WriteLine(CStr(i) & "|" & CStr(d) & "|" & CStr(b))
        End Sub
        """;
    private const string E06Expected = "42\n2.5\nFalse\n42\n2.5\nFalse\n42|2.5|False";

    [Test]
    public void E06_CStrAndToStringOnBoxedValues()
    {
        // JS is excluded here: `i.ToString()` on a boxed value throws "i.ToString is not a
        // function" on the JavaScript backend — a separate, pre-existing, unrelated gap (not
        // filed under #177; JS does not model a boxed primitive as an object with methods).
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E06)), Is.EqualTo(E06Expected), "C#");
            AssertCppRefuses(E06);
        });
        AssertMsilAllEntryPoints(E06, E06Expected);
    }

    private const string E07 = """
        Sub Main()
            Dim o As Object = Nothing
            Dim setIt As Action = Sub()
                                      o = 99
                                  End Sub
            setIt()
            Console.WriteLine(o)
            Dim wrap = Function(n As Integer) As Object
                           Return n + 1
                       End Function
            Console.WriteLine(wrap(41))
            Dim show As Func(Of Object, String) = Function(x As Object) CStr(x) & "!"
            Console.WriteLine(show(7))
            Console.WriteLine(show(1.5))
            Dim k As Integer = 5
            Dim capt = Function() As Object
                           Return k * 10
                       End Function
            Console.WriteLine(capt())
        End Sub
        """;
    private const string E07Expected = "99\n42\n7!\n1.5!\n50";

    [Test]
    public void E07_LambdaBoxing_JsAndMsilAgree()
    {
        // C# is asserted separately below (hang-safe runner); Cpp refuses the program.
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E07)), Is.EqualTo(E07Expected), "JavaScript");
            AssertCppRefuses(E07);
        });
        AssertMsilAllEntryPoints(E07, E07Expected);
    }

    /// <summary>
    /// ⭐ MOVED PIN (#136). E07 on C# USED TO print <c>42\n7!\n1.5!\n50</c>, vbc's answer without its first line: a Sub lambda's write
    /// to a captured Object variable (<c>o = 99</c> — a value renamed <c>o</c>, which the old lambda loop skipped as a temp) was lost,
    /// so the '99' line never printed. The lambda body is written by the function-body emitter now and C# prints
    /// <see cref="E07Expected"/>, like JavaScript and MSIL. Hang-safe runner, both pipelines.
    /// </summary>
    [Test]
    public void E07_LambdaBoxing_CSharpAgrees()
    {
        Assert.That(Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(E07))), Is.EqualTo(E07Expected), "C#");
        Assert.That(Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(E07))), Is.EqualTo(E07Expected), "C#, aggressive");
    }

    private const string E08 = """
        Sub Main()
            Dim i As Integer = 123456
            Dim o As Object = i
            Dim l As Long = CLng(o)
            Console.WriteLine(l * 100000)
            Dim l2 As Long = CType(o, Long)
            Console.WriteLine(l2 + 1)
            Dim s As Single = CSng(o)
            Console.WriteLine(s)
        End Sub
        """;

    [Test]
    public void E08_IntegerIntoObjectIntoLong() =>
        // JS refuses: Long (BL7003), a pre-existing, unrelated restriction.
        AssertContractProbe(E08, "12345600000\n123457\n123456", jsRuns: false);

    private const string E09 = """
        Sub Main()
            Dim o As Object = Nothing
            Console.WriteLine(o Is Nothing)
            o = 5
            Console.WriteLine(o)
            Console.WriteLine(o Is Nothing)
            o = Nothing
            Console.WriteLine(o Is Nothing)
            Console.WriteLine("[" & CStr(o) & "]")
            o = 2.25
            Console.WriteLine(o)
        End Sub
        """;

    [Test]
    public void E09_NothingThenAValue() =>
        AssertContractProbe(E09, "True\n5\nFalse\nTrue\n[]\n2.25");

    private const string E10 = """
        Sub Main()
            Dim o As Object = 20
            Dim n As Integer = DirectCast(o, Integer)
            Console.WriteLine(n + 1)
            Dim sh As Object = CType(7, Short)
            Dim s As Short = CType(sh, Short)
            Console.WriteLine(s * 2)
            Try
                Dim bad As Short = CType(o, Short)
                Console.WriteLine(bad)
            Catch ex As Exception
                Console.WriteLine("cast failed")
            End Try
            Dim c As Object = "z"c
            Dim ch As Char = CType(c, Char)
            Console.WriteLine(ch)
        End Sub
        """;

    [Test]
    public void E10_UnboxCasts_DirectCastAndCType() =>
        // JS refuses: Char (BL7004), a pre-existing, unrelated restriction.
        AssertContractProbe(E10, "21\n14\ncast failed\nz", jsRuns: false);

    private const string E11 = """
        Sub Main()
            Dim i As Object = 5
            Dim d As Object = 0.5
            Dim b As Object = True
            Dim s As Object = "txt"
            Dim n As Object = Nothing
            Console.WriteLine("i=" & i)
            Console.WriteLine("d=" & d)
            Console.WriteLine("b=" & b)
            Console.WriteLine("s=" & s)
            Console.WriteLine("n=[" & n & "]")
            Console.WriteLine(i & "/" & d)
        End Sub
        """;

    [Test]
    public void E11_ConcatOfBoxedObjects() =>
        AssertContractProbe(E11, "i=5\nd=0.5\nb=True\ns=txt\nn=[]\n5/0.5");

    private const string E12 = """
        Dim g As Object = 7
        Class Holder
            Public F As Object = 3
            Public Shared S As Object = 4
        End Class
        Sub Main()
            Dim n As Integer = 11
            Dim o As Object = CType(n, Object)
            Console.WriteLine(o)
            Dim a() As Object = {1, "x", 2.5, True}
            For Each e As Object In a
                Console.WriteLine(e)
            Next
            Console.WriteLine(g)
            g = 8
            Console.WriteLine(g)
            Dim h As New Holder()
            Console.WriteLine(h.F)
            Console.WriteLine(Holder.S)
            Holder.S = 4.5
            Console.WriteLine(Holder.S)
        End Sub
        """;
    private const string E12Expected = "11\n1\nx\n2.5\nTrue\n7\n8\n3\n4\n4.5";

    [Test]
    public void E12_CastsArrayLiteralGlobalsAndFields_CSharpAndMsilAgree()
    {
        // JS is excluded here: `CType(n, Object)` has no JavaScript IRCast lowering at all — a
        // separate, pre-existing, unrelated gap, not filed under #177.
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E12)), Is.EqualTo(E12Expected), "C#");
            AssertCppRefuses(E12);
        });
        AssertMsilAllEntryPoints(E12, E12Expected);
    }

    private const string E13 = """
        Class Box
            Public ReadOnly Property P As Object
                Get
                    Return 5
                End Get
            End Property
            Private _v As Object
            Public Property V As Object
                Get
                    Return _v
                End Get
                Set(value As Object)
                    _v = value
                End Set
            End Property
            Public Sub Bump()
                V = 9
            End Sub
        End Class
        Function Guarded(k As Integer) As Object
            Try
                If k > 0 Then Return k * 2
                Return False
            Finally
                Console.WriteLine("fin")
            End Try
        End Function
        Sub Main()
            Console.WriteLine(Guarded(4))
            Console.WriteLine(Guarded(0))
            Dim b As New Box()
            Console.WriteLine(b.P)
            b.V = 1.25
            Console.WriteLine(b.V)
            b.Bump()
            Console.WriteLine(b.V)
        End Sub
        """;

    [Test]
    public void E13_TryFinallyReturn_AndPropertyGetSet() =>
        AssertContractProbe(E13, "fin\n8\nfin\nFalse\n5\n1.25\n9");

    private const string E14 = """
        Sub Main()
            Dim half As Object = 2.5
            Console.WriteLine(CInt(half))
            Dim txt As Object = "12"
            Console.WriteLine(CInt(txt) + 1)
            Console.WriteLine(CDbl(txt) / 8)
            Dim t As Object = True
            Console.WriteLine(CBool(t))
            Dim z As Object = 0
            Console.WriteLine(CBool(z))
            Console.WriteLine(CLng(half))
            Try
                Dim bad As Object = "abc"
                Console.WriteLine(CInt(bad))
            Catch ex As FormatException
                Console.WriteLine("format")
            End Try
        End Sub
        """;
    private const string E14Expected = "2\n13\n1.5\nTrue\nFalse\n2\nformat";

    [Test]
    public void E14_ConvertFromObject_CSharpAndMsilAgree()
    {
        // JS is excluded here: no lowering for 'CLng' at all — a separate, pre-existing,
        // unrelated gap, not filed under #177.
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E14)), Is.EqualTo(E14Expected), "C#");
            AssertCppRefuses(E14);
        });
        AssertMsilAllEntryPoints(E14, E14Expected);
    }

    private const string E15 = """
        Class MathUtil
            Public Shared Function Twice(n As Integer) As Integer
                Return n * 2
            End Function
            Public Shared Function Wrap(o As Object) As String
                Return "<" & CStr(o) & ">"
            End Function
        End Class
        Interface IShape
            ReadOnly Property Area As Double
            Function Sides() As Integer
            Sub Take(o As Object)
        End Interface
        Class Sq
            Implements IShape
            Public ReadOnly Property Area As Double
                Get
                    Return 2.5
                End Get
            End Property
            Public Function Sides() As Integer
                Return 4
            End Function
            Public Sub Take(o As Object)
                Console.WriteLine("took " & CStr(o))
            End Sub
        End Class
        Sub Main()
            Dim o As Object = MathUtil.Twice(3)
            Console.WriteLine(o)
            Console.WriteLine(MathUtil.Wrap(8))
            Dim s As IShape = New Sq()
            Dim a As Object = s.Area
            Console.WriteLine(a)
            Dim n As Object = s.Sides()
            Console.WriteLine(n)
            s.Take(6)
            s.Take(0.5)
        End Sub
        """;

    [Test]
    public void E15_StaticMethodAndInterfaceMethodResults_IntoObject() =>
        AssertContractProbe(E15, "6\n<8>\n2.5\n4\ntook 6\ntook 0.5");

    private const string E16 = """
        Sub Show(Optional o As Object = 5)
            Console.WriteLine(o)
        End Sub
        Sub Main()
            Show()
            Show(3.5)
        End Sub
        """;
    private const string E16Expected = "5\n3.5";

    [Test]
    public void E16_OptionalObjectParameter_JsAndMsilAgree()
    {
        // Pinned separately below: C# refuses to compile this shape at all (#216), unrelated to
        // #177's own boxing fix.
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E16)), Is.EqualTo(E16Expected), "JavaScript");
            AssertCppRefuses(E16);
        });
        AssertMsilAllEntryPoints(E16, E16Expected);
    }

    private const string E18 = """
        Class Base
            Public Tag As Object
            Public Sub New(t As Object)
                Tag = t
            End Sub
            Public Shared Function Twice(n As Integer) As Integer
                Return n * 2
            End Function
        End Class
        Class Derived
            Inherits Base
            Public Sub New()
                MyBase.New(5)
            End Sub
        End Class
        Class Counter
            Public Item As Object
            Public Shared Last As Object
            Private Shared _shared As Object
            Public Shared Property SharedProp As Object
                Get
                    Return _shared
                End Get
                Set(value As Object)
                    _shared = value
                End Set
            End Property
            Public Sub Fill()
                Item = 12
                Last = 2.5
                SharedProp = True
            End Sub
        End Class
        Sub Reassign(o As Object)
            o = 77
            Console.WriteLine(o)
        End Sub
        Sub Main()
            Dim b As New Base(3.25)
            Console.WriteLine(b.Tag)
            Dim d As New Derived()
            Console.WriteLine(d.Tag)
            Dim c As New Counter()
            c.Fill()
            Console.WriteLine(c.Item)
            Console.WriteLine(Counter.Last)
            Console.WriteLine(Counter.SharedProp)
            Reassign("s")
            Dim keyed As New Dictionary(Of Object, String)()
            keyed(1) = "one"
            keyed(2.5) = "two-and-a-half"
            Console.WriteLine(keyed(1))
            Console.WriteLine(keyed(2.5))
            Console.WriteLine(Derived.Twice(4))
        End Sub
        """;

    [Test]
    public void E18_RemainingStoreArms_ConstructorSharedFieldsAndDictionaryKeys() =>
        AssertContractProbe(E18, "3.25\n5\n12\n2.5\nTrue\n77\none\ntwo-and-a-half\n8");

    private const string E19 = """
        Function Make(k As Integer) As Object
            Select Case k
                Case 1
                    Return 5
                Case 2
                    Return 0.5
                Case 3
                    Return True
                Case 4
                    Return "txt"
                Case Else
                    Return Nothing
            End Select
        End Function
        Sub Main()
            For k As Integer = 1 To 5
                Console.WriteLine("v" & k & "=[" & Make(k) & "]")
            Next
            Dim o As Object = Make(2)
            Console.WriteLine(Make(1) & "/" & o)
        End Sub
        """;

    [Test]
    public void E19_ConcatOfANonConstantObject() =>
        AssertContractProbe(E19, "v1=[5]\nv2=[0.5]\nv3=[True]\nv4=[txt]\nv5=[]\n5/0.5");

    // ====================================================================================
    // 3. C1/C2 (S/t177/cmp, S/t177/lb) and L01-L10, L12 (S/t177/lb) — the late-bound
    //    comparison, taken to execution.
    // ====================================================================================

    private const string C1 = """
        Function MakeI() As Object
            Return 20
        End Function
        Function MakeD() As Object
            Return 2.5
        End Function
        Function MakeB() As Object
            Return True
        End Function
        Function MakeS() As Object
            Return "x"
        End Function
        Sub Main()
            Dim i As Object = MakeI()
            Dim d As Object = MakeD()
            Dim b As Object = MakeB()
            Dim s As Object = MakeS()
            Console.WriteLine(i = 20)
            Console.WriteLine(i <> 20)
            Console.WriteLine(d = 2.5)
            Console.WriteLine(b = True)
            Console.WriteLine(s = "x")
            Dim local As Object = 7
            Console.WriteLine(local = 7)
        End Sub
        """;
    private const string C1Expected = "True\nFalse\nTrue\nTrue\nTrue\nTrue";

    [Test]
    public void C1_LateBoundEquality_JsAndMsilAgree()
    {
        // C# itself refuses (CS0019 — see the #211 pin group below).
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(C1)), Is.EqualTo(C1Expected), "JavaScript");
            AssertCppRefuses(C1);
        });
        AssertMsilAllEntryPoints(C1, C1Expected);
    }

    private const string C2 = "Sub Main()\n Dim i As Object = 20\n Console.WriteLine(i > 5)\nEnd Sub\n";

    [Test]
    public void C2_LateBoundOrdering_AllFourEntryPointsAgree() => AssertContractProbe(C2, "True");

    private const string L01 = """
        Function MakeI() As Object
            Return 20
        End Function
        Sub Main()
            Dim o As Object = MakeI()
            Console.WriteLine(o = 20.0)
            Console.WriteLine(o = 20.5)
            Console.WriteLine(o < 20.5)
            Dim d As Double = 20
            Console.WriteLine(o = d)
            Dim p As Object = 20.0
            Console.WriteLine(o = p)
        End Sub
        """;
    private const string L01Expected = "True\nFalse\nTrue\nTrue\nTrue";

    [Test]
    public void L01_IntegerObjectVersusDouble_JsAndMsilAgree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L01)), Is.EqualTo(L01Expected), "JavaScript");
            AssertCppRefuses(L01);
        });
        AssertMsilAllEntryPoints(L01, L01Expected);
    }

    private const string L02 = """
        Function MakeS() As Object
            Return "abc"
        End Function
        Sub Main()
            Dim o As Object = MakeS()
            Dim s As String = "ab"
            s = s & "c"
            Console.WriteLine(o = s)
            Console.WriteLine(o <> s)
            Console.WriteLine(o = "abd")
            Console.WriteLine("abc" = o)
            Console.WriteLine(o = "ABC")
            If o = s Then
                Console.WriteLine("if-eq")
            End If
        End Sub
        """;

    [Test]
    public void L02_StringInObject_AllFourEntryPointsAgree() =>
        AssertContractProbe(L02, "True\nFalse\nFalse\nTrue\nFalse\nif-eq");

    private const string L03 = """
        Function MakeB() As Object
            Return True
        End Function
        Sub Main()
            Dim o As Object = MakeB()
            Console.WriteLine(o = True)
            Console.WriteLine(o = False)
            Console.WriteLine(o <> False)
            Dim f As Boolean = False
            Dim p As Object = f
            Console.WriteLine(p = False)
            Console.WriteLine(o = p)
            If o = True Then
                Console.WriteLine("if-true")
            End If
        End Sub
        """;
    private const string L03Expected = "True\nFalse\nTrue\nTrue\nFalse\nif-true";

    [Test]
    public void L03_Boolean_JsAndMsilAgree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L03)), Is.EqualTo(L03Expected), "JavaScript");
            AssertCppRefuses(L03);
        });
        AssertMsilAllEntryPoints(L03, L03Expected);
    }

    private const string L04 = """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeD() As Object
            Return 2.5
        End Function
        Sub Main()
            Dim o As Object = Make(7)
            Console.WriteLine(o < 10)
            Console.WriteLine(o > 10)
            Console.WriteLine(o <= 7)
            Console.WriteLine(o >= 8)
            Console.WriteLine(5 < o)
            Console.WriteLine(o < 7)
            Console.WriteLine(o >= 7)
            Console.WriteLine(o > 7)
            Dim d As Object = MakeD()
            Console.WriteLine(d > 2)
            Console.WriteLine(d < 3)
            If o > 5 Then
                Console.WriteLine("gt5")
            End If
            Dim k As Integer = 0
            Dim c As Object = 0
            Do While c < 3
                k = k + 1
                c = k
            Loop
            Console.WriteLine(k)
            While c > 0
                k = k - 1
                c = k
            End While
            Console.WriteLine(k)
        End Sub
        """;
    private const string L04Expected =
        "True\nFalse\nTrue\nFalse\nTrue\nFalse\nTrue\nFalse\nTrue\nTrue\ngt5\n3\n0";

    [Test]
    public void L04_Ordering_JsAndMsilAgree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L04)), Is.EqualTo(L04Expected), "JavaScript");
            AssertCppRefuses(L04);
        });
        AssertMsilAllEntryPoints(L04, L04Expected);
    }

    private const string L05 = """
        Function MakeN() As Object
            Return Nothing
        End Function
        Sub Main()
            Dim o As Object = MakeN()
            Console.WriteLine(o = 0)
            Console.WriteLine(o <> 0)
            Console.WriteLine(o = "")
            Console.WriteLine(o = False)
            Console.WriteLine(o < 1)
            If o = 0 Then
                Console.WriteLine("nothing-is-zero")
            End If
        End Sub
        """;
    private const string L05Expected = "True\nFalse\nTrue\nTrue\nTrue\nnothing-is-zero";

    [Test]
    public void L05_NothingEqualsZero_MsilAgreesWithVb()
    {
        // JS is pinned separately below (#215) — it disagrees with VB here.
        AssertCppRefuses(L05);
        AssertMsilAllEntryPoints(L05, L05Expected);
    }

    private const string L06 = """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim a As Object = Make(5)
            Dim b As Object = Make(5)
            Dim c As Object = 5.0
            Dim t As String = "h"
            t = t & "i"
            Dim s1 As Object = "hi"
            Dim s2 As Object = t
            Console.WriteLine(a = b)
            Console.WriteLine(a <> b)
            Console.WriteLine(a = c)
            Console.WriteLine(s1 = s2)
            Dim n1 As Object = Nothing
            Dim n2 As Object = Nothing
            Console.WriteLine(n1 = n2)
            If a = b Then
                Console.WriteLine("if-eq")
            End If
        End Sub
        """;
    private const string L06Expected = "True\nFalse\nTrue\nTrue\nTrue\nif-eq";

    [Test]
    public void L06_ObjectVersusObject_JsAndMsilAgree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L06)), Is.EqualTo(L06Expected), "JavaScript");
            AssertCppRefuses(L06);
        });
        AssertMsilAllEntryPoints(L06, L06Expected);
    }

    private const string L07 = """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Test(o As Object)
            Select Case o
                Case 1
                    Console.WriteLine("one")
                Case 2, 3
                    Console.WriteLine("two-three")
                Case 4 To 6
                    Console.WriteLine("four-six")
                Case Is > 100
                    Console.WriteLine("big")
                Case Is <> 50
                    Console.WriteLine("not-fifty")
                Case Else
                    Console.WriteLine("fifty")
            End Select
        End Sub
        Sub Main()
            Test(Make(1))
            Test(Make(3))
            Test(Make(5))
            Test(Make(200))
            Test(Make(7))
            Test(Make(50))
            Test(2.0)
            Test(Make(100))
            Test(Make(4))
            Test(Make(6))
        End Sub
        """;
    private const string L07Expected =
        "one\ntwo-three\nfour-six\nbig\nnot-fifty\nfifty\ntwo-three\nnot-fifty\nfour-six\nfour-six";

    [Test]
    public void L07_SelectCaseWithRangesAndRelationalCases_JsAndMsilAgree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L07)), Is.EqualTo(L07Expected), "JavaScript");
            AssertCppRefuses(L07);
        });
        AssertMsilAllEntryPoints(L07, L07Expected);
    }

    private const string L08 = """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeN() As Object
            Return Nothing
        End Function
        Sub Test(o As Object)
            Select Case o
                Case Nothing
                    Console.WriteLine("nothing")
                Case Else
                    Console.WriteLine("something")
            End Select
        End Sub
        Sub TestIs(o As Object)
            Select Case o
                Case Is Nothing
                    Console.WriteLine("is-nothing")
                Case Else
                    Console.WriteLine("not-nothing")
            End Select
        End Sub
        Sub Guard(o As Object, limit As Integer)
            Select Case o
                Case Is > 0 When o < limit
                    Console.WriteLine("in-range")
                Case Is > 0
                    Console.WriteLine("over")
                Case Else
                    Console.WriteLine("non-positive")
            End Select
        End Sub
        Sub Main()
            Test(Make(0))
            Test(MakeN())
            Test(Make(5))
            Test("")
            Test(False)
            TestIs(Make(0))
            TestIs(MakeN())
            TestIs("")
            TestIs(False)
            Guard(Make(3), 10)
            Guard(Make(30), 10)
            Guard(Make(-1), 10)
        End Sub
        """;
    // ⚠ `limit` is an Integer, not an Object: the guard is a late-bound Object-vs-number
    // comparison, like `If o < 20` in L01. Object-vs-Object ordering is refused by the analyzer
    // ("Comparison operator '<' requires numeric operands") in a statement, and — since #115 made
    // Case patterns analyzed — in a When guard too. It once passed here only because guards were
    // skipped.
    private const string L08Expected =
        "nothing\nnothing\nsomething\nnothing\nnothing\nnot-nothing\nis-nothing\nnot-nothing\n"
        + "not-nothing\nin-range\nover\nnon-positive";

    [Test]
    public void L08_CaseNothingCaseIsNothingAndAGuard_MsilAgreesWithVb()
    {
        // JS is pinned separately below (#215) — it disagrees with VB on `Case Nothing` and the
        // guard. C# itself refuses (CS0019 — see the #211 pin group below).
        AssertCppRefuses(L08);
        AssertMsilAllEntryPoints(L08, L08Expected);
    }

    private const string L09 = """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim a As Object = Make(5)
            Dim b As Object = Make(5)
            Dim c As Object = a
            Dim n As Object = Nothing
            Console.WriteLine(a Is b)
            Console.WriteLine(a Is c)
            Console.WriteLine(a IsNot b)
            Console.WriteLine(n Is Nothing)
            Console.WriteLine(a Is Nothing)
            Select Case n
                Case Is Nothing
                    Console.WriteLine("case-is-nothing")
                Case Else
                    Console.WriteLine("case-else")
            End Select
        End Sub
        """;
    private const string L09Expected = "False\nTrue\nTrue\nTrue\nFalse\ncase-is-nothing";

    [Test]
    public void L09_IsIdentity_UnaffectedByBoxing_CSharpAndMsilAgree()
    {
        // JS is pinned separately below (#215) — a boxed `Is` disagrees with VB there.
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(L09)), Is.EqualTo(L09Expected), "C#");
            AssertCppRefuses(L09);
        });
        AssertMsilAllEntryPoints(L09, L09Expected);
    }

    private const string L10 = """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeD() As Object
            Return 2.5
        End Function
        Sub TestStr(o As Object)
            Select Case o
                Case "x"
                    Console.WriteLine("x")
                Case Is < "m"
                    Console.WriteLine("below-m")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub TestBool(o As Object)
            Select Case o
                Case True
                    Console.WriteLine("true")
                Case Else
                    Console.WriteLine("false")
            End Select
        End Sub
        Sub TestNum(o As Object, n As Integer)
            Select Case o
                Case Is < n
                    Console.WriteLine("below-n")
                Case 2.5
                    Console.WriteLine("two-and-a-half")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub Main()
            TestStr("x")
            TestStr("abc")
            TestStr("zz")
            TestBool(True)
            TestBool(False)
            TestNum(Make(2), 5)
            TestNum(MakeD(), 1)
            TestNum(Make(9), 5)
        End Sub
        """;
    private const string L10Expected = "x\nbelow-m\nelse\ntrue\nfalse\nbelow-n\ntwo-and-a-half\nelse";

    [Test]
    public void L10_SelectCaseStringBooleanAndMixedNumeric_JsAndMsilAgree()
    {
        // C# itself refuses (CS8781 — see the #211 pin group below).
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L10)), Is.EqualTo(L10Expected), "JavaScript");
            AssertCppRefuses(L10);
        });
        AssertMsilAllEntryPoints(L10, L10Expected);
    }

    private const string L12 = """
        Function GetS() As String
            Return Nothing
        End Function
        Function GetI() As Integer
            Return 0
        End Function
        Sub Main()
            Dim s As String = GetS()
            Console.WriteLine(s = Nothing)
            Dim i As Integer = GetI()
            Console.WriteLine(i = Nothing)
        End Sub
        """;

    [Test]
    public void L12_NothingLiteralNeverMakesAComparisonLateBound_OnMsil() =>
        // C# and JavaScript are deliberately excluded: both print "True | False" here for a
        // reason unrelated to #177 (they already disagreed with VB's own `i = Nothing` answer
        // before this fix touched anything), so this is an MSIL-only assertion — MSIL is the
        // ONLY backend that answers VB's "True | True" for this probe.
        AssertMsilAllEntryPoints(L12, "True\nTrue");

    // ====================================================================================
    // 4. Pins for follow-up tasks this fix's own probes surfaced. Each is a fact about a
    //    DIFFERENT area (the C#/JS backends, or the optimizer's constant folding), not a defect
    //    in #177's own MSIL work — do not fix here.
    // ====================================================================================

    /// <summary>Compiles <paramref name="source"/> to C# and returns the Roslyn diagnostics,
    /// without asserting success — the pin below wants the FAILURE, not a thrown exception.</summary>
    private static ImmutableArray<Diagnostic> CSharpDiagnostics(string source)
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            "MsilObjectBoxingCSharpProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return compilation.GetDiagnostics();
    }

    // #211 — a comparison with a statically Object operand refuses to compile on C# at all
    // (CS0019 for the ordinary operators; CS8781 for L10's string relational pattern). MSIL's
    // own late-bound comparison (this fix) has no C# counterpart yet.
    [TestCase(nameof(E02), "CS0019")]
    [TestCase(nameof(C1), "CS0019")]
    [TestCase(nameof(L01), "CS0019")]
    [TestCase(nameof(L03), "CS0019")]
    [TestCase(nameof(L04), "CS0019")]
    [TestCase(nameof(L05), "CS0019")]
    [TestCase(nameof(L06), "CS0019")]
    [TestCase(nameof(L07), "CS0019")]
    [TestCase(nameof(L08), "CS0019")]
    [TestCase(nameof(L10), "CS8781")]
    public void ObjectComparison_RefusesToCompileOnCSharp_PinnedForTask211(string which, string diagnosticId)
    {
        var source = which switch
        {
            nameof(E02) => E02,
            nameof(C1) => C1,
            nameof(L01) => L01,
            nameof(L03) => L03,
            nameof(L04) => L04,
            nameof(L05) => L05,
            nameof(L06) => L06,
            nameof(L07) => L07,
            nameof(L08) => L08,
            nameof(L10) => L10,
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        var diagnostics = CSharpDiagnostics(source);
        Assert.That(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Id == diagnosticId),
            $"task #211 (pre-existing, unrelated to #177's MSIL fix): expected a {diagnosticId} "
            + "diagnostic when {which} is emitted to C#. A DIFFERENT diagnostic set (including "
            + "none at all, meaning C# now compiles this) means #211 moved — update this pin, "
            + "do not just delete it.\n"
            + string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }

    // #215 — the JavaScript backend disagrees with VB on `= Nothing`, `Case Nothing` and a
    // boxed `Is`, pre-existing and unrelated to #177's own MSIL fix.
    [Test]
    public void L05_NothingEqualsZero_JavaScript_PinsPreExistingDisagreement_Against215()
    {
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(L05)), Is.EqualTo("False\nTrue\nFalse\nFalse\nTrue"),
            "task #215 (pre-existing, unrelated to #177): JavaScript's `= Nothing` disagrees "
            + "with VB's late-bound answer here. A different answer here (including VB's own "
            + $"'{L05Expected}') means #215 moved — update this pin, do not just delete it.");
    }

    [Test]
    public void L08_CaseNothingAndGuard_JavaScript_PinsPreExistingDisagreement_Against215()
    {
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(L08)),
            Is.EqualTo("something\nnothing\nsomething\nsomething\nsomething\nnot-nothing\n"
                + "is-nothing\nnot-nothing\nnot-nothing\nin-range\nover\nnon-positive"),
            "task #215 (pre-existing, unrelated to #177): JavaScript's `Case Nothing` and its "
            + "guard evaluation disagree with VB here. A different answer means #215 moved — "
            + "update this pin, do not just delete it.");
    }

    [Test]
    public void L09_IsIdentity_JavaScript_PinsPreExistingDisagreement_Against215()
    {
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(L09)), Is.EqualTo("True\nTrue\nFalse\nTrue\nFalse\ncase-is-nothing"),
            "task #215 (pre-existing, unrelated to #177): JavaScript's boxed `Is` disagrees with "
            + $"VB's reference identity here. A different answer here (including VB's own "
            + $"'{L09Expected}') means #215 moved — update this pin, do not just delete it.");
    }

    // #214, NARROWED by #123. The optimizer used to fold this mixed-type constant compare WRONG
    // ("False | False") on every backend that runs it (C#, JavaScript, MSIL): copy propagation puts
    // the Object's constant into the compare, and ConstantFoldingPass.TryFoldCompare knew only
    // same-type pairs. #123 compares two numeric constants of different widths in the WIDER one,
    // as VB does — and for two NUMERIC boxes VB's late-bound compare widens the same way, so the
    // fold now gives VB's own answer here. C++ still refuses (Object has no C++ mapping).
    // ⚠ #214 stays OPEN for what the fold cannot see: a boxed String compared with a number (VB
    // converts the String late-bound; the fold's Equals says "unequal") — pinned as L11b.
    private const string L11 = "Sub Main()\n Dim o As Object = 20\n Console.WriteLine(o = 20.0)\n"
        + " Dim d As Object = 2.5\n Console.WriteLine(d > 2)\nEnd Sub\n";

    [Test]
    public void L11_FoldedNumericObjectConstants_AnswerLikeVb_OnEveryBackend()
    {
        const string vb = "True\nTrue";   // 20 = 20.0 and 2.5 > 2, measured with vbc
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(L11)), Is.EqualTo(vb), "C#");
            // JavaScriptExecutionTests.RunJs does not run the IR optimizer at all (FourBackends'
            // own doc comment), so it never reaches the fold this test is about — the
            // STANDARD-pipeline JS run is the one that goes through the same
            // OptimizationPipeline.AddStandardPasses() the CLI always runs.
            Assert.That(Norm(JavaScriptOptimizedExecutionTests.RunOptimized(L11)), Is.EqualTo(vb),
                "JavaScript (optimizer-running pipeline)");
            AssertCppRefuses(L11);
        });
        AssertMsilAllEntryPoints(L11, vb);
    }

    // #214 — what REMAINS after #123: a boxed String against a number is not a numeric pair, so
    // the fold still answers with Equals ("unequal") where VB converts the String to Double
    // late-bound. Silent wrong answer, pinned VISIBLY as today's output rather than left
    // undiscovered.
    private const string L11b = "Sub Main()\n Dim s As Object = \"20\"\n Console.WriteLine(s = 20)\n"
        + " Console.WriteLine(s <> 20)\nEnd Sub\n";

    [Test]
    public void L11b_FoldedStringVersusNumberObjectConstants_PinsWrongAnswer_Against214()
    {
        const string wrongToday = "False\nTrue";   // vbc: "True | False"
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(L11b)), Is.EqualTo(wrongToday), "C#");
            Assert.That(Norm(JavaScriptOptimizedExecutionTests.RunOptimized(L11b)), Is.EqualTo(wrongToday),
                "JavaScript (optimizer-running pipeline)");
            AssertCppRefuses(L11b);
        });
        AssertMsilAllEntryPoints(L11b, wrongToday);
        // A change to vbc's "True | False" on every backend above means the rest of #214 is fixed;
        // update `wrongToday` rather than deleting the test.
    }

    // #216 — an Optional parameter typed Object with a non-null default refuses to compile on
    // C# at all (CS1763: a reference-typed default other than string/null).
    [Test]
    public void E16_OptionalObjectDefault_RefusesToCompileOnCSharp_PinnedForTask216()
    {
        var diagnostics = CSharpDiagnostics(E16);
        Assert.That(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS1763"),
            "task #216 (pre-existing, unrelated to #177): `Optional o As Object = 5` must still "
            + "refuse with CS1763 on C#. A different diagnostic set (including none, meaning C# "
            + "now compiles this) means #216 moved — update this pin, do not just delete it.\n"
            + string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }

    // #213 — MyBase.Show(5) into a Base method typed `o As Object` once threw MissingMethodException
    // on MSIL: the call site named the signature from the ARGUMENT's type (`Show(int32)`), not from the
    // declared parameter (`Show(object)`). A MyBase call now carries its target's parameter facts.
    private const string MyBaseIntoObjectParameter = """
        Class Base
            Public Sub Show(o As Object)
                Console.WriteLine(o)
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Sub Relay()
                MyBase.Show(5)
            End Sub
        End Class
        Sub Main()
            Dim d As New Derived()
            d.Relay()
        End Sub
        """;

    [Test]
    public void MyBaseCallIntoAnObjectParameter_Msil_PrintsVbcsAnswer_Task213()
    {
        // vbc prints `5`. C# prints it too (MyBaseMethodCallStatementExecutionTests row `p2_object`, with JavaScript's);
        // C++ refuses the program (`'Object' has no C++ mapping`, the parameter itself).
        //
        // The MSIL call site names the method from its DECLARATION (`Show(object)`), where it used to name it from
        // the ARGUMENT (`Show(int32)`) — a method that does not exist, so the CLR threw MissingMethodException (#213).
        var run = Run(MyBaseIntoObjectParameter);
        Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.Ran), run.Report);
        Assert.That(run.Output.Replace("\r\n", "\n").Trim(), Is.EqualTo("5"), run.Report);
    }

    // #212 — CInt of a boxed True prints 1 on C# and MSIL where VB prints -1 (True widens to
    // -1, not 1, as an Integer). Pinned on both.
    private const string CIntOfBoxedTrue =
        "Sub Main()\n Dim o As Object = True\n Console.WriteLine(CInt(o))\nEnd Sub\n";

    [Test]
    public void CIntOfBoxedTrue_CSharpAndMsil_PinTheSameWrongAnswer_Against212()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(CIntOfBoxedTrue)), Is.EqualTo("1"), "C#");
            Assert.That(Norm(RunExpectingSuccess(CIntOfBoxedTrue)), Is.EqualTo("1"), "MSIL");
        });
        // task #212 (pre-existing, unrelated to #177): VB's own answer is -1 (True widens to
        // Integer as -1, all bits set), which is what Convert.ToInt32(object) does NOT do for a
        // boxed Boolean (it gives 1). A change to "-1" on either backend above means #212
        // moved — update this pin, do not just delete it.
    }

    // ====================================================================================
    // 5. E05 (Structure in Object) and E17 (enum in Object) — MSIL cannot run these today;
    //    both are #192, pre-existing and unrelated to #177's own boxing/unboxing/comparison
    //    work (a Structure and an Enum are not yet real value types on this backend).
    // ====================================================================================

    private const string E05 = """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Sub Main()
            Dim p As Pt
            p.X = 3
            p.Y = 4
            Dim o As Object = p
            Dim q As Pt = CType(o, Pt)
            Console.WriteLine(q.X + q.Y)
        End Sub
        """;

    [Test]
    public void E05_StructureInObject_Msil_PinsPreExistingFailure_Against192()
    {
        Assert.That(Norm(FourBackends.RunEmittedCSharp(E05)), Is.EqualTo("7"), "C# is the oracle");

        var run = Run(E05);
        Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed),
            "task #192 (pre-existing, unrelated to #177): a Structure boxed into an Object slot "
            + "must still fail at run time on MSIL. A different outcome (including Ran, with the "
            + "correct '7') means #192 moved — update this pin, do not just delete it.\n" + run.Report);
        Assert.That(run.Output, Does.Contain("NullReferenceException"), run.Report);
    }

    private const string E17 = """
        Enum Color
            Red
            Green
        End Enum
        Sub Main()
            Dim c As Object = Color.Green
            Console.WriteLine(c)
        End Sub
        """;

    [Test]
    public void E17_EnumInObject_Msil_PinsPreExistingAssembleFailure_Against192()
    {
        Assert.That(Norm(FourBackends.RunEmittedCSharp(E17)), Is.EqualTo("Green"), "C# is the oracle");

        var run = Run(E17);
        Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.AssembleFailed),
            "task #192 (pre-existing, unrelated to #177): an Enum's own declaration "
            + "(`.field public specialname rtspecialname int32 value__`) must still fail to "
            + "assemble on this backend, independent of anything boxing an Object touches. A "
            + "different outcome (including assembling) means #192 moved — update this pin, do "
            + "not just delete it.\n" + run.Report);
    }
}
