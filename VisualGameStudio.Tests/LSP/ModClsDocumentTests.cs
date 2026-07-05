using NUnit.Framework;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// .mod/.cls document support: the compiler wraps .mod files in an implicit
/// Module and .cls files in an implicit Class. The LSP server must parse them
/// the same way WITHOUT shifting line numbers (a text wrapper would corrupt
/// every diagnostic position and completion cursor mapping).
/// </summary>
[TestFixture]
public class ModClsDocumentTests
{
    private CompletionService _completionService = null!;
    private DocumentManager _documentManager = null!;
    private string _rootDir = null!;

    [SetUp]
    public void SetUp()
    {
        _completionService = new CompletionService();
        _documentManager = new DocumentManager();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-modcls-" + Guid.NewGuid().ToString("N"));
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

    private static DocumentState CreateParsedState(string sourceCode, string fileName)
    {
        var uri = DocumentUri.From($"file:///{fileName}");
        var state = new DocumentState(uri, sourceCode);
        state.Parse();
        return state;
    }

    private static IEnumerable<Diagnostic> Errors(DocumentState state) =>
        state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    private static string Dump(DocumentState state) =>
        string.Join("; ", state.Diagnostics.Select(d => $"{d.Severity}({d.Line},{d.Column}): {d.Message}"));

    // ------------------------------------------------------------------
    // .mod documents
    // ------------------------------------------------------------------

    private const string BareModSource =
        "Public Function AddNums(a As Integer, b As Integer) As Integer\n" + // 1-based line 1
        "    Return a + b\n" +                                               // line 2
        "End Function\n" +                                                   // line 3
        "\n" +                                                               // line 4
        "Public Sub Announce()\n" +                                          // line 5
        "    PrintLine(\"hello\")\n" +                                       // line 6
        "End Sub\n";                                                         // line 7

    [Test]
    public void BareModFile_ParsesWithoutErrors_AndWrapsInImplicitModule()
    {
        var state = CreateParsedState(BareModSource, "MathHelpers.mod");

        Assert.That(state.ParseSuccessful, Is.True);
        Assert.That(Errors(state), Is.Empty, "unexpected diagnostics: " + Dump(state));

        var module = state.AST!.Declarations.OfType<ModuleNode>().FirstOrDefault();
        Assert.That(module, Is.Not.Null, "a .mod file must be wrapped in an implicit Module node");
        Assert.That(module!.Name, Is.EqualTo("MathHelpers"), "module name comes from the file name");
        Assert.That(module.Members.OfType<FunctionNode>().Any(f => f.Name == "AddNums"), Is.True);
        Assert.That(module.Members.OfType<SubroutineNode>().Any(s => s.Name == "Announce"), Is.True);
    }

    [Test]
    public void BareModFile_SymbolsAppearInCompletion()
    {
        var state = CreateParsedState(BareModSource, "MathHelpers.mod");

        // General completion inside the file
        var result = _completionService.GetCompletions(state, 3, 0);

        Assert.That(result.Any(c => c.Label == "AddNums"), Is.True, "module function must complete");
        Assert.That(result.Any(c => c.Label == "Announce"), Is.True, "module sub must complete");
    }

    [Test]
    public void BareModFile_LocalsAndParametersComplete_InsideProcedure()
    {
        var source =
            "Public Sub Work(amount As Integer)\n" + // 0-based line 0
            "    Dim total As Integer\n" +           // line 1
            "    \n" +                               // line 2 (cursor)
            "End Sub\n";
        var state = CreateParsedState(source, "Worker.mod");

        var result = _completionService.GetCompletions(state, 2, 4);

        Assert.That(result.Any(c => c.Label == "amount"), Is.True, "parameter must complete inside a .mod procedure");
        Assert.That(result.Any(c => c.Label == "total"), Is.True, "local must complete inside a .mod procedure");
    }

    [Test]
    public void ModFile_DiagnosticLandsOnCorrectLine()
    {
        var source =
            "Public Sub DoWork()\n" +      // 1-based line 1
            "    Dim x As Integer\n" +     // line 2
            "    x = undefinedVar\n" +     // line 3  <-- error here
            "End Sub\n";
        var state = CreateParsedState(source, "Broken.mod");

        var errors = Errors(state).ToList();
        Assert.That(errors, Is.Not.Empty, "expected an error for the undefined identifier");
        Assert.That(errors.Any(d => d.Line == 3), Is.True,
            "the diagnostic must land on line 3 (no line shift from the implicit wrapper); got: " + Dump(state));
    }

    [Test]
    public void ModFile_WithExplicitModuleHeader_IsNotDoubleWrapped()
    {
        var source =
            "Module Tools\n" +
            "    Public Sub Ping()\n" +
            "    End Sub\n" +
            "End Module\n";
        var state = CreateParsedState(source, "Tools.mod");

        Assert.That(Errors(state), Is.Empty, "unexpected diagnostics: " + Dump(state));
        var modules = state.AST!.Declarations.OfType<ModuleNode>().ToList();
        Assert.That(modules, Has.Count.EqualTo(1));
        Assert.That(modules[0].Name, Is.EqualTo("Tools"));
        Assert.That(modules[0].Members.OfType<ModuleNode>(), Is.Empty, "no nested double-wrap");
    }

    // ------------------------------------------------------------------
    // .cls documents
    // ------------------------------------------------------------------

    private const string BareClsSource =
        "Private _health As Integer\n" +                    // 1-based line 1
        "\n" +                                              // line 2
        "Public Sub Heal(amount As Integer)\n" +            // line 3
        "    _health = _health + amount\n" +                // line 4
        "End Sub\n" +                                       // line 5
        "\n" +                                              // line 6
        "Public Function GetHealth() As Integer\n" +        // line 7
        "    Return _health\n" +                            // line 8
        "End Function\n";                                   // line 9

    [Test]
    public void BareClsFile_ParsesWithoutErrors_AndWrapsInImplicitClass()
    {
        var state = CreateParsedState(BareClsSource, "Player.cls");

        Assert.That(state.ParseSuccessful, Is.True);
        Assert.That(Errors(state), Is.Empty, "unexpected diagnostics: " + Dump(state));

        var cls = state.AST!.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(cls, Is.Not.Null, "a .cls file must be wrapped in an implicit Class node");
        Assert.That(cls!.Name, Is.EqualTo("Player"), "class name comes from the file name");
        Assert.That(cls.Members.OfType<SubroutineNode>().Any(s => s.Name == "Heal"), Is.True);
        Assert.That(cls.Members.OfType<FunctionNode>().Any(f => f.Name == "GetHealth"), Is.True);
    }

    [Test]
    public void ClsFile_DiagnosticLandsOnCorrectLine()
    {
        var source =
            "Private _health As Integer\n" +   // 1-based line 1
            "\n" +                             // line 2
            "Public Sub Heal()\n" +            // line 3
            "    _health = missingThing\n" +   // line 4  <-- error here
            "End Sub\n";
        var state = CreateParsedState(source, "Player.cls");

        var errors = Errors(state).ToList();
        Assert.That(errors, Is.Not.Empty, "expected an error for the undefined identifier");
        Assert.That(errors.Any(d => d.Line == 4), Is.True,
            "the diagnostic must land on line 4 (no line shift from the implicit wrapper); got: " + Dump(state));
    }

    [Test]
    public void ClsFile_MeDot_CompletesClassMembers()
    {
        var source =
            "Private _health As Integer\n" +
            "\n" +
            "Public Sub Heal(amount As Integer)\n" +
            "    Me.\n" +                            // 0-based line 3
            "End Sub\n";
        var state = CreateParsedState(source, "Player.cls");

        var result = _completionService.GetCompletions(state, 3, "    Me.".Length);

        Assert.That(result.Any(c => c.Label == "_health"), Is.True, "'Me.' must list the implicit class's fields");
        Assert.That(result.Any(c => c.Label == "Heal"), Is.True, "'Me.' must list the implicit class's methods");
    }

    [Test]
    public void ClsFile_WithLeadingPublicLine_MakesClassPublic()
    {
        var source =
            "Public\n" +
            "Private _x As Integer\n" +
            "Public Sub Go()\n" +
            "End Sub\n";
        var state = CreateParsedState(source, "Thing.cls");

        Assert.That(Errors(state), Is.Empty, "unexpected diagnostics: " + Dump(state));
        var cls = state.AST!.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(cls, Is.Not.Null);
        Assert.That(cls!.Access, Is.EqualTo(AccessModifier.Public));
        Assert.That(cls.Members.OfType<SubroutineNode>().Any(s => s.Name == "Go"), Is.True);
    }

    [Test]
    public void ClsFile_WithOptionPublicDirective_MakesClassPublic()
    {
        // Compiler parity (PreprocessClassFile): "Option Public" as the first
        // code line makes the implicit class public and is consumed — it must
        // not surface as a parse error or shift any diagnostic line.
        var source =
            "Option Public\n" +                // 1-based line 1 (directive)
            "Private _x As Integer\n" +        // line 2
            "Public Sub Go()\n" +              // line 3
            "End Sub\n";                       // line 4
        var state = CreateParsedState(source, "Thing.cls");

        Assert.That(Errors(state), Is.Empty, "unexpected diagnostics: " + Dump(state));
        var cls = state.AST!.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(cls, Is.Not.Null);
        Assert.That(cls!.Access, Is.EqualTo(AccessModifier.Public));
        Assert.That(cls.Members.OfType<SubroutineNode>().Any(s => s.Name == "Go"), Is.True);
    }

    [Test]
    public void ClsFile_OptionPublicAfterLeadingComments_StillApplies()
    {
        // The compiler skips blank lines and comment lines when looking for
        // the directive; the LSP must match.
        var source =
            "' player entity\n" +
            "\n" +
            "Option Public\n" +
            "Public Sub Go()\n" +
            "End Sub\n";
        var state = CreateParsedState(source, "Thing.cls");

        Assert.That(Errors(state), Is.Empty, "unexpected diagnostics: " + Dump(state));
        var cls = state.AST!.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(cls, Is.Not.Null);
        Assert.That(cls!.Access, Is.EqualTo(AccessModifier.Public));
    }

    [Test]
    public void ClsFile_OptionPublic_DiagnosticsStillLandOnCorrectLine()
    {
        var source =
            "Option Public\n" +                 // 1-based line 1
            "Public Sub Heal()\n" +             // line 2
            "    Dim h As Integer\n" +          // line 3
            "    h = missingThing\n" +          // line 4  <-- error here
            "End Sub\n";
        var state = CreateParsedState(source, "Player.cls");

        var errors = Errors(state).ToList();
        Assert.That(errors, Is.Not.Empty, "expected an error for the undefined identifier");
        Assert.That(errors.Any(d => d.Line == 4), Is.True,
            "the diagnostic must land on line 4 (directive consumed, no line shift); got: " + Dump(state));
    }

    // ------------------------------------------------------------------
    // Cross-file: sibling .cls/.mod symbols reach other documents
    // ------------------------------------------------------------------

    [Test]
    public void SiblingClsFile_ClassMembers_CompleteInMainFile()
    {
        File.WriteAllText(Path.Combine(_rootDir, "Player.cls"), BareClsSource);
        var mainSource = "Sub Main()\n    Dim p As Player\n    p.\nEnd Sub\n";
        var mainPath = Path.Combine(_rootDir, "Main.bas");
        File.WriteAllText(mainPath, mainSource);

        var state = _documentManager.UpdateDocument(DocumentUri.FromFileSystemPath(mainPath), mainSource);
        Assert.That(state.ProjectContext, Is.Not.Null, "expected an implicit sibling project");

        var result = _completionService.GetCompletions(state, 2, "    p.".Length);

        Assert.That(result.Any(c => c.Label == "Heal"), Is.True,
            "members of a sibling .cls class must complete");
        Assert.That(result.Any(c => c.Label == "GetHealth"), Is.True);
    }

    [Test]
    public void SiblingModFile_FunctionCompletes_InMainFile()
    {
        File.WriteAllText(Path.Combine(_rootDir, "MathHelpers.mod"), BareModSource);
        var mainSource = "Sub Main()\n    \nEnd Sub\n";
        var mainPath = Path.Combine(_rootDir, "Main.bas");
        File.WriteAllText(mainPath, mainSource);

        var state = _documentManager.UpdateDocument(DocumentUri.FromFileSystemPath(mainPath), mainSource);

        var result = _completionService.GetCompletions(state, 1, 4);

        Assert.That(result.Any(c => c.Label == "AddNums"), Is.True,
            "functions from a sibling .mod file must complete");
    }
}
