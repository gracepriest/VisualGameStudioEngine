using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan 2c — <c>.bli</c> declaration files and the shipped DOM declarations.
///
/// <para>A <c>.bli</c> holds <c>Extern Class</c> declarations of types the target runtime
/// provides. It is RESOLVED and COMPILED like any other unit — a project's glob picks it up,
/// the LSP serves it — but it is never an entry point: <c>BasicLang.exe x.bli</c> is refused,
/// and the entry-point search skips it like <c>.mod</c>/<c>.cls</c>.</para>
///
/// <para><b>The extension lives in several independent lists</b> (compiler resolver, project
/// glob, IDE constants, editor language map) — the plan counted ~15 sites and documented that
/// this exact drift already happened once with <c>.basic</c>/<c>.class</c>. The first tests
/// here pin each list by name so a missing one fails by name.</para>
///
/// <para><b>The DOM declarations</b> (<c>lib/js/dom-core.bli</c>) ship beside the compiler and
/// are auto-included in every JavaScript build, on both routes (single file and project). They
/// are gated on the BACKEND: on C# an <c>Element</c> would collide with a user type. A program
/// reaches the runtime objects through the hatch — <c>Dim d As Document = ::document</c> — which
/// plan 2 Task 7 made legal; from there every member access is typed and emitted verbatim.</para>
/// </summary>
[TestFixture]
public class BliDeclarationFileTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_Bli_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static BasicCompiler Js() => new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" });

    // ---------------------------------------------------------------- the extension, list by list

    [Test]
    public void ModuleResolver_TreatsBliAsSource()
        => Assert.That(ModuleResolver.IsSourceFile("dom-core.bli"), Is.True);

    [Test]
    public void ProjectGlob_IncludesBli()
        => Assert.That(BasicLang.Compiler.ProjectSystem.ProjectFile.BasicLangSourceExtensions, Does.Contain(".bli"));

    [Test]
    public void IdeConstants_IncludeBli()
        => Assert.That(VisualGameStudio.Core.Constants.FileExtensions.IsSourceFile("x.bli"), Is.True);

    [Test]
    public void EditorLanguageMap_RoutesBliToBasicLang()
        => Assert.That(VisualGameStudio.Core.Utilities.LanguageFileTypes.IsBasicLangSourceFile("x.bli"), Is.True);

    // ---------------------------------------------------------------- a .bli in a project

    [Test]
    public void ABliInTheProject_DeclaresATypeTheBasCanUse_AndEmitsNothingForIt()
    {
        var bli = Write("Widget.bli", "Public Extern Class Widget\nPublic Property label As String\nEnd Class\n");
        var bas = Write("Main.bas",
            "Sub Main()\nDim w As Widget\njavascript{ w = { label: \"from the runtime\" }; }\n" +
            "Console.WriteLine(w.label)\nEnd Sub\n");

        var result = Js().CompileProjectFiles(new[] { bas, bli });

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        var js = new JavaScriptCodeGenerator().Generate(result.CombinedIR);
        Assert.That(js, Does.Contain("w.label"));
        Assert.That(js, Does.Not.Contain("class Widget"), "an extern type must not be emitted — it would shadow the runtime's");
    }

    /// <summary>The entry-point search must skip a .bli exactly as it skips .mod/.cls.</summary>
    [Test]
    public void ABli_IsNeverTheEntryPoint()
    {
        var bli = Write("Widget.bli", "Public Extern Class Widget\nPublic Property label As String\nEnd Class\n");
        var bas = Write("Main.bas", "Sub Main()\nConsole.WriteLine(\"hi\")\nEnd Sub\n");

        // .bli listed FIRST: a naive "first file is the program" would pick it.
        var result = Js().CompileProjectFiles(new[] { bli, bas });

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        Assert.That(new JavaScriptCodeGenerator().Generate(result.CombinedIR), Does.Contain("Main();"));
    }

    [Test]
    public void ABliAlone_IsRefusedAsAProgram()
    {
        var bli = Write("Widget.bli", "Public Extern Class Widget\nPublic Property label As String\nEnd Class\n");

        var result = Js().CompileFile(bli);

        Assert.That(result.Success, Is.False);
        Assert.That(result.AllErrors.Select(e => e.Message), Has.Some.Contains("declaration file"));
    }

    // ---------------------------------------------------------------- the shipped DOM declarations

    [Test]
    public void DomDeclarations_ShipBesideTheCompiler()
        => Assert.That(File.Exists(BasicCompiler.DomDeclarationsPath), Is.True,
            $"{BasicCompiler.DomDeclarationsPath} is missing — the csproj must copy lib/js/dom-core.bli to the output");

    /// <summary>Single-file route: a .bas using Document compiles with nothing else on disk.</summary>
    [Test]
    public void DomDeclarations_AreAutoIncluded_OnTheSingleFileRoute()
    {
        var bas = Write("Main.bas",
            "Sub Main()\nDim d As Document = ::document\nDim el As Element = d.getElementById(\"out\")\n" +
            "el.textContent = \"hi\"\nEnd Sub\n");

        var result = Js().CompileFile(bas);

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        var js = new JavaScriptCodeGenerator().Generate(result.CombinedIR);
        Assert.That(js, Does.Contain("getElementById(\"out\")"));
        Assert.That(js, Does.Contain(".textContent = \"hi\";"));
        Assert.That(js, Does.Not.Contain("class Document"));
    }

    /// <summary>Project route — the one the IDE's BuildService and `build proj.blproj` share.</summary>
    [Test]
    public void DomDeclarations_AreAutoIncluded_OnTheProjectRoute()
    {
        var bas = Write("Main.bas", "Sub Main()\nDim d As Document = ::document\nConsole.WriteLine(d.title)\nEnd Sub\n");

        var result = Js().CompileProjectFiles(new[] { bas });

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        Assert.That(new JavaScriptCodeGenerator().Generate(result.CombinedIR), Does.Contain("d.title"));
    }

    /// <summary>
    /// ⛔ Backend-gated: on C# there is no DOM, and `Element` would collide with a user type.
    /// (An unknown type name is not itself an analyzer error on the C# route — it may be a .NET
    /// type — so the assertion is on the IR: no Document class was pulled in.)
    /// </summary>
    [Test]
    public void DomDeclarations_AreNotIncluded_ForCSharp()
    {
        var bas = Write("Main.bas", "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n");

        var result = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileFile(bas);

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Document"), Is.False,
            "the DOM declarations were included on a non-JavaScript backend");
        Assert.That(result.CombinedIR.Classes.ContainsKey("Element"), Is.False);
    }

    /// <summary>…and on JavaScript they are there.</summary>
    [Test]
    public void DomDeclarations_ArePresentInTheIR_ForJavaScript()
    {
        var bas = Write("Main.bas", "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n");

        var result = Js().CompileFile(bas);

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Document"), Is.True);
        Assert.That(result.CombinedIR.Classes["Document"].IsExtern, Is.True);
    }

    /// <summary>Every declared name must survive JsCapabilityChecker — no BL7011 collision with the stdlib surface or the exception set.</summary>
    [Test]
    public void DomDeclarations_PassTheCapabilityChecker()
    {
        var bas = Write("Main.bas", "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n");

        var result = Js().CompileFile(bas);

        Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
        Assert.DoesNotThrow(() => new JavaScriptCodeGenerator().Generate(result.CombinedIR));
    }
}

/// <summary>The typed DOM, RUN under Node with a fake `document` standing in for the browser's.</summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class DomDeclarationsExecutionTests
{
    private const string FakeDom =
        "javascript{ globalThis.document = { title: \"t\", _el: { id: \"out\", textContent: \"before\", " +
        "addEventListener(type, fn) { fn({ target: this, key: type }); } }, " +
        "getElementById(id) { return this._el; } }; }\n";

    private static string Run(string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_Dom_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var bas = Path.Combine(dir, "Main.bas");
            File.WriteAllText(bas, $"Sub Main()\n{FakeDom}{body}\nEnd Sub\n");

            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileFile(bas);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));

            return JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(result.CombinedIR));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Test]
    public void TypedDocument_ThroughTheHatch_ReachesTheRuntimeObject()
        => Assert.That(Run(
            "Dim d As Document = ::document\nDim el As Element = d.getElementById(\"out\")\n" +
            "el.textContent = \"after\"\nConsole.WriteLine(el.textContent)"),
            Is.EqualTo("after"));

    [Test]
    public void EventListener_WithALambda_ReceivesATypedEvent()
        => Assert.That(Run(
            "Dim d As Document = ::document\nDim el As Element = d.getElementById(\"out\")\n" +
            "el.addEventListener(\"click\", Sub(e As DomEvent) Console.WriteLine(\"clicked \" & e.target.id))"),
            Is.EqualTo("clicked out"));

    /// <summary>
    /// The 2b payoff, on the shipped declarations: PascalCase at the use site still reaches the
    /// camelCase PROPERTY. (A PascalCase METHOD call — <c>d.GetElementById</c> — is typed
    /// <c>Object</c> by the analyzer today, on every backend; that is a front-end case-folding
    /// gap, measured while writing this and tracked separately, not a 2c one.)
    /// </summary>
    [Test]
    public void PascalCaseUseSite_ReachesTheDeclaredCamelCaseProperty()
        => Assert.That(Run(
            "Dim d As Document = ::document\nDim el As Element = d.getElementById(\"out\")\n" +
            "Console.WriteLine(el.TextContent)"),
            Is.EqualTo("before"));
}
