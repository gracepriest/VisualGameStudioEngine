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

    // ------------------------------------------------------------------
    // Explicit Module blocks — what every project template generates
    // (Module Main / Module Helpers), so cross-file calls between module
    // members must be exactly as order-independent as top-level ones.
    // ------------------------------------------------------------------

    private const string ModuleCallerSource = @"Module Caller
    Sub Main()
        Dim x As Integer = GetNumber()
        Console.WriteLine(x)
    End Sub
End Module
";

    private const string ModuleCalleeSource = @"Module Callee
    Public Function GetNumber() As Integer
        Return 42
    End Function
End Module
";

    /// <summary>
    /// Caller module listed FIRST calls an Integer-returning function declared
    /// inside an explicit Module block of a sibling listed SECOND.
    /// </summary>
    [Test]
    public void ModuleBlocks_CallerListedFirst_IntegerReturn_CompilesClean()
    {
        var caller = WriteFile("Caller.bas", ModuleCallerSource);
        var callee = WriteFile("Callee.bas", ModuleCalleeSource);

        var result = Compile(caller, callee);

        AssertClean(result, "module-block caller-first project");
    }

    /// <summary>
    /// Same project with the callee module listed FIRST — pins that
    /// completed-sibling visibility covers explicit module members too.
    /// </summary>
    [Test]
    public void ModuleBlocks_CalleeListedFirst_IntegerReturn_CompilesClean()
    {
        var caller = WriteFile("Caller.bas", ModuleCallerSource);
        var callee = WriteFile("Callee.bas", ModuleCalleeSource);

        var result = Compile(callee, caller);

        AssertClean(result, "module-block callee-first project");
    }

    /// <summary>
    /// Mutual references between two explicit Module blocks: A calls into B
    /// and B calls into A, both assigned to typed variables.
    /// </summary>
    [Test]
    public void ModuleBlocks_MutualReferences_BothDirectionsTyped()
    {
        var fileA = WriteFile("A.bas", @"Module ModA
    Public Function GetGreeting() As String
        Return ""hello""
    End Function

    Sub Main()
        Dim n As Integer = GetNumberFromB()
        Console.WriteLine(n)
    End Sub
End Module
");
        var fileB = WriteFile("B.bas", @"Module ModB
    Public Function GetNumberFromB() As Integer
        Return 7
    End Function

    Public Function DescribeA() As String
        Dim s As String = GetGreeting()
        Return s
    End Function
End Module
");

        var result = Compile(fileA, fileB);

        AssertClean(result, "mutual module-block A<->B references");
    }

    /// <summary>
    /// A String-returning module member from a later-listed sibling.
    /// </summary>
    [Test]
    public void ModuleBlocks_CallerListedFirst_StringReturn_CompilesClean()
    {
        var caller = WriteFile("Program.bas", @"Module Program
    Sub Main()
        Dim label As String = GetLabel()
        Console.WriteLine(""Value: "" & label)
    End Sub
End Module
");
        var callee = WriteFile("Labels.bas", @"Module Labels
    Public Function GetLabel() As String
        Return ""answer""
    End Function
End Module
");

        var result = Compile(caller, callee);

        AssertClean(result, "module-block caller-first string return");
    }

    /// <summary>
    /// Mixed shapes: a top-level caller calling into a sibling's explicit
    /// Module block, and a module-block caller calling a sibling's top-level
    /// function — both with the caller listed first.
    /// </summary>
    [Test]
    public void MixedTopLevelCaller_ModuleBlockCallee_CallerFirst_CompilesClean()
    {
        var caller = WriteFile("Program.bas", @"
Sub Main()
    Dim x As Integer = GetNumber()
    Console.WriteLine(x)
End Sub
");
        var callee = WriteFile("Callee.bas", ModuleCalleeSource);

        var result = Compile(caller, callee);

        AssertClean(result, "top-level caller into module-block callee");
    }

    [Test]
    public void ModuleBlockCaller_TopLevelCallee_CallerFirst_CompilesClean()
    {
        var caller = WriteFile("Caller.bas", ModuleCallerSource);
        var callee = WriteFile("Helpers.bas", @"
Public Function GetNumber() As Integer
    Return 42
End Function
");

        var result = Compile(caller, callee);

        AssertClean(result, "module-block caller into top-level callee");
    }

    /// <summary>
    /// Qualified module-member access (Callee.GetNumber()) must be as
    /// order-independent as unqualified access. The callee-first control pins
    /// today's working behavior; caller-first is the regression case.
    /// </summary>
    [Test]
    public void ModuleBlocks_QualifiedCall_CallerFirst_CompilesClean()
    {
        var caller = WriteFile("Caller.bas", @"Module Caller
    Sub Main()
        Dim x As Integer = Callee.GetNumber()
        Console.WriteLine(x)
    End Sub
End Module
");
        var callee = WriteFile("Callee.bas", ModuleCalleeSource);

        var result = Compile(caller, callee);

        AssertClean(result, "qualified module call, caller-first");
    }

    [Test]
    public void ModuleBlocks_QualifiedCall_CalleeFirst_CompilesClean()
    {
        var caller = WriteFile("Caller.bas", @"Module Caller
    Sub Main()
        Dim x As Integer = Callee.GetNumber()
        Console.WriteLine(x)
    End Sub
End Module
");
        var callee = WriteFile("Callee.bas", ModuleCalleeSource);

        var result = Compile(callee, caller);

        AssertClean(result, "qualified module call, callee-first");
    }

    /// <summary>
    /// Completed-path parity for classes nested inside explicit Module
    /// blocks: the compiled path flattens their method SIGNATURES into the
    /// unit's global scope (pass-1 RegisterDeclarations) and exports them, so
    /// a bare cross-file call to a nested class's method type-checks when the
    /// callee compiles first. The signature path must mirror that exactly —
    /// same result in both orders.
    /// </summary>
    [Test]
    public void ModuleNestedClassMethod_FlattenedSignature_OrderIndependent()
    {
        var caller = WriteFile("Caller.bas", @"Module Caller
    Sub Main()
        Dim n As Integer = NestedValue()
        Console.WriteLine(n)
    End Sub
End Module
");
        var callee = WriteFile("Callee.bas", @"Module Callee
    Class Counter
        Public Function NestedValue() As Integer
            Return 5
        End Function
    End Class
End Module
");

        var forward = Compile(caller, callee);
        var reversed = Compile(callee, caller);

        AssertClean(forward, "nested-class flattened signature, caller-first");
        AssertClean(reversed, "nested-class flattened signature, callee-first");
        Assert.That(
            forward.AllErrors.Select(e => e.Message).ToList(),
            Is.EquivalentTo(reversed.AllErrors.Select(e => e.Message).ToList()),
            "nested-class diagnostics must not depend on file order");
    }

    /// <summary>
    /// Order-independence sanity for explicit module blocks: identical
    /// diagnostics whichever way the file list is ordered.
    /// </summary>
    [Test]
    public void ModuleBlocks_FileListReversed_CompilesIdentically()
    {
        var caller = WriteFile("Caller.bas", ModuleCallerSource);
        var callee = WriteFile("Callee.bas", ModuleCalleeSource);

        var forward = Compile(caller, callee);
        var reversed = Compile(callee, caller);

        AssertClean(forward, "module-block caller-first order");
        AssertClean(reversed, "module-block callee-first order");
        Assert.That(
            forward.AllErrors.Select(e => e.Message).ToList(),
            Is.EquivalentTo(reversed.AllErrors.Select(e => e.Message).ToList()),
            "module-block diagnostics must not depend on file order");
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
