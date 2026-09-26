using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Views.Controls;

/// <summary>
/// The designer's Properties window (spec §3), extracted from CodeEditorDocumentView so it can grow the
/// selector, toolbar, search and (later) the editors and Events tab without the document view carrying
/// them. Bound to the same <c>FormPropertyGridViewModel</c> it always was.
///
/// <para>⛔ View-only code-behind. It never touches the SELECTION: that goes through the view model's
/// <c>SelectionRequested</c>, so the document view model's one selection store stays the only writer.
/// What it does own: the header keys, and each row container's Reset menu.</para>
/// </summary>
public partial class FormPropertyGridView : UserControl
{
    public FormPropertyGridView()
    {
        InitializeComponent();

        // ⚠ TUNNEL: the list's own KeyDown handling runs on the bubble, and a ToggleButton would take
        // Enter/Space for itself. The headers are not focusable, so a header key arrives from its CONTAINER.
        PropertyList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        PropertyList.AddHandler(KeyUpEvent, OnListKeyUp, RoutingStrategies.Tunnel);
        PropertyList.ContainerPrepared += OnContainerPrepared;
    }

    /// <summary>
    /// Shift+F10 opens the context menu of whatever has focus — the Windows gesture, beside the Menu key.
    ///
    /// <para>⚠ Measured headless: the platform's <c>OpenContextMenu</c> gestures are the Menu key ALONE, so
    /// Avalonia never raised a context request for Shift+F10. Raised here only when the platform does NOT
    /// map it itself, so a platform that does cannot open the menu twice.</para>
    ///
    /// <para>⛔ Raised on the FOCUSED element (the event source) and left to BUBBLE, exactly as the Menu
    /// key's request is: focus in a row's editor opens the editor's own cut/copy/paste menu, and the row
    /// container's Reset menu opens only when nothing below it takes the request. Handled only if the
    /// request was.</para>
    /// </summary>
    private void OnListKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F10 || e.KeyModifiers != KeyModifiers.Shift)
        {
            return;
        }

        var mapped = Application.Current?.PlatformSettings?.HotkeyConfiguration.OpenContextMenu;
        if (mapped?.Any(g => g.Matches(e)) == true)
        {
            return;
        }

        if (e.Source is Control focused)
        {
            var request = new ContextRequestedEventArgs();
            focused.RaiseEvent(request);
            e.Handled = request.Handled;
        }
    }

    /// <summary>
    /// A category collapses from the keyboard, as in VS: Left collapses, Right expands, Enter/Space toggle.
    ///
    /// <para>⛔ Only when focus is ON a header's own container. The list's SelectedItem alone is not enough:
    /// a header can stay selected while the user types into a row's editor (a TextBox takes the press
    /// without selecting its row), and Space there must type a space, not fold a category.</para>
    /// </summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None ||
            e.Source is not Visual source ||
            source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: FormPropertyCategoryHeader header })
        {
            return;
        }

        bool? expand = e.Key switch
        {
            Key.Left => false,
            Key.Right => true,
            Key.Enter or Key.Space => !header.IsExpanded,
            _ => null
        };

        if (expand is { } value)
        {
            header.IsExpanded = value;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Gives each ROW's container its context menu — Reset (spec §2.7): REMOVE the property from the
    /// document, disabled through CanReset on an absent, frozen or intrinsic row. A header gets none.
    ///
    /// <para>⚠ On the CONTAINER, not inside the row template: Shift+F10 and the Menu key raise the context
    /// request on the FOCUSED element (the ListBoxItem) and it bubbles UP, so a menu on a Border inside the
    /// item never heard it — Reset was mouse-only. Containers are RECYCLED between rows and headers, so
    /// every preparation sets it, including back to null.</para>
    ///
    /// <para>The command is bound by reference, not by a {Binding} path: the compiler checks it, where a
    /// reflection path would bind to nothing, silently, if it were wrong.</para>
    /// </summary>
    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var item = e.Index >= 0 && e.Index < PropertyList.ItemCount ? PropertyList.Items[e.Index] : null;
        e.Container.ContextMenu = item is FormPropertyRow row
            ? new ContextMenu { Items = { new MenuItem { Header = "Reset", Command = row.ResetCommand } } }
            : null;
    }
}
