using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// The designer's property grid: the catalog properties of the selected control, in catalog order.
///
/// <para>⛔ Rows come from <see cref="FormControlCatalog"/>, never from whatever the document
/// happens to carry. The catalog is the single source of truth for "what can be set on this
/// control" — the same table the markup emitter, the region writer and the CI gate read. Building
/// rows from the document's own attributes instead would offer exactly the properties already set
/// and no way to add a new one.</para>
///
/// <para>⚠ Identifiers here are <c>Form*</c>, not <c>WebView*</c> — that prefix is taken by the
/// extension host's HTML source document type across Core, Shell, DockFactory and ViewLocator.</para>
/// </summary>
public partial class FormPropertyGridViewModel : ObservableObject
{
    private FormFile? _file;

    /// <summary>Raised when a row edited the model, so the host can write the document.</summary>
    public event EventHandler? Edited;

    [ObservableProperty]
    private FormControl? _selectedControl;

    /// <summary>The rows for the current selection. Empty when nothing is selected.</summary>
    public ObservableCollection<FormPropertyRow> Rows { get; } = new();

    /// <summary>The selected control's id, shown as the grid's header.</summary>
    public string Header => SelectedControl?.Id ?? "No selection";

    /// <summary>
    /// The control's KIND beside its id, the way VS's property window shows "button1  Button".
    /// Empty with no selection, so the header does not read "No selection No selection".
    /// </summary>
    public string HeaderKind => SelectedControl?.Kind ?? string.Empty;

    /// <summary>True when there is nothing to show, so the view can say so rather than look broken.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// The row the description pane is describing.
    ///
    /// <para>⚠ Selection here is a VIEW concern only — it changes nothing in the document. It exists
    /// because VS's property window devotes its bottom third to explaining the highlighted property,
    /// and that pane is the single most useful thing the window does for someone who does not
    /// already know the control's API.</para>
    /// </summary>
    [ObservableProperty]
    private FormPropertyRow? _selectedRow;

    /// <summary>The description pane's title: the property's name, or a prompt when nothing is picked.</summary>
    public string DescriptionTitle => SelectedRow?.Name ?? (IsEmpty ? string.Empty : "Properties");

    /// <summary>
    /// The description pane's body — the property's type, and WHY it is read-only when it is.
    ///
    /// <para>⛔ A frozen row's reason belongs here rather than only under the value. D9 freezes a
    /// property when its value did not parse, and "why can I not edit this" is exactly the question
    /// this pane is for.</para>
    /// </summary>
    public string DescriptionBody
    {
        get
        {
            if (SelectedRow is not { } row)
            {
                return IsEmpty
                    ? "Select a control on the canvas to see its properties."
                    : "Select a property to see what it does.";
            }

            return row.IsFrozen && !string.IsNullOrEmpty(row.FrozenReason)
                ? $"{row.TypeName} — read-only. {row.FrozenReason}"
                : row.TypeName;
        }
    }

    partial void OnSelectedRowChanged(FormPropertyRow? value)
    {
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionBody));
    }

    /// <summary>
    /// Points the grid at a loaded document. Passing the <see cref="FormFile"/> rather than the
    /// model is what carries D9's <b>Degraded</b> reasons — the model alone cannot say which values
    /// failed to parse.
    /// </summary>
    public void Load(FormFile? file)
    {
        _file = file;
        SelectedControl = null;
    }

    partial void OnSelectedControlChanged(FormControl? value) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();

        var control = SelectedControl;
        var definition = control?.Definition;

        if (control != null && definition != null)
        {
            var target = _file?.Model.Target ?? FormTarget.Web;

            foreach (var property in definition.Properties)
            {
                // ⛔ A property the target does not have is not offered. WinForms RadioButton has
                // no GroupName and WinForms ListBox no MultiSelect — measured, by csc. Offering
                // them would let the user set a value that silently never reaches the generated
                // code, which is the designer/runtime divergence D9 exists to prevent.
                if (!property.AppliesTo(target))
                {
                    continue;
                }

                Rows.Add(new FormPropertyRow(
                    control,
                    property,
                    _file?.DegradedReason(control.Id, property.Name),
                    () => Edited?.Invoke(this, EventArgs.Empty)));
            }
        }

        // ⚠ SelectedRow is cleared first: it points at a row of the PREVIOUS control, and leaving it
        // would leave the description pane describing a property that is no longer on screen.
        SelectedRow = null;

        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(HeaderKind));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionBody));
    }
}
