using System;
using System.IO;
using System.Linq;
using Moq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// C++ Phase 2 (mixed projects), Task 9: the BasicLang LSP server must never
/// treat a .cpp/.h file as BasicLang source.
///
/// A mixed BasicLang/C++ project's .blproj lists .cpp/.h alongside .bas in
/// &lt;Compile Include&gt; items. Before this fix, LspProjectContextProvider took
/// ProjectFile.GetSourceFiles() UNFILTERED, so a .cpp/.h got lexed/parsed AS
/// BASICLANG by the error-recovering parser and silently registered a junk
/// module under its basename (or merged into a same-named .bas module via
/// ModuleSymbols.MergeFrom, since ProjectSymbolTable's module dictionary is
/// case-insensitive). The pollution is invisible in diagnostics (the LSP only
/// publishes analyzer errors, never recovered parse errors), so it must be
/// asserted via the symbol table / module registry directly.
///
/// Three independent layers are covered here:
///  1. The choke-point whitelist filter in LspProjectContext.GetBlprojSourceFiles
///     (the load-bearing fix — a mixed .blproj's C++ items never enter the
///     LSP's project file set).
///  2. The sibling-scan pattern reconciliation (an implicit/no-.blproj project
///     now derives its glob patterns from the same shared whitelist, closing
///     a pre-existing .basic/.class gap).
///  3. Defense-in-depth: DocumentManager.UpdateDocument refuses to
///     register/parse/publish a URI with a recognizable non-BasicLang
///     extension, GetTextDocumentAttributes answers such URIs as "plaintext",
///     and DiagnosticsService.PublishDiagnostics no-ops on a null state (the
///     shape UpdateDocument now returns for a rejected URI) instead of
///     throwing a NullReferenceException at the TextDocumentSyncHandler call
///     sites that don't null-check.
/// </summary>
[TestFixture]
public class LspMixedProjectTests
{
    private string _rootDir = null!;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-lspmixed-" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------
    // 1. Choke-point filter: LspProjectContext.GetBlprojSourceFiles
    // ------------------------------------------------------------------

    private const string MixedProjectXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>MixedTest</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Logic.bas"" />
    <Compile Include=""helper.cpp"" />
    <Compile Include=""helper.h"" />
  </ItemGroup>
</BasicLangProject>
";

    [Test]
    public void MixedBlproj_ProjectContext_ExcludesCppItems()
    {
        WriteFile("MixedTest.blproj", MixedProjectXml);
        var logicPath = WriteFile("Logic.bas",
            "Public Function AddNums(a As Integer, b As Integer) As Integer\n    Return a + b\nEnd Function\n");

        // Deliberately VALID BasicLang syntax saved under .cpp/.h names. Real
        // C++ text would depend on unspecified parser error-recovery
        // behavior; using parseable content instead makes the assertion
        // deterministic — if the choke-point filter didn't exclude these
        // files, "helper" WOULD be registered as a real module below.
        WriteFile("helper.cpp",
            "Public Function HelperFn() As Integer\n    Return 42\nEnd Function\n");
        WriteFile("helper.h",
            "Public Function HelperFn2() As Integer\n    Return 43\nEnd Function\n");

        var provider = new LspProjectContextProvider();
        var context = provider.GetContext(logicPath, File.ReadAllText(logicPath));

        Assert.That(context, Is.Not.Null, "expected a project context from the mixed .blproj");
        Assert.That(context!.SourceFiles, Has.Count.EqualTo(1),
            "the .cpp/.h Compile items must never enter the LSP's source-file set: " +
            string.Join(", ", context.SourceFiles));
        Assert.That(context.SourceFiles, Does.Contain(Path.GetFullPath(logicPath)));

        var moduleNames = context.Symbols.GetModuleNames().ToList();
        Assert.That(moduleNames.Any(n => string.Equals(n, "Logic", StringComparison.OrdinalIgnoreCase)),
            Is.True, "the .bas module must still be registered: " + string.Join(", ", moduleNames));
        Assert.That(moduleNames.Any(n => string.Equals(n, "helper", StringComparison.OrdinalIgnoreCase)),
            Is.False, "a .cpp/.h basename must never appear as a registered module: " + string.Join(", ", moduleNames));

        Assert.That(context.IndeterminateImports.Any(n => string.Equals(n, "helper", StringComparison.OrdinalIgnoreCase)),
            Is.False, "excluded files must not be reported as indeterminate either — they were never attempted");
    }

    private const string SharedBasenameProjectXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>SharedBasenameTest</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""logic.bas"" />
    <Compile Include=""logic.cpp"" />
  </ItemGroup>
</BasicLangProject>
";

    [Test]
    public void CppFileSharingBasename_DoesNotPolluteRealModule()
    {
        WriteFile("SharedBasenameTest.blproj", SharedBasenameProjectXml);
        var logicBasPath = WriteFile("logic.bas",
            "Public Function Add(a As Integer, b As Integer) As Integer\n    Return a + b\nEnd Function\n");

        // Same basename as logic.bas, on purpose: ProjectSymbolTable's module
        // dictionary is case-insensitive, so LspModuleSymbolCollector.Collect
        // would look up the ALREADY-REGISTERED "Logic" module (by the
        // case-insensitive key "logic") and merge this file's declarations
        // directly into the shared ModuleSymbols object — corrupting the real
        // module — if the choke-point filter didn't exclude this file first.
        WriteFile("logic.cpp",
            "Public Function Corrupt() As Integer\n    Return 999\nEnd Function\n");

        var provider = new LspProjectContextProvider();
        var context = provider.GetContext(logicBasPath, File.ReadAllText(logicBasPath));

        Assert.That(context, Is.Not.Null);
        Assert.That(context!.SourceFiles, Has.Count.EqualTo(1),
            "logic.cpp must be excluded from the project's file set: " + string.Join(", ", context.SourceFiles));

        var module = context.Symbols.GetModule("Logic");
        Assert.That(module, Is.Not.Null);
        Assert.That(module!.FilePath, Is.EqualTo(Path.GetFullPath(logicBasPath)),
            "the module's defining file must be the .bas — never the .cpp");

        var symbolNames = module.GetAllSymbols().Select(s => s.Name).ToList();
        Assert.That(symbolNames, Is.EquivalentTo(new[] { "Add" }),
            "only the .bas's own symbol may be present: " + string.Join(", ", symbolNames));
        Assert.That(symbolNames, Does.Not.Contain("Corrupt"),
            "the .cpp's symbol must never be merged into the real module");
    }

    // ------------------------------------------------------------------
    // 2. Sibling-scan pattern reconciliation (implicit/no-.blproj project)
    // ------------------------------------------------------------------

    [Test]
    public void ImplicitProject_SiblingScan_IncludesBasicAndClassExtensions()
    {
        // No .blproj — implicit (sibling-directory) project. The sibling scan
        // used to be a hand-maintained literal list { *.bas, *.bl, *.mod,
        // *.cls } that had drifted from ProjectFile.BasicLangSourceExtensions
        // (missing .basic/.class). Deriving the patterns from the shared
        // array closes that gap.
        var mainPath = WriteFile("Main.bas", "Sub Main()\nEnd Sub\n");
        var basicPath = WriteFile("Extra.basic", "Public Sub ExtraSub()\nEnd Sub\n");
        var classPath = WriteFile("Widget.class", "Public Sub WidgetMethod()\nEnd Sub\n");

        var provider = new LspProjectContextProvider();
        var context = provider.GetContext(mainPath, File.ReadAllText(mainPath));

        Assert.That(context, Is.Not.Null);
        Assert.That(context!.ProjectFilePath, Is.Null, "no .blproj present — implicit sibling project");
        Assert.That(context.SourceFiles, Does.Contain(Path.GetFullPath(basicPath)),
            ".basic siblings must be picked up by the implicit-project scan");
        Assert.That(context.SourceFiles, Does.Contain(Path.GetFullPath(classPath)),
            ".class siblings must be picked up by the implicit-project scan");
    }

    // ------------------------------------------------------------------
    // 3. Defense-in-depth: DocumentManager / TextDocumentSyncHandler
    // ------------------------------------------------------------------

    [Test]
    public void UpdateDocument_CppUri_ReturnsNull_DoesNotRegisterDocument()
    {
        var cppPath = WriteFile("helper.cpp", "int main() { return 0; }\n");
        var uri = DocumentUri.FromFileSystemPath(cppPath);
        var documentManager = new DocumentManager();

        var state = documentManager.UpdateDocument(uri, File.ReadAllText(cppPath));

        Assert.That(state, Is.Null, "a .cpp URI must never be registered/parsed by the BasicLang server");
        Assert.That(documentManager.GetDocument(uri), Is.Null,
            "no document should have been registered for the .cpp URI");
    }

    [Test]
    public void UpdateDocument_BasUri_StillRegisters()
    {
        // Regression guard: the new gate must not affect real BasicLang documents.
        var basPath = WriteFile("Main.bas", "Sub Main()\nEnd Sub\n");
        var uri = DocumentUri.FromFileSystemPath(basPath);
        var documentManager = new DocumentManager();

        var state = documentManager.UpdateDocument(uri, File.ReadAllText(basPath));

        Assert.That(state, Is.Not.Null);
        Assert.That(documentManager.GetDocument(uri), Is.Not.Null);
    }

    [Test]
    public void GetTextDocumentAttributes_CppUri_ReturnsPlaintext_BasUri_ReturnsBasicLang()
    {
        var documentManager = new DocumentManager();
        var diagnosticsService = new DiagnosticsService();
        var serverMock = new Mock<ILanguageServerFacade>();
        var handler = new TextDocumentSyncHandler(documentManager, diagnosticsService, serverMock.Object);

        var cppUri = DocumentUri.From("file:///helper.cpp");
        var basUri = DocumentUri.From("file:///Main.bas");

        var cppAttrs = handler.GetTextDocumentAttributes(cppUri);
        var basAttrs = handler.GetTextDocumentAttributes(basUri);

        Assert.That(cppAttrs.LanguageId, Is.EqualTo("plaintext"),
            "the server must not claim ownership of a .cpp URI");
        Assert.That(basAttrs.LanguageId, Is.EqualTo("basiclang"));
    }

    [Test]
    public void PublishDiagnostics_NullState_DoesNotThrow()
    {
        // DocumentManager.UpdateDocument now returns null for a rejected URI,
        // and TextDocumentSyncHandler.Handle(DidOpen/DidChange...) passes that
        // straight to DiagnosticsService.PublishDiagnostics without a null
        // check. This must no-op rather than NRE on state.Diagnostics/state.Uri.
        var diagnosticsService = new DiagnosticsService();
        var serverMock = new Mock<ILanguageServerFacade>();

        Assert.DoesNotThrow(() => diagnosticsService.PublishDiagnostics(serverMock.Object, null!));
    }
}
