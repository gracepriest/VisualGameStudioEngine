using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using Moq;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Diagnostic = BasicLang.Compiler.LSP.Diagnostic;
using DiagnosticSeverity = BasicLang.Compiler.LSP.DiagnosticSeverity;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// O18 / spec §4.12 — the editor sees what the build compiles: the inactive branch of an #If is blanked LINE FOR
/// LINE (so every reported position is the user's), and a web project's files know the DOM declarations.
/// Measured before: the LSP had no preprocessor at all (no "Preprocessor" in BasicLang/LSP/) and no backend.
/// </summary>
[TestFixture]
public class LspConditionalCompilationTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-lspif-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    // Line 6 is a type error that exists only in the DESKTOP branch.
    private const string Source =
        "Module M\n" +                               // 1
        "Sub Main()\n" +                             // 2
        "#If WEB Then\n" +                           // 3
        "    Dim a As Integer = 1\n" +               // 4
        "#Else\n" +                                  // 5
        "    Dim b As Integer = \"not a number\"\n" +// 6
        "#End If\n" +                                // 7
        "End Sub\n" +                                // 8
        "End Module\n";

    private void WriteProject(string backend) =>
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            $"<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>{backend}</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");

    private DocumentState Open(string backend, string source = Source, DocumentManager? manager = null)
    {
        WriteProject(backend);
        var path = Path.Combine(_dir, "Main.bas");
        File.WriteAllText(path, source);
        return (manager ?? new DocumentManager()).UpdateDocument(DocumentUri.FromFileSystemPath(path), source);
    }

    private static List<Diagnostic> Errors(DocumentState state) =>
        state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static string Dump(DocumentState state) =>
        string.Join(" | ", state.Diagnostics.Select(d => $"{d.Severity}({d.Line},{d.Column}): {d.Message}"));

    [Test]
    public void AWebProject_ReportsNothing_FromTheDesktopBranch()
    {
        var state = Open("JavaScript");
        Assert.That(Errors(state), Is.Empty, Dump(state));
    }

    [Test]
    public void ADesktopProject_ReportsTheDesktopBranch_AtItsOwnLine()
    {
        var state = Open("CSharp");
        var errors = Errors(state);
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1), Dump(state));
            Assert.That(errors[0].Line, Is.EqualTo(6), "blanking keeps every line where it was");
            Assert.That(errors[0].Column, Is.EqualTo(ExpectedColumnOnLine6()), "and every column: " + Dump(state));
        });
    }

    /// <summary>The column the analyzer reports for line 6 when there is NO directive around it at all — the
    /// same statement, at the same place in its line.</summary>
    private int ExpectedColumnOnLine6()
    {
        var plain = Source.Split('\n');
        plain[2] = ""; plain[3] = ""; plain[4] = ""; plain[6] = "";
        var sub = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(sub);
        var path = Path.Combine(sub, "Plain.bas");
        var text = string.Join("\n", plain);
        File.WriteAllText(path, text);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), text);
        var only = Errors(state).Single(e => e.Line == 6);
        return only.Column;
    }

    /// <summary>The diagnostics the SERVER publishes (0-based LSP ranges) point at the user's line.</summary>
    [Test]
    public void ThePublishedDiagnostic_IsOnTheUsersLine()
    {
        var state = Open("CSharp");
        PublishDiagnosticsParams? published = null;
        var textDocument = new Mock<ITextDocumentLanguageServer>();
        textDocument.Setup(t => t.SendNotification(It.IsAny<MediatR.IRequest>()))
            .Callback<MediatR.IRequest>(r => published = r as PublishDiagnosticsParams);
        var server = new Mock<ILanguageServerFacade>();
        server.SetupGet(s => s.TextDocument).Returns(textDocument.Object);

        new DiagnosticsService().PublishDiagnostics(server.Object, state);

        Assert.That(published, Is.Not.Null, "nothing was published");
        var errors = published!.Diagnostics
            .Where(d => d.Severity == OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error)
            .ToList();
        Assert.That(errors.Select(e => e.Range.Start.Line), Is.EqualTo(new[] { 5 }), "0-based line 5 = the user's line 6");
    }

    [Test]
    public void TheEditorMode_BlanksDirectivesAndInactiveLines_AndKeepsTheLineCount()
    {
        var pre = new Preprocessor();
        pre.Define("WEB");
        var blanked = pre.ProcessForEditor(Source, "Main.bas").Replace("\r\n", "\n").Split('\n');
        var original = Source.Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(blanked.Length, Is.EqualTo(original.Length));
            Assert.That(blanked[2], Is.Empty, "#If");
            Assert.That(blanked[3], Is.EqualTo(original[3]), "the active branch is kept verbatim");
            Assert.That(blanked[4], Is.Empty, "#Else");
            Assert.That(blanked[5], Is.Empty, "the inactive branch is blank");
            Assert.That(blanked[6], Is.Empty, "#End If");
            Assert.That(pre.InactiveLines, Is.EqualTo(new[] { 6 }), "1-based: only the code line of the dead branch");
        });
    }

    /// <summary>An #Include is never spliced in the editor: splicing adds lines, and every later position would
    /// be off by the included file's length.</summary>
    [Test]
    public void TheEditorMode_NeverSplicesAnInclude()
    {
        File.WriteAllText(Path.Combine(_dir, "Other.bas"), "Sub Helper()\nEnd Sub\n");
        var source = "#Include \"Other.bas\"\nModule M\nEnd Module\n";
        var blanked = new Preprocessor().ProcessForEditor(source, Path.Combine(_dir, "Main.bas"))
            .Replace("\r\n", "\n").Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(blanked.Length, Is.EqualTo(source.Split('\n').Length));
            Assert.That(blanked[0], Is.Empty);
            Assert.That(string.Join("\n", blanked), Does.Not.Contain("Helper"));
        });
    }

    /// <summary>A web project's files see dom-core.bli's Document/Element, as the web build does (K1).</summary>
    [Test]
    public void AWebProject_KnowsTheDomDeclarations()
    {
        const string dom = "Module M\nSub Main()\nDim d As Document = ::document\nDim e As Element = d.getElementById(\"x\")\n" +
                           "e.textContent = \"a\"\nEnd Sub\nEnd Module\n";
        var state = Open("JavaScript", dom);
        Assert.That(Errors(state), Is.Empty, Dump(state));
    }

    /// <summary>The same file on a desktop project does NOT see the DOM (gated on the backend, as the build's
    /// WithJavaScriptDeclarations is): <c>getElementById</c> is unknown, so its result is Object and the
    /// assignment to <c>Element</c> is refused — exactly what the web project said before this task.</summary>
    [Test]
    public void ADesktopProject_DoesNotSeeTheDomDeclarations()
    {
        const string dom = "Module M\nSub Main()\nDim d As Document = ::document\nDim e As Element = d.getElementById(\"x\")\n" +
                           "e.textContent = \"a\"\nEnd Sub\nEnd Module\n";
        var state = Open("CSharp", dom);
        Assert.Multiple(() =>
        {
            Assert.That(Errors(state).Select(e => e.Line), Does.Contain(4), Dump(state));
            Assert.That(state.ProjectContext!.SourceFiles.Select(Path.GetFileName), Does.Not.Contain("dom-core.bli"));
        });
    }

    /// <summary>Inactive branches are dimmed as Visual Studio does: each inactive line is ONE comment token, as long
    /// as the line WITHOUT its carriage return (a CRLF file's token must not run onto the line break).</summary>
    [TestCase("\n")]
    [TestCase("\r\n")]
    public void AnInactiveBranch_IsReportedAsCommentTokens(string newline)
    {
        var manager = new DocumentManager();
        var state = Open("JavaScript", Source.Replace("\n", newline), manager);
        var handler = new SemanticTokensHandler(manager);

        var result = handler.Handle(new SemanticTokensParams { TextDocument = new TextDocumentIdentifier(state.Uri) },
            CancellationToken.None).GetAwaiter().GetResult();

        var tokens = Decode(result!.Data.ToArray());
        const int comment = 15;
        var line6 = Source.Split('\n')[5];
        Assert.Multiple(() =>
        {
            Assert.That(tokens.Where(t => t.Line == 5).ToList(),
                Is.EqualTo(new[] { (Line: 5, Col: 0, Length: line6.Length, Type: comment) }),
                "the dead line is one comment token over the whole line");
            Assert.That(tokens.Where(t => t.Type == comment).Select(t => t.Line), Is.EqualTo(new[] { 5 }),
                "no other line is dimmed — the active branch and the directives are not");
            Assert.That(tokens.Any(t => t.Line == 3), Is.True, "the active branch is still highlighted");
        });
    }

    /// <summary>Completion on a line of an inactive branch offers NOTHING — the build never sees that code —
    /// while the same text on a live line still completes (through the real CompletionHandler).</summary>
    [Test]
    public void CompletionOnAnInactiveLine_OffersNothing()
    {
        const string source =
            "Module M\n" +                              // 0-based 0
            "Sub Main()\n" +                            // 1
            "#If WEB Then\n" +                          // 2
            "    Console.WriteLine(\"live\")\n" +       // 3
            "#Else\n" +                                 // 4
            "    Console.WriteLine(\"dead\")\n" +       // 5
            "#End If\n" +                               // 6
            "End Sub\n" +
            "End Module\n";
        var manager = new DocumentManager();
        var state = Open("JavaScript", source, manager);
        var handler = new CompletionHandler(manager, new CompletionService());
        int AfterTheDot(int line) =>
            handler.Handle(new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier(state.Uri),
                    Position = new Position(line, "    Console.".Length)
                }, CancellationToken.None)
                .GetAwaiter().GetResult().Items.Count();

        Assert.Multiple(() =>
        {
            Assert.That(AfterTheDot(3), Is.GreaterThan(0), "the live line completes Console's members");
            Assert.That(AfterTheDot(5), Is.EqualTo(0), "the dead line offers nothing");
        });
    }

    private static List<(int Line, int Col, int Length, int Type)> Decode(int[] data)
    {
        var list = new List<(int, int, int, int)>();
        int line = 0, col = 0;
        for (var i = 0; i + 4 < data.Length; i += 5)
        {
            if (data[i] != 0) col = 0;
            line += data[i];
            col += data[i + 1];
            list.Add((line, col, data[i + 2], data[i + 3]));
        }
        return list;
    }

    private const string DesktopDebugSource =
        "Module M\n" +                               // 1
        "Sub Main()\n" +                             // 2
        "#If DESKTOP Then\n" +                       // 3
        "    Dim a As Integer = \"desktop\"\n" +     // 4
        "#Else\n" +                                  // 5
        "    Dim b As Integer = \"web\"\n" +         // 6
        "#End If\n" +                                // 7
        "#If DEBUG Then\n" +                         // 8
        "    Dim c As Integer = \"debug\"\n" +       // 9
        "#End If\n" +                                // 10
        "End Sub\n" +                                // 11
        "End Module\n";

    /// <summary>A loose file (no project context at all — an unsaved buffer) is a desktop Debug build.</summary>
    [Test]
    public void ALooseFile_IsPreprocessedAsADesktopDebugBuild()
    {
        var state = new DocumentManager().UpdateDocument(DocumentUri.From("untitled:Untitled-1"), DesktopDebugSource);
        Assert.That(state.ProjectContext, Is.Null, "an untitled buffer has no project");
        Assert.That(Errors(state).Select(e => e.Line), Is.EquivalentTo(new[] { 4, 9 }), Dump(state));
    }

    /// <summary>A file with no .blproj above it (the implicit sibling project) is a desktop Debug build too.</summary>
    [Test]
    public void AFileWithNoProjectFile_IsPreprocessedAsADesktopDebugBuild()
    {
        var path = Path.Combine(_dir, "Loose.bas");
        File.WriteAllText(path, DesktopDebugSource);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), DesktopDebugSource);
        Assert.That(Errors(state).Select(e => e.Line), Is.EquivalentTo(new[] { 4, 9 }), Dump(state));
    }

    /// <summary>The project's Debug configuration's DefineConstants reach the editor, as they reach the Debug build.</summary>
    [Test]
    public void TheProjectsDebugDefineConstants_ReachTheEditor()
    {
        const string source = "Module M\nSub Main()\n#If TRACE Then\n    Dim t As Integer = \"trace\"\n#End If\nEnd Sub\nEnd Module\n";
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>CSharp</TargetBackend>\n" +
            "  </PropertyGroup>\n  <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">\n" +
            "    <DefineConstants>DEBUG;TRACE</DefineConstants>\n  </PropertyGroup>\n" +
            "  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        var path = Path.Combine(_dir, "Main.bas");
        File.WriteAllText(path, source);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), source);
        Assert.That(Errors(state).Select(e => e.Line), Is.EqualTo(new[] { 4 }), Dump(state));
    }

    /// <summary>Retargeting the project re-reads the document through the new symbols, although its text did not
    /// change — the document cache and the parse cache must both key on the symbols.</summary>
    [Test]
    public void RetargetingTheProject_ReblanksAnUnchangedDocument()
    {
        var manager = new DocumentManager();
        var desktop = Open("CSharp", Source, manager);
        Assert.That(Errors(desktop), Has.Count.EqualTo(1), Dump(desktop));

        WriteProject("JavaScript");
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "App.blproj"), DateTime.UtcNow.AddMinutes(1));
        var web = manager.UpdateDocument(DocumentUri.FromFileSystemPath(Path.Combine(_dir, "Main.bas")), Source);
        Assert.That(Errors(web), Is.Empty, Dump(web));
    }

    /// <summary>A SIBLING file's declarations are read through the same symbols (LspProjectContext.ParseCached): the
    /// project symbol table holds the branch the build compiles, never both. Read from the table (the declaring
    /// LINE and return type) rather than through a use site: measured, the LSP types a cross-file module function
    /// returning Integer as Object at the call whichever branch declares it, so a use-site assertion could not
    /// tell the branches apart.</summary>
    [TestCase("JavaScript", 7, "Integer")]
    [TestCase("CSharp", 3, "String")]
    public void ASiblingFile_IsPreprocessedWithTheProjectsSymbols(string backend, int declaredOnLine, string returns)
    {
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            $"<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>{backend}</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n    <Compile Include=\"Helper.bas\" />\n" +
            "  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(_dir, "Helper.bas"),
            "Public Module Helper\n#If DESKTOP Then\nPublic Function Pick() As String\n    Return \"d\"\nEnd Function\n" +
            "#Else\nPublic Function Pick() As Integer\n    Return 1\nEnd Function\n#End If\nEnd Module\n");
        const string main = "Module M\nSub Main()\nDim i As Integer = Pick()\nEnd Sub\nEnd Module\n";
        var path = Path.Combine(_dir, "Main.bas");
        File.WriteAllText(path, main);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), main);
        var (pick, _) = state.ProjectContext!.FindPublicSymbol("Pick");
        Assert.That(pick, Is.Not.Null, "the sibling's Pick is in the project table");
        Assert.That((pick!.Line, pick.ReturnType?.Name), Is.EqualTo((declaredOnLine, returns)));
    }

    /// <summary>A malformed directive fails the build (the compiler reports the preprocessor's errors), so the
    /// editor reports it too, at its line.</summary>
    [Test]
    public void AMalformedDirective_IsReportedAtItsLine()
    {
        const string source = "Module M\nSub Main()\n#Else\nEnd Sub\nEnd Module\n";
        var state = Open("CSharp", source);
        Assert.That(Errors(state).Select(e => (e.Line, e.Message)).ToList(),
            Has.Some.Matches<(int Line, string Message)>(e => e.Line == 3 && e.Message.Contains("#Else")), Dump(state));
    }
}
