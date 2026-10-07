using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using VisualGameStudio.Tests.Blnet;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #222, the front end. A write to a .NET ReadOnly property is vbc's BC30526 ("Property 'Count' is 'ReadOnly'.") on EVERY backend. It was accepted: a .NET member binds no BasicLang symbol, so
//  #178's check (a user property) and #220's (the built-in Exception's three members) never saw `s.Length = 3`, `l.Count = 5`, `t.Year = 1`, `a.Message = "x"` on an ArgumentException or
//  `Environment.ProcessorCount = 1`, and each backend then failed in its own way: C# CS0200, MSIL MissingFieldException, C++ did not compile, JavaScript printed the OLD value with no error.
//
//  THE DECISION is one method, `SemanticAnalyzer.ReadOnlyNetPropertyName`, asked by the write check (`CheckPropertyWrite`) and by the ByRef check (`IsCopyOutPropertyArgument`). It answers from a recorded
//  settability FACT, never from the member's spelling, and there are FOUR sources of fact. Each has its own row here, so each of the mutants below has its own killer:
//    1. `BclReadOnlyProperties`        String.Length, the Count of List / Dictionary / HashSet, Dictionary.Keys / Values / Comparer, an array's members     -> `AStringsLength_...`, `ACollectionsCount_...`
//    2. `NativeBclSurface` rows        DateTime, TimeSpan and DateTimeOffset carry `readOnly: true` (33 rows)                                           -> `ADateTimeAndTimeSpanProperty_...`
//    3. a .NET exception CLASS         ArgumentException & co inherit the built-in Exception's Message / StackTrace / InnerException that #220 marked     -> `ADotNetExceptionClassesInheritedMember_...`
//    4. resolver metadata              `NetMemberDescriptor.IsGetOnly` = no setter of ANY accessibility (not IsSettable: that is also false for an init / non-public setter and a read-only field) -> `AMetadataGetOnlyProperty_...`
//  The native C++ backend keeps its own BL6017 ("no setter export to generate") beside it: `NativeCpp_...` asserts BC30526 is present and says nothing of BL6017, which
//  `NetShimPipelineTests.WriteToAReadOnlyNetProperty_IsRefusedWithAPositionedBl6017` already pins.
//
//  ORACLE: vbc (the .NET SDK's own, via `S/t136/tools/vbv2.py`). Every refused source below was wrapped in a VB Module and compiled with vbc by the test-writer: each says `error BC30526: Property '<name>' is 'ReadOnly'.`
//  with the member as .NET spells it, and the controls (`Controls` below) compile AND run (`2113m20205 | True | /p | 1`; the user-class-named-List program prints 5). The expected line of each refusal is read off
//  the program text (`LineOf`), never off the analyzer.
//
//  ENTRY POINTS: this fixture is the fast, in-process half (parse + analyze, plus the LSP). `NetReadOnlyPropertyExecutionTests` (Integration) takes one program per source through the spawned CLI on all four
//  targets, the CLI with `--optimize`, `BasicCompiler.CompileProjectFiles` with .NET resolution armed and a Release .blproj build.
//  ⚠ The resolver rows (4 and the native one) arm the analyzer with the shared test resolver (`NetStubHarness.SharedResolver`, as `NetShimPipelineTests` does). `new SemanticAnalyzer()` alone has NONE, which is
//  also the LSP's and a WinForms project's configuration: there a metadata member stays permissive, as spec §6.3 says of anything unresolved.
//
//  MUTANTS (each the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; the cases that go red, measured):
//    M1 no String row (`["String"] = Names("Length")` removed) ......... `AStringsLength_PlainAndCompoundWrite_AreRefused`, `Lsp_AStringLengthWrite_…`, and in the siblings the string row of `EveryFactSource_…`,
//       `AStringsLength_PassedByRef_…` (the ByRef copy-in is lost with it) and `PropertyAccessExecutionTests.N1_…`.
//    M2 `Year` not marked `readOnly: true` .................................. `ADateTimeAndTimeSpanProperty_IsRefused` (the DateTime row only) and the datetime row of `EveryFactSource_…`.
//    M3 no exception inheritance (the `IsNetException` arm skipped) ........... `ADotNetExceptionClassesInheritedMember_IsRefused` (all three rows), the exception row of `EveryFactSource_…` and
//       `PropertyAccessExecutionTests.ArgumentExceptionMessageWrite_IsRefusedWithBC30526_Task222`.
//    M4 `IsGetOnly` forced false (`isGetOnly: false` in `NetTypeResolver`) .... `AMetadataGetOnlyProperty_IsRefused_…`, `NativeCpp_AMetadataWrite_…` and both metadata rows of `EveryFactSource_…`.
//    M5 the ByRef copy-in removed (`IsCopyOutPropertyArgument` answers false for a .NET member) ... no fast case: the two ByRef rows of `NetReadOnlyPropertyExecutionTests` only (C# CS0206, C++ clang error,
//       MSIL refuses the argument). A write is refused the same with or without it.
//    The unmutated plain copy, built the same way: all 35 cases pass. No other case of the 35 (this fixture, its sibling and `PropertyAccessExecutionTests`) went red under any mutant.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect):
//    * `With l : .Count = 5 : End With` is refused, but with the WRONG message ("Type 'List' does not have a member 'Count'", which a READ of `.Count` gets too). Pre-existing; the fix leaves the form alone rather than
//      report a second, contradicting error.
//    * A member whose settability is unknown stays permissive (spec §6.3): an unresolved or Object-degraded receiver (a WinForms member), a BasicLang type, any receiver with no resolver armed (the LSP), and a
//      member the hand-built tables have no row for (`DateTimeOffset.Year` is accepted: the surface lists only Now, UtcNow, DateTime, UtcDateTime, LocalDateTime, Offset and Ticks, and the claim predicate keeps
//      DateTimeOffset away from the resolver). vbc refuses all of them.
//    * `Change(l.Capacity)` and `Change(a.Message)` (a settable or inherited .NET property passed ByRef) are refused, "cannot convert from 'Object' to 'Integer'" / "... 'String'": a resolver-bound or inherited .NET
//      property types as Object there. Same before and after #222; and a ByRef of the settable `sb.Length` is still CS0206 on C# (a native-BCL-surface member has no property symbol).
//    * The C++ .blproj build prints the code twice ("BC30526: BC30526: Property ..."): #223, pinned in `PropertyAccessExecutionTests`.
// ================================================================================================

/// <summary>#222 — a write to a .NET ReadOnly property is BC30526, from every source of the fact. Fast subset: parse + analyze + the LSP, no process.</summary>
[TestFixture]
public class NetReadOnlyPropertyDiagnosticsTests
{
    /// <summary>How .NET resolution is armed: not at all (a bare analyzer, the LSP), as the CLI arms it for a managed target, or as it is for the native C++ backend.</summary>
    private enum Net { Off, Managed, Native }

    private static SemanticAnalyzer Analyze(string source, Net net = Net.Off)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        if (net != Net.Off) analyzer.ConfigureNetResolution(() => NetStubHarness.SharedResolver.Value, nativeBackend: net == Net.Native);
        analyzer.Analyze(ast);
        return analyzer;
    }

    private static string Said(IEnumerable<SemanticError> errors) => string.Join(" | ", errors.Select(e => $"{e.ErrorCode}@{e.Line}:{e.Message}"));

    /// <summary>One statement under test, in a program whose only other line is its declaration.</summary>
    private static string Program(string declaration, string statement) => "Sub Main()\n    " + declaration + "\n    " + statement + "\nEnd Sub\n";

    private static int LineOf(string source, string text) => source.Split('\n').ToList().FindIndex(l => l.Trim() == text) + 1;

    /// <summary>
    /// Each row is refused with EXACTLY one error, BC30526, vbc's text, on the statement's line. "Exactly one" is the point of the second half: the write is reported once (the native path also has BL6017, which is
    /// not in <c>Errors</c>), and nothing else is said about the program.
    /// </summary>
    private static void AssertRefused(Net net, params (string Declaration, string Statement, string Property)[] rows)
    {
        Assert.Multiple(() =>
        {
            foreach (var (declaration, statement, property) in rows)
            {
                var source = Program(declaration, statement);
                var errors = Analyze(source, net).Errors.ToList();
                Assert.That(errors, Has.Count.EqualTo(1), $"`{statement}`: exactly one error expected; got: {Said(errors)}");
                if (errors.Count != 1) continue;
                Assert.That(errors[0].ErrorCode, Is.EqualTo("BC30526"), $"`{statement}`: {Said(errors)}");
                Assert.That(errors[0].Message, Does.Contain($"Property '{property}' is 'ReadOnly'."), $"`{statement}`: vbc's message names the member as .NET spells it");
                Assert.That(errors[0].Line, Is.EqualTo(LineOf(source, statement)), $"`{statement}`: the diagnostic lands on the statement's line");
            }
        });
    }

    // ============================================================================================
    //  SOURCE 1 — BclReadOnlyProperties (String, the collections, arrays)
    // ============================================================================================

    /// <summary>String.Length, plain and compound. A compound write (`+=`) reads then writes: it must be refused once, like the plain one. Mutant "no String row": both go red, and so does the ByRef `Change(s.Length)` row.</summary>
    [Test]
    public void AStringsLength_PlainAndCompoundWrite_AreRefused()
        => AssertRefused(Net.Off,
            ("Dim s As String = \"ab\"", "s.Length = 3", "Length"),
            ("Dim s As String = \"ab\"", "s.Length += 1", "Length"));

    /// <summary>The Count of a List, a Dictionary and a HashSet, a Dictionary's Keys, and an array's Length (the array is named after its ELEMENT type, so it is judged by kind, not by name).</summary>
    [Test]
    public void ACollectionsCount_AndAnArraysLength_AreRefused()
        => AssertRefused(Net.Off,
            ("Dim l As New List(Of Integer)", "l.Count = 5", "Count"),
            ("Dim d As New Dictionary(Of String, Integer)", "d.Count = 5", "Count"),
            ("Dim h As New HashSet(Of Integer)", "h.Count = 5", "Count"),
            ("Dim d As New Dictionary(Of String, Integer)", "d.Keys = Nothing", "Keys"),
            ("Dim a(2) As Integer", "a.Length = 9", "Length"));

    // ============================================================================================
    //  SOURCE 2 — the NativeBclSurface rows (`readOnly: true`)
    // ============================================================================================

    /// <summary>DateTime.Year, TimeSpan.Seconds, the static DateTime.Now and DateTimeOffset.Ticks: a surface row marked ReadOnly, each from its own type's block. Mutant "Year not marked": the DateTime row only.</summary>
    [Test]
    public void ADateTimeAndTimeSpanProperty_IsRefused()
        => AssertRefused(Net.Off,
            ("Dim t As DateTime = New DateTime(2020, 5, 6)", "t.Year = 1", "Year"),
            ("Dim ts As TimeSpan = TimeSpan.FromSeconds(5)", "ts.Seconds = 9", "Seconds"),
            ("Dim x As Integer = 0", "DateTime.Now = New DateTime(2020, 1, 1)", "Now"),
            ("Dim o As DateTimeOffset = DateTimeOffset.UtcNow", "o.Ticks = 1", "Ticks"));

    // ============================================================================================
    //  SOURCE 3 — a .NET exception class inherits the built-in Exception's ReadOnly members
    // ============================================================================================

    /// <summary>
    /// `ArgumentException` and `InvalidOperationException` are not the built-in `Exception` symbol #220 marked, so they bind nothing: each inherits its three ReadOnly members from it. A different name from
    /// each of two classes, and a second member, so the answer is not one spelling. Mutant "no exception inheritance": all three go red.
    /// </summary>
    [Test]
    public void ADotNetExceptionClassesInheritedMember_IsRefused()
        => AssertRefused(Net.Off,
            ("Dim a As New ArgumentException(\"m\")", "a.Message = \"x\"", "Message"),
            ("Dim e As InvalidOperationException = New InvalidOperationException(\"m\")", "e.Message = \"x\"", "Message"),
            ("Dim a As New ArgumentException(\"m\")", "a.InnerException = Nothing", "InnerException"));

    // ============================================================================================
    //  SOURCE 4 — resolver metadata (`NetMemberDescriptor.IsGetOnly`)
    // ============================================================================================

    /// <summary>
    /// A static (`Environment.ProcessorCount`) and an instance (`Uri.AbsolutePath`) property the .NET resolver bound, so the fact is the descriptor's. Neither is in any hand-built table. Mutant "IsGetOnly forced
    /// false": both go red. Without a resolver the same lines are NOT refused (the known gap in the header), so this row arms one.
    /// </summary>
    [Test]
    public void AMetadataGetOnlyProperty_IsRefused_WhenTheResolverBoundIt()
        => AssertRefused(Net.Managed,
            ("Dim x As Integer = 0", "Environment.ProcessorCount = 1", "ProcessorCount"),
            ("Dim u As New Uri(\"http://h/p\")", "u.AbsolutePath = \"/q\"", "AbsolutePath"));

    /// <summary>
    /// The native C++ backend: the same metadata write is BC30526 there too. It is a language rule, so it must not depend on the target. The native path ALSO says BL6017 (its own lowering fact; a non-public or
    /// `init` setter and a read-only field get only that one), which is not asserted here either way.
    /// </summary>
    [Test]
    public void NativeCpp_AMetadataWrite_IsBc30526_Too()
        => AssertRefused(Net.Native,
            ("Dim x As Integer = 0", "Environment.ProcessorCount = 1", "ProcessorCount"),
            ("Dim u As New Uri(\"http://h/p\")", "u.AbsolutePath = \"/q\"", "AbsolutePath"));

    // ============================================================================================
    //  THE CONTROLS — compile with no error at all (and run under vbc)
    // ============================================================================================

    /// <summary>
    /// The shapes that must NOT be refused, in two configurations (a bare analyzer, and with the resolver armed as the CLI arms it): READS of every member above; a SETTABLE .NET property (`sb.Capacity`, `sb.Length`,
    /// `l.Capacity`, `UriBuilder.Port`) and an inherited one (`e.HelpLink`); `DateTime.MinValue`, a .NET FIELD read; a user ReadWrite property, including the ones named `Length` and `Count`; element writes on a plain
    /// array, a MULTI-DIMENSIONAL array, an array of a USER type (`fs(0).P = 1`, an array is named after its element type) and a List / Dictionary; and a user class that takes the name of a .NET type
    /// (`Class List` with a writable `Count`: the answer is never the spelling).
    /// </summary>
    [Test]
    public void Controls_ReadsSettableMembersAndUserTypes_CompileClean()
    {
        const string controls = """
            Class Foo
                Public Property P As Integer
            End Class

            Class Box
                Public Property W As Integer
                Public Property Length As Integer
                Public Property Count As Integer
            End Class

            Sub Main()
                Dim s As String = "ab"
                Dim l As New List(Of Integer)
                l.Add(7)
                Dim d As New Dictionary(Of String, Integer)
                d("a") = 1
                Dim a(2) As Integer
                Dim m(2, 2) As Integer
                Dim e As New ArgumentException("m")
                Dim t As DateTime = New DateTime(2020, 5, 6)
                Dim ts As TimeSpan = TimeSpan.FromSeconds(5)
                Dim sb As New System.Text.StringBuilder()
                Dim u As New Uri("http://h/p")
                Dim ub As New UriBuilder("http://h/p")
                Console.WriteLine(s.Length & l.Count & d.Count & a.Length & e.Message & t.Year & ts.Seconds)
                Console.WriteLine(Environment.ProcessorCount > 0)
                Console.WriteLine(u.AbsolutePath)
                Console.WriteLine(DateTime.MinValue.Year)
                sb.Capacity = 40
                sb.Length = 1
                l.Capacity = 20
                ub.Port = 81
                e.HelpLink = "x"
                a(0) = 3
                m(1, 1) = 5
                l(0) = 1
                d("a") = 2
                Dim fs(2) As Foo
                fs(0) = New Foo()
                fs(0).P = 1
                Dim b As New Box()
                b.W = 5
                b.Length = 6
                b.Count = 7
            End Sub
            """;

        const string shadowing = """
            Class List
                Public Property Count As Integer
            End Class

            Sub Main()
                Dim x As New List()
                x.Count = 5
                Console.WriteLine(x.Count)
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var net in new[] { Net.Off, Net.Managed })
            {
                Assert.That(Analyze(controls, net).Errors, Is.Empty, $"controls, resolver {net}: " + Said(Analyze(controls, net).Errors));
                Assert.That(Analyze(shadowing, net).Errors, Is.Empty, $"a user class named List, resolver {net}: " + Said(Analyze(shadowing, net).Errors));
            }
        });
    }

    // ============================================================================================
    //  THE EDITOR
    // ============================================================================================

    /// <summary>
    /// The editor sees it too: <c>DocumentManager</c> (no resolver: the table facts apply, the metadata ones do not) surfaces `s.Length = 3` as an ERROR carrying vbc's number on the use line, with a real column.
    /// </summary>
    [Test]
    public void Lsp_AStringLengthWrite_SurfacesAsAnErrorOnTheUseLine()
    {
        var source = Program("Dim s As String = \"ab\"", "s.Length = 3");
        var state = new DocumentManager().UpdateDocument(DocumentUri.From("untitled:NetReadOnlyWrite.bas"), source);

        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC30526"));
        Assert.That(match, Is.Not.Null, "expected a BC30526 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("Property 'Length' is 'ReadOnly'."));
        Assert.That(match.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(match.Line, Is.EqualTo(LineOf(source, "s.Length = 3")), "the use line, not (0,0)");
        Assert.That(match.Column, Is.GreaterThan(0), "a real column, not a placeholder");
    }
}
