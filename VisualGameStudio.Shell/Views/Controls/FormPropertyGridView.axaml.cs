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
        PickFile = DefaultPickFileAsync;
        ChooseImport = DefaultChooseImportAsync;

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
    ///
    /// <para>⛔ <c>async void</c> (an event handler): an exception escaping it would reach the dispatcher and take the IDE
    /// down, so it is caught and logged — the font is simply not changed.</para>
    /// </summary>
    private async void OnFontEllipsisClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FormPropertyRow row } || !row.HasEllipsis)
        {
            return;
        }

        try
        {
            var start = row.EffectiveFont ?? new BasicLang.Forms.FormFontValue(
                FormFontDialogViewModel.DefaultFamily, 9m, false, false, false, false);
            var dialog = new Dialogs.FormFontDialog
            {
                DataContext = new FormFontDialogViewModel(FontFamilies(), start, row.Target)
            };

            var result = await ShowDialogAsync(dialog, TopLevel.GetTopLevel(this),
                () => (dialog.DataContext as FormFontDialogViewModel)?.Result);
            if (result != null)
            {
                row.ApplyFont(result);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"The Font dialog failed; {row.Name} is unchanged. {ex}");
        }
    }

    /// <summary>
    /// Where the image picker's file comes from (slice 4 Task 10): the platform's open-file picker, filtered to the row's
    /// kind. ⚠ A SEAM — the headless platform has no storage provider — so a test supplies the picked path.
    /// </summary>
    public Func<FormPropertyRow, Task<string?>> PickFile { get; set; }

    /// <summary>
    /// What to do with a picked file OUTSIDE the project (D-5e): copy it into Resources (recommended), keep the absolute
    /// path (only offered on WinForms — the argument says whether), or cancel. ⚠ A SEAM, for the same reason.
    /// </summary>
    public Func<bool, Task<FormAssetImportChoice>> ChooseImport { get; set; }

    /// <summary>
    /// An Image/Icon row's <c>…</c> (slice 4 Task 10, D-5e): pick a file; inside the project it is stored relative to it;
    /// outside, the user chooses (copy into Resources / keep the path on WinForms / cancel); the value goes through the row.
    /// Caught and logged like the dialogs' handlers.
    /// </summary>
    private async void OnAssetPickClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FormPropertyRow row } || !row.HasAssetPicker ||
            DataContext is not FormPropertyGridViewModel { DocumentPath: { } document })
        {
            return;
        }

        try
        {
            if (await PickFile(row) is not { } picked)
            {
                return;
            }

            var choice = FormAssetImportChoice.Cancel;
            if (FormAssetImport.RelativeInsideProject(picked, document) == null)
            {
                choice = await ChooseImport(row.Target == BasicLang.Forms.FormTarget.WinForms);
            }

            if (FormAssetImport.Import(picked, document, row.Target, _ => choice) is { } value)
            {
                row.ApplyAsset(value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"The file picker failed; {row.Name} is unchanged. {ex}");
        }
    }

    /// <summary>The default <see cref="PickFile"/>: the top level's storage provider, images (or icons) only.</summary>
    private async Task<string?> DefaultPickFileAsync(FormPropertyRow row)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }

        var patterns = row.TypeName == nameof(BasicLang.Forms.FormPropertyType.Icon)
            ? row.Target == BasicLang.Forms.FormTarget.WinForms
                ? new[] { "*.ico" }
                : new[] { "*.ico", "*.png", "*.svg", "*.gif" }
            : row.Target == BasicLang.Forms.FormTarget.WinForms
                ? new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.ico" }
                : new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.ico", "*.svg", "*.webp" };
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = $"Choose the {row.Name}",
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType(row.Name) { Patterns = patterns } }
        });
        return files.Count == 1 ? Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) : null;
    }

    /// <summary>
    /// The default <see cref="ChooseImport"/>: a small modal question over the IDE window — Copy into Resources (default),
    /// Use this path (WinForms only), Cancel.
    /// </summary>
    private async Task<FormAssetImportChoice> DefaultChooseImportAsync(bool offerKeepPath)
    {
        var choice = FormAssetImportChoice.Cancel;
        var dialog = new Window
        {
            Title = "Copy the file into the project?",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        Button Make(string text, FormAssetImportChoice result)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 3) };
            button.Click += (_, _) =>
            {
                choice = result;
                dialog.Close();
            };
            return button;
        }

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        buttons.Children.Add(Make("Copy into Resources", FormAssetImportChoice.CopyIntoProject));
        if (offerKeepPath)
        {
            buttons.Children.Add(Make("Use this path", FormAssetImportChoice.KeepAbsolutePath));
        }

        buttons.Children.Add(Make("Cancel", FormAssetImportChoice.Cancel));
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = offerKeepPath
                        ? "The file is outside the project. Copy it into the project's Resources folder (it then ships beside " +
                          "the program), or use its path as it is (the program will look for it there on the machine it runs on)?"
                        : "The file is outside the project, and a web page cannot reach your disk. Copy it into the project's " +
                          "Resources folder (it is then copied beside the page)?"
                },
                buttons
            }
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }

        return choice;
    }

    /// <summary>
    /// An item collection's <c>…</c> (slice 4 Task 7): VS's String Collection Editor over the IDE window; OK commits the
    /// list through the row (an empty list resets), Cancel and the close box write nothing. Caught and logged like the
    /// Font dialog's handler.
    /// </summary>
    private async void OnItemsEllipsisClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FormPropertyRow row } || !row.IsCollectionEditor)
        {
            return;
        }

        try
        {
            var dialog = new Dialogs.FormItemsDialog { DataContext = new FormItemsDialogViewModel(row.RawValue) };
            var result = await ShowDialogAsync(dialog, TopLevel.GetTopLevel(this),
                () => (dialog.DataContext as FormItemsDialogViewModel)?.Result);
            if (result != null)
            {
                row.ApplyItems(result);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"The String Collection Editor failed; {row.Name} is unchanged. {ex}");
        }
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> modally over its owner window. ⚠ A top level that is NOT a <see cref="Window"/> (an
    /// embedded host) cannot own a modal dialog: the dialog is then shown on its own and awaited until it closes, with the
    /// same result rule — <paramref name="resultOnClose"/> (the dialog's OK value, or null for any other close).
    /// </summary>
    /// <remarks>Public for the modeless-path test (the Shell grants the tests no internals access).</remarks>
    public static Task<string?> ShowDialogAsync(Window dialog, TopLevel? top, Func<string?> resultOnClose)
    {
        if (top is Window owner)
        {
            return dialog.ShowDialog<string?>(owner);
        }

        var closed = new TaskCompletionSource<string?>();
        dialog.Closed += (_, _) => closed.TrySetResult(resultOnClose());
        dialog.Show();
        return closed.Task;
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
            // ⚠ A STALE row is harmless: if the grid rebuilt (or recycled the container) between the click and this post,
            // ContainerFromItem returns null for the old row and nothing is hidden — the pop-up went with its container.
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
        // Slice 5 D-5: a double-click on an Events-tab row (its name, or its value cell) creates-or-navigates its handler —
        // the host decides which (bound → navigate; empty → write <Id>_<Event>). Not inside an OPEN drop-down: a
        // double-click there picks an item.
        if (e.Source is Visual eventSource &&
            eventSource.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: FormEventRow eventRow } &&
            eventSource.FindAncestorOfType<ComboBoxItem>(includeSelf: true) == null)
        {
            eventRow.RequestHandler();
            e.Handled = true;
            return;
        }

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
    /// An Events-tab handler drop-down is opening (D-5 freshness): ask the host for the code-behind as it is NOW — the open
    /// tab's unsaved text first, then the disk — so the list offers a Sub typed a moment ago.
    /// </summary>
    private void OnHandlerDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is FormPropertyGridViewModel grid)
        {
            grid.RequestCodeBehindRefresh();
        }
    }

    /// <summary>
    /// A handler was PICKED from the drop-down: bind it (the row's commit rules). ⚠ Only while the drop-down is open — the
    /// combo also re-selects when its items or text change underneath it (a refresh), which is not a pick.
    /// </summary>
    private void OnHandlerPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: FormEventRow row, IsDropDownOpen: true, SelectedItem: string picked })
        {
            row.Commit(picked);
        }
    }

    /// <summary>Leaving the handler cell commits what was typed, as Enter does (VS). The same text as bound is a no-op.</summary>
    private void OnHandlerLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { DataContext: FormEventRow row, IsDropDownOpen: false } combo &&
            !combo.IsKeyboardFocusWithin)
        {
            row.Commit(combo.Text);
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
        // Slice 5: Enter in an Events-tab row's handler combo COMMITS what was typed (a fitting Sub binds, a new name asks
        // the host for the stub, an empty cell unbinds). On the TUNNEL, before the editable combo takes Enter for itself.
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is Visual typedIn &&
            typedIn.FindAncestorOfType<ComboBox>(includeSelf: true) is { DataContext: FormEventRow eventRow } combo)
        {
            eventRow.Commit(combo.Text);
            combo.IsDropDownOpen = false;
            e.Handled = true;
            return;
        }

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
