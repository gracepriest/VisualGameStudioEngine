using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #230 — the diagnostics VB reports for a Structure that declares members, reported by the FRONT END before any backend runs, so every backend refuses alike:
/// BC36638 (a lambda in a Structure member uses <c>Me</c> or an instance member), BC30629 (a parameterless instance <c>Sub New</c>), BC31049 (an instance field initializer), BC36713 (an instance
/// auto-property initializer), BC30435 (a Protected member) and BC30269 (two <c>Shared Sub New</c>).
///
/// <para>Fast subset: nothing runs and no process is spawned. Each program goes through TWO entry points, the <c>Parser</c> + <c>SemanticAnalyzer</c> pair and
/// <c>BasicCompiler.CompileProjectFiles</c> (the IDE's own build path, which stops at the combined IR): the CODE must be the only code reported, on the line the declaration or the use is on. The
/// legal half of each contract is asserted beside it (a Shared initializer, an all-Optional constructor, a lambda over a local), so a check that fires on everything cannot pass.
/// <c>StructureMembersExecutionTests</c> (Integration) RUNS the legal programs against vbc.</para>
///
/// <para>ORACLE: every program below was compiled with vbc (wrapped in a VB Module, <c>S/t136/tools/vbv2.py</c>) and reports exactly the code asserted for it; each legal control RUNS under vbc. Sources are
/// the implementer's <c>S/t230/probes</c> (s1, s2, s3, s6, p12, p17, p23, p31) and the test-writer's <c>S/t230/tw/diag</c> and <c>diag-ctl</c>.</para>
///
/// <para>⛔ <b>M1</b> (BC36638 never reported) is killed by <see cref="ALambdaUsingMeOrAnInstanceMember_IsBC36638"/> alone: ClosureLowering's own ADR-0010 D6 refusal is MSIL-only, so on C# the program is
/// CS1673 and on C++ it RUNS and prints the copy's answer (7 where VB refuses it). Nothing else in the suite notices, because a refused program runs nowhere.</para>
///
/// <para>Not tested, a known divergence: a Protected <c>Sub</c> or <c>Function</c> in a Structure is BC31067 in vbc, and BasicLang reports BC30435 for it (BC30435 is vbc's own code for a field and a
/// property, which are pinned). Both refuse; only the CODE differs for a method, so the method row asserts the refusal and the word "Protected", not the code.</para>
/// </summary>
[TestFixture]
public class StructureMembersDiagnosticsTests
{
    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    private static List<SemanticError> ViaProjectEntryPoint(string source)
    {
        var compiler = new BasicCompiler(new CompilerOptions());
        var dir = Path.Combine(Path.GetTempPath(), "bl-t230-proj-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            return compiler.CompileProjectFiles(new List<string> { path }).AllErrors.ToList();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static IEnumerable<(string Entry, List<SemanticError> Errors)> EveryEntryPoint(string source)
    {
        yield return ("Parser + SemanticAnalyzer", Analyze(source));
        yield return ("CompileProjectFiles", ViaProjectEntryPoint(source));
    }

    private static string Describe(IEnumerable<SemanticError> errors) => string.Join(" | ", errors.Select(e => $"{e.ErrorCode}@{e.Line}:{e.Message}"));

    /// <summary>
    /// Through both entry points, the program reports <paramref name="code"/> and NO OTHER code, once for each line in <paramref name="lines"/>, and the message carries VB's own wording
    /// (<paramref name="vbWording"/>, taken from vbc's message for the same program).
    /// </summary>
    private static void AssertRefused(string source, string code, string vbWording, params int[] lines)
    {
        foreach (var (entry, errors) in EveryEntryPoint(source))
        {
            Assert.That(errors.Select(e => e.ErrorCode).Distinct(), Is.EqualTo(new[] { code }), $"{entry}: only {code} is expected; got: {Describe(errors)}");
            Assert.That(errors.Select(e => e.Line).Distinct().OrderBy(l => l), Is.EqualTo(lines.OrderBy(l => l)), $"{entry}: {code} is expected on line(s) {string.Join(",", lines)}; got: {Describe(errors)}");
            Assert.That(errors.All(e => e.Message.Contains(vbWording)), Is.True, $"{entry}: the message must carry VB's wording [{vbWording}]; got: {Describe(errors)}");
        }
    }

    /// <summary>Through both entry points, the program is accepted: no diagnostic at all (the legal half of a contract; vbc runs each of these).</summary>
    private static void AssertAccepted(string source)
    {
        foreach (var (entry, errors) in EveryEntryPoint(source))
            Assert.That(errors, Is.Empty, $"{entry}: vbc runs this program, so no diagnostic is expected; got: {Describe(errors)}");
    }

    // ====================================================================================
    // BC36638 — a lambda in a Structure member that uses Me or an instance member.
    // ====================================================================================

    private const string BC36638Wording = "cannot be used within a lambda expression in structures";

    /// <summary>
    /// S1 a bare INSTANCE FIELD (`Function() V`), S2 an explicit `Me` (`Function() Me.V`), S3 an instance METHOD called bare (`Function() Twice()`), S6 an instance field READ and WRITTEN in a Sub lambda
    /// (vbc reports it twice, once per use: line 4 twice), and a lambda nested in a lambda (`Function() Function() V`): each is BC36638 on the line of the use. The lambda would capture a COPY of the value, so
    /// VB refuses it. The legal half: a lambda over a LOCAL copy of the field, a parameter, a Shared member and its own parameter reports nothing. Before #230 none of these parsed. M1 (never reported) is
    /// killed here and nowhere else in the suite.
    /// </summary>
    [Test]
    public void ALambdaUsingMeOrAnInstanceMember_IsBC36638()
    {
        // S1
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Function Getter() As Func(Of Integer)
                    Return Function() V
                End Function
            End Structure
            Sub Main()
                Dim p As New P()
                p.V = 7
                Dim f As Func(Of Integer) = p.Getter()
                Console.WriteLine(f())
            End Sub
            """, "BC36638", BC36638Wording, 4);

        // S2
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Function Getter() As Func(Of Integer)
                    Return Function() Me.V
                End Function
            End Structure
            Sub Main()
                Dim p As New P()
                p.V = 7
                Dim f As Func(Of Integer) = p.Getter()
                Console.WriteLine(f())
            End Sub
            """, "BC36638", BC36638Wording, 4);

        // S3
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Function Twice() As Integer
                    Return V * 2
                End Function
                Public Function Getter() As Func(Of Integer)
                    Return Function() Twice()
                End Function
            End Structure
            Sub Main()
                Dim p As New P()
                p.V = 7
                Dim f As Func(Of Integer) = p.Getter()
                Console.WriteLine(f())
            End Sub
            """, "BC36638", BC36638Wording, 7);

        // S6: V is read and written inside the lambda, on one line
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Sub Run()
                    Dim a As Action = Sub() V = V + 1
                    a()
                    Console.WriteLine(V)
                End Sub
            End Structure
            Sub Main()
                Dim p As New P()
                p.V = 7
                p.Run()
            End Sub
            """, "BC36638", BC36638Wording, 4);

        // a lambda inside a lambda reaches the Structure's field at any depth
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Function Getter() As Func(Of Func(Of Integer))
                    Return Function() Function() V
                End Function
            End Structure
            Sub Main()
                Dim p As New P()
                Console.WriteLine(p.V)
            End Sub
            """, "BC36638", BC36638Wording, 4);

        // the legal half: a local copy, a parameter, a Shared member and the lambda's own parameter
        AssertAccepted("""
            Structure P
                Public V As Integer
                Public Shared K As Integer
                Public Function Getter(n As Integer) As Func(Of Integer)
                    Dim local As Integer = V
                    Return Function() local + K + n
                End Function
                Public Function Sum() As Integer
                    Dim f As Func(Of Integer, Integer) = Function(x) x * 2
                    Return f(V)
                End Function
            End Structure
            Sub Main()
                Dim s As P
                s.V = 1
                Console.WriteLine(s.Getter(2)() & " " & s.Sum())
            End Sub
            """);
    }

    // ====================================================================================
    // BC30629 — a Structure always has its parameterless constructor.
    // ====================================================================================

    /// <summary>
    /// P12 a non-Shared `Sub New()` with no parameters is BC30629 (the constructor `Dim s As P` and `New P()` already mean is the zeroing one). The legal half is what makes the rule exact: an all-Optional
    /// `Sub New(Optional a As Integer = 9)` is NOT parameterless in VB's sense (it is declared with a parameter), and a `Shared Sub New()` is the type initializer; vbc runs the program that holds both.
    /// </summary>
    [Test]
    public void AParameterlessInstanceSubNew_IsBC30629()
    {
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Sub New()
                    V = 1
                End Sub
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.V)
            End Sub
            """, "BC30629", "Structures cannot declare a non-shared 'Sub New' with no parameters", 3);

        AssertAccepted("""
            Structure P
                Public V As Integer
                Public Shared K As Integer
                Shared Sub New()
                    K = 1
                End Sub
                Public Sub New(Optional a As Integer = 9)
                    V = a
                End Sub
            End Structure
            Sub Main()
                Dim s As New P()
                Console.WriteLine(s.V & " " & P.K)
            End Sub
            """);
    }

    // ====================================================================================
    // BC31049 — an instance field initializer.
    // ====================================================================================

    /// <summary>
    /// P17 an instance field with an initializer in a Structure that has a method is BC31049; so is one in a FIELDS-ONLY Structure, which used to be a parse error ("Expected member name but found
    /// Assignment") and is now the same diagnostic VB gives (a Structure is default-initialized, `Dim s As P` runs no constructor, so the initializer would never run). The legal half: a Shared field
    /// initializer, a `Const` and a Shared Property initializer are accepted, as in vbc.
    /// </summary>
    [Test]
    public void AnInstanceFieldInitializer_IsBC31049()
    {
        AssertRefused("""
            Structure P
                Public V As Integer = 5
                Public Function Get1() As Integer
                    Return V
                End Function
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.Get1())
            End Sub
            """, "BC31049", "Initializers on structure members are valid only for 'Shared' members and constants", 2);

        AssertRefused("""
            Structure P
                Public V As Integer = 5
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.V)
            End Sub
            """, "BC31049", "Initializers on structure members are valid only for 'Shared' members and constants", 2);

        AssertAccepted("""
            Structure P
                Public V As Integer
                Public Shared K As Integer = 30
                Public Const C As Integer = 12
                Public Shared Property L As Integer = 7
                Public Property N As Integer
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.V & " " & P.K & " " & P.C & " " & P.L & " " & s.N)
            End Sub
            """);
    }

    // ====================================================================================
    // BC36713 — an instance auto-property initializer.
    // ====================================================================================

    /// <summary>
    /// P31 `Public Property N As Integer = 5` in a Structure is BC36713, the auto-property twin of BC31049; the Shared one beside it (`Public Shared Property K As Integer = 7`) is legal and is NOT reported,
    /// so exactly one diagnostic lands, on line 2. (The legal Shared property initializer is also run by `StructureMembersExecutionTests`.)
    /// </summary>
    [Test]
    public void AnInstanceAutoPropertyInitializer_IsBC36713()
    {
        AssertRefused("""
            Structure P
                Public Property N As Integer = 5
                Public Shared Property K As Integer = 7
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.N & " " & P.K)
            End Sub
            """, "BC36713", "Auto-implemented Properties contained in Structures cannot have initializers unless they are marked 'Shared'", 2);
    }

    // ====================================================================================
    // BC30435 — a Protected member.
    // ====================================================================================

    /// <summary>
    /// A Protected FIELD (P23) and a Protected PROPERTY are BC30435 ("Members in a Structure cannot be declared 'Protected'"), each on its own line: nothing derives from a Structure. The legal half: Private,
    /// Friend and Public members of every kind are accepted. A Protected FUNCTION is refused too, but vbc's code for a method is BC31067 and BasicLang's is BC30435, so only the refusal and the word
    /// "Protected" are asserted for it (see the fixture header).
    /// </summary>
    [Test]
    public void AProtectedMember_IsBC30435()
    {
        AssertRefused("""
            Structure P
                Protected V As Integer
                Public Function Get1() As Integer
                    Return V
                End Function
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.Get1())
            End Sub
            """, "BC30435", "Members in a Structure cannot be declared 'Protected'", 2);

        AssertRefused("""
            Structure P
                Public V As Integer
                Protected Property W As Integer
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.V)
            End Sub
            """, "BC30435", "Members in a Structure cannot be declared 'Protected'", 3);

        // a Protected Function: refused, code not pinned (vbc: BC31067)
        foreach (var (entry, errors) in EveryEntryPoint("""
            Structure P
                Public V As Integer
                Protected Function Get1() As Integer
                    Return V
                End Function
            End Structure
            Sub Main()
                Dim s As P
                Console.WriteLine(s.V)
            End Sub
            """))
        {
            Assert.That(errors, Is.Not.Empty, $"{entry}: a Protected Function in a Structure must be refused");
            Assert.That(errors.All(e => e.Line == 3 && e.Message.Contains("Protected")), Is.True, $"{entry}: refused on line 3, naming 'Protected'; got: {Describe(errors)}");
        }

        AssertAccepted("""
            Structure P
                Private A As Integer
                Friend B As Integer
                Public C As Integer
                Private Function F() As Integer
                    Return A
                End Function
                Friend Sub G()
                    B = F()
                End Sub
                Friend Property H As Integer
            End Structure
            Sub Main()
                Dim s As P
                s.G()
                Console.WriteLine(s.B)
            End Sub
            """);
    }

    // ====================================================================================
    // BC30269 — two Shared Sub New.
    // ====================================================================================

    /// <summary>
    /// Two `Shared Sub New()` in one Structure are BC30269 on the SECOND one (line 7): a type has one type initializer, and without the check the second was emitted as a second constructor, as it
    /// was for a Class before. The legal half: one `Shared Sub New` per Structure, in two Structures of one file, is accepted (vbc runs it: `1 2`).
    /// </summary>
    [Test]
    public void TwoSharedSubNews_AreBC30269()
    {
        AssertRefused("""
            Structure P
                Public V As Integer
                Public Shared K As Integer
                Shared Sub New()
                    K = 1
                End Sub
                Shared Sub New()
                    K = 2
                End Sub
            End Structure
            Sub Main()
                Console.WriteLine(P.K)
            End Sub
            """, "BC30269", "'Shared Sub New()' has multiple definitions with identical signatures", 7);

        AssertAccepted("""
            Structure P
                Public Shared K As Integer
                Shared Sub New()
                    K = 1
                End Sub
            End Structure
            Structure Q
                Public Shared K As Integer
                Shared Sub New()
                    K = 2
                End Sub
            End Structure
            Sub Main()
                Console.WriteLine(P.K & " " & Q.K)
            End Sub
            """);
    }
}
