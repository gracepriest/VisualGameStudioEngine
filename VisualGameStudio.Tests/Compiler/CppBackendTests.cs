using System.Diagnostics;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend tests: BasicLang source -> Lexer -> Parser -> SemanticAnalyzer -> IRBuilder -> CppCodeGenerator -> C++ output.
/// Capability diagnostics (CppCapabilityException) are allowed to propagate out of CompileToCpp.
/// </summary>
[TestFixture]
public class CppBackendTests
{
    /// <summary>
    /// Helper: compile BasicLang source to C++ output string.
    /// Returns null and populates errors list if a pipeline stage fails.
    /// CppCapabilityException from the generator propagates to the caller.
    /// </summary>
    private string CompileToCpp(string source, out List<string> errors)
    {
        errors = new List<string>();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        ProgramNode ast;
        try
        {
            ast = parser.Parse();
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
            return null;
        }

        var analyzer = new SemanticAnalyzer();
        bool success = analyzer.Analyze(ast);
        if (!success)
        {
            foreach (var err in analyzer.Errors)
                errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    // ========================================================================
    // Task 1: capability diagnostics - unsupported features are hard errors
    // ========================================================================

    // ========================================================================
    // Task 6: async (Task emulation) + yield (C++20 coroutines)
    // ========================================================================

    [Test]
    public void Cpp_AsyncFunction_EmitsTaskEmulation()
    {
        var source = @"
Async Function GetValue() As Task(Of Integer)
    Return 42
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::Task<int32_t> GetValue()"));
        Assert.That(output, Does.Not.Contain("#warning"));
    }

    [Test]
    public void Cpp_AwaitExpression_UsesTaskGet()
    {
        var source = @"
Async Function GetValue() As Task(Of Integer)
    Return 42
End Function

Async Function Caller() As Task(Of Integer)
    Dim x As Integer = Await GetValue()
    Return x
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain(".get()"));
        Assert.That(output, Does.Not.Contain("#warning"));
    }

    [Test]
    public void Cpp_IteratorYield_EmitsCoroutine()
    {
        var source = @"
Iterator Function Numbers() As IEnumerable(Of Integer)
    Yield 1
    Yield 2
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::Generator<int32_t> Numbers()"));
        Assert.That(output, Does.Contain("co_yield 1"));
        Assert.That(output, Does.Not.Contain("#warning"));
    }

    // ========================================================================
    // Task 5: lambdas -> C++ lambdas
    // ========================================================================

    [Test]
    public void Cpp_LambdaAssignedToVariable_EmitsCppLambda()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    Dim result As Integer = f(5)
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::function<int32_t(int32_t)>"));
        Assert.That(output, Does.Contain("[=](int32_t x)"));
        Assert.That(output, Does.Not.Contain("__lambda_"), "no dangling lambda references:\n" + output);
        Assert.That(output, Does.Contain("f(5)"));
    }

    // ========================================================================
    // Task 2: generics -> real C++ templates
    // ========================================================================

    [Test]
    public void Cpp_TemplateFunction_EmitsCppTemplate()
    {
        var source = @"
Template Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    End If
    Return b
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("template <typename T>"));
        Assert.That(output, Does.Match(@"T\s+Max\(const T& a, const T& b\)"));
    }

    [Test]
    public void Cpp_GenericClass_EmitsCppTemplate()
    {
        var source = @"
Class Pair(Of T)
    Public ItemA As T
    Public ItemB As T
End Class";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("template <typename T>"));
        Assert.That(output, Does.Contain("class Pair"));
        Assert.That(output, Does.Contain("T ItemA;"));
        Assert.That(output, Does.Contain("T ItemB;"));
    }

    [Test]
    public void Cpp_GenericInstantiation_EmitsTemplateArguments()
    {
        var source = @"
Class Pair(Of T)
    Public ItemA As T
End Class

Sub Main()
    Dim p As New Pair(Of Integer)()
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("Pair<int32_t>"));
    }

    [Test]
    public void Cpp_UnmappedNetType_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Dim items As List(Of Integer)
End Sub";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("List"));
    }

    // ========================================================================
    // Task 3: reference semantics - classes are std::shared_ptr, structures stay values
    // ========================================================================

    [Test]
    public void Cpp_ClassInstance_UsesSharedPtr()
    {
        var source = @"
Class Person
    Public Name As String
End Class

Sub Main()
    Dim p As New Person()
    p.Name = ""Alice""
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::shared_ptr<Person>"));
        Assert.That(output, Does.Contain("std::make_shared<Person>("));
        Assert.That(output, Does.Contain("->Name"));
    }

    [Test]
    public void Cpp_ClassMethodCall_UsesArrow()
    {
        var source = @"
Class Counter
    Private _n As Integer
    Public Sub Increment()
        _n = _n + 1
    End Sub
End Class

Sub Main()
    Dim c As New Counter()
    c.Increment()
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("c->Increment()"));
    }

    [Test]
    public void Cpp_Structure_ValueSemantics()
    {
        var source = @"
Structure Point
    Public X As Integer
    Public Y As Integer
End Structure

Sub Main()
    Dim p As Point
    p.X = 1
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("struct Point"));
        Assert.That(output, Does.Contain("int32_t X;"));
        Assert.That(output, Does.Not.Contain("shared_ptr<Point>"));
        Assert.That(output, Does.Contain("p.X = 1"));
    }

    // ========================================================================
    // Task 4: throw + finally + exception type mapping
    // ========================================================================

    [Test]
    public void Cpp_ThrowStatement_EmitsCppThrow()
    {
        var source = @"
Sub Fail()
    Throw New Exception(""boom"")
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("throw std::runtime_error("));
        Assert.That(output, Does.Contain("boom"));
    }

    [Test]
    public void Cpp_TryCatchTyped_MapsExceptionType()
    {
        var source = @"
Sub Main()
    Try
        Dim x As Integer = 1
    Catch ex As Exception
        Dim y As Integer = 2
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("catch (const std::exception& ex)"));
    }

    [Test]
    public void Cpp_TryFinally_EmitsFinallyOnBothPaths()
    {
        var source = @"
Sub Main()
    Dim n As Integer = 0
    Try
        n = 1
    Finally
        n = 2
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // exceptional path: catch(...) { finally; throw; }
        Assert.That(output, Does.Contain("catch (...)"));
        Assert.That(output, Does.Contain("throw;"));
        // finally body appears on both the exceptional and the normal path
        Assert.That(CountOccurrences(output, "n = 2;"), Is.EqualTo(2),
            "finally body should be emitted twice (exceptional + normal path):\n" + output);
    }

    // ========================================================================
    // App-readiness regressions: bugs found by compiling the reaww/rewrwwq
    // user projects (game-app + console-app templates on the Cpp backend).
    // ========================================================================

    [Test]
    public void Cpp_FrameworkCallResult_IsAssignedToItsTemp()
    {
        // Regression: `If IsKeyDown(87) Then` emitted the call as a bare
        // statement (`Framework_IsKeyDown(87);`) and then branched on an
        // UNINITIALIZED temp (`if (t0)`). The stdlib/framework/extern call
        // paths dropped temp destinations that the regular-call path kept.
        var source = @"
Sub Poll()
    If IsKeyDown(87) Then
        PrintLine(""w"")
    End If
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Match(@"t\d+ = Framework_IsKeyDown\(87\);"),
            "framework call result must be assigned to the temp the branch tests:\n" + output);
    }

    [Test]
    public void Cpp_FieldCompoundAssignment_StoresBack()
    {
        // Regression: `Y = Y - Speed` in a class method emitted only the
        // subtraction into a temp (`t0 = Y - Speed;`) and never stored the
        // result back to the field — movement code silently did nothing.
        var source = @"
Class Player
    Public Y As Single
    Public Speed As Single

    Public Sub Update()
        Y = Y - Speed
    End Sub
End Class";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Match(@"(this->)?Y = (Y - Speed|t\d+);"),
            "the computed value must be stored back to the field:\n" + output);
    }

    [Test]
    public void Cpp_StringArgToFrameworkCall_UsesCStr()
    {
        // Regression: `DrawText(name, ...)` passed a std::string where the
        // extern ""C"" export takes const char* — the generated file cannot
        // compile. Non-literal string args must be marshaled with .c_str().
        var source = @"
Sub Draw(name As String)
    DrawText(name, 10, 10, 20, 255, 255, 255, 255)
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("Framework_DrawText(name.c_str()"),
            "std::string args to extern C exports must be marshaled via .c_str():\n" + output);
    }

    [Test]
    public void Cpp_CrossModuleCall_MatchesEmittedDefinitionName()
    {
        // Regression: the console template linked Main.bas against Helpers.mod;
        // call sites emitted `HelpersPrintHeader(...)` while the definition was
        // emitted as `PrintHeader(...)` — every multi-module program failed to
        // link with undefined symbols.
        var source = @"
Module Main
    Sub Main()
        PrintHeader(""x"")
    End Sub
End Module

Module Helpers
    Public Sub PrintHeader(title As String)
        PrintLine(title)
    End Sub
End Module";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        // Whatever name the call site uses must have a matching definition.
        var callsQualified = output.Contains("HelpersPrintHeader(");
        var definesQualified = output.Contains("void HelpersPrintHeader(");
        var definesPlain = output.Contains("void PrintHeader(");

        if (callsQualified)
            Assert.That(definesQualified, Is.True,
                "call site uses the module-qualified name but no such definition exists:\n" + output);
        else
            Assert.That(definesPlain, Is.True,
                "plain call name must have a plain definition:\n" + output);
    }

    [Test]
    public void Cpp_MultiFileProject_CrossFileCall_MatchesEmittedDefinitionName()
    {
        // Regression (console-app template, Main.bas + Helpers.mod): the
        // multi-file pipeline emitted call sites as `HelpersPrintHeader(...)`
        // while the definition stayed `PrintHeader(...)` — undefined symbols
        // at link time for every multi-file C++ project.
        var dir = Path.Combine(Path.GetTempPath(), "blcpp-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"),
@"Module Main
    Sub Main()
        PrintHeader(""x"")
    End Sub
End Module
");
            File.WriteAllText(Path.Combine(dir, "Helpers.mod"),
@"Public Sub PrintHeader(title As String)
    PrintLine(title)
End Sub
");

            var compiler = new BasicCompiler();
            var result = compiler.CompileProjectFiles(new[]
            {
                Path.Combine(dir, "Main.bas"),
                Path.Combine(dir, "Helpers.mod")
            });

            Assert.That(result.AllErrors, Is.Empty,
                "multi-file compile failed: " + string.Join("; ", result.AllErrors));
            Assert.That(result.CombinedIR, Is.Not.Null);

            var output = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                .Generate(result.CombinedIR);

            // Whatever name the call site uses must have a matching definition.
            var callsQualified = output.Contains("HelpersPrintHeader(");
            var definesQualified = output.Contains("void HelpersPrintHeader(");
            var definesPlain = output.Contains("void PrintHeader(");

            if (callsQualified)
                Assert.That(definesQualified, Is.True,
                    "cross-file call uses the module-qualified name but no such definition exists:\n" + output);
            else
                Assert.That(definesPlain, Is.True,
                    "plain call name must have a plain definition:\n" + output);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Cpp_ConsoleTemplateSurface_LowersToValidCpp()
    {
        // Regression (console-app template): Console.WriteLine, DateTime.Now,
        // .ToString(fmt), String.Length and New String(c, n) emitted garbage —
        // `DateTime->Now` on nothing, calls to a nonexistent ConsoleWriteLine,
        // .NET properties on std::string. The template surface must lower to
        // real C++ (cout, BasicLangRt time helpers, .length(), std::string(n,c)).
        var source = @"
Using System

Module Main
    Sub Main()
        PrintHeader(""Demo"")
        Console.WriteLine(FormatMessage(""Hello""))
        Console.WriteLine()
        Console.WriteLine(""Time: "" & DateTime.Now.ToString())
    End Sub

    Public Function FormatMessage(message As String) As String
        Return ""["" & DateTime.Now.ToString(""HH:mm:ss"") & ""] "" & message
    End Function

    Public Sub PrintHeader(title As String)
        Dim separator As String = New String(""=""c, title.Length + 4)
        Console.WriteLine(separator)
    End Sub
End Module";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        // The known-garbage forms must be gone...
        Assert.That(output, Does.Not.Contain("DateTime->"), "DateTime.Now emitted as member access on nothing:\n" + output);
        Assert.That(output, Does.Not.Contain("ConsoleWriteLine"), "Console.WriteLine emitted as a phantom function:\n" + output);
        Assert.That(output, Does.Not.Contain(".Length"), ".NET Length property leaked into C++:\n" + output);
        Assert.That(output, Does.Not.Contain(".ToString"), ".NET ToString leaked into C++:\n" + output);

        // ...replaced by real C++.
        Assert.That(output, Does.Contain("cout"), "Console.WriteLine must lower to cout:\n" + output);
        Assert.That(output, Does.Contain("BasicLangRt::Now()"), "DateTime.Now must lower to the runtime helper:\n" + output);
        Assert.That(output, Does.Contain("BasicLangRt::FormatTime"), "DateTime ToString must lower to the runtime helper:\n" + output);
        Assert.That(output, Does.Match(@"std::string\(\s*t\d+,\s*'='\)"), "New String(c, n) must lower to std::string(n, c):\n" + output);
        Assert.That(output, Does.Contain(".length()"), "String.Length must lower to .length():\n" + output);
    }

    // ========================================================================
    // Task 7: end-to-end - generated C++ must be accepted by a real C++ compiler
    // ========================================================================

    [Test]
    public void Cpp_EndToEnd_GeneratedCodeIsValidCpp()
    {
        // One representative program exercising templates, shared_ptr classes,
        // throw/catch/finally, lambdas, async Task emulation, and coroutine yield.
        var source = @"
Structure Vec2
    Public X As Integer
    Public Y As Integer
End Structure

Class Pair(Of T)
    Public ItemA As T
    Public ItemB As T
End Class

Class Person
    Public Name As String
    Public Sub Rename(newName As String)
        Name = newName
    End Sub
End Class

Template Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    End If
    Return b
End Function

Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Iterator Function Numbers() As IEnumerable(Of Integer)
    Yield 1
    Yield 2
End Function

Async Function GetValueAsync() As Task(Of Integer)
    Return 42
End Function

Async Function CallerAsync() As Task(Of Integer)
    Dim x As Integer = Await GetValueAsync()
    Return x
End Function

Sub Main()
    Dim v As Vec2
    v.X = 3
    v.Y = 4
    Dim p As New Person()
    p.Rename(""Alice"")
    Dim pair As New Pair(Of Integer)()
    pair.ItemA = 1
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    Dim doubled As Integer = f(21)
    Dim total As Integer = Add(doubled, 0)
    For Each i In Numbers()
        total = total + i
    Next
    Dim n As Integer = 0
    Try
        Throw New Exception(""boom"")
    Catch ex As Exception
        n = 1
    Finally
        n = 2
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);

        var compiler = FindCppCompiler();
        if (compiler == null)
            Assert.Ignore("No C++ compiler available (clang++/g++/MSVC) - structural assertions only");

        var tmp = Path.Combine(Path.GetTempPath(), $"blcpp_{Guid.NewGuid():N}.cpp");
        File.WriteAllText(tmp, output);
        try
        {
            var (exe, argsTemplate) = compiler.Value;
            var psi = new ProcessStartInfo(exe, string.Format(argsTemplate, tmp))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120000);

            Assert.That(proc.ExitCode, Is.EqualTo(0),
                $"generated C++ failed to compile:\n{stdout}\n{stderr}\n--- generated code ---\n{output}");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>Probe for a C++ compiler: clang++/g++ on PATH, then MSVC via vswhere +
    /// vcvars64.bat. Returns (executable, args template with {0} = source path).</summary>
    private static (string exe, string argsTemplate)? FindCppCompiler()
    {
        foreach (var (exe, args) in new[]
        {
            ("clang++", "-std=c++20 -fsyntax-only \"{0}\""),
            ("g++", "-std=c++20 -fsyntax-only \"{0}\"")
        })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(exe, "--version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                probe.WaitForExit(10000);
                if (probe.ExitCode == 0) return (exe, args);
            }
            catch { /* not on PATH */ }
        }

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(vswhere,
                    "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                var installPath = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(15000);
                if (!string.IsNullOrEmpty(installPath))
                {
                    var vcvars = Path.Combine(installPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(vcvars))
                        return ("cmd.exe", "/s /c \"\"" + vcvars + "\" >nul && cl /nologo /std:c++20 /EHsc /Zs \"{0}\"\"");
                }
            }
            catch { /* vswhere probe failed */ }
        }

        return null;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Test]
    public void Cpp_PlainProceduralCode_StillCompiles()
    {
        var source = @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int32_t Add(int32_t a, int32_t b)"));
    }

    [Test]
    public void Cpp_ClassWithFieldsAndMethods_StillCompiles()
    {
        var source = @"
Class Counter
    Private _n As Integer
    Public Sub Increment()
        _n = _n + 1
    End Sub
End Class";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("class Counter"));
    }
}
