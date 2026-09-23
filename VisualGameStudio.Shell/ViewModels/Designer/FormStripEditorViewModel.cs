using BasicLang.Forms;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// Task 23 (commit 24d) — "Type Here" (spec §6). Backs the overlay editor
/// (<c>FormTypeHereEditor</c>, Task 22): which host is being typed into, and what has been typed so
/// far. The document view model (<c>CodeEditorDocumentViewModel</c>) owns the transitions —
/// <c>BeginTypeHere</c>, <c>CommitTypeHere</c>, <c>CancelTypeHere</c> and the leave-rule; this is
/// only the state the view binds to.
///
/// <para>⛔⛔ ONE <c>[ObservableProperty]</c> PER FIELD DECLARATION (24d pre-flight BLOCKER 1). The
/// attribute binds the single declaration after it: <c>[ObservableProperty] bool _isActive;
/// FormControl? _host; string _text;</c> generates <c>IsActive</c> alone, and the view's bindings
/// to <c>Host</c> and <c>Text</c> then read once at attach and never update — the overlay becomes
/// a 0×0 focused TextBox. <c>FormStripEditorTests.StripEditor_RaisesPropertyChanged_…</c> is the
/// only test that can see it.</para>
/// </summary>
public partial class FormStripEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private FormControl? _host;

    [ObservableProperty]
    private string _text = "";
}
