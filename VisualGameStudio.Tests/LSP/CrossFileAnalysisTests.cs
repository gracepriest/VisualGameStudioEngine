using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Wave 6: cross-file/project context for the LSP server.
///
/// A document that belongs to a project (nearest .blproj listing it, or the
/// sibling .bas/.mod/.cls files in its directory) is analyzed against a
/// project-wide symbol table, so:
///  - Import of a sibling module and calls into it produce NO spurious
///    diagnostics,
///  - hover/definition/completion see symbols defined in sibling files,
///  - a genuinely unresolvable Import still produces a diagnostic,
///  - editing one file invalidates the cached project analysis of its siblings.
/// </summary>
[TestFixture]
public class CrossFileAnalysisTests
{
    private DocumentManager _documentManager = null!;
    private SymbolService _symbolService = null!;
    private string _rootDir = null!;

    private const string MainSource = @"Import MathUtils

Sub Main()
    Dim total As Integer = addNums(1, 2)
    Console.WriteLine(total)
End Sub
";

    private const string MathUtilsSource = @"Public Function addNums(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
";

    private const string ProjectXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>CrossFileTest</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
    <Compile Include=""MathUtils.bas"" />
  </ItemGroup>
</BasicLangProject>
";

    [SetUp]
    public void SetUp()
    {
        _documentManager = new DocumentManager();
        _symbolService = new SymbolService();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-crossfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_rootDir))
                Directory.Delete(_rootDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_rootDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private DocumentState OpenDocument(string path, string? content = null)
    {
        var uri = DocumentUri.FromFileSystemPath(path);
        return _documentManager.UpdateDocument(uri, content ?? File.ReadAllText(path));
    }

    private static IEnumerable<Diagnostic> Errors(DocumentState state) =>
        state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    private static string DumpDiagnostics(DocumentState state) =>
        string.Join("; ", state.Diagnostics.Select(d => $"{d.Severity}({d.Line},{d.Column}): {d.Message}"));

    // ------------------------------------------------------------------
    // Diagnostics: cross-file references must not be spurious errors
    // ------------------------------------------------------------------

    [Test]
    public void Diagnostics_ImportAndCallSiblingFunction_WithBlproj_NoErrors()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", MainSource);

        var state = OpenDocument(mainPath);

        Assert.That(state.ProjectContext, Is.Not.Null, "expected a project context from the .blproj");
        Assert.That(state.ProjectContext!.ProjectFilePath, Does.EndWith("CrossFileTest.blproj"));
        Assert.That(Errors(state), Is.Empty, "spurious diagnostics: " + DumpDiagnostics(state));
    }

    [Test]
    public void Diagnostics_ImportAndCallSiblingFunction_SiblingFallbackWithoutBlproj_NoErrors()
    {
        // No .blproj — the project is the sibling files in the directory
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", MainSource);

        var state = OpenDocument(mainPath);

        Assert.That(state.ProjectContext, Is.Not.Null, "expected an implicit sibling-directory project context");
        Assert.That(state.ProjectContext!.ProjectFilePath, Is.Null, "implicit projects have no .blproj");
        Assert.That(Errors(state), Is.Empty, "spurious diagnostics: " + DumpDiagnostics(state));
    }

    [Test]
    public void Diagnostics_QualifiedSiblingAccess_WithoutImport_NoErrors()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", @"Sub Main()
    Dim total As Integer = MathUtils.addNums(1, 2)
    Console.WriteLine(total)
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state), Is.Empty, "spurious diagnostics: " + DumpDiagnostics(state));
    }

    [Test]
    public void Diagnostics_WithoutProjectContext_CrossFileCallStillErrors()
    {
        // Control: a virtual (non-file) document gets no project context, so the
        // cross-file call is (correctly) reported — proving the assertions above
        // actually exercise the project context.
        var uri = DocumentUri.From("untitled:Main.bas");
        var state = _documentManager.UpdateDocument(uri, MainSource);

        Assert.That(state.ProjectContext, Is.Null);
        Assert.That(Errors(state).Any(d => d.Message.Contains("addNums")), Is.True,
            "expected an unresolved-identifier error without project context, got: " + DumpDiagnostics(state));
    }

    // ------------------------------------------------------------------
    // Unresolvable imports must still be reported
    // ------------------------------------------------------------------

    [Test]
    public void Diagnostics_UnresolvableImport_LoneFile_IsReported()
    {
        var mainPath = WriteFile("Main.bas", @"Import DoesNotExist

Sub Main()
    Console.WriteLine(""hi"")
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(state.ProjectContext, Is.Not.Null, "a lone on-disk file still gets a (single-file) context");
        Assert.That(Errors(state).Any(d => d.Message.Contains("Cannot resolve import 'DoesNotExist'")), Is.True,
            "expected unresolvable-import diagnostic, got: " + DumpDiagnostics(state));
    }

    [Test]
    public void Diagnostics_UnresolvableImport_InsideProject_IsReported()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", @"Import MathUtils
Import NoSuchModule

Sub Main()
    Dim total As Integer = addNums(1, 2)
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state).Any(d => d.Message.Contains("Cannot resolve import 'NoSuchModule'")), Is.True,
            "expected unresolvable-import diagnostic, got: " + DumpDiagnostics(state));
        Assert.That(Errors(state).Any(d => d.Message.Contains("MathUtils")), Is.False,
            "the resolvable import must not be reported: " + DumpDiagnostics(state));
    }

    // ------------------------------------------------------------------
    // Hover: symbols defined in sibling project files
    // ------------------------------------------------------------------

    [Test]
    public void Hover_CrossFileFunction_ReturnsSignatureAndDefiningFile()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", MainSource);

        var state = OpenDocument(mainPath);
        var hover = _symbolService.GetHoverInfo(state, "Add");

        Assert.That(hover, Is.Not.Null, "hover over cross-file symbol returned null");
        Assert.That(hover, Does.Contain("Function Add(a As Integer, b As Integer) As Integer"));
        Assert.That(hover, Does.Contain("MathUtils.bas"));
    }

    [Test]
    public void Hover_CrossFileSymbolInsideModuleBlock_IsFound()
    {
        // Wave 4 lesson: everything is usually inside a Module block — the
        // symbol collector must recurse into ModuleNode.
        WriteFile("Helpers.bas", @"Module Helpers
    Public Function Triple(x As Integer) As Integer
        Return x * 3
    End Function
End Module
");
        var mainPath = WriteFile("Main.bas", @"Import Helpers

Sub Main()
    Console.WriteLine(Triple(5))
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state), Is.Empty, "spurious diagnostics: " + DumpDiagnostics(state));

        var hover = _symbolService.GetHoverInfo(state, "Triple");
        Assert.That(hover, Is.Not.Null, "hover over module-nested cross-file symbol returned null");
        Assert.That(hover, Does.Contain("Function Triple(x As Integer) As Integer"));
        Assert.That(hover, Does.Contain("Helpers.bas"));
    }

    [Test]
    public void Hover_ModuleName_ShowsModuleAndFile()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", MainSource);

        var state = OpenDocument(mainPath);
        var hover = _symbolService.GetHoverInfo(state, "MathUtils");

        Assert.That(hover, Is.Not.Null);
        Assert.That(hover, Does.Contain("Module MathUtils"));
        Assert.That(hover, Does.Contain("MathUtils.bas"));
    }

    // ------------------------------------------------------------------
    // Definition: cross-file
    // ------------------------------------------------------------------

    [Test]
    public void Definition_CrossFileFunction_PointsAtSiblingFile()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", MainSource);

        var state = OpenDocument(mainPath);
        var location = _symbolService.FindDefinition(state, "Add");

        Assert.That(location, Is.Not.Null, "cross-file definition returned null");
        Assert.That(location!.Uri.ToString(), Does.EndWith("MathUtils.bas"));
        Assert.That(location.Range.Start.Line, Is.EqualTo(4), "Add is declared on 1-based line 5");
    }

    // ------------------------------------------------------------------
    // Completion: cross-file symbols
    // ------------------------------------------------------------------

    [Test]
    public void Completion_IncludesCrossFileSymbols()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", @"Import MathUtils

Sub Main()

End Sub
");

        var state = OpenDocument(mainPath);
        var completionService = new CompletionService();
        // Cursor on the blank line inside Sub Main (0-based line 3)
        var completions = completionService.GetCompletions(state, 3, 4);

        Assert.That(completions.Any(c => c.Label == "addNums"), Is.True,
            "cross-file function missing from completions");
        Assert.That(completions.Any(c => c.Label == "Add"), Is.True,
            "cross-file function missing from completions");
    }

    // ------------------------------------------------------------------
    // Invalidation: editing one file re-analyzes its project siblings
    // ------------------------------------------------------------------

    [Test]
    public void DidChange_SiblingGainsFunction_MainReAnalyzedWithoutStaleError()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        var utilsPath = WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", @"Import MathUtils

Sub Main()
    Dim x As Integer = twice(21)
End Sub
");

        // Open both documents; Main has a genuine error (twice doesn't exist yet)
        OpenDocument(utilsPath);
        var mainState = OpenDocument(mainPath);
        Assert.That(Errors(mainState).Any(d => d.Message.Contains("twice")), Is.True,
            "expected unresolved 'twice' before the sibling defines it: " + DumpDiagnostics(mainState));

        // didChange on the sibling: it now defines twice()
        OpenDocument(utilsPath, MathUtilsSource + @"
Public Function twice(x As Integer) As Integer
    Return x * 2
End Function
");

        // The server refreshes open siblings after a change (TextDocumentSyncHandler)
        var refreshed = _documentManager.RefreshOpenProjectSiblings(DocumentUri.FromFileSystemPath(utilsPath));

        Assert.That(refreshed.Select(s => s.FilePath), Does.Contain(mainState.FilePath),
            "Main.bas should have been re-analyzed after the sibling changed");

        var mainAfter = _documentManager.GetDocument(DocumentUri.FromFileSystemPath(mainPath));
        Assert.That(Errors(mainAfter!), Is.Empty,
            "stale error after sibling change: " + DumpDiagnostics(mainAfter!));
    }

    [Test]
    public void DidChange_SiblingLosesFunction_MainGetsErrorBack()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        var utilsPath = WriteFile("MathUtils.bas", MathUtilsSource);
        var mainPath = WriteFile("Main.bas", MainSource);

        OpenDocument(utilsPath);
        var mainState = OpenDocument(mainPath);
        Assert.That(Errors(mainState), Is.Empty, DumpDiagnostics(mainState));

        // The sibling loses addNums
        OpenDocument(utilsPath, @"Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
");
        _documentManager.RefreshOpenProjectSiblings(DocumentUri.FromFileSystemPath(utilsPath));

        var mainAfter = _documentManager.GetDocument(DocumentUri.FromFileSystemPath(mainPath));
        Assert.That(Errors(mainAfter!).Any(d => d.Message.Contains("addNums")), Is.True,
            "expected 'addNums' to be unresolved after the sibling removed it: " + DumpDiagnostics(mainAfter!));
    }

    // ------------------------------------------------------------------
    // Module-name collisions: file-level declarations must never be lost
    // or misattributed when a Module block shares the name
    // ------------------------------------------------------------------

    [Test]
    public void Collector_FileLevelAndSameNamedModuleBlock_BothContributeSymbols()
    {
        // Utils.bas has BOTH a file-level sub (registered under the file-derived
        // module "Utils") and an explicit `Module Utils` block. Neither set of
        // symbols may be dropped.
        WriteFile("Utils.bas", @"Public Sub FreeFunc()
End Sub

Module Utils
    Public Function Blocked() As Integer
        Return 1
    End Function
End Module
");
        var mainPath = WriteFile("Main.bas", @"Import Utils

Sub Main()
    FreeFunc()
    Dim x As Integer = Blocked()
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state), Is.Empty, "spurious diagnostics: " + DumpDiagnostics(state));

        var hover = _symbolService.GetHoverInfo(state, "FreeFunc");
        Assert.That(hover, Is.Not.Null, "file-level symbol lost when a Module block shares the file name");
        Assert.That(hover, Does.Contain("Utils.bas"));
    }

    [Test]
    public void Collector_FileNameCollidesWithSiblingModuleBlock_SymbolsAttributedToOwnFiles()
    {
        // Helpers.bas declares `Module Foo`; Foo.bas has a file-level function.
        // Both land in project-module "Foo", but hover/definition must point at
        // each symbol's ACTUAL defining file.
        WriteFile("Helpers.bas", @"Module Foo
    Public Function FromBlock() As Integer
        Return 1
    End Function
End Module
");
        WriteFile("Foo.bas", @"Public Function Bar() As Integer
    Return 2
End Function
");
        var mainPath = WriteFile("Main.bas", @"Import Foo

Sub Main()
    Dim x As Integer = Bar()
    Dim y As Integer = FromBlock()
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state), Is.Empty, "spurious diagnostics: " + DumpDiagnostics(state));

        var barLocation = _symbolService.FindDefinition(state, "Bar");
        Assert.That(barLocation, Is.Not.Null);
        Assert.That(barLocation!.Uri.ToString(), Does.EndWith("Foo.bas"),
            "Bar is declared in Foo.bas, not in the file that declared Module Foo");

        var blockHover = _symbolService.GetHoverInfo(state, "FromBlock");
        Assert.That(blockHover, Is.Not.Null);
        Assert.That(blockHover, Does.Contain("Helpers.bas"),
            "FromBlock is declared in Helpers.bas via the Module Foo block");
    }

    // ------------------------------------------------------------------
    // Imports that the LSP cannot prove unresolvable must not error
    // ------------------------------------------------------------------

    [Test]
    public void Diagnostics_ImportOfTransientlyUnparseableSibling_NoImportError()
    {
        // The user is mid-edit in MathUtils.bas (unterminated string — the
        // single most common transient state). Main.bas must NOT get a red
        // squiggle on its Import line for a file that exists but won't parse.
        WriteFile("CrossFileTest.blproj", ProjectXml);
        WriteFile("MathUtils.bas", "Public Const Msg As String = \"Hello\n");
        var mainPath = WriteFile("Main.bas", @"Import MathUtils

Sub Main()
    Console.WriteLine(""hi"")
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state).Any(d => d.Message.Contains("Cannot resolve import")), Is.False,
            "import of an unparseable-but-present sibling must not error: " + DumpDiagnostics(state));
    }

    [Test]
    public void Diagnostics_ImportOfModuleNamedSubdirectory_NoImportError()
    {
        // No .blproj: the compiler's ModuleResolver can resolve `Import X`
        // via a subdirectory named X, but the implicit LSP sibling scan is
        // top-directory-only. The LSP must not flag what the compiler accepts.
        var subDir = Path.Combine(_rootDir, "MathUtils");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "MathUtils.bas"), MathUtilsSource);
        var mainPath = WriteFile("Main.bas", @"Import MathUtils

Sub Main()
    Console.WriteLine(""hi"")
End Sub
");

        var state = OpenDocument(mainPath);

        Assert.That(Errors(state).Any(d => d.Message.Contains("Cannot resolve import")), Is.False,
            "import resolvable via module-named subdirectory must not error: " + DumpDiagnostics(state));
    }

    // ------------------------------------------------------------------
    // Concurrency: re-analysis must publish results atomically
    // ------------------------------------------------------------------

    [Test]
    public void ReRunSemanticAnalysis_PublishesNewDiagnosticsList_OldSnapshotIntact()
    {
        // Cross-document refresh re-analyzes documents that other LSP threads
        // (hover/completion/publish) may be reading concurrently. The results
        // must be published by reference swap, never by mutating the list a
        // reader may be enumerating.
        var mainPath = WriteFile("Main.bas", @"Sub Main()
    Dim x As Integer = missingFunc(1)
End Sub
");
        var state = OpenDocument(mainPath);
        var before = state.Diagnostics;
        var beforeCount = before.Count;
        Assert.That(beforeCount, Is.GreaterThan(0), "expected at least one diagnostic to pin");

        state.ReRunSemanticAnalysis();

        Assert.That(state.Diagnostics, Is.Not.SameAs(before),
            "re-analysis must swap in a fresh list, not mutate the one readers hold");
        Assert.That(before.Count, Is.EqualTo(beforeCount),
            "the old snapshot must remain intact for concurrent readers");
    }

    [Test]
    public void UpdateDocument_UnchangedContentButChangedProject_ReRunsSemanticAnalysis()
    {
        WriteFile("CrossFileTest.blproj", ProjectXml);
        var utilsPath = WriteFile("MathUtils.bas", @"Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
");
        var mainPath = WriteFile("Main.bas", MainSource);

        var mainState = OpenDocument(mainPath);
        Assert.That(Errors(mainState).Any(d => d.Message.Contains("addNums")), Is.True,
            "addNums should be unresolved initially: " + DumpDiagnostics(mainState));

        // The sibling gains addNums ON DISK (not open in the editor)
        File.WriteAllText(utilsPath, MathUtilsSource);

        // Re-open/update Main with IDENTICAL content: the content-hash shortcut
        // must not return the stale analysis because the project stamp changed
        var mainAfter = OpenDocument(mainPath, MainSource);

        Assert.That(Errors(mainAfter), Is.Empty,
            "stale analysis returned although a sibling changed on disk: " + DumpDiagnostics(mainAfter));
    }
}
