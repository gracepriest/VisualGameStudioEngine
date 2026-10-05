using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Forms;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Portable-controls Task 7d: a WinForms project's .NET resolution is ARMED with the WindowsDesktop reference pack
/// (<c>NetReferenceResolver.WindowsDesktopAssemblies</c>), so <c>System.Windows.Forms</c> is knowable to BasicLang.
///
/// <para>⛔ Before, <c>EnableNetResolution</c> returned early for <c>UseWindowsForms</c>: in <c>Class F1 Inherits Form</c> a
/// bare <c>Close()</c> beside a Module's <c>Sub Close</c> was emitted <c>Util.Close()</c> (VB calls Form.Close), and a
/// misspelled member (<c>b.Textt</c>, <c>Me.Textt</c>) compiled green and was left to csc — the "WinForms catalog is
/// unfalsifiable without csc" gap CLAUDE.md records.</para>
///
/// <para>⚠ Windows-only, and SKIPS without the pack (CLAUDE.md's gate rule): the pack is installed by the Windows
/// .NET SDK, never on Linux, where the project stays un-armed exactly as before.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Platform(Include = "Win")]
public class WinFormsNetResolutionTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        if (NetReferenceResolver.WindowsDesktopAssemblies.Count == 0)
            Assert.Ignore("no WindowsDesktop reference pack under this machine's dotnet root — a WinForms project stays un-armed");
        _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-wfnet-" + Path.GetRandomFileName())).FullName;
    }

    [TearDown]
    public void TearDown()
    {
        try { if (_dir.Length > 0) Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Compiles the files as a UseWindowsForms project with resolution ARMED, as the CLI and the IDE do.</summary>
    private CompilationResult CompileWinForms(params (string Name, string Text)[] files)
    {
        var paths = files.Select(f =>
        {
            var path = Path.Combine(_dir, f.Name);
            File.WriteAllText(path, f.Text);
            return path;
        }).ToArray();
        var project = new ProjectFile { UseWindowsForms = true, FilePath = Path.Combine(_dir, "App.blproj") };
        var options = new CompilerOptions();
        options.EnableNetResolution(project, project.FilePath);
        return new BasicCompiler(options).CompileProjectFiles(paths);
    }

    private static string Messages(CompilationResult r) => string.Join(" | ", r.AllErrors.Select(e => e.Message));

    private static NetTypeResolver DesktopResolver() =>
        NetTypeResolver.Create(NetReferenceResolver.WithWindowsDesktop(
            NetReferenceResolver.FrameworkAssemblies, NetReferenceResolver.WindowsDesktopAssemblies));

    // ---- what the resolver sees (measured first, as the task asked)

    /// <summary>The armed closure resolves the WinForms surface, and "nameable" counts events and protected members —
    /// absent from the CALL surface (<c>GetMembers</c>), present all the same.</summary>
    [Test]
    public void TheArmedClosure_SeesWinForms_EventsAndProtectedMembersIncluded()
    {
        var resolver = DesktopResolver();
        Assert.Multiple(() =>
        {
            Assert.That(resolver.ResolveTypeDetailed("System.Windows.Forms.Form").Outcome, Is.EqualTo(NetTypeLookupOutcome.Resolved));
            Assert.That(resolver.DeclaresNameableMember("System.Windows.Forms.Form", "Close", includeProtected: false), Is.True);
            Assert.That(resolver.DeclaresNameableMember("System.Windows.Forms.Button", "Click", includeProtected: false), Is.True, "an event");
            Assert.That(resolver.DeclaresNameableMember("System.Windows.Forms.Button", "text", includeProtected: false), Is.True, "VB is case-insensitive");
            Assert.That(resolver.DeclaresNameableMember("System.Windows.Forms.Form", "OnLoad", includeProtected: true), Is.True, "protected, from a derived class");
            Assert.That(resolver.DeclaresNameableMember("System.Windows.Forms.Form", "OnLoad", includeProtected: false), Is.False, "protected, from outside");
            Assert.That(resolver.DeclaresNameableMember("System.Windows.Forms.Button", "Textt", includeProtected: false), Is.False);
        });
    }

    /// <summary>⛔ Three assemblies ship in BOTH the framework and the desktop pack; the DESKTOP copy must win, and only once.</summary>
    [Test]
    public void TheDesktopCopy_OfASharedAssemblyName_Wins()
    {
        var merged = NetReferenceResolver.WithWindowsDesktop(
            new[] { @"C:\fw\System.Runtime.dll", @"C:\fw\System.Drawing.dll", @"C:\fw\WindowsBase.dll" },
            new[] { @"C:\desk\System.Drawing.dll", @"C:\desk\WindowsBase.dll", @"C:\desk\System.Windows.Forms.dll" });
        Assert.That(merged, Is.EquivalentTo(new[]
        {
            @"C:\fw\System.Runtime.dll", @"C:\desk\System.Drawing.dll", @"C:\desk\WindowsBase.dll", @"C:\desk\System.Windows.Forms.dll"
        }));
    }

    // ---- Inherits Form: a bare inherited call is the Form's

    /// <summary>⛔ <c>Close()</c> in a Form subclass beside a Module <c>Sub Close</c>: emitted <c>Util.Close()</c> before. VB calls
    /// Form.Close — the emitted C# calls the inherited member, and csc accepts it against the real WinForms.</summary>
    [Test]
    public void ABareClose_InAFormSubclass_IsFormClose_NotAModuleSub()
    {
        var result = CompileWinForms(
            ("Util.bas", "Module Util\n Public Sub Close()\n  PrintLine(\"module\")\n End Sub\nEnd Module\n"),
            ("Form1.bas", "Using System.Windows.Forms\nPublic Class Form1\n Inherits Form\n Public Sub Quit()\n  Close()\n End Sub\nEnd Class\n"),
            ("Main.bas", "Sub Main()\nEnd Sub\n"));
        Assert.That(result.HasErrors, Is.False, Messages(result));

        var cs = new CSharpCodeGenerator().Generate(result.CombinedIR!);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Contain("Util.Close()"), cs);
            Assert.That(cs, Does.Contain("Close();"), cs);
        });
        WinFormsCompile.AssertCompiles(cs, "the bare Close() must bind to the inherited Form.Close.");
    }

    // ---- a misspelled WinForms member is a BasicLang error

    /// <summary>⛔ <c>b.Textt</c> on a Button compiled green and was csc's to refuse. Now BL6017 names it.</summary>
    [Test]
    public void AMisspelledMember_OnAWinFormsControl_IsABasicLangError() =>
        Assert.That(Messages(CompileWinForms(
                ("Form1.bas", "Using System.Windows.Forms\nPublic Class Form1\n Inherits Form\n Public Sub Build()\n" +
                              "  Dim b As New Button()\n  b.Textt = \"x\"\n End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\nEnd Sub\n"))),
            Does.Contain("BL6017").And.Contain("'Textt'"));

    /// <summary>⛔ <c>Me.Textt</c> in a Form subclass — the member is neither the class's nor the Form's.</summary>
    [Test]
    public void AMisspelledMember_ThroughMe_InAFormSubclass_IsABasicLangError() =>
        Assert.That(Messages(CompileWinForms(
                ("Form1.bas", "Using System.Windows.Forms\nPublic Class Form1\n Inherits Form\n Public Sub Build()\n" +
                              "  Me.Textt = \"x\"\n End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\nEnd Sub\n"))),
            Does.Contain("BL6017").And.Contain("'Textt'"));

    /// <summary>The shapes WinForms code is made of stay clean: an event wired with AddHandler, a property through Me, an
    /// inherited protected member through Me, a method, an extension method (LINQ) on a collection, the class's own member.</summary>
    [Test]
    public void ValidWinFormsShapes_HaveNoErrors() =>
        Assert.That(Messages(CompileWinForms(
                ("Form1.bas",
                    "Using System\nUsing System.Linq\nUsing System.Windows.Forms\nPublic Class Form1\n Inherits Form\n" +
                    " Private count As Integer\n" +
                    " Public Sub Build()\n  Dim b As New Button()\n  b.Text = \"Go\"\n  AddHandler b.Click, AddressOf OnGo\n" +
                    "  Me.Controls.Add(b)\n  Me.Text = \"Title\"\n  Me.OnLoad(EventArgs.Empty)\n  b.PerformClick()\n" +
                    "  Dim n As Integer = Me.Controls.OfType(Of Button)().Count()\n  Me.count = n\n End Sub\n" +
                    " Private Sub OnGo(sender As Object, e As EventArgs)\n End Sub\nEnd Class\n"),
                ("Main.bas", "Sub Main()\nEnd Sub\n"))),
            Is.Empty);

    /// <summary>Every diagnostic of either channel, any severity: the analyzer's (errors AND warnings) and the .NET one.</summary>
    private static string AllDiagnostics(CompilationResult r) =>
        string.Join(" | ", r.AllErrors.Select(e => $"{e.Severity}: {e.Message}")
            .Concat(r.NetDiagnostics.Select(d => $"{d.Code}{(d.IsWarning ? " (warning)" : "")}: {d.Message}")));

    /// <summary>
    /// ⛔ Task 7d review (CRITICAL + owner principle: arming adds NO diagnostic to a valid WinForms program). A real
    /// form: <c>MyBase.OnPaint(e)</c> / <c>MyBase.OnKeyDown(e)</c> / <c>MyBase.OnLoad(e)</c> / <c>MyBase.WndProc(m)</c> in
    /// overrides (protected — were BL6017 ERRORS), <c>lb.Items.Add</c>, <c>Controls.OfType(…).Count()</c>,
    /// <c>e.Graphics.DrawLine</c> (receivers that degrade to Object — were BL6017 warnings) and
    /// <c>Application.Run(New Form1())</c> (was BL6019). Zero diagnostics of any kind.
    /// </summary>
    [Test]
    public void AValidFormProgram_HasNoDiagnosticsAtAll_WhenArmed()
    {
        var result = CompileWinForms(
            ("Form1.bas",
                "Using System\nUsing System.Drawing\nUsing System.Linq\nUsing System.Windows.Forms\n" +
                "Public Class Form1\n Inherits Form\n Private Button1 As Button\n Private lb As ListBox\n" +
                " Public Sub New()\n  Me.Button1 = New Button()\n  Me.lb = New ListBox()\n  Me.lb.Items.Add(\"a\")\n" +
                "  Dim first As Object = Me.lb.Items.Item(0)\n  Me.Controls.Add(Me.Button1)\n" +
                "  Dim n As Integer = Me.Controls.OfType(Of Button)().Count()\n  Me.DoubleBuffered = True\n End Sub\n" +
                " Protected Overrides Sub OnPaint(e As PaintEventArgs)\n  MyBase.OnPaint(e)\n  e.Graphics.DrawLine(Pens.Black, 0, 0, 10, 10)\n  Me.Invalidate()\n End Sub\n" +
                " Protected Overrides Sub OnKeyDown(e As KeyEventArgs)\n  If e.KeyCode = Keys.Enter Then\n   Me.Close()\n  End If\n  MyBase.OnKeyDown(e)\n End Sub\n" +
                " Protected Overrides Sub OnLoad(e As EventArgs)\n  MyBase.OnLoad(e)\n End Sub\n" +
                " Protected Overrides Sub WndProc(ByRef m As Message)\n  MyBase.WndProc(m)\n End Sub\nEnd Class\n"),
            ("Main.bas", "Using System.Windows.Forms\nSub Main()\n Application.EnableVisualStyles()\n Application.Run(New Form1())\nEnd Sub\n"));
        Assert.That(AllDiagnostics(result), Is.Empty);
    }

    // ---- the catalog: every control, every property, through the ARMED compiler — no false error

    private static IEnumerable<TestCaseData> EveryWinFormsControl() =>
        FormControlCatalog.All
            .Where(c => c.SupportsTarget(FormTarget.WinForms))
            .Select(c => new TestCaseData(c.Kind).SetName("{m}(" + c.Kind + ")"));

    /// <summary>
    /// The designer's own output for every catalog control with every property set, compiled by the ARMED compiler:
    /// no BL6017 — the catalog sweep's csc gate, now asked of BasicLang itself. A false error here would break every
    /// form the designer writes.
    /// </summary>
    [TestCaseSource(nameof(EveryWinFormsControl))]
    public void EveryProperty_OfEveryControl_CompilesCleanWhenArmed(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "SweepForm", Width = 800, Height = 450, Text = "Sweep" };
        var control = FormCatalogShapes.Canonical(form, definition, "ctl");
        foreach (var property in definition.Properties)
            control.Properties[property.Name] = WinFormsCatalogSweepTests.SampleValueFor(property);

        var plan = FormHandlers.PlanDefault(form, control, FormScaffolder.Create("SweepForm", FormTarget.WinForms).CodeText);
        var code = plan.Outcome == HandlerOutcome.Created ? plan.CodeText : FormScaffolder.Create("SweepForm", FormTarget.WinForms).CodeText;
        if (plan.Outcome == HandlerOutcome.Created) FormHandlers.EnsureBind(control, plan.EventName, plan.Handler);

        var written = RegionWriter.Write("SweepForm.bas", code, form, "SweepForm.blform");
        Assert.That(written.Refused, Is.False, string.Join("; ", written.Diagnostics.Select(d => d.Format())));

        var result = CompileWinForms(("SweepForm.bas", written.Text));
        Assert.That(Messages(result), Is.Empty, $"--- generated ---\n{written.Text}");
    }
}
