using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class TreeViewPanel : UserControl
{
    public TreeViewPanel()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // When a TreeViewPanelViewModel is assigned, load root items and subscribe to expansion
        if (DataContext is TreeViewPanelViewModel vm)
        {
            SubscribeToItemExpansion(vm);

            if (vm.RootItems.Count == 0)
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await vm.LoadRootItemsAsync();
                    // Re-subscribe after loading since items are new
                    SubscribeToItemExpansion(vm);
                });
            }
        }
    }

    /// <summary>
    /// Subscribes to PropertyChanged on all current tree items to detect expansion
    /// and trigger lazy loading of children.
    /// </summary>
    private void SubscribeToItemExpansion(TreeViewPanelViewModel vm)
    {
        foreach (var item in vm.RootItems)
        {
            SubscribeItemRecursive(item, vm);
        }

        // Also subscribe to collection changes so new items get wired up
        vm.RootItems.CollectionChanged -= OnRootItemsCollectionChanged;
        vm.RootItems.CollectionChanged += OnRootItemsCollectionChanged;
    }

    private void OnRootItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not TreeViewPanelViewModel vm) return;

        if (e.NewItems != null)
        {
            foreach (TreeViewItemViewModel item in e.NewItems)
            {
                SubscribeItemRecursive(item, vm);
            }
        }
    }

    private void SubscribeItemRecursive(TreeViewItemViewModel item, TreeViewPanelViewModel panelVm)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        item.PropertyChanged += OnItemPropertyChanged;

        foreach (var child in item.Children)
        {
            SubscribeItemRecursive(child, panelVm);
        }

        item.Children.CollectionChanged -= OnChildrenCollectionChanged;
        item.Children.CollectionChanged += OnChildrenCollectionChanged;
    }

    private void OnChildrenCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not TreeViewPanelViewModel panelVm) return;

        if (e.NewItems != null)
        {
            foreach (TreeViewItemViewModel item in e.NewItems)
            {
                SubscribeItemRecursive(item, panelVm);
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeViewItemViewModel.IsExpanded) &&
            sender is TreeViewItemViewModel itemVm &&
            itemVm.IsExpanded &&
            !itemVm.ChildrenLoaded &&
            itemVm.HasChildren &&
            DataContext is TreeViewPanelViewModel panelVm)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await panelVm.LoadChildrenAsync(itemVm);
            });
        }
    }

    private void OnTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TreeViewPanelViewModel vm && vm.SelectedItem != null)
        {
            vm.ItemClickedCommand.Execute(vm.SelectedItem);
        }
    }
}
