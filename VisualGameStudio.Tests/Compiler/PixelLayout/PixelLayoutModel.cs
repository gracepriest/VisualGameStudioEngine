using System.Globalization;
using BasicLang.Forms;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// What the MODEL says every control's form-client rectangle is at the document's design size — the expectation
/// both reference harnesses are held to. Undocked positioned controls: their stored geometry; docked controls and
/// strips: <see cref="FormDockLayout"/> (the one resolver, not a copy of it). A container's children are offset by
/// the container's rectangle: the model takes a container's client area to be its bounds (the bordered-Panel and
/// GroupBox insets are the recorded gaps the WinForms harness measures).
/// </summary>
internal static class PixelLayoutModel
{
    public static Dictionary<string, LayoutBox> Rects(FormDocument document, FormDockMode mode = FormDockMode.Runtime)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layout = FormDockLayout.Resolve(document, mode);
        var boxes = new Dictionary<string, LayoutBox>();
        Walk(document.Controls, 0, 0, shown: true);
        return boxes;

        void Walk(IReadOnlyList<FormControl> controls, int originX, int originY, bool shown)
        {
            foreach (var control in controls)
            {
                if (control.Definition?.Place == FormPlace.Item)
                {
                    continue;
                }

                var visible = shown && !(mode == FormDockMode.Runtime && control.IsHidden);
                FormRect rect;
                if (layout.TryGet(control, out var docked))
                {
                    rect = docked.Bounds;
                }
                else if (control.Geometry is PixelGeometry pixel)
                {
                    rect = new FormRect(pixel.X, pixel.Y, pixel.Width, pixel.Height);
                }
                else
                {
                    // A strip the Runtime resolver skipped as hidden: no place, compared by visibility only.
                    rect = default;
                }

                boxes[control.Id] = new LayoutBox(originX + rect.X, originY + rect.Y, rect.Width, rect.Height, visible);

                if (FormDockLayout.HasClientArea(control))
                {
                    Walk(control.Children, originX + rect.X, originY + rect.Y, visible);
                }
            }
        }
    }
}

/// <summary>⛔ The one comparison of two layouts — the WinForms harness against the model, Task 13's Edge page against both.</summary>
internal static class LayoutComparison
{
    /// <summary>
    /// Every difference between <paramref name="expected"/> and <paramref name="actual"/>, empty when they agree: a
    /// control missing from either side, a visibility mismatch, and — for a control visible on both — each of
    /// X/Y/Width/Height further apart than <paramref name="tolerance"/> pixels. Ordered by id.
    /// </summary>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, LayoutBox> expected, IReadOnlyDictionary<string, LayoutBox> actual, double tolerance)
    {
        var differences = new List<string>();
        foreach (var id in expected.Keys.Union(actual.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(id, out var e))
            {
                differences.Add($"'{id}': measured at {Show(actual[id])} but not expected at all");
                continue;
            }

            if (!actual.TryGetValue(id, out var a))
            {
                differences.Add($"'{id}': expected at {Show(e)} but not measured");
                continue;
            }

            if (e.Visible != a.Visible)
            {
                differences.Add($"'{id}': expected visible={e.Visible}, measured visible={a.Visible}");
                continue;
            }

            if (!e.Visible)
            {
                continue;
            }

            Compare("X", e.X, a.X);
            Compare("Y", e.Y, a.Y);
            Compare("Width", e.Width, a.Width);
            Compare("Height", e.Height, a.Height);

            void Compare(string what, double want, double got)
            {
                if (Math.Abs(want - got) > tolerance)
                {
                    differences.Add($"'{id}' {what}: expected {Num(want)}, measured {Num(got)} (tolerance {Num(tolerance)}px)");
                }
            }
        }

        return differences;
    }

    public static string Show(LayoutBox box) =>
        $"({Num(box.X)}, {Num(box.Y)}, {Num(box.Width)}x{Num(box.Height)}{(box.Visible ? "" : ", hidden")})";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
