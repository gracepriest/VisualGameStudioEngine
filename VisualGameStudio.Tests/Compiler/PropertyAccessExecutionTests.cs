using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #178, execution — BC30526/BC30524 taken all the way to a running program (or a refused
/// build) at every entry point: the CLI (standard pipeline), the CLI <c>--optimize</c> (aggressive
/// pipeline), and a Release <c>.blproj</c> build (<c>CompileProjectFiles</c> — a different code
/// path than the CLI's, per CLAUDE.md's "test both entry points"). <see cref="PropertyAccessDiagnosticsTests"/>
/// is the fast-subset sibling that pins the diagnostic's code, message and line directly off the
/// analyzer.
///
/// <para>Probe sources and expected values are the implementer's own measured ones
/// (<c>S/t178/probes2</c>, <c>S/t178/edge</c>, <c>S/t178/edge/vb-edge.txt</c> for VB's verdict) —
/// never re-derived from what a backend under test prints.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // FourBackends' C# leg redirects Console.Out
public class PropertyAccessExecutionTests
{
    // ====================================================================================
    // L1 — a ReadOnly auto-property assigned bare and via Me. in its own constructor. This was
    // the MSIL `set_P` MissingMethodException (probe L1, S/t178/probes2/L1.bas / L1.exp).
    // ====================================================================================

    private const string L1 = """
        Class Ctx
            Public ReadOnly Property P As Integer
            Public ReadOnly Property Q As Integer
            Public Sub New(a As Integer)
                P = a
                Me.Q = a + 1
            End Sub
        End Class
        Sub Main()
            Dim o As New Ctx(42)
            Console.WriteLine(o.P & " " & o.Q)
        End Sub
        """;

    private const string L1Expected = "42 43";

    [Test]
    public void L1_ReadOnlyAutoPropertyInitializedInOwnCtor_RunsOnEveryBackend() =>
        FourBackends.RunsOnEveryBackend(L1, L1Expected);

    [Test]
    public void L1_ReadOnlyAutoPropertyInitializedInOwnCtor_RunsOnEveryBackendAggressive() =>
        FourBackends.RunsOnEveryBackendAggressive(L1, L1Expected);

    [Test]
    public void L1_ReadOnlyAutoPropertyInitializedInOwnCtor_RunsOnMsilReleaseBlproj() =>
        Assert.That(FourBackends.Norm(BuildReleaseAndRun(L1, "MSIL")), Is.EqualTo(L1Expected));

    // ====================================================================================
    // E12b, E16, E24, E27 — went from MSIL RUN-FAIL (MissingMethodException: set_P) to the
    // correct value, both pipelines. Asserted against the implementer's .exp / VB's own verdict
    // (S/t178/edge/vb-edge.txt), never re-derived from MSIL's own output.
    // ====================================================================================

    // E12b — MyBase.New() then a bare write in the SAME constructor.
    private const string E12b = """
        Class Ctx
            Public ReadOnly Property P As Integer
            Public Sub New()
                MyBase.New()
                P = 4
            End Sub
        End Class
        Sub Main()
            Dim c As New Ctx()
            Console.WriteLine(c.P)
        End Sub
        """;

    [Test]
    public void E12b_MyBaseNewThenBareWrite_Msil() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(E12b)), Is.EqualTo("4"));

    [Test]
    public void E12b_MyBaseNewThenBareWrite_MsilAggressive() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(E12b)), Is.EqualTo("4"));

    // E16 — a compound assignment chain on the ReadOnly auto-property, inside its own
    // constructor: bare +=, then Me. *=. Both the write (carve-out) and the read half of each
    // compound (ReadOnly allows reading) are legal.
    private const string E16 = """
        Class Ctx
            Public ReadOnly Property P As Integer
            Public Sub New(a As Integer)
                P = a
                P += 10
                Me.P *= 2
            End Sub
        End Class
        Sub Main()
            Dim c As New Ctx(1)
            Console.WriteLine(c.P)
        End Sub
        """;

    [Test]
    public void E16_CompoundAssignmentChainInOwnCtor_Msil() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(E16)), Is.EqualTo("22"));

    [Test]
    public void E16_CompoundAssignmentChainInOwnCtor_MsilAggressive() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(E16)), Is.EqualTo("22"));

    // E24 — the SAME shape as L1's single-property case, but the property is Overridable.
    private const string E24 = """
        Class Ctx
            Public Overridable ReadOnly Property P As Integer
            Public Sub New(a As Integer)
                P = a
            End Sub
        End Class
        Sub Main()
            Dim c As New Ctx(9)
            Console.WriteLine(c.P)
        End Sub
        """;

    [Test]
    public void E24_OverridableReadOnlyAutoProperty_Msil() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(E24)), Is.EqualTo("9"));

    [Test]
    public void E24_OverridableReadOnlyAutoProperty_MsilAggressive() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(E24)), Is.EqualTo("9"));

    // E27 — an If/Else block, then a counted For loop, both writing the SAME ReadOnly
    // auto-property inside its own constructor: -4 -> If/Else -> 4 -> +1 -> +2 -> 7.
    private const string E27 = """
        Class Ctx
            Public ReadOnly Property P As Integer
            Public Sub New(a As Integer)
                If a > 0 Then
                    P = a
                Else
                    P = -a
                End If
                For i As Integer = 1 To 2
                    P = P + i
                Next
            End Sub
        End Class
        Sub Main()
            Dim c As New Ctx(-4)
            Console.WriteLine(c.P)
        End Sub
        """;

    [Test]
    public void E27_IfElseThenForLoopInOwnCtor_Msil() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(E27)), Is.EqualTo("7"));

    [Test]
    public void E27_IfElseThenForLoopInOwnCtor_MsilAggressive() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(E27)), Is.EqualTo("7"));

    /// <summary>#218 — was a SILENT WRONG ANSWER, now vbc's: the C++ backend ran E16's compound-assignment
    /// chain to completion and printed 2 for 22, because a computed bare store to a plain auto-property
    /// (`P += 10`, `Me.P *= 2` on a ReadOnly one, inside its own constructor) was dropped. It now prints
    /// "22" like every other backend (moved from `..._PinsSilentWrongAnswer_Against218`; the C++ backend's
    /// `IsStorageAutoProperty`). The fixture `CppAutoPropertyBareStoreExecutionTests` covers the shape
    /// through the CLI, `--optimize` and `CompileProjectFiles`; this row keeps the in-process optimizing
    /// helper.</summary>
    [Test]
    public void E16_CompoundAssignmentChainInOwnCtor_Cpp()
    {
        Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(E16))),
            Is.EqualTo("22"),
            "task #218: a compound-assignment chain on a ReadOnly auto-property inside its own "
            + "constructor must print VB's/every other backend's '22' on C++ too. '2' means the C++ "
            + "backend dropped the computed bare stores again (CppCodeGenerator.IsStorageAutoProperty).");
    }

    /// <summary>#218's second probe — the same shape, an If/Else block then a counted For loop instead of a
    /// compound chain. Was a pin printing "0"; now vbc's "7" (moved from
    /// `..._PinsSilentWrongAnswer_Against218`).</summary>
    [Test]
    public void E27_IfElseThenForLoopInOwnCtor_Cpp()
    {
        Assert.That(
            FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(E27))),
            Is.EqualTo("7"),
            "task #218: an If/Else block then a For loop writing a ReadOnly auto-property inside its own "
            + "constructor must print VB's/every other backend's '7' on C++ too. '0' means the C++ "
            + "backend dropped the computed bare stores again (CppCodeGenerator.IsStorageAutoProperty).");
    }

    // ====================================================================================
    // Pins — each names the task it belongs to. Not fixed here; #178's own scope is the
    // diagnostic, not these pre-existing backend gaps its probes surfaced.
    // ====================================================================================

    /// <summary>#219 — E09 (the accessor's implicit GET RETURN VARIABLE: inside a Get, a bare `P`
    /// names that variable, not the property, so `P = v` is never BC30526 — see
    /// <c>PropertyAccessDiagnosticsTests.Legal_AssignmentToPInsideItsOwnGet_IsTheGetterReturnVariable</c>).
    /// Task #178 left it exempt from BC30526 but unimplemented, so every backend failed to RUN it
    /// (MSIL: MissingMethodException set_P). #219 implements the variable
    /// (<c>SemanticAnalyzer.AsReturnVariable</c>, the IR carrier <c>__ret</c>), and E09 now prints
    /// VB's own 43. <c>ImplicitReturnVariableExecutionTests</c> runs the family on all four backends.</summary>
    private const string E09 = """
        Class Ctx
            Private _v As Integer = 42
            Public ReadOnly Property P As Integer
                Get
                    P = _v + 1
                End Get
            End Property
        End Class
        Sub Main()
            Dim o As New Ctx()
            Console.WriteLine(o.P)
        End Sub
        """;

    [Test]
    public void E09_GetterReturnVariableWrite_Msil_PrintsVbs43_Task219()
    {
        // Not inside Assert.Multiple: the harness may Ignore (no ilasm), and an Ignore inside one FAILS the test.
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(E09)), Is.EqualTo("43"),
            "task #219: `P = _v + 1` inside the Get is the Get's return variable, so it returns 43 (was MissingMethodException set_P)");
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(E09)), Is.EqualTo("43"),
            "task #219: the same through the aggressive pipeline");
    }

    // (#220's pin, `ExceptionMessageWrite_FrontEndAccepts_PinsPreExistingGap_Against220`, stood
    // here. #220 is fixed — the built-in Exception's Message / StackTrace / InnerException are
    // ReadOnly at their SymbolTable declaration — and the pin moved to the fast fixture as its
    // positive row, PropertyAccessDiagnosticsTests.Write_ExceptionMessage_InACatch_IsRefused.)

    /// <summary>#222 — was the half of #220 that was NOT fixed (the pin `..._FrontEndAccepts_PinsPreExistingGap_Against222`,
    /// moved here as its positive row): an exception typed as a .NET exception CLASS
    /// (<c>ArgumentException</c>, and every <c>System.*Exception</c> but the built-in <c>Exception</c>) does
    /// not bind <c>Message</c> to the built-in symbol #220 marked ReadOnly, so its write was accepted
    /// (vbc: BC30526). It inherits that member now (<c>SemanticAnalyzer.ReadOnlyNetPropertyName</c>).
    /// Before: C# CS0200, MSIL MissingFieldException, JavaScript ran and printed the old message, C++ did
    /// not compile (<c>S/t220/probes/EArg.bas</c>). The per-source rows and every entry point are
    /// <c>NetReadOnlyPropertyDiagnosticsTests</c> / <c>NetReadOnlyPropertyExecutionTests</c>.</summary>
    [Test]
    public void ArgumentExceptionMessageWrite_IsRefusedWithBC30526_Task222()
    {
        const string source = """
            Sub Main()
                Dim a As ArgumentException = New ArgumentException("m")
                a.Message = "x"
                Console.WriteLine(a.Message)
            End Sub
            """;
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var refusals = analyzer.Errors.Where(e => e.ErrorCode == "BC30526").ToList();
        Assert.That(refusals, Has.Count.EqualTo(1),
            "task #222: `a.Message = ...` on an ArgumentException is vbc's BC30526 (it inherits the built-in "
            + "Exception's ReadOnly Message). None means a .NET exception class stopped inheriting it.\nerrors: "
            + string.Join(" | ", analyzer.Errors.Select(e => e.ToString())));
        Assert.That(refusals[0].Message, Does.Contain("Property 'Message' is 'ReadOnly'."), "vbc's message");
        Assert.That(refusals[0].Line, Is.EqualTo(3), "on the write's line");
    }

    // (#221's pin, `BareForLoop_OverAReadWriteProperty_FrontEndAccepts_PinsPreExistingGap_Against221`,
    // stood here. #221 is fixed — a For control variable bound to a property is VB's BC30039 — and
    // the pin moved to the fast fixture as its refusal row,
    // LoopControlPropertyDiagnosticsTests.ACountedFor_OverAnAutoProperty_IsBC30039.)

    // N1 / N2 — a .NET ReadOnly property (String.Length, List(Of T).Count). #222 moved both: they were the
    // two shapes BasicLang accepted (rule 5: the resolver carried no .NET settability fact) and JavaScript
    // then RAN them silently wrong — N1 threw `TypeError: Cannot create property 'Length'` under Node's
    // always-strict ES module, N2 printed the real count, 1, the write dropped. The write is vbc's BC30526
    // now, on every backend (`SemanticAnalyzer.ReadOnlyNetPropertyName`), so JavaScript is never reached.

    /// <summary>The JavaScript route (<c>JsTestSupport.Compile</c>: parse, analyze, IR, generate) stops at the front end, with vbc's message.</summary>
    private static void AssertJavaScriptCompileRefuses(string source, string property)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsTestSupport.Compile(source),
            "task #222: the front end must refuse the write, so no JavaScript is generated for it (before: JS ran and printed the old value)");
        Assert.That(ex!.Message, Does.Contain($"Property '{property}' is 'ReadOnly'."),
            "task #222: refused as vbc refuses it (BC30526), not for some other reason.\n" + ex.Message);
    }

    private const string N1 = """
        Sub Main()
            Dim s As String = "abc"
            s.Length = 3
            Console.WriteLine(s)
        End Sub
        """;

    /// <summary>#222 — N1, was `..._NotRefused_JavaScriptThrowsTypeError_PinnedForTask222` (a JS string is a primitive, so the
    /// emitted <c>s.Length = 3;</c> threw a real <c>TypeError</c> under Node). A String's Length is get-only
    /// (<c>BclReadOnlyProperties</c>), so it is BC30526 before any backend. (C# said CS0200 and MSIL
    /// MissingFieldException; <c>NetReadOnlyPropertyExecutionTests</c> takes the same write through the CLI on all four targets.)</summary>
    [Test]
    public void N1_DotNetStringLengthWrite_IsRefusedWithBC30526_OnJavaScript_Task222()
        => AssertJavaScriptCompileRefuses(N1, "Length");

    private const string N2 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Count = 5
            Console.WriteLine(l.Count)
        End Sub
        """;

    /// <summary>#222 — N2, was `..._NotRefused_JavaScriptPrintsRealCount_PinnedForTask222` (JavaScript printed the REAL
    /// count, 1: the write to Count never resized anything). A List's Count is get-only, so it is BC30526.</summary>
    [Test]
    public void N2_DotNetListCountWrite_IsRefusedWithBC30526_OnJavaScript_Task222()
        => AssertJavaScriptCompileRefuses(N2, "Count");

    // ====================================================================================
    // #223 (FIXED) — the native C++ .blproj build printed the code twice: BasicLang's message
    // already starts with "BC30526: " (PropertyAccessError), and the C++ project builder's
    // diagnostic formatter prints the code field too, so the output read
    // "error BC30526: BC30526: Property 'P' is 'ReadOnly'.". CppProjectBuilder now takes the code
    // off the message's head where it builds the CppDiagnostic; this is the real CLI's leg of
    // DiagnosticCodeOnceTests. Skips (not fails) without a C++ toolchain.
    // ====================================================================================

    [Test]
    public void CppReleaseBuild_PrintsTheDiagnosticCodeOnce_Task223()
    {
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC) — cannot see the native "
                + "build's own error text on this machine.");

        var (exitCode, stdOut, stdErr) = BuildRelease("""
            Class C
                Public ReadOnly Property P As Integer
                    Get
                        Return 1
                    End Get
                End Property
            End Class
            Sub Main()
                Dim o As New C()
                o.P = 99
            End Sub
            """, "Cpp");
        var output = stdOut + "\n" + stdErr;

        Assert.That(exitCode, Is.Not.EqualTo(0), "the Release C++ build must still refuse this program.\n" + output);
        Assert.That(output, Does.Contain("Main.bas(10,5): error BC30526: Property 'P' is 'ReadOnly'."),
            "task #223: the native C++ .blproj build prints the MSBuild shape with the code once "
            + "(was 'error BC30526: BC30526: …').\nOUTPUT:\n" + output);
        Assert.That(System.Text.RegularExpressions.Regex.Matches(output, "BC30526").Count, Is.EqualTo(1),
            "task #223: exactly one BC30526 in the whole build output.\nOUTPUT:\n" + output);
    }

    // ====================================================================================
    // The CLI refuses A1, B1 and A6b on every --target, and the Release .blproj build refuses
    // them too — both entry points, per CLAUDE.md.
    // ====================================================================================

    private const string A1 = """
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 42
                End Get
            End Property
        End Class
        Sub Main()
            Dim o As New C()
            o.P = 99
        End Sub
        """;

    private const string B1 = """
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
        End Class
        Sub Main()
            Dim o As New C()
            Dim x As Integer = o.W
        End Sub
        """;

    private const string A6b = """
        Interface IShape
            ReadOnly Property P As Integer
        End Interface
        Class C
            Implements IShape
            Public Property P As Integer
        End Class
        Sub Main()
            Dim o As IShape = New C()
            o.P = 99
        End Sub
        """;

    private static readonly string[] AllCliTargets = { "csharp", "cpp", "javascript", "msil" };

    [TestCase(nameof(A1), "BC30526")]
    [TestCase(nameof(B1), "BC30524")]
    [TestCase(nameof(A6b), "BC30526")]
    public void SingleFileCli_RefusesOnEveryTarget(string which, string code)
    {
        var source = which switch { nameof(A1) => A1, nameof(B1) => B1, nameof(A6b) => A6b,
            _ => throw new ArgumentOutOfRangeException(nameof(which)) };

        var dir = Path.Combine(Path.GetTempPath(), "bl-propaccess-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var basFile = Path.Combine(dir, "Program.bas");
            File.WriteAllText(basFile, source);

            Assert.Multiple(() =>
            {
                foreach (var target in AllCliTargets)
                {
                    var (exitCode, stdOut, stdErr) = CliTestHarness.RunProcess(
                        CliTestHarness.CliPath(), new[] { basFile, "--target=" + target }, dir, timeoutMs: 60_000);
                    var output = stdOut + "\n" + stdErr;
                    Assert.That(exitCode, Is.Not.EqualTo(0), $"--target={target} must refuse.\n{output}");
                    Assert.That(output, Does.Contain(code), $"--target={target} must name {code}.\n{output}");
                }
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    [Test]
    public void ReleaseBlprojBuild_AlsoRefuses_TheSameProgramTheCliRefuses()
    {
        var (exitCode, stdOut, stdErr) = BuildRelease(A1, "MSIL");
        var output = stdOut + "\n" + stdErr;

        Assert.That(exitCode, Is.Not.EqualTo(0), "a Release .blproj build must refuse this too.\n" + output);
        Assert.That(output, Does.Contain("BC30526"), output);
    }

    // ====================================================================================
    // Shared Release .blproj helper — the CLI's "-c Release" -> OptimizeAggressive mapping
    // (Program.cs), a genuinely different code path than MsilHarness.CompileToIl's direct
    // AggressivePipeline.Apply call. Same idiom as MsilObjectBoxingExecutionTests.
    // BuildReleaseMsilAndRun / MeReceiverTypingExecutionTests.BuildReleaseMsilAndRun.
    // ====================================================================================

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-propaccess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private (int ExitCode, string StdOut, string StdErr) BuildRelease(string basSource, string targetBackend)
    {
        File.WriteAllText(Path.Combine(_projectDir, "Main.bas"), basSource);
        File.WriteAllText(Path.Combine(_projectDir, "App.blproj"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>{targetBackend}</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        return CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(_projectDir, "App.blproj"), "-c", "Release" },
            _projectDir,
            timeoutMs: 120_000);
    }

    private string BuildReleaseAndRun(string basSource, string targetBackend)
    {
        var (buildExit, buildOut, buildErr) = BuildRelease(basSource, targetBackend);
        Assert.That(buildExit, Is.EqualTo(0),
            $"CLI Release {targetBackend} build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

        var ilFiles = Directory.GetFiles(_projectDir, "App.il", SearchOption.AllDirectories);
        Assert.That(ilFiles, Is.Not.Empty,
            $"CLI build claimed success but produced no App.il.\nSTDOUT:\n{buildOut}");

        return MsilHarness.RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App");
    }
}
