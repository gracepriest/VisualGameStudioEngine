using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VisualGameStudio.Shell.Views.Controls;

/// <summary>
/// The four typed editors — bool, int, choice, text — shared by the Settings dialog and the form
/// designer's property grid. Binds to whatever <c>ITypedValueRow</c> is its DataContext.
/// </summary>
public partial class TypedValueEditor : UserControl
{
    public TypedValueEditor() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
