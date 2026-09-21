using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The canvas draws each control kind AS ITSELF — proved in pixels, through the real control.
///
/// <para>⛔ The defect these pin: <c>DrawControl</c> never read <c>control.Kind</c>. A Button, a
/// TextBox, a CheckBox and a Panel were one identical grey rectangle, and the only thing telling
/// them apart was a label that defaults to the id. A populated form was unreadable, and the owner
/// reported it as "the items are not right, all just look like textboxes". No test could see it:
/// every assertion about the canvas was about geometry or hit-testing, never about what was
/// DRAWN.</para>
///
/// <para>⛔ A string assertion cannot catch this class of bug and neither can a mock. The question
/// is "do two kinds paint differently", and the only honest answer is two different images. These
/// render the real <see cref="FormCanvasControl"/> and hash the frames.</para>
/// </summary>
[TestFixture]
public class FormCanvasRenderTests
{
    private const int FormWidth = 200;
    private const int FormHeight = 80;

    /// <summary>
    /// ⛔ The SAME id on every control, deliberately. The label is drawn from
    /// <c>Text</c> ?? <c>Id</c>, so ids like "Button1"/"TextBox1" would make every frame differ by
    /// its text alone and the test would pass with the defect fully present.
    /// </summary>
    private const string SharedId = "X";

    private static FormDocument DocumentWith(string kind)
    {
        var document = new FormDocument
        {
            Target = FormTarget.WinForms,
            Name = "T",
            Width = FormWidth,
            Height = FormHeight
        };

        // Task 24, commit 24b: the shape (Tray/Docked/Item/Positioned) is now the ONE answer in
        // FormCatalogShapes.Canonical — the fixture no longer hand-builds a FormControl and chooses
        // its list by IsComponent alone, which is exactly the thing a fifth shape would have slipped
        // past. hostId reuses SharedId: it is only consulted for a FormPlace.Item kind, and no such
        // kind exists yet (commit 24c adds the first).
        FormCatalogShapes.Canonical(document, FormControlCatalog.Find(kind)!, SharedId, hostId: SharedId,
            geometry: new PixelGeometry { X = 20, Y = 20, Width = 140, Height = 40 });

        return document;
    }

    private static string RenderHash(FormDocument document)
    {
        var canvas = new FormCanvasControl { Document = document };
        var window = new Window { Width = 260, Height = 140, Content = canvas };
        window.Show();

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "No rendered frame. Headless drawing is on — Skia is required to render pixels.");

        using var stream = new MemoryStream();
        frame.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    [AvaloniaTest]
    public void EveryControlKindRendersDistinctly()
    {
        // ⚠ Components are excluded on PURPOSE, not for convenience: a component has no position, is
        // never in <Controls> (the reader refuses one there, BL8020) and is never drawn — the tray
        // shows it. AComponent_IsNeverLaidOut below is the pin for that claim.
        // ⚠ Task 24, commit 24b: an Item is excluded too — it has no geometry and no document of its
        // own, so it leaves this document-level hash. FormSchematicPinTests pins every item schematic
        // pairwise through the DrawSchematic seam instead (spec §7).
        var winFormsKinds = FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.WinForms) && d.Place != FormPlace.Tray && d.Place != FormPlace.Item)
            .ToList();

        Assert.That(winFormsKinds, Is.Not.Empty, "the catalog has no WinForms kinds to draw");

        var byHash = new Dictionary<string, List<string>>();
        foreach (var definition in winFormsKinds)
        {
            var hash = RenderHash(DocumentWith(definition.Kind));
            if (!byHash.TryGetValue(hash, out var kinds))
            {
                byHash[hash] = kinds = new List<string>();
            }

            kinds.Add(definition.Kind);
        }

        var collisions = byHash.Values.Where(k => k.Count > 1).ToList();
        Assert.That(collisions, Is.Empty,
            "these kinds paint IDENTICAL pixels, so the canvas cannot tell them apart: " +
            string.Join(" | ", collisions.Select(k => string.Join(", ", k))));
    }

    /// <summary>
    /// Task 25: a component has no place on the canvas, so the layout must never yield bounds for
    /// one — on either target. It lives in the tray.
    /// </summary>
    [Test]
    public void AComponent_IsNeverLaidOut()
    {
        var document = DocumentWith("Button");
        document.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });

        var web = new FormDocument
        {
            Target = FormTarget.Web, Name = "T", Layout = new FormLayout { Kind = FormLayoutKind.Grid }
        };
        web.Controls.Add(new FormControl { Kind = "Button", Id = "btn", Geometry = new GridGeometry() });
        web.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });

        Assert.Multiple(() =>
        {
            Assert.That(FormCanvasTransform.Layout(document).Select(l => l.Control!.Id), Is.EqualTo(new[] { SharedId }));
            Assert.That(FormCanvasTransform.Layout(web).Select(l => l.Control!.Id), Is.EqualTo(new[] { "btn" }));
        });
    }

    /// <summary>
    /// The catalog owns the shape, so a kind cannot be added without choosing one — and the
    /// choice has to actually reach the canvas. Without this, a new row silently inherits
    /// <see cref="FormSchematic.Input"/> and draws as a plain box, which is the defect above
    /// coming back one control at a time.
    /// </summary>
    [Test]
    public void EverySchematicTheCatalogUsesIsDrawnDifferentlyFromTheFallback()
    {
        var used = FormControlCatalog.All.Select(d => d.Schematic).Distinct().ToList();

        Assert.That(used.Count, Is.GreaterThan(1),
            "every catalog row shares one schematic — the canvas is back to one grey box for all");

        Assert.That(used, Does.Contain(FormSchematic.Text),
            "a Label with a box around it is the tell that shapes stopped being per-kind");
    }

    /// <summary>
    /// ⛔ Setting BackColor must CHANGE THE PICTURE. The canvas drew classic chrome and ignored the
    /// colours the property grid had just written, so setting BackColor changed the document, the
    /// generated code and the running program — and nothing on screen. The owner reported it as
    /// "setting the backcolor doesn't work", which is exactly what it looked like.
    /// </summary>
    [AvaloniaTest]
    public void AControlsOwnBackColourChangesWhatIsDrawn()
    {
        var plain = DocumentWith("Button");

        var coloured = DocumentWith("Button");
        coloured.Controls[0].Properties["BackColor"] = "#FF0000";

        Assert.That(RenderHash(coloured), Is.Not.EqualTo(RenderHash(plain)),
            "a Button with BackColor set rendered identically to one without — the canvas is " +
            "ignoring the control's own colours again");
    }

    /// <summary>
    /// ⚠ An unparseable colour must leave the control looking normal. D9 already freezes such a
    /// value in the property grid with its reason; the canvas's job is to not make it worse by
    /// falling back to black or throwing.
    /// </summary>
    [AvaloniaTest]
    public void AnUnparseableBackColourIsIgnored_NotDrawnAsBlack()
    {
        var plain = DocumentWith("Button");

        var nonsense = DocumentWith("Button");
        nonsense.Controls[0].Properties["BackColor"] = "not a colour";

        Assert.That(RenderHash(nonsense), Is.EqualTo(RenderHash(plain)),
            "an unparseable BackColor changed the drawing, so it was coerced to something");
    }

    /// <summary>
    /// ⚠ Pins the harness itself, not the product. If Skia is ever dropped from the test project,
    /// <c>CaptureRenderedFrame</c> returns nothing and <see cref="EveryControlKindRendersDistinctly"/>
    /// would fail with a message about frames rather than about drawing — this says which it is.
    /// </summary>
    [AvaloniaTest]
    public void TheHarnessActuallyRendersPixels()
    {
        var blank = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "T", Width = FormWidth, Height = FormHeight
        };

        var withControl = DocumentWith("Button");

        Assert.That(RenderHash(withControl), Is.Not.EqualTo(RenderHash(blank)),
            "a form with a Button rendered identically to an empty one — nothing is being drawn");
    }
}
