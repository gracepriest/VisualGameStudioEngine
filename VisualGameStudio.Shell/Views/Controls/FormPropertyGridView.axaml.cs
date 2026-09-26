using Avalonia.Controls;

namespace VisualGameStudio.Shell.Views.Controls;

/// <summary>
/// The designer's Properties window (spec §3), extracted from CodeEditorDocumentView so it can grow the
/// selector, toolbar, search and (later) the editors and Events tab without the document view carrying
/// them. Bound to the same <c>FormPropertyGridViewModel</c> it always was.
///
/// <para>⚠ No code-behind logic: selection goes through the view model's <c>SelectionRequested</c>, so
/// the document view model's one selection store stays the only writer.</para>
/// </summary>
public partial class FormPropertyGridView : UserControl
{
    public FormPropertyGridView() => InitializeComponent();
}
