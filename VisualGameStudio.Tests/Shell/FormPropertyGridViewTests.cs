using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Spec §3/§8 — the extracted grid view (<c>FormPropertyGridView.axaml</c>). ⛔⛔ This repo has NO
/// compiled bindings: every {Binding} is a REFLECTION binding that resolves to nothing, silently, when
/// wrong (the AddNewFormCommand failure mode). So every path is walked BY REFLECTION against the type
/// in SCOPE — the view model at the root, each DataTemplate's DataType inside it. ⛔ A missing file
/// FAILS; it never Assert.Ignores.
/// </summary>
[TestFixture]
public class FormPropertyGridViewTests
{
    private static readonly Regex BindingPath = new(@"^\{Binding\s*([^,}]*)", RegexOptions.Compiled);
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Load(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            dir = dir.Parent;
        }

        Assert.Fail($"{string.Join("/", parts)} not found — a gate that cannot find its file must FAIL.");
        return null!;
    }

    private static XDocument GridView() => Load("VisualGameStudio.Shell", "Views", "Controls", "FormPropertyGridView.axaml");

    private static XDocument DocumentView() => Load("VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml");

    private static Type? ResolveType(XElement scope, string qualified)
    {
        var name = qualified.Trim();
        if (name.StartsWith("{x:Type", StringComparison.Ordinal))
        {
            name = name["{x:Type".Length..].TrimEnd('}').Trim();
        }

        var colon = name.IndexOf(':');
        var prefix = colon < 0 ? "" : name[..colon];
        var local = colon < 0 ? name : name[(colon + 1)..];
        var ns = scope.GetNamespaceOfPrefix(prefix)?.NamespaceName ?? "";
        var clr = ns.StartsWith("using:", StringComparison.Ordinal) ? ns["using:".Length..] : ns;
        return typeof(FormPropertyGridViewModel).Assembly.GetType(clr + "." + local)
               ?? typeof(VisualGameStudio.Core.Abstractions.ViewModels.ITypedValueRow).Assembly.GetType(clr + "." + local);
    }

    private static PropertyInfo? Resolve(Type root, string path, out string? failing)
    {
        var type = root;
        PropertyInfo? property = null;
        foreach (var segment in path.Split('.'))
        {
            property = type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
            {
                failing = segment;
                return null;
            }

            type = property.PropertyType;
        }

        failing = null;
        return property;
    }

    /// <summary>Walks the tree carrying the scope type; a DataTemplate re-scopes to its DataType.</summary>
    private static void Walk(XElement element, Type scope, List<string> failures, ref int checkedBindings)
    {
        if (element.Name.LocalName == "DataTemplate")
        {
            var declared = (string?)element.Attribute("DataType") ?? (string?)element.Attribute(XName.Get("DataType", Xaml));
            var type = declared == null ? null : ResolveType(element, declared);
            if (type == null)
            {
                failures.Add($"a DataTemplate declares DataType '{declared}', which does not resolve to a type");
                return;
            }

            scope = type;
        }

        foreach (var attribute in element.Attributes())
        {
            var match = BindingPath.Match(attribute.Value.Trim());
            if (!match.Success)
            {
                continue;
            }

            var path = match.Groups[1].Value.Trim();
            if (path.Length == 0 || path.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            path = path.TrimStart('!');
            checkedBindings++;
            if (Resolve(scope, path, out var failing) == null)
            {
                failures.Add($"<{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\">: " +
                             $"'{failing}' is not a public property of {scope.Name} along '{path}'");
            }
        }

        foreach (var child in element.Elements())
        {
            Walk(child, scope, failures, ref checkedBindings);
        }
    }

    [Test]
    public void EveryBindingInTheGridView_ResolvesAgainstTheTypeInScope()
    {
        var failures = new List<string>();
        var checkedBindings = 0;

        Walk(GridView().Root!, typeof(FormPropertyGridViewModel), failures, ref checkedBindings);

        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        // ⚠ Not vacuous: a view with no bindings (or a walk that stopped at the root) resolves "every"
        // binding too. The view carries the selector, toolbar, search, description, list, header and row.
        Assert.That(checkedBindings, Is.GreaterThanOrEqualTo(25),
            "the walk checked too few bindings to be a gate on the grid view");
    }

    [Test]
    public void TheGridView_DeclaresTheGridViewModelAsItsDataType()
    {
        var root = GridView().Root!;

        Assert.That(ResolveType(root, (string)root.Attribute(XName.Get("DataType", Xaml))!),
            Is.EqualTo(typeof(FormPropertyGridViewModel)));
    }

    [Test]
    public void EachListItemKind_HasItsOwnTemplateMatchedByDataType()
    {
        var templates = GridView().Descendants().Where(e => e.Name.LocalName == "DataTemplate").ToList();

        // ⛔ The plain DataType attribute, not x:DataType: ListBox.DataTemplates matches an item by
        // DataType, and x:DataType is only the binding scope.
        var types = templates.Select(t => ResolveType(t, (string?)t.Attribute("DataType") ?? "")).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(types, Does.Contain(typeof(FormPropertyRow)));
            Assert.That(types, Does.Contain(typeof(FormPropertyCategoryHeader)));
            Assert.That(types, Does.Contain(typeof(FormObjectItem)));
        });
    }

    /// <summary>
    /// ⛔ The list binds <see cref="FormPropertyGridViewModel.SelectedItem"/> (a header OR a row), never
    /// <see cref="FormPropertyGridViewModel.SelectedRow"/>: DisplayItems carries headers, and a header
    /// pushed into a FormPropertyRow-typed property is a binding error that leaves the pane stale.
    /// SelectedRow is derived from SelectedItem and drives the description pane.
    /// </summary>
    [Test]
    public void TheList_BindsDisplayItemsAndSelectedItem()
    {
        var list = GridView().Descendants().SingleOrDefault(e => e.Name.LocalName == "ListBox");

        Assert.That(list, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That((string?)list!.Attribute("ItemsSource"), Is.EqualTo("{Binding DisplayItems}"));
            Assert.That((string?)list.Attribute("SelectedItem"), Is.EqualTo("{Binding SelectedItem, Mode=TwoWay}"));
        });
    }

    [Test]
    public void TheDocumentView_HostsTheGridView_BoundToPropertyGrid()
    {
        var host = DocumentView().Descendants().SingleOrDefault(e => e.Name.LocalName == "FormPropertyGridView");

        Assert.That(host, Is.Not.Null, "the grid is extracted into its own view (spec §3)");
        Assert.That((string?)host!.Attribute("DataContext"), Is.EqualTo("{Binding PropertyGrid}"));
        Assert.That(Resolve(typeof(CodeEditorDocumentViewModel), "PropertyGrid", out _)?.PropertyType,
            Is.EqualTo(typeof(FormPropertyGridViewModel)));
    }

    /// <summary>
    /// The grid MOVED, it was not copied: two copies would be two editors for one value. ⚠ Not vacuous —
    /// these bindings exist in the document view at 91f1343e (:323-342).
    /// </summary>
    [Test]
    public void TheDocumentView_NoLongerCarriesTheGridsOwnBindings()
    {
        var text = DocumentView().ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("PropertyGrid.Rows"));
            Assert.That(text, Does.Not.Contain("PropertyGrid.SelectedRow"));
            Assert.That(text, Does.Not.Contain("PropertyGrid.DescriptionBody"));
            Assert.That(text, Does.Not.Contain("PropertyGrid.Header"));
        });
    }

    // ==================================================================
    // Hosted headless — the bindings LIVE, not just resolvable
    // ==================================================================

    private const string Doc = """
        <Form Name="F" Version="1" Width="400" Height="300" Text="Hello">
          <Controls>
            <Label Id="lbl" X="0" Y="0" Width="10" Height="10" TabIndex="0" Text="Hi"/>
          </Controls>
          <Components/>
        </Form>
        """;

    private static (FormPropertyGridViewModel Grid, FormPropertyGridView View, Window Window) Host()
    {
        var file = FormDocumentReader.Read("F.blform", Doc);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("lbl");

        var view = new FormPropertyGridView { DataContext = grid };
        // ⚠ Tall enough that the ListBox realises EVERY row: a virtualised row is not in the visual tree.
        var window = new Window { Width = 320, Height = 2400, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (grid, view, window);
    }

    /// <summary>
    /// ⚠ Two presses at the SAME point with no virtual clock between them arrive as a double-click
    /// (measured in FormDesignerRealViewTests) — and whether they do depends on how fast the run is, so
    /// a test that repeats a click passes alone and fails in the full suite. <paramref name="dx"/> moves
    /// each repeat to a different point inside the same button.
    /// </summary>
    private static void Click(Window window, Control target, double dx = 0)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2 + dx, target.Bounds.Height / 2), window)
                 ?? throw new InvalidOperationException($"{target.Name} is not in the window's visual tree");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// ⚠ The sort buttons are RADIOS wearing the ToggleButton theme. A bare ToggleButton unchecks on a
    /// second click, so clicking the mode already shown flipped to the other one. Each click is from a
    /// fresh point (+x) so the headless pipeline does not stamp the second one a double-click.
    /// </summary>
    [AvaloniaTest]
    public void TheSortButtons_ClickingTheModeAlreadyShown_KeepsIt()
    {
        var (grid, view, window) = Host();
        // ToggleButton, the base both shapes share: the test judges the BEHAVIOUR, not the element name.
        var categorized = view.FindControl<ToggleButton>("CategorizedButton")!;
        var alphabetical = view.FindControl<ToggleButton>("AlphabeticalButton")!;
        Assert.That(categorized.IsChecked, Is.True, "precondition: Categorized is VS's default");

        Click(window, alphabetical);
        Assert.That(grid.IsCategorized, Is.False, "clicking A-Z sorts alphabetically");
        Assert.That(categorized.IsChecked, Is.False, "the group unchecks its partner");

        Click(window, alphabetical, dx: 8);
        Assert.That(grid.IsAlphabetical, Is.True, "clicking A-Z AGAIN keeps it alphabetical");

        Click(window, categorized);
        Click(window, categorized, dx: 8);
        Assert.That(grid.IsCategorized, Is.True, "clicking Categorized twice keeps it categorized");
        Assert.That(alphabetical.IsChecked, Is.False);
    }

    /// <summary>
    /// ⛔ Two designers open in ONE window (two form tabs, a split) each have a grid view. A RadioButton
    /// <c>GroupName</c> is scoped to the whole visual ROOT, so a named group would make them one group:
    /// sorting one grid A-Z would uncheck the other's A-Z, whose binding flips THAT grid back. The sort
    /// buttons group by their parent panel instead — one group per view.
    /// </summary>
    [AvaloniaTest]
    public void TwoGridViewsInOneWindow_SortIndependently()
    {
        FormPropertyGridViewModel NewGrid()
        {
            var file = FormDocumentReader.Read("F.blform", Doc);
            var grid = new FormPropertyGridViewModel();
            grid.Load(file);
            return grid;
        }

        var (first, second) = (NewGrid(), NewGrid());
        var firstView = new FormPropertyGridView { DataContext = first };
        var secondView = new FormPropertyGridView { DataContext = second };
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(secondView, 1);
        panel.Children.Add(firstView);
        panel.Children.Add(secondView);
        var window = new Window { Width = 800, Height = 900, Content = panel };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Click(window, firstView.FindControl<ToggleButton>("AlphabeticalButton")!);
        Click(window, secondView.FindControl<ToggleButton>("AlphabeticalButton")!);
        Click(window, firstView.FindControl<ToggleButton>("CategorizedButton")!);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsCategorized, Is.True, "the first grid went back to Categorized");
            Assert.That(second.IsAlphabetical, Is.True, "…and the second grid STAYED A-Z");
        });
    }

    /// <summary>
    /// The list shows the view model's DisplayItems through the two templates, and a row's name is bold
    /// exactly when <see cref="FormPropertyRow.IsBold"/>: Text="Hi" is set and differs from the default;
    /// Enabled is absent. A reflection binding that silently bound nothing would leave both regular.
    /// </summary>
    [AvaloniaTest]
    public void TheList_ShowsHeadersAndRows_AndBoldsAChangedProperty()
    {
        var (grid, view, _) = Host();
        var list = view.FindControl<ListBox>("PropertyList")!;

        TextBlock NameCell(string name) =>
            list.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == name && t.DataContext is FormPropertyRow);

        Assert.Multiple(() =>
        {
            Assert.That(list.ItemCount, Is.EqualTo(grid.DisplayItems.Count));
            Assert.That(list.GetVisualDescendants().OfType<ToggleButton>()
                    .Count(b => b.DataContext is FormPropertyCategoryHeader), Is.GreaterThan(0),
                "category headers render through their own template");
            Assert.That(NameCell("Text").FontWeight, Is.EqualTo(FontWeight.Bold));
            Assert.That(NameCell("Enabled").FontWeight, Is.Not.EqualTo(FontWeight.Bold));
        });
    }

    /// <summary>
    /// ⚠ The two PropertyGrid bindings that deliberately STAY in the document view: the canvas's
    /// SelectedControl (the real-view tests read it) and the tray Delete's CommandParameter
    /// (FormTrayViewTests pins it). The extraction must not take them along.
    /// </summary>
    [Test]
    public void TheDocumentView_KeepsTheCanvasAndTrayBindings()
    {
        var text = DocumentView().ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("SelectedControl=\"{Binding PropertyGrid.SelectedControl, Mode=TwoWay}\""));
            Assert.That(text, Does.Contain("CommandParameter=\"{Binding PropertyGrid.SelectedControl}\""));
        });
    }
}
