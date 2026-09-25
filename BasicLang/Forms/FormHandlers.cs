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
public sealed record FormHandlerPlan(
    HandlerOutcome Outcome,
    string EventName,
    string Handler,
    string CodeText,
    int CaretLine,
    string? Refusal);

/// <summary>
/// Task 22 — the double-click gesture: create the control's default handler if it is absent, navigate
/// to it if it is present.
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

    /// <summary>
    /// <c>btnLogin_Click</c> — VS's shape, kept on BOTH targets even though the DOM's event is
    /// lowercase. <c>btnLogin_click</c> would be the literal event name and would read, in a file
    /// full of PascalCase Subs, like a mistake.
    /// </summary>
    public static string NameFor(string controlId, string eventName) =>
        $"{controlId}_{Pascal(eventName)}";

    private static string Pascal(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    /// <summary>
    /// Wires <paramref name="handler"/> to <paramref name="eventName"/> unless that event is already
    /// bound. Returns whether it added one.
    ///
    /// <para>⛔ A stub nothing wires is dead code — the region emits the
    /// <c>addEventListener</c>/<c>AddHandler</c> only for a bind that exists, so creating the Sub
    /// without this leaves the user with a handler that never fires and nothing to see why.</para>
    /// </summary>
    public static bool EnsureBind(FormControl control, string eventName, string handler)
    {
        if (control.Binds.Any(b => string.Equals(b.Event, eventName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        control.Binds.Add(new FormBind { Event = eventName, Handler = handler });
        return true;
    }

    /// <summary>
    /// Plans the double-click: which handler, and the code-behind with it present.
    ///
    /// <para>⚠ An existing bind wins over the computed name. The user may have wired
    /// <c>SignIn</c> by hand; opening <c>btnLogin_Click</c> instead would put them in a Sub the form
    /// never calls, and they would rightly conclude the designer was broken.</para>
    /// </summary>
    public static FormHandlerPlan PlanDefault(FormDocument form, FormControl control, string codeText)
    {
        var eventName = DefaultEvent(control.Kind, form.Target);
        if (string.IsNullOrEmpty(eventName))
        {
            return Refuse(codeText,
                $"'{control.Kind}' has no default event for {Describe(form.Target)}, so there is " +
                "nothing for a double-click to open. Add one to the control catalog.");
        }

        // An existing wiring names the handler; otherwise the convention does.
        var bind = control.Binds.FirstOrDefault(
            b => string.Equals(b.Event, eventName, StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrEmpty(b.Handler));
        var handler = bind?.Handler ?? NameFor(control.Id, eventName);

        var index = new Recognizer.SourceIndex(codeText);

        var existing = FindDeclarationLine(index, handler);
        if (existing > 0)
        {
            // Already there — land in the body, one line below the signature.
            return new FormHandlerPlan(
                HandlerOutcome.Navigated, eventName, handler, codeText,
                Math.Min(existing + 1, Math.Max(1, index.LineCount)), null);
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

        return Insert(form, codeText, index, init, eventName, handler, control.Definition);
    }

    /// <param name="definition">
    /// The control's catalog row, which owns the stub's SIGNATURE (Task 25): the <c>e</c> type of
    /// a WinForms handler (<c>DoWorkEventArgs</c> for a BackgroundWorker — the <c>EventArgs</c>
    /// stub compiles by contravariance but cannot reach <c>e.Argument</c>), and whether a web
    /// callback takes the event at all (a Timer's does not: <c>Window.setInterval</c> takes an
    /// <c>Action</c> and refuses <c>Action(Of DomEvent)</c>, measured).
    /// </param>
    private static FormHandlerPlan Insert(
        FormDocument form,
        string codeText,
        Recognizer.SourceIndex index,
        FormRegion init,
        string eventName,
        string handler,
        FormControlDef? definition)
    {
        // ⚠ The file's own terminator, not the platform's. A stub inserted with the wrong one leaves
        // a file with both, which reads as a whole-file diff the next time anything touches it.
        var newline = codeText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = IndentOf(index, init.Line);

        var signature = form.Target == FormTarget.Web
            ? definition?.WebHandlerTakesEvent == false
                ? $"{indent}Private Sub {handler}()"
                // ⛔ addEventListener will not accept anything but Action(Of DomEvent).
                : $"{indent}Private Sub {handler}(e As DomEvent)"
            : $"{indent}Private Sub {handler}(sender As Object, e As {definition?.WinFormsEventArgs ?? "EventArgs"})";

        var stub = new StringBuilder()
            .Append(signature).Append(newline)
            .Append(indent).Append(indent).Append(newline)
            .Append(indent).Append("End Sub").Append(newline)
            .Append(newline)
            .ToString();

        // ⛔⛔ The two targets insert on OPPOSITE sides of the init region, and both are measured.
        //
        // Web: ABOVE it, because D8's ordering rule bites — a handler below loses its parameter
        //   types and addEventListener rejects the erased Action(Of Object).
        // WinForms: BELOW it, because the rule does NOT bite there (the event is an unresolvable
        //   .NET member typed Object, so there is no declared delegate to mismatch) and because the
        //   scaffold's own "your event handlers go here" comment sits below the region. A stub above
        //   it would land somewhere the file itself says handlers do not go.
        var at = form.Target == FormTarget.Web
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

    /// <summary>
    /// The 1-based line declaring <c>Sub &lt;handler&gt;</c>, or 0.
    ///
    /// <para>⚠ A text scan, deliberately: this runs on a file the user is midway through editing, and
    /// refusing to find a handler in a file that does not parse would make the gesture fail exactly
    /// when someone is working.</para>
    /// </summary>
    private static int FindDeclarationLine(Recognizer.SourceIndex index, string handler)
    {
        for (var line = 1; line <= index.LineCount; line++)
        {
            var text = index.LineText(line).TrimStart();
            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                continue;
            }

            var sub = text.IndexOf("Sub ", StringComparison.OrdinalIgnoreCase);
            if (sub < 0)
            {
                continue;
            }

            var after = text.Substring(sub + 4).TrimStart();
            if (after.StartsWith(handler, StringComparison.Ordinal) &&
                (after.Length == handler.Length ||
                 !char.IsLetterOrDigit(after[handler.Length]) && after[handler.Length] != '_'))
            {
                return line;
            }
        }

        return 0;
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
