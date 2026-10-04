using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using RecordingOutput = VisualGameStudio.Tests.Services.JavaScriptProjectBuildTests.RecordingOutput;

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

    /// <summary>
    /// ⛔ An <c>Extension</c> method on a user class is not in the class's declaration, and was
    /// REFUSED by the first cut of the strict check ("Type 'Widget' does not have a member
    /// 'Describe'") on every backend and in the editor — while the base built and ran it on C#.
    /// Runs on C# only: on base the same program already died on JavaScript
    /// (<c>TypeError: w.Describe is not a function</c>) and C++ (<c>C2039</c>) — extension calls
    /// are not lowered there, a separate gap this does not make worse (measured 2026-09-29).
    /// ⚠ One file on purpose: a class in a file of its own name is CS0101 on C#, and extending a
    /// class from ANOTHER file is "Cannot extend unknown type" — both separate, pre-existing.
    /// </summary>
    [TestCase("Widget")]
    [TestCase("W")]
    public void AnExtensionMethodOnAUserClass_IsNotAMissingMember_AndRunsOnCSharp(string widget)
    {
        var paths = Write(("Main.bas",
            $"Public Class {widget}\n Public Name As String = \"W2\"\nEnd Class\n" +
            $"Extension Function Describe(w As {widget}) As String\n Return w.Name\nEnd Function\n" +
            $"Sub Main()\n Dim w As New {widget}()\n PrintLine(w.Describe())\nEnd Sub\n"));
        var result = Compile(paths);
        Assert.That(result.HasErrors, Is.False, Messages(result));
        var cs = new CSharpCodeGenerator().Generate(Optimized(result.CombinedIR!));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("W2"), cs);
    }

    /// <summary>
    /// The branch of the strict check for an <c>Inherits</c> it could not resolve: a not-yet-compiled
    /// sibling's class over a .NET base (<c>ArrayList</c>) has a shell with no BaseType, and its
    /// inherited members must stay permissive (a surviving mutant said nothing pinned it).
    /// </summary>
    [Test]
    public void AClassOverAnUnresolvedBase_InAPendingSibling_StaysPermissive()
    {
        var paths = Write(
            ("Main.bas", "Sub Main()\n Dim z As New ZList()\n z.Add(1)\nEnd Sub\n"),
            ("ZList.bas", "Using System.Collections\nPublic Class ZList\n Inherits ArrayList\nEnd Class\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(Messages(result), Does.Not.Contain("does not have a member"),
                string.Join(",", order.Select(Path.GetFileName)));
        }
    }

    /// <summary><c>Me.New(…)</c> is constructor chaining, and the message says so.</summary>
    [Test]
    public void ConstructorChainingViaMeNew_IsNamedAsSuch()
    {
        var paths = Write(("Main.bas",
            "Public Class Widget\n Public N As Integer\n Public Sub New()\n  Me.New(3)\n End Sub\n" +
            " Public Sub New(n As Integer)\n  Me.N = n\n End Sub\nEnd Class\n" +
            "Sub Main()\n Dim w As New Widget()\nEnd Sub\n"));
        var messages = Messages(Compile(paths));
        Assert.That(messages, Does.Contain("Constructor chaining via Me.New is not supported"));
        Assert.That(messages, Does.Not.Contain("does not have a member 'New'"));
    }

    /// <summary>
    /// <c>Game.Version</c> with <c>Game</c> both a class and <c>Game.bas</c>, and <c>Version</c> a
    /// file-level Const: refused in BOTH compile orders, naming the real problem. Before, one order
    /// built clean and printed an empty line on JavaScript; the other said only "does not have a member".
    /// </summary>
    [Test]
    public void AFileLevelMember_QualifiedByAClassOfTheFilesName_IsRefusedWithTheRealReason()
    {
        var paths = Write(
            ("Game.bas", "Public Const Version As String = \"1.0\"\nPublic Class Game\n Public Sub Run()\n End Sub\nEnd Class\n"),
            ("Main.bas", "Sub Main()\n PrintLine(Game.Version)\nEnd Sub\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var messages = Messages(Compile(order));
            Assert.That(messages, Does.Contain("is declared at file level in Game.bas"),
                string.Join(",", order.Select(Path.GetFileName)));
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

    // ------------------------------------------------------------------ item 4: Inherits across files
    // Portable-controls plan Task 7 (spec §4.2, M6; chip task_e7af351e item 1). Measured on bc29391e, 40e9c172
    // and 6fbde6a5: "Unknown base class 'Base'" + "MyBase can only be used in a class that inherits from another
    // class" + the upcast refused, on JavaScript, C# AND C++ (BL3001). With any Using line the base silently became
    // an opaque .NET class instead. Visit(ClassNode) and RegisterClassBases looked the base up in the unit's own
    // type manager only, where a sibling file's class never is.

    private const string BaseFile =
        "Public Class Base\n Public Overridable Function Hi() As String\n  Return \"base\"\n End Function\n" +
        " Public Sub Hello()\n  PrintLine(\"hello from base\")\n End Sub\nEnd Class\n";

    private const string DerivedFile =
        "Public Class D\n Inherits Base\n Public Overrides Function Hi() As String\n  Return \"D:\" & MyBase.Hi()\n" +
        " End Function\n Public Sub Greet()\n  Me.Hello()\n End Sub\nEnd Class\n";

    private const string UseBaseAndDerived =
        "Module Program\n Sub Main()\n  Dim x As Base = New D()\n  PrintLine(x.Hi())\n  Dim d As New D()\n  d.Greet()\n End Sub\nEnd Module\n";

    private const string CrossFileBaseOutput = "D:base\nhello from base";

    [Test]
    public void AClassInheritsAClassFromAnotherFile() => RunsOnEveryBackend(CrossFileBaseOutput,
        ("Base.bas", BaseFile), ("Derived.bas", DerivedFile), ("Main.bas", UseBaseAndDerived));

    /// <summary>
    /// ⛔ The Using shape: the analyzer's "unresolved base + a .NET Using = an opaque .NET class" must not win
    /// over a sibling's real class. The WinForms scaffold always has Using lines (FormScaffolder).
    /// ⚠ <c>Greet</c> calls the inherited Sub UNQUALIFIED here: <c>Me.Hello()</c> under a .NET Using is M7 (the IR
    /// builder takes <c>Me</c> for a .NET static type — JavaScript "no lowering for 'Me.Hello'"), a separate defect
    /// owned by portable-controls Task 11, which switches this row back to <c>Me.Hello()</c>.
    /// </summary>
    [Test]
    public void AClassInheritsAClassFromAnotherFile_UnderAUsing() => RunsOnEveryBackend(CrossFileBaseOutput,
        ("Base.bas", BaseFile),
        ("Derived.bas", "Using System\n" + DerivedFile.Replace("  Me.Hello()\n", "  Hello()\n")),
        ("Main.bas", UseBaseAndDerived));

    /// <summary>
    /// ⛔ An UNQUALIFIED call to an inherited Sub, the base in a file named unlike it. Pass 1 flattens the base's
    /// methods into the global scope as imports owned by their FILE, and the IR builder spelled the call against
    /// that owner: C# emitted <c>Shapes.Hello()</c> / <c>Base.Hello()</c> (CS0120/CS0103) once the base resolved.
    /// </summary>
    [Test]
    public void AnUnqualifiedCallToAnInheritedSub_FromAnotherFile() => RunsOnEveryBackend("hello from base\nhello from base",
        ("Shapes.bas", BaseFile),
        ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Hello()\n End Sub\nEnd Class\n"),
        ("Main.bas", "Sub Main()\n Dim d As New D()\n d.Greet()\n d.Hello()\nEnd Sub\n"));

    /// <summary>
    /// Chip task_e7af351e item 2 across files: every class must be emitted AFTER its base (a JavaScript
    /// <c>class C extends B</c> above <c>class B</c> is a TDZ ReferenceError at load; C++ needs the complete base).
    /// Both file orders put the derived file first once.
    /// </summary>
    [Test]
    public void AThreeLevelChain_SplitOverThreeFiles() => RunsOnEveryBackend("C>B>A",
        ("A.bas", "Public Class A\n Public Overridable Function Name() As String\n  Return \"A\"\n End Function\nEnd Class\n"),
        ("B.bas", "Public Class B\n Inherits A\n Public Overrides Function Name() As String\n  Return \"B>\" & MyBase.Name()\n End Function\nEnd Class\n"),
        ("C.bas", "Public Class C\n Inherits B\n Public Overrides Function Name() As String\n  Return \"C>\" & MyBase.Name()\n End Function\nEnd Class\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Dim a As A = New C()\n  PrintLine(a.Name())\n End Sub\nEnd Module\n"));

    /// <summary>
    /// The rest of what a derived class inherits across a file boundary: a field, a property (read and written
    /// through <c>Me</c> and from outside) and a base constructor with a parameter reached by <c>MyBase.New</c>.
    /// </summary>
    [Test]
    public void FieldsPropertiesAndMyBaseNew_AreInheritedAcrossFiles() => RunsOnEveryBackend("box:3\nbox4",
        ("Shape.bas",
            "Public Class Shape\n Public Name As String\n Private _size As Integer\n" +
            " Public Sub New(n As String)\n  Name = n\n End Sub\n" +
            " Public Property Size As Integer\n  Get\n   Return _size\n  End Get\n  Set(value As Integer)\n   _size = value\n  End Set\n End Property\nEnd Class\n"),
        ("Box.bas",
            "Public Class Box\n Inherits Shape\n Public Sub New()\n  MyBase.New(\"box\")\n  Me.Size = 3\n End Sub\n" +
            " Public Function Describe() As String\n  Return Me.Name & \":\" & CStr(Me.Size)\n End Function\nEnd Class\n"),
        ("Main.bas", "Sub Main()\n Dim b As New Box()\n PrintLine(b.Describe())\n b.Size = b.Size + 1\n PrintLine(b.Name & CStr(b.Size))\nEnd Sub\n"));

    /// <summary>The strict missing-member check walks the cross-file base chain: what the base has binds, what
    /// neither class has is still refused by name — through <c>Me</c> and through a local, in both orders.</summary>
    [Test]
    public void AMemberNeitherClassHas_IsStillReported_AcrossACrossFileBase()
    {
        var paths = Write(
            ("Base.bas", BaseFile),
            ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Me.Hello()\n  Me.Vanish()\n End Sub\nEnd Class\n"),
            ("Main.bas", "Sub Main()\n Dim d As New D()\n d.Hello()\n d.Frobnicate()\nEnd Sub\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var label = string.Join(",", order.Select(Path.GetFileName));
            var messages = Messages(Compile(order));
            Assert.Multiple(() =>
            {
                Assert.That(messages, Does.Contain("does not have a member 'Vanish'"), label);
                Assert.That(messages, Does.Contain("does not have a member 'Frobnicate'"), label);
                Assert.That(messages, Does.Not.Contain("'Hello'"), label);
                Assert.That(messages, Does.Not.Contain("Unknown base class"), label);
            });
        }
    }

    // ---- a class member (inherited included) shadows a Module procedure of the same name (VB's rule)

    private const string UtilModuleWithHello =
        "Module Util\n Public Sub Hello()\n  PrintLine(\"module\")\n End Sub\nEnd Module\n";

    private const string BaseWithHello =
        "Public Class Base\n Public Sub Hello()\n  PrintLine(\"base\")\n End Sub\nEnd Class\n";

    private const string DerivedCallsHello =
        "Public Class D\n Inherits Base\n Public Sub Greet()\n  Hello()\n End Sub\nEnd Class\n";

    private const string MainGreets = "Sub Main()\n Dim d As New D()\n d.Greet()\nEnd Sub\n";

    /// <summary>
    /// ⛔ Task 7 review: C# and JavaScript called DIFFERENT methods. JavaScript ran the inherited <c>Hello</c>
    /// (VB: a member, inherited included, shadows a Module's); C# emitted <c>Util.Hello();</c> inside
    /// <c>class D</c> — the IR builder handed the call its Module owner without consulting the base chain.
    /// </summary>
    [Test]
    public void AnInheritedSub_ShadowsAModuleSubOfTheSameName_AcrossFiles() => RunsOnEveryBackend("base",
        ("Base.bas", BaseWithHello), ("Util.bas", UtilModuleWithHello), ("Derived.bas", DerivedCallsHello),
        ("Main.bas", MainGreets));

    /// <summary>The same collision with Base and D in ONE file: C# depended on the file order.</summary>
    [Test]
    public void AnInheritedSub_ShadowsAModuleSubOfTheSameName_BaseAndDerivedInOneFile() => RunsOnEveryBackend("base",
        ("Classes.bas", BaseWithHello + DerivedCallsHello), ("Util.bas", UtilModuleWithHello), ("Main.bas", MainGreets));

    /// <summary>A QUALIFIED <c>Util.Hello()</c> inside the derived class still reaches the Module.</summary>
    [Test]
    public void AQualifiedModuleCall_InsideADerivedClass_StillReachesTheModule() => RunsOnEveryBackend("module\nbase",
        ("Base.bas", BaseWithHello), ("Util.bas", UtilModuleWithHello),
        ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Util.Hello()\n  Hello()\n End Sub\nEnd Class\n"),
        ("Main.bas", MainGreets));

    /// <summary>
    /// A PRIVATE base method is inaccessible from the derived class, so VB skips it and the Module's
    /// <c>Hello</c> is called; inside the base itself its own Private <c>Hello</c> still wins.
    /// </summary>
    [Test]
    public void APrivateBaseSub_DoesNotShadowAModuleSub_ButStillWinsInsideItsOwnClass() => RunsOnEveryBackend("module\nprivate base",
        ("Base.bas", "Public Class Base\n Private Sub Hello()\n  PrintLine(\"private base\")\n End Sub\n" +
                     " Public Sub Run()\n  Hello()\n End Sub\nEnd Class\n"),
        ("Util.bas", UtilModuleWithHello), ("Derived.bas", DerivedCallsHello),
        ("Main.bas", "Sub Main()\n Dim d As New D()\n d.Greet()\n d.Run()\nEnd Sub\n"));

    /// <summary>⛔ The CLI route of the collision, on the backend that got it wrong: a C# project built by the real
    /// <c>BasicLang.exe build</c> and its App.exe RUN, in both Compile orders.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TheCli_BuildsAndRunsACSharpProject_WhereAnInheritedSubShadowsAModuleSub(bool reversed)
    {
        var files = new[] { "Base.bas", "Util.bas", "Derived.bas", "Main.bas" };
        if (reversed) Array.Reverse(files);
        Write(
            ("App.blproj",
                "<BasicLangProject Version=\"1.0\">\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n" +
                "    <OutputType>Exe</OutputType>\n    <TargetBackend>CSharp</TargetBackend>\n  </PropertyGroup>\n" +
                "  <ItemGroup>\n" + string.Concat(files.Select(f => $"    <Compile Include=\"{f}\" />\n")) +
                "  </ItemGroup>\n</BasicLangProject>\n"),
            ("Base.bas", BaseWithHello), ("Util.bas", UtilModuleWithHello), ("Derived.bas", DerivedCallsHello),
            ("Main.bas", MainGreets));

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 300_000);
        Assert.That(exit, Is.Zero, $"the CLI refused the project\n{stdout}\n{stderr}");

        var exe = Path.Combine(_dir, "bin", "Debug", "net8.0", OperatingSystem.IsWindows() ? "App.exe" : "App");
        Assert.That(File.Exists(exe), Is.True, stdout);
        var ran = CliTestHarness.RunProcess(exe, Array.Empty<string>(), _dir, timeoutMs: 60_000);
        Assert.That(FourBackends.Norm(ran.Item2), Is.EqualTo("base"), ran.Item3);
    }

    // ---- the ANALYZER binds a bare call by the same class-first rule (Task 7b)
    // ⛔ Task 7 taught the IR builder and the backends VB's rule — a class member, own or inherited and
    // accessible, shadows a Module's procedure — but the analyzer still bound and TYPE-CHECKED a bare call by
    // module/global scope first. With the two signatures different, "Compilation successful!" on C# and JS:
    // JavaScript ran `this.Hello(3)` against Base's `Hello()` and printed 5 into a String (the extra argument
    // dropped silently); csc refused the C# late. VB binds `Hello(3)` to Base.Hello and reports the count.

    private const string UtilModuleHelloOfInteger =
        "Module Util\n Public Function Hello(x As Integer) As String\n  Return \"module\"\n End Function\nEnd Module\n";

    private const string BaseHelloNoArgs =
        "Public Class Base\n Public Function Hello() As Integer\n  Return 5\n End Function\nEnd Class\n";

    private const string DerivedCallsHello3 =
        "Public Class D\n Inherits Base\n Public Sub Greet()\n  Dim s As String = Hello(3)\n  PrintLine(s)\n End Sub\nEnd Class\n";

    /// <summary>Compiles the files in both orders and returns each order's messages, asserting it FAILED.</summary>
    private void RefusedInBothOrders(Action<string, string> check, params (string Name, string Text)[] files)
    {
        var paths = Write(files);
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var label = string.Join(",", order.Select(Path.GetFileName));
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.True, $"[{label}] a green build of a call VB refuses");
            check(Messages(result), label);
        }
    }

    private static void NamesHelloArgumentCount(string messages, string label) => Assert.That(messages,
        Does.Contain("Function 'Hello' expects 0 argument(s), got 1"), label);

    /// <summary>⛔ The reviewer's repro (scratchpad t7probe\sig.bas), every declaration in ONE file.</summary>
    [Test]
    public void AnInheritedMemberWithAnotherSignature_IsTheOneTypeChecked_OneFile() => RefusedInBothOrders(
        NamesHelloArgumentCount,
        ("Program.bas", UtilModuleHelloOfInteger + BaseHelloNoArgs + DerivedCallsHello3 +
                        "Sub Main()\n Dim d As New D()\n d.Greet()\nEnd Sub\n"));

    /// <summary>⛔ The same with the base, the derived class and the Module each in a file of its own.</summary>
    [Test]
    public void AnInheritedMemberWithAnotherSignature_IsTheOneTypeChecked_AcrossFiles() => RefusedInBothOrders(
        NamesHelloArgumentCount,
        ("Base.bas", BaseHelloNoArgs), ("Util.bas", UtilModuleHelloOfInteger), ("Derived.bas", DerivedCallsHello3),
        ("Main.bas", MainGreets));

    /// <summary>⛔ The class's OWN member is nearer too — declared below its caller, where lexical scope has not met
    /// it yet and pass 1's flattened global copy is the Module's.</summary>
    [Test]
    public void AnOwnMemberWithAnotherSignature_IsTheOneTypeChecked_EvenDeclaredBelowTheCall() => RefusedInBothOrders(
        NamesHelloArgumentCount,
        ("Util.bas", UtilModuleHelloOfInteger),
        ("D.bas", "Public Class D\n Public Sub Greet()\n  Dim s As String = Hello(3)\n  PrintLine(s)\n End Sub\n" +
                  " Public Function Hello() As Integer\n  Return 5\n End Function\nEnd Class\n"),
        ("Main.bas", MainGreets));

    /// <summary>The matching signature binds the INHERITED member and is typed by its return — Integer, not the
    /// Module's String — so the arithmetic compiles and runs it on every backend.</summary>
    [Test]
    public void AnInheritedFunction_IsTypedByItsOwnReturn_AndRuns() => RunsOnEveryBackend("8",
        ("Base.bas", "Public Class Base\n Public Function Hello(x As Integer) As Integer\n  Return x + 1\n End Function\nEnd Class\n"),
        ("Util.bas", UtilModuleHelloOfInteger),
        ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Dim n As Integer = Hello(3)\n  PrintLine(n * 2)\n End Sub\nEnd Class\n"),
        ("Main.bas", MainGreets));

    /// <summary>The same, base and derived in one file.</summary>
    [Test]
    public void AnInheritedFunction_IsTypedByItsOwnReturn_AndRuns_OneFile() => RunsOnEveryBackend("8",
        ("Classes.bas",
            "Public Class Base\n Public Function Hello(x As Integer) As Integer\n  Return x + 1\n End Function\nEnd Class\n" +
            "Public Class D\n Inherits Base\n Public Sub Greet()\n  Dim n As Integer = Hello(3)\n  PrintLine(n * 2)\n End Sub\nEnd Class\n"),
        ("Util.bas", UtilModuleHelloOfInteger), ("Main.bas", MainGreets));

    /// <summary>A base's PRIVATE <c>Hello()</c> is inaccessible from D, so it shadows nothing: <c>Hello(3)</c> is the
    /// Module's, type-checks against it, and runs it.</summary>
    [Test]
    public void APrivateBaseMember_DoesNotShadow_TheModuleOverloadIsCheckedAndRuns() => RunsOnEveryBackend("module",
        ("Base.bas", "Public Class Base\n Private Function Hello() As Integer\n  Return 5\n End Function\nEnd Class\n"),
        ("Util.bas", UtilModuleHelloOfInteger), ("Derived.bas", DerivedCallsHello3), ("Main.bas", MainGreets));

    /// <summary>The class's OWN Private member does shadow the Module's, and is the one typed and run.</summary>
    [Test]
    public void AnOwnPrivateMember_Shadows_AndIsTheOneTypedAndRun() => RunsOnEveryBackend("8",
        ("Util.bas", UtilModuleHelloOfInteger),
        ("D.bas", "Public Class D\n Private Function Hello(x As Integer) As Integer\n  Return x + 1\n End Function\n" +
                  " Public Sub Greet()\n  Dim n As Integer = Hello(3)\n  PrintLine(n * 2)\n End Sub\nEnd Class\n"),
        ("Main.bas", MainGreets));

    /// <summary>...and an own Private member with another signature is refused, like an inherited one.</summary>
    [Test]
    public void AnOwnPrivateMemberWithAnotherSignature_IsTheOneTypeChecked() => RefusedInBothOrders(
        NamesHelloArgumentCount,
        ("Util.bas", UtilModuleHelloOfInteger),
        ("D.bas", "Public Class D\n Private Function Hello() As Integer\n  Return 5\n End Function\n" +
                  " Public Sub Greet()\n  Dim s As String = Hello(3)\n  PrintLine(s)\n End Sub\nEnd Class\n"),
        ("Main.bas", MainGreets));

    /// <summary>A QUALIFIED <c>Util.Hello(3)</c> in D names the Module whatever the base declares.</summary>
    [Test]
    public void AQualifiedModuleCall_ReachesTheModule_PastAnInheritedMemberWithAnotherSignature() => RunsOnEveryBackend("module",
        ("Base.bas", BaseHelloNoArgs), ("Util.bas", UtilModuleHelloOfInteger),
        ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet()\n  Dim s As String = Util.Hello(3)\n  PrintLine(s)\n End Sub\nEnd Class\n"),
        ("Main.bas", MainGreets));

    /// <summary>
    /// A parameter is NEARER than the class: <c>Hello(3)</c> on a delegate parameter named <c>Hello</c> is typed by
    /// the delegate, not refused against the inherited <c>Hello()</c>. Front end only — ⚠ JavaScript still EMITS
    /// <c>this.Hello(3)</c> for it (its CallTarget resolves a bare name against the class before anything else), a
    /// separate backend defect recorded in the plan's Task 7 follow-ups.
    /// </summary>
    [Test]
    public void AParameterIsNearerThanTheClass_InTheAnalyzer()
    {
        var paths = Write(
            ("Base.bas", BaseHelloNoArgs),
            ("Derived.bas", "Public Class D\n Inherits Base\n Public Sub Greet(Hello As Func(Of Integer, String))\n" +
                            "  Dim s As String = Hello(3)\n  PrintLine(s)\n End Sub\nEnd Class\n"),
            ("Main.bas", "Sub Main()\n Dim d As New D()\n d.Greet(Function(x As Integer) CStr(x))\nEnd Sub\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.False, $"[{string.Join(",", order.Select(Path.GetFileName))}] {Messages(result)}");
        }
    }

    // ---- an inheritance cycle across files

    /// <summary>
    /// ⛔ Task 7 review: <c>A Inherits B</c> / <c>B Inherits A</c> in two files compiled CLEAN (csc refused it, the
    /// JavaScript run failed) while the same pair in one file reports VB's BC30257. The cycle check compared
    /// TypeInfo references, and a sibling file's class reaches this unit as a SHELL — another object for the same class.
    /// </summary>
    [Test]
    public void AnInheritanceCycle_AcrossFiles_IsReported()
    {
        var paths = Write(
            ("A.bas", "Public Class A\n Inherits B\nEnd Class\n"),
            ("B.bas", "Public Class B\n Inherits A\nEnd Class\n"),
            ("Main.bas", "Sub Main()\n Dim a As New A()\nEnd Sub\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var label = string.Join(",", order.Select(Path.GetFileName));
            var result = Compile(order);
            Assert.That(result.HasErrors, Is.True, label);
            Assert.That(Messages(result), Does.Contain("cannot inherit from itself"), label);
        }
    }

    /// <summary>The cycle check must not fire on a legal cross-file chain whose classes share nothing but a base.</summary>
    [Test]
    public void TwoSiblingsOverOneCrossFileBase_AreNotACycle() => RunsOnEveryBackend("X\nY",
        ("Base.bas", "Public Class Base\n Public Overridable Function Name() As String\n  Return \"?\"\n End Function\nEnd Class\n"),
        ("X.bas", "Public Class X\n Inherits Base\n Public Overrides Function Name() As String\n  Return \"X\"\n End Function\nEnd Class\n"),
        ("Y.bas", "Public Class Y\n Inherits Base\n Public Overrides Function Name() As String\n  Return \"Y\"\n End Function\nEnd Class\n"),
        ("Main.bas", "Sub Main()\n Dim a As Base = New X()\n Dim b As Base = New Y()\n PrintLine(a.Name())\n PrintLine(b.Name())\nEnd Sub\n"));

    /// <summary>A JavaScript project over the three files, listed in the given order.</summary>
    private string WriteCrossFileBaseProject(bool reversed)
    {
        var files = new[] { "Base.bas", "Derived.bas", "Main.bas" };
        if (reversed) Array.Reverse(files);
        Write(
            ("App.blproj",
                "<BasicLangProject Version=\"1.0\">\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n" +
                "    <OutputType>Exe</OutputType>\n    <TargetBackend>JavaScript</TargetBackend>\n  </PropertyGroup>\n" +
                "  <ItemGroup>\n" + string.Concat(files.Select(f => $"    <Compile Include=\"{f}\" />\n")) +
                "  </ItemGroup>\n</BasicLangProject>\n"),
            ("Base.bas", BaseFile), ("Derived.bas", DerivedFile), ("Main.bas", UseBaseAndDerived));
        return Path.Combine(_dir, "App.blproj");
    }

    /// <summary>⛔ The CLI entry point: the real <c>BasicLang.exe build</c> of a JavaScript project whose class
    /// inherits a class from another file, RUN under node.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TheCli_BuildsAndRunsAJavaScriptProject_WithACrossFileBase(bool reversed)
    {
        var project = WriteCrossFileBaseProject(reversed);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", project }, _dir, timeoutMs: 180_000);
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
        Assert.That(FourBackends.Norm(ran.Item2), Is.EqualTo(CrossFileBaseOutput), "a green build is not a running page\n" + ran.Item3);
    }

    /// <summary>⛔ The IDE entry point: <see cref="BuildService.BuildProjectAsync"/> (which delegates to the CLI
    /// engine's CompileProjectFiles) over the same project, its generated script RUN under node.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TheIde_BuildsAJavaScriptProject_WithACrossFileBase(bool reversed)
    {
        var project = await new ProjectSerializer().LoadAsync(WriteCrossFileBaseProject(reversed));
        var output = new RecordingOutput();
        var service = new BuildService(output) { CurrentConfiguration = new BuildConfiguration { Name = "Debug" } };
        var result = await service.BuildProjectAsync(project);
        Assert.That(result.Success, Is.True, output.Dump());
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(result.GeneratedCode!)), Is.EqualTo(CrossFileBaseOutput));
    }
}
