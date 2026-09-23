using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 24 (commit 24d) — wiring the Type Here editor into
/// <c>CodeEditorDocumentView.axaml</c>: <c>designer:FormCanvasControl</c> gains
/// <c>x:Name="DesignCanvas"</c>, <c>TypeHereHost</c> and <c>BeginTypeHereCommand</c>, and a new
/// <c>designer:FormTypeHereEditor</c> sits in the same grid cell, after the canvas in document
/// order, wired to <see cref="CodeEditorDocumentViewModel.StripEditor"/> and the three generated
/// Type Here commands.
///
/// <para>⛔⛔ BLOCKER 2 (plan pre-flight, 24d): this repo has no compiled bindings
/// (<c>AvaloniaUseCompiledBindingsByDefault</c> appears nowhere and there is no
/// <c>Directory.Build.props</c>), so every <c>{Binding ...}</c> here is a REFLECTION binding — it
/// resolves to nothing, silently, exactly like the <c>AddNewFormCommand</c> defect. A string
/// comparison on the attribute text cannot see that. So every view-model-sourced binding path
/// collected off the parsed XML is walked BY REFLECTION, segment by segment, against
/// <see cref="CodeEditorDocumentViewModel"/> — and every target attribute name is checked against
/// the control's own public properties too, because a typo'd attribute name is as dead as a typo'd
/// path.</para>
///
/// <para>⛔ NO PASS-BY-ABSENCE (the <c>FormTrayViewTests</c> lesson): a gate that cannot find its
/// file, or cannot find either element, fails. It never <c>Assert.Ignore</c>s — see the note at the
/// bottom of this file on why <c>FormTrayViewTests</c>' own <c>Assert.Ignore</c> should probably not
/// stay that way either.</para>
/// </summary>
[TestFixture]
public class FormStripViewTests
{
    private static readonly Regex BindingRegex = new(@"^\{Binding\s*([^,}]*)", RegexOptions.Compiled);

    private XDocument _axaml = null!;
    private XElement _canvas = null!;
    private XElement _editor = null!;

    [OneTimeSetUp]
    public void LoadAxaml()
    {
        var axamlPath = FindRepoFile("VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml");
        if (axamlPath == null)
        {
            Assert.Fail("CodeEditorDocumentView.axaml not found from the test base directory — " +
                        "a gate that cannot find its file must FAIL, not report success.");
            return;
        }

        _axaml = XDocument.Load(axamlPath);

        _canvas = _axaml.Descendants().FirstOrDefault(e => e.Name.LocalName == "FormCanvasControl")!;
        Assert.That(_canvas, Is.Not.Null, "no designer:FormCanvasControl element in the design view");

        _editor = _axaml.Descendants().FirstOrDefault(e => e.Name.LocalName == "FormTypeHereEditor")!;
        Assert.That(_editor, Is.Not.Null,
            "no designer:FormTypeHereEditor element — the Type Here overlay (Task 22) is unwired, " +
            "the exact shape of the five earlier 'complete, tested and unreachable' defects in this feature");
    }

    // ---- 1. The plan's attribute checks (string-level; necessary but not sufficient) ----

    [Test]
    public void Canvas_HasName_AndTheTwoNewBindings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(XName("x", _canvas, "Name"), Is.EqualTo("DesignCanvas"));
            Assert.That((string?)_canvas.Attribute("TypeHereHost"), Is.EqualTo("{Binding StripEditor.Host}"));
            Assert.That((string?)_canvas.Attribute("BeginTypeHereCommand"), Is.EqualTo("{Binding BeginTypeHereCommand}"));
        });
    }

    [Test]
    public void Editor_IsInTheSameGridCell_AfterTheCanvasInDocumentOrder()
    {
        var canvasParent = _canvas.Parent;
        var editorParent = _editor.Parent;
        Assert.That(editorParent, Is.SameAs(canvasParent), "the editor must sit in the SAME Grid as the canvas");

        Assert.Multiple(() =>
        {
            Assert.That((string?)_canvas.Attribute("Grid.Row") ?? "0", Is.EqualTo("0"),
                "the canvas is expected in Grid.Row=\"0\" (unset defaults to row 0)");
            Assert.That((string?)_editor.Attribute("Grid.Row") ?? "0", Is.EqualTo("0"),
                "the editor must share Grid.Row=\"0\" with the canvas, or it will not paint above it");
        });

        var siblings = canvasParent!.Elements().ToList();
        var canvasIndex = siblings.IndexOf(_canvas);
        var editorIndex = siblings.IndexOf(_editor);
        Assert.That(editorIndex, Is.GreaterThan(canvasIndex),
            "the editor must come AFTER the canvas in document order, so it paints above it");
    }

    [Test]
    public void Editor_HasName_AndAllSevenAttributes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(XName("x", _editor, "Name"), Is.EqualTo("TypeHereEditor"));
            Assert.That((string?)_editor.Attribute("IsActive"), Is.EqualTo("{Binding StripEditor.IsActive}"));
            Assert.That((string?)_editor.Attribute("Host"), Is.EqualTo("{Binding StripEditor.Host}"));
            Assert.That((string?)_editor.Attribute("Text"), Is.EqualTo("{Binding StripEditor.Text, Mode=TwoWay}"));
            Assert.That((string?)_editor.Attribute("SlotBounds"), Is.EqualTo("{Binding #DesignCanvas.TypeHereBounds}"));
            Assert.That((string?)_editor.Attribute("CommitCommand"), Is.EqualTo("{Binding CommitTypeHereCommand}"));
            Assert.That((string?)_editor.Attribute("CancelCommand"), Is.EqualTo("{Binding CancelTypeHereCommand}"));
        });
    }

    // ---- 2. BLOCKER 2 — reflection resolution of every view-model-sourced binding ----

    [Test]
    public void EveryViewModelBinding_OnCanvasOrEditor_ResolvesByReflection()
    {
        var vmType = typeof(CodeEditorDocumentViewModel);
        var failures = new List<string>();

        foreach (var (element, elementLabel) in new[] { (_canvas, "FormCanvasControl"), (_editor, "FormTypeHereEditor") })
        {
            foreach (var attr in element.Attributes())
            {
                var path = ExtractBindingPath(attr.Value);
                if (path == null) continue;                 // not a {Binding ...} attribute
                if (path.StartsWith("#")) continue;          // element-name form — handled separately

                var prop = ResolvePath(vmType, path, out var failingSegment);
                if (prop == null)
                {
                    failures.Add($"{elementLabel}.{attr.Name.LocalName}=\"{attr.Value}\": " +
                                 $"'{failingSegment}' is not a public property reachable from " +
                                 $"{vmType.Name} along '{path}'");
                    continue;
                }

                if (attr.Name.LocalName.EndsWith("Command", StringComparison.Ordinal))
                {
                    Assert.That(typeof(ICommand).IsAssignableFrom(prop.PropertyType), Is.True,
                        $"{elementLabel}.{attr.Name.LocalName}=\"{attr.Value}\": resolved property " +
                        $"'{path}' has type {prop.PropertyType.Name}, which is not an ICommand");
                }
            }
        }

        Assert.That(failures, Is.Empty, () => "Reflection binding failures (reflection bindings resolve to " +
            "nothing when wrong — this is the AddNewFormCommand failure mode):\n" + string.Join("\n", failures));
    }

    [Test]
    public void TheElementNameBinding_SlotBounds_ResolvesAgainstFormCanvasControl()
    {
        var raw = (string?)_editor.Attribute("SlotBounds");
        var path = ExtractBindingPath(raw);
        Assert.That(path, Is.Not.Null.And.StartsWith("#"));

        var dot = path!.IndexOf('.');
        Assert.That(dot, Is.GreaterThan(0), $"'{path}' is not of the form #ElementName.Property");
        var elementName = path[1..dot];
        var memberPath = path[(dot + 1)..];

        Assert.That(elementName, Is.EqualTo("DesignCanvas"));

        var prop = ResolvePath(typeof(FormCanvasControl), memberPath, out var failingSegment);
        Assert.That(prop, Is.Not.Null,
            $"'{failingSegment}' is not a public property of FormCanvasControl along '{memberPath}'");
    }

    // ---- Target side: every attribute NAME on the two elements is a real property on the control ----

    [Test]
    public void EveryAttributeName_OnCanvasOrEditor_IsARealPropertyOfTheControlType()
    {
        var failures = new List<string>();

        foreach (var (element, controlType) in new[]
                 {
                     (_canvas, typeof(FormCanvasControl)),
                     (_editor, typeof(FormTypeHereEditor))
                 })
        {
            foreach (var attr in element.Attributes())
            {
                var localName = attr.Name.LocalName;
                if (attr.Name.Namespace == System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml").Namespace
                    && localName == "Name")
                {
                    continue; // x:Name — not a CLR property
                }

                if (localName.Contains('.'))
                {
                    continue; // an attached property (Grid.Row, Grid.Column, …), not this control's own
                }

                var prop = controlType.GetProperty(localName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                {
                    failures.Add($"{controlType.Name} has no public property '{localName}' " +
                                 $"(attribute value \"{attr.Value}\") — a typo here is an AXAML compile " +
                                 "error, but the gate should name it too");
                }
            }
        }

        Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
    }

    // ---- helpers ----

    private static string? ExtractBindingPath(string? attrValue)
    {
        if (attrValue == null) return null;
        var m = BindingRegex.Match(attrValue.Trim());
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static PropertyInfo? ResolvePath(Type rootType, string dottedPath, out string? failingSegment)
    {
        var segments = dottedPath.Split('.');
        var currentType = rootType;
        PropertyInfo? prop = null;
        foreach (var seg in segments)
        {
            prop = currentType.GetProperty(seg, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                failingSegment = seg;
                return null;
            }

            currentType = prop.PropertyType;
        }

        failingSegment = null;
        return prop;
    }

    private static string? XName(string prefix, XElement element, string localName)
    {
        var ns = prefix == "x" ? "http://schemas.microsoft.com/winfx/2006/xaml" : "";
        return (string?)element.Attribute(System.Xml.Linq.XName.Get(localName, ns));
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
