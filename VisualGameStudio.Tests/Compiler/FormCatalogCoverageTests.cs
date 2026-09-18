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
