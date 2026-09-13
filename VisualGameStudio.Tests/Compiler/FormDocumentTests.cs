using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 4: the form model and its catalog.
///
/// <para>The catalog assertions are the load-bearing ones. A WinForms control catalog is otherwise
/// unfalsifiable in this repo — <c>EnableNetResolution</c> returns early for <c>UseWindowsForms</c>,
/// the resolver closure cannot reach <c>System.Windows.Forms.dll</c>, and every <c>Form</c>/
/// <c>Button</c> member access types as <c>Object</c> with no diagnostic, so a misspelled property
/// compiles green. These tests pin the table that the eventual CLI-exit-0 gate is driven from.</para>
/// </summary>
[TestFixture]
public class FormDocumentTests
{
    // ------------------------------------------------------------------
    // Catalog
    // ------------------------------------------------------------------

    [Test]
    public void Catalog_HasTenKinds_WithUniqueNames()
    {
        Assert.That(FormControlCatalog.All, Has.Count.EqualTo(10));
        Assert.That(FormControlCatalog.All.Select(c => c.Kind).Distinct().Count(), Is.EqualTo(10),
            "two catalog rows share a Kind — the element name is the document's only control discriminator");
    }

    [Test]
    public void Catalog_EveryKind_IsUsableByAtLeastOneTarget()
    {
        foreach (var def in FormControlCatalog.All)
        {
            Assert.That(def.SupportsTarget(FormTarget.WinForms) || def.SupportsTarget(FormTarget.Web), Is.True,
                $"'{def.Kind}' has neither a WinForms type nor an HTML tag, so no target can emit it");
        }
    }

    [Test]
    public void Catalog_BothTargets_CanBuildTheSpecsLoginForm()
    {
        // The spec shows the same two-control login form in both formats, so both catalogs must
        // carry Label, TextBox and Button. A target missing one cannot express the worked example.
        foreach (var target in new[] { FormTarget.WinForms, FormTarget.Web })
        {
            var kinds = FormControlCatalog.For(target).Select(c => c.Kind).ToList();
            Assert.That(kinds, Does.Contain("Label").And.Contains("TextBox").And.Contains("Button"),
                $"{target} cannot express the spec's worked example");
        }
    }

    [Test]
    public void Catalog_PropertyNames_AreUniqueWithinAKind()
    {
        foreach (var def in FormControlCatalog.All)
        {
            var names = def.Properties.Select(p => p.Name).ToList();
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(names.Count),
                $"'{def.Kind}' declares a property twice — the property grid would show two rows " +
                "writing the same attribute");
        }
    }

    [Test]
    public void Catalog_NoPropertyCollidesWithAStructuralAttribute()
    {
        // A property named "Width" would be written twice on the element: once as geometry and once
        // as a property, and the second would win non-deterministically depending on write order.
        foreach (var def in FormControlCatalog.All)
        {
            foreach (var property in def.Properties)
            {
                Assert.That(FormControlCatalog.IsStructural(property.Name), Is.False,
                    $"'{def.Kind}.{property.Name}' collides with a structural attribute");
            }
        }
    }

    [Test]
    public void Catalog_EnumProperties_DeclareTheirAllowedValues_AndAcceptTheirDefault()
    {
        foreach (var def in FormControlCatalog.All)
        {
            foreach (var property in def.Properties.Where(p => p.Type == FormPropertyType.Enum))
            {
                Assert.That(property.AllowedValues, Is.Not.Null.And.Not.Empty,
                    $"'{def.Kind}.{property.Name}' is an Enum with no allowed values — nothing could ever " +
                    "reach the Canon tier");
                Assert.That(property.Accepts(property.Default), Is.True,
                    $"'{def.Kind}.{property.Name}' declares a default its own allowed values reject");
            }
        }
    }

    [Test]
    public void Catalog_EveryDeclaredDefault_ParsesToItsDeclaredType()
    {
        foreach (var def in FormControlCatalog.All)
        {
            foreach (var property in def.Properties.Where(p => p.Default != null))
            {
                Assert.That(property.Accepts(property.Default), Is.True,
                    $"'{def.Kind}.{property.Name}' default '{property.Default}' does not parse as " +
                    $"{property.Type} — the property would start life outside the Canon tier");
            }
        }
    }

    [TestCase("Text", FormPropertyType.String, "anything", true)]
    [TestCase("MaxLength", FormPropertyType.Int, "12", true)]
    [TestCase("MaxLength", FormPropertyType.Int, "twelve", false)]
    [TestCase("Enabled", FormPropertyType.Bool, "true", true)]
    [TestCase("Enabled", FormPropertyType.Bool, "yes", false)]
    [TestCase("ForeColor", FormPropertyType.Color, "#ff8800", true)]
    [TestCase("ForeColor", FormPropertyType.Color, "#f80", true)]
    [TestCase("ForeColor", FormPropertyType.Color, "Red", true)]
    [TestCase("ForeColor", FormPropertyType.Color, "#gggggg", false)]
    [TestCase("ForeColor", FormPropertyType.Color, "12345", false)]
    public void PropertyDef_Accepts_OnlyValuesOfItsDeclaredType(
        string name, FormPropertyType type, string value, bool expected)
    {
        var def = new FormPropertyDef(name, type);
        Assert.That(def.Accepts(value), Is.EqualTo(expected));
    }

    [Test]
    public void PropertyDef_Accepts_RejectsNull()
    {
        Assert.That(new FormPropertyDef("Text", FormPropertyType.String).Accepts(null), Is.False,
            "an absent value is not a Canon value — it means the property is unset");
    }

    // ------------------------------------------------------------------
    // Identifiers
    // ------------------------------------------------------------------

    [TestCase("btnLogin", true)]
    [TestCase("txt_user", true, Description = "underscores ARE legal in an identifier")]
    [TestCase("_hidden", true)]
    [TestCase("btn1", true)]
    [TestCase("1btn", false)]
    [TestCase("btn-login", false)]
    [TestCase("btn login", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsLegalControlId_FollowsTheIdentifierRule_NotTheTypeNameRule(string? id, bool expected)
    {
        // The PascalCase/underscore heuristic that breaks Me.<member> applies to TYPE names.
        // Dim txt_user As TextBox is safe; a CLASS called Main_Form is not.
        Assert.That(FormDocument.IsLegalControlId(id), Is.EqualTo(expected));
    }

    [Test]
    public void MakeUniqueId_ReturnsTheDesiredId_WhenFree()
    {
        Assert.That(FormDocument.MakeUniqueId("btnLogin", _ => false), Is.EqualTo("btnLogin"));
    }

    [Test]
    public void MakeUniqueId_AppendsACounter_WhenTaken()
    {
        var taken = new HashSet<string> { "btnLogin" };
        Assert.That(FormDocument.MakeUniqueId("btnLogin", taken.Contains), Is.EqualTo("btnLogin1"));
    }

    [Test]
    public void MakeUniqueId_TreatsATrailingNumberAsACounter()
    {
        // btnLogin2 colliding must give btnLogin3, not btnLogin21 — otherwise ids grow a digit
        // every paste and become unreadable after four.
        var taken = new HashSet<string> { "btnLogin2" };
        Assert.That(FormDocument.MakeUniqueId("btnLogin2", taken.Contains), Is.EqualTo("btnLogin3"));
    }

    [Test]
    public void MakeUniqueId_SkipsARunOfTakenIds()
    {
        var taken = new HashSet<string> { "lbl", "lbl1", "lbl2", "lbl3" };
        Assert.That(FormDocument.MakeUniqueId("lbl", taken.Contains), Is.EqualTo("lbl4"));
    }

    // ------------------------------------------------------------------
    // Document
    // ------------------------------------------------------------------

    [Test]
    public void Document_RootElementAndExtension_FollowTheTarget()
    {
        Assert.Multiple(() =>
        {
            var web = new FormDocument { Target = FormTarget.Web };
            Assert.That(web.RootElementName, Is.EqualTo("WebForm"));
            Assert.That(web.FileExtension, Is.EqualTo(".blwebform"));

            var winforms = new FormDocument { Target = FormTarget.WinForms };
            Assert.That(winforms.RootElementName, Is.EqualTo("Form"));
            Assert.That(winforms.FileExtension, Is.EqualTo(".blform"));
        });
    }

    [Test]
    public void Document_ExtensionsAvoidThe8Point3GlobSweep()
    {
        // Windows 8.3 short names make a 3-character glob sweep in longer ones: *.bas really does
        // return Lib.basic. An extension beginning bas/cls/mod/bli would be swept into the compile
        // set by the default glob with no <Compile> item and no diagnostic.
        foreach (var extension in new[] { ".blform", ".blwebform" })
        {
            foreach (var swept in new[] { "bas", "cls", "mod", "bli" })
            {
                Assert.That(extension.TrimStart('.').StartsWith(swept, StringComparison.OrdinalIgnoreCase), Is.False,
                    $"'{extension}' would be swept into the compile set by the default *.{swept} glob");
            }
        }
    }

    [Test]
    public void Document_AllControls_WalksContainersBeforeChildren()
    {
        var document = BuildLoginForm(FormTarget.WinForms);
        var panel = new FormControl { Kind = "Panel", Id = "pnlBox" };
        panel.Children.Add(new FormControl { Kind = "Label", Id = "lblInner" });
        document.Controls.Add(panel);

        Assert.That(document.AllControls().Select(c => c.Id),
            Is.EqualTo(new[] { "lblUser", "txtUser", "btnLogin", "pnlBox", "lblInner" }),
            "a container must precede its children — the region writer declares the parent first");
    }

    [Test]
    public void Document_RenumberTabIndexes_UsesDocumentOrder()
    {
        var document = BuildLoginForm(FormTarget.Web);
        foreach (var control in document.Controls)
        {
            control.TabIndex = 99;
        }

        document.RenumberTabIndexes();

        Assert.That(document.Controls.Select(c => c.TabIndex), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void Document_FindById_IsCaseSensitive()
    {
        var document = BuildLoginForm(FormTarget.Web);
        Assert.Multiple(() =>
        {
            Assert.That(document.FindById("btnLogin"), Is.Not.Null);
            Assert.That(document.FindById("btnlogin"), Is.Null,
                "BasicLang identifiers that differ only in case are different identifiers here; " +
                "matching loosely would let the designer bind to the wrong control");
        });
    }

    // ------------------------------------------------------------------
    // Clipboard — subtree serialize / deserialize-with-rename
    // ------------------------------------------------------------------

    [Test]
    public void Clipboard_RoundTripsAControlsKindIdGeometryAndProperties()
    {
        var button = new FormControl
        {
            Kind = "Button",
            Id = "btnLogin",
            TabIndex = 2,
            Geometry = new PixelGeometry { X = 190, Y = 60, Width = 100, Height = 30, Anchor = "Left,Top" }
        };
        button.Properties["Text"] = "Sign in";
        button.Binds.Add(new FormBind { Event = "Click", Handler = "btnLogin_Click" });

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { button });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, _ => false);

        Assert.That(pasted, Has.Count.EqualTo(1));
        var copy = pasted[0];
        Assert.Multiple(() =>
        {
            Assert.That(copy.Kind, Is.EqualTo("Button"));
            Assert.That(copy.Id, Is.EqualTo("btnLogin"), "nothing was taken, so nothing is renamed");
            Assert.That(copy.TabIndex, Is.EqualTo(2));
            Assert.That(copy.Properties["Text"], Is.EqualTo("Sign in"));
            Assert.That(copy.Binds.Single().Event, Is.EqualTo("Click"));
            Assert.That(copy.Binds.Single().Handler, Is.EqualTo("btnLogin_Click"));
            var geometry = (PixelGeometry)copy.Geometry!;
            Assert.That((geometry.X, geometry.Y, geometry.Width, geometry.Height), Is.EqualTo((190, 60, 100, 30)));
            Assert.That(geometry.Anchor, Is.EqualTo("Left,Top"));
        });
    }

    [Test]
    public void Clipboard_RenamesACollidingId_AndRetargetsItsConventionHandler()
    {
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        button.Binds.Add(new FormBind { Event = "Click", Handler = "btnLogin_Click" });

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { button });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, id => id == "btnLogin");

        Assert.Multiple(() =>
        {
            Assert.That(pasted.Single().Id, Is.EqualTo("btnLogin1"));
            Assert.That(pasted.Single().Binds.Single().Handler, Is.EqualTo("btnLogin1_Click"),
                "a handler named after the control must follow the rename, or the copy silently " +
                "shares the original's handler");
        });
    }

    [Test]
    public void Clipboard_LeavesAHandThatDoesNotFollowTheConvention_Alone()
    {
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        button.Binds.Add(new FormBind { Event = "Click", Handler = "SubmitTheForm" });

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { button });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, id => id == "btnLogin");

        Assert.That(pasted.Single().Binds.Single().Handler, Is.EqualTo("SubmitTheForm"),
            "a deliberately-named Sub is shared by both copies — that is what copying a control means");
    }

    [Test]
    public void Clipboard_RenamesChildrenToo_AndTwoPastedSiblingsDoNotCollide()
    {
        var panel = new FormControl { Kind = "Panel", Id = "pnl" };
        panel.Children.Add(new FormControl { Kind = "Label", Id = "lbl" });
        var other = new FormControl { Kind = "Label", Id = "lbl" };

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { panel, other });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, _ => false);

        var ids = pasted.SelectMany(c => c.SelfAndDescendants()).Select(c => c.Id).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count),
            "ids minted during one paste must be reserved against each other, not only against the document");
    }

    [Test]
    public void Clipboard_RefusesAPasteFromTheOtherFormat()
    {
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { button });

        Assert.That(FormClipboard.DeserializeSubtree(xml, FormTarget.Web, _ => false), Is.Empty,
            "a .blform subtree pasted into a .blwebform must be refused, not silently reinterpreted");
    }

    [Test]
    public void Clipboard_ReturnsEmpty_ForTextThatIsNotAFragment()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormClipboard.DeserializeSubtree("not xml at all", FormTarget.Web, _ => false), Is.Empty);
            Assert.That(FormClipboard.DeserializeSubtree("<Other/>", FormTarget.Web, _ => false), Is.Empty,
                "pasting arbitrary XML from another app must not produce controls");
        });
    }

    [Test]
    public void Clipboard_PreservesAttributesTheCatalogDoesNotKnow()
    {
        // D9: elements and attributes the reader does not recognise round-trip untouched. Losing
        // them would delete whatever a newer designer version wrote.
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        button.UnknownAttributes["FutureThing"] = "42";

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { button });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, _ => false);

        Assert.That(pasted.Single().UnknownAttributes["FutureThing"], Is.EqualTo("42"));
    }

    [Test]
    public void Clone_IsDeep()
    {
        var panel = new FormControl { Kind = "Panel", Id = "pnl", Geometry = new PixelGeometry { X = 5 } };
        panel.Children.Add(new FormControl { Kind = "Label", Id = "lbl" });
        panel.Properties["BorderStyle"] = "Fixed3D";

        var copy = panel.Clone();
        copy.Children[0].Id = "changed";
        copy.Properties["BorderStyle"] = "None";
        ((PixelGeometry)copy.Geometry!).X = 99;

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children[0].Id, Is.EqualTo("lbl"));
            Assert.That(panel.Properties["BorderStyle"], Is.EqualTo("Fixed3D"));
            Assert.That(((PixelGeometry)panel.Geometry!).X, Is.EqualTo(5));
        });
    }

    private static FormDocument BuildLoginForm(FormTarget target)
    {
        var document = new FormDocument { Target = target, Name = "LoginForm" };
        document.Controls.Add(new FormControl { Kind = "Label", Id = "lblUser", TabIndex = 0 });
        document.Controls.Add(new FormControl { Kind = "TextBox", Id = "txtUser", TabIndex = 1 });
        document.Controls.Add(new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 2 });
        return document;
    }
}
