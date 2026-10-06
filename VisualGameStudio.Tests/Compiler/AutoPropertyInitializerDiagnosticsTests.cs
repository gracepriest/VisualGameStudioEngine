using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// #210, the fast half (see <see cref="AutoPropertyInitializerExecutionTests"/> for the defect, the oracle, the mutants and the known gaps): ONE case that needs no process.
///
/// <para><b>What an auto-property initializer is judged by.</b> The analyzer runs it through <c>CheckDeclaredInitializer</c>, the body a <c>Dim</c> / field initializer goes through, and refuses it on a property
/// that has a Get/Set block and on an interface property. Every expectation below that is vbc's was measured: S/t136/tools/vbv2.py on S/t210/probes P05, P11, P13, P14, P15 and their field twins.</para>
///
/// <para><b>Where vbc REFUSES (asserted as vbc's answer: the code or text, and the line, counted off the SOURCE).</b>
///   * P13, a property with a Get/Set block and `= 5`: BC36714 "Expanded Properties cannot be initialized." (vbc line 6 = source line 3, the three `Imports` lines the wrapper adds).
///   * P15, an interface `Property P As Integer = 5`: the same BC36714 (source line 2).
///   * P11, `Property B As Byte = 300`: BC30439 "Constant expression not representable in type 'Byte'." (source line 2). BasicLang words it without a code, so the text is what is asserted.
/// BasicLang's TEXT continues past vbc's full stop (it says how to fix it); only the shared prefix is asserted.</para>
///
/// <para><b>Where vbc ACCEPTS (so NOTHING here claims vbc refuses; the cases pin only that a property is judged the way the field written in its place is).</b>
///   * P14, `Property I As Integer = "x"`: vbc compiles it (Option Strict off, the default) and throws InvalidCastException at run time. BasicLang refuses the conversion, for a field too.
///   * P05, `Property P As Integer = Twice(3)`: vbc compiles and runs it (prints 6). BasicLang refuses a non-constant initializer, for a field too (#236, a known gap, not pinned).
/// Each of these is compared, live, with the identical program declared as a FIELD: the same errors at the same lines, the declaration-kind word ("property" / "field" / "variable") the only difference allowed.
/// If #236 or a conversion change makes the FIELD accepted, the property must follow it, and this case still holds; a property judged by anything else fails it.</para>
///
/// <para>Each program goes through the analyzer and through <c>BasicCompiler.CompileProjectFiles</c> (the IDE build's entry point, which also reaches the IR builder: the non-constant refusal is an IR-build error,
/// not an analyzer one). The control: legal initializers (an instance, a ReadOnly, a Shared and an Overridable one) are accepted by both.</para>
///
/// <para><b>Mutants</b> (each built for real from a plain source copy of the fix with ONE change; M1-M3 and M6 are in the execution fixture's header):
///   * M4 the analyzer analyzes a property initializer but never judges it (no conversion, no BC30439)  -> this case (P11 reports nothing where vbc reports BC30439), and `AutoPropertyInitializerExecutionTests`'
///                                                                                                         case 1 (the Decimal property of P02 is no longer retyped).
///   * M5 BC36714 never reported on a Get/Set property (the initializer is accepted and dropped)           -> this case (P13) and nothing else: the execution fixture never writes one. Measured: it was the ONLY red case.
/// </para>
/// </summary>
[TestFixture]
public class AutoPropertyInitializerDiagnosticsTests
{
    private const string Nl = "\n";

    /// <summary>A program vbc REFUSES: one error, its code (or null), vbc's text prefix, and the source line vbc reports.</summary>
    private sealed record VbcRefusal(string Id, string Source, string? Code, string Text, int Line)
    {
        public override string ToString() => Id;
    }

    /// <summary>A program vbc ACCEPTS, beside the same program as a field: the two must be judged alike.</summary>
    private sealed record Parity(string Id, string PropertySource, string FieldSource)
    {
        public override string ToString() => Id;
    }

    private static readonly VbcRefusal P13_getset = new("P13_getset", """
        Class C
            Private _p As Integer
            Public Property P As Integer = 5
                Get
                    Return _p
                End Get
                Set(value As Integer)
                    _p = value
                End Set
            End Property
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
        End Sub
        """, "BC36714", "Expanded Properties cannot be initialized", 3);

    private static readonly VbcRefusal P15_iface = new("P15_iface", """
        Interface I
            Property P As Integer = 5
        End Interface
        Class C
            Implements I
            Public Property P As Integer
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
        End Sub
        """, "BC36714", "Expanded Properties cannot be initialized", 2);

    private static readonly VbcRefusal P11_bc30439 = new("P11_bc30439", """
        Class C
            Public Property B As Byte = 300
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.B)
        End Sub
        """, null, "Constant expression not representable in type 'Byte'", 2);

    /// <summary>The same as <see cref="P11_bc30439"/> declared as a field: BasicLang's refusal of the property is the field's.</summary>
    private static readonly Parity P11_parity = new("P11_bc30439", P11_bc30439.Source, """
        Class C
            Public B As Byte = 300
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.B)
        End Sub
        """);

    private static readonly Parity P14_narrow = new("P14_narrow", """
        Class C
            Public Property I As Integer = "x"
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.I)
        End Sub
        """, """
        Class C
            Public I As Integer = "x"
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.I)
        End Sub
        """);

    private static readonly Parity P05_nonconst = new("P05_nonconst", """
        Class C
            Public Property P As Integer = Twice(3)
        End Class
        Function Twice(x As Integer) As Integer
            Return x * 2
        End Function
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
        End Sub
        """, """
        Class C
            Public P As Integer = Twice(3)
        End Class
        Function Twice(x As Integer) As Integer
            Return x * 2
        End Function
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
        End Sub
        """);

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a refusal) and analyze: every ERROR, in the analyzer's order.</summary>
    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join(Nl, parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).ToList();
    }

    /// <summary>The same program through <c>CompileProjectFiles</c> (the IDE build's entry point, which also builds the IR): every error it reports.</summary>
    private static List<SemanticError> Project(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t210-proj-" + Guid.NewGuid().ToString("N"));
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

    private static string Describe(IEnumerable<SemanticError> errors)
        => "[" + string.Join(" | ", errors.Select(e => $"line {e.Line}: {e.Message.Split('\n')[0]}")) + "]";

    private static void AssertRefused(string entry, VbcRefusal probe, List<SemanticError> errors)
    {
        var where = $"{probe.Id} through {entry}";
        Assert.That(errors, Has.Count.EqualTo(1), $"{where}: vbc reports ONE error, got {Describe(errors)}");
        var error = errors[0];
        if (probe.Code != null) Assert.That(error.ErrorCode, Is.EqualTo(probe.Code), $"{where}: the error's code");
        Assert.That(error.Message, Does.Contain(probe.Text), $"{where}: vbc's text");
        Assert.That(error.Line, Is.EqualTo(probe.Line), $"{where}: vbc reports it at source line {probe.Line}");
    }

    /// <summary>The declaration-kind word is the only thing a property's message and its field's may differ in.</summary>
    private static List<string> Normalized(IEnumerable<SemanticError> errors)
        => errors.Select(e => $"line {e.Line}: " + Regex.Replace(e.Message, @"\b(property|field|variable)\b", "<decl>")).ToList();

    private static void AssertJudgedLikeTheField(string entry, Parity probe, Func<string, List<SemanticError>> run)
    {
        var asProperty = run(probe.PropertySource);
        var asField = run(probe.FieldSource);
        Assert.That(Normalized(asProperty), Is.EqualTo(Normalized(asField)),
            $"{probe.Id} through {entry}: the property was judged {Describe(asProperty)}, the same program with a field {Describe(asField)}");
    }

    /// <summary>
    /// The refusals #210 adds and the judgement it shares with fields, in one case (see the header for which are vbc's answer and which are parity only): BC36714 on a Get/Set property (P13) and on an interface
    /// property (P15); BC30439 on `Property B As Byte = 300` (P11), vbc's own refusal; the conversion refusal (P14) and the non-constant refusal (P05)
    /// the property gets exactly as the field written in its place does. The control: a plain, a ReadOnly, a Shared and an Overridable initializer are accepted. Through the analyzer and CompileProjectFiles.
    /// </summary>
    [Test]
    public void APropertyInitializer_IsRefusedAtVbcsSites_AndJudgedLikeTheFieldWrittenInItsPlace()
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in new[] { P13_getset, P15_iface, P11_bc30439 })
            {
                AssertRefused("the analyzer", probe, Analyze(probe.Source));
                AssertRefused("CompileProjectFiles", probe, Project(probe.Source));
            }

            foreach (var probe in new[] { P11_parity, P14_narrow, P05_nonconst })
            {
                AssertJudgedLikeTheField("the analyzer", probe, Analyze);
                AssertJudgedLikeTheField("CompileProjectFiles", probe, Project);
            }

            foreach (var legal in new[] { AutoPropertyInitProbes.P01, AutoPropertyInitProbes.P03, AutoPropertyInitProbes.P04, AutoPropertyInitProbes.P07 })
            {
                Assert.That(Analyze(legal.Source), Is.Empty, $"{legal.Id}: the analyzer refused a legal auto-property initializer");
                Assert.That(Project(legal.Source), Is.Empty, $"{legal.Id}: CompileProjectFiles refused a legal auto-property initializer");
            }
        });
    }
}
