using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>One toolbox entry: a control kind the current document can hold.</summary>
/// <param name="Kind">The catalog kind, which is also the document element name.</param>
/// <param name="Description">What it maps to on this target — the thing a user actually wonders.</param>
public sealed record FormToolboxItem(string Kind, string Description);

/// <summary>
/// The designer's toolbox — the control kinds available for the open document's target.
///
/// <para>⛔ Driven from <see cref="FormControlCatalog.For"/>, never a hand-written list. A hand
/// list is how a catalog row ships with no way to place it, and how a kind that does not exist on
/// the target gets offered anyway: <c>SupportsTarget</c> is the only thing that knows a control is
/// web-only or WinForms-only, and it is checked HERE rather than after the user has dragged one
/// onto the canvas.</para>
/// </summary>
public partial class FormToolboxViewModel : ObservableObject
{
    [ObservableProperty]
    private FormTarget _target = FormTarget.Web;

    public ObservableCollection<FormToolboxItem> Items { get; } = new();

    public FormToolboxViewModel() => Rebuild();

    partial void OnTargetChanged(FormTarget value) => Rebuild();

    private void Rebuild()
    {
        Items.Clear();

        foreach (var control in FormControlCatalog.For(Target))
        {
            // What this kind IS on the target the user is looking at — the WinForms type name or
            // the HTML tag. A toolbox that only says "TextBox" leaves them guessing which of the
            // three <input> kinds they are about to place.
            var description = Target == FormTarget.WinForms
                ? control.WinFormsType ?? control.Kind
                : control.HtmlInputType != null
                    ? $"<{control.HtmlTag} type=\"{control.HtmlInputType}\">"
                    : $"<{control.HtmlTag}>";

            Items.Add(new FormToolboxItem(control.Kind, description));
        }
    }
}
