using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>One toolbox entry: a control kind the current document can hold.</summary>
/// <param name="Kind">The catalog kind, which is also the document element name.</param>
/// <param name="Description">What it maps to on this target — the thing a user actually wonders.</param>
/// <param name="Glyph">
/// A two-or-three character stand-in for VS's control icons. Derived from the catalog's
/// <see cref="FormSchematic"/> rather than from the kind's name, so it is distinct for every row
/// the same way the canvas drawing is — first letters collide (Label/ListBox, Panel/PictureBox,
/// CheckBox/ComboBox) and would put the same mark beside different controls.
/// </param>
/// <param name="Category">
/// "Common Controls", "Containers", "Menus &amp; Toolbars" or "Components", as VS groups them — in
/// that order, which <c>Rebuild</c>'s sort and this string have to agree on.
/// </param>
/// <param name="StartsCategory">
/// True on the FIRST row of each category, which is how the single flat list draws group headers.
/// ⛔ One ListBox, deliberately: the toolbox drag is wired to <c>ToolboxList</c> by name, and
/// splitting the list per group would give each group its own unwired ListBox.
/// </param>
public sealed record FormToolboxItem(
    string Kind, string Description, string Glyph, string Category, bool StartsCategory);

/// <summary>
/// The designer's toolbox — the control kinds available for the open document's target.
///
/// <para>⛔ Driven from <see cref="FormControlCatalog.For"/>, never a hand-written list. A hand
/// list is how a catalog row ships with no way to place it, and how a kind that does not exist on
/// the target gets offered anyway: <c>SupportsTarget</c> is the only thing that knows a control is
/// web-only or WinForms-only, and it is checked HERE rather than after the user has dragged one
/// onto the canvas.</para>
/// </summary>
public partial class FormToolboxViewModel : ObservableObject
{
    [ObservableProperty]
    private FormTarget _target = FormTarget.Web;

    public ObservableCollection<FormToolboxItem> Items { get; } = new();

    public FormToolboxViewModel() => Rebuild();

    partial void OnTargetChanged(FormTarget value) => Rebuild();

    private void Rebuild()
    {
        Items.Clear();

        // Containers after the common controls and components last, the way VS orders its Windows
        // Forms tab. Ordered here rather than in the catalog because the catalog's order is the
        // order controls were SPECIFIED in, and changing it to suit one panel would move every row
        // of every other consumer.
        // ⛔ An ITEM kind is never a toolbox row (spec Decision 7, and VS does not list one either):
        // a menu item is created from the "Type Here" slot on the strip that will hold it, never
        // dragged onto the canvas — it has no place of its own to be dropped at. Filtered HERE, in
        // commit 24b, so the rule is in force before the first item row exists: today this excludes
        // nothing, and the two membership pins say the same thing from the other side.
        // ⚠ Task 24 (commit 24c) adds the fourth rank: a DOCKED strip is neither a common control nor
        // a container, and VS gives it its own tab. Ranked on Place rather than on the kind's name,
        // so a strip row added later lands in the group without a second list to update.
        var ordered = FormControlCatalog.For(Target)
            .Where(c => c.Place != FormPlace.Item)
            .OrderBy(c => c.Place switch
            {
                FormPlace.Tray => 3,
                FormPlace.Docked => 2,
                _ when c.IsContainer => 1,
                _ => 0
            })
            .ThenBy(c => c.Kind, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? previousCategory = null;
        foreach (var control in ordered)
        {
            // What this kind IS on the target the user is looking at — the WinForms type name or
            // the HTML tag. A toolbox that only says "TextBox" leaves them guessing which of the
            // three <input> kinds they are about to place. A script-backed component is not an
            // element and must not read as one; a WinForms component shows its qualified type,
            // which is exactly what it is.
            var description = Target == FormTarget.WinForms
                ? control.WinFormsType ?? control.Kind
                : control.WebScript != null
                    ? "script"
                    : control.HtmlInputType != null
                        ? $"<{control.HtmlTag} type=\"{control.HtmlInputType}\">"
                        : $"<{control.HtmlTag}>";

            // ⚠ The SAME rank the ordering above applies, said in words. Two spellings of one grouping
            // is how a category header ends up drawn twice: StartsCategory fires on every change of
            // this string, so a category that disagrees with the sort interleaves.
            var category = control.IsComponent ? "Components"
                : control.Place == FormPlace.Docked ? "Menus & Toolbars"
                : control.IsContainer ? "Containers"
                : "Common Controls";

            Items.Add(new FormToolboxItem(
                control.Kind,
                description,
                GlyphFor(control.Schematic),
                category,
                StartsCategory: category != previousCategory));

            previousCategory = category;
        }
    }

    /// <summary>
    /// A stand-in for VS's icons, keyed on the same catalog field the canvas draws from — so the
    /// mark beside a row and the shape it produces cannot drift apart.
    ///
    /// <para>⛔ PUBLIC, not internal: <c>FormSchematicPinTests</c> drives this per ENUM VALUE, and
    /// the Shell grants no <c>InternalsVisibleTo</c> to the test project by convention (see
    /// <c>CodeEditorDocumentView.axaml.cs:770</c> — public seams, never internal + IVT). The existing
    /// glyph gates iterate toolbox ROWS and read <see cref="FormToolboxItem.Glyph"/>, so they would
    /// never see the missing arm of a schematic no row uses — which is every item schematic.</para>
    /// </summary>
    public static string GlyphFor(FormSchematic schematic) => schematic switch
    {
        FormSchematic.Text => "A",
        FormSchematic.Input => "ab",
        FormSchematic.Button => "OK",
        FormSchematic.Check => "[x]",
        FormSchematic.Radio => "(o)",
        FormSchematic.Dropdown => "v",
        FormSchematic.List => "=",
        FormSchematic.Container => "[ ]",
        FormSchematic.Group => "{ }",
        FormSchematic.Image => "/\\",

        // Task 23's widening. ⚠ Each must be VISUALLY distinct from every other mark, not merely
        // different: FormToolboxGlyphTests fails on a repeat, because two controls wearing the same
        // mark are as unhelpful as thirteen wearing "?".
        FormSchematic.Link => "_A_",
        FormSchematic.CheckList => "[=]",
        FormSchematic.Spinner => "1^",
        FormSchematic.DatePicker => "31",
        FormSchematic.Slider => "-O-",
        FormSchematic.Progress => "##",
        FormSchematic.ListDetail => "|=|",
        FormSchematic.Tree => "+-",
        FormSchematic.DataGrid => "###",
        FormSchematic.Tabs => "|_|",
        FormSchematic.Split => "|:|",
        FormSchematic.FlowContainer => ">>",
        FormSchematic.TableContainer => "#|#",

        // Task 25: the tray. One mark per component kind, as VS gives each its own icon — a tray
        // with a Timer and a ToolTip in it must not show two of the same thing.
        FormSchematic.Clock => "(t)",
        FormSchematic.Hint => "(?)",
        FormSchematic.Alert => "(!)",
        FormSchematic.Worker => "(w)",

        // Task 24: menus, toolbars and status bars. The three strips are toolbox rows; the four item
        // kinds are not (they are created from Type Here), but they still need a mark — the tray and
        // the property grid name a control by its schematic, and a missing arm here is a "?" beside
        // a real control. ⚠ Each bar reads as its own band: the rule is under the menu bar, over the
        // status bar, and the tool bar wears its grip.
        FormSchematic.MenuBar => "≡_",
        FormSchematic.ToolBar => "[▸]",
        FormSchematic.StatusBar => "_≡",
        FormSchematic.MenuItem => "≡",
        FormSchematic.Separator => "—",
        FormSchematic.ToolButton => "[▸",
        FormSchematic.StatusLabel => "_A",

        // ⛔ Reached only by a schematic added without a mark, which FormToolboxGlyphTests fails on.
        // Left as a visible "?" rather than something plausible precisely so it cannot pass for a
        // real icon if that test is ever removed.
        _ => "?"
    };
}
