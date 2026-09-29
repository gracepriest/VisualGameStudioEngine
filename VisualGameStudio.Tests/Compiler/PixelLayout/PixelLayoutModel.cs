using System.Globalization;
using BasicLang.Forms;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// What the MODEL says every control's form-client rectangle is — the expectation both reference harnesses are held
/// to, at the design size or at any other client size.
/// <list type="bullet">
///   <item><description>Docked controls and strips: <see cref="FormDockLayout.ResolveSiblings"/> (the one resolver,
///   not a copy of it), re-docked in each container's CURRENT client area — WinForms re-docks at every size, and
///   the Task 12 harness measured the resolver's answer at 400×300 and 600×400.</description></item>
///   <item><description>Undocked positioned controls: their stored geometry, re-anchored against the growth of
///   their container's client area NOW over its DESIGNER client area (all siblings visible) by <see cref="AnchorAxis"/> — spec §4's table with the MEASURED WinForms
///   rounding (a centred axis floors the half pixel).</description></item>
///   <item><description>A container's children are offset by the container's rectangle: the model takes a
///   container's client area to be its bounds. ⚠ The bordered-Panel and GroupBox insets are the recorded gaps
///   (<c>WinFormsReferenceHarnessTests.BorderedContainers_…</c>).</description></item>
/// </list>
/// <para>⚠ Not modelled (never measured): a stretched size shrunk past zero — it comes out negative.</para>
/// </summary>
internal static class PixelLayoutModel
{
    /// <param name="clientSize">The form's client size to lay out at; null is the design size.</param>
    public static Dictionary<string, LayoutBox> Rects(
        FormDocument document, FormDockMode mode = FormDockMode.Runtime, (int Width, int Height)? clientSize = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        // ⛔ Each container's DESIGNER client area (every sibling visible) is the reference its children's anchor
        // distances were captured against: the region writer writes a docked container's Designer size, and WinForms
        // captures anchors when a child is added (measured, HiddenSiblingAnchor). Never the Runtime size: a hidden
        // docked sibling changes that, and the children then MOVE by the difference.
        var design = FormDockLayout.Resolve(document, FormDockMode.Designer);
        var boxes = new Dictionary<string, LayoutBox>();
        var now = clientSize ?? design.RootClientSize;
        Walk(document.Controls, 0, 0, design.RootClientSize, now, shown: true);
        return boxes;

        void Walk(
            IReadOnlyList<FormControl> controls, int originX, int originY,
            (int Width, int Height) designClient, (int Width, int Height) client, bool shown)
        {
            var docked = FormDockLayout.ResolveSiblings(controls, client.Width, client.Height, mode)
                .ToDictionary(d => d.Control, d => d.Bounds, ReferenceEqualityComparer.Instance);

            foreach (var control in controls)
            {
                if (control.Definition?.Place == FormPlace.Item)
                {
                    continue;
                }

                var visible = shown && !(mode == FormDockMode.Runtime && control.IsHidden);
                FormRect rect;
                if (docked.TryGetValue(control, out var bounds))
                {
                    rect = bounds;
                }
                else if (control.Geometry is PixelGeometry pixel)
                {
                    var edges = FormAnchor.Parse(pixel.Anchor, out _);
                    var across = AnchorAxis(pixel.X, pixel.Width, client.Width - designClient.Width,
                        edges.HasFlag(FormAnchorEdges.Left), edges.HasFlag(FormAnchorEdges.Right));
                    var down = AnchorAxis(pixel.Y, pixel.Height, client.Height - designClient.Height,
                        edges.HasFlag(FormAnchorEdges.Top), edges.HasFlag(FormAnchorEdges.Bottom));
                    rect = new FormRect(across.WinFormsOffset, down.WinFormsOffset, across.Size, down.Size);
                }
                else
                {
                    // A strip the Runtime resolver skipped as hidden: no place, compared by visibility only.
                    rect = default;
                }

                boxes[control.Id] = new LayoutBox(originX + rect.X, originY + rect.Y, rect.Width, rect.Height, visible);

                if (FormDockLayout.HasClientArea(control))
                {
                    var childDesign = design.TryGetClientSize(control, out var size) ? size : FormDockLayout.OwnSizeOf(control);
                    Walk(control.Children, originX + rect.X, originY + rect.Y, childDesign, (rect.Width, rect.Height), visible);
                }
            }
        }
    }

    /// <summary>
    /// ⛔ The anchor oracle, one axis (spec §4's table), with <paramref name="growth"/> = the container's client size
    /// now minus at design: near only → offset; far only → offset + growth; both → offset, size + growth; neither →
    /// CENTRED, offset + growth/2. <see cref="AnchorResult.SpecOffset"/> keeps the half pixel (what the page's
    /// <c>calc(50% …)</c> computes); <see cref="AnchorResult.WinFormsOffset"/> is what the real window does —
    /// MEASURED in Task 12: it floors, toward −∞ (+201 → +100, −59 → −30).
    /// </summary>
    public static AnchorResult AnchorAxis(int offset, int size, int growth, bool near, bool far) =>
        (near, far) switch
        {
            (true, true) => new(offset, offset, size + growth, false),
            (false, true) => new(offset + growth, offset + growth, size, false),
            (true, false) => new(offset, offset, size, false),
            _ => new(offset + growth / 2.0, offset + (int)Math.Floor(growth / 2.0), size, true)
        };

    /// <summary>One axis of <see cref="AnchorAxis"/>.</summary>
    internal readonly record struct AnchorResult(double SpecOffset, int WinFormsOffset, int Size, bool Centred);
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
