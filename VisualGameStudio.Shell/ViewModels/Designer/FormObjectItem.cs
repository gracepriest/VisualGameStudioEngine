using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// One entry of the grid's object selector (spec §3): a control, a tray component, or — with a null
/// <see cref="Control"/> — the form itself.
/// </summary>
public sealed class FormObjectItem
{
    public FormObjectItem(string name, string kind, FormControl? control)
    {
        Name = name;
        Kind = kind;
        Control = control;
    }

    public string Name { get; }

    public string Kind { get; }

    /// <summary>Null for the form: choosing it is <c>SelectInDesigner(null)</c>.</summary>
    public FormControl? Control { get; }

    /// <summary>VS's "Name  Kind".</summary>
    public string Display => $"{Name}  {Kind}";

    public override string ToString() => Display;
}
