namespace BasicLang.Forms;

/// <summary>
/// WinForms' event groups (spec §2.5). ⚠ Not <see cref="FormPropertyCategory"/>: WinForms files events
/// under Action, Mouse, Key, Property Changed, Drag Drop — none of which is a property group. A member
/// the snapshot names that is missing here makes the parity test fail naming it.
/// </summary>
public enum FormEventCategory
{
    Action,
    Appearance,
    Asynchronous,
    Behavior,
    Data,
    DragDrop,
    Focus,
    Key,
    Layout,
    Misc,
    Mouse,
    PropertyChanged,
    WindowStyle
}

/// <summary>One event of one control kind, in both targets' vocabularies (spec §2.5).</summary>
/// <param name="Name">The WinForms event name — what <c>AddHandler x.Name</c> names.</param>
/// <param name="WinFormsArgs">The handler's <c>e</c> type, qualified when its namespace is not imported; null means <c>EventArgs</c>.</param>
/// <param name="WebEvent">The DOM event type (case-sensitive), or null when the event has no web meaning.</param>
/// <param name="Category">The Events tab group. Compared with WinForms by the parity test.</param>
/// <param name="Description">WinForms' own <c>[Description]</c>. Compared with the snapshot.</param>
/// <param name="IsDefault">The event a double-click means. At most one per row.</param>
public sealed record FormEventDef(
    string Name,
    string? WinFormsArgs = null,
    string? WebEvent = null,
    FormEventCategory? Category = null,
    string? Description = null,
    bool IsDefault = false);

/// <summary>
/// ⛔⛔ THE one answer to "which events are wired on this target" (spec §5). The region writer's BL8032
/// check and its emitter call it today; the grid's Events tab calls it in slice 5 — so the grid can
/// never offer an event BL8032 refuses or the emitter drops. It takes a DEFINITION, not a control, so
/// it serves the Form root's definition (plan Task 6 — not a FormControl) and every control alike.
/// ⚠ The signature is FIXED here (spec §5): widening in slice 5 changes the rows, never this shape.
/// </summary>
public static class FormEvents
{
    public static IReadOnlyList<FormEventDef> WiredOn(FormControlDef definition, FormTarget target)
    {
        if (!definition.SupportsTarget(target) || definition.Events == null)
        {
            return Array.Empty<FormEventDef>();
        }

        return definition.Events.Where(e => NameOn(e, target) != null).ToList();
    }

    /// <summary>The event's name in <paramref name="target"/>'s vocabulary, or null when it has none there.</summary>
    public static string? NameOn(FormEventDef evt, FormTarget target) => target switch
    {
        FormTarget.WinForms => evt.Name,
        FormTarget.Web => evt.WebEvent,
        _ => null
    };
}
