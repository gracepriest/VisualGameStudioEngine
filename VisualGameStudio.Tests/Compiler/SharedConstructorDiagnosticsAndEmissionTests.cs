using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// #208, the fast half (see <see cref="SharedConstructorExecutionTests"/> for the defect, the oracle, the mutants and the known gaps): two cases that need no process.
///
/// <para><b>Diagnostics.</b> A `Shared Sub New` is the type initializer: parameterless, static, calling no base constructor, and one per class. The analyzer now refuses what VB refuses, with VB's number. Every
/// expectation below is what vbc answers for the same program (S/t136/tools/vbv2.py on S/t208/tw/diag/D1..D7), counted off the SOURCE text, and the test asserts nothing vbc does not say. Where BasicLang's SITE
/// or TEXT differs from vbc's, the test pins the part that is the same and the difference is listed here, not asserted:
///   * BC30043: vbc reports it on the `MyBase.New(2)` line; BasicLang on the `Shared Sub New` line. Asserted: the code, vbc's text, and a line inside that Shared Sub New.
///   * BC30269: vbc reports it on the FIRST declaration, as `'Private Shared Sub New()' has multiple definitions with identical signatures.`; BasicLang on the SECOND, as `'Shared Sub New()' has multiple
///     definitions with identical signatures in class 'C'.`. Asserted: the code, the clause both texts share, and a line that is one of the two declarations.
///   * D6 (`New C()` beside only `Public Sub New(x)` and a Shared Sub New): vbc is BC30455 (`Argument not specified for parameter 'x' ...`); BasicLang's text is its own and carries no code. Asserted: ONE error,
///     on the line of the `New`. D5 (a derived class declaring ONLY a Shared Sub New over a base that needs arguments): vbc's BC30387 text is BasicLang's, up to the full stop.
/// Each program also goes through <c>BasicCompiler.CompileProjectFiles</c>, which the IDE build and a Release .blproj call, and must give the same answer.</para>
///
/// <para><b>Emission.</b> A class with NO `Shared Sub New` must emit none of the type-initializer machinery on any backend: no C# `static C()`, no JavaScript `$typeInit` / `$C$` slot accessors, no C++
/// `blTypeInit_` (nor the `(C::blTypeInit_(), C::F)` access guard), no MSIL `.cctor`. This is the byte-identity guard for the access-site changes: they reach into every Shared field access of every class, and only
/// a class that has a type initializer may be spelled differently. The program still prints the right answer when the guard leaks onto a plain class (the call just runs a no-op), so nothing that RUNS can kill it:
/// the control is the same program with a `Shared Sub New` added, which must show every piece.</para>
///
/// <para><b>Mutants</b> (each built for real from a plain source copy of `92a0e3cc` with ONE change; M1-M6 are in the execution fixture's header, and M5 and M6 also turn the emission case red because they remove
/// a piece its control must show; M7-M12 were run against THIS fixture alone):
///   * M7  the C++ Shared field guard also wraps a class WITHOUT a type initializer      -> `AClassWithNoSharedSubNew_EmitsNoTypeInitializerMachinery_OnAnyBackend` ONLY: the C++ text gains `blTypeInit_`.
///   * M8  BC30479 (parameters on a Shared Sub New) is never reported                      -> `TheSharedSubNewRefusals_AreVbcs_AtTheirSites_AndTheLegalShapeIsAccepted`
///   * M9  BC30043 (`MyBase.New(args)` in one) is never reported                           -> the same case
///   * M10 BC30269 (a second Shared Sub New) is never reported                             -> the same case
///   * M11 a Shared Sub New registers a `.ctorN` again (so `New C()` binds to it)          -> the same case (D6)
///   * M12 a Shared Sub New counts as the class's instance constructor again               -> the same case (D5: BC30387 is not reported)
/// </para>
/// </summary>
[TestFixture]
public class SharedConstructorDiagnosticsAndEmissionTests
{
    /// <summary>One probe: its source, the number of errors vbc reports, and where / what. Lines are 1-based SOURCE lines.</summary>
    private sealed record Refusal(string Id, string Source, string? Code, string? Text, int[] Lines)
    {
        public override string ToString() => Id;
    }

    private static readonly Refusal D1_params = new("D1_params", """
        Class C
            Public Shared F As Integer
            Shared Sub New(x As Integer)
                F = x
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.F)
        End Sub
        """, "BC30479", "Shared 'Sub New' cannot have any parameters.", new[] { 3 });

    private static readonly Refusal D2_mybase = new("D2_mybase", """
        Class B
            Public Sub New(x As Integer)
            End Sub
        End Class
        Class C
            Inherits B
            Public Shared F As Integer
            Public Sub New()
                MyBase.New(1)
            End Sub
            Shared Sub New()
                MyBase.New(2)
                F = 1
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.F)
        End Sub
        """, "BC30043", "'MyBase' is valid only within an instance method.", new[] { 11, 12, 13, 14 });

    private static readonly Refusal D3_dup = new("D3_dup", """
        Class C
            Public Shared F As Integer
            Shared Sub New()
                F = 1
            End Sub
            Shared Sub New()
                F = 2
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.F)
        End Sub
        """, "BC30269", "has multiple definitions with identical signatures", new[] { 3, 6 });

    private static readonly Refusal D5_derivedOnlyShared = new("D5_derivedOnlyShared", """
        Class B
            Public Sub New(x As Integer)
            End Sub
        End Class
        Class D
            Inherits B
            Shared Sub New()
            End Sub
        End Class
        Sub Main()
        End Sub
        """, null, "Class 'D' must declare a 'Sub New' because its base class 'B' does not have an accessible 'Sub New' that can be called with no arguments", new[] { 5 });

    private static readonly Refusal D6_newNoArgs = new("D6_newNoArgs", """
        Class C
            Public V As Integer
            Public Sub New(x As Integer)
                V = x
            End Sub
            Shared Sub New()
            End Sub
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.V)
        End Sub
        """, null, null, new[] { 10 });

    /// <summary>vbc ACCEPTS it and prints 7: a Shared Sub New beside an instance constructor that takes an argument, which `New C(4)` uses.</summary>
    private const string D7_acceptedBeside = """
        Class C
            Public V As Integer
            Public Shared F As Integer
            Public Sub New(x As Integer)
                V = x
            End Sub
            Shared Sub New()
                F = 3
            End Sub
        End Class
        Sub Main()
            Dim c As New C(4)
            Console.WriteLine(c.V + C.F)
        End Sub
        """;

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a refusal) and analyze: every ERROR, in the analyzer's order.</summary>
    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).ToList();
    }

    /// <summary>The same program through <c>CompileProjectFiles</c> (the IDE build's entry point): every error it reports.</summary>
    private static List<SemanticError> Project(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t208-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var result = new BasicCompiler(new CompilerOptions()).CompileProjectFiles(new List<string> { path });
            return result.AllErrors.Where(e => e.Severity == ErrorSeverity.Error).ToList();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static void AssertRefused(string entry, Refusal probe, List<SemanticError> errors)
    {
        var where = $"{probe.Id} through {entry}";
        Assert.That(errors, Has.Count.EqualTo(1), $"{where}: vbc reports ONE error, got [{string.Join(" | ", errors.Select(e => $"line {e.Line}: {e.Message.Split('\n')[0]}"))}]");
        var error = errors[0];
        if (probe.Code != null) Assert.That(error.ErrorCode, Is.EqualTo(probe.Code), $"{where}: the error's code");
        if (probe.Text != null) Assert.That(error.Message, Does.Contain(probe.Text), $"{where}: vbc's text");
        Assert.That(probe.Lines, Does.Contain(error.Line), $"{where}: reported at line {error.Line}, vbc's site is one of [{string.Join(", ", probe.Lines)}]");
    }

    /// <summary>
    /// The refusals #208 adds, as vbc words and places them (see the header for where BasicLang's site or text differs and what is asserted instead): parameters on a Shared Sub New (BC30479), `MyBase.New(args)`
    /// in one (BC30043), a second one (BC30269), a derived class declaring only one over a base that needs arguments (BC30387's text, the implicit instance constructor kept), and `New C()` that no longer binds to
    /// the Shared one (D6). The control: a Shared Sub New beside an instance `Sub New(x)` is accepted, as vbc accepts it. Through the analyzer and through <c>CompileProjectFiles</c>.
    /// </summary>
    [Test]
    public void TheSharedSubNewRefusals_AreVbcs_AtTheirSites_AndTheLegalShapeIsAccepted()
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in new[] { D1_params, D2_mybase, D3_dup, D5_derivedOnlyShared, D6_newNoArgs })
            {
                AssertRefused("the analyzer", probe, Analyze(probe.Source));
                AssertRefused("CompileProjectFiles", probe, Project(probe.Source));
            }

            Assert.That(Analyze(D7_acceptedBeside), Is.Empty, "D7: the analyzer refused a Shared Sub New beside `Public Sub New(x As Integer)`");
            Assert.That(Project(D7_acceptedBeside), Is.Empty, "D7: CompileProjectFiles refused a Shared Sub New beside `Public Sub New(x As Integer)`");
        });
    }

    /// <summary>The plain class: Shared fields, an auto-property, a Shared function and an array, read, stored and `+=`ed. `withArray: false` is for MSIL, where a Shared array's own initializer is a `.cctor`.</summary>
    private static string Program(bool withSharedSubNew, bool withArray = true)
    {
        var source = """
            Class C
                Public Shared F As Integer
                Public Shared Arr(2) As Integer
                Public Shared Property P As Integer
                Public Shared Function Twice() As Integer
                    Return F * 2
                End Function
            End Class
            Sub Main()
                C.F = 1
                C.F += 1
                C.P = 3
                C.Arr(1) = 7
                Console.WriteLine(C.F + C.P + C.Arr(1) + C.Twice())
            End Sub
            """.Replace("\r\n", "\n");   // the raw string carries the checkout's line endings
        if (!withArray)
            source = source.Replace("    Public Shared Arr(2) As Integer\n", "").Replace("    C.Arr(1) = 7\n", "").Replace(" + C.Arr(1)", "");
        return withSharedSubNew
            ? source.Replace("End Function\nEnd Class", "End Function\n    Shared Sub New()\n        F = 5\n    End Sub\nEnd Class")
            : source;
    }

    /// <summary>
    /// A class with NO `Shared Sub New` emits no type-initializer machinery: not on C# (`static C()`), JavaScript (`$typeInit`, a `$C$` slot), C++ (`blTypeInit_`, so `C::F` is spelled bare) or MSIL (`.cctor`),
    /// through the standard passes (C++, JavaScript) and the aggressive ones (`CompileProjectFiles`, all four). The same program WITH a `Shared Sub New` shows every piece, so the absence is not a renamed piece.
    /// </summary>
    [Test]
    public void AClassWithNoSharedSubNew_EmitsNoTypeInitializerMachinery_OnAnyBackend()
    {
        string[] cMarks = { "static C()" };
        string[] jsMarks = { "$typeInit", "$C$" };
        string[] cppMarks = { "blTypeInit_" };
        string[] msilMarks = { ".cctor" };

        var plain = Program(withSharedSubNew: false);
        var plainNoArray = Program(withSharedSubNew: false, withArray: false);
        var withCtor = Program(withSharedSubNew: true);
        var withCtorNoArray = Program(withSharedSubNew: true, withArray: false);
        Assert.That(withCtor, Does.Contain("Shared Sub New"), "the control program lost its Shared Sub New (the Replace in Program() found nothing)");
        Assert.That(plainNoArray, Does.Not.Contain("Arr"), "the no-array program kept the array");

        var plainTexts = new List<(string Label, string Text, string[] Marks)>
        {
            ("C++, standard passes", BclE2E.CompileToCppOptimized(plain), cppMarks),
            ("JavaScript, standard passes", JsTestSupport.CompileOptimized(plain), jsMarks),
            ("C#, CompileProjectFiles", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, plain), cMarks),
            ("C++, CompileProjectFiles", TempExec.Emit(Bk.Cpp, EntryPoint.ProjectRelease, plain), cppMarks),
            ("JavaScript, CompileProjectFiles", TempExec.Emit(Bk.JavaScript, EntryPoint.ProjectRelease, plain), jsMarks),
            ("MSIL, CompileProjectFiles (no Shared array)", TempExec.Emit(Bk.Msil, EntryPoint.ProjectRelease, plainNoArray), msilMarks),
        };
        var controlTexts = new List<(string Label, string Text, string[] Marks)>
        {
            ("C++, standard passes", BclE2E.CompileToCppOptimized(withCtor), cppMarks),
            ("JavaScript, standard passes", JsTestSupport.CompileOptimized(withCtor), jsMarks),
            ("C#, CompileProjectFiles", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, withCtor), cMarks),
            ("C++, CompileProjectFiles", TempExec.Emit(Bk.Cpp, EntryPoint.ProjectRelease, withCtor), cppMarks),
            ("JavaScript, CompileProjectFiles", TempExec.Emit(Bk.JavaScript, EntryPoint.ProjectRelease, withCtor), jsMarks),
            ("MSIL, CompileProjectFiles (no Shared array)", TempExec.Emit(Bk.Msil, EntryPoint.ProjectRelease, withCtorNoArray), msilMarks),
        };

        Assert.Multiple(() =>
        {
            foreach (var (label, text, marks) in plainTexts)
                foreach (var mark in marks)
                    Assert.That(text, Does.Not.Contain(mark), $"{label}: a class with no Shared Sub New emitted '{mark}'");

            foreach (var (label, text, marks) in controlTexts)
                foreach (var mark in marks)
                    Assert.That(text, Does.Contain(mark), $"{label}: the control (WITH a Shared Sub New) has no '{mark}', so the absence above proves nothing");

            // C++: the access guard wraps a Shared field of a class that HAS a type initializer, and nothing else.
            Assert.That(controlTexts[0].Text, Does.Contain("(C::blTypeInit_(), C::F)"), "C++ control: no guarded Shared field access");
            Assert.That(plainTexts[0].Text, Does.Contain("C::F"), "C++: the plain program's Shared field is not spelled C::F");
        });
    }
}
