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
/// <param name="Category">"Common Controls" or "Containers", as VS groups them.</param>
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

        // Containers last, the way VS orders its Windows Forms tab. Ordered here rather than in the
        // catalog because the catalog's order is the order controls were SPECIFIED in, and changing
        // it to suit one panel would move every row of every other consumer.
        var ordered = FormControlCatalog.For(Target)
            .OrderBy(c => c.IsContainer ? 1 : 0)
            .ThenBy(c => c.Kind, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? previousCategory = null;
        foreach (var control in ordered)
        {
            // What this kind IS on the target the user is looking at — the WinForms type name or
            // the HTML tag. A toolbox that only says "TextBox" leaves them guessing which of the
            // three <input> kinds they are about to place.
            var description = Target == FormTarget.WinForms
                ? control.WinFormsType ?? control.Kind
                : control.HtmlInputType != null
                    ? $"<{control.HtmlTag} type=\"{control.HtmlInputType}\">"
                    : $"<{control.HtmlTag}>";

            var category = control.IsContainer ? "Containers" : "Common Controls";

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
    /// </summary>
    private static string GlyphFor(FormSchematic schematic) => schematic switch
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

        // ⛔ Reached only by a schematic added without a mark, which FormToolboxGlyphTests fails on.
        // Left as a visible "?" rather than something plausible precisely so it cannot pass for a
        // real icon if that test is ever removed.
        _ => "?"
    };
}
