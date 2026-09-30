namespace BasicLang.Forms;

/// <summary>
/// WinForms' AMBIENT properties (Font, ForeColor, BackColor, Cursor): a control that does not set one shows its PARENT's,
/// all the way up to the Form, whose own default is the catalog's. The one answer to "what does this control inherit" —
/// the property grid's Font parts ask it (code review I1, 2026-09-29), so a part edit starts from the font the control
/// actually shows and never from a default it does not have.
/// </summary>
public static class FormAmbient
{
    /// <summary>
    /// The value <paramref name="control"/> inherits for <paramref name="property"/>: the nearest CONTAINER that carries a
    /// value its own row accepts, else the Form's value (<see cref="FormDocument.Properties"/>) when its row accepts it,
    /// else the Form row's catalog default; null when none of those has one.
    ///
    /// <para>⚠ A value its row does not accept (Degraded — frozen, preserved, never emitted) is skipped: WinForms never
    /// receives it, so the control inherits from further up, which is what running the form shows.</para>
    /// </summary>
    public static string? Inherited(FormDocument form, FormControl control, string property)
    {
        for (var parent = form.ParentOf(control); parent != null; parent = form.ParentOf(parent))
        {
            if (parent.Properties.TryGetValue(property, out var value) &&
                parent.Definition?.Property(property) is { } row && row.Accepts(value, form.Target))
            {
                return value;
            }
        }

        var rootRow = FormControlCatalog.FormRoot.Property(property);
        if (rootRow == null)
        {
            return null;
        }

        return form.Properties.TryGetValue(property, out var own) && rootRow.Accepts(own, form.Target)
            ? own
            : rootRow.DefaultFor(form.Target);
    }
}
