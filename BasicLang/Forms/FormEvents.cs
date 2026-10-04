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
    Display,
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
/// <param name="OracleExemption">
/// Why the WinForms snapshot is NOT the truth for this event — the event twin of
/// <see cref="FormPropertyDef.OracleExemption"/>, carried on the row so the parity test prints the
/// reason rather than keeping a hand list. Null for every event the snapshot judges.
/// </param>
/// <param name="IsWebDefault">
/// What a double-click opens on the WEB when the <see cref="IsDefault"/> event has no web meaning — a Panel's Paint
/// (owner decision 2026-09-29): Click, and the gesture says so. ⛔ Declared, never guessed as "the first event with a
/// web name": a silent pick is the widen-the-default failure <see cref="FormControlDef.DefaultEvent"/> exists to stop.
/// Only on a row whose default has no web name, at most once, and only on an event that has one.
/// </param>
/// <param name="WebWiring">
/// WHERE the page's DOM source for this event is (ADR 0021): the control's own element (the Form's is
/// <c>document.body</c>), the <c>window</c> (Form Resize), or a direct call at the end of
/// <c>InitializeComponent</c> (Form Load). <see cref="FormWebWiring.Window"/> and
/// <see cref="FormWebWiring.AfterInit"/> appear on the Form only.
/// </param>
/// <param name="WebFilter">
/// Which generated wrapper the page needs between the listener and the user's handler (ADR 0021 §3): the
/// WinForms KeyPress key set, or "focus came from outside the element" for Enter/Leave. Stated on the row so
/// the emitter never switches on an event's name.
/// </param>
public sealed record FormEventDef(
    string Name,
    string? WinFormsArgs = null,
    string? WebEvent = null,
    FormEventCategory? Category = null,
    string? Description = null,
    bool IsDefault = false,
    string? OracleExemption = null,
    bool IsWebDefault = false,
    FormWebWiring WebWiring = FormWebWiring.Element,
    FormWebFilter WebFilter = FormWebFilter.None);

/// <summary>Where an event's DOM source is on the page (ADR 0021 §1).</summary>
public enum FormWebWiring
{
    /// <summary>The control's own element; for the Form, <c>document.body</c> (its <c>HtmlTag</c>).</summary>
    Element,

    /// <summary><c>window</c> — the Form's Resize. Form only.</summary>
    Window,

    /// <summary>
    /// No listener: <c>Me.&lt;handler&gt;()</c> is the LAST statement of <c>InitializeComponent</c> — the Form's Load. A
    /// <c>window</c> <c>load</c> listener can register after the event fired and silently never run. Form only, at most
    /// one, and it is the default event.
    /// </summary>
    AfterInit
}

/// <summary>The generated wrapper an event needs on the page (ADR 0021 §3); None for a plain listener.</summary>
public enum FormWebFilter
{
    None,

    /// <summary>
    /// WinForms' KeyPress keys: <c>key.length === 1 || key === "Enter" || key === "Backspace" || key === "Escape"</c>,
    /// listened to as a <c>keydown</c> (<see cref="FormEvents.ListenType"/>). On exactly the events stored <c>keypress</c>.
    /// </summary>
    KeyPressKeys,

    /// <summary>
    /// "<c>relatedTarget</c> is outside the element": focus moving between two children of a Panel raises no
    /// Enter/Leave on the Panel, as in WinForms. On exactly the events stored <c>focusin</c>/<c>focusout</c>.
    /// </summary>
    FromOutside
}

/// <summary>
/// ⛔⛔ THE one answer to "which events are wired on this target" (spec §5). Every region-writer
/// question about a bind asks it — the emitter, the BL8032 refusal of a web bind (a control's or the
/// Form's), and the BL8028 warning / <c>IsEmittedBind</c> for a tray component — and so does the grid's
/// Events tab, so the grid can never offer an event BL8032 refuses or the emitter drops. It takes a
/// DEFINITION, not a control, so it serves the Form root's definition (not a FormControl) and every
/// control alike.
///
/// <para>⛔ "Wired means running" (CLAUDE.md): on the web a tray COMPONENT is wired only through its
/// <see cref="FormControlDef.WebScript"/> template, on its DEFAULT event. That rule lives HERE, not in
/// each caller — a component row with several web-named events must still answer only its default,
/// or the grid would offer binds the emitter silently drops.</para>
/// ⚠ The signature of <see cref="WiredOn"/> is FIXED (spec §5): slice 5 widened the rows and grew
/// <see cref="FormEventDef"/> (<see cref="FormEventDef.WebWiring"/>, <see cref="FormEventDef.WebFilter"/>),
/// never this shape. The representation piece 2 consumes is ADR 0021.
/// </summary>
public static class FormEvents
{
    public static IReadOnlyList<FormEventDef> WiredOn(FormControlDef definition, FormTarget target)
    {
        if (!definition.SupportsTarget(target) || definition.Events == null)
        {
            return Array.Empty<FormEventDef>();
        }

        if (target == FormTarget.Web && definition.IsComponent)
        {
            return definition.WebScript != null &&
                   definition.DefaultEventDef is { } defaultEvent && NameOn(defaultEvent, target) != null
                ? new[] { defaultEvent }
                : Array.Empty<FormEventDef>();
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

    /// <summary>
    /// The DOM type the page actually <c>addEventListener</c>s for <paramref name="evt"/> — <c>keydown</c> for a
    /// <see cref="FormWebFilter.KeyPressKeys"/> event (its STORED name stays <c>keypress</c>), otherwise its
    /// <see cref="FormEventDef.WebEvent"/>. The emitter, the run tiers and piece 2's library all ask this; null when the
    /// event has no web name.
    /// </summary>
    public static string? ListenType(FormEventDef evt) =>
        evt.WebEvent == null ? null : evt.WebFilter == FormWebFilter.KeyPressKeys ? "keydown" : evt.WebEvent;

    /// <summary>
    /// ⛔ GATES ONLY — the region writer never reads it (a test greps <c>RegionWriter.cs</c>). The DOM event INTERFACE
    /// a stored web event name is dispatched with, for the Edge run tier and piece 2's coverage gate; ONE table, stated
    /// explicitly (ADR 0021). A KeyPress (<c>keypress</c>) is a <c>KeyboardEvent</c> dispatched as a <c>keydown</c>
    /// through <see cref="ListenType"/>. <c>tick</c> is deliberately ABSENT: a Timer is a <c>setInterval</c> callback,
    /// never a dispatched DOM event. Null for a name the table does not hold.
    /// </summary>
    public static string? DomInterfaceOf(string webEvent) =>
        DomInterfaces.TryGetValue(webEvent, out var name) ? name : null;

    /// <summary>The table behind <see cref="DomInterfaceOf"/>, exposed for the gates that sweep it.</summary>
    public static readonly IReadOnlyDictionary<string, string> DomInterfaces =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["click"] = "MouseEvent",
            ["dblclick"] = "MouseEvent",
            ["mousedown"] = "MouseEvent",
            ["mouseup"] = "MouseEvent",
            ["mousemove"] = "MouseEvent",
            ["mouseenter"] = "MouseEvent",
            ["mouseleave"] = "MouseEvent",
            ["keydown"] = "KeyboardEvent",
            ["keyup"] = "KeyboardEvent",
            ["keypress"] = "KeyboardEvent",
            ["focusin"] = "FocusEvent",
            ["focusout"] = "FocusEvent",
            ["input"] = "Event",
            ["change"] = "Event",
            ["resize"] = "Event",
            ["load"] = "Event"
        };

    /// <summary>
    /// The .NET base chains of the handler args types the catalog names, keyed by the DERIVED type's last segment —
    /// what lets a <c>(sender As Object, e As CancelEventArgs)</c> handler fit FormClosing (ADR 0021 §4). ⚠ Filled in
    /// slice 5 Task 3 and falsified there by in-process Roslyn; empty until then.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ArgsBases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
