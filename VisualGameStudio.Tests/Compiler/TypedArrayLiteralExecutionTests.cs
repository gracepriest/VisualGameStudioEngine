using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 24a, Task 4 — `New T() { … }` RUNS on all three backends (spec §9, "Lowering" and the
/// per-backend rows). Before this fixture the JavaScript backend had no array-literal lowering at
/// all (`Visit(IRArrayAlloc)` / `Visit(IRArrayStore)` threw NotYet, and the alloca that an
/// array-typed local's initializer stores through had no expression arm), and the C# backend
/// rendered a store's non-literal value BY NAME, so the `IRCast` Task 3 wraps a non-literal element
/// in came out as an undeclared temp (CS0103).
/// </summary>
[TestFixture]
[Category("Integration")]
public class TypedArrayLiteralExecutionTests
{
    private static void RequireNode()
    {
        // ⛔ FAIL loudly, never Assert.Ignore: these are the first tests of a backend arm that has
        // never run, and a pass-by-absence is what this project calls a false green.
        Assert.That(BasicLang.Runtime.NodeLocator.Find(), Is.Not.Null, "node is required for this row");
    }

    private const string SumTyped = "Sub Main()\nDim a() As Integer = New Integer() {1, 2, 3}\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";
    private const string SumBare  = "Sub Main()\nDim a() As Integer = {1, 2, 3}\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";
    // ⚠ The Double program prints the NUMBER ALONE. The C++ backend DELIBERATELY refuses floating-point
    // string concatenation (CppCodeGenerator.cs — `StringifyForText` has no Single/Double arm;
    // `"SUM " & aDouble` becomes `std::string("SUM ") + aDouble`, the intended build break),
    // while its print arm formats a Double .NET-style (CppBclEndToEndTests → `19.99`).
    // C# prints `3` for 3.0 and node prints `3`, so one expected string serves all three.
    // ⛔ `i` is a PARAMETER, never `Dim i As Integer = 2`. The optimizer folds IRCast(IRConstant)
    // (IROptimizer.cs) and ReplaceUses reaches IRArrayStore.Value, so a constant `i` folds the cast
    // away and every optimized row goes green WITHOUT ever emitting a cast into a store — the shape
    // §9 mandates and the C# backend could not render. A same-file bare `Sub` called from `Main`
    // works on all three backends (the cross-FILE call is the broken one).
    // ⚠ NOT `Sub Run`: `Run(exePath As String, arguments As String)` is a BasicLang BUILTIN, and the
    // analyzer refuses `Run(2)` with "expects 2 argument(s), got 1" before any backend is reached
    // (measured; see BuiltinCollisionTests for the general trap).
    private const string SumDouble = "Sub Main()\nSumFrom(2)\nEnd Sub\nSub SumFrom(i As Integer)\nDim a() As Double = New Double() {1, i}\nDim total As Double = 0\nFor Each x As Double In a\ntotal = total + x\nNext\nConsole.WriteLine(total)\nEnd Sub";
    // ⛔ A CALL as an element. On the C# backend an element's use was never COUNTED
    // (`GetOperands` had no IRArrayStore arm), so `Bump()` had use-count 0, was emitted as a bare
    // statement for its side effect, and the store then re-rendered it inline: measured through
    // the real CLI, `Bump(); Bump(); var t2 = new int[2]; t2[0] = Bump(); t2[1] = Bump();` —
    // a green build that calls Bump FOUR times. Before Task 4 the same program was CS0103
    // (`t2[0] = t0;`, loud); Task 4's EmitExpression turned it silent. The counter is a
    // top-level Dim (measured: `private static int counter = 0;` on C#).
    private const string BumpTwice = "Dim counter As Integer = 0\nSub Main()\nDim a() As Integer = New Integer() {Bump(), Bump()}\nConsole.WriteLine(\"N \" & counter)\nEnd Sub\nFunction Bump() As Integer\ncounter = counter + 1\nReturn counter\nEnd Function";
    // The cast-in-store's twin on Visit(IRIndexerStore): IRBuilder coerces the value before every
    // assignment-target arm, so `l(0) = i` into a List(Of Double) carries an IRCast, which the
    // indexer store must render as an expression (measured: `l[0] = (double)(i);`).
    private const string IndexerStoreCast = "Sub Main()\nPut(2)\nEnd Sub\nSub Put(i As Integer)\nDim l As New List(Of Double)()\nl.Add(0)\nl(0) = i\nConsole.WriteLine(l(0))\nEnd Sub";
    // The M4 shape — the literal passed straight to a call, AFTER its stores — is the stated reason
    // the JS `Expr` arm answers the BOUND name (measured emission: `const t0 = new Array(2);
    // t0[0] = 1; t0[1] = 2; Show(t0);`). ⚠ The callee is declared BEFORE Main, and its parameter
    // uses the bracket spelling `a[] As Integer` (the only one that parses): measured, a callee
    // declared AFTER the call site has its array parameter typed as plain `Integer` at that call
    // ("cannot convert from 'Integer[]' to 'Integer'") for a literal AND a variable argument alike —
    // a front-end declaration-order defect, not this row's subject.
    private const string SumViaCall = "Sub Show(a[] As Integer)\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub\nSub Main()\nShow(New Integer() {1, 2})\nEnd Sub";

    [TestCase(SumTyped, "SUM 6"), TestCase(SumBare, "SUM 6"), TestCase(SumDouble, "3"), TestCase(BumpTwice, "N 2"), TestCase(SumViaCall, "SUM 3")]
    public void JavaScript_RunsTheLiteral(string source, string expected)
    {
        RequireNode();
        // ⛔ BOTH routes. `RunJs` compiles through `JsTestSupport.Compile`, which runs NO optimizer pass,
        // while every shipping route runs `AddStandardPasses()` unconditionally (JsTestSupport.cs
        // says so in its own doc); the new `Expr` arm's `Bound()` branch depends on the alloc
        // still sitting in block.Instructions after those passes, which only the optimized run shows
        // (CLAUDE.md: never the non-optimizing helper alone).
        Assert.That(JavaScriptExecutionTests.RunJs(source), Is.EqualTo(expected), "non-optimized");
        Assert.That(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(source)).Trim(),
            Is.EqualTo(expected), "optimized — the IR a user gets");
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "3"), TestCase(BumpTwice, "N 2"), TestCase(IndexerStoreCast, "2"), TestCase(SumViaCall, "SUM 3")]
    public void CSharp_RunsTheLiteral(string source, string expected)
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(source);   // the real CLI, optimizer on
        Assert.That(CSharpRun.CompileAndRun(csharp).Trim(), Is.EqualTo(expected));
    }

    /// <summary>
    /// The C# backend rendered a store's non-literal value BY NAME (<c>t1[1] = t0;</c>, with the
    /// cast temp <c>t0</c> declared nowhere — CS0103, measured through the real CLI). The stored
    /// element must be the cast EXPRESSION. Measured rendering: <c>(double)(i)</c>.
    /// </summary>
    [Test]
    public void CSharp_Emission_CastsTheStoredNonLiteral()
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(SumDouble);   // the real CLI, optimizer on

        Assert.That(csharp, Does.Contain("[1] = (double)(i);"),
            "the non-literal element must be stored through its cast, not a temp name.\n--- generated C# ---\n" + csharp);
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "3"), TestCase(BumpTwice, "N 2"), TestCase(SumViaCall, "SUM 3")]
    public void Cpp_RunsTheLiteral(string source, string expected)
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Trim(), Is.EqualTo(expected));
    }

    // ------------------------------------------------------------------------------------------
    // JavaScriptBackend.cs Expr's IRArrayAlloc arm (~:768) — an UNBOUND alloc means IRBuilder's
    // `_suppressEmit` (a `Select Case … When` guard) swallowed the allocation AND its element
    // stores together (IRBuilder.EmitInstruction ~:349, guard ~:2883-2885; both are emitted
    // together in Visit(CollectionInitializerNode) ~:1926-1934). Rendering `new Array(N)` there
    // would produce a sparse array of holes wearing the right length — silently wrong output from
    // a green build. `Size == 0` is carved out because `New Integer() {}` has no stores to lose.
    // ------------------------------------------------------------------------------------------

    // Populated typed literal inside a `When` guard's call argument. ⚠ MEASURED (both by the
    // implementer and re-measured here through the real CLI on these exact binaries): the
    // pre-fix behaviour is NOT NaN — the JS backend wraps int32 arithmetic (`s = ((s + x) | 0)`),
    // so the swallowed sum comes out 0, and the guard silently takes the `Case Else` arm instead
    // of throwing.
    private const string GuardPopulatedLiteral = "Function Total(a[] As Integer) As Integer\nDim s As Integer = 0\nFor Each x As Integer In a\ns = s + x\nNext\nReturn s\nEnd Function\nSub Main()\nDim n As Integer = 5\nSelect Case n\nCase Is > 0 When Total(New Integer() {1, 2}) = 3\nConsole.WriteLine(\"guard-hit\")\nCase Else\nConsole.WriteLine(\"guard-miss\")\nEnd Select\nEnd Sub";

    // The Size == 0 carve-out, same guard shape. Total() of an empty array is 0, so the guard is
    // true and the program must print "empty-hit" — this is what stops someone "simplifying" the
    // `Size == 0` branch away; a throw-only row for the populated case above would not catch that.
    private const string GuardEmptyLiteral = "Function Total(a[] As Integer) As Integer\nDim s As Integer = 0\nFor Each x As Integer In a\ns = s + x\nNext\nReturn s\nEnd Function\nSub Main()\nDim n As Integer = 5\nSelect Case n\nCase Is > 0 When Total(New Integer() {}) = 0\nConsole.WriteLine(\"empty-hit\")\nCase Else\nConsole.WriteLine(\"empty-miss\")\nEnd Select\nEnd Sub";

    /// <summary>
    /// The throw arm: a populated typed literal inside a `When` guard must fail loudly rather
    /// than silently rendering a sparse array. Message must name the unrenderable node so a
    /// diagnosing reader lands in the right place.
    /// </summary>
    [Test]
    public void JavaScript_ArrayLiteralInsideWhenGuard_Populated_Throws()
    {
        var ex = Assert.Throws<NotSupportedException>(() => JsTestSupport.Compile(GuardPopulatedLiteral));
        Assert.That(ex!.Message, Does.Contain("IRArrayAlloc"),
            "the thrown message must name the unrenderable node.\n" + ex.Message);
    }

    /// <summary>
    /// The Size == 0 carve-out, positive: this must still COMPILE AND RUN correctly under node,
    /// not merely avoid throwing.
    /// </summary>
    [Test]
    public void JavaScript_ArrayLiteralInsideWhenGuard_Empty_CompilesAndRuns()
    {
        RequireNode();
        Assert.That(JavaScriptExecutionTests.RunJs(GuardEmptyLiteral), Is.EqualTo("empty-hit"));
    }

    /// <summary>
    /// Comments on <see cref="SumDouble"/> used to claim a constant element's cast gets folded
    /// away — false: no cast-folding pass is registered in <c>OptimizationPipeline.AddStandardPasses</c>
    /// (IROptimizer.cs:1495-1505). Measured through the real CLI on clean binaries: a LOCAL element
    /// (not a `Sub` parameter) emits the identical cast expression, <c>t1[1] = (double)(i);</c> — a
    /// local is not special. Non-Integration: this is a codegen/text assertion, no node required.
    /// </summary>
    private const string SumDoubleLocalElement = "Sub Main()\nDim i As Integer = 2\nDim a() As Double = New Double() {1, i}\nDim total As Double = 0\nFor Each x As Double In a\ntotal = total + x\nNext\nConsole.WriteLine(total)\nEnd Sub";

    [Test]
    public void CSharp_Emission_CastsAStoredLocal_NotOnlyAParameter()
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(SumDoubleLocalElement);   // the real CLI, optimizer on

        Assert.That(csharp, Does.Contain("[1] = (double)(i);"),
            "a local element is not special: it must be stored through its cast like a parameter, " +
            "not constant-folded away.\n--- generated C# ---\n" + csharp);
    }

    // ------------------------------------------------------------------------------------------
    // Task 5 (plan 2026-09-20-menus-toolbars-statusbars.md, Task 5) — the M4 / M3 / two-file rows
    // through the real CLI and csc. The brief's own motivating idiom
    // (menuStrip.Items.AddRange(New ToolStripItem() { … })) and its declared-array twin, built and
    // RUN under csc; plus the sibling-file predicate that keeps a user class from being judged
    // "csc decides" (spec §9's IsUnresolvableNetType, `!IsUserDefinedTypeName`).
    // ------------------------------------------------------------------------------------------

    // The plan's M4 program verbatim (lines 596-622): the VS idiom, AddRange(New ToolStripItem() {…}).
    private const string MenuFormVsIdiom = """
        Using System
        Using System.Windows.Forms
        Using System.Drawing

        Public Class MenuForm
            Inherits Form

            Private menuStrip1 As MenuStrip
            Private mnuFile As ToolStripMenuItem
            Private sep As ToolStripSeparator

            Public Sub New()
                menuStrip1 = New MenuStrip()
                mnuFile = New ToolStripMenuItem()
                mnuFile.Text = "File"
                sep = New ToolStripSeparator()
                menuStrip1.Items.AddRange(New ToolStripItem() {mnuFile, sep})
                Me.MainMenuStrip = menuStrip1
                Me.Controls.Add(menuStrip1)
            End Sub

            Public Sub Poke()
                Console.WriteLine("ITEMS " & menuStrip1.Items.Count)
            End Sub
        End Class
        """;

    // The same class with the constructor's AddRange line replaced by the TYPED M3 shape (a
    // DECLARED array-typed local, not the inline call-argument literal) — exercises the array
    // `Equals` path (`CreateArrayType` names arrays `T[]`, SymbolTable.cs:657/:677) the argument
    // shape does not. ⚠ NOT the bare `{mnuFile, sep}` literal — that is the refused M3 shape.
    private const string MenuFormDeclaredArrayShape = """
        Using System
        Using System.Windows.Forms
        Using System.Drawing

        Public Class MenuForm
            Inherits Form

            Private menuStrip1 As MenuStrip
            Private mnuFile As ToolStripMenuItem
            Private sep As ToolStripSeparator

            Public Sub New()
                menuStrip1 = New MenuStrip()
                mnuFile = New ToolStripMenuItem()
                mnuFile.Text = "File"
                sep = New ToolStripSeparator()
                Dim items() As ToolStripItem = New ToolStripItem() {mnuFile, sep}
                menuStrip1.Items.AddRange(items)
                Me.MainMenuStrip = menuStrip1
                Me.Controls.Add(menuStrip1)
            End Sub

            Public Sub Poke()
                Console.WriteLine("ITEMS " & menuStrip1.Items.Count)
            End Sub
        End Class
        """;

    /// <summary>
    /// The M4 idiom: real CLI (optimizer on) → the generated C# names the typed array literal →
    /// csc accepts it → a real dotnet build + run prints the menu's populated item count. Route and
    /// csproj/driver text mirror <see cref="FormComponentAcceptanceTests"/> (":135-204"); this fixture
    /// compiles the BasicLang source directly (no designer/document round trip) since Task 5 is about
    /// the compiler change, not the designer.
    /// </summary>
    [Test]
    public void WinForms_TheVsIdiom_BuildsUnderCsc_AndRuns()
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(MenuFormVsIdiom);
        TestContext.Out.WriteLine("[1] compiled the M4 program through the real CLI, optimizer on");

        Assert.That(csharp, Does.Contain("new ToolStripItem[2]"),
            "AddRange(New ToolStripItem() {mnuFile, sep}) must lower to a typed 2-element array.\n--- generated C# ---\n" + csharp);
        TestContext.Out.WriteLine("[2] generated C# contains 'new ToolStripItem[2]'");

        WinFormsCompile.AssertCompiles(csharp,
            "the M4 idiom (menuStrip1.Items.AddRange(New ToolStripItem() {mnuFile, sep})) must compile under csc");
        TestContext.Out.WriteLine("[3] csc accepts the generated C#");

        // The build + run below targets net8.0-windows with UseWindowsForms, which needs
        // Microsoft.NET.Sdk.WindowsDesktop — only the Windows .NET SDK ships it (MSB4019
        // elsewhere). Steps [1]-[3] above already ran; same skip as BuildServicePipelineTests'
        // and TemplateBuildSweepTests' WinForms rows.
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("building and running the WinForms app needs the Windows Desktop SDK (Windows only); steps [1]-[3] passed");

        var dir = Path.Combine(Path.GetTempPath(), "bl-menu-vsidiom-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(dir, "app");
        Directory.CreateDirectory(app);
        try
        {
            File.WriteAllText(Path.Combine(app, "MenuForm.cs"), csharp);
            File.WriteAllText(Path.Combine(app, "app.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0-windows</TargetFramework>
                    <UseWindowsForms>true</UseWindowsForms>
                    <Nullable>disable</Nullable>
                    <AssemblyName>MenuVsIdiomApp</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
            // ⚠ The DRIVER is the only hand-written part, as in FormComponentAcceptanceTests. No
            // Show()/message pump needed here: Poke() reads menuStrip1.Items.Count, set entirely
            // inside the constructor.
            File.WriteAllText(Path.Combine(app, "Driver.cs"), """
                using System;
                using GeneratedCode;

                internal static class Driver
                {
                    [STAThread]
                    private static void Main()
                    {
                        var form = new MenuForm();
                        form.Poke();
                    }
                }
                """);
            TestContext.Out.WriteLine("[4] wrote app.csproj + Driver.cs");

            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
            TestContext.Out.WriteLine($"[5] dotnet build  -> {buildExit}");
            Assert.That(buildExit, Is.Zero, $"the WinForms app did not build.\n{buildOut}\n{buildErr}");

            var exe = Directory.GetFiles(app, "MenuVsIdiomApp.exe", SearchOption.AllDirectories).FirstOrDefault();
            Assert.That(exe, Is.Not.Null, "no executable was produced");

            var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 60_000);
            TestContext.Out.WriteLine($"[6] run           -> exit {runExit}\n{runOut.Trim()}");

            Assert.Multiple(() =>
            {
                Assert.That(runExit, Is.Zero, runErr);
                Assert.That(runOut.Trim(), Is.EqualTo("ITEMS 2"), "the menu's populated item count must print");
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// The M3 shape — a declared array-typed local built from <c>New ToolStripItem() {…}</c> — through
    /// the real CLI and csc. Compile-only (spec §9's "M3 through CLI + csc" gate row): it exercises the
    /// array <c>Equals</c> path the argument shape does not, and needs no separate run assertion since
    /// <see cref="WinForms_TheVsIdiom_BuildsUnderCsc_AndRuns"/> already proves the runtime behaviour.
    /// </summary>
    [Test]
    public void WinForms_TheDeclaredArrayShape_BuildsUnderCsc()
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(MenuFormDeclaredArrayShape);
        TestContext.Out.WriteLine("[1] compiled the M3 declared-array-shape program through the real CLI, optimizer on");

        WinFormsCompile.AssertCompiles(csharp,
            "the M3 shape (Dim items() As ToolStripItem = New ToolStripItem() {mnuFile, sep}; " +
            "menuStrip1.Items.AddRange(items)) must compile under csc");
        TestContext.Out.WriteLine("[2] csc accepts the generated C#");
    }

    /// <summary>
    /// ⛔ The single-file CLI refuses two source files before parsing (Program.cs:194-202), so this
    /// goes through the PROJECT route: two <c>.bas</c> files, one <c>.blproj</c>, <c>build</c>. Pins
    /// <c>IsUnresolvableNetType</c>'s <c>!IsUserDefinedTypeName</c> guard (spec §9, SemanticAnalyzer.cs
    /// ~6732): a sibling-file BasicLang class must still be recognised as user-defined — never judged
    /// "csc decides" — so <c>New Shape() {aString}</c> is refused with the SAME message the single-file
    /// analyzer tests pin (<c>TypedArrayLiteralTests.TypedLiteral_RefusesAnUnrelatedElement</c>).
    /// Both halves matter: the exit code alone is also what a plain multi-file refusal gives.
    /// </summary>
    [Test]
    public void TwoFiles_ASiblingClass_IsNotExemptedAsANetType()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-menu-twofile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Shape.bas"), "Public Class Shape\nEnd Class");
            File.WriteAllText(Path.Combine(dir, "Main.bas"),
                "Public Class Prog\nPublic Shared Sub Main()\nDim s As String = \"a\"\nDim all() As Shape = New Shape() {s}\nEnd Sub\nEnd Class");
            File.WriteAllText(Path.Combine(dir, "App.blproj"), """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>CSharp</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Shape.bas" />
                    <Compile Include="Main.bas" />
                  </ItemGroup>
                </BasicLangProject>
                """);
            TestContext.Out.WriteLine("[1] wrote Shape.bas, Main.bas, App.blproj (two source files, one project)");

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "build", "App.blproj" }, dir, 120_000);
            TestContext.Out.WriteLine($"[2] build exit -> {exit}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Not.Zero, "a String cannot go into a Shape() — the build must fail");
                Assert.That(stderr, Does.Contain("cannot put a 'String' in a 'Shape()'"),
                    "the sibling-file Shape must be recognised as user-defined, not exempted as an unresolvable .NET type");
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
