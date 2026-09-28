using System;
using System.IO;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;
using VisualGameStudio.Tests.Native;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Task #183 — execution: MSIL now RUNS <c>&amp;</c> with a value operand and
/// <c>Console.Write</c>/<c>WriteLine</c> of every value type, and prints what C# prints.
///
/// <para>Expected strings are taken from the implementer's measured <c>.exp</c> files
/// (<c>S/t183/probes/{C1,C2,C3,W1,W2}.exp</c>, and the edge probes' outputs in
/// <c>S/t183/edge-after.txt</c>), themselves the C#/VB answer — never copied from MSIL, which is
/// the backend this whole task is about.</para>
///
/// <para><b>Why some probes skip the JavaScript leg.</b> C1 and W1 use <c>Long</c>, which
/// JavaScript refuses by design (BL7003 — a JS number is a double, exact only to 2^53). E1, E9b
/// use <c>ULong</c> for the same reason. E10 DOES run on JavaScript, but prints a different
/// (pre-existing, unrelated) answer: Single precision differs because JavaScript has no float32
/// type at all. That is not what task #183 touches, so that leg is pinned separately, not folded
/// into a 4-way comparison that would silently pin someone else's known gap under this task's
/// name. ⛔ <b>#189 DONE (fix commit 381b95ff):</b> E8 USED to be in this same boat — its
/// String-<c>Nothing</c> printed the real word <c>null</c> on JS where every other backend
/// printed "" — but that was exactly task #189's own gap (a <c>Nothing</c> String in <c>&amp;</c>
/// printing JS's own <c>null</c> spelling instead of VB's ""), and #189 fixed it: JS now agrees
/// with C#/C++/MSIL on E8, so E8 is folded into a 4-way comparison below like C2/C3/E7/W2.</para>
///
/// <para><b>What is deliberately NOT here</b> (see docs/HANDOFF.md): Decimal on MSIL is a
/// pre-existing, total gap (#129 — <c>MSILBackend</c> cannot even declare a Decimal local; every
/// use of the type, mixed or not, fails to assemble). Date, an Enum LOCAL, and a default
/// (never-<c>New</c>'d) Structure local are a pre-existing MSIL gap (#192 — an enum local is
/// declared <c>class</c> where the type itself is a value type, a TypeLoadException; a default
/// Structure local is never initialized, a NullReferenceException) — <see cref="MsilValueToStringTests"/>
/// pins their IL shape only, never a run. A user-class operand of <c>&amp;</c> and
/// <c>Console.Write</c> of a class are pre-existing, unrelated gaps (#191). None of these are
/// pinned as passing here — pinning a known failure as "still fails the same way" is not this
/// fixture's job, and a fix for any of #129/#191/#192 should not have to touch this file.</para>
///
/// <para>⚠ <c>[NonParallelizable]</c>: the C# leg (<see cref="FourBackends.RunEmittedCSharp"/>)
/// redirects <c>Console.Out</c>, matching every other fixture that calls it.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class MsilValueToStringExecutionTests
{
    // ========================================================================================
    // Helpers — three-backend (no JS) and four-backend runners, standard and aggressive.
    // ========================================================================================

    private static void RunOnCSharpCppMsil(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))),
                Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)),
                Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(RunExpectingSuccess(program)),
                Is.EqualTo(expected), "MSIL");
        });
    }

    private static void RunOnCSharpCppMsilAggressive(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(program))),
                Is.EqualTo(expected), "C++ aggressive");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(program)),
                Is.EqualTo(expected), "C# aggressive");
            Assert.That(FourBackends.Norm(RunAggressiveExpectingSuccess(program)),
                Is.EqualTo(expected), "MSIL aggressive");
        });
    }

    // ========================================================================================
    // 1. The implementer's five contract probes — CLI (standard) and CLI --optimize (aggressive)
    //    entry points. All 15 cells (5 probes x 3 entry points, counting the Release .blproj
    //    section below) must print the .exp text.
    // ========================================================================================

    private const string C1 = """
        Sub Main()
            Dim i As Integer = 5
            Dim l As Long = 7
            Dim sh As Short = 3
            Dim b As Byte = 9
            Dim d As Double = 2.5
            Dim f As Single = 1.5
            Dim t As Boolean = True
            Console.WriteLine("i=" & i)
            Console.WriteLine("l=" & l)
            Console.WriteLine("sh=" & sh)
            Console.WriteLine("b=" & b)
            Console.WriteLine("d=" & d)
            Console.WriteLine("f=" & f)
            Console.WriteLine("t=" & t)
        End Sub
        """;
    private const string C1Expected = "i=5\nl=7\nsh=3\nb=9\nd=2.5\nf=1.5\nt=True";

    private const string C2 = """
        Sub Main()
            Dim i As Integer = 5
            Dim d As Double = 2.5
            Console.WriteLine(i & "!")
            Dim s As String = "x" & i & "y" & d & "z"
            Console.WriteLine(s)
            Console.WriteLine((i + 1) & "")
        End Sub
        """;
    private const string C2Expected = "5!\nx5y2.5z\n6";

    private const string C3 = """
        Function Label(n As Integer) As String
            Return "#" & n
        End Function

        Class P
            Public X As Integer = 3
            Public Function Show() As String
                Return "X=" & X
            End Function
        End Class

        Sub Main()
            Console.WriteLine(Label(42))
            Dim p As New P()
            Console.WriteLine(p.Show())
            Dim acc As String = ""
            For k As Integer = 1 To 3
                acc = acc & k
            Next
            Console.WriteLine(acc)
        End Sub
        """;
    private const string C3Expected = "#42\nX=3\n123";

    private const string W1 = """
        Sub Main()
            Dim sh As Short = 3
            Dim b As Byte = 9
            Dim f As Single = 1.5
            Dim l As Long = 7
            Dim i As Integer = 4
            Dim d As Double = 2.5
            Dim t As Boolean = False
            Console.WriteLine(sh)
            Console.WriteLine(b)
            Console.WriteLine(f)
            Console.WriteLine(l)
            Console.WriteLine(i)
            Console.WriteLine(d)
            Console.WriteLine(t)
        End Sub
        """;
    private const string W1Expected = "3\n9\n1.5\n7\n4\n2.5\nFalse";

    private const string W2 = """
        Sub Main()
            Dim sh As Short = 3
            Dim f As Single = 1.5
            Console.Write(sh)
            Console.Write(",")
            Console.Write(f)
            Console.WriteLine()
            Console.WriteLine(sh + sh)
        End Sub
        """;
    private const string W2Expected = "3,1.5\n6";

    // C1 and W1 carry a Long — no JavaScript leg (BL7003, by design).
    [Test]
    public void C1_StandardPipeline_CSharpCppMsilAgree() => RunOnCSharpCppMsil(C1, C1Expected);

    [Test]
    public void C1_AggressivePipeline_CSharpCppMsilAgree() => RunOnCSharpCppMsilAggressive(C1, C1Expected);

    [Test]
    public void W1_StandardPipeline_CSharpCppMsilAgree() => RunOnCSharpCppMsil(W1, W1Expected);

    [Test]
    public void W1_AggressivePipeline_CSharpCppMsilAgree() => RunOnCSharpCppMsilAggressive(W1, W1Expected);

    // C2, C3, W2 have no Long/ULong — all four backends agree.
    [Test]
    public void C2_StandardPipeline_AllFourBackendsAgree() => FourBackends.RunsOnEveryBackend(C2, C2Expected);

    [Test]
    public void C2_AggressivePipeline_AllFourBackendsAgree() => FourBackends.RunsOnEveryBackendAggressive(C2, C2Expected);

    [Test]
    public void C3_StandardPipeline_AllFourBackendsAgree() => FourBackends.RunsOnEveryBackend(C3, C3Expected);

    [Test]
    public void C3_AggressivePipeline_AllFourBackendsAgree() => FourBackends.RunsOnEveryBackendAggressive(C3, C3Expected);

    [Test]
    public void W2_StandardPipeline_AllFourBackendsAgree() => FourBackends.RunsOnEveryBackend(W2, W2Expected);

    [Test]
    public void W2_AggressivePipeline_AllFourBackendsAgree() => FourBackends.RunsOnEveryBackendAggressive(W2, W2Expected);

    // ========================================================================================
    // 2. The THIRD entry point: a Release .blproj build (BasicCompiler.CompileProjectFiles under
    //    the CLI's own "-c Release" -> OptimizeAggressive mapping), a different code path than
    //    MsilHarness.CompileToIl(aggressive: true)'s direct AggressivePipeline.Apply call. Same
    //    pattern as MsilBinaryOperandCoercionTests.BuildReleaseMsilAndRun. The CLI's MSIL build
    //    step only emits the .il (no ilasm call of its own), so ilasm/dotnet still run through
    //    MsilHarness.RunIl.
    // ========================================================================================

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-msilvts-" + Guid.NewGuid().ToString("N"));
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

    [TestCase(nameof(C1))]
    [TestCase(nameof(C2))]
    [TestCase(nameof(C3))]
    [TestCase(nameof(W1))]
    [TestCase(nameof(W2))]
    public void ReleaseBlprojBuild_EachProbe_PrintsTheExpectedText(string which)
    {
        var (source, expected) = which switch
        {
            nameof(C1) => (C1, C1Expected),
            nameof(C2) => (C2, C2Expected),
            nameof(C3) => (C3, C3Expected),
            nameof(W1) => (W1, W1Expected),
            nameof(W2) => (W2, W2Expected),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        Assert.That(FourBackends.Norm(BuildReleaseMsilAndRun(source)), Is.EqualTo(expected));
    }

    // ========================================================================================
    // 3. The edge probes that now run clean on MSIL (S/t183/edge-after.txt): unsigned types, an
    //    operand from a function/field/array-element/arithmetic expression, and a String Nothing
    //    operand. Standard pipeline only — these are supplementary to the five contract probes
    //    above, which already cover both pipelines and all three entry points.
    // ========================================================================================

    // E1 — UInteger, ULong, SByte, UShort in `&`. ULong excludes the JavaScript leg (BL7003).
    private const string E1 = """
        Sub Main()
            Dim ui As UInteger = 4000000000
            Dim ul As ULong = 9000000000
            Dim sb As SByte = -5
            Dim us As UShort = 65000
            Console.WriteLine("ui=" & ui)
            Console.WriteLine("ul=" & ul)
            Console.WriteLine("sb=" & sb)
            Console.WriteLine("us=" & us)
            Console.WriteLine(sb & "|" & us)
        End Sub
        """;
    private const string E1Expected = "ui=4000000000\nul=9000000000\nsb=-5\nus=65000\n-5|65000";

    [Test]
    public void E1_UnsignedAndSByteInConcat_CSharpCppMsilAgree() => RunOnCSharpCppMsil(E1, E1Expected);

    // E7 — a function result, a field, an array element and an arithmetic expression as the `&`
    // operand. No Long/ULong: all four backends agree.
    private const string E7 = """
        Class Box
            Public N As Integer = 7
            Public D As Double = 0.25
        End Class

        Function Twice(n As Integer) As Integer
            Return n * 2
        End Function

        Sub Main()
            Dim b As New Box()
            Dim arr() As Integer = {10, 20, 30}
            Dim i As Integer = 3
            Console.WriteLine("f=" & Twice(21))
            Console.WriteLine("fld=" & b.N & "," & b.D)
            Console.WriteLine("arr=" & arr(1))
            Console.WriteLine("ar=" & (i * 4 - 1))
            Console.WriteLine("dv=" & (i / 2))
            Console.WriteLine(Twice(i) & "" & arr(2) & b.N)
        End Sub
        """;
    private const string E7Expected = "f=42\nfld=7,0.25\narr=20\nar=11\ndv=1.5\n6307";

    [Test]
    public void E7_FunctionFieldArrayArithmeticOperands_AllFourBackendsAgree() =>
        FourBackends.RunsOnEveryBackend(E7, E7Expected);

    // E8 — a String Nothing operand of `&`. #189 DONE (fix commit 381b95ff): JavaScript used to
    // print the real word "null" here where the other three printed "" — that divergence WAS
    // task #189's own gap, now fixed, so all four backends agree.
    private const string E8 = """
        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine("n=" & s)
            Console.WriteLine("n=" & Nothing)
            Console.WriteLine(s & "|" & 5)
        End Sub
        """;
    private const string E8Expected = "n=\nn=\n|5";

    [Test]
    public void E8_StringNothingOperand_AllFourBackendsAgree() =>
        FourBackends.RunsOnEveryBackend(E8, E8Expected);

    // E9b — SByte, UShort, UInteger, ULong, Single, Byte, Short, Char, Boolean, Double, Long
    // through both WriteLine and Write, plus two compound &= assignments. No Decimal (that leg is
    // E9, left out — #129, MSIL cannot declare a Decimal local at all). ULong excludes JavaScript.
    private const string E9b = """
        Sub Main()
            Dim sb As SByte = -5
            Dim us As UShort = 65000
            Dim ui As UInteger = 4000000000
            Dim ul As ULong = 9000000000
            Dim f As Single = 0.1
            Dim b As Byte = 200
            Dim sh As Short = -3
            Dim ch As Char = "q"c
            Console.WriteLine(sb)
            Console.WriteLine(us)
            Console.WriteLine(ui)
            Console.WriteLine(ul)
            Console.WriteLine(f)
            Console.WriteLine(b)
            Console.WriteLine(sh)
            Console.WriteLine(ch)
            Console.Write(sb)
            Console.Write(",")
            Console.Write(us)
            Console.Write(",")
            Console.Write(ui)
            Console.Write(",")
            Console.Write(ul)
            Console.Write(",")
            Console.Write(f)
            Console.Write(",")
            Console.Write(b)
            Console.Write(",")
            Console.Write(sh)
            Console.Write(",")
            Console.Write(ch)
            Console.Write(",")
            Console.Write(True)
            Console.Write(",")
            Console.Write(2.5)
            Console.Write(",")
            Console.Write(7L)
            Console.WriteLine()
            Console.WriteLine("f=" & f & ",ch=" & ch & ",b=" & b & ",sh=" & sh)
            Dim s As String = "a"
            s &= 5
            s &= True
            Console.WriteLine(s)
        End Sub
        """;
    private const string E9bExpected =
        "-5\n65000\n4000000000\n9000000000\n0.1\n200\n-3\nq\n" +
        "-5,65000,4000000000,9000000000,0.1,200,-3,q,True,2.5,7\n" +
        "f=0.1,ch=q,b=200,sh=-3\na5True";

    [Test]
    public void E9b_EveryValueTypeThroughWriteAndWriteLine_CSharpCppMsilAgree() =>
        RunOnCSharpCppMsil(E9b, E9bExpected);

    // E10 — Single 0.1 must print "0.1", never the float64-widened "0.10000000149011612". Uses
    // 0.1 deliberately (not 1.5, which prints identically either way, per the implementer brief).
    // No JavaScript leg: JS has no float32 type, so 1.1F * 3 prints a different (correct-for-JS,
    // irrelevant-to-#183) double answer.
    private const string E10 = """
        Sub Main()
            Dim f As Single = 0.1
            Console.WriteLine(f)
            Dim g As Single = 1.1
            Console.WriteLine(g * 3)
        End Sub
        """;
    private const string E10Expected = "0.1\n3.3000002";

    [Test]
    public void E10_SinglePrecision_CSharpCppMsilAgree_NeverWidenedText() =>
        RunOnCSharpCppMsil(E10, E10Expected);
}
