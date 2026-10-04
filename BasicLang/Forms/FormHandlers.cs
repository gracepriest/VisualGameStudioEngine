using System.Text;

namespace BasicLang.Forms;

/// <summary>What a double-click did, or why it did nothing.</summary>
public enum HandlerOutcome
{
    /// <summary>The handler did not exist and a stub was written into the code-behind.</summary>
    Created,

    /// <summary>The handler already existed; the file is untouched and the caret moves to it.</summary>
    Navigated,

    /// <summary>Nothing was done. <see cref="FormHandlerPlan.Refusal"/> says why, in the user's terms.</summary>
    Refused
}

/// <summary>The result of double-clicking a control: the code-behind as it should now read, and where to put the caret.</summary>
/// <param name="CodeText">The <c>.bas</c> after any insert — unchanged when navigating or refusing.</param>
/// <param name="CaretLine">1-based line to reveal, or 0 when refused.</param>
/// <param name="Notice">
/// Set when the double-click opened an event OTHER than the kind's default because the default has no meaning on
/// this target (a web Panel: Paint → Click). The host must show it — a substitution only the planner knew would be
/// as silent as a Paint handler that never fires. Null for every ordinary gesture.
/// </param>
public sealed record FormHandlerPlan(
    HandlerOutcome Outcome,
    string EventName,
    string Handler,
    string CodeText,
    int CaretLine,
    string? Refusal,
    string? Notice = null);

/// <summary>Which side of the designer's init region a handler stub goes on.</summary>
public enum FormHandlerPlacement
{
    /// <summary>The page: an <c>AddressOf</c> naming a Sub declared later erases its parameter types (BL8013).</summary>
    AboveInitRegion,

    /// <summary>WinForms: VS's place, under the scaffold's "your event handlers go here".</summary>
    BelowInitRegion
}

/// <summary>One parameter of a handler's signature.</summary>
public sealed record FormHandlerParameter(string Name, string Type);

/// <summary>A handler's signature and its side of the init region — what <see cref="FormHandlers.Shape"/> answers.</summary>
public sealed record FormHandlerShape(IReadOnlyList<FormHandlerParameter> Parameters, FormHandlerPlacement Placement)
{
    /// <summary>The parameter list as BasicLang source, parentheses included — <c>sender</c>/<c>e</c> with their types.</summary>
    public string ParameterList => "(" + string.Join(", ", Parameters.Select(p => $"{p.Name} As {p.Type}")) + ")";

    /// <summary>
    /// ⛔ The CALL shape — how generated code calls a handler of this shape, passing its own parameters through: the web
    /// wrapper's <c>Me.&lt;h&gt;(e)</c> and the Form Load's <c>Me.&lt;h&gt;()</c> (review ruling 3). <c>Me.</c>-qualified
    /// always. Read by the region writer, so a second style changes ONE place.
    /// </summary>
    public string Call(string handler) => $"Me.{handler}(" + string.Join(", ", Parameters.Select(p => p.Name)) + ")";
}

/// <summary>
/// Task 22 — the double-click gesture: create the control's default handler if it is absent, navigate
/// to it if it is present. Slice 5: any event of any owner (a control, or the Form — <see cref="FormBindOwner"/>),
/// the handlers that FIT an event (the Events tab's drop-down), and the ONE signature rule (<see cref="Shape"/>).
///
/// <para>Pure text in, pure text out, like <see cref="FormScaffolder"/> and <see cref="RegionWriter"/>
/// — no file system and no IDE, so the designer, the CLI and the tests drive one implementation. The
/// caller owns reading and writing the <c>.bas</c>.</para>
///
/// <para>⛔⛔ <b>WHERE the stub goes is the whole problem.</b> An append to the end of the class is
/// correct on WinForms and broken on the web: D8's ordering rule means an <c>AddressOf</c> naming a
/// <c>Sub</c> declared BELOW the region that wires it erases the parameter types to
/// <c>Action(Of Object)</c>, and <c>addEventListener</c> rejects it. The failure is a delegate
/// conversion error in generated code, naming neither the control the user double-clicked nor the
/// rule that was broken.</para>
/// </summary>
public static class FormHandlers
{
    /// <summary>
    /// The event a double-click on <paramref name="kind"/> means, or null when the catalog does not
    /// say — in which case the gesture refuses by name rather than guessing.
    /// </summary>
    public static string? DefaultEvent(string kind, FormTarget target) =>
        FormControlCatalog.Find(kind)?.DefaultEvent(target);

    /// <summary>The EVENT a double-click on <paramref name="definition"/> opens on <paramref name="target"/>, or null.</summary>
    public static FormEventDef? DefaultEventDef(FormControlDef definition, FormTarget target) =>
        definition.DefaultEventDefOn(target);

    /// <summary>
    /// <c>btnLogin_Click</c> — VS's shape. ⛔ Callers pass the WinForms event NAME on both targets (owner decision
    /// 2026-09-29): <c>GroupBox1_Enter</c> on the page too, never the DOM's <c>GroupBox1_Focusin</c>. The PascalCase
    /// step remains for a caller that has only a DOM name.
    /// </summary>
    public static string NameFor(string controlId, string eventName) =>
        $"{controlId}_{Pascal(eventName)}";

    private static string Pascal(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    // ==================================================================
    // THE signature rule (D-4, ADR 0021 §4)
    // ==================================================================

    /// <summary>
    /// ⛔ The ONE place a form's code style is read (review ruling 3) — <see cref="Shape"/> asks it, and everything that
    /// writes, offers or calls a handler asks <see cref="Shape"/>, so the stub writer, <see cref="Fits"/> and the region
    /// writer's calls can never disagree. Today every form is Classic; piece 2 replaces this
    /// body with its reading of the file's style (its "ONE reader of a file's style").
    /// </summary>
    public static FormCodeStyle StyleOf(FormDocument form) => FormCodeStyle.Classic;

    /// <summary>
    /// ⛔⛔ THE one place a handler's signature and its side of the init region are decided. The stub writer
    /// (<see cref="Insert"/>) and <see cref="Fits"/> both READ it and neither restates it, so the stub the designer writes
    /// is exactly the handler the drop-down offers (<c>FormHandlerShapeTests</c>); the region writer takes its handler
    /// CALLS from it too (<see cref="FormHandlerShape.Call"/>). The style comes from <see cref="StyleOf"/>, read here and
    /// nowhere else. Today's one style is Classic; piece 2 adds its Portable arm HERE, never a second signature site.
    /// <list type="bullet">
    /// <item>WinForms: <c>sender</c> typed Object and <c>e</c> typed as the event's args (or EventArgs), below the region.</item>
    /// <item>Web: one <c>e</c> typed DomEvent for a listener — filtered or not; <c>()</c> for the Form's Load (a direct call at
    /// the end of init) and for a row whose callback takes no event (a Timer: <c>Window.setInterval</c> takes an
    /// <c>Action</c> and refuses <c>Action(Of DomEvent)</c>, measured). Above the region.</item>
    /// </list>
    /// </summary>
    public static FormHandlerShape Shape(FormBindOwner owner, FormEventDef evt, FormTarget target)
    {
        var style = StyleOf(owner.Form);
        if (style != FormCodeStyle.Classic)
        {
            throw new ArgumentOutOfRangeException(nameof(owner), style, "only the Classic style exists yet");
        }

        if (target == FormTarget.Web)
        {
            var takesEvent = evt.WebWiring != FormWebWiring.AfterInit && owner.Definition?.WebHandlerTakesEvent != false;
            return new FormHandlerShape(
                takesEvent ? new[] { new FormHandlerParameter("e", "DomEvent") } : Array.Empty<FormHandlerParameter>(),
                FormHandlerPlacement.AboveInitRegion);
        }

        return new FormHandlerShape(
            new[] { new FormHandlerParameter("sender", "Object"), new FormHandlerParameter("e", evt.WinFormsArgs ?? "EventArgs") },
            FormHandlerPlacement.BelowInitRegion);
    }

    /// <summary>
    /// Whether <paramref name="sub"/> can be wired to <paramref name="evt"/> — read off <see cref="Shape"/>, parameter by
    /// parameter, the same count and each type fitting (D-4): <c>Object</c> takes an untyped or <c>Object</c> parameter;
    /// <c>DomEvent</c> only <c>DomEvent</c> (an untyped one is erased to <c>Object</c>, which <c>addEventListener</c>
    /// refuses); a WinForms args type <c>A</c> takes <c>A</c>, a .NET BASE of <c>A</c> (<see cref="FormEvents.ArgsBases"/>),
    /// <c>EventArgs</c>, <c>Object</c> or an untyped parameter — delegate parameter contravariance, falsified against
    /// csc by <c>FormHandlerFitCscTests</c>. Types compare on their last segment, ignoring case. ⛔ A <c>ByRef</c>
    /// parameter never fits (csc CS0123: an event delegate's parameters are by value).
    /// </summary>
    public static bool Fits(FormBindOwner owner, FormEventDef evt, FormTarget target, FormDeclaredSub sub)
    {
        var shape = Shape(owner, evt, target);
        if (sub.Parameters.Count != shape.Parameters.Count || sub.Parameters.Any(p => p.IsByRef))
        {
            return false;
        }

        return shape.Parameters.Zip(sub.Parameters).All(pair => TypeFits(pair.First.Type, pair.Second.Type));
    }

    private static bool TypeFits(string wanted, string? declared)
    {
        var w = LastSegment(wanted);
        var d = declared == null ? null : LastSegment(declared);

        if (w.Equals("DomEvent", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(d, "DomEvent", StringComparison.OrdinalIgnoreCase);
        }

        if (d == null || d.Equals("Object", StringComparison.OrdinalIgnoreCase) || d.Equals(w, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (w.Equals("Object", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return d.Equals("EventArgs", StringComparison.OrdinalIgnoreCase) ||
               (FormEvents.ArgsBases.TryGetValue(w, out var bases) &&
                bases.Any(b => b.Equals(d, StringComparison.OrdinalIgnoreCase)));
    }

    private static string LastSegment(string type)
    {
        var t = type.Trim();
        var dot = t.LastIndexOf('.');
        return dot < 0 ? t : t[(dot + 1)..];
    }

    /// <summary>
    /// The user's Subs that FIT <paramref name="evt"/> (<see cref="Fits"/>), in document order — the Events tab's handler
    /// drop-down. Never a Function (the scanner reads Subs only), <c>New</c>, <c>InitializeComponent</c>, or anything
    /// inside the designer regions (the scanner skips them).
    /// </summary>
    public static IReadOnlyList<string> FittingHandlers(
        FormDocument form, FormBindOwner owner, FormEventDef evt, string codeText) =>
        FormCodeScan.DeclaredSubs(codeText, form.Name)
            .Where(s => !s.Name.Equals("New", StringComparison.OrdinalIgnoreCase) &&
                        !s.Name.Equals("InitializeComponent", StringComparison.OrdinalIgnoreCase))
            .Where(s => Fits(owner, evt, form.Target, s))
            .Select(s => s.Name)
            .ToList();

    // ==================================================================
    // Binds
    // ==================================================================

    /// <summary>
    /// Wires <paramref name="handler"/> to <paramref name="eventName"/> unless that event is already
    /// bound. Returns whether it added one.
    ///
    /// <para>⛔ A stub nothing wires is dead code — the region emits the
    /// <c>addEventListener</c>/<c>AddHandler</c> only for a bind that exists, so creating the Sub
    /// without this leaves the user with a handler that never fires and nothing to see why.</para>
    /// </summary>
    public static bool EnsureBind(FormBindOwner owner, string eventName, string handler)
    {
        if (owner.Binds.Any(b => !b.UsesReservedDataBinding &&
                                 string.Equals(b.Event, eventName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        owner.Binds.Add(new FormBind { Event = eventName, Handler = handler });
        return true;
    }

    /// <summary>The control overload of <see cref="EnsureBind(FormBindOwner, string, string)"/>.</summary>
    public static bool EnsureBind(FormControl control, string eventName, string handler) =>
        EnsureBind(new FormBindOwner(new FormDocument(), control), eventName, handler);

    /// <summary>
    /// Removes every bind of <paramref name="eventName"/> from the owner — the Events tab's cleared cell. ⛔ The code is
    /// never touched: VS leaves the handler in place when you clear the cell. ⛔ A reserved data-binding bind
    /// (<see cref="FormBind.UsesReservedDataBinding"/>) sharing the event name is not event wiring and is never removed.
    /// Returns whether anything was removed.
    /// </summary>
    public static bool Unbind(FormBindOwner owner, string eventName) =>
        owner.Binds.RemoveAll(b => !b.UsesReservedDataBinding &&
                                   string.Equals(b.Event, eventName, StringComparison.OrdinalIgnoreCase)) > 0;

    // ==================================================================
    // Plans
    // ==================================================================

    /// <summary>
    /// Plans the double-click: which handler, and the code-behind with it present.
    ///
    /// <para>⚠ An existing bind wins over the computed name. The user may have wired
    /// <c>SignIn</c> by hand; opening <c>btnLogin_Click</c> instead would put them in a Sub the form
    /// never calls, and they would rightly conclude the designer was broken.</para>
    /// </summary>
    public static FormHandlerPlan PlanDefault(FormDocument form, FormControl control, string codeText) =>
        PlanDefault(form, new FormBindOwner(form, control), codeText);

    /// <summary>The owner overload: a control, or the Form (its Load — VS's double-click on the form).</summary>
    public static FormHandlerPlan PlanDefault(FormDocument form, FormBindOwner owner, string codeText)
    {
        var definition = owner.Definition;
        var evt = definition?.DefaultEventDefOn(form.Target);
        if (evt == null || string.IsNullOrEmpty(FormEvents.NameOn(evt, form.Target)))
        {
            return Refuse(codeText,
                $"'{owner.KindName}' has no default event for {Describe(form.Target)}, so there is " +
                "nothing for a double-click to open. Add one to the control catalog.");
        }

        var plan = Plan(form, owner, evt, codeText);

        // A substitution is named (owner decision 2026-09-29): the kind's default has no meaning here, so the
        // gesture opened the row's declared fallback instead — and says so, rather than leaving the user waiting
        // for a Paint handler a page can never raise.
        var notice = definition?.DefaultEventDef is { } preferred && !ReferenceEquals(preferred, evt)
            ? $"{DesignCodes.DefaultEventNotOnTarget}: a {owner.KindName}'s default event, {preferred.Name}, has no " +
              $"{Describe(form.Target)} equivalent, so the double-click opened {evt.Name} ('{FormEvents.NameOn(evt, form.Target)}') instead."
            : null;

        return plan.Outcome == HandlerOutcome.Refused || notice == null ? plan : plan with { Notice = notice };
    }

    /// <summary>
    /// Plans <paramref name="evt"/>'s handler on <paramref name="owner"/>: <paramref name="handlerName"/> when given (a
    /// name the user typed in the Events tab), else the handler an existing bind on that event names (never renamed),
    /// else the convention <c>&lt;Prefix&gt;_&lt;WinForms event&gt;</c> on BOTH targets (owner decision 2026-09-29).
    /// Navigates to a Sub that already exists (found ignoring case); writes a stub in <see cref="Shape"/>'s signature
    /// otherwise.
    /// </summary>
    public static FormHandlerPlan Plan(
        FormDocument form, FormBindOwner owner, FormEventDef evt, string codeText, string? handlerName = null)
    {
        var eventName = FormEvents.NameOn(evt, form.Target);
        if (string.IsNullOrEmpty(eventName))
        {
            return Refuse(codeText,
                $"{owner.Label}'s {evt.Name} event has no meaning on {Describe(form.Target)}, so no handler was written for it.");
        }

        var bind = owner.Binds.FirstOrDefault(
            b => string.Equals(b.Event, eventName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(b.Handler));
        var handler = handlerName ?? bind?.Handler ?? NameFor(owner.Prefix, evt.Name);

        return PlanHandler(form, owner, evt, eventName, handler, codeText);
    }

    /// <summary>
    /// Plans the stub for ONE existing bind — the handler it names, with ITS event's signature.
    ///
    /// <para>⛔ The retarget's pair needs this for every bind that crossed, not only the default event's
    /// (code review, 2026-09-29): a crossed non-default bind — a GroupBox's Click beside its Enter — wired a Sub the
    /// pair never declared, and the retargeted form stopped compiling on both targets.</para>
    /// </summary>
    /// <returns>A plan, or a refusal when the bind names no handler or no event of the kind.</returns>
    public static FormHandlerPlan PlanBind(FormDocument form, FormBindOwner owner, FormBind bind, string codeText)
    {
        var evt = owner.Definition == null ? null : EventOn(owner.Definition, bind.Event, form.Target);
        if (evt == null || string.IsNullOrEmpty(bind.Handler))
        {
            return Refuse(codeText,
                $"{owner.Label} has a bind on '{bind.Event}', which is not an event '{owner.KindName}' has on " +
                $"{Describe(form.Target)}, so no handler was written for it.");
        }

        return PlanHandler(form, owner, evt, bind.Event, bind.Handler, codeText);
    }

    /// <summary>The control overload of <see cref="PlanBind(FormDocument, FormBindOwner, FormBind, string)"/>.</summary>
    public static FormHandlerPlan PlanBind(FormDocument form, FormControl control, FormBind bind, string codeText) =>
        PlanBind(form, new FormBindOwner(form, control), bind, codeText);

    /// <summary>Every handler a bind of ANOTHER owner names (the controls, the components, the Form — minus <paramref name="owner"/>).</summary>
    private static HashSet<string> OtherOwnersHandlers(FormDocument form, FormBindOwner owner) =>
        form.AllControls().Concat(form.AllComponents())
            .Where(c => !ReferenceEquals(c, owner.Control))
            .SelectMany(c => c.Binds)
            .Concat(owner.Control == null ? Enumerable.Empty<FormBind>() : form.Binds)
            .Where(b => !string.IsNullOrEmpty(b.Handler))
            .Select(b => b.Handler)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The kind's event that <paramref name="name"/> names in <paramref name="target"/>'s vocabulary, or null.</summary>
    private static FormEventDef? EventOn(FormControlDef definition, string name, FormTarget target) =>
        definition.Events?.FirstOrDefault(e =>
            string.Equals(FormEvents.NameOn(e, target), name, StringComparison.OrdinalIgnoreCase));

    private static FormHandlerPlan PlanHandler(
        FormDocument form, FormBindOwner owner, FormEventDef evt, string eventName, string handler, string codeText)
    {
        var index = new Recognizer.SourceIndex(codeText);
        var subs = FormCodeScan.DeclaredSubs(codeText, form.Name);

        // ⛔ Review ruling 4: a Sub that matches only IGNORING CASE and is ANOTHER owner's handler is a conflict, not this
        // owner's handler — a form `Pic` and a control `pic` compute `Pic_Click` and `pic_Click`, one member to BasicLang,
        // and reusing it would bind the control to the form's Click. VS appends `_1`, `_2`, … until the name is free (no
        // Sub of that name in any case, and no bind naming it). An EXACT match is navigated to as before (deliberate
        // sharing), and so is a case-variant nobody else binds (the M6 hand-written `btnlogin_click`).
        var match = subs.FirstOrDefault(s => string.Equals(s.Name, handler, StringComparison.OrdinalIgnoreCase));
        if (match != null && !string.Equals(match.Name, handler, StringComparison.Ordinal) &&
            OtherOwnersHandlers(form, owner).Contains(match.Name))
        {
            var taken = OtherOwnersHandlers(form, owner).Concat(owner.Binds.Select(b => b.Handler))
                .Concat(subs.Select(s => s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var n = 1;
            while (taken.Contains($"{handler}_{n}"))
            {
                n++;
            }

            handler = $"{handler}_{n}";
            match = null;
        }

        // ⛔ Through the ONE scanner, ignoring case: a hand-written `btnlogin_click` is the same member in BasicLang, and
        // writing a second `btnLogin_Click` beside it would be a duplicate declaration (D-11, M6).
        if (match is { } existing)
        {
            // Already there — land in the body, one line below the signature.
            return new FormHandlerPlan(
                HandlerOutcome.Navigated, eventName, existing.Name, codeText,
                Math.Min(existing.Line + 1, Math.Max(1, index.LineCount)), null);
        }

        var init = RegionMarkers.Find(RegionMarkers.Scan(codeText), RegionMarkers.Init);
        if (init == null)
        {
            return Refuse(codeText,
                $"'{form.Name}' has no designer init region in its code-behind, so the designer " +
                "does not own this file and will not write a handler into it. Open it in Code view " +
                "and add the handler yourself.");
        }

        if (init.State == RegionState.Malformed)
        {
            return Refuse(codeText,
                "the designer region in this form's code-behind is malformed, so the handler was " +
                "not written. Fix the '<vgs:designer>' markers and try again.");
        }

        return Insert(codeText, index, init, eventName, handler, Shape(owner, evt, form.Target));
    }

    /// <summary>
    /// Writes the stub — <paramref name="shape"/>'s signature, on <paramref name="shape"/>'s side of the region. ⛔ Never
    /// its own signature or placement: <see cref="Shape"/> is the only place either is decided.
    /// </summary>
    private static FormHandlerPlan Insert(
        string codeText, Recognizer.SourceIndex index, FormRegion init, string eventName, string handler,
        FormHandlerShape shape)
    {
        // ⚠ The file's own terminator, not the platform's. A stub inserted with the wrong one leaves
        // a file with both, which reads as a whole-file diff the next time anything touches it.
        var newline = codeText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = IndentOf(index, init.Line);

        var stub = new StringBuilder()
            .Append(indent).Append("Private Sub ").Append(handler).Append(shape.ParameterList).Append(newline)
            .Append(indent).Append(indent).Append(newline)
            .Append(indent).Append("End Sub").Append(newline)
            .Append(newline)
            .ToString();

        // ⛔⛔ The two targets insert on OPPOSITE sides of the init region, and both are measured (Shape says which).
        //
        // Web: ABOVE it, because D8's ordering rule bites — a handler below loses its parameter
        //   types and addEventListener rejects the erased Action(Of Object).
        // WinForms: BELOW it, because the rule does NOT bite there (the event is an unresolvable
        //   .NET member typed Object, so there is no declared delegate to mismatch) and because the
        //   scaffold's own "your event handlers go here" comment sits below the region. A stub above
        //   it would land somewhere the file itself says handlers do not go.
        var at = shape.Placement == FormHandlerPlacement.AboveInitRegion
            ? StartOfLineAt(codeText, init.StartOffset)
            : AfterLine(codeText, init.EndOffset);

        var text = codeText.Insert(at, stub);

        // The caret goes on the blank body line — where the user types, not on the signature they
        // did not write.
        var caret = new Recognizer.SourceIndex(text).PositionOf(at).Line + 1;

        return new FormHandlerPlan(HandlerOutcome.Created, eventName, handler, text, caret, null);
    }

    /// <summary>The leading whitespace of a line, so a stub matches the file it lands in.</summary>
    private static string IndentOf(Recognizer.SourceIndex index, int line)
    {
        var text = index.LineText(line);
        var i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
        {
            i++;
        }

        return i == 0 ? "    " : text.Substring(0, i);
    }

    private static int StartOfLineAt(string text, int offset)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, Math.Min(offset, text.Length) - 1));
        return start < 0 ? 0 : start + 1;
    }

    /// <summary>Offset just past the terminator of the line <paramref name="offset"/> sits on.</summary>
    private static int AfterLine(string text, int offset)
    {
        var next = text.IndexOf('\n', Math.Clamp(offset, 0, Math.Max(0, text.Length - 1)));
        return next < 0 ? text.Length : next + 1;
    }

    private static string Describe(FormTarget target) =>
        target == FormTarget.Web ? "the web" : "WinForms";

    /// <summary>
    /// ⛔ A refusal hands back the ORIGINAL text, never an empty string. The caller's natural shape
    /// is "write plan.CodeText back to the file", and a refusal that returned "" would silently
    /// truncate the user's code-behind to nothing — turning a polite no into data loss.
    /// </summary>
    private static FormHandlerPlan Refuse(string codeText, string why) =>
        new(HandlerOutcome.Refused, "", "", codeText, 0, why);
}
