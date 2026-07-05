using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Finding [7]: classes defined in sibling project files must get full
/// IntelliSense — "player." lists Player's members, and New/As/Inherits/
/// Implements contexts offer the cross-file types.
/// </summary>
[TestFixture]
public class CrossFileMemberCompletionTests
{
    private DocumentManager _documentManager = null!;
    private CompletionService _completionService = null!;
    private string _rootDir = null!;

    private const string PlayerSource =
        "Public Class Player\n" +
        "    Public Name As String\n" +
        "    Public Health As Integer\n" +
        "    Public Sub Jump()\n" +
        "    End Sub\n" +
        "    Public Function GetScore() As Integer\n" +
        "        Return 0\n" +
        "    End Function\n" +
        "End Class\n" +
        "\n" +
        "Public Interface IDrawable\n" +
        "    Sub Draw()\n" +
        "End Interface\n";

    [SetUp]
    public void SetUp()
    {
        _documentManager = new DocumentManager();
        _completionService = new CompletionService();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-xfile-member-" + Guid.NewGuid().ToString("N"));
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

    private DocumentState OpenDocument(string path, string content)
    {
        var uri = DocumentUri.FromFileSystemPath(path);
        return _documentManager.UpdateDocument(uri, content);
    }

    [Test]
    public void MemberAccess_OnCrossFileClassInstance_ListsItsMembers()
    {
        WriteFile("Player.bas", PlayerSource);
        var mainSource = "Sub Main()\n    Dim player As Player\n    player.\nEnd Sub\n";
        var mainPath = WriteFile("Main.bas", mainSource);

        var state = OpenDocument(mainPath, mainSource);
        Assert.That(state.ProjectContext, Is.Not.Null, "expected an implicit sibling project");

        var result = _completionService.GetCompletions(state, 2, "    player.".Length);

        Assert.That(result, Is.Not.Empty, "'player.' must list cross-file Player members");
        Assert.That(result.Any(c => c.Label == "Name"), Is.True, "expected field 'Name'");
        Assert.That(result.Any(c => c.Label == "Jump"), Is.True, "expected method 'Jump'");
        Assert.That(result.Any(c => c.Label == "GetScore"), Is.True, "expected method 'GetScore'");
    }

    [Test]
    public void NewContext_IncludesCrossFileClasses()
    {
        WriteFile("Player.bas", PlayerSource);
        var mainSource = "Sub Main()\n    Dim p = New \nEnd Sub\n";
        var mainPath = WriteFile("Main.bas", mainSource);

        var state = OpenDocument(mainPath, mainSource);

        var result = _completionService.GetCompletions(state, 1, "    Dim p = New ".Length);

        Assert.That(result.Any(c => c.Label == "Player"), Is.True,
            "'New ' must offer classes defined in sibling files");
    }

    [Test]
    public void AsContext_IncludesCrossFileClasses()
    {
        WriteFile("Player.bas", PlayerSource);
        var mainSource = "Sub Main()\n    Dim p As \nEnd Sub\n";
        var mainPath = WriteFile("Main.bas", mainSource);

        var state = OpenDocument(mainPath, mainSource);

        var result = _completionService.GetCompletions(state, 1, "    Dim p As ".Length);

        Assert.That(result.Any(c => c.Label == "Player"), Is.True,
            "'As ' must offer classes defined in sibling files");
    }

    [Test]
    public void InheritsContext_IncludesCrossFileClasses()
    {
        WriteFile("Player.bas", PlayerSource);
        var mainSource = "Class Wizard\n    Inherits \nEnd Class\n";
        var mainPath = WriteFile("Main.bas", mainSource);

        var state = OpenDocument(mainPath, mainSource);

        var result = _completionService.GetCompletions(state, 1, "    Inherits ".Length);

        Assert.That(result.Any(c => c.Label == "Player"), Is.True,
            "'Inherits ' must offer classes defined in sibling files");
    }

    [Test]
    public void ImplementsContext_IncludesCrossFileInterfaces()
    {
        WriteFile("Player.bas", PlayerSource);
        var mainSource = "Class Sprite\n    Implements \nEnd Class\n";
        var mainPath = WriteFile("Main.bas", mainSource);

        var state = OpenDocument(mainPath, mainSource);

        var result = _completionService.GetCompletions(state, 1, "    Implements ".Length);

        Assert.That(result.Any(c => c.Label == "IDrawable"), Is.True,
            "'Implements ' must offer interfaces defined in sibling files");
    }

    [Test]
    public void MemberAccess_MethodChainThroughCrossFileClass_Resolves()
    {
        WriteFile("Player.bas", PlayerSource);
        var mainSource = "Sub Main()\n    Dim player As Player\n    player.Name.\nEnd Sub\n";
        var mainPath = WriteFile("Main.bas", mainSource);

        var state = OpenDocument(mainPath, mainSource);

        var result = _completionService.GetCompletions(state, 2, "    player.Name.".Length);

        Assert.That(result.Any(c => c.Label == "ToUpper"), Is.True,
            "'player.Name.' must resolve the field type String through the cross-file class");
    }

    // ------------------------------------------------------------------
    // [13] 'Me.' must include members inherited from a CROSS-FILE base
    //      class (previously only 'MyBase.' had the project-symbol
    //      fallback).
    // ------------------------------------------------------------------

    [Test]
    public void MeDot_WithCrossFileBaseClass_IncludesInheritedMembers()
    {
        WriteFile("Animal.bas",
            "Public Class Animal\n" +
            "    Public Sub Speak()\n" +
            "    End Sub\n" +
            "End Class\n");
        var dogSource =
            "Public Class Dog\n" +          // line 0
            "    Inherits Animal\n" +       // line 1
            "    Public Sub Fetch()\n" +    // line 2
            "    End Sub\n" +               // line 3
            "    Public Sub Test()\n" +     // line 4
            "        Me.\n" +               // line 5
            "    End Sub\n" +               // line 6
            "End Class\n";                  // line 7
        var dogPath = WriteFile("Dog.bas", dogSource);

        var state = OpenDocument(dogPath, dogSource);
        Assert.That(state.ProjectContext, Is.Not.Null, "expected an implicit sibling project");

        var result = _completionService.GetCompletions(state, 5, "        Me.".Length);

        Assert.That(result.Any(c => c.Label == "Fetch"), Is.True, "expected own method 'Fetch'");
        Assert.That(result.Any(c => c.Label == "Speak"), Is.True,
            "'Me.' must include members inherited from a cross-file base class (parity with 'MyBase.')");
    }
}
