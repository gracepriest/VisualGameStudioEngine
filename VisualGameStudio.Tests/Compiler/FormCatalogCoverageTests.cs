using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 23: the catalog covers the controls a user expects to find in a toolbox.
///
/// <para>⛔ <b>The expected list here is a REQUIREMENT, not a duplicate of the catalog.</b> Every
/// other list beside <see cref="FormControlCatalog"/> in this repo is forbidden because it is a
/// second copy of the same facts that silently goes stale. This one is the opposite: it states what
/// the toolbox must OFFER, which the catalog cannot state about itself. A catalog that quietly drops
/// a control would otherwise be indistinguishable from one that never had it.</para>
///
/// <para>⚠ Ten kinds was the plan's deliberate floor — the smallest set covering a login form, a
/// settings pane and a list-detail pane. It is not a ceiling, and a designer that stops there is a
/// proof of concept rather than something anyone would build a program with.</para>
/// </summary>
[TestFixture]
public class FormCatalogCoverageTests
{
    /// <summary>
    /// The common-controls tier Visual Studio puts in its own toolbox. ⚠ Deliberately NOT every
    /// WinForms control — this is the set a user reaches for without thinking.
    /// </summary>
    private static readonly string[] ExpectedWinFormsKinds =
    {
        // Text and commands
        "Label", "TextBox", "Button", "LinkLabel",
        // Choices
        "CheckBox", "RadioButton", "ComboBox", "ListBox", "CheckedListBox",
        // Values
        "NumericUpDown", "DateTimePicker", "TrackBar", "ProgressBar",
        // Data
        "ListView", "TreeView", "DataGridView",
        // Containers and layout
        "Panel", "GroupBox", "TabControl", "SplitContainer",
        "FlowLayoutPanel", "TableLayoutPanel",
        // Media
        "PictureBox"
    };

    [Test]
    public void TheCatalogCoversTheCommonControlsTier()
    {
        var have = FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.WinForms))
            .Select(d => d.Kind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ExpectedWinFormsKinds.Where(k => !have.Contains(k)).ToList();

        Assert.That(missing, Is.Empty,
            "the toolbox is missing controls a user expects to find: " + string.Join(", ", missing));
    }

    // ==================================================================
    // Task 25 — the component tray
    // ==================================================================

    /// <summary>The four components the brief names, as COMPONENT rows: no place on the canvas.</summary>
    [Test]
    public void TheTrayComponentsAreCatalogRows_MarkedNonVisual()
    {
        var kinds = new[] { "Timer", "ToolTip", "ErrorProvider", "BackgroundWorker" };

        foreach (var kind in kinds)
        {
            var def = FormControlCatalog.Find(kind);
            Assert.That(def, Is.Not.Null, kind);
            Assert.That(def!.IsComponent, Is.True, $"'{kind}' must be a component, or the canvas would try to place it");
            Assert.That(def.SupportsTarget(FormTarget.WinForms), Is.True, kind);
            Assert.That(def.IsContainer, Is.False, kind);
        }

        // Each has its own schematic, like every control kind: the tray shows a mark per kind, and
        // FormCanvasRenderTests never draws one, so the schematic is a glyph key and nothing else.
        var schematics = kinds.Select(k => FormControlCatalog.Find(k)!.Schematic).ToList();
        Assert.That(schematics.Distinct().Count(), Is.EqualTo(kinds.Length), "components must not share a mark");
        Assert.That(FormControlCatalog.All.Where(d => !d.IsComponent).Select(d => d.Schematic).Intersect(schematics),
            Is.Empty, "a component's schematic is not a control's");
    }

    /// <summary>
    /// ⛔ <c>Common(...)</c> bakes Visible/ForeColor/BackColor into a row. A component has none of
    /// them; the row would compile green through BasicLang and fail only at csc.
    /// </summary>
    [Test]
    public void AComponentRow_CarriesNoControlOnlyProperties()
    {
        var offenders = FormControlCatalog.All
            .Where(d => d.IsComponent)
            .SelectMany(d => d.Properties.Select(p => $"{d.Kind}.{p.Name}"))
            .Where(name => name.EndsWith(".Visible") || name.EndsWith(".ForeColor") || name.EndsWith(".BackColor"))
            .ToList();

        Assert.That(offenders, Is.Empty, string.Join(", ", offenders));
    }

    /// <summary>
    /// ⛔ The C# backend imports <c>System.Threading</c> whenever the generated body contains the
    /// substring "Thread", and the scaffold never imports <c>System.ComponentModel</c> — a bare
    /// <c>Timer</c> is then CS0104 and a bare <c>BackgroundWorker</c> CS0246, with BasicLang silent
    /// on both (spec M10, M11). Every component type is qualified.
    /// </summary>
    [Test]
    public void EveryComponentRow_QualifiesItsWinFormsType()
    {
        var bare = FormControlCatalog.All
            .Where(d => d.IsComponent && d.WinFormsType != null && !d.WinFormsType.Contains('.'))
            .Select(d => d.Kind)
            .ToList();

        Assert.That(bare, Is.Empty, "unqualified component types: " + string.Join(", ", bare));
    }

    /// <summary>
    /// The honest web equivalents: a Timer IS <c>setInterval</c>; a ToolTip's web form lives on
    /// OTHER controls (the <c>title</c> attribute); an ErrorProvider and a BackgroundWorker have
    /// none. The catalog says so rather than faking an element.
    /// </summary>
    [Test]
    public void OnlyTheTimer_HasAWebEquivalent_AndItIsScript_NotAnElement()
    {
        var web = FormControlCatalog.All.Where(d => d.IsComponent && d.SupportsTarget(FormTarget.Web)).ToList();

        Assert.That(web.Select(d => d.Kind), Is.EqualTo(new[] { "Timer" }));
        Assert.Multiple(() =>
        {
            Assert.That(web[0].HtmlTag, Is.Null, "a Timer is not an element");
            Assert.That(web[0].WebScript, Is.Not.Null);
            Assert.That(web[0].WebScript!.Construct, Does.Contain("{handler}").And.Contain("{Interval}"));
            Assert.That(web[0].WebHandlerTakesEvent, Is.False, "Window.setInterval takes an Action, not an Action(Of DomEvent)");
        });
    }

    /// <summary>
    /// ⛔ Every row must declare its own default event or the double-click gesture refuses by name.
    /// Driven from the catalog, so a row added tomorrow is covered without touching this file.
    /// </summary>
    [Test]
    public void EveryKindDeclaresADefaultEventForEveryTargetItSupports()
    {
        var gaps = new List<string>();

        foreach (var def in FormControlCatalog.All)
        {
            foreach (var target in new[] { FormTarget.WinForms, FormTarget.Web })
            {
                if (def.SupportsTarget(target) && string.IsNullOrEmpty(def.DefaultEvent(target)))
                {
                    gaps.Add($"{def.Kind}/{target}");
                }
            }
        }

        Assert.That(gaps, Is.Empty, "no default event declared for: " + string.Join(", ", gaps));
    }

    /// <summary>
    /// ⚠ A control that reaches NEITHER target is unreachable data — it cannot be placed, emitted or
    /// drawn, and the only symptom is a toolbox entry that does nothing.
    /// </summary>
    [Test]
    public void EveryKindSupportsAtLeastOneTarget()
    {
        var orphans = FormControlCatalog.All
            .Where(d => !d.SupportsTarget(FormTarget.WinForms) && !d.SupportsTarget(FormTarget.Web))
            .Select(d => d.Kind)
            .ToList();

        Assert.That(orphans, Is.Empty, "these kinds reach no target at all: " + string.Join(", ", orphans));
    }

    /// <summary>
    /// ⛔⛔ <b>Where the web has no honest equivalent, the catalog must SAY SO rather than fake one.</b>
    /// A <c>DataGridView</c> has no single HTML tag; emitting it as a <c>&lt;div&gt;</c> would produce
    /// a page that silently is not the control the designer drew. These are WinForms-only by
    /// decision, and Task 21's retarget reports them as explicit loss.
    /// </summary>
    [Test]
    public void TheControlsWithNoHonestHtmlEquivalentAreWinFormsOnly()
    {
        foreach (var kind in new[]
                 {
                     "DataGridView", "TreeView", "ListView", "TabControl",
                     "SplitContainer", "FlowLayoutPanel", "TableLayoutPanel", "CheckedListBox"
                 })
        {
            var def = FormControlCatalog.Find(kind);
            Assert.That(def, Is.Not.Null, $"'{kind}' is not in the catalog at all");
            Assert.That(def!.SupportsTarget(FormTarget.Web), Is.False,
                $"'{kind}' claims a web equivalent. It has no single honest HTML tag — say so in " +
                "the catalog rather than emitting markup that is not the control the user drew.");
        }
    }

    /// <summary>The ones that DO have an honest equivalent must actually carry it.</summary>
    [Test]
    public void TheControlsWithAnHonestHtmlEquivalentDeclareIt()
    {
        foreach (var (kind, tag) in new[]
                 {
                     ("NumericUpDown", "input"),
                     ("DateTimePicker", "input"),
                     ("TrackBar", "input"),
                     ("ProgressBar", "progress"),
                     ("LinkLabel", "a")
                 })
        {
            var def = FormControlCatalog.Find(kind);
            Assert.That(def, Is.Not.Null, $"'{kind}' is not in the catalog at all");
            Assert.That(def!.HtmlTag, Is.EqualTo(tag), $"'{kind}' should emit as <{tag}>");
        }
    }
}
