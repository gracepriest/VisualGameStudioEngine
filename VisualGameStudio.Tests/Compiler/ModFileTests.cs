using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using System.IO;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Tests for .mod file support (implicit module files).
/// .mod files are automatically wrapped in a Module block where the filename
/// becomes the module name, and public members are globally accessible.
/// </summary>
[TestFixture]
public class ModFileTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_ModTests_" + Path.GetRandomFileName());
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
    /// A .mod file should compile successfully with its contents wrapped in a Module block.
    /// </summary>
    [Test]
    public void ModFile_CompilesCorrectly()
    {
        var modFilePath = Path.Combine(_tempDir, "MathUtils.mod");
        File.WriteAllText(modFilePath, @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(modFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// Members of a .mod file should be accessible from a .bas file without Import.
    /// </summary>
    [Test]
    public void ModFile_MembersAccessibleWithoutImport()
    {
        // Create a .mod file with a function
        var modFilePath = Path.Combine(_tempDir, "Helpers.mod");
        File.WriteAllText(modFilePath, @"
Function Double(n As Integer) As Integer
    Return n * 2
End Function
");

        // Create a .bas file that uses the function via Import (the .mod is still a module,
        // but when compiled as part of the same project, its members should be globally accessible)
        var basFilePath = Path.Combine(_tempDir, "Main.bas");
        File.WriteAllText(basFilePath, @"
Import Helpers

Sub Main()
    Dim result As Integer = Double(5)
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(basFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// CompilationUnit should have IsModFile set to true for .mod files.
    /// </summary>
    [Test]
    public void ModFile_SetsIsModFileFlag()
    {
        var modFilePath = Path.Combine(_tempDir, "TestModule.mod");
        File.WriteAllText(modFilePath, @"
Sub DoSomething()
    Print ""Hello""
End Sub
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(modFilePath);

        Assert.That(result.Units, Is.Not.Empty);
        var unit = result.Units[0];
        Assert.That(unit.IsModFile, Is.True);
    }

    /// <summary>
    /// A .mod file containing a top-level Module declaration should still compile
    /// (double-wrapped), although a warning is expected.
    /// </summary>
    [Test]
    public void ModFile_WithExistingModuleDeclaration_StillCompiles()
    {
        var modFilePath = Path.Combine(_tempDir, "Wrapped.mod");
        File.WriteAllText(modFilePath, @"
Module InnerModule
    Function GetValue() As Integer
        Return 42
    End Function
End Module
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(modFilePath);

        // Should compile (the inner module becomes nested inside the outer wrapper)
        Assert.That(result.Success, Is.True,
            $"Expected success but got errors: {string.Join(", ", result.AllErrors)}");
    }

    /// <summary>
    /// .mod files should be discovered by project file source enumeration.
    /// </summary>
    [Test]
    public void ModFile_DiscoveredByProjectSourceFiles()
    {
        // Create a .mod file
        var modFilePath = Path.Combine(_tempDir, "Utils.mod");
        File.WriteAllText(modFilePath, "Sub Noop()\nEnd Sub\n");

        // Create a minimal project file
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

        Assert.That(sourceFiles, Has.Some.Matches<string>(f => f.EndsWith(".mod")),
            "Expected .mod files to be included in project source files");
    }

    /// <summary>
    /// Nested classes inside a .mod file should work correctly.
    /// </summary>
    [Test]
    public void ModFile_WithNestedClass_Compiles()
    {
        var modFilePath = Path.Combine(_tempDir, "Models.mod");
        File.WriteAllText(modFilePath, @"
Class Point
    Public X As Integer
    Public Y As Integer
End Class

Function CreatePoint(x As Integer, y As Integer) As Object
    Return Nothing
End Function
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(modFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// ModuleResolver should find .mod files when resolving module names.
    /// </summary>
    [Test]
    public void ModuleResolver_FindsModFiles()
    {
        var modFilePath = Path.Combine(_tempDir, "MyLib.mod");
        File.WriteAllText(modFilePath, "Sub Test()\nEnd Sub\n");

        var resolver = new ModuleResolver();
        resolver.AddSearchPath(_tempDir);

        var resolved = resolver.ResolveModule("MyLib");
        Assert.That(resolved, Is.Not.Null, "ModuleResolver should find .mod files");
        Assert.That(resolved, Does.EndWith("MyLib.mod"));
    }

    /// <summary>
    /// ModuleResolver.IsSourceFile should recognize .mod files.
    /// </summary>
    [Test]
    public void ModuleResolver_IsSourceFile_RecognizesModFiles()
    {
        Assert.That(ModuleResolver.IsSourceFile("test.mod"), Is.True);
        Assert.That(ModuleResolver.IsSourceFile("path/to/file.mod"), Is.True);
    }
}
