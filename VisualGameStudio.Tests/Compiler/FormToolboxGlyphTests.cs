using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 23: every control in the toolbox carries its own mark.
///
/// <para>⛔⛔ <c>GlyphFor</c> is a <c>switch</c> with a <c>_ =&gt; "?"</c> arm, which is the failure
/// shape this repo keeps finding: it compiles, it never throws, and the only symptom is a toolbox
/// where thirteen controls are all labelled "?" — indistinguishable from each other and from a
/// feature that was never finished. Widening the catalog without this guard would have produced
/// exactly that, and nothing else in the suite looks at the toolbox's marks.</para>
///
/// <para>⚠ Driven from the ENUM, not from a list here, so a schematic added tomorrow fails this
/// without anyone remembering to come back.</para>
/// </summary>
[TestFixture]
public class FormToolboxGlyphTests
{
    /// <summary>
    /// ⚠ Per TARGET. The toolbox shows what the target supports, so a WinForms-only control is
    /// correctly absent from the web toolbox — checking one target would miss half the catalog.
    /// </summary>
    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void EverySchematicHasItsOwnGlyph(FormTarget target)
    {
        var toolbox = new FormToolboxViewModel { Target = target };

        var unmarked = toolbox.Items
            .Where(i => i.Glyph == "?" || string.IsNullOrWhiteSpace(i.Glyph))
            .Select(i => i.Kind)
            .ToList();

        Assert.That(unmarked, Is.Empty,
            "these toolbox rows fell through to the fallback mark: " + string.Join(", ", unmarked));
    }

    /// <summary>
    /// ⚠ Two kinds may legitimately share a glyph only if they share a SCHEMATIC — the mark is keyed
    /// on the shape, so that is the same statement twice rather than a collision. Different shapes
    /// wearing the same mark is a real one.
    /// </summary>
    [Test]
    public void TwoDifferentShapesNeverWearTheSameMark()
    {
        var byGlyph = new Dictionary<string, HashSet<FormSchematic>>(StringComparer.Ordinal);

        foreach (var item in new FormToolboxViewModel().Items)
        {
            var schematic = FormControlCatalog.Find(item.Kind)!.Schematic;
            if (!byGlyph.TryGetValue(item.Glyph, out var shapes))
            {
                byGlyph[item.Glyph] = shapes = new HashSet<FormSchematic>();
            }

            shapes.Add(schematic);
        }

        var collisions = byGlyph.Where(p => p.Value.Count > 1).ToList();

        Assert.That(collisions, Is.Empty,
            "these marks are worn by more than one shape: " +
            string.Join(" | ", collisions.Select(c => $"'{c.Key}' = {string.Join(", ", c.Value)}")));
    }

    /// <summary>
    /// The toolbox offers everything the catalog has FOR THAT TARGET — a row nobody can place is
    /// catalog data with no way in.
    ///
    /// <para>⚠ Task 24, commit 24b: EXCEPT an ITEM kind (none exist yet; commit 24c adds the first),
    /// which has no place of its own to be dropped at — it is created from the "Type Here" slot on
    /// its host (spec Decision 7). The production filter already drops it from
    /// <see cref="FormToolboxViewModel.Rebuild"/>; this pin says so from the catalog's side too.</para>
    /// </summary>
    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void TheToolboxOffersEveryCatalogKindForItsTarget(FormTarget target)
    {
        var offered = new FormToolboxViewModel { Target = target }.Items
            .Select(i => i.Kind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = FormControlCatalog.All
            .Where(d => d.SupportsTarget(target) && d.Place != FormPlace.Item)
            .Select(d => d.Kind)
            .Where(k => !offered.Contains(k))
            .ToList();

        Assert.That(missing, Is.Empty,
            $"the catalog has {target} controls the toolbox does not offer: " + string.Join(", ", missing));
    }

    /// <summary>Task 25: components are their own toolbox category, last, as VS's tab orders them.</summary>
    [Test]
    public void ComponentsSitInTheirOwnCategory_LastLikeVs()
    {
        var items = new FormToolboxViewModel { Target = FormTarget.WinForms }.Items;

        Assert.Multiple(() =>
        {
            Assert.That(items.Where(i => i.Category == "Components").Select(i => i.Kind),
                Is.EquivalentTo(new[] { "Timer", "ToolTip", "ErrorProvider", "BackgroundWorker" }));
            Assert.That(items.Last().Category, Is.EqualTo("Components"));
            Assert.That(items.First(i => i.Kind == "Timer").Description, Is.EqualTo("System.Windows.Forms.Timer"));
        });

        var web = new FormToolboxViewModel { Target = FormTarget.Web }.Items;
        Assert.That(web.Single(i => i.Kind == "Timer").Description, Is.EqualTo("script"),
            "a script-backed component is not an element and must not read as one");
    }

    /// <summary>
    /// ⛔ The mirror of the above, and the one that matters more: a WinForms-only control must NOT
    /// appear in the web toolbox. Offering it would let a user place a DataGridView on a page that
    /// cannot emit one, and the refusal would arrive after the drop rather than before it.
    /// </summary>
    [Test]
    public void TheWebToolboxOffersNothingTheWebCannotEmit()
    {
        var offered = new FormToolboxViewModel { Target = FormTarget.Web }.Items;

        var impossible = offered
            .Where(i => !FormControlCatalog.Find(i.Kind)!.SupportsTarget(FormTarget.Web))
            .Select(i => i.Kind)
            .ToList();

        Assert.That(impossible, Is.Empty,
            "the web toolbox offers controls the web cannot emit: " + string.Join(", ", impossible));
    }

    /// <summary>
    /// Task 24, commit 24c: the three strips get their own toolbox category, "Menus &amp; Toolbars"
    /// (spec §6), the way Task 25 gave components theirs. An Item kind (a menu item, a separator, a
    /// toolbar button, a status label) still has no toolbox row of its own — it is created from its
    /// host's "Type Here" slot, never dragged onto the canvas.
    ///
    /// <para>⚠ TAUTOLOGY CHECK: the "no Item kind is offered" half of this pin is already true before
    /// this commit — <c>FormToolboxViewModel.Rebuild</c> has filtered <c>Place != FormPlace.Item</c>
    /// since commit 24b, before any Item row existed to filter. It is asserted here anyway, alongside
    /// the category, because it is the fact the category split must not disturb; it is NOT itself red
    /// against today's code and proves nothing about the category logic on its own.</para>
    /// </summary>
    [Test]
    public void StripsSitInTheirOwnCategory_MenusAndToolbars()
    {
        var items = new FormToolboxViewModel { Target = FormTarget.WinForms }.Items;

        Assert.Multiple(() =>
        {
            Assert.That(items.Where(i => i.Category == "Menus & Toolbars").Select(i => i.Kind),
                Is.EquivalentTo(new[] { "MenuStrip", "ToolStrip", "StatusStrip" }));
            Assert.That(
                items.Select(i => i.Kind).Intersect(new[]
                {
                    "ToolStripMenuItem", "ToolStripSeparator", "ToolStripButton", "ToolStripStatusLabel"
                }),
                Is.Empty,
                "an Item kind has no place of its own — it is never a toolbox row");
        });
    }
}
