using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Names that cross a FILE boundary in a multi-file project, compiled through the project path
/// (<see cref="BasicCompiler.CompileProjectFiles"/>, which the CLI and the IDE both reach), put
/// through the standard optimizer, and RUN on C#, JavaScript and C++. Each case was measured
/// broken on master 73db87b9 (2026-09-29) with a green build or a misleading refusal.
///
/// <para>⛔ <b>A Module BLOCK named unlike its file was unreachable by qualified name from another
/// file.</b> <c>Module Program</c> (holding <c>Main</c> and <c>Helper</c>) in <c>Main.bas</c>, and
/// <c>Program.Helper()</c> in a class in <c>Worker.bas</c>: the qualified-call channel looked
/// <c>Program</c> up only as a FILE unit, found none, and let the access fall to the permissive
/// path — <c>ReferenceError: Program is not defined</c> on JavaScript, <c>C2065</c> on C++,
/// <c>CS0103</c> on C#. A Module in a file OF THE SAME NAME worked, which is why the report did not
/// reproduce for everyone. And the BARE call imported the procedure without its owning Module, so
/// C# qualified it with the FILE's container (<c>Program.Go()</c> for a Module named <c>M</c>).</para>
///
/// <para>⛔ <b><c>Dim c As New C()</c> with <c>C</c> in a sibling file was refused</b> ("Cannot
/// assign value of type 'Object' to variable of type 'C'"). The local is defined before its
/// initializer is analyzed and scope lookup is case-insensitive, so the initializer's <c>C</c> found
/// the VARIABLE and never reached the sibling's class symbol. Two-letter and longer names were
/// rescued by the "any PascalCase name could be .NET" fallback, as a member-less phantom.</para>
///
/// <para>⛔ <b>A member that does not exist on a user class compiled silently</b> whenever the
/// class name was two letters or more — the same fallback claimed the receiver as a .NET type — and
/// JavaScript, which has no csc behind it, died at load with <c>TypeError: this.X is not a
/// function</c>. Not a cross-file defect at all: the one-file shape was rejected only because the
/// probe named its class <c>F</c>.</para>
///
/// <para>⚠ Not in the JavaScript execution roster: the fixture name matches none of its patterns,
/// and it runs three backends rather than being a JavaScript tier fixture.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CrossFileBindingTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() =>
        _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "BasicLang_CrossFile_" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string[] Write(params (string Name, string Text)[] files) =>
        files.Select(f =>
        {
            var path = Path.Combine(_dir, f.Name);
            File.WriteAllText(path, f.Text);
            return path;
        }).ToArray();

    private static IRModule Optimized(IRModule ir)
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(ir);
        return ir;
    }

    private static CompilationResult Compile(string[] paths) => new BasicCompiler().CompileProjectFiles(paths);

    private static string Messages(CompilationResult r) => string.Join(" | ", r.AllErrors.Select(e => e.Message));

    /// <summary>Compiles the files in the given order AND reversed, and runs each on three backends.</summary>
    private void RunsOnEveryBackend(string expected, params (string Name, string Text)[] files)
    {
        var paths = Write(files);
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var label = string.Join(",", order.Select(Path.GetFileName));
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.False, $"[{label}] {Messages(result)}");
            var ir = Optimized(result.CombinedIR!);

            Assert.Multiple(() =>
            {
                var cs = new CSharpCodeGenerator().Generate(ir);
                Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo(expected), $"C# [{label}]\n{cs}");

                var js = new JavaScriptCodeGenerator().Generate(ir);
                Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo(expected), $"JavaScript [{label}]\n{js}");
            });

            // Outside Assert.Multiple: CompileRun IGNORES when there is no C++ compiler.
            var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir);
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(cpp)), Is.EqualTo(expected), $"C++ [{label}]");
        }
    }

    // ------------------------------------------------------------------ item 1: Module blocks

    private const string ProgramWithMainAndHelper =
        "Module Program\n Sub Main()\n  Dim w As New Worker()\n  w.Run()\n End Sub\n" +
        " Sub Helper()\n  PrintLine(\"HELPER RAN\")\n End Sub\nEnd Module\n";

    /// <summary>⛔ The reported shape: a class's method calls the entry module's procedure, qualified.</summary>
    [Test]
    public void AClassInAnotherFile_CallsTheEntryModulesProcedure_Qualified() => RunsOnEveryBackend("HELPER RAN",
        ("Main.bas", ProgramWithMainAndHelper),
        ("Worker.bas", "Public Class Worker\n Public Sub Run()\n  Program.Helper()\n End Sub\nEnd Class\n"));

    /// <summary>⛔ Ran on JavaScript and C++; C# emitted <c>Program.Go()</c> — the FILE's container.</summary>
    [Test]
    public void AClassInAnotherFile_CallsAModuleBlocksProcedure_Bare() => RunsOnEveryBackend("GO RAN",
        ("Main.bas", "Module M\n Sub Main()\n  Dim w As New Worker()\n  w.Run()\n End Sub\n Sub Go()\n  PrintLine(\"GO RAN\")\n End Sub\nEnd Module\n"),
        ("Worker.bas", "Public Class Worker\n Public Sub Run()\n  Go()\n End Sub\nEnd Class\n"));

    /// <summary>⛔ A Module in a file named differently (<c>Helpers.bas</c> holds <c>Module M</c>).</summary>
    [Test]
    public void AModuleBlockNamedUnlikeItsFile_IsCallableQualified_FromAnotherFile() => RunsOnEveryBackend("GO RAN\n42",
        ("Helpers.bas", "Module M\n Sub Go()\n  PrintLine(\"GO RAN\")\n End Sub\n Function Answer() As Integer\n  Return 42\n End Function\nEnd Module\n"),
        ("Main.bas", "Sub Main()\n M.Go()\n PrintLine(CStr(M.Answer() + 0))\nEnd Sub\n"));

    /// <summary>
    /// A lambda inside the class is the same channel and must not regress.
    /// ⚠ The file is deliberately NOT named after the class: a class holding a lambda in a file of
    /// its own name makes C# emit an empty <c>static class Worker</c> beside it (CS0101) — a separate,
    /// pre-existing defect that has nothing to do with the call.
    /// </summary>
    [Test]
    public void AQualifiedModuleCall_InsideALambda_InAnotherFile() => RunsOnEveryBackend("HELPER RAN",
        ("Main.bas", ProgramWithMainAndHelper),
        ("Workers.bas", "Public Class Worker\n Public Sub Run()\n  Dim a As Action = Sub() Program.Helper()\n  a()\n End Sub\nEnd Class\n"));

    /// <summary>
    /// ⛔ The CLI entry point, as the report came in: a JavaScript PROJECT built by the real
    /// <c>BasicLang.exe build</c> and RUN under node. Also carries the one-file-named-after-its-class
    /// sibling (<c>C.bas</c>) and a same-named local, so the three fixes meet in one real build.
    /// </summary>
    [Test]
    public void TheCli_BuildsAndRunsAJavaScriptProject_ThatCrossesFiles()
    {
        Write(
            ("App.blproj",
                "<BasicLangProject Version=\"1.0\">\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n" +
                "    <OutputType>Exe</OutputType>\n    <TargetBackend>JavaScript</TargetBackend>\n  </PropertyGroup>\n</BasicLangProject>\n"),
            ("Main.bas",
                "Module Program\n Sub Main()\n  Dim c As New C()\n  c.Run()\n End Sub\n" +
                " Sub Helper()\n  Console.WriteLine(\"HELPER RAN\")\n End Sub\nEnd Module\n"),
            ("C.bas", "Public Class C\n Public Sub Run()\n  Program.Helper()\n End Sub\nEnd Class\n"));

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the CLI refused the project\n{stdout}\n{stderr}");

        var script = Path.Combine(_dir, "bin", "Debug", "net8.0", "App.js");
        Assert.That(File.Exists(script), Is.True, stdout);

        (int, string, string) ran;
        try
        {
            ran = CliTestHarness.RunProcess("node", new[] { script }, _dir, timeoutMs: 60_000);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Assert.Ignore($"node could not be started ({e.Message})");
            return;
        }
        Assert.That(ran.Item2 + ran.Item3, Does.Contain("HELPER RAN").And.Not.Contain("ReferenceError"),
            "a green build is not a running page");
    }

    /// <summary>A member the Module does not have is refused by name, not left to the fallback.</summary>
    [Test]
    public void AQualifiedCall_ToAMemberTheModuleBlockLacks_IsRefused()
    {
        var paths = Write(
            ("Main.bas", ProgramWithMainAndHelper),
            ("Worker.bas", "Public Class Worker\n Public Sub Run()\n  Program.Missing()\n End Sub\nEnd Class\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.True, "a call to a member Program does not have must not build");
            Assert.That(Messages(result), Does.Contain("Missing"));
        }
    }

    /// <summary>A Private procedure of the Module is refused across files, as it is in one file.</summary>
    [Test]
    public void AQualifiedCall_ToAPrivateProcedureOfAModuleBlock_IsRefused()
    {
        var paths = Write(
            ("Main.bas", "Module Program\n Sub Main()\n  Dim w As New Worker()\n  w.Run()\n End Sub\n Private Sub Hidden()\n End Sub\nEnd Module\n"),
            ("Worker.bas", "Public Class Worker\n Public Sub Run()\n  Program.Hidden()\n End Sub\nEnd Class\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(Messages(result), Does.Contain("Private"), string.Join(",", order.Select(Path.GetFileName)));
        }
    }

    // ------------------------------------------------------------------ item 3: sibling class, same-named local

    /// <summary>⛔ Was "Cannot assign value of type 'Object' to variable of type 'C'" on every backend.</summary>
    [Test]
    public void ASiblingClass_ConstructedIntoALocalOfTheSameName() => RunsOnEveryBackend("HELLO RAN",
        ("Main.bas", "Module Program\n Sub Main()\n  Dim c As New C()\n  c.Hello()\n End Sub\nEnd Module\n"),
        ("C.bas", "Public Class C\n Public Sub Hello()\n  PrintLine(\"HELLO RAN\")\n End Sub\nEnd Class\n"));

    /// <summary>
    /// The longer name compiled before only as a member-less phantom; typed as the real class now,
    /// a member's type flows (the + would be refused on an Object).
    /// </summary>
    [Test]
    public void ASiblingClass_ConstructedIntoALocalOfTheSameName_KeepsItsMembers() => RunsOnEveryBackend("8",
        ("Main.bas", "Sub Main()\n Dim greeter As New Greeter()\n PrintLine(CStr(greeter.Count + 1))\nEnd Sub\n"),
        ("Greeter.bas", "Public Class Greeter\n Public Count As Integer = 7\nEnd Class\n"));

    // ------------------------------------------------------------------ item 2: a member the class does not have

    [TestCase("LoginForm", TestName = "AMissingMethodOnAUserClass_IsReported_CrossFile(LoginForm)")]
    [TestCase("F", TestName = "AMissingMethodOnAUserClass_IsReported_CrossFile(F)")]
    public void AMissingMethodOnAUserClass_IsReported_CrossFile(string className)
    {
        var paths = Write(
            ("Main.bas", $"Sub Main()\n Dim x As New {className}()\nEnd Sub\n"),
            ($"{className}.bas", $"Public Class {className}\n Public Sub New()\n  Me.InitializeComponent()\n End Sub\nEnd Class\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(Messages(result), Does.Contain($"Type '{className}' does not have a member 'InitializeComponent'"),
                string.Join(",", order.Select(Path.GetFileName)));
        }
    }

    /// <summary>From OUTSIDE the class, through a local, on a sibling file's class.</summary>
    [Test]
    public void AMissingMethodOnASiblingClass_ThroughALocal_IsReported()
    {
        var paths = Write(
            ("Main.bas", "Sub Main()\n Dim w As New Widget()\n w.Frobnicate()\nEnd Sub\n"),
            ("Widget.bas", "Public Class Widget\n Public Sub Spin()\n End Sub\nEnd Class\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(Messages(result), Does.Contain("Type 'Widget' does not have a member 'Frobnicate'"),
                string.Join(",", order.Select(Path.GetFileName)));
        }
    }

    /// <summary>
    /// What the strict check must NOT refuse: a Private method reached through <c>Me</c> and an
    /// inherited member. Run with a long name and a one-letter one.
    /// </summary>
    [TestCase("Widget", "BaseWidget")]
    [TestCase("W", "B")]
    public void TheMembersAUserClassReallyHas_StillBindAndRun(string widget, string baseWidget) => RunsOnEveryBackend("PRIV\nINH",
        ($"{widget}.bas",
            $"Public Class {baseWidget}\n Public Sub Inherited()\n  PrintLine(\"INH\")\n End Sub\nEnd Class\n" +
            $"Public Class {widget}\n Inherits {baseWidget}\n" +
            " Private Sub Earlier()\n  PrintLine(\"PRIV\")\n End Sub\n" +
            " Public Sub New()\n  Me.Earlier()\n End Sub\nEnd Class\n"),
        ("Main.bas", $"Sub Main()\n Dim w As New {widget}()\n w.Inherited()\nEnd Sub\n"));

    /// <summary>
    /// ⛔ A Private method declared BELOW its <c>Me.</c> caller was "does not have a member" on a
    /// one-letter class (the only name that missed the .NET arm). Compile-only: the call is typed
    /// Object either way, which C++ spells <c>t0 = this->Later()</c> — a separate, pre-existing gap
    /// for every class name.
    /// </summary>
    [TestCase("Widget")]
    [TestCase("W")]
    public void APrivateMethodDeclaredBelowItsMeCaller_IsNotAMissingMember(string widget)
    {
        var paths = Write(
            ($"{widget}.bas", $"Public Class {widget}\n Public Sub New()\n  Me.Later()\n End Sub\n Private Sub Later()\n End Sub\nEnd Class\n"),
            ("Main.bas", $"Sub Main()\n Dim w As New {widget}()\nEnd Sub\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.False, Messages(result));
        }
    }

    /// <summary>
    /// An Event reached by AddHandler, on a long name and a one-letter one — compile-only, because a
    /// user event does not lower on C++ at all (a separate, known gap).
    /// </summary>
    [TestCase("Widget")]
    [TestCase("W")]
    public void AnEventOfAUserClass_IsNotAMissingMember(string widget)
    {
        var paths = Write(
            ($"{widget}.bas", $"Public Class {widget}\n Public Event Changed()\n Public Sub Fire()\n  RaiseEvent Changed()\n End Sub\nEnd Class\n"),
            ("Main.bas", "Sub OnChanged()\nEnd Sub\n" +
                         $"Sub Main()\n Dim w As New {widget}()\n AddHandler w.Changed, AddressOf OnChanged\n w.Fire()\nEnd Sub\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.False, Messages(result));
        }
    }

    /// <summary>Object's own members are members of every class — never "does not have a member".</summary>
    [Test]
    public void ObjectsOwnMembers_AreNotRefused_OnAUserClass()
    {
        var paths = Write(
            ("Main.bas", "Sub Main()\n Dim w As New Widget()\n Dim s As String = w.ToString()\n Dim b As Boolean = w.Equals(w)\n Dim h As Integer = w.GetHashCode()\nEnd Sub\n"),
            ("Widget.bas", "Public Class Widget\n Public Sub Spin()\n End Sub\nEnd Class\n"));
        var result = Compile(paths);
        Assert.That(Messages(result), Does.Not.Contain("does not have a member"));
    }

    /// <summary>The same-file shape of the reported defect: a missing member on a long-named class.</summary>
    [Test]
    public void AMissingMethodOnAUserClass_IsReported_InOneFile()
    {
        var paths = Write(("Main.bas",
            "Public Class LoginForm\n Public Sub New()\n  Me.InitializeComponent()\n End Sub\nEnd Class\n" +
            "Sub Main()\n Dim x As New LoginForm()\nEnd Sub\n"));
        Assert.That(Messages(Compile(paths)), Does.Contain("Type 'LoginForm' does not have a member 'InitializeComponent'"));
    }

    /// <summary>A user class over a .NET base keeps the permissive lookup: its members are the base's.</summary>
    [Test]
    public void AUserClassOverANetBase_StaysPermissive()
    {
        var paths = Write(
            ("Main.bas", "Sub Main()\n Dim e As New AppError(\"boom\")\n PrintLine(e.Message)\nEnd Sub\n"),
            ("AppError.bas", "Public Class AppError\n Inherits Exception\n Public Sub New(m As String)\n  MyBase.New(m)\n End Sub\nEnd Class\n"));
        var result = Compile(paths);
        Assert.That(result.HasErrors, Is.False, Messages(result));
    }
}
