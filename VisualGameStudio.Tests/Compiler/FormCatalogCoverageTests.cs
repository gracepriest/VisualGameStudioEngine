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
        "PictureBox",
        // Task 24, commit 24c — menus, toolbars and status bars (spec §4). RED until Task 12 Step 3
        // adds the seven rows.
        "MenuStrip", "ToolStrip", "StatusStrip",
        "ToolStripMenuItem", "ToolStripSeparator", "ToolStripButton", "ToolStripStatusLabel"
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

    // ==================================================================
    // Task 24, commit 24b — the catalog SHAPE (FormPlace, FormItemRule) lands with NO new row, so
    // every gate that will need to know the shape learns it before there is anything to go red.
    // These two tests currently describe a catalog with no Docked/Item rows at all: commit 24c adds
    // the seven strip/item rows and REWRITES both tests (see the per-test note).
    // ==================================================================

    /// <summary>
    /// Every row's derived <c>IsComponent</c> must agree with its <c>Place</c>. The four tray
    /// components (Timer, ToolTip, ErrorProvider, BackgroundWorker) are <c>Tray</c>; the seven
    /// strip/item rows (Task 24, commit 24c) are <c>Docked</c>/<c>Item</c>; every other row is
    /// <c>Positioned</c>.
    ///
    /// ⚠ <b>FLIPPED in Task 12 (commit 24c), per plan Step 1.</b> Until Task 12 Step 3 adds the
    /// seven rows, this is RED: no catalog row named below exists, so the "not tray, not strip"
    /// check has nothing to exclude and — once the rows exist — must find them <c>Docked</c>/<c>Item</c>,
    /// never <c>Positioned</c>.
    /// </summary>
    [Test]
    public void EveryRow_HasAPlace_AndIsComponentIsDerived()
    {
        var trayKinds = new[] { "Timer", "ToolTip", "ErrorProvider", "BackgroundWorker" };
        var stripKinds = new[]
        {
            "MenuStrip", "ToolStrip", "StatusStrip",
            "ToolStripMenuItem", "ToolStripSeparator", "ToolStripButton", "ToolStripStatusLabel"
        };

        foreach (var def in FormControlCatalog.All)
        {
            Assert.That(def.IsComponent, Is.EqualTo(def.Place == FormPlace.Tray),
                $"'{def.Kind}': IsComponent must be derived from Place (IsComponent == (Place == Tray))");
        }

        foreach (var kind in trayKinds)
        {
            var def = FormControlCatalog.Find(kind);
            Assert.That(def, Is.Not.Null, kind);
            Assert.That(def!.Place, Is.EqualTo(FormPlace.Tray), $"'{kind}' must be Tray");
        }

        // The seven strip/item rows must exist and must NOT be Positioned (Docked or Item).
        foreach (var kind in stripKinds)
        {
            var def = FormControlCatalog.Find(kind);
            Assert.That(def, Is.Not.Null, $"'{kind}' is not in the catalog at all");
            Assert.That(def!.Place, Is.Not.EqualTo(FormPlace.Positioned),
                $"'{kind}' must be Docked or Item, never Positioned");
            Assert.That(def.Place, Is.Not.EqualTo(FormPlace.Tray), $"'{kind}' must not be Tray");
        }

        // Every row whose kind is not one of the seven strip/item kinds is Positioned (the tray
        // four excepted above, and asserted Tray, not Positioned).
        var positioned = FormControlCatalog.All
            .Where(d => !trayKinds.Contains(d.Kind, StringComparer.OrdinalIgnoreCase) &&
                        !stripKinds.Contains(d.Kind, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Assert.That(positioned, Is.Not.Empty);

        foreach (var def in positioned)
        {
            Assert.That(def.Place, Is.EqualTo(FormPlace.Positioned),
                $"'{def.Kind}': every row but the four tray components and the seven strip/item " +
                "rows is Positioned");
        }
    }

    /// <summary>
    /// The seven strip/item <see cref="FormSchematic"/> values exist (commit 24b, spec Decision 12),
    /// and each is used by EXACTLY ONE row, which is one of the seven strip/item kinds.
    ///
    /// ⚠ <b>FLIPPED in Task 12 (commit 24c), per plan Step 1.</b> INVERTED, not merely relaxed, from
    /// 24b's "no row uses any of these seven yet" to "exactly the seven new rows use exactly these
    /// seven schematics, one each". RED until Task 12 Step 3 adds the rows.
    /// </summary>
    [Test]
    public void FormSchematic_HasTheSevenStripValues()
    {
        var stripValues = new[]
        {
            FormSchematic.MenuBar, FormSchematic.ToolBar, FormSchematic.StatusBar,
            FormSchematic.MenuItem, FormSchematic.Separator, FormSchematic.ToolButton,
            FormSchematic.StatusLabel
        };
        var stripKinds = new[]
        {
            "MenuStrip", "ToolStrip", "StatusStrip",
            "ToolStripMenuItem", "ToolStripSeparator", "ToolStripButton", "ToolStripStatusLabel"
        };

        Assert.That(Enum.GetValues<FormSchematic>(), Is.SupersetOf(stripValues));

        foreach (var schematic in stripValues)
        {
            var rows = FormControlCatalog.All.Where(d => d.Schematic == schematic).ToList();
            Assert.That(rows, Has.Count.EqualTo(1),
                $"'{schematic}' must be used by exactly one row, found {rows.Count}: " +
                string.Join(", ", rows.Select(r => r.Kind)));
            Assert.That(stripKinds, Does.Contain(rows[0].Kind),
                $"'{schematic}' must belong to one of the seven strip/item kinds, not '{rows[0].Kind}'");
        }
    }

    /// <summary>
    /// Task 24, commit 24c, Task 12 Step 1: the seven strip/item rows have the shape spec §4
    /// states, before any row exists. RED until Task 12 Step 3 adds them.
    /// </summary>
    [Test]
    public void TheStripRows_HaveTheShapeTheSpecStates()
    {
        FormControlDef Row(string kind)
        {
            var def = FormControlCatalog.Find(kind);
            Assert.That(def, Is.Not.Null, $"'{kind}' is not in the catalog at all");
            return def!;
        }

        var menuStrip = Row("MenuStrip");
        var toolStrip = Row("ToolStrip");
        var statusStrip = Row("StatusStrip");
        var menuItem = Row("ToolStripMenuItem");
        var separator = Row("ToolStripSeparator");
        var toolButton = Row("ToolStripButton");
        var statusLabel = Row("ToolStripStatusLabel");

        var strips = new[] { menuStrip, toolStrip, statusStrip };
        var items = new[] { menuItem, separator, toolButton, statusLabel };

        Assert.Multiple(() =>
        {
            // MenuStrip/ToolStrip/StatusStrip are Docked, not a container, and host their Items
            // rule, added through Items.Add.
            foreach (var strip in strips)
            {
                Assert.That(strip.Place, Is.EqualTo(FormPlace.Docked), $"'{strip.Kind}' must be Docked");
                Assert.That(strip.IsContainer, Is.False, $"'{strip.Kind}' must not be IsContainer");
                Assert.That(strip.Items, Is.Not.Null, $"'{strip.Kind}' must declare an Items rule");
                if (strip.Items != null)
                {
                    Assert.That(strip.Items.Add, Does.Contain("Items.Add"),
                        $"'{strip.Kind}' must add its items through Items.Add");
                }
            }

            if (menuStrip.Items != null) Assert.That(menuStrip.Items.Kinds[0], Is.EqualTo("ToolStripMenuItem"));
            if (toolStrip.Items != null) Assert.That(toolStrip.Items.Kinds[0], Is.EqualTo("ToolStripButton"));
            if (statusStrip.Items != null) Assert.That(statusStrip.Items.Kinds[0], Is.EqualTo("ToolStripStatusLabel"));

            // ToolStripMenuItem is itself a host — its dropdown — via DropDownItems.Add.
            Assert.That(menuItem.Place, Is.EqualTo(FormPlace.Item), "'ToolStripMenuItem' must be Item");
            Assert.That(menuItem.Items, Is.Not.Null, "'ToolStripMenuItem' must also host its own dropdown");
            if (menuItem.Items != null)
            {
                Assert.That(menuItem.Items.Add, Does.Contain("DropDownItems.Add"));
            }

            // The other three item rows are Item with no Items rule of their own.
            foreach (var item in new[] { separator, toolButton, statusLabel })
            {
                Assert.That(item.Place, Is.EqualTo(FormPlace.Item), $"'{item.Kind}' must be Item");
                Assert.That(item.Items, Is.Null, $"'{item.Kind}' must not be a host");
            }

            // Every strip and item row exists on both targets.
            foreach (var def in strips.Concat(items))
            {
                Assert.That(def.SupportsTarget(FormTarget.WinForms), Is.True, $"'{def.Kind}' must support WinForms");
                Assert.That(def.SupportsTarget(FormTarget.Web), Is.True, $"'{def.Kind}' must support Web");
            }

            // MenuStrip assigns itself to the form's MainMenuStrip.
            Assert.That(menuStrip.FormProperty, Is.EqualTo("MainMenuStrip"));

            // Children wrap in <ul> on MenuStrip and ToolStripMenuItem.
            Assert.That(menuStrip.HtmlChildrenWrapper, Is.EqualTo("ul"));
            Assert.That(menuItem.HtmlChildrenWrapper, Is.EqualTo("ul"));

            // ToolStripButton is an <input type="button">.
            Assert.That(toolButton.HtmlTag, Is.EqualTo("input"));
            Assert.That(toolButton.HtmlInputType, Is.EqualTo("button"));

            // ToolTipText is the one property with an honest attribute form, wherever it appears.
            foreach (var def in new[] { menuItem, toolButton, statusLabel })
            {
                var toolTipText = def.Property("ToolTipText");
                Assert.That(toolTipText, Is.Not.Null, $"'{def.Kind}' must declare ToolTipText");
                if (toolTipText != null)
                {
                    Assert.That(toolTipText.HtmlAttributeName, Is.EqualTo("title"));
                }
            }

            // Enabled is WinForms-only on every strip/item row that declares it, EXCEPT
            // ToolStripButton, whose <input> honours the emitter's ` disabled`.
            // ToolStripSeparator declares no Enabled at all — spec §4 gives it only Visible — and
            // that absence is now asserted explicitly rather than silently skipped, so a future row
            // that grows an Enabled property is pinned in EITHER direction: never asserted for
            // ToolStripSeparator, always asserted for everything else.
            foreach (var def in strips.Concat(items))
            {
                var enabled = def.Property("Enabled");

                if (def.Kind == "ToolStripSeparator")
                {
                    Assert.That(enabled, Is.Null,
                        "'ToolStripSeparator' must declare no Enabled — spec §4 gives it only Visible");
                    continue;
                }

                Assert.That(enabled, Is.Not.Null, $"'{def.Kind}' must declare Enabled");
                if (enabled == null)
                {
                    continue;
                }

                if (def.Kind == "ToolStripButton")
                {
                    Assert.That(enabled.AppliesTo(FormTarget.Web), Is.True,
                        "ToolStripButton's <input> honours 'disabled' — Enabled must be shared");
                }
                else
                {
                    Assert.That(enabled.AppliesTo(FormTarget.WinForms), Is.True, $"'{def.Kind}'.Enabled must apply to WinForms");
                    Assert.That(enabled.AppliesTo(FormTarget.Web), Is.False, $"'{def.Kind}'.Enabled must be WinForms-only");
                }
            }

            // Checked/CheckOnClick/DisplayStyle/Spring/GripStyle/SizingGrip are WinForms-only,
            // wherever spec §4 places them.
            void AssertWinFormsOnly(FormControlDef def, string propertyName)
            {
                var prop = def.Property(propertyName);
                Assert.That(prop, Is.Not.Null, $"'{def.Kind}' must declare {propertyName}");
                if (prop != null)
                {
                    Assert.That(prop.AppliesTo(FormTarget.WinForms), Is.True, $"'{def.Kind}'.{propertyName} must apply to WinForms");
                    Assert.That(prop.AppliesTo(FormTarget.Web), Is.False, $"'{def.Kind}'.{propertyName} must be WinForms-only");
                }
            }

            AssertWinFormsOnly(menuItem, "Checked");
            AssertWinFormsOnly(menuItem, "CheckOnClick");
            AssertWinFormsOnly(toolButton, "Checked");
            AssertWinFormsOnly(toolButton, "CheckOnClick");
            AssertWinFormsOnly(toolButton, "DisplayStyle");
            AssertWinFormsOnly(statusLabel, "Spring");
            AssertWinFormsOnly(toolStrip, "GripStyle");
            AssertWinFormsOnly(statusStrip, "SizingGrip");

            // Every item kind's canonical host — the first Docked row whose Items rule accepts it,
            // exactly FormCatalogShapes.Canonical's own selection — is Docked.
            foreach (var item in items)
            {
                var host = FormControlCatalog.All.FirstOrDefault(
                    d => d.Place == FormPlace.Docked && d.Items?.Accepts(item.Kind) == true);
                Assert.That(host, Is.Not.Null, $"'{item.Kind}' must have a Docked canonical host that accepts it");
            }
        });

        // The nesting invariant's refusal code (spec §3), added by Task 12 Step 3 alongside the rows.
        Assert.That(DesignCodes.StripMisplaced, Is.EqualTo("BL8030"));
    }
}
