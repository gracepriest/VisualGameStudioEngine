using System;
using System.IO;
using System.Linq;
using BasicLang.Compiler.LSP;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using DiagnosticSeverity = BasicLang.Compiler.LSP.DiagnosticSeverity;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Portable-controls Task 7 (spec §4.2, M6), the EDITOR route: a class whose base is declared in another file of the
/// project. The build accepted it once Visit(ClassNode) looked through the scopes, but the language server supplies
/// sibling files' types through the PROJECT symbol table (<c>_projectSymbols</c>), not through a scope — so the
/// editor still underlined the class with "Unknown base class 'Base'", then "MyBase can only be used…" and
/// "'D' does not have a member 'Hello'" (Task 7 review, measured through DocumentManager).
/// </summary>
[TestFixture]
public class LspCrossFileInheritsTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-lspinh-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private const string BaseFile =
        "Public Class Base\n Public Overridable Function Hi() As String\n  Return \"base\"\n End Function\n" +
        " Public Sub Hello()\n  PrintLine(\"hello from base\")\n End Sub\nEnd Class\n";

    private const string DerivedFile =
        "Public Class D\n Inherits Base\n Public Overrides Function Hi() As String\n  Return \"D:\" & MyBase.Hi()\n" +
        " End Function\n Public Sub Greet()\n  Me.Hello()\n  Hello()\n End Sub\nEnd Class\n";

    private string Errors(string openedFile, string backend)
    {
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            $"<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>{backend}</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Base.bas\" />\n    <Compile Include=\"Derived.bas\" />\n" +
            "    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(_dir, "Base.bas"), BaseFile);
        File.WriteAllText(Path.Combine(_dir, "Derived.bas"), DerivedFile);
        File.WriteAllText(Path.Combine(_dir, "Main.bas"),
            "Module Program\n Sub Main()\n  Dim x As Base = New D()\n  PrintLine(x.Hi())\n  Dim d As New D()\n  d.Greet()\n  d.Hello()\n End Sub\nEnd Module\n");

        var path = Path.Combine(_dir, openedFile);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), File.ReadAllText(path));
        Assert.That(state.ProjectContext, Is.Not.Null, "the document must be opened inside its project");
        return string.Join(" | ", state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"({d.Line},{d.Column}): {d.Message}"));
    }

    /// <summary>The derived class's own file: its base, MyBase and the inherited Sub all resolve.</summary>
    [TestCase("CSharp")]
    [TestCase("JavaScript")]
    public void TheDerivedFile_HasNoErrors_WhenItsBaseIsInAnotherFile(string backend) =>
        Assert.That(Errors("Derived.bas", backend), Is.Empty);

    /// <summary>A third file using both: the upcast and the inherited member through a local.</summary>
    [TestCase("CSharp")]
    [TestCase("JavaScript")]
    public void AFileUsingTheDerivedClass_HasNoErrors(string backend) =>
        Assert.That(Errors("Main.bas", backend), Is.Empty);
}
