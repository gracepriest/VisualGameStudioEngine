using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// The TOP-LEVEL members of a selection (property-grid slice 6 D-11, VS): the members none of whose ancestors is also
/// selected. Ctrl+click can select a Panel AND a Button inside it (the marquee cannot — it takes the container only);
/// Delete and the arrow-key nudge act on these alone. Deleting the Panel already removes the Button (deleting it first is
/// a second model path for one intent), and nudging both would move the Button twice — once with its container, once
/// itself. ⛔ The ONE helper both gestures use.
/// </summary>
public static class FormSelectionTopLevel
{
    /// <summary><paramref name="controls"/> without any member that lies inside another member, in the same order.</summary>
    public static IReadOnlyList<FormControl> Of(FormDocument document, IReadOnlyList<FormControl> controls)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(controls);

        bool Selected(FormControl c) => controls.Any(s => ReferenceEquals(s, c));

        return controls.Where(control =>
        {
            for (var parent = FormGeometryEdit.ParentOf(document, control);
                 parent != null;
                 parent = FormGeometryEdit.ParentOf(document, parent))
            {
                if (Selected(parent))
                {
                    return false;
                }
            }

            return true;
        }).ToList();
    }
}
