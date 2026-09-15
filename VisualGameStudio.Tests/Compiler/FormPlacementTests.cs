using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Placing a control on the canvas — the model half of toolbox drag-and-drop.
///
/// <para>⛔ Deliberately UI-free. What a drop does to the document is decidable without a pointer,
/// a visual tree or a headless renderer, and the parts that genuinely need a running IDE (does the
/// cursor show a drop effect, does the ListBox start a drag) are not pretended at here. The canvas
/// contributes exactly one thing to a drop — the form-space point, via the same
/// <c>FormCanvasTransform</c> that renders and hit-tests — and that mapping has its own tests.</para>
/// </summary>
[TestFixture]
public class FormPlacementTests
{
    private static FormDocument WinFormsDocument(int width = 400, int height = 300)
    {
        var document = new FormDocument
        {
            Target = FormTarget.WinForms,
            Name = "LoginForm",
            Width = width,
            Height = height
        };

        return document;
    }

    private static FormControl Existing(string kind, string id, int x, int y, int w, int h)
    {
        var control = new FormControl
        {
            Kind = kind,
            Id = id,
            Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
        };

        return control;
    }

    // ==================================================================
    // What a drop produces
    // ==================================================================

    [Test]
    public void ADroppedControl_LandsAtTheDropPoint()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "Button", 96, 80);

        Assert.That(result.Refusal, Is.Null);
        Assert.That(result.Control, Is.Not.Null);
        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(96));
            Assert.That(pixel.Y, Is.EqualTo(80));
        });
    }

    [Test]
    public void ADroppedControl_GetsTheKindsDefaultSize()
    {
        // ⛔ From the CATALOG, not a switch in the placer. A switch over kinds is the missing-arm
        // trap this repo has hit four times: add a catalog row, forget the arm, and the control
        // lands as a zero-size box the user cannot see or click.
        var document = WinFormsDocument();
        var button = FormControlCatalog.Find("Button")!;

        var result = FormPlacement.Place(document, "Button", 10, 10);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.Width, Is.EqualTo(button.DefaultWidth));
            Assert.That(pixel.Height, Is.EqualTo(button.DefaultHeight));
        });
    }

    [Test]
    public void EveryCatalogKind_HasAUsableDefaultSize()
    {
        // Driven from the catalog so a NEW row is covered the day it lands, rather than the day
        // someone remembers to add a case here.
        Assert.Multiple(() =>
        {
            foreach (var control in FormControlCatalog.All)
            {
                Assert.That(control.DefaultWidth, Is.GreaterThan(0), $"{control.Kind} width");
                Assert.That(control.DefaultHeight, Is.GreaterThan(0), $"{control.Kind} height");
            }
        });
    }

    [Test]
    public void ADroppedControl_IsAddedToTheDocument()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.That(document.Controls, Does.Contain(result.Control));
    }

    [Test]
    public void ADroppedControl_GoesOnTopOfItsSiblings()
    {
        // Document order is z-order. A control dropped onto the form belongs in front of what is
        // already there — dropping behind an existing control looks like the drop did nothing.
        var document = WinFormsDocument();
        document.Controls.Add(Existing("Label", "Label1", 0, 0, 400, 300));

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.That(document.Controls[^1], Is.SameAs(result.Control));
    }

    // ==================================================================
    // Identity
    // ==================================================================

    [Test]
    public void ADroppedControl_IsNamedAfterItsKind()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "TextBox", 10, 10);

        Assert.That(result.Control!.Id, Is.EqualTo("TextBox1"));
    }

    [Test]
    public void TwoOfAKind_DoNotCollide()
    {
        var document = WinFormsDocument();

        var first = FormPlacement.Place(document, "Button", 10, 10).Control!;
        var second = FormPlacement.Place(document, "Button", 20, 20).Control!;

        Assert.Multiple(() =>
        {
            Assert.That(first.Id, Is.EqualTo("Button1"));
            Assert.That(second.Id, Is.EqualTo("Button2"));
        });
    }

    [Test]
    public void AnIdAlreadyUsedByAHandWrittenControl_IsSkipped()
    {
        // ⛔ The document is the user's file. A designer that mints an id already in the XML
        // produces two controls with one name, and the region writer then declares the field twice.
        var document = WinFormsDocument();
        document.Controls.Add(Existing("Button", "Button1", 0, 0, 75, 23));

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.That(result.Control!.Id, Is.EqualTo("Button2"));
    }

    [Test]
    public void ADroppedControl_TakesTheNextTabIndex()
    {
        var document = WinFormsDocument();
        var first = Existing("Label", "Label1", 0, 0, 50, 20);
        first.TabIndex = 0;
        document.Controls.Add(first);

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.That(result.Control!.TabIndex, Is.EqualTo(1));
    }

    [Test]
    public void ADroppedControl_CarriesItsIdAsItsCaption()
    {
        // What every VB/WinForms designer does, and the reason is legibility: the canvas draws the
        // Text when there is one, so a caption-less Button is a blank box on a schematic.
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.That(result.Control!.Properties["Text"], Is.EqualTo("Button1"));
    }

    [Test]
    public void AKindWithNoTextProperty_GetsNoCaption()
    {
        // ⛔ Ask the catalog. Stamping Text onto a kind whose catalog row has no Text property is
        // how an unknown attribute reaches the writer and, on WinForms, how csc rejects the
        // generated file.
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "ListBox", 10, 10);

        Assert.That(result.Control!.Properties.ContainsKey("Text"), Is.False);
    }

    // ==================================================================
    // Containers
    // ==================================================================

    [Test]
    public void ADropInsideAPanel_BecomesAChildOfThatPanel()
    {
        var document = WinFormsDocument();
        var panel = Existing("Panel", "Panel1", 50, 40, 200, 150);
        document.Controls.Add(panel);

        var result = FormPlacement.Place(document, "Button", 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Does.Contain(result.Control));
            Assert.That(document.Controls, Does.Not.Contain(result.Control));
        });
    }

    [Test]
    public void ADropInsideAPanel_IsPositionedRelativeToThePanel()
    {
        // ⛔ WinForms child coordinates are relative to the CONTAINER. Storing the form-space point
        // puts the control at panel.X + x once the program runs — it looks right on the canvas and
        // lands somewhere else at run time, which is the worst shape a designer bug can take.
        var document = WinFormsDocument();
        var panel = Existing("Panel", "Panel1", 50, 40, 200, 150);
        document.Controls.Add(panel);

        var result = FormPlacement.Place(document, "Button", 100, 90);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(50));
            Assert.That(pixel.Y, Is.EqualTo(50));
        });
    }

    [Test]
    public void ADropOnANonContainer_LandsOnTheFormNotInsideIt()
    {
        // A Button is not a container. Dropping on one must not nest.
        var document = WinFormsDocument();
        var button = Existing("Button", "Button1", 50, 40, 200, 150);
        document.Controls.Add(button);

        var result = FormPlacement.Place(document, "Label", 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(button.Children, Is.Empty);
            Assert.That(document.Controls, Does.Contain(result.Control));
        });
    }

    // ==================================================================
    // Staying on the form
    // ==================================================================

    [Test]
    public void ADropNearTheRightEdge_IsPulledBackSoTheControlFits()
    {
        var document = WinFormsDocument(width: 400, height: 300);
        var button = FormControlCatalog.Find("Button")!;

        var result = FormPlacement.Place(document, "Button", 390, 295);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(400 - button.DefaultWidth));
            Assert.That(pixel.Y, Is.EqualTo(300 - button.DefaultHeight));
        });
    }

    [Test]
    public void ADropAboveOrLeftOfTheForm_IsPulledToTheOrigin()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "Button", -30, -30);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(0));
            Assert.That(pixel.Y, Is.EqualTo(0));
        });
    }

    [Test]
    public void AControlLargerThanTheForm_IsNotGivenANegativePosition()
    {
        var document = WinFormsDocument(width: 40, height: 30);

        var result = FormPlacement.Place(document, "ListBox", 20, 20);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(0));
            Assert.That(pixel.Y, Is.EqualTo(0));
        });
    }

    // ==================================================================
    // Refusals — each says why, because a drop that silently does nothing reads as a broken IDE
    // ==================================================================

    [Test]
    public void AnUnknownKind_IsRefusedWithAReason()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "DataGridView", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control, Is.Null);
            Assert.That(result.Refusal, Does.Contain("DataGridView"));
            Assert.That(document.Controls, Is.Empty);
        });
    }

    [Test]
    public void AWebDocument_IsRefusedBecauseItsControlsAreNotPositionedInPixels()
    {
        // ⛔ D3: a .blwebform positions controls by Grid/Flow cell, not by X/Y — and the canvas
        // draws nothing for grid geometry, so a "successful" drop here would add a control the user
        // cannot see anywhere but the Code view. Refusing with a reason is the honest answer until
        // the canvas can draw cells.
        var document = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control, Is.Null);
            Assert.That(result.Refusal, Is.Not.Null.And.Not.Empty);
            Assert.That(document.Controls, Is.Empty);
        });
    }

    [Test]
    public void ARefusal_LeavesTheDocumentExactlyAsItWas()
    {
        var document = WinFormsDocument();
        document.Controls.Add(Existing("Button", "Button1", 0, 0, 75, 23));

        FormPlacement.Place(document, "NotAControl", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(document.Controls, Has.Count.EqualTo(1));
            Assert.That(document.Controls[0].Id, Is.EqualTo("Button1"));
        });
    }
}
