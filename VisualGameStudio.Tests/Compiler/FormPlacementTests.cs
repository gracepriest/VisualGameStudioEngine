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
    // ==================================================================
    // Task 25 — a component has no place, so a drop of one goes to the tray
    // ==================================================================

    [Test]
    public void PlacingAComponent_LandsInTheTray_AndIgnoresThePoint()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "Timer", 999, 999);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refusal, Is.Null);
            Assert.That(document.Components.Single(), Is.SameAs(result.Control));
            Assert.That(result.Control!.Id, Is.EqualTo("Timer1"));
            Assert.That(result.Control.Geometry, Is.Null, "no position — the point is irrelevant");
            Assert.That(result.Control.TabIndex, Is.Zero, "no tab order");
            Assert.That(result.Control.Properties, Is.Empty, "no Text stamped: a Timer has no caption");
            Assert.That(document.Controls, Is.Empty, "not a control");
        });
    }

    [Test]
    public void PlacingASecondComponent_MintsTheNextId_AcrossBothLists()
    {
        var document = WinFormsDocument();
        document.Controls.Add(Existing("Button", "Timer1", 0, 0, 10, 10));   // a control squatting on the name

        var result = FormPlacement.Place(document, "Timer", 0, 0);

        Assert.That(result.Control!.Id, Is.EqualTo("Timer2"), "one class, one field namespace");
    }

    [Test]
    public void PlacingAWebComponent_NeedsNoLayout_BecauseItHasNoCell()
    {
        // A web control refuses without a <Layout> (no cell to land in); a component has no cell
        // to need. A WinForms-only component is still refused on the web, by the catalog.
        var document = new FormDocument { Target = FormTarget.Web, Name = "F" };

        Assert.Multiple(() =>
        {
            Assert.That(FormPlacement.Place(document, "Timer", 0, 0).Refusal, Is.Null);
            Assert.That(document.Components.Single().Kind, Is.EqualTo("Timer"));
            Assert.That(FormPlacement.Place(document, "ToolTip", 0, 0).Refusal, Does.Contain("not available"));
        });
    }

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

    /// <summary>
    /// Task 24c, Task 15 Step 1 Part 2 — the same guard as
    /// <c>FormDocument.RenumberTabIndexes</c>, in <c>FormPlacement</c>'s own private
    /// <c>NextTabIndex</c>: <c>Definition?.Place is null or FormPlace.Positioned</c>, never
    /// <c>== FormPlace.Positioned</c>. An existing control whose Kind the catalog does not know has
    /// a null <see cref="FormControl.Definition"/> and IS positioned; the equality mutant would
    /// exclude it from the Max and hand this drop a TabIndex that collides with it.
    /// </summary>
    [Test]
    public void ADroppedControl_TakesTheNextTabIndex_EvenWhenAnExistingControlHasNoCatalogRow()
    {
        var document = WinFormsDocument();
        var mystery = Existing("UnknownWidgetKind", "mystery1", 0, 0, 10, 10);
        mystery.TabIndex = 5;
        document.Controls.Add(mystery);

        Assert.That(mystery.Definition, Is.Null, "fixture premise: an unknown Kind has no catalog row");

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.That(result.Control!.TabIndex, Is.EqualTo(6),
            "mystery1 (no catalog row, TabIndex 5) must still count toward the next tab index");
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
    /// <summary>
    /// ⚠ The example used to be <c>DataGridView</c> — a real WinForms control the catalog did not
    /// have yet. Task 23 added it, and this test began failing for a reason that had nothing to do
    /// with what it checks. The stand-in is now a name no control will ever have, so widening the
    /// catalog cannot break it again.
    /// </summary>
    public void AnUnknownKind_IsRefusedWithAReason()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "NotARealControl", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control, Is.Null);
            Assert.That(result.Refusal, Does.Contain("NotARealControl"));
            Assert.That(document.Controls, Is.Empty);
        });
    }

    // ==================================================================
    // Web documents — placed by CELL, not by pixel (D3)
    // ==================================================================

    private static FormDocument WebGrid(string cols = "1fr,1fr", string rows = "1fr,1fr") =>
        new()
        {
            Target = FormTarget.Web,
            Name = "LoginForm",
            Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = cols, Rows = rows, Gap = "0px" }
        };

    [Test]
    public void ADropOnAWebGrid_LandsInTheCellUnderThePointer()
    {
        // The web half of a drop. A .blwebform records WHICH CELL a control is in, so that — not a
        // pixel — is what the drop has to produce.
        var document = WebGrid();

        var result = FormPlacement.Place(document, "Button", 300, 250);   // right column, second row

        Assert.That(result.Refusal, Is.Null);
        var grid = (GridGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(grid.Col, Is.EqualTo(1));
            Assert.That(grid.Row, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWebControl_SpansOneCell()
    {
        // ⚠ Span 1 is the default and is deliberately NOT written out by the writer, so inventing
        // anything else here would put ColSpan="1" into every element the designer touches.
        var document = WebGrid();

        var result = FormPlacement.Place(document, "Button", 50, 50);

        var grid = (GridGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(grid.ColSpan, Is.EqualTo(1));
            Assert.That(grid.RowSpan, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWebDrop_GetsTheSameIdAndCaptionRulesAsAWinFormsOne()
    {
        var document = WebGrid();

        var result = FormPlacement.Place(document, "Button", 50, 50);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control!.Id, Is.EqualTo("Button1"));
            Assert.That(result.Control.Properties["Text"], Is.EqualTo("Button1"));
            Assert.That(result.Control.TabIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void AWebDropOutsideThePage_IsRefused()
    {
        var document = WebGrid();

        var result = FormPlacement.Place(document, "Button", -50, -50);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control, Is.Null);
            Assert.That(result.Refusal, Is.Not.Null.And.Not.Empty);
            Assert.That(document.Controls, Is.Empty);
        });
    }

    [Test]
    public void AFlowLayout_IsStillRefused_BecauseItHasNoCells()
    {
        // ⛔ Flow is flexbox: position comes from document ORDER, not from a cell, so there is
        // nothing on the canvas for a point to mean. Refusing with a reason beats inventing a Col
        // and Row that the emitted page would ignore.
        var document = new FormDocument
        {
            Target = FormTarget.Web,
            Name = "LoginForm",
            Layout = new FormLayout { Kind = FormLayoutKind.Flow, Dir = "Vertical" }
        };

        var result = FormPlacement.Place(document, "Button", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control, Is.Null);
            Assert.That(result.Refusal, Does.Contain("Flow"));
            Assert.That(document.Controls, Is.Empty);
        });
    }

    [Test]
    public void APageWithNoLayout_IsRefused()
    {
        // No <Layout> means no grid in the emitted CSS either, so a Col and Row would describe a
        // grid the page does not have.
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

    // ==================================================================
    // Task 18 (commit 24c) — a Docked strip, an Item's refusal, and PlaceItem
    // ==================================================================

    /// <summary>
    /// A strip (spec §1: `Place == Docked`) never gets a geometry, never nests inside whatever
    /// container the point happens to be over, and takes the row's own default `Dock` — the point is
    /// pure noise to it, exactly as it is for a component.
    /// </summary>
    [Test]
    public void Place_ADockedKind_LandsTopLevel_GeometryLess_WithTheRowsDock_IgnoringThePoint()
    {
        var document = WinFormsDocument();
        var panel = Existing("Panel", "Panel1", 0, 0, 300, 200);
        document.Controls.Add(panel);

        // A point deep inside the Panel would nest a Positioned control; a Docked kind must not —
        // "the point is irrelevant" has to hold for containment, not only for X/Y.
        var result = FormPlacement.Place(document, "StatusStrip", 150, 100);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refusal, Is.Null);
            Assert.That(document.Controls, Does.Contain(result.Control),
                "a strip is top-level, never nested inside a container it was dropped over");
            Assert.That(panel.Children, Is.Empty);
            Assert.That(result.Control!.Geometry, Is.Null, "no X/Y/Width/Height for a docked strip");
            Assert.That(result.Control.TabIndex, Is.Zero, "no tab order");
            Assert.That(result.Control.Properties["Dock"], Is.EqualTo("Bottom"),
                "the row's own default (StatusStrip docks Bottom), not anything derived from the point");
        });
    }

    /// <summary>
    /// An item (spec §1: `Place == Item`) has no place of its own on the canvas — it is created from
    /// its host's "Type Here" slot (§6), never dropped — so a canvas drop of one must refuse, by name,
    /// rather than silently position it like an ordinary control.
    /// </summary>
    [Test]
    public void Place_AnItemKind_IsRefused_NamingTypeHere()
    {
        var document = WinFormsDocument();

        var result = FormPlacement.Place(document, "ToolStripMenuItem", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Control, Is.Null);
            Assert.That(result.Refusal, Does.Contain("Type Here"));
            Assert.That(document.Controls, Is.Empty);
        });
    }

    /// <summary>
    /// <see cref="FormPlacement.PlaceItem"/> — the "Type Here" entry point (§6): appends an item to a
    /// host's <c>Children</c>, minting VS's own id (the caption, camel-cased and sanitised, plus the
    /// kind), or refuses when the host's <see cref="FormItemRule"/> does not accept the kind.
    ///
    /// <para>⚠ The fallback (an unusable caption, or <c>ToolStripSeparator</c>, which never reads its
    /// caption) is SEEDED with the "1" — <c>FormDocument.MakeUniqueId</c> returns its argument
    /// UNCHANGED when free — while the caption stem is NOT seeded: <c>openToolStripMenuItem</c>, then
    /// <c>openToolStripMenuItem1</c> only once the plain stem collides. Get this backwards and a
    /// SINGLE separator would read <c>toolStripSeparator</c> rather than VS's own
    /// <c>toolStripSeparator1</c>.</para>
    /// </summary>
    [Test]
    public void PlaceItem_AppendsToTheHost_WithTheCaptionAsId()
    {
        var document = WinFormsDocument();
        var menuStrip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        document.Controls.Add(menuStrip);

        var open = FormPlacement.PlaceItem(document, menuStrip, "ToolStripMenuItem", "&Open...");

        Assert.Multiple(() =>
        {
            Assert.That(open.Refusal, Is.Null);
            Assert.That(open.Control!.Id, Is.EqualTo("openToolStripMenuItem"));
            Assert.That(open.Control.Properties["Text"], Is.EqualTo("&Open..."));
            Assert.That(menuStrip.Children[^1], Is.SameAs(open.Control), "appended, last child of the host");
        });

        var secondOpen = FormPlacement.PlaceItem(document, menuStrip, "ToolStripMenuItem", "Open");
        Assert.That(secondOpen.Control!.Id, Is.EqualTo("openToolStripMenuItem1"),
            "a second Open collides on the plain caption stem, which was NOT seeded with a 1");

        var separator = FormPlacement.PlaceItem(document, menuStrip, "ToolStripSeparator", "-");
        Assert.Multiple(() =>
        {
            Assert.That(separator.Refusal, Is.Null);
            Assert.That(separator.Control!.Kind, Is.EqualTo("ToolStripSeparator"));
            Assert.That(separator.Control.Id, Is.EqualTo("toolStripSeparator1"),
                "the fallback IS seeded with the 1 — MakeUniqueId returns it unchanged because it is free");
        });

        var numeric = FormPlacement.PlaceItem(document, menuStrip, "ToolStripMenuItem", "123");
        Assert.That(numeric.Control!.Id, Is.EqualTo("toolStripMenuItem1"),
            "a leading-digit caption is unusable as an identifier and falls back to the kind stem");

        // `&&` is a literal ampersand in a WinForms caption (never the accelerator mark), and after it
        // collapses the surviving `&` still has to drop out of the id rather than break it.
        var ampersand = FormPlacement.PlaceItem(document, menuStrip, "ToolStripMenuItem", "Save && Close");
        Assert.That(ampersand.Control!.Id, Is.EqualTo("saveCloseToolStripMenuItem"));

        var before = menuStrip.Children.Count;
        var refused = FormPlacement.PlaceItem(document, menuStrip, "Button", "nope");
        Assert.Multiple(() =>
        {
            Assert.That(refused.Control, Is.Null);
            Assert.That(refused.Refusal, Is.Not.Null.And.Not.Empty);
            Assert.That(menuStrip.Children, Has.Count.EqualTo(before), "the refused item added nothing");
        });
    }

    /// <summary>
    /// Task 18 implementer's flagged edge case: a caption that is NOTHING but accelerator marks
    /// leaves the accelerator-stripped, alnum-filtered caption empty, so <c>ItemId</c> must take its
    /// fallback (the kind stem + "1") rather than mint an empty or otherwise unusable id.
    /// </summary>
    [Test]
    public void PlaceItem_ACaptionThatIsEntirelyAcceleratorMarks_FallsBackRatherThanBreaking()
    {
        var document = WinFormsDocument();
        var menuStrip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        document.Controls.Add(menuStrip);

        var result = FormPlacement.PlaceItem(document, menuStrip, "ToolStripMenuItem", "&&&");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refusal, Is.Null);
            Assert.That(result.Control, Is.Not.Null);
            Assert.That(result.Control!.Id, Is.EqualTo("toolStripMenuItem1"),
                "an all-accelerator-marks caption leaves nothing usable — the fallback (kind stem + " +
                "'1') must be used, never an empty or malformed id");
            Assert.That(FormDocument.IsLegalControlId(result.Control.Id), Is.True);
        });
    }
}
