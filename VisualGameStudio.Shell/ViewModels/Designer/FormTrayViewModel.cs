using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>One item in the component tray: a component, and how the strip shows it.</summary>
public sealed partial class FormTrayItem : ObservableObject
{
    public FormTrayItem(FormControl control, string glyph)
    {
        Control = control;
        Glyph = glyph;
    }

    /// <summary>The component itself — the same object the document, the grid and the commands hold.</summary>
    public FormControl Control { get; }

    public string Id => Control.Id;

    public string Kind => Control.Kind;

    /// <summary>The toolbox's own mark for this kind — one table, keyed on the catalog's schematic.</summary>
    public string Glyph { get; }

    /// <summary>Follows the designer's <see cref="FormSelection"/>, never the click that set it.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The component tray (Task 25) — VS's strip under the design surface for the things that are part
/// of the form but have no position on it.
///
/// <para>⛔ Rebuilt from the DOCUMENT, never from the last drop: the document is the truth and the
/// tray is a view of <see cref="FormDocument.Components"/>, so an undo — which rewinds the text and
/// re-parses — brings the tray back into line the same way it brings the canvas back. The host
/// rebuilds on every <c>DesignModelRevision</c> bump and on every panel sync.</para>
///
/// <para>⚠ Selection goes through the ONE store the canvas and every command read,
/// <see cref="FormSelection"/>. The host view model's own <c>Changed</c> subscription pushes the
/// primary into the property grid (the canvas's TwoWay binding agrees with it); this class marks
/// its items from the same event, so a highlight here and a grid over there can never disagree
/// about what is selected. ⛔ A drop must select through the same store — one that wrote the grid
/// alone left the two disagreeing after the first tray click, and Delete removed the wrong one.</para>
/// </summary>
public sealed class FormTrayViewModel : ObservableObject
{
    private readonly FormSelection _selection;

    public FormTrayViewModel(FormSelection selection)
    {
        _selection = selection;
        _selection.Changed += (_, _) => Mark();
    }

    public ObservableCollection<FormTrayItem> Items { get; } = new();

    /// <summary>An empty strip under every form would be noise; the tray appears with its first component.</summary>
    public bool IsVisible => Items.Count > 0;

    /// <summary>Re-reads the document's components. Cheap, and correct by construction.</summary>
    public void Rebuild(FormDocument? document)
    {
        Items.Clear();

        if (document != null)
        {
            foreach (var component in document.Components)
            {
                var schematic = component.Definition?.Schematic ?? FormSchematic.Input;
                Items.Add(new FormTrayItem(component, FormToolboxViewModel.GlyphFor(schematic)));
            }
        }

        Mark();
        OnPropertyChanged(nameof(IsVisible));
    }

    /// <summary>A click on an item: select it, through the shared selection.</summary>
    public void Select(FormTrayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _selection.Set(item.Control);
    }

    private void Mark()
    {
        foreach (var item in Items)
        {
            item.IsSelected = _selection.Contains(item.Control);
        }
    }
}
