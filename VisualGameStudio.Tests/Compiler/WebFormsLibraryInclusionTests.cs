using System;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 28 — the portable WinForms-shaped library is included by a TOKEN scan of the compiled
/// source set, only on JavaScript, and a program class may not shadow a library class.
///
/// <para>These tests deliberately drive <see cref="BasicCompiler"/>, not
/// <c>JsTestSupport</c>: the latter starts at the lexer and bypasses the source-set hook, so it
/// cannot see an auto-included unit.</para>
/// </summary>
[TestFixture]
public class WebFormsLibraryInclusionTests
{
    private static BasicCompiler Js() => new(new CompilerOptions { TargetBackend = "javascript" });

    private static BasicCompiler Cs() => new(new CompilerOptions { TargetBackend = "csharp" });

    private static CompilationResult Compile(
        BasicCompiler compiler, params (string Name, string Text)[] files)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "bl-forms-" + Path.GetRandomFileName())).FullName;
        try
        {
            var paths = files
                .Select(f =>
                {
                    var path = Path.Combine(directory, f.Name);
                    File.WriteAllText(path, f.Text);
                    return path;
                })
                .ToArray();
            return compiler.CompileProjectFiles(paths);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static string Generated(CompilationResult result) =>
        new JavaScriptCodeGenerator().Generate(result.CombinedIR);

    private static string? FindRepoPath(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ---------------------------------------------------------------- trigger

    [Test]
    public void NoWebFormsMention_DoesNotIncludeTheLibrary()
    {
        var result = Compile(Js(), ("Main.bas",
            "Module Program\n Sub Main()\n  Console.WriteLine(1)\n End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Button"), Is.False,
            "a program that never mentions the namespaces must not ship the library");
        Assert.That(Generated(result), Does.Not.Contain("class Button"));
    }

    [Test]
    public void UsingSystemWindowsForms_IncludesTheLibrary()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System.Windows.Forms\nModule Program\n Sub Main()\n  Dim b As Button\n" +
            "  Console.WriteLine(\"ok\")\n End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Button"), Is.True);
        Assert.That(result.CombinedIR.Classes.ContainsKey("Control"), Is.True);
        Assert.That(Generated(result), Does.Contain("class Button extends Control"));
    }

    [Test]
    public void UsingSystemDrawing_IncludesTheDrawingTypes()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System.Drawing\nModule Program\n Sub Main()\n  Dim c As Color\n" +
            "  Console.WriteLine(\"ok\")\n End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Color"), Is.True);
        Assert.That(Generated(result), Does.Contain("class Color"));
    }

    [Test]
    public void QualifiedReference_IncludesTheLibrary_WithoutAUsing()
    {
        var result = Compile(Js(), ("Main.bas",
            "Module Program\n Sub Main()\n  Dim c As System.Drawing.Color\n" +
            "  Console.WriteLine(\"ok\")\n End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Color"), Is.True);
    }

    [Test]
    public void CommentOrStringMention_DoesNotIncludeTheLibrary()
    {
        var result = Compile(Js(), ("Main.bas",
            "' Using System.Windows.Forms\n" +
            "Module Program\n Sub Main()\n  Console.WriteLine(\"System.Drawing.Color\")\n" +
            " End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Button"), Is.False);
        Assert.That(result.CombinedIR.Classes.ContainsKey("Color"), Is.False);
    }

    [Test]
    public void CSharp_NeverIncludesTheLibrary()
    {
        var result = Compile(Cs(), ("Main.bas",
            "Using System.Windows.Forms\nModule Program\n Sub Main()\n  Console.WriteLine(1)\n" +
            " End Sub\nEnd Module\n"));

        Assert.That(result.CombinedIR?.Classes.ContainsKey("Button") ?? false, Is.False,
            "the portable library is JavaScript-only");
        Assert.That(result.CombinedIR?.Classes.ContainsKey("Control") ?? false, Is.False);
    }

    // ---------------------------------------------------------------- shipped files and skeleton

    [Test]
    public void LibraryFiles_ShipBesideTheCompiler()
    {
        var sourceDirectory = FindRepoPath("BasicLang", "lib", "js", "forms");
        Assert.That(sourceDirectory, Is.Not.Null, "BasicLang/lib/js/forms was not found from the test directory");

        foreach (var source in Directory.EnumerateFiles(sourceDirectory!, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory!, source);
            var shipped = Path.Combine(BasicCompiler.WebFormsLibraryDirectory, relative);
            Assert.That(File.Exists(shipped), Is.True,
                $"{relative} is missing from {BasicCompiler.WebFormsLibraryDirectory}; " +
                "the csproj must copy every library file");
        }

        Assert.That(File.Exists(Path.Combine(BasicCompiler.WebFormsLibraryDirectory, "vgs-page.bli")),
            Is.True, "vgs-page.bli belongs to the library and must ship with it");
    }

    [Test]
    public void Skeleton_ContainsTheSpecifiedCoreTypesAndKinds()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System.Windows.Forms\nModule Program\n Sub Main()\n  Console.WriteLine(1)\n" +
            " End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        foreach (var name in new[]
                 {
                     "EventArgs", "Color", "Point", "Size", "Image", "Font",
                     "Control", "Form", "ControlCollection", "ObjectCollection", "Padding",
                     "Button", "Label", "TextBox", "CheckBox", "RadioButton", "Panel", "GroupBox",
                     "ComboBox", "ListBox", "PictureBox", "LinkLabel", "NumericUpDown",
                     "TrackBar", "ProgressBar", "DateTimePicker", "Timer",
                     "ToolStrip", "MenuStrip", "StatusStrip", "ToolStripItem",
                     "ToolStripMenuItem", "ToolStripButton", "ToolStripStatusLabel", "ToolStripSeparator"
                 })
        {
            Assert.That(result.CombinedIR.Classes.ContainsKey(name), Is.True, name);
        }

        Assert.That(result.CombinedIR.Classes.ContainsKey("VgsDockHook"), Is.True);
        Assert.That(result.CombinedIR.Classes["VgsDockHook"].IsExtern, Is.True);
    }

    // ---------------------------------------------------------------- collision

    [Test]
    public void PortableLibraryNameCollision_NamesBothClasses()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System.Windows.Forms\nPublic Class Button\nEnd Class\n" +
            "Module Program\n Sub Main()\n  Console.WriteLine(1)\n End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.False,
            "a program class must not silently shadow the library's Button");
        var messages = result.AllErrors.Select(e => e.Message).ToList();
        Assert.That(messages, Has.Some.Contains("PortableLibraryNameCollision"));
        Assert.That(messages, Has.Some.Contains("Button"));
        Assert.That(messages, Has.Some.Contains("System.Windows.Forms.Button"));
    }

    [Test]
    public void ProgramClassInsideAModule_AlsoCollides()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System.Windows.Forms\nModule Program\n Public Class Button\n End Class\n" +
            " Sub Main()\n  Console.WriteLine(1)\n End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.AllErrors.Select(e => e.Message),
            Has.Some.Contains("PortableLibraryNameCollision"));
    }

    [Test]
    public void AProgramClassWithTheSameName_IsAllowed_WhenTheLibraryIsNotIncluded()
    {
        var result = Compile(Js(), ("Main.bas",
            "Public Class Button\nEnd Class\nModule Program\n Sub Main()\n  Console.WriteLine(1)\n" +
            " End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.CombinedIR.Classes.ContainsKey("Button"), Is.True);
    }

    [Test]
    public void Collision_IsNotRaisedOnCSharp()
    {
        var result = Compile(Cs(), ("Main.bas",
            "Public Class Button\nEnd Class\nModule Program\n Sub Main()\n  Console.WriteLine(1)\n" +
            " End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        Assert.That(result.AllErrors.Select(e => e.Message),
            Has.None.Contains("PortableLibraryNameCollision"));
    }
}
