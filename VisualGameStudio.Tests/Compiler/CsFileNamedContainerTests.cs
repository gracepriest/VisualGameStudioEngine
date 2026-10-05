using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Chip task_e7af351e items 3–4 (portable-controls Task 8). The C# backend puts a file's top-level procedures in a
/// static class named after the FILE. (3) A lambda inside a class is a standalone IR function whose ModuleName is the
/// file's, so a file LoginForm.bas holding Class LoginForm with a lambda emitted an EMPTY <c>static class LoginForm</c>
/// beside the user's class: CS0101. (4) A Sub named like its file became a member named like its enclosing class:
/// CS0542. Only "Main" was special-cased (→ "Program"). A form class lives in a file of its own name, so this is the
/// shared code-behind's shape. Compiled through the project path, optimized, emitted and RUN.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# run redirects Console.Out
public class CsFileNamedContainerTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-csname-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Compiles in the given order AND reversed; both must print the same.</summary>
    private string RunCSharp(params (string Name, string Text)[] files) => RunCSharpWithSource(files).Output;

    private (string Output, string CSharp) RunCSharpWithSource(params (string Name, string Text)[] files)
    {
        string lastCs = "";
        var paths = files.Select(f => { var p = Path.Combine(_dir, f.Name); File.WriteAllText(p, f.Text); return p; }).ToArray();
        string? first = null;
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileProjectFiles(order);
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(result.CombinedIR!);
            var cs = lastCs = new CSharpCodeGenerator().Generate(result.CombinedIR!);
            string output;
            try { output = FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)); }
            catch (System.Exception e) { Assert.Fail($"{e.Message}\n{cs}"); throw; }
            if (first == null) first = output;
            else Assert.That(output, Is.EqualTo(first), "the file order changed the result");
        }
        return (first!, lastCs);
    }

    /// <summary>
    /// ⚠ Two fixes each make this row run: the lambda is no longer grouped into a container, AND a container that
    /// collides is renamed. The second assertion pins the first fix on its own — with only the rename, an EMPTY
    /// <c>static class LoginFormModule</c> would still be emitted for nothing.
    /// </summary>
    [Test]
    public void AClassWithALambda_InAFileOfItsOwnName()
    {
        var (output, cs) = RunCSharpWithSource(
            ("LoginForm.bas", "Public Class LoginForm\n Public Function Run() As Integer\n" +
                              "  Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2\n  Return f(21)\n End Function\nEnd Class\n"),
            ("Main.bas", "Module Program\n Sub Main()\n  Dim l As New LoginForm()\n  PrintLine(l.Run())\n End Sub\nEnd Module\n"));
        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo("42"));
            Assert.That(cs, Does.Not.Match(@"static class LoginForm"), "a lambda's file must not get a container\n" + cs);
        });
    }

    [Test]
    public void ASubNamedLikeItsFile_IsCallable() => Assert.That(RunCSharp(
        ("Greet.bas", "Sub Greet()\n PrintLine(\"hi\")\nEnd Sub\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Greet()\n End Sub\nEnd Module\n")),
        Is.EqualTo("hi"));

    [Test]
    public void AUserClassNamedLikeAFileWithProcedures_KeepsItsName() => Assert.That(RunCSharp(
        ("Tools.bas", "Public Class Tools\n Public Function N() As Integer\n  Return 7\n End Function\nEnd Class\n" +
                      "Sub Helper()\n PrintLine(New Tools().N())\nEnd Sub\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Helper()\n End Sub\nEnd Module\n")),
        Is.EqualTo("7"));

    /// <summary>
    /// The renamed container is spelled the same way by its DECLARATION and by every qualified use from another file —
    /// a global read and a call, each qualified by the backend — so the three cannot disagree.
    /// </summary>
    [Test]
    public void ARenamedContainer_IsReachedByQualifiedName_FromAnotherFile() => Assert.That(RunCSharp(
        ("Score.bas", "Public Class Score\n Public Function Points() As Integer\n  Return 5\n End Function\nEnd Class\n" +
                      "Public Dim Bonus As Integer = 3\n" +
                      "Function Total() As Integer\n Return New Score().Points() + Bonus\nEnd Function\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  PrintLine(Bonus)\n  PrintLine(Total())\n End Sub\nEnd Module\n")),
        Is.EqualTo("3\n8"));
}
