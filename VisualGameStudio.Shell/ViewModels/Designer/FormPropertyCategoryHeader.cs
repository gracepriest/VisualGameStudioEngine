using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>A collapsible category header in the Categorized view (spec §3).</summary>
public partial class FormPropertyCategoryHeader : ObservableObject
{
    private readonly Action<FormPropertyCategoryHeader> _toggled;

    public FormPropertyCategoryHeader(string name, bool isExpanded, Action<FormPropertyCategoryHeader> toggled)
    {
        Name = name;
        _isExpanded = isExpanded; // the field, not the property: construction is not a toggle
        _toggled = toggled;
    }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private bool _isExpanded;

    /// <summary>VS's +/− box (a real minus sign, U+2212, so it is as wide as the plus).</summary>
    public string Glyph => IsExpanded ? "−" : "+";

    partial void OnIsExpandedChanged(bool value) => _toggled(this);
}
