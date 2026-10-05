using System;
using System.IO;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #189 — a <c>Nothing</c> String in <c>&amp;</c> / <c>Write</c>/<c>WriteLine</c> (JS, C#);
/// a <c>Catch</c> variable captured by a lambda (C++). Fix commit 381b95ff.
///
/// <para>Expected strings are taken from the implementer's measured <c>.exp</c> files
/// (<c>S/t189/probes/*.exp</c>, <c>S/t189/edge/*.exp</c>) — the C#/VB answer — never from a
/// non-C# backend, matching every sibling fixture's own rule (<c>NothingConversionExecutionTests</c>,
/// <c>MsilValueToStringExecutionTests</c>).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // FourBackends' C# leg redirects Console.Out
public class NothingStringTextExecutionTests
{
    // ============================================================================================
    // 1. J1-J6 / C1-C3 — the implementer's contract probes. J1-J6, C1-C2 moved 27/108 -> 108/108
    //    per this task's own matrix; C3 (ex.Message read directly, no lambda) was already OK and
    //    stays the control.
    // ============================================================================================

    private const string J1 = """
        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine("[" & s & "]")
        End Sub
        """;
    private const string J1Expected = "[]";

    private const string J2 = """
        Function F() As String
            Return Nothing
        End Function
        Sub Take(t As String)
            Console.WriteLine("arg[" & t & "]")
        End Sub
        Sub Main()
            Dim s As String = "x"
            s = Nothing
            Console.WriteLine("asg[" & s & "]")
            Console.WriteLine("ret[" & F() & "]")
            Take(Nothing)
        End Sub
        """;
    private const string J2Expected = "asg[]\nret[]\narg[]";

    private const string J3 = """
        Class Person
            Public Name As String
            Public Function Tag() As String
                Return "<" & Name & ">"
            End Function
        End Class

        Sub Main()
            Dim p As New Person()
            Console.WriteLine(p.Tag())
            p.Name = Nothing
            Console.WriteLine("(" & p.Name & ")")
        End Sub
        """;
    private const string J3Expected = "<>\n()";

    private const string J4 = """
        Sub Main()
            Dim s As String = Nothing
            Dim t As String = s & 5
            Console.WriteLine(t)
            Dim u As String = Nothing
            Console.WriteLine("{" & s & u & "}")
        End Sub
        """;
    private const string J4Expected = "5\n{}";

    private const string J5 = """
        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine(s)
            Console.WriteLine("after")
        End Sub
        """;
    // J5.exp's raw text is "\nafter\n" (an empty line, then "after") — but FourBackends.Norm
    // Trim()s both sides before comparing, which strips the LEADING empty line too, so the
    // expectation actually checked here is post-Norm, not the raw .exp bytes.
    private const string J5Expected = "after";

    /// <summary>
    /// Before #189, JS emitted <c>&amp;</c> as a bare <c>+</c>: <c>null + 1</c> is NUMERIC
    /// addition, so this loop summed 1+2+3 and printed the silently-wrong <c>6</c> where VB
    /// prints <c>"123"</c>. Given its own test below (<see cref="J6_ReassignedNothingLocal_InLoopConcat_PrintsOneTwoThree_NotSilentSix"/>),
    /// NOT folded into the generic TestCase list, so the fact this was a silent wrong answer is
    /// visible in a test NAME, not just a table row.
    /// </summary>
    private const string J6 = """
        Sub Main()
            Dim s As String = Nothing
            Dim acc As String = Nothing
            For i As Integer = 1 To 3
                acc = acc & i
            Next
            Console.WriteLine(acc)
        End Sub
        """;
    private const string J6Expected = "123";

    private const string C1 = """
        Sub Main()
            Dim f As Action = Nothing
            Try
                Throw New Exception("boom")
            Catch ex As Exception
                f = Sub() Console.WriteLine(ex.Message)
            End Try
            f()
        End Sub
        """;
    private const string C1Expected = "boom";

    private const string C2 = """
        Sub Main()
            Try
                Throw New Exception("inner")
            Catch ex As Exception
                Dim show As Action = Sub() Console.WriteLine("msg=" & ex.Message)
                show()
            End Try
        End Sub
        """;
    private const string C2Expected = "msg=inner";

    /// <summary>The control: <c>ex.Message</c> read directly in the Catch, no lambda. Was ALREADY
    /// correct on every backend before #189 and must stay that way.</summary>
    private const string C3 = """
        Sub Main()
            Try
                Throw New Exception("direct")
            Catch ex As Exception
                Console.WriteLine(ex.Message)
            End Try
        End Sub
        """;
    private const string C3Expected = "direct";

    [TestCase(J1, J1Expected, "J1")]
    [TestCase(J2, J2Expected, "J2")]
    [TestCase(J3, J3Expected, "J3")]
    [TestCase(J4, J4Expected, "J4")]
    [TestCase(J5, J5Expected, "J5")]
    [TestCase(C1, C1Expected, "C1")]
    [TestCase(C2, C2Expected, "C2")]
    [TestCase(C3, C3Expected, "C3")]
    public void Probe_RunsOnEveryBackend_BothPipelines(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    [Test]
    public void J6_ReassignedNothingLocal_InLoopConcat_PrintsOneTwoThree_NotSilentSix()
    {
        FourBackends.RunsOnEveryBackend(J6, J6Expected);
        FourBackends.RunsOnEveryBackendAggressive(J6, J6Expected);
    }

    // ============================================================================================
    // 2. The THIRD entry point — a REAL Release .blproj build through the deployed BasicLang.exe,
    //    matching MsilBinaryOperandCoercionTests.BuildReleaseMsilAndRun /
    //    MsilValueToStringExecutionTests.BuildReleaseMsilAndRun /
    //    LicmKillVocabularyTests.L1_ReleaseBlprojBuild_Msil_PrintsTheCorrectValue exactly, and for
    //    the same reason those three picked MSIL: task #134 recorded a C++ Release .blproj as the
    //    STANDARD pipeline (no aggressive-only pass to miss) — FIXED by #134, it now takes the
    //    aggressive one — and a native C++ project build on THIS machine always uses MSVC
    //    (CLAUDE.md), which is not installed here — confirmed by hand: `build -c Release` on a
    //    Cpp-backend .blproj fails BL6015 on this Linux box. MSIL's Release build takes the
    //    aggressive pipeline AND runs on Linux (ilasm/mono), so it is the one backend that
    //    actually exercises CompileProjectFiles' real Release code path here.
    // ============================================================================================

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-nothingstring-release-" + Guid.NewGuid().ToString("N"));
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

        return Msil.MsilHarness.RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App");
    }

    [TestCase(nameof(J1))]
    [TestCase(nameof(J2))]
    [TestCase(nameof(J3))]
    [TestCase(nameof(J4))]
    [TestCase(nameof(J5))]
    [TestCase(nameof(J6))]
    [TestCase(nameof(C1))]
    [TestCase(nameof(C2))]
    [TestCase(nameof(C3))]
    public void ReleaseBlprojBuild_EachProbe_PrintsTheExpectedText(string which)
    {
        var (source, expected) = which switch
        {
            nameof(J1) => (J1, J1Expected),
            nameof(J2) => (J2, J2Expected),
            nameof(J3) => (J3, J3Expected),
            nameof(J4) => (J4, J4Expected),
            nameof(J5) => (J5, J5Expected),
            nameof(J6) => (J6, J6Expected),
            nameof(C1) => (C1, C1Expected),
            nameof(C2) => (C2, C2Expected),
            nameof(C3) => (C3, C3Expected),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        Assert.That(FourBackends.Norm(BuildReleaseMsilAndRun(source)), Is.EqualTo(expected));
    }

    // ============================================================================================
    // 2b. E1 — an Object Nothing in `&` and in Write/WriteLine, RUN on the three backends where it
    //     compiles. Not in the "runs everywhere" set below (C++ has no mapping for Object at all,
    //     a much older, unrelated gap — measured only as a fact, see E13's own pin below for the
    //     same C++ limitation). The mutant table's __blStr-null-check mutant (#5) has NO other
    //     RUNTIME coverage anywhere in the suite — JavaScriptBooleanTextTests only pins the
    //     helper's TEXT, never runs it — so this is the one test that actually exercises
    //     `__blStr(null)` under Node rather than just reading its source.
    // ============================================================================================

    private const string E1 = """
        Sub Main()
            Dim o As Object = Nothing
            Console.WriteLine("[" & o & "]")
            Console.WriteLine(o)
            Console.WriteLine("after")
            Dim b As Object = True
            Console.WriteLine("b=" & b)
            Console.Write(o)
            Console.WriteLine("|")
        End Sub
        """;
    private const string E1Expected = "[]\n\nafter\nb=True\n|";

    [Test]
    public void E1_NothingObject_CSharpJavaScriptMsilAgree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E1)), Is.EqualTo(E1Expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E1)), Is.EqualTo(E1Expected), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E1)), Is.EqualTo(E1Expected), "MSIL");
        });
    }

    // ============================================================================================
    // 3. Edge probes that run EVERYWHERE (S/t189/edge/*.bas, all four backends, both pipelines).
    //    E1, E5, E6, E10, E12, E13 do NOT run everywhere — they are pinned individually below,
    //    each against the task the pre-existing (unrelated) gap is filed under.
    // ============================================================================================

    private const string E2 = """
        Sub Main()
            Dim s As String = Nothing
            Dim f As Boolean = True
            Dim n As Integer = 7
            Dim d As Double = 2.5
            Console.WriteLine(s & f)
            Console.WriteLine(f & s)
            Console.WriteLine(s & n)
            Console.WriteLine(n & s)
            Console.WriteLine(s & d)
            Console.WriteLine(d & s)
            Console.WriteLine((s & n) & (n & s))
        End Sub
        """;
    private const string E2Expected = "True\nTrue\n7\n7\n2.5\n2.5\n77";

    private const string E3 = """
        Function Wrap(x As String) As String
            Return "<" & x & ">"
        End Function
        Function Echo(x As String) As String
            Return x
        End Function
        Sub Show(x As String)
            Console.WriteLine(x)
        End Sub
        Sub Main()
            Dim s As String = Nothing
            Show("a" & s)
            Show(s & 1)
            Console.WriteLine(Wrap(Nothing))
            Console.WriteLine(Wrap(s))
            Console.WriteLine("e=" & Echo(Nothing) & "!")
            Console.WriteLine(Echo(s) & Echo(s) & "|")
        End Sub
        """;
    private const string E3Expected = "a\n1\n<>\n<>\ne=!\n|";

    private const string E7 = """
        Sub Main()
            Dim g As Func(Of Integer) = Nothing
            Dim h As Func(Of String) = Nothing
            Try
                Throw New Exception("sixsix")
            Catch ex As Exception
                g = Function() ex.Message.Length
                h = Function() ex.Message
            End Try
            Console.WriteLine(g())
            Console.WriteLine(h())
        End Sub
        """;
    private const string E7Expected = "6\nsixsix";

    private const string E8 = """
        Sub Main()
            Dim s As String = Nothing
            Console.Write(s)
            Console.Write("x")
            Console.WriteLine(s)
            Console.WriteLine("y")
        End Sub
        """;
    private const string E8Expected = "x\ny";

    private const string E9 = """
        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine($"[{s}]")
            Dim acc As String = Nothing
            acc &= "a"
            acc &= s
            acc &= 1
            Console.WriteLine(acc)
        End Sub
        """;
    private const string E9Expected = "[]\na1";

    private const string E11 = """
        Sub Main()
            Dim outer As Func(Of Action) = Nothing
            Try
                Throw New Exception("deep")
            Catch ex As Exception
                outer = Function() (Sub() Console.WriteLine("n:" & ex.Message))
            End Try
            Dim inner As Action = outer()
            inner()
        End Sub
        """;
    private const string E11Expected = "n:deep";

    [TestCase(E2, E2Expected, "E2")]
    [TestCase(E3, E3Expected, "E3")]
    [TestCase(E7, E7Expected, "E7")]
    [TestCase(E8, E8Expected, "E8")]
    [TestCase(E9, E9Expected, "E9")]
    [TestCase(E11, E11Expected, "E11")]
    public void EdgeProbe_RunsOnEveryBackend_BothPipelines(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    // ============================================================================================
    // 4. Pre-existing failures, pinned individually so a fix anywhere else is a DELIBERATE,
    //    noticed change to this file — never fixed here (that is the owning task's job).
    //    (E6 stood here until #136; it runs on every backend now.)
    // ============================================================================================

    /// <summary>
    /// E6 — a nested <c>Try</c> written inside a lambda that itself lives in a <c>Catch</c>
    /// clause. C++/JS/MSIL print the correct "outer/inner\nafter:outer" (this IS #189's own
    /// region-reset fix, on C++). ⭐ MOVED PIN (#136): C# was a DIFFERENT, PRE-EXISTING bug, unrelated to #189 — the lambda's body
    /// rendered as <c>() =&gt; { ; };</c>, the ENTIRE nested Try/Catch and the trailing WriteLine silently dropped, so the program
    /// printed NOTHING (every block after the lambda's entry block was never written). The lambda body is now written by the
    /// function-body emitter, so C# prints vbc's answer too.
    /// </summary>
    private const string E6 = """
        Sub Main()
            Dim f As Action = Nothing
            Try
                Throw New Exception("outer")
            Catch ex As Exception
                f = Sub()
                        Try
                            Throw New Exception("inner")
                        Catch ex2 As Exception
                            Console.WriteLine(ex.Message & "/" & ex2.Message)
                        End Try
                        Console.WriteLine("after:" & ex.Message)
                    End Sub
            End Try
            f()
        End Sub
        """;
    private const string E6CorrectAnswer = "outer/inner\nafter:outer";

    [Test]
    public void E6_NestedTryInsideCatchLambda_RunsOnEveryBackend()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(E6))),
                Is.EqualTo(E6CorrectAnswer), "C++ (this IS #189's own fix)");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E6)),
                Is.EqualTo(E6CorrectAnswer), "JavaScript (this IS #189's own fix)");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E6)),
                Is.EqualTo(E6CorrectAnswer), "MSIL (unaffected by #189)");
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(E6))),
                Is.EqualTo(E6CorrectAnswer), "C# (#136: the lambda's nested Try/Catch and trailing WriteLine are written)");
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(E6))),
                Is.EqualTo(E6CorrectAnswer), "C#, aggressive");
        });
    }

    /// <summary>
    /// E5 — a lambda that captures a <c>Catch</c> variable, stored in a <c>List(Of Action)</c> and
    /// invoked after the <c>Try</c>. C#/JS/MSIL run correctly; C++ runs it since #140 lowered the
    /// closures. It used to fail to COMPILE — a PRE-EXISTING, unrelated gap: <c>List(Of Action)</c>'s
    /// element type lowered to a bare <c>void*</c> rather than <c>std::function&lt;void()&gt;</c>, so
    /// invoking an element read out of the list (<c>a()</c>) was "assigning to 'void *' from incompatible
    /// type 'void'" (clang) / "void value not ignored as it ought to be" (g++) — a generic-collection-of-
    /// delegate gap, not a catch-capture one. Filed under #201 (widened: #201's own entry already covers
    /// several independent C++ gaps specific to a user-delegate VALUE; a delegate inside a generic
    /// collection is a new one). ⚠ The gap is still THERE on the by-copy FALLBACK path (a root the
    /// lowering refuses and W2 admits, byte-identical to before #140): see
    /// <see cref="E5_CatchLambdaStoredInListOfAction_OnTheByCopyFallback_StillFailsClang_Against201"/>.
    /// </summary>
    private const string E5 = """
        Sub Main()
            Dim acts As New List(Of Action)()
            Try
                Throw New Exception("listed")
            Catch ex As Exception
                acts.Add(Sub() Console.WriteLine("a:" & ex.Message))
                acts.Add(Sub() Console.WriteLine("b:" & ex.Message))
            End Try
            For Each a As Action In acts
                a()
            Next
        End Sub
        """;
    private const string E5Expected = "a:listed\nb:listed";

    [Test]
    public void E5_CatchLambdaStoredInListOfAction_RunsOnEveryBackend_Cpp_SinceTask140()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E5)), Is.EqualTo(E5Expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E5)), Is.EqualTo(E5Expected), "JavaScript");
            Assert.That(CppClosures.Compile(E5).PathOf("Main"), Is.EqualTo(BasicLang.Compiler.CodeGen.CPlusPlus.CppClosurePath.Lowered), "C++ root 'Main'");
            Assert.That(CppClosures.Run(E5), Is.EqualTo(E5Expected), "C++, standard");
            Assert.That(CppClosures.Run(E5, CppEntry.Aggressive), Is.EqualTo(E5Expected), "C++, aggressive");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E5)), Is.EqualTo(E5Expected), "MSIL");
        });
    }

    /// <summary>E5 with a read-only Select Case 'When' guard in the root: a D9 refusal (the guard is rendered
    /// inline) that W2 admits because nothing writes what a lambda captures, so the root takes the by-copy
    /// FALLBACK, byte-identical to the emission before #140 — and #201's widened gap is still there.</summary>
    private const string E5_OnTheFallback = """
        Sub Main()
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim acts As New List(Of Action)()
            Try
                Throw New Exception("listed")
            Catch ex As Exception
                acts.Add(Sub() Console.WriteLine("a:" & ex.Message))
                acts.Add(Sub() Console.WriteLine("b:" & ex.Message))
            End Try
            For Each a As Action In acts
                a()
            Next
        End Sub
        """;

    [Test]
    public void E5_CatchLambdaStoredInListOfAction_OnTheByCopyFallback_StillFailsClang_Against201()
    {
        var build = CppClosures.Compile(E5_OnTheFallback);
        Assert.That(build.PathOf("Main"), Is.EqualTo(BasicLang.Compiler.CodeGen.CPlusPlus.CppClosurePath.ByCopy),
            "the guard must force the by-copy fallback — otherwise this pins nothing about #201");
        var (compiled, output) = CppClosures.TryClang(build.Cpp);
        Assert.That(compiled, Is.False, "#201 (widened): List(Of Action)'s element lowering is fixed only where the root is lowered");
        Assert.That(output, Does.Contain("void"),
            "the compile failure must still be List(Of Action)'s element lowering to a bare " +
            "void* (#201, widened). A DIFFERENT failure here means this pin is stale.\n" + output);
    }

    /// <summary>
    /// E10 — a native (out-of-bounds List index) exception whose message is read once directly
    /// and once through a lambda captured in the SAME Catch, then compared. C#/C++ agree
    /// (<c>same=True</c> — the two reads see the same message). MSIL is a PRE-EXISTING, unrelated
    /// bug: <c>same=False</c> — the captured Catch variable's <c>Message</c>, read from inside the
    /// lambda, disagrees with the direct read taken before the lambda was even created. Filed
    /// under #205 (new — no prior HANDOFF mention of an MSIL closure/catch-message inconsistency).
    /// </summary>
    private const string E10 = """
        Sub Main()
            Dim f As Action = Nothing
            Try
                Dim a As New List(Of Integer)()
                Console.WriteLine(a(3))
            Catch ex As Exception
                Dim direct As String = ex.Message
                f = Sub() Console.WriteLine("same=" & (ex.Message = direct))
            End Try
            f()
        End Sub
        """;

    [Test]
    public void E10_NativeExceptionMessageThroughCapturedLambda_Msil_PinsPreExistingWrongAnswer_Against205()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E10)), Is.EqualTo("same=True"), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(E10))),
                Is.EqualTo("same=True"), "C++");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E10)), Is.EqualTo("same=False"),
                "MSIL — #205. A DIFFERENT answer here (including the correct 'same=True') means " +
                "#205 moved — update this pin, do not just delete it.");
        });
    }

    /// <summary>
    /// E10's OWN JavaScript leg is not "wrong" in the same sense — it does not run to the point of
    /// comparing messages at all. <c>List(Of Integer)</c> in JS is a real JS array with no bounds
    /// check on read: <c>a(3)</c> on an array with nothing added returns <c>undefined</c> rather
    /// than throwing, so the <c>Try</c> never enters its <c>Catch</c>, <c>f</c> stays <c>Nothing</c>,
    /// and <c>f()</c> after the Try throws an UNCAUGHT <c>TypeError: f is not a function</c>. This
    /// is a JavaScript List-bounds gap, not a #189 one — pinned here against task #207 (filed from
    /// this measurement). A DIFFERENT outcome means #207 moved — update this pin, do not delete it.
    /// </summary>
    [Test]
    public void E10_JavaScript_ReadingPastEndOfList_DoesNotThrow_Against207()
    {
        var js = JsTestSupport.Compile(E10);
        var (exitCode, stdout, _) = JavaScriptExecutionTests.RunNodeScriptForOutcome(js);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(stdout), Is.EqualTo("undefined"),
                "a(3) on an empty JS array must print 'undefined' rather than throw — if this " +
                "changed, either JS gained bounds-checked List indexing (great — file the fix as " +
                "closing this gap) or something else regressed.\n" + js);
            Assert.That(exitCode, Is.Not.Zero,
                "f stays Nothing (the Catch never ran) and f() must still crash with an uncaught " +
                "TypeError.\n" + js);
        });
    }

    /// <summary>
    /// E12 — two <c>Catch</c> clauses in the SAME loop iteration bind DIFFERENT exception types to
    /// the SAME variable name (<c>ex</c>), and a lambda in each captures it. C#/C++/JS all run
    /// correctly (a per-clause local, no function-level unification needed). MSIL is a DELIBERATE
    /// REFUSAL, not a bug: ADR-0010 D2 makes a captured Catch variable ONE function-level closure
    /// field, which needs ONE static type — two Catch clauses with different exception types
    /// sharing a name have no single type to hoist. <c>ClosureLowering</c> refuses it by design
    /// (<c>ForeignFeatureException</c>), rather than silently picking one type or corrupting the
    /// other. Not filed as a bug — this is the ADR's own D2 rule working as intended.
    /// </summary>
    private const string E12 = """
        Sub Main()
            Dim f As Action = Nothing
            For i As Integer = 1 To 2
                Try
                    If i = 1 Then
                        Throw New ArgumentException("arg" & i)
                    Else
                        Throw New InvalidOperationException("inv" & i)
                    End If
                Catch ex As ArgumentException
                    f = Sub() Console.WriteLine("A:" & ex.Message)
                Catch ex As InvalidOperationException
                    f = Sub() Console.WriteLine("I:" & ex.Message)
                End Try
                f()
            Next
        End Sub
        """;
    private const string E12Expected = "A:arg1\nI:inv2";

    [Test]
    public void E12_TwoCatchClausesSameVariableName_Msil_RefusesToCompile_ADR0010D2()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E12)), Is.EqualTo(E12Expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(E12))),
                Is.EqualTo(E12Expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E12)), Is.EqualTo(E12Expected), "JavaScript");
        });

        var ilEx = Assert.Throws<ForeignFeatureException>(() => Msil.MsilHarness.CompileToIl(E12));
        Assert.That(ilEx!.Message, Does.Contain("captured by a lambda and declared by more than one Catch")
            .And.Contain("ADR-0010 D2"),
            "the refusal must still be D2's own message. A DIFFERENT message (or none at all) " +
            "means this pin is stale.\n" + ilEx.Message);
    }

    /// <summary>
    /// E13 — an <c>Object</c> field set to a Boolean, concatenated. C#/JS/MSIL all agree
    /// ("T=False"). C++ does not compile at all — a PRE-EXISTING, unrelated, and much older gap
    /// (<c>Object</c> has no C++ mapping whatsoever; not specific to Boolean or to #189) —
    /// measured here only as a fact, not filed under a new number by this pin.
    ///
    /// <para>⛔ <b>Corrected, 2026-09-28 (task #177):</b> this used to pin MSIL printing "T="
    /// (the Boolean's text LOST) under #191, widened. That was a MIS-FILING — #191 is about a
    /// user-class <c>&amp;</c> operand and <c>Console.Write</c> of a class; a Boolean boxed into
    /// an <c>Object</c> field is #177's own box-into-Object-slot gap (MSIL never boxed the store,
    /// so <c>&amp;</c> concatenated a raw, unboxed value's text away). #177's fix
    /// (<c>EmitCoerceToSlot</c> boxing every field store into an <c>Object</c> slot) makes MSIL
    /// agree with every other backend, so this is no longer a pin of a wrong answer.</para>
    /// </summary>
    private const string E13 = """
        Class Box
            Public Label As String
            Public Tag As Object
        End Class
        Sub Main()
            Dim b As New Box()
            Console.WriteLine("L=" & b.Label & ";T=" & b.Tag & ";")
            b.Tag = False
            Console.WriteLine("T=" & b.Tag)
            Dim arr(2) As String
            Console.WriteLine("[" & arr(0) & "]")
        End Sub
        """;
    private const string E13Expected = "L=;T=;\nT=False\n[]";

    [Test]
    public void E13_ObjectFieldHoldingBoolean_AgreesOnCSharpJavaScriptAndMsil()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E13)), Is.EqualTo(E13Expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E13)), Is.EqualTo(E13Expected), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E13)), Is.EqualTo(E13Expected), "MSIL");
        });

        Assert.That(() => BclE2E.CompileToCppOptimized(E13), Throws.Exception,
            "C++ — pre-existing, unrelated to #189 or #177: Object has no C++ mapping at all. If " +
            "this starts compiling, C++ gained Object support and this assertion (not a tracked " +
            "task here) should simply be removed.");
    }

    // ============================================================================================
    // 5. E4/E4b — `Nothing = ""`. VB says True; every backend here says False. NOT #189's semantic
    //    question (filed separately as #206) — pinned so #206's fix flips these visibly, on
    //    purpose, rather than as a silent side effect of touching this area again.
    // ============================================================================================

    /// <summary>Constant-foldable: the optimizer can fold <c>Nothing = ""</c> at COMPILE time.
    /// All four backends agree — False, False, True.</summary>
    private const string E4 = """
        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine(s = "")
            Dim t As String = Nothing
            Console.WriteLine("" = t)
            Console.WriteLine(s Is Nothing)
        End Sub
        """;
    private const string E4Expected = "False\nFalse\nTrue";

    [Test]
    public void E4_NothingEqualsEmptyString_ConstantFolded_AllBackendsAgree_Against206()
        => FourBackends.RunsOnEveryBackend(E4, E4Expected);

    /// <summary>
    /// Same question, forced through a function call so the optimizer cannot fold it — a REAL
    /// run-time comparison. C#/JS/MSIL still agree with E4 (False, False, True, True). C++
    /// DIVERGES from its OWN E4 answer: True, True, False, True — because C++ represents String by
    /// VALUE (<c>std::string</c>) with no null state, so at RUN TIME <c>Nothing</c> IS <c>""</c> on
    /// C++ (same root cause as <c>IsIsNotOperatorExecutionTests
    /// .CppStringNothingIsStillEmptiness_ButAnEmptyArrayIsNotNothing_AgreesWithDotNet</c>'s P12). E4 vs E4b together are
    /// the "folded False vs run-time True" the test-writer brief names: C++'s own answer is
    /// INTERNALLY inconsistent depending on whether the comparison survives to run time, which is
    /// exactly what makes #206 worth a deliberate owner decision rather than a quick fix.
    /// </summary>
    private const string E4b = """
        Function Nada() As String
            Return Nothing
        End Function
        Sub Check(s As String)
            Console.WriteLine(s = "")
            Console.WriteLine("" = s)
            Console.WriteLine(s <> "")
            Console.WriteLine(s Is Nothing)
        End Sub
        Sub Main()
            Check(Nada())
        End Sub
        """;
    private const string E4bExpectedManaged = "False\nFalse\nTrue\nTrue";
    private const string E4bExpectedCpp = "True\nTrue\nFalse\nTrue";

    [Test]
    public void E4b_NothingEqualsEmptyString_Runtime_CppDivergesFromItsOwnFoldedAnswer_Against206()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E4b)), Is.EqualTo(E4bExpectedManaged), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E4b)), Is.EqualTo(E4bExpectedManaged), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E4b)), Is.EqualTo(E4bExpectedManaged), "MSIL");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(E4b))), Is.EqualTo(E4bExpectedCpp),
                "C++ — #206. If this now matches E4's answer (False/False/True/True) C++'s " +
                "fold-vs-runtime split is gone — update this pin to say so, do not just delete it.");
        });
    }
}
