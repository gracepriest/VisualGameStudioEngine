using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using System;
using System.IO;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Tests for .cls/.class file support (implicit class files).
/// .cls/.class files are automatically wrapped in a Class block where the filename
/// becomes the class name. Private by default; use "Public" on the first line for global access.
/// Other files must use "Import ClassName" to access private .cls classes.
/// </summary>
[TestFixture]
public class ClassFileTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_ClsTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// A .cls file should compile successfully with its contents wrapped in a Class block.
    /// </summary>
    [Test]
    public void ClsFile_CompilesCorrectly()
    {
        var clsFilePath = Path.Combine(_tempDir, "Player.cls");
        File.WriteAllText(clsFilePath, @"
Public Name As String
Public Score As Integer

Sub New(name As String)
    Me.Name = name
    Me.Score = 0
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// A .class file should compile the same as a .cls file.
    /// </summary>
    [Test]
    public void ClassFile_CompilesCorrectly()
    {
        var classFilePath = Path.Combine(_tempDir, "Enemy.class");
        File.WriteAllText(classFilePath, @"
Public Health As Integer

Sub New()
    Me.Health = 100
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(classFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// CompilationUnit should have IsClassFile set to true for .cls files.
    /// </summary>
    [Test]
    public void ClsFile_SetsIsClassFileFlag()
    {
        var clsFilePath = Path.Combine(_tempDir, "TestClass.cls");
        File.WriteAllText(clsFilePath, @"
Public Value As Integer
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.Units, Is.Not.Empty);
        var unit = result.Units[0];
        Assert.That(unit.IsClassFile, Is.True);
    }

    /// <summary>
    /// A private .cls class (default) should require Import to be accessed from another file.
    /// </summary>
    [Test]
    public void ClsFile_PrivateClass_RequiresImport()
    {
        // Create a private .cls file (no "Public" on first line)
        var clsFilePath = Path.Combine(_tempDir, "Helper.cls");
        File.WriteAllText(clsFilePath, @"
Public Value As Integer

Sub New()
    Me.Value = 42
End Sub
");

        // Create a .bas file that uses Import to access the class
        var basFilePath = Path.Combine(_tempDir, "Main.bas");
        File.WriteAllText(basFilePath, @"
Import Helper

Sub Main()
    Dim h As Helper = New Helper()
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(basFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// A public .cls class should be accessible without Import.
    /// The first line of the file must be "Public" on its own line.
    /// </summary>
    [Test]
    public void ClsFile_PublicClass_AccessibleWithoutImport()
    {
        // Create a public .cls file
        var clsFilePath = Path.Combine(_tempDir, "GlobalHelper.cls");
        File.WriteAllText(clsFilePath, @"Public
Public Value As Integer

Sub New()
    Me.Value = 99
End Sub
");

        // The public class should compile with Public access modifier
        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);

        // Verify the class has Public access in the AST
        Assert.That(result.Units, Is.Not.Empty);
        var unit = result.Units[0];
        Assert.That(unit.AST, Is.Not.Null);
        var classNode = unit.AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null, "Expected a ClassNode in the AST");
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public));
    }

    /// <summary>
    /// Nested classes inside a .cls file should work correctly.
    /// </summary>
    [Test]
    public void ClsFile_WithNestedClass_Compiles()
    {
        var clsFilePath = Path.Combine(_tempDir, "Container.cls");
        File.WriteAllText(clsFilePath, @"
Class InnerItem
    Public Name As String
End Class

Public Items As Object

Sub New()
    Me.Items = Nothing
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// A field of a class defined in a sibling .cls file must resolve to its
    /// declared type across files — even when the instance variable is named
    /// like the class (player vs Player). Regression for the game template:
    /// player.Name typed as Object and player was misread as a module.
    /// </summary>
    [Test]
    public void CrossFile_InstanceFieldAccess_ResolvesFieldType()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Player.cls"), @"Public
Public Name As String

Public Sub New(n As String)
    Me.Name = n
End Sub
");
        var mainPath = Path.Combine(_tempDir, "Main.bas");
        File.WriteAllText(mainPath, @"Module Main
    Sub Main()
        Dim player As New Player(""Hero"")
        Dim s As String = player.Name
    End Sub
End Module
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileProjectFiles(new[]
        {
            mainPath,
            Path.Combine(_tempDir, "Player.cls")
        });

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// ModuleResolver should find .cls files when resolving module names.
    /// </summary>
    [Test]
    public void ModuleResolver_FindsClsFiles()
    {
        var clsFilePath = Path.Combine(_tempDir, "MyClass.cls");
        File.WriteAllText(clsFilePath, "Public Value As Integer\n");

        var resolver = new ModuleResolver();
        resolver.AddSearchPath(_tempDir);

        var resolved = resolver.ResolveModule("MyClass");
        Assert.That(resolved, Is.Not.Null, "ModuleResolver should find .cls files");
        Assert.That(resolved, Does.EndWith("MyClass.cls"));
    }

    /// <summary>
    /// ModuleResolver should find .class files when resolving module names.
    /// </summary>
    [Test]
    public void ModuleResolver_FindsClassFiles()
    {
        var classFilePath = Path.Combine(_tempDir, "MyEntity.class");
        File.WriteAllText(classFilePath, "Public Id As Integer\n");

        var resolver = new ModuleResolver();
        resolver.AddSearchPath(_tempDir);

        var resolved = resolver.ResolveModule("MyEntity");
        Assert.That(resolved, Is.Not.Null, "ModuleResolver should find .class files");
        Assert.That(resolved, Does.EndWith("MyEntity.class"));
    }

    /// <summary>
    /// ModuleResolver.IsSourceFile should recognize .cls and .class files.
    /// </summary>
    [Test]
    public void ModuleResolver_IsSourceFile_RecognizesClassFiles()
    {
        Assert.That(ModuleResolver.IsSourceFile("test.cls"), Is.True);
        Assert.That(ModuleResolver.IsSourceFile("path/to/file.cls"), Is.True);
        Assert.That(ModuleResolver.IsSourceFile("test.class"), Is.True);
        Assert.That(ModuleResolver.IsSourceFile("path/to/file.class"), Is.True);
    }

    /// <summary>
    /// ModuleResolver.IsClassFile should correctly identify .cls and .class files.
    /// </summary>
    [Test]
    public void ModuleResolver_IsClassFile_Works()
    {
        Assert.That(ModuleResolver.IsClassFile("Player.cls"), Is.True);
        Assert.That(ModuleResolver.IsClassFile("Enemy.class"), Is.True);
        Assert.That(ModuleResolver.IsClassFile("Main.bas"), Is.False);
        Assert.That(ModuleResolver.IsClassFile("Utils.mod"), Is.False);
    }

    /// <summary>
    /// .cls files should be discovered by project file source enumeration.
    /// </summary>
    [Test]
    public void ClsFile_DiscoveredByProjectSourceFiles()
    {
        var clsFilePath = Path.Combine(_tempDir, "Widget.cls");
        File.WriteAllText(clsFilePath, "Public Value As Integer\n");

        var classFilePath = Path.Combine(_tempDir, "Gadget.class");
        File.WriteAllText(classFilePath, "Public Id As Integer\n");

        var projectPath = Path.Combine(_tempDir, "Test.blproj");
        File.WriteAllText(projectPath, @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project>
  <PropertyGroup>
    <Name>Test</Name>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>");

        var project = BasicLang.Compiler.ProjectSystem.ProjectFile.Load(projectPath);
        var sourceFiles = project.GetSourceFiles().ToList();

        Assert.That(sourceFiles, Has.Some.Matches<string>(f => f.EndsWith(".cls")),
            "Expected .cls files to be included in project source files");
        Assert.That(sourceFiles, Has.Some.Matches<string>(f => f.EndsWith(".class")),
            "Expected .class files to be included in project source files");
    }

    /// <summary>
    /// A .cls file without "Public" on the first line should default to Private access.
    /// </summary>
    [Test]
    public void ClsFile_DefaultsToPrivateAccess()
    {
        var clsFilePath = Path.Combine(_tempDir, "PrivateClass.cls");
        File.WriteAllText(clsFilePath, @"
Public Value As Integer
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.Success, Is.True,
            $"Expected success but got errors: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Units, Is.Not.Empty);
        var unit = result.Units[0];
        var classNode = unit.AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null);
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Private));
    }

    /// <summary>
    /// A .cls file with "Public Sub" on the first line should NOT be treated as a public class.
    /// Only "Public" alone on its own line triggers public access.
    /// </summary>
    [Test]
    public void ClsFile_PublicSubOnFirstLine_StaysPrivate()
    {
        var clsFilePath = Path.Combine(_tempDir, "NotPublic.cls");
        File.WriteAllText(clsFilePath, @"Public Sub DoWork()
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.Success, Is.True,
            $"Expected success but got errors: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Units, Is.Not.Empty);
        var unit = result.Units[0];
        var classNode = unit.AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null);
        // Should be Private because "Public Sub" is not the same as just "Public"
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Private));
    }

    /// <summary>
    /// "Option Public" as the first code line makes the implicit class public.
    /// </summary>
    [Test]
    public void OptionPublic_MakesClassPublic()
    {
        var clsFilePath = Path.Combine(_tempDir, "GlobalThing.cls");
        File.WriteAllText(clsFilePath, @"Option Public
Public Value As Integer

Sub New()
    Me.Value = 7
End Sub
");
        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
        var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null, "Expected a ClassNode in the AST");
        Assert.That(classNode.Name, Is.EqualTo("GlobalThing"));
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public));
    }

    /// <summary>
    /// The directive is honored below leading blank lines and comment lines
    /// (both ' and Rem forms) — unlike the legacy bare "Public" marker.
    /// </summary>
    [Test]
    public void OptionPublic_AfterLeadingComments_Works()
    {
        var clsFilePath = Path.Combine(_tempDir, "Banner.cls");
        File.WriteAllText(clsFilePath, @"' File header banner
Rem legacy-style comment

Option Public
Public Value As Integer
");
        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null);
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public));
    }

    /// <summary>
    /// The directive is case-insensitive, like all BasicLang keywords.
    /// </summary>
    [Test]
    public void OptionPublic_IsCaseInsensitive()
    {
        foreach (var variant in new[] { "OPTION PUBLIC", "option public", "Option public" })
        {
            var clsFilePath = Path.Combine(_tempDir, "Case" + variant.GetHashCode().ToString("X") + ".cls");
            File.WriteAllText(clsFilePath, variant + "\nPublic Value As Integer\n");

            var compiler = new BasicCompiler();
            var result = compiler.CompileFile(clsFilePath);

            Assert.That(result.AllErrors, Is.Empty,
                $"Variant '{variant}' failed: {string.Join(", ", result.AllErrors)}");
            var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
            Assert.That(classNode, Is.Not.Null, $"Variant '{variant}' did not produce a ClassNode");
            Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public),
                $"Variant '{variant}' did not produce a public class");
        }
    }

    /// <summary>
    /// A .cls class made public via Option Public is usable from a sibling file
    /// with no Import statement (project-files compilation).
    /// </summary>
    [Test]
    public void OptionPublic_CrossFile_NoImportNeeded()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Enemy.cls"), @"Option Public
Public Name As String

Public Sub New(n As String)
    Me.Name = n
End Sub
");
        var mainPath = Path.Combine(_tempDir, "Main.bas");
        File.WriteAllText(mainPath, @"Module Main
    Sub Main()
        Dim e As New Enemy(""Slime"")
        Dim s As String = e.Name
    End Sub
End Module
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileProjectFiles(new[]
        {
            mainPath,
            Path.Combine(_tempDir, "Enemy.cls")
        });

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// The directive line is replaced in place by the class header, so every
    /// line keeps its original number and LineOffset is 0 (diagnostics and the
    /// debugger SourceMapper need no adjustment).
    /// </summary>
    [Test]
    public void OptionPublic_PreservesLineNumbers()
    {
        var clsFilePath = Path.Combine(_tempDir, "LineCheck.cls");
        File.WriteAllText(clsFilePath, "' banner\r\nOption Public\r\nPublic Value As Integer\r\n");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        var unit = result.Units[0];
        Assert.That(unit.LineOffset, Is.EqualTo(0));

        var lines = unit.SourceCode.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.That(lines[0], Is.EqualTo("' banner"), "comment must stay on line 1");
        Assert.That(lines[1], Is.EqualTo("Public Class LineCheck"), "directive line replaced in place");
        Assert.That(lines[2], Is.EqualTo("Public Value As Integer"), "body lines must not shift");
    }

    /// <summary>
    /// The legacy bare "Public" first line still compiles as a public class,
    /// but emits a deprecation warning on stderr pointing at Option Public.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void BarePublic_StillWorks_AndWarns()
    {
        var clsFilePath = Path.Combine(_tempDir, "OldStyle.cls");
        File.WriteAllText(clsFilePath, @"Public
Public Value As Integer
");
        var originalError = Console.Error;
        var captured = new StringWriter();
        CompilationResult result;
        try
        {
            Console.SetError(captured);
            var compiler = new BasicCompiler();
            result = compiler.CompileFile(clsFilePath);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public),
            "bare Public must keep working");
        Assert.That(captured.ToString(), Does.Contain("deprecated"));
        Assert.That(captured.ToString(), Does.Contain("Option Public"));
    }
}
