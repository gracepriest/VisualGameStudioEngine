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

    /// <summary>
    /// Task: "edit an existing item's Text" (spec §6 follow-up, owner report 2026-09-23) — the
    /// item being RENAMED via VS's second-click / F2 gesture, or null in the ordinary Type Here
    /// CREATE flow that <see cref="Host"/>/<see cref="Text"/>/<see cref="IsActive"/> already back.
    ///
    /// <para>⛔⛔ ITS OWN <c>[ObservableProperty]</c> declaration, never folded onto another field's
    /// line — see the class summary's Blocker-1 warning, which applies again to every new field
    /// added here.</para>
    /// </summary>
    [ObservableProperty]
    private FormControl? _editTarget;

    /// <summary>
    /// ⛔ A rename whose target is WITHDRAWN is over. The canvas binds <c>EditingItem</c> TwoWay to
    /// <see cref="EditTarget"/> and clears it when the second press of a double-click arrives (the
    /// double-click opens the handler instead). Without this the editor would stay open with
    /// <see cref="Host"/> still the item and no target — which is the CREATE flow, so the next Enter
    /// would nest a NEW item under the one the user meant to rename.
    /// </summary>
    partial void OnEditTargetChanged(FormControl? oldValue, FormControl? newValue)
    {
        if (oldValue != null && newValue == null && IsActive && ReferenceEquals(Host, oldValue))
        {
            IsActive = false;
            Host = null;
            Text = "";
        }
    }
}
