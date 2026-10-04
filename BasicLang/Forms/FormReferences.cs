namespace BasicLang.Forms;

/// <summary>
/// Which controls a <see cref="FormPropertyType.Reference"/> row may name — the Form's <c>AcceptButton</c> and
/// <c>CancelButton</c> (slice 4 D-7).
///
/// <para>⛔ ONE answer for two consumers: the property grid's drop-down lists <see cref="Candidates"/>, and the region
/// writer's BL8034 check asks <see cref="IsAllowed"/>, which is membership of that same list. So the drop-down can never
/// offer an Id the writer then refuses to emit, nor hide one it would emit.</para>
/// </summary>
public static class FormReferences
{
    /// <summary>
    /// The Ids of every control on <paramref name="form"/> whose kind is in the row's
    /// <see cref="FormPropertyDef.ReferenceKinds"/>, in DOCUMENT order (<see cref="FormDocument.AllControls"/>: a
    /// container, then its children, then its next sibling) — a Button inside a Panel included.
    ///
    /// <para>⛔ Never a tray component: it has no place on the form, and every walker chooses its lists explicitly
    /// (<see cref="FormDocument.AllControls"/> is the visual tree and never includes one). No row's kinds name a component
    /// today, so this is the rule, not a behaviour change. A row with no kinds allows nothing.</para>
    /// </summary>
    public static IReadOnlyList<string> Candidates(FormDocument form, FormPropertyDef row)
    {
        var kinds = row.ReferenceKinds ?? Array.Empty<string>();
        return form.AllControls()
            .Where(control => kinds.Contains(control.Kind, StringComparer.OrdinalIgnoreCase))
            .Select(control => control.Id)
            .ToList();
    }

    /// <summary>
    /// Whether <paramref name="id"/> names one of the <see cref="Candidates"/> — ORDINAL, as
    /// <see cref="FormDocument.FindById"/> and the generated field name are: <c>BTNOK</c> is not <c>btnOk</c>.
    /// </summary>
    public static bool IsAllowed(FormDocument form, FormPropertyDef row, string id) =>
        Candidates(form, row).Contains(id, StringComparer.Ordinal);
}
