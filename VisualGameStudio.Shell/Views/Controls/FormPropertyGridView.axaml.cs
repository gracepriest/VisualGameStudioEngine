using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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
        PropertyList.AddHandler(DoubleTappedEvent, OnListDoubleTapped);
        PropertyList.ContainerPrepared += OnContainerPrepared;
    }

    /// <summary>
    /// ⛔ A bring-into-view request that comes out of a POPUP is not the list's to honour (slice 4 Task 1 review).
    ///
    /// <para>Measured chain: opening a row's ComboBox runs <c>PopupOpened → TryFocusSelectedItem →
    /// ComboBoxItem.BringIntoView()</c>, and the request bubbles out of the popup (its logical parent is the combo) to this
    /// list's <c>ScrollContentPresenter.BringDescendantIntoView</c>. Where the popup is an <c>OverlayPopupHost</c> in the
    /// same window (the headless platform, and any overlay-popup host), the transform succeeds, the list scrolls to the
    /// popup's item (27→0, 209→0), the <c>VirtualizingStackPanel</c> recycles every container — and the combo that just
    /// opened is detached and closes. A Win32 popup is a separate root, so the IDE is likely unaffected; the guard costs
    /// nothing there. The popup's own ScrollViewer has already handled its item before the request reaches here.</para>
    ///
    /// <para>⛔ Handled on each ROW CONTAINER (<see cref="OnContainerPrepared"/>), not on the ListBox: the request is
    /// bubble-only, and the list's ScrollContentPresenter is INSIDE the ListBox's template — a handler on the ListBox runs
    /// after the presenter has already scrolled (measured: the guard there changed nothing). The container is the last
    /// element between the popup's combo and the presenter.</para>
    /// </summary>
    private void OnContainerRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is Visual target && IsInsidePopup(target))
        {
            e.Handled = true;
        }
    }

    private bool IsInsidePopup(Visual target) =>
        target.FindAncestorOfType<OverlayPopupHost>(includeSelf: true) != null ||
        target.FindAncestorOfType<PopupRoot>(includeSelf: true) != null ||
        !ReferenceEquals(TopLevel.GetTopLevel(target), TopLevel.GetTopLevel(PropertyList));

    /// <summary>
    /// Where the Font dialog's families come from (slice 4 D-2): the installed fonts — Avalonia's own list, Windows and
    /// Linux fontconfig alike. ⚠ A SEAM: the list differs per machine (pre-flight M6), so a test supplies its own.
    /// </summary>
    public Func<IEnumerable<string>> FontFamilies { get; set; } =
        () => FontManager.Current.SystemFonts.Select(family => family.Name);

    /// <summary>
    /// A Font row's <c>…</c>: opens the Font dialog over the IDE window, starting from the row's font (or, absent, the one
    /// its control inherits — <see cref="FormPropertyRow.EffectiveFont"/>), and commits ONE canonical value on OK. Cancel
    /// writes nothing.
    /// </summary>
    private async void OnFontEllipsisClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FormPropertyRow row } || !row.HasEllipsis ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var start = row.EffectiveFont ?? new BasicLang.Forms.FormFontValue(
            FormFontDialogViewModel.DefaultFamily, 9m, false, false, false, false);
        var dialog = new Dialogs.FormFontDialog
        {
            DataContext = new FormFontDialogViewModel(FontFamilies(), start, row.Target)
        };

        var result = await dialog.ShowDialog<string?>(owner);
        if (result != null)
        {
            row.ApplyFont(result);
        }
    }

    /// <summary>
    /// A Dock region was picked in the Dock pop-up: close it, as VS does, and put focus back on the row's drop-down
    /// button, so a keyboard user lands where they started (slice 4 review follow-up). Anchor does NOT close on a pick:
    /// it is a SET of edges, and VS keeps its pop-up open for the next one.
    ///
    /// <para>⚠ POSTED: the Click event runs BEFORE the button executes its Command (Avalonia raises Click first), and
    /// closing the pop-up synchronously detaches the region from its row — measured as a mutant: nothing was written. ⚠ The drop-down is found through the row's own
    /// container (<see cref="ItemsControl.ContainerFromItem"/>), never by walking up from the popup content, whose parent
    /// chain depends on the popup host.</para>
    /// </summary>
    private void OnDockRegionPicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FormPropertyRow row })
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var dropDown = PropertyList.ContainerFromItem(row)?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Name == "DockDropDown");
            // ⚠ No explicit Focus after: the flyout hands focus back to its target on Hide (measured — an explicit
            // dropDown.Focus here was an EQUIVALENT mutant; the keyboard test asserts focus lands on the button).
            if (dropDown?.Flyout is { } flyout)
            {
                flyout.Hide();
            }
        });
    }

    /// <summary>
    /// VS's double-click on a Bool row flips it (slice 4 D-3) — <see cref="FormPropertyRow.ToggleBool"/>, which decides
    /// whether the row is an editable Bool and does nothing otherwise.
    ///
    /// <para>⚠ One handler on the LIST, finding the row's container from the source: containers are recycled between
    /// rows and headers, so a handler added per preparation would pile up. ⛔ Not from inside the row's EDITOR: a
    /// double-click there belongs to the drop-down (it opens and picks), and flipping the value under it as well would be
    /// two edits for one gesture.</para>
    /// </summary>
    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source ||
            source.FindAncestorOfType<TypedValueEditor>(includeSelf: true) != null ||
            source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: FormPropertyRow row })
        {
            return;
        }

        // Handled only when the value flipped: a double-click on any other row stays available to whoever else listens.
        if (row.ToggleBool())
        {
            e.Handled = true;
        }
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
        // A COMPOSITE row (Font, Size, Location, Padding) expands with Right and collapses with Left, as in VS — only when
        // focus is on the row's CONTAINER itself: in its editor the arrows move the caret.
        if (e.KeyModifiers == KeyModifiers.None && e.Source is ListBoxItem { DataContext: FormPropertyRow { IsComposite: true } row } &&
            e.Key is Key.Left or Key.Right)
        {
            row.IsExpanded = e.Key == Key.Right;
            e.Handled = true;
            return;
        }

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
        // ⚠ Containers are RECYCLED and prepared again: remove first, so a container never carries the guard twice.
        e.Container.RemoveHandler(RequestBringIntoViewEvent, OnContainerRequestBringIntoView);
        e.Container.AddHandler(RequestBringIntoViewEvent, OnContainerRequestBringIntoView);

        var item = e.Index >= 0 && e.Index < PropertyList.ItemCount ? PropertyList.Items[e.Index] : null;
        e.Container.ContextMenu = item is FormPropertyRow row
            ? new ContextMenu { Items = { new MenuItem { Header = "Reset", Command = row.ResetCommand } } }
            : null;
    }
}
