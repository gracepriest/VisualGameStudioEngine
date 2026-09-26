using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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

    /// <summary>
    /// Walks the tree carrying the scope type; a DataTemplate re-scopes to its DataType.
    ///
    /// <para>⛔ Strict by construction: anything binding-shaped this walker cannot JUDGE is a failure, never a
    /// skip — an unparsed attribute mentioning Binding (CompiledBinding, ReflectionBinding, a typo), a
    /// <c>&lt;Binding&gt;</c>/<c>&lt;MultiBinding&gt;</c> element, an element (<c>#name</c>) or relative
    /// (<c>$parent</c>) source, and a nested <c>DataContext="{Binding …}"</c>, which re-scopes everything
    /// under it to a type this walker does not track. Each is supportable; none is used today, so the
    /// first one to arrive must teach the walker about it rather than slip past it. A
    /// <c>Mode=TwoWay</c> binding also needs a public SETTER — a getter-only target compiles, renders,
    /// and silently never writes back.</para>
    /// </summary>
    private static void Walk(XElement element, Type scope, List<string> failures, ref int checkedBindings)
    {
        var local = element.Name.LocalName;
        if (local is "Binding" or "MultiBinding" or "CompiledBinding" or "ReflectionBinding")
        {
            failures.Add($"a <{local}> element — this checker judges only attribute {{Binding}}s; teach it this shape");
            return;
        }

        if (local == "DataTemplate")
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
            var value = attribute.Value.Trim();
            var where = $"<{local} {attribute.Name.LocalName}=\"{attribute.Value}\">";
            var match = BindingPath.Match(value);
            if (!match.Success)
            {
                if (value.Contains("Binding", StringComparison.Ordinal))
                {
                    failures.Add($"{where}: mentions Binding but is not a {{Binding …}} this checker can parse");
                }

                continue;
            }

            if (attribute.Name.LocalName == "DataContext")
            {
                failures.Add($"{where}: a nested DataContext re-scopes everything under it, which this checker does not track");
                continue;
            }

            var path = match.Groups[1].Value.Trim().TrimStart('!');
            if (path.Length == 0)
            {
                continue; // {Binding}: the scope object itself — nothing to resolve.
            }

            if (path.StartsWith('#') || path.StartsWith('$') || path.Contains('='))
            {
                failures.Add($"{where}: an element, relative or Path= source — not judged by this checker; teach it this shape");
                continue;
            }

            checkedBindings++;
            var property = Resolve(scope, path, out var failing);
            if (property == null)
            {
                failures.Add($"{where}: '{failing}' is not a public property of {scope.Name} along '{path}'");
            }
            else if (Regex.IsMatch(value, @"\bMode\s*=\s*TwoWay\b") && property.SetMethod is not { IsPublic: true })
            {
                failures.Add($"{where}: Mode=TwoWay, but '{path}' on {scope.Name} has no public setter");
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

    /// <summary>
    /// The walker's own gate: each binding shape it cannot judge FAILS rather than passing unexamined.
    /// Snippets are scoped to <see cref="FormPropertyGridViewModel"/>, like the real view.
    /// </summary>
    [TestCase("<TextBlock Text=\"{CompiledBinding SearchText}\"/>", "not a {Binding")]
    [TestCase("<TextBlock Text=\"{ReflectionBinding SearchText}\"/>", "not a {Binding")]
    [TestCase("<TextBlock><TextBlock.Text><Binding Path=\"SearchText\"/></TextBlock.Text></TextBlock>", "<Binding> element")]
    [TestCase("<TextBlock><TextBlock.Text><MultiBinding/></TextBlock.Text></TextBlock>", "<MultiBinding> element")]
    [TestCase("<Border DataContext=\"{Binding SelectedRow}\"><TextBlock Text=\"{Binding Name}\"/></Border>", "nested DataContext")]
    [TestCase("<TextBlock Text=\"{Binding #SearchBox.Text}\"/>", "element, relative or Path=")]
    [TestCase("<TextBlock Text=\"{Binding $parent.Tag}\"/>", "element, relative or Path=")]
    [TestCase("<TextBlock Text=\"{Binding Path=SearchText}\"/>", "element, relative or Path=")]
    [TestCase("<TextBox Text=\"{Binding Header, Mode=TwoWay}\"/>", "no public setter")]
    [TestCase("<TextBlock Text=\"{Binding NoSuchProperty}\"/>", "is not a public property")]
    public void TheWalker_FailsOnEveryShapeItCannotJudge(string snippet, string expected)
    {
        var root = XElement.Parse(
            $"<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"{Xaml}\">{snippet}</UserControl>");
        var failures = new List<string>();
        var checkedBindings = 0;

        Walk(root, typeof(FormPropertyGridViewModel), failures, ref checkedBindings);

        Assert.That(failures, Has.Some.Contains(expected), string.Join("\n", failures));
    }

    [Test]
    public void TheWalker_AcceptsAWritableTwoWayBinding()
    {
        var root = XElement.Parse(
            $"<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"{Xaml}\">" +
            "<TextBox Text=\"{Binding SearchText, Mode=TwoWay}\"/></UserControl>");
        var failures = new List<string>();
        var checkedBindings = 0;

        Walk(root, typeof(FormPropertyGridViewModel), failures, ref checkedBindings);

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            Assert.That(checkedBindings, Is.EqualTo(1));
        });
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

    /// <summary>
    /// A screen reader needs a NAME: "A-Z" says nothing, and the nine anchor/dock edge toggles have no
    /// content at all. Each carries AutomationProperties.Name; the edge toggles mirror their tooltip.
    /// </summary>
    [Test]
    public void TheToolbarSearchSelectorAndEdgeToggles_CarryAnAutomationName()
    {
        var elements = GridView().Descendants().ToList();
        const string Automation = "AutomationProperties.Name";

        string? NameOf(string xName) =>
            (string?)elements.Single(e => (string?)e.Attribute(XName.Get("Name", Xaml)) == xName).Attribute(Automation);

        var edgeToggles = elements
            .Where(e => ((string?)e.Attribute("ToolTip.Tip"))?.StartsWith("Anchor to", StringComparison.Ordinal) == true
                        || ((string?)e.Attribute("ToolTip.Tip"))?.StartsWith("Dock to", StringComparison.Ordinal) == true
                        || (string?)e.Attribute("ToolTip.Tip") == "Fill the container")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(NameOf("AlphabeticalButton"), Is.EqualTo("Alphabetical"));
            Assert.That(NameOf("CategorizedButton"), Is.EqualTo("Categorized"));
            Assert.That(NameOf("SearchBox"), Is.EqualTo("Search properties"));
            Assert.That(NameOf("ObjectSelector"), Is.Not.Null.And.Not.Empty);
            Assert.That(edgeToggles, Has.Count.EqualTo(9), "four anchor edges, four dock edges and Fill");
            foreach (var toggle in edgeToggles)
            {
                Assert.That((string?)toggle.Attribute(Automation), Is.EqualTo((string?)toggle.Attribute("ToolTip.Tip")));
            }
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

    /// <summary>
    /// The IDE's own brushes, as distinct known colours: the headless app loads FluentTheme alone, so without
    /// these every <c>{DynamicResource Ide…}</c> resolves to NOTHING — and "two headers draw the same
    /// background" would pass with both backgrounds null.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IBrush> IdeBrushes = new Dictionary<string, IBrush>
    {
        ["IdeBg"] = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30)),
        ["IdePanelBg"] = new ImmutableSolidColorBrush(Color.FromRgb(0x11, 0x21, 0x31)),
        ["IdeBorder"] = new ImmutableSolidColorBrush(Color.FromRgb(0x12, 0x22, 0x32)),
        ["IdeHeaderBg"] = new ImmutableSolidColorBrush(Color.FromRgb(0x13, 0x23, 0x33)),
        ["IdeFg"] = new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
    };

    private static Window NewWindow(Control content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        foreach (var (key, brush) in IdeBrushes)
        {
            window.Resources[key] = brush;
        }

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// A hosted view. ⛔ DISPOSE it: an unclosed headless Window stays alive for the rest of the run with
    /// its bindings live, and views piling up across tests is the likelier cause of the one-off flake this
    /// fixture once had (see the pre-flight's Task 13 execution note).
    /// </summary>
    private sealed class Hosted : IDisposable
    {
        public required FormPropertyGridViewModel Grid { get; init; }
        public required FormPropertyGridView View { get; init; }
        public required Window Window { get; init; }

        public ListBox List => View.FindControl<ListBox>("PropertyList")!;

        public ListBoxItem Container(object item) =>
            (ListBoxItem?)List.ContainerFromItem(item)
            ?? throw new InvalidOperationException($"{item} is not realised — scroll it into view first");

        public FormPropertyRow Row(string name) => Grid.Rows.Single(r => r.Name == name);

        public FormPropertyCategoryHeader Header(string name) =>
            Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == name);

        /// <summary>The header's toggle, found through its container — never a stale instance.</summary>
        public ToggleButton HeaderButton(FormPropertyCategoryHeader header) =>
            Container(header).GetVisualDescendants().OfType<ToggleButton>().First();

        public void Dispose() => Window.Close();
    }

    /// <param name="height">⚠ The default is tall enough that the ListBox realises EVERY row: a virtualised
    /// row is not in the visual tree. Pass a short height to test virtualisation itself.</param>
    private static Hosted Host(double height = 2400)
    {
        var file = FormDocumentReader.Read("F.blform", Doc);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("lbl");

        var view = new FormPropertyGridView { DataContext = grid };
        return new Hosted { Grid = grid, View = view, Window = NewWindow(view, 320, height) };
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers);
        window.KeyRelease(key, modifiers);
        Dispatcher.UIThread.RunJobs();
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
        using var host = Host();
        var (grid, view, window) = (host.Grid, host.View, host.Window);
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
        var window = NewWindow(panel, 800, 900);
        try
        {
            Click(window, firstView.FindControl<ToggleButton>("AlphabeticalButton")!);
            Click(window, secondView.FindControl<ToggleButton>("AlphabeticalButton")!);
            Click(window, firstView.FindControl<ToggleButton>("CategorizedButton")!);

            Assert.Multiple(() =>
            {
                Assert.That(first.IsCategorized, Is.True, "the first grid went back to Categorized");
                Assert.That(second.IsAlphabetical, Is.True, "…and the second grid STAYED A-Z");
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The list shows the view model's DisplayItems through the two templates, and a row's name is bold
    /// exactly when <see cref="FormPropertyRow.IsBold"/>: Text="Hi" is set and differs from the default;
    /// Enabled is absent. A reflection binding that silently bound nothing would leave both regular.
    /// </summary>
    [AvaloniaTest]
    public void TheList_ShowsHeadersAndRows_AndBoldsAChangedProperty()
    {
        using var host = Host();
        var list = host.List;

        TextBlock NameCell(string name) =>
            host.Container(host.Row(name)).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name);

        Assert.Multiple(() =>
        {
            Assert.That(list.ItemCount, Is.EqualTo(host.Grid.DisplayItems.Count));
            Assert.That(list.GetVisualDescendants().OfType<ToggleButton>()
                    .Count(b => b.DataContext is FormPropertyCategoryHeader), Is.GreaterThan(0),
                "category headers render through their own template");
            Assert.That(NameCell("Text").FontWeight, Is.EqualTo(FontWeight.Bold));
            Assert.That(NameCell("Enabled").FontWeight, Is.Not.EqualTo(FontWeight.Bold));
        });
    }

    /// <summary>
    /// Greyed = the row shows a default the document does not carry (<see cref="FormPropertyRow.IsDefaultShown"/>).
    /// Enabled is absent on the Label; Text="Hi" is present.
    /// </summary>
    [AvaloniaTest]
    public void AnAbsentRow_IsGreyed_AndAPresentOneIsNot()
    {
        using var host = Host();

        Panel ValuePanel(string name) =>
            host.Container(host.Row(name)).GetVisualDescendants().OfType<Panel>()
                .First(p => p.GetType() == typeof(Panel) && Grid.GetColumn(p) == 2);

        Assert.Multiple(() =>
        {
            Assert.That(host.Row("Enabled").IsDefaultShown, Is.True, "precondition: Enabled is absent");
            Assert.That(ValuePanel("Enabled").Opacity, Is.LessThan(1.0));
            Assert.That(ValuePanel("Text").Opacity, Is.EqualTo(1.0));
        });
    }

    /// <summary>
    /// ⛔ An EXPANDED header is a checked ToggleButton, and Fluent paints a checked ToggleButton with the
    /// accent (<c>:checked /template/ ContentPresenter</c>), which beats a local Background. So every
    /// expanded category drew as a solid accent bar. Expanded and collapsed must draw the SAME background:
    /// the IDE's own <c>IdeBg</c>.
    /// </summary>
    [AvaloniaTest]
    public void AnExpandedHeader_DrawsTheSameBackgroundAsACollapsedOne()
    {
        using var host = Host();
        var headers = host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().ToList();
        Assume.That(headers, Has.Count.GreaterThanOrEqualTo(2), "precondition: two categories to compare");
        headers[1].IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        host.Window.UpdateLayout();

        IBrush? Drawn(FormPropertyCategoryHeader header) =>
            host.HeaderButton(header).GetVisualDescendants().OfType<ContentPresenter>().First().Background;

        Assert.Multiple(() =>
        {
            Assert.That(host.HeaderButton(headers[0]).IsChecked, Is.True, "precondition: the first is expanded");
            Assert.That(Drawn(headers[0]), Is.SameAs(IdeBrushes["IdeBg"]), "an expanded header draws IdeBg");
            Assert.That(Drawn(headers[1]), Is.SameAs(IdeBrushes["IdeBg"]), "a collapsed header draws IdeBg");
        });
    }

    /// <summary>Clicking a header collapses it in place; the header stays under the pointer.</summary>
    [AvaloniaTest]
    public void ClickingAHeader_CollapsesIt_AndTheHeaderStaysUnderThePointer()
    {
        using var host = Host();
        var header = host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().First();
        var button = host.HeaderButton(header);
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), host.Window)!.Value;
        var before = host.Grid.DisplayItems.Count;

        host.Window.MouseDown(at, MouseButton.Left);
        host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        host.Window.UpdateLayout();

        var under = host.Window.InputHitTest(at) as Visual;
        Assert.Multiple(() =>
        {
            Assert.That(header.IsExpanded, Is.False);
            Assert.That(host.Grid.DisplayItems, Has.Count.LessThan(before), "its rows left the list");
            Assert.That(under?.GetSelfAndVisualAncestors().OfType<ToggleButton>().FirstOrDefault()?.DataContext,
                Is.SameAs(header), "the same header is still under the pointer");
        });
    }

    /// <summary>
    /// A category collapses from the KEYBOARD, with the header selected in the list (VS: Left collapses,
    /// Right expands; Enter/Space toggle). The list's own selection is not changed by it.
    /// </summary>
    [AvaloniaTest]
    public void AHeaderSelectedInTheList_CollapsesAndExpandsFromTheKeyboard()
    {
        using var host = Host();
        var header = host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().First();
        host.Grid.SelectedItem = header;
        Dispatcher.UIThread.RunJobs();
        host.Container(header).Focus();
        Dispatcher.UIThread.RunJobs();

        Press(host.Window,Avalonia.Input.Key.Left);
        Assert.That(header.IsExpanded, Is.False, "Left collapses");
        Press(host.Window,Avalonia.Input.Key.Left);
        Assert.That(header.IsExpanded, Is.False, "Left on a collapsed header stays collapsed");
        Press(host.Window,Avalonia.Input.Key.Right);
        Assert.That(header.IsExpanded, Is.True, "Right expands");
        Press(host.Window,Avalonia.Input.Key.Enter);
        Assert.That(header.IsExpanded, Is.False, "Enter toggles");
        Press(host.Window,Avalonia.Input.Key.Space);
        Assert.That(header.IsExpanded, Is.True, "Space toggles");
        Assert.That(host.Grid.SelectedItem, Is.SameAs(header), "the keys never moved the selection");
    }

    /// <summary>Left/Right on a selected ROW are not header keys: nothing collapses.</summary>
    [AvaloniaTest]
    public void ARowSelectedInTheList_LeavesItsHeaderAloneOnLeft()
    {
        using var host = Host();
        var row = host.Row("Enabled");
        host.Grid.SelectedItem = row;
        Dispatcher.UIThread.RunJobs();
        host.Container(row).Focus();
        Dispatcher.UIThread.RunJobs();

        Press(host.Window,Avalonia.Input.Key.Left);

        Assert.That(host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().All(h => h.IsExpanded), Is.True);
    }

    [AvaloniaTest]
    public void TypingInTheSearchBox_FiltersTheList()
    {
        using var host = Host();
        var search = host.View.FindControl<TextBox>("SearchBox")!;
        search.Focus();
        Dispatcher.UIThread.RunJobs();

        host.Window.KeyTextInput("Enab");
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(host.Grid.SearchText, Is.EqualTo("Enab"));
            Assert.That(host.Grid.DisplayItems.OfType<FormPropertyRow>().Select(r => r.Name), Is.EqualTo(new[] { "Enabled" }));
            Assert.That(host.List.ItemCount, Is.EqualTo(host.Grid.DisplayItems.Count));
        });
    }

    /// <summary>A pick in the object selector REQUESTS the selection (the grid never selects).</summary>
    [AvaloniaTest]
    public void PickingInTheObjectSelector_RaisesSelectionRequested()
    {
        using var host = Host();
        var requested = new List<object?>();
        host.Grid.SelectionRequested += (_, control) => requested.Add(control);
        var selector = host.View.FindControl<ComboBox>("ObjectSelector")!;
        Assume.That(selector.SelectedItem, Is.SameAs(host.Grid.SelectedObject), "precondition: the selector shows the label");
        selector.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(host.Window,Avalonia.Input.Key.Up); // the label → the form, the entry above it

        Assert.That(requested, Is.EqualTo(new object?[] { null }), "one request, for the FORM (null)");
    }

    /// <summary>
    /// A short window virtualises the list: rows scroll out and their containers are recycled into other
    /// items. After scrolling to the end and back, every realised header's toggle still agrees with its
    /// header, and the selected row is still selected.
    /// </summary>
    [AvaloniaTest]
    public void AShortWindow_ScrolledToTheEndAndBack_KeepsHeadersAndTheSelection()
    {
        using var host = Host(height: 300);
        var headers = host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().ToList();
        // The FIRST collapsed: its header and the next (expanded) one then share the top of the list, so
        // both states come back through recycled containers.
        headers[0].IsExpanded = false;
        var selected = host.Grid.DisplayItems.OfType<FormPropertyRow>().First();
        host.Grid.SelectedItem = selected;
        Dispatcher.UIThread.RunJobs();

        // ⚠ Through the ScrollViewer, as the user scrolls — measured: ScrollIntoView(0) after scrolling to the
        // end stopped part-way (offset 236 of 418) with the first header still unrealised.
        var scroller = (ScrollViewer)host.List.Scroll!;
        scroller.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        host.Window.UpdateLayout();
        // ⚠ The FIRST HEADER, not the selected row: measured, Avalonia keeps the selected row's container
        // realised however far the list scrolls, while an unselected item's container is recycled.
        Assume.That(host.List.ContainerFromIndex(0), Is.Null, "precondition: the first header scrolled OUT");
        scroller.ScrollToHome();
        Dispatcher.UIThread.RunJobs();
        host.Window.UpdateLayout();

        var realised = host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>()
            .Select(h => (Header: h, Container: host.List.ContainerFromItem(h)))
            .Where(p => p.Container != null).ToList();
        TestContext.Out.WriteLine($"offset={host.List.Scroll?.Offset} realised headers={string.Join(",", realised.Select(p => p.Header.Name))}");
        Assert.Multiple(() =>
        {
            Assert.That(realised.Select(p => p.Header.IsExpanded).Distinct().Count(), Is.EqualTo(2),
                "a collapsed AND an expanded header are realised after the round trip");
            foreach (var (header, container) in realised)
            {
                Assert.That(container!.GetVisualDescendants().OfType<ToggleButton>().First().IsChecked,
                    Is.EqualTo(header.IsExpanded), header.Name);
            }

            Assert.That(host.Grid.SelectedItem, Is.SameAs(selected));
            Assert.That(host.List.SelectedItem, Is.SameAs(selected));
        });
    }

    /// <summary>
    /// Reset from the KEYBOARD: Shift+F10 (and the Menu key) on the focused row opens its context menu.
    /// The menu lives on the ListBoxItem container — a menu inside the row template never sees the
    /// request, which is raised on the FOCUSED element and bubbles up, not down. A header has none.
    /// </summary>
    [AvaloniaTest]
    public void TheFocusedRow_OpensItsResetMenu_OnShiftF10() =>
        OpensTheResetMenu(Avalonia.Input.Key.F10, RawInputModifiers.Shift);

    [AvaloniaTest]
    public void TheFocusedRow_OpensItsResetMenu_OnTheMenuKey() =>
        OpensTheResetMenu(Avalonia.Input.Key.Apps, RawInputModifiers.None);

    private static void OpensTheResetMenu(Key key, RawInputModifiers modifiers)
    {
        {
            using var host = Host();
            var row = host.Row("Text");
            host.Grid.SelectedItem = row;
            Dispatcher.UIThread.RunJobs();
            var container = host.Container(row);
            container.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(host.Window,key, modifiers);

            var menu = container.ContextMenu;
            Assert.That(menu, Is.Not.Null, "the row's container carries the menu");
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(menu!.IsOpen, Is.True, "the keyboard opened it");
                    var reset = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Reset");
                    Assert.That(reset.Command, Is.SameAs(row.ResetCommand));
                    Assert.That(host.Container(host.Grid.DisplayItems.OfType<FormPropertyCategoryHeader>().First()).ContextMenu,
                        Is.Null, "a header has nothing to reset");
                });
            }
            finally
            {
                menu?.Close();
            }
        }
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
