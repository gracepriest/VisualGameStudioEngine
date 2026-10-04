namespace BasicLang.Forms;

/// <summary>
/// WHO owns a set of event binds (slice 5 D-10): a control, or — with <paramref name="Control"/> null — the FORM itself,
/// whose binds live on <see cref="FormDocument.Binds"/> and whose events are <see cref="FormControlCatalog.FormRoot"/>'s.
/// Every <see cref="FormHandlers"/> entry point and the retarget's crossing rule take one, so the Form is never a special
/// case beside the controls — a second code path is where the two would drift.
/// </summary>
public sealed record FormBindOwner(FormDocument Form, FormControl? Control = null)
{
    /// <summary>The owner's catalog row: the control's, or <see cref="FormControlCatalog.FormRoot"/> for the Form. Null for an unknown kind.</summary>
    public FormControlDef? Definition => Control == null ? FormControlCatalog.FormRoot : Control.Definition;

    /// <summary>The owner's binds — the control's, or the Form's own.</summary>
    public List<FormBind> Binds => Control?.Binds ?? Form.Binds;

    /// <summary>The handler-name prefix, <c>&lt;Prefix&gt;_&lt;Event&gt;</c>: the control's Id, or the form's name (VS's <c>Form1_Load</c>).</summary>
    public string Prefix => Control?.Id ?? Form.Name;

    /// <summary>How a diagnostic names the owner: <c>'btn'</c>, or <c>'form'</c>.</summary>
    public string Label => Control == null ? "'form'" : $"'{Control.Id}'";

    /// <summary>The owner's kind for a sentence: <c>Button</c>, or <c>Form</c>.</summary>
    public string KindName => Control?.Kind ?? FormControlCatalog.FormRoot.Kind;
}
