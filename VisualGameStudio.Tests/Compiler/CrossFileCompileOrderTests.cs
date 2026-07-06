using NUnit.Framework;
using BasicLang.Compiler;
using System;
using System.IO;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// BasicCompiler.CompileProjectFiles must be compile-order independent:
/// a cross-file call into a sibling .bas file listed AFTER the caller in the
/// project must resolve the callee's signature (return type included) exactly
/// as it does when the callee is listed first. Regression tests for the
/// order-sensitivity bug where a later-listed sibling contributed no symbols
/// and cross-file calls degraded to 'Object'
/// ("Cannot assign value of type 'Object' to variable of type 'Integer'").
/// </summary>
[TestFixture]
public class CrossFileCompileOrderTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_CrossFileOrder_" + Path.GetRandomFileName());
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

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static CompilationResult Compile(params string[] files)
    {
        // Fresh compiler per compile — the module registry is stateful.
        var compiler = new BasicCompiler();
        return compiler.CompileProjectFiles(files);
    }

    private static void AssertClean(CompilationResult result, string scenario)
    {
        Assert.That(result.AllErrors, Is.Empty,
            $"{scenario}: expected no errors but got: {string.Join("; ", result.AllErrors)}");
        Assert.That(result.Success, Is.True, $"{scenario}: compilation should succeed");
    }

    /// <summary>
    /// Caller listed FIRST calls an Integer-returning function defined in a
    /// sibling listed SECOND. The callee's return type must be visible so
    /// `Dim x As Integer = GetNumber()` compiles clean.
    /// </summary>
    [Test]
    public void CallerListedFirst_CrossFileIntegerReturn_CompilesClean()
    {
        var caller = WriteFile("Program.bas", @"
Sub Main()
    Dim x As Integer = GetNumber()
    Console.WriteLine(x)
End Sub
");
        var callee = WriteFile("MathHelpers.bas", @"
Public Function GetNumber() As Integer
    Return 42
End Function
");

        var result = Compile(caller, callee);

        AssertClean(result, "caller-first project");
    }

    /// <summary>
    /// An explicit Import of the later-listed sibling must work too
    /// (previously the import silently contributed nothing because the
    /// sibling had not compiled yet).
    /// </summary>
    [Test]
    public void CallerListedFirst_WithExplicitImport_CompilesClean()
    {
        var caller = WriteFile("Program.bas", @"Import MathHelpers

Sub Main()
    Dim x As Integer = GetNumber()
    Console.WriteLine(x)
End Sub
");
        var callee = WriteFile("MathHelpers.bas", @"
Public Function GetNumber() As Integer
    Return 42
End Function
");

        var result = Compile(caller, callee);

        AssertClean(result, "caller-first project with explicit Import");
    }

    /// <summary>
    /// Mutual cross-file references: A.bas calls into B.bas AND B.bas calls
    /// into A.bas. No compile order can put both callees first, so this only
    /// works with order-independent symbol visibility.
    /// </summary>
    [Test]
    public void MutualCrossFileReferences_BothDirectionsTyped()
    {
        var fileA = WriteFile("A.bas", @"
Public Function GetGreeting() As String
    Return ""hello""
End Function

Sub Main()
    Dim n As Integer = GetNumberFromB()
    Console.WriteLine(n)
End Sub
");
        var fileB = WriteFile("B.bas", @"
Public Function GetNumberFromB() As Integer
    Return 7
End Function

Public Function DescribeA() As String
    Dim s As String = GetGreeting()
    Return s
End Function
");

        var result = Compile(fileA, fileB);

        AssertClean(result, "mutual A<->B references");
    }

    /// <summary>
    /// A String-returning function from a later-listed sibling used in a
    /// string concatenation — the degraded-to-Object bug also broke `&amp;`.
    /// </summary>
    [Test]
    public void CallerListedFirst_CrossFileStringReturn_ConcatCompilesClean()
    {
        var caller = WriteFile("Program.bas", @"
Sub Main()
    Dim label As String = GetLabel()
    Dim message As String = ""Value: "" & label
    Console.WriteLine(message)
End Sub
");
        var callee = WriteFile("Labels.bas", @"
Public Function GetLabel() As String
    Return ""answer""
End Function
");

        var result = Compile(caller, callee);

        AssertClean(result, "caller-first string concat");
    }

    /// <summary>
    /// Order-independence sanity: the same project must compile identically
    /// (same success, same diagnostics) whichever way the file list is ordered.
    /// </summary>
    [Test]
    public void SameProject_FileListReversed_CompilesIdentically()
    {
        var caller = WriteFile("Program.bas", @"
Sub Main()
    Dim x As Integer = GetNumber()
    Dim s As String = GetLabel()
    Console.WriteLine(s & x)
End Sub
");
        var callee = WriteFile("Helpers.bas", @"
Public Function GetNumber() As Integer
    Return 42
End Function

Public Function GetLabel() As String
    Return ""answer""
End Function
");

        var forward = Compile(caller, callee);
        var reversed = Compile(callee, caller);

        AssertClean(forward, "caller-first order");
        AssertClean(reversed, "callee-first order");
        Assert.That(forward.Success, Is.EqualTo(reversed.Success),
            "success must not depend on file order");
        Assert.That(
            forward.AllErrors.Select(e => e.Message).ToList(),
            Is.EquivalentTo(reversed.AllErrors.Select(e => e.Message).ToList()),
            "diagnostics must not depend on file order");
    }
}
