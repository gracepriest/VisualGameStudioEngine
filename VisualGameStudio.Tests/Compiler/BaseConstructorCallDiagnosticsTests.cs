using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0016 D4: VB's <b>BC31095</b> ("Reference to object under construction is not valid when
/// calling another constructor") for an explicit <c>Me</c> / <c>MyBase</c>, and <b>BC31096</b>
/// ("Implicit reference to object under construction ...") for an instance member named without
/// its qualifier, anywhere in <c>MyBase.New</c>'s arguments — a lambda written there, and a lambda
/// nested in it, included. It lives in <c>SemanticAnalyzer</c>, so the compiler (every backend) and
/// the LSP share ONE answer: four backend-specific outcomes (CS0103, a ReferenceError, a refusal,
/// and an accidental run) become one front-end diagnostic. It is also the precondition of D3's
/// MSIL ordering — without it an environment would hand a lambda a null <c>Me</c>.
///
/// <para>The shapes are the implementer's <c>S/t170/d4/X01-X26</c> and <c>ctrlV/V1-V4</c>, each
/// checked against the real VB compiler (<c>d4/vb.txt</c>): the refused ones give VB's own code,
/// the not-refused ones RUN in VB. C4 (binding): "a Shared member, a module function, a constant,
/// <c>MyBase.New</c>'s own parameters and a Shared member reached through the class name are NOT
/// refused" — an over-firing diagnostic is worse than none, so those are half of this file.</para>
///
/// <para>⚠ ONE KNOWN GAP, pinned as current behaviour with the gap named (never "fixed" by
/// loosening a pin): <see cref="X07_MyClass_IsAGap_ATypeErrorInsteadOfBC31095"/>. The other one, X23
/// (`Me` as a bare value, which needs VB's conditional <c>If(c, x, y)</c> to write), CLOSED with #123:
/// it parses now and is a row of <c>Refused()</c>, BC31095 like vbc.</para>
/// </summary>
[TestFixture]
public class BaseConstructorCallDiagnosticsTests
{
    // ============================================================================================
    // Shapes. Two headers, one substituted line: the <<ARG>> of `MyBase.New(<<ARG>>)`.
    // ============================================================================================

    /// <summary>The Integer-base header (X01-X08, X14-X20): a base taking one Integer, plus every
    /// kind of name the argument might reach — an instance field <c>A</c>, a Shared field <c>S</c>,
    /// a Const <c>K</c>, a Property <c>P</c>, an instance method <c>Inst</c>, a Shared method
    /// <c>Sh</c>, a module function <c>ModF</c>, a module global <c>G</c>, and the base's own
    /// <c>N</c> / <c>BaseInst</c>.</summary>
    private const string IntHeader = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
            Public Function BaseInst() As Integer
                Return 5
            End Function
        End Class
        Function ModF(x As Integer) As Integer
            Return x + 1
        End Function
        Module Globals
            Public G As Integer = 3
        End Module

        Class D
            Inherits Base
            Public A As Integer
            Public Shared S As Integer = 4
            Public Const K As Integer = 6
            Public Property P As Integer
            Public Function Inst() As Integer
                Return 7
            End Function
            Public Shared Function Sh() As Integer
                Return 8
            End Function
            Public Sub New(p0 As Integer)
                MyBase.New(<<ARG>>)
            End Sub
        End Class
        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.N)
        End Sub
        """;

    /// <summary>The delegate-base header (X09-X13, X21, X22, X25, X26): a base taking a
    /// <c>Func(Of Integer)</c>, plus <c>Apply</c> so a lambda can nest one.</summary>
    private const string FuncHeader = """
        Class Base
            Public F As Func(Of Integer)
            Public Sub New(f As Func(Of Integer))
                Me.F = f
            End Sub
        End Class
        Function Apply(f As Func(Of Integer, Integer), v As Integer) As Integer
            Return f(v)
        End Function
        Module Globals
            Public G As Integer = 3
        End Module

        Class D
            Inherits Base
            Public A As Integer
            Public Shared S As Integer = 4
            Public Const K As Integer = 6
            Public Function Inst() As Integer
                Return 7
            End Function
            Public Shared Function Sh() As Integer
                Return 8
            End Function
            Public Sub New(p0 As Integer)
                MyBase.New(<<ARG>>)
            End Sub
        End Class
        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.F())
        End Sub
        """;

    private static string Int(string arg) => IntHeader.Replace("<<ARG>>", arg);
    private static string Fn(string arg) => FuncHeader.Replace("<<ARG>>", arg);

    // V1-V4 (ADR-0016's own four): the smallest shapes, a plain class D with `A`.
    private const string V1 = """
        Class Base
            Public F As Action
            Public Sub New(f As Action)
                Me.F = f
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(Sub() A = p)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            d.F()
            Console.WriteLine(d.A)
        End Sub
        """;

    private const string V2 = """
        Class Base
            Public F As Action
            Public Sub New(f As Action)
                Me.F = f
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(Sub() Console.WriteLine(Me.A))
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            d.F()
            Console.WriteLine(d.A)
        End Sub
        """;

    private const string V3 = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(Me.A + p)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.N)
        End Sub
        """;

    private const string V4 = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(A + p)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.N)
        End Sub
        """;

    private const string Implicit = "BC31096";
    private const string Explicit = "BC31095";

    // ============================================================================================
    // Plumbing: the analyzer alone (what the LSP shares), and the whole compiler per backend
    // ============================================================================================

    private static string[] Analyze(string source) => OptionalConstructorTests.Analyze(source);

    /// <summary>The compiler's own error messages for <paramref name="source"/> on
    /// <paramref name="backend"/>, through <c>CompileFile</c> (the CLI's single-file entry
    /// point).</summary>
    private static List<string> CompilerErrors(string source, string backend)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t170-d4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var result = new BasicCompiler(new CompilerOptions { TargetBackend = backend }).CompileFile(path);
            return result.AllErrors.Select(e => e.Message).ToList();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static readonly string[] Backends = { "csharp", "cpp", "javascript", "msil" };

    // ============================================================================================
    // Refused: BC31096 (an instance member named WITHOUT its qualifier) and BC31095 (an explicit
    // Me / MyBase) — direct, inside a lambda, and inside a lambda nested in a lambda
    // ============================================================================================

    /// <summary>(name, source, expected code). Every row is a program VB itself rejects with
    /// exactly that code (<c>S/t170/d4/vb.txt</c>).</summary>
    private static IEnumerable<TestCaseData> Refused()
    {
        // implicit -> BC31096
        yield return new TestCaseData(Int("A + p0"), Implicit).SetName("X01_field");
        yield return new TestCaseData(Int("Inst() + p0"), Implicit).SetName("X03_instanceCall");
        yield return new TestCaseData(Int("P"), Implicit).SetName("X05_property");
        yield return new TestCaseData(Int("N + 1"), Implicit).SetName("X08_baseFieldBare");
        yield return new TestCaseData(Fn("Function() A"), Implicit).SetName("X09_lambdaField");
        yield return new TestCaseData(Fn("Function() Apply(Function(x As Integer) x + A, 1)"), Implicit).SetName("X11_nestedLambdaField");
        yield return new TestCaseData(Fn("Function() Inst()"), Implicit).SetName("X13_lambdaInstanceCall");
        yield return new TestCaseData(V1, Implicit).SetName("V1_lambdaAssignsAField");
        yield return new TestCaseData(V4, Implicit).SetName("V4_fieldBare");

        // explicit -> BC31095
        yield return new TestCaseData(Int("Me.A + p0"), Explicit).SetName("X02_meField");
        yield return new TestCaseData(Int("Me.Inst()"), Explicit).SetName("X04_meCall");
        yield return new TestCaseData(Int("MyBase.BaseInst()"), Explicit).SetName("X06_myBaseCall");
        yield return new TestCaseData(Fn("Function() Me.A"), Explicit).SetName("X10_lambdaMe");
        yield return new TestCaseData(Fn("Function() Apply(Function(x As Integer) x + Me.A, 1)"), Explicit).SetName("X12_nestedLambdaMe");
        yield return new TestCaseData(V2, Explicit).SetName("V2_lambdaMe");
        yield return new TestCaseData(V3, Explicit).SetName("V3_meField");

        // `Me` as a bare VALUE in the arguments. It needed VB's conditional `If(c, x, y)` to write (#123 made it
        // parse); vbc reports BC31095 for it, and so does the analyzer, on every backend.
        yield return new TestCaseData(Int("CType(If(Me Is Nothing, 1, 2), Integer)"), Explicit).SetName("X23_meAsABareValue");
    }

    /// <summary>The same rows under distinct names, so every case has a unique full name.</summary>
    private static IEnumerable<TestCaseData> RefusedOnEveryBackend()
        => Refused().Select(t => new TestCaseData(t.Arguments).SetName("AllBackends_" + t.TestName));

    /// <summary>
    /// The analyzer — the one place the compiler AND the LSP get the diagnostic from — reports
    /// exactly one error, carrying VB's own code for the shape. Exactly one: a second error for the
    /// same node is how a per-backend duplicate would look.
    /// </summary>
    [TestCaseSource(nameof(Refused))]
    public void TheAnalyzer_ReportsVbsOwnCode(string source, string code)
    {
        var errors = Analyze(source);
        Assert.That(errors, Has.Length.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain(code), string.Join(" | ", errors));
    }

    /// <summary>
    /// ⭐ "The same on every backend": through the WHOLE compiler on all four targets the program
    /// is refused with the same code, and no backend gets far enough to say anything of its own
    /// (a CS0103, a ReferenceError, a MSIL refusal or a silent run — the four outcomes this
    /// diagnostic replaces).
    /// </summary>
    [TestCaseSource(nameof(RefusedOnEveryBackend))]
    public void EveryBackend_IsRefusedWithTheSameCode(string source, string code)
    {
        Assert.Multiple(() =>
        {
            foreach (var backend in Backends)
            {
                var errors = CompilerErrors(source, backend);
                Assert.That(errors, Has.Count.EqualTo(1), $"{backend}: " + string.Join(" | ", errors));
                Assert.That(errors[0], Does.Contain(code), $"{backend}: " + string.Join(" | ", errors));
            }
        });
    }

    // ============================================================================================
    // NOT refused — C4: a Shared member, a module function, a constant, a parameter, a global,
    // a Shared member through the class name, all of them inside a lambda too
    // ============================================================================================

    private static IEnumerable<TestCaseData> NotRefused()
    {
        yield return new TestCaseData(Int("S + p0"), "5").SetName("X14_sharedField");
        yield return new TestCaseData(Int("Sh() + p0"), "9").SetName("X15_sharedCall");
        yield return new TestCaseData(Int("ModF(p0)"), "2").SetName("X16_moduleFunction");
        yield return new TestCaseData(Int("K + p0"), "7").SetName("X17_constant");
        yield return new TestCaseData(Int("p0"), "1").SetName("X18_ownParameter");
        yield return new TestCaseData(Int("D.S + D.Sh()"), "12").SetName("X19_sharedThroughTheClassName");
        yield return new TestCaseData(Int("G + p0"), "4").SetName("X20_moduleGlobal");
        yield return new TestCaseData(Fn("Function() S + Sh() + K + G + p0"), "22").SetName("X21_lambdaOverAllTheAllowedNames");
        yield return new TestCaseData(Fn("Function() D.S + D.Sh()"), "12").SetName("X26_lambdaSharedThroughTheClassName");
    }

    /// <summary>Not refused, on any backend, and — because an over-firing diagnostic is worse than
    /// none — RUNNING with VB's own answer (<c>d4/*.exp</c>): "not refused" that then fails to
    /// build would prove nothing. MSIL and C++ included; none of these nest a lambda.</summary>
    [Test, TestCaseSource(nameof(NotRefused))]
    [Category("Integration")]
    [NonParallelizable] // the C# leg redirects Console.Out
    public void TheAllowedNames_AreNotRefused_AndRunWithVbsAnswer(string source, string expected)
    {
        Assert.That(Analyze(source), Is.Empty);
        FourBackends.RunsOnEveryBackend(source, expected);
    }

    /// <summary>X22 / X25: a lambda NESTED in the base-args lambda that reads only the constructor's
    /// own parameter (X22) or nothing at all (X25) — not refused. MSIL is excluded: a nested lambda in
    /// a class member fails <c>ilasm</c> there (#241, pre-existing; see
    /// <see cref="BaseConstructorCallLoweringExecutionTests.E01_NestedLambda_CSharpAndJavaScriptRun_CppRefusedByName_MsilPinnedForTask241"/>).</summary>
    [TestCase("Function() Apply(Function(x As Integer) x + p0, 1)", "2", TestName = "X22_nestedLambdaOverTheOwnParameter")]
    [TestCase("Function() Apply(Function(x As Integer) x, 1)", "1", TestName = "X25_nestedLambdaOverNothing")]
    [Category("Integration")]
    [NonParallelizable]
    public void ANestedLambda_OverTheOwnParameter_IsNotRefused_AndRunsOnCSharpJavaScriptAndCpp(string arg, string expected)
    {
        var source = Fn(arg);
        Assert.That(Analyze(source), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected), "C++");
        });
    }

    /// <summary>The refusal is about the argument LIST, not the class: the same lambda written in
    /// the constructor BODY, after the base call, reads <c>Me</c> and its fields freely (E14/E15).</summary>
    [Test]
    public void AMemberReadInTheConstructorBody_IsNotRefused()
    {
        Assert.That(Analyze("""
            Class Base
                Public F As Action
                Public Sub New(f As Action)
                    Me.F = f
                End Sub
            End Class
            Class D
                Inherits Base
                Public A As Integer
                Public G As Func(Of Integer)
                Public Sub New(p As Integer)
                    MyBase.New(Sub() p = p + 1)
                    A = 5
                    G = Function() A + p + Me.A
                End Sub
            End Class
            Sub Main()
                Dim d As New D(1)
            End Sub
            """), Is.Empty);
    }

    // ============================================================================================
    // The LSP path
    // ============================================================================================

    /// <summary>
    /// ⭐ C4: BC31095/BC31096 REACH THE LSP. <c>DocumentManager.UpdateDocument</c> is what the
    /// language server calls on every edit; its <c>Diagnostics</c> are what the editor squiggles.
    /// V4 (<c>MyBase.New(A + p)</c>) must show BC31096 as an Error, positioned on the reference.
    /// </summary>
    [Test]
    public void TheLanguageServer_ShowsBC31096_ForV4()
    {
        var state = new DocumentManager().UpdateDocument(DocumentUri.From("untitled:V4.bas"), V4);

        var hit = state.Diagnostics.Where(d => d.Message.Contains(Implicit)).ToList();
        Assert.That(hit, Has.Count.EqualTo(1),
            string.Join(" | ", state.Diagnostics.Select(d => $"{d.Line}:{d.Column} {d.Message}")));
        Assert.That(hit[0].Severity.ToString(), Does.Contain("Error"));
        Assert.That(hit[0].Message, Does.Contain("Implicit reference to object under construction"));
    }

    /// <summary>The LSP shares the analyzer, so an explicit <c>Me</c> inside a NESTED lambda shows
    /// BC31095 there too — "the vbc code is matched per shape, a lambda and a nested lambda
    /// included" (C4).</summary>
    [Test]
    public void TheLanguageServer_ShowsBC31095_ForANestedLambdaMe()
    {
        var state = new DocumentManager().UpdateDocument(DocumentUri.From("untitled:X12.bas"),
            Fn("Function() Apply(Function(x As Integer) x + Me.A, 1)"));

        Assert.That(state.Diagnostics.Where(d => d.Message.Contains(Explicit)).ToList(), Has.Count.EqualTo(1),
            string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
    }

    /// <summary>And the LSP does NOT squiggle an allowed name (X14, a Shared field): a false
    /// positive here is a red line under valid code, on every keystroke.</summary>
    [Test]
    public void TheLanguageServer_ShowsNothing_ForASharedField()
    {
        var state = new DocumentManager().UpdateDocument(DocumentUri.From("untitled:X14.bas"), Int("S + p0"));

        Assert.That(state.Diagnostics.Where(d => d.Message.Contains("BC3109")).ToList(), Is.Empty,
            string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
    }

    // ============================================================================================
    // The two KNOWN GAPS — pinned as current behaviour, named
    // ============================================================================================

    /// <summary>
    /// ⚠ KNOWN GAP 1 (X07). VB rejects <c>MyBase.New(MyClass.Inst())</c> with BC31095. BasicLang does
    /// not support <c>MyClass</c> in this position at all: the reference types as <c>Object</c>, so it
    /// reports a TYPE MISMATCH ("Base constructor argument 1 of type 'Object' is not compatible with
    /// parameter 'n' of type 'Integer'") instead. Still an error — the program never builds — only
    /// the wrong one. Filed with ADR-0016's language gaps; when <c>MyClass</c> resolves this pin
    /// must move to BC31095 (it is NOT a reason to loosen D4).
    /// </summary>
    [Test]
    public void X07_MyClass_IsAGap_ATypeErrorInsteadOfBC31095()
    {
        var errors = Analyze(Int("MyClass.Inst()"));

        Assert.That(errors, Has.Length.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("Base constructor argument 1 of type 'Object' is not compatible"),
            "KNOWN GAP: MyClass is a type error, not BC31095. If this now says BC31095 the gap is closed — "
            + "update the pin deliberately.\n" + errors[0]);
        Assert.That(errors[0], Does.Not.Contain(Explicit));
    }
}
