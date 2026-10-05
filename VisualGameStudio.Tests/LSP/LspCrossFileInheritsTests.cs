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

    /// <summary>The errors the editor reports for <paramref name="openedFile"/> in a project of the given files.</summary>
    private string ErrorsIn(string openedFile, params (string Name, string Text)[] files)
    {
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>CSharp</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n" +
            string.Concat(files.Select(f => $"    <Compile Include=\"{f.Name}\" />\n")) +
            "  </ItemGroup>\n</Project>\n");
        foreach (var f in files) File.WriteAllText(Path.Combine(_dir, f.Name), f.Text);

        var path = Path.Combine(_dir, openedFile);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), File.ReadAllText(path));
        Assert.That(state.ProjectContext, Is.Not.Null, "the document must be opened inside its project");
        return string.Join(" | ", state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"({d.Line},{d.Column}): {d.Message}"));
    }

    private const string UtilWithHelloOfInteger =
        "Module Util\n Public Function Hello(x As Integer) As String\n  Return \"module\"\n End Function\nEnd Module\n";

    /// <summary>
    /// ⛔ Task 7b, the EDITOR route: a bare <c>Hello(3)</c> in D binds to the base's <c>Hello()</c> (VB: a member,
    /// inherited included, shadows a Module's), so the editor reports the argument count the build reports — it
    /// used to type-check the call against the Module's <c>Hello(x As Integer)</c> and show nothing.
    /// </summary>
    [Test]
    public void ABareCallToAnInheritedMemberWithAnotherSignature_IsReportedInTheEditor() =>
        Assert.That(ErrorsIn("Derived.bas",
                ("Base.bas", "Public Class Base\n Public Function Hello() As Integer\n  Return 5\n End Function\nEnd Class\n"),
                ("Util.bas", UtilWithHelloOfInteger),
                ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Dim s As String = Hello(3)\n  PrintLine(s)\n End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\n Dim d As New D()\n d.Greet()\nEnd Sub\n")),
            Does.Contain("Function 'Hello' expects 0 argument(s), got 1"));

    /// <summary>
    /// ⛔ Task 7c, the EDITOR route of BC30469: a Shared method naming an instance method of a base in ANOTHER file.
    /// The base's shared-ness reaches the editor through the project symbol table's member symbols
    /// (<c>LspProjectContext</c>), so the editor reports what the build reports.
    /// </summary>
    [Test]
    public void ASharedMethodNamingAnInheritedInstanceMethod_IsReportedInTheEditor() =>
        Assert.That(ErrorsIn("Derived.bas",
                ("Base.bas", "Public Class Base\n Public Function Hello() As String\n  Return \"base\"\n End Function\nEnd Class\n"),
                ("Util.bas", "Module Util\n Public Function Hello() As String\n  Return \"module\"\n End Function\nEnd Module\n"),
                ("Derived.bas", "Public Class D\n Inherits Base\n Public Shared Sub S()\n  PrintLine(Hello())\n End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\n D.S()\nEnd Sub\n")),
            Does.Contain("BC30469"));

    /// <summary>
    /// ⛔ Task 7c review: the editor's cross-file member symbols (<c>LspProjectContext</c>) must carry a SHARED base
    /// member's shared-ness, or a valid bare use from a Shared method is a false BC30469 in the editor — proved by
    /// a mutation that nothing caught. One row per member kind the project table records.
    /// </summary>
    [TestCase("Public Shared Function Hello() As Integer\n  Return 1\n End Function", "  Dim n As Integer = Hello()\n", TestName = "ASharedBaseFunction_CalledBareFromASharedMethod_IsCleanInTheEditor")]
    [TestCase("Public Shared Sub Hello()\n End Sub", "  Hello()\n", TestName = "ASharedBaseSub_CalledBareFromASharedMethod_IsCleanInTheEditor")]
    [TestCase("Public Shared Hello As Func(Of Integer)", "  Dim n As Integer = Hello()\n", TestName = "ASharedBaseDelegateField_InvokedBareFromASharedMethod_IsCleanInTheEditor")]
    [TestCase("Public Shared ReadOnly Property Hello As Integer\n  Get\n   Return 1\n  End Get\n End Property", "  Dim n As Integer = Hello()\n", TestName = "ASharedBaseProperty_ReadBareFromASharedMethod_IsCleanInTheEditor")]
    public void ASharedBaseMember_NamedBareFromASharedMethod_IsCleanInTheEditor(string baseMember, string use) =>
        Assert.That(ErrorsIn("Derived.bas",
                ("Base.bas", "Public Class Base\n " + baseMember + "\nEnd Class\n"),
                ("Derived.bas", "Public Class D\n Inherits Base\n Public Shared Sub S()\n" + use + " End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\nEnd Sub\n")),
            Does.Not.Contain("BC30469"));

    /// <summary>The matching signature is clean in the editor: the call is typed Integer, by the base.</summary>
    [Test]
    public void ABareCallToAnInheritedFunction_IsTypedByTheBaseInTheEditor() =>
        Assert.That(ErrorsIn("Derived.bas",
                ("Base.bas", "Public Class Base\n Public Function Hello(x As Integer) As Integer\n  Return x + 1\n End Function\nEnd Class\n"),
                ("Util.bas", UtilWithHelloOfInteger),
                ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Dim n As Integer = Hello(3)\n  PrintLine(n * 2)\n End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\n Dim d As New D()\n d.Greet()\nEnd Sub\n")),
            Is.Empty);
}
