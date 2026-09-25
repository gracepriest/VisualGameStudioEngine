using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// What is selected on the canvas, and the algebra for changing it (Task 20).
///
/// <para>A plain class with no Avalonia in it, so the rules — extend, toggle, promote a new primary
/// — are unit-tested rather than inferred from clicking around in the IDE. The canvas owns one of
/// these and repaints on <see cref="Changed"/>.</para>
///
/// <para>⚠ <b><see cref="Primary"/> is what makes align and make-same-size meaningful.</b> "Align
/// left" is "align to WHICH left?", and VS's answer is the control selected last. Without a primary
/// the command has to invent a rule — leftmost, or first in document order — and either one moves
/// controls the user was not expecting to move.</para>
///
/// <para>⛔ Membership is by REFERENCE. Ids are not unique while a paste is mid-flight (the clipboard
/// mints new ones as it goes), and a selection that matched on id would outline the wrong box.</para>
/// </summary>
public sealed class FormSelection
{
    private readonly List<FormControl> _controls = new();

    /// <summary>In selection order — the last is <see cref="Primary"/>.</summary>
    public IReadOnlyList<FormControl> Controls => _controls;

    /// <summary>The reference control for align and size commands: the one selected last.</summary>
    public FormControl? Primary => _controls.Count == 0 ? null : _controls[^1];

    public bool IsEmpty => _controls.Count == 0;

    /// <summary>
    /// Raised only when the selection ACTUALLY changed.
    ///
    /// <para>⚠ The canvas repaints on this. Raising it for a no-op — clicking a control that is
    /// already the whole selection, which is what every drag begins with — would repaint the entire
    /// form on each one.</para>
    /// </summary>
    public event EventHandler? Changed;

    public bool Contains(FormControl control) =>
        _controls.Any(c => ReferenceEquals(c, control));

    /// <summary>Replaces the selection with one control, or clears it when given null.</summary>
    public void Set(FormControl? control)
    {
        if (control == null)
        {
            Clear();
            return;
        }

        if (_controls.Count == 1 && ReferenceEquals(_controls[0], control))
        {
            return;
        }

        _controls.Clear();
        _controls.Add(control);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a control, making it the new primary. A no-op when it is already selected.</summary>
    public void Add(FormControl control)
    {
        if (Contains(control))
        {
            return;
        }

        _controls.Add(control);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ctrl-click: selects the control if it is not selected, deselects it if it is.
    ///
    /// <para>⛔ Removing the primary must leave a valid one behind. <see cref="Primary"/> is derived
    /// from the list rather than stored precisely so this cannot go stale — a stored field would
    /// keep pointing at a control the user just deselected, and align would move everything to it.</para>
    /// </summary>
    public void Toggle(FormControl control)
    {
        var at = _controls.FindIndex(c => ReferenceEquals(c, control));
        if (at < 0)
        {
            Add(control);
            return;
        }

        _controls.RemoveAt(at);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the selection wholesale — the marquee's result.</summary>
    public void SetRange(IEnumerable<FormControl> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        var next = controls.ToList();
        if (next.Count == _controls.Count &&
            next.Zip(_controls).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        _controls.Clear();
        _controls.AddRange(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_controls.Count == 0)
        {
            return;
        }

        _controls.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
