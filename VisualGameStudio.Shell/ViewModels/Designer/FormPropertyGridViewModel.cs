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

    /// <summary>
    /// The selected control's id — or the FORM's name when nothing is selected, because that is
    /// what the grid is then showing.
    /// </summary>
    public string Header => SelectedControl?.Id ?? _file?.Model.Name ?? "No selection";

    /// <summary>
    /// The control's KIND beside its id, the way VS's property window shows "button1  Button".
    /// Empty with no selection, so the header does not read "No selection No selection".
    /// </summary>
    public string HeaderKind => SelectedControl?.Kind
        ?? (_file != null ? _file.Model.RootElementName : string.Empty);

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

        // ⛔ Rebuild EXPLICITLY. Assigning null over null raises no change, so OnSelectedControlChanged
        // does not fire — which was harmless while an empty selection meant an empty grid, and stopped
        // being harmless the moment a form with no selection had rows of its own to show.
        SelectedControl = null;
        Rebuild();
    }

    partial void OnSelectedControlChanged(FormControl? value) => Rebuild();

    /// <summary>
    /// The rows VS shows for EVERY control, which this grid had none of: its name, where it is, how
    /// big it is, and its tab order.
    ///
    /// <para>⛔ These are not catalog properties — they are fields on the control and its geometry,
    /// so the catalog cannot supply them and adding them there would invent attributes the document
    /// does not have. Without them the grid could style a control but not place one: there was no
    /// way to type an exact X, and dragging is the wrong tool for "line these three up at 96".</para>
    ///
    /// <para>⚠ Name is READ-ONLY, deliberately and visibly. The id names the generated field, so a
    /// rename has to rewrite the user's designer region and check the new name is unique and legal;
    /// until that exists, a frozen row says so where an editable one would silently produce a
    /// document that no longer compiles. The Degraded tier already renders exactly this shape.</para>
    /// </summary>
    private void AddIntrinsicRows(FormControl control)
    {
        void Changed() => Edited?.Invoke(this, EventArgs.Empty);

        Rows.Add(new FormPropertyRow(
            "Name", FormPropertyType.String,
            () => control.Id,
            write: null,
            Changed,
            "The id names the generated field. Renaming is not supported here yet."));

        // ⚠ Geometry rows follow the shape the control actually HAS. A web control lives in a grid
        // cell and has no X/Y at all; offering them would let the user set a number the emitter
        // cannot use — the same designer/runtime divergence D9 exists to prevent.
        switch (control.Geometry)
        {
            case PixelGeometry pixel:
                Rows.Add(IntRow("X", () => pixel.X, v => pixel.X = v, Changed));
                Rows.Add(IntRow("Y", () => pixel.Y, v => pixel.Y = v, Changed));
                Rows.Add(IntRow("Width", () => pixel.Width, v => pixel.Width = Math.Max(1, v), Changed));
                Rows.Add(IntRow("Height", () => pixel.Height, v => pixel.Height = Math.Max(1, v), Changed));

                // ⛔⛔ PIXEL GEOMETRY ONLY, and that is D3 rather than an oversight. Anchor and Dock
                // are WinForms layout vocabulary; a .blwebform control lives in a grid CELL and has
                // no edges to anchor to. Offering them on the web would let the user set a value the
                // emitter cannot use — the designer/runtime divergence D9 exists to prevent — and
                // Task 21's retarget reports the loss when a form crosses formats.
                //
                // ⚠ Reached only through this arm, so the web grid cannot show them by accident:
                // there is no `if (target == Web) hide` to forget.
                Rows.Add(new FormPropertyRow(
                    "Anchor", FormPropertyType.String,
                    () => pixel.Anchor ?? "",
                    v => pixel.Anchor = string.IsNullOrWhiteSpace(v) ? null : v,
                    Changed,
                    editor: FormRowEditor.AnchorPicker));

                Rows.Add(new FormPropertyRow(
                    "Dock", FormPropertyType.String,
                    () => pixel.Dock ?? "",
                    v => pixel.Dock = string.IsNullOrWhiteSpace(v) ? null : v,
                    Changed,
                    editor: FormRowEditor.DockPicker));
                break;

            case GridGeometry grid:
                Rows.Add(IntRow("Col", () => grid.Col, v => grid.Col = Math.Max(0, v), Changed));
                Rows.Add(IntRow("Row", () => grid.Row, v => grid.Row = Math.Max(0, v), Changed));
                break;
        }

        // A component (Task 25) has no tab order. Its geometry rows were already absent — the switch
        // above has no arm for a null Geometry — but TabIndex was unconditional, and a Timer with a
        // TabIndex row would write a number the emitter cannot use.
        if (control.Definition?.IsComponent != true)
        {
            Rows.Add(IntRow(
                "TabIndex", () => control.TabIndex, v => control.TabIndex = Math.Max(0, v), Changed));
        }
    }

    /// <summary>
    /// An intrinsic Int row. The accessors read and write the live model, so the canvas redraws from
    /// the same object the grid edited rather than from a copy that has to be pushed back.
    /// </summary>
    private static FormPropertyRow IntRow(
        string name, Func<int> read, Action<int> write, Action changed) =>
        new(name,
            FormPropertyType.Int,
            () => read().ToString(),
            text =>
            {
                // ⚠ An unparseable value is IGNORED rather than coerced to zero. The numeric editor
                // should not produce one, but a binding can push mid-edit text — and snapping a
                // control to the origin because the user was halfway through typing "1" of "128" is
                // the kind of thing that makes a designer feel haunted.
                if (int.TryParse(text, out var parsed))
                {
                    write(parsed);
                }
            },
            changed);

    /// <summary>
    /// The FORM's own properties — what VS shows when nothing on the surface is selected.
    ///
    /// <para>⚠ The form is a <see cref="FormDocument"/>, not a <see cref="FormControl"/>, so it has
    /// no catalog row and none of its values live in an attribute dictionary. Intrinsic rows are
    /// the whole of it.</para>
    ///
    /// <para>⚠ Name is frozen for a stronger reason than a control's: it names the generated CLASS,
    /// and the document's own file name has to agree with it — a mismatch is refused at load.</para>
    /// </summary>
    private void AddFormRows(FormDocument form)
    {
        void Changed() => Edited?.Invoke(this, EventArgs.Empty);

        Rows.Add(new FormPropertyRow(
            "Name", FormPropertyType.String,
            () => form.Name,
            write: null,
            Changed,
            "The form's name is its class name, and the file name must agree with it."));

        Rows.Add(new FormPropertyRow(
            "Text", FormPropertyType.String,
            () => form.Text ?? "",
            v => form.Text = v,
            Changed));

        if (form.Target == FormTarget.WinForms)
        {
            // ⚠ The CLIENT size, as WinForms' ClientSize is — the same numbers the canvas's own
            // resize grips write, so typing 400 here and dragging to 400 produce one document.
            Rows.Add(IntRow("Width", () => form.Width ?? 0, v => form.Width = Math.Max(1, v), Changed));
            Rows.Add(IntRow("Height", () => form.Height ?? 0, v => form.Height = Math.Max(1, v), Changed));
        }
        else if (form.Layout is { } layout)
        {
            // ⛔ Kept verbatim as CSS track lists, because the browser is the renderer. Offering
            // them as free text is deliberate: "auto,1fr" and "repeat(3, 1fr)" are both legal and
            // neither is something a typed editor could enumerate.
            Rows.Add(new FormPropertyRow(
                "Cols", FormPropertyType.String,
                () => layout.Cols ?? "", v => layout.Cols = v, Changed));
            Rows.Add(new FormPropertyRow(
                "Rows", FormPropertyType.String,
                () => layout.Rows ?? "", v => layout.Rows = v, Changed));
            Rows.Add(new FormPropertyRow(
                "Gap", FormPropertyType.String,
                () => layout.Gap ?? "", v => layout.Gap = v, Changed));
        }
    }

    private void Rebuild()
    {
        Rows.Clear();

        var control = SelectedControl;
        var definition = control?.Definition;

        if (control != null && definition != null)
        {
            var target = _file?.Model.Target ?? FormTarget.Web;

            AddIntrinsicRows(control);

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
        else if (_file?.Model is { } form)
        {
            // ⛔ The FORM's own properties, shown when no control is selected — which is what VS
            // does, and what this grid did not: clicking the form said "No selection" and offered
            // nothing, so a form's caption and size could only be changed by editing the XML.
            AddFormRows(form);
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
