using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// The Events tab asks its host for a handler (pre-flight D-5): <paramref name="Handler"/> is the name the user TYPED, or
/// null for a double-click (the host computes <c>&lt;Id&gt;_&lt;Event&gt;</c>, or navigates to the handler already bound).
/// The host writes the stub (<see cref="FormHandlers.Plan"/>), binds it, and opens the code.
/// </summary>
public sealed record FormHandlerRequest(FormBindOwner Owner, FormEventDef Event, string? Handler);

/// <summary>
/// One row of the grid's Events tab (property-grid slice 5, D-5): an event of the selected control — or of the Form with
/// nothing selected — and the handler bound to it. ⛔ Built only from <see cref="FormEvents.WiredOn"/> (never
/// <c>definition.Events</c>), so the tab can never offer an event the region writer refuses or drops: a web Panel has no
/// Paint, a web Timer only Tick.
///
/// <para>The value cell's commit rules (VS): pick a handler that FITS → bind it (the target's own vocabulary, the DOM name
/// on the web), the code untouched; clear the text → unbind, the code never deleted; type a NEW legal name → ask the host
/// for that stub (<see cref="FormHandlerRequest"/>); a name <see cref="FormHandlers.DescribeUnusableHandler"/> refuses
/// (illegal, a keyword, reserved, the constructor, a control or the form, another member, a Shared/ByRef/non-fitting Sub)
/// → REFUSED, said in the description pane (spec §7), nothing written.</para>
///
/// <para>⚠ The row never scans the code-behind itself: the grid scans it ONCE per refresh and every row reads that scan
/// (review ruling 5).</para>
/// </summary>
public sealed partial class FormEventRow : ObservableObject, IFormDisplayRow
{
    private readonly FormDocument _form;
    private readonly Func<FormCodeScanResult> _scan;
    private readonly Action _edited;
    private readonly Action<FormHandlerRequest> _requested;
    private IReadOnlyList<string> _choices = Array.Empty<string>();

    public FormEventRow(
        FormDocument form, FormBindOwner owner, FormEventDef evt, Func<FormCodeScanResult> scan, Action edited,
        Action<FormHandlerRequest> requested)
    {
        _form = form;
        Owner = owner;
        Event = evt;
        _scan = scan;
        _edited = edited;
        _requested = requested;
        RefreshChoices();
    }

    public FormBindOwner Owner { get; }

    public FormEventDef Event { get; }

    /// <summary>The WinForms event name — what VS lists, on both targets.</summary>
    public string Name => Event.Name;

    /// <summary>The Events tab group: WinForms' own category (<see cref="FormEventCategory"/>).</summary>
    public string Category => (Event.Category ?? FormEventCategory.Misc).ToString();

    /// <summary>WinForms' own description, for the pane.</summary>
    public string Description => Event.Description ?? "";

    /// <summary>
    /// The name a bind stores on this target: <c>Click</c> on WinForms, <c>click</c> on the web. ⛔ Never falls back to the
    /// WinForms name: a row exists only for an event <see cref="FormEvents.WiredOn"/> wires here, so a missing name is a
    /// defect, and storing <c>Click</c> on a page would register a listener that never fires.
    /// </summary>
    public string EventName => FormEvents.NameOn(Event, _form.Target)
        ?? throw new InvalidOperationException($"'{Event.Name}' has no name on {_form.Target}, so it cannot be an Events-tab row");

    /// <summary>The handler bound to this event, or empty.</summary>
    public string Handler => Bind?.Handler ?? "";

    /// <summary>Why the last typed value was refused, while it is the newest thing to say; null otherwise.</summary>
    [ObservableProperty]
    private string? _refusal;

    private FormBind? Bind => Owner.Binds.FirstOrDefault(b =>
        !b.UsesReservedDataBinding && !string.IsNullOrEmpty(b.Handler) &&
        string.Equals(b.Event, EventName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The handlers that fit this event (<see cref="FormHandlers.FittingHandlers(FormDocument, FormBindOwner, FormEventDef, FormCodeScanResult)"/>),
    /// document order. ⛔ The SAME instance while its items are unchanged — a new list during a pick undoes the pick
    /// (slice 4 Task 3).
    /// </summary>
    public IReadOnlyList<string> Choices => _choices;

    /// <summary>Re-reads the fitting handlers from the grid's current scan; a new list only when its items changed.</summary>
    public void RefreshChoices()
    {
        var fresh = FormHandlers.FittingHandlers(_form, Owner, Event, _scan());
        if (!fresh.SequenceEqual(_choices, StringComparer.Ordinal))
        {
            _choices = fresh;
            OnPropertyChanged(nameof(Choices));
        }
    }

    /// <summary>Commits the value cell's text — see the class remarks for the rules.</summary>
    public void Commit(string? text)
    {
        var typed = (text ?? "").Trim();
        Refusal = null;

        if (typed.Length == 0)
        {
            if (FormHandlers.Unbind(Owner, EventName))
            {
                Changed();
            }

            return;
        }

        // The bound name again, in any case, is the same member to BasicLang: nothing to do (no Edited, no undo step).
        if (string.Equals(typed, Handler, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var scan = _scan();
        if (FormHandlers.DescribeUnusableHandler(_form, Owner, Event, typed, scan) is { } refusal)
        {
            Refusal = refusal;
            return;
        }

        if (scan.Find(typed) is { } existing)
        {
            if (Bind is { } bound)
            {
                bound.Handler = existing.Name;
            }
            else
            {
                FormHandlers.EnsureBind(Owner, EventName, existing.Name);
            }

            Changed();
            return;
        }

        // A new, legal name: the host writes the stub and binds it (it owns the code-behind).
        _requested(new FormHandlerRequest(Owner, Event, typed));
    }

    /// <summary>A double-click on the row: create-or-navigate its handler (the host decides which).</summary>
    public void RequestHandler() => _requested(new FormHandlerRequest(Owner, Event, null));

    private void Changed()
    {
        OnPropertyChanged(nameof(Handler));
        _edited();
    }
}
