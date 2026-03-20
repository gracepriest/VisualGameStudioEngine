using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class CallHierarchyView : UserControl
{
    public CallHierarchyView()
    {
        InitializeComponent();
    }

    private void OnTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CallHierarchyViewModel vm && vm.SelectedItem != null)
        {
            vm.NavigateToItemCommand.Execute(vm.SelectedItem);
        }
    }
}

/// <summary>
/// Converts HierarchyCallableKind to a display icon character
/// </summary>
public class HierarchyKindToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is HierarchyCallableKind kind)
        {
            return kind switch
            {
                HierarchyCallableKind.Function => "f",
                HierarchyCallableKind.Method => "m",
                HierarchyCallableKind.Subroutine => "S",
                HierarchyCallableKind.Constructor => "c",
                HierarchyCallableKind.Property => "P",
                _ => "?"
            };
        }
        return "?";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
