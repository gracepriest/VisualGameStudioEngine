using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 17: the WinForms control catalog, and the gate that makes it falsifiable.
///
/// <para>⛔⛔ <b>The catalog is hand-written data that nothing in this compiler can check.</b>
/// <c>EnableNetResolution</c> returns early for <c>UseWindowsForms</c>, the resolver closure cannot
/// reach <c>System.Windows.Forms.dll</c>, and <c>CommonNetTypes</c> carries no WinForms names — so
/// every <c>Form</c>/<c>Button</c>/<c>Point</c> member types as <c>Object</c> with no diagnostic and
/// <b>a misspelled property name compiles green</b>. The stand-in for the type system that does not
/// exist is this: generate every control with every property, run it through the real emitter, and
/// require <c>csc</c> to accept the result.</para>
///
/// <para>⛔ Every case is driven from <see cref="FormControlCatalog.All"/> via
/// <c>TestCaseSource</c>. A hand-written <c>[TestCase]</c> list beside the catalog is the failure
/// mode this is built to prevent: a row added to the catalog would gain no coverage, silently.</para>
///
/// <para><b>Split across two fixtures on purpose.</b> The catalog guards and the
/// gate-proves-itself tests are pure in-process Roslyn, cost milliseconds, and belong in the fast
/// subset where people will actually run them. Anything that SPAWNS THE REAL CLI is
/// <c>[Category("Integration")] [NonParallelizable]</c> below, per the plan — each spawn saturates
/// a core, and the repo has measured build contention failing tests that pass in isolation.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class WinFormsCatalogSweepTests
{
    /// <summary>Every catalog row that claims to exist on WinForms. The sweep's source of truth.</summary>
    private static IEnumerable<TestCaseData> EveryWinFormsControl() =>
        FormControlCatalog.All
            .Where(c => c.SupportsTarget(FormTarget.WinForms))
            .Select(c => new TestCaseData(c.Kind).SetName("{m}(" + c.Kind + ")"));

    // ==================================================================
    // The gate itself must be able to fail
    // ==================================================================

    [Test]
    public void TheGate_RejectsAPropertyTheControlDoesNotHave()
    {
        // ⛔ Proving the instrument before trusting it. If this ever passes, every test below is
        // reporting success about nothing — which is strictly worse than having no gate, because
        // it looks like coverage.
        var errors = WinFormsCompile.Errors("""
            using System.Windows.Forms;
            public class F : Form {
                void Init() { var l = new Label(); l.Txt = "x"; }
            }
            """);

        Assert.That(errors, Is.Not.Empty, "csc must reject a property Label does not have");
        Assert.That(string.Join("\n", errors), Does.Contain("CS1061"));
    }

    [Test]
    public void TheGate_RejectsAssigningThroughAStructReturn()
    {
        // ⛔⛔ The geometry fan-in trap, pinned as a MEASUREMENT rather than an assumption — the
        // plan says not to assert a specific code without measuring it. Location returns a Point
        // STRUCT, so `Location.X = 96` modifies a temporary. Measured 2026-09-13: CS1612.
        var errors = WinFormsCompile.Errors("""
            using System.Windows.Forms;
            public class F : Form {
                void Init() { var l = new Label(); l.Location.X = 96; }
            }
            """);

        Assert.That(string.Join("\n", errors), Does.Contain("CS1612"),
            "this is exactly what the one-statement Location fan-in exists to avoid");
    }

    // ==================================================================
    // Completeness — a row must not be able to opt out of coverage
    // ==================================================================

    [Test]
    public void EveryWinFormsControl_DeclaresItsWinFormsTypeName()
    {
        var missing = FormControlCatalog.All
            .Where(c => c.SupportsTarget(FormTarget.WinForms) && string.IsNullOrWhiteSpace(c.WinFormsType))
            .Select(c => c.Kind);

        Assert.That(missing, Is.Empty,
            "a control the designer offers on WinForms with no type name generates `New ()`");
    }

    [Test]
    public void EveryEnumProperty_CarriesItsWinFormsEnumType()
    {
        // ⛔ The completeness guard that stops the next enum property repeating the measured bug:
        // `lbl.TextAlign = Center` compiles through BasicLang and csc rejects the emitted C# with
        // CS0103. Without the enum type there is nothing to qualify the member with.
        var untyped = FormControlCatalog.All
            .Where(c => c.SupportsTarget(FormTarget.WinForms))
            .SelectMany(c => c.Properties.Select(p => (c.Kind, p)))
            .Where(x => x.p.Type == FormPropertyType.Enum && x.p.WinFormsEnumType == null)
            .Select(x => $"{x.Kind}.{x.p.Name}");

        Assert.That(untyped, Is.Empty,
            "add WinFormsEnumType (and a member mapping if the names differ) to every Enum row");
    }

    [Test]
    public void EveryEnumValue_MapsToAMemberName()
    {
        var unmapped = new List<string>();

        foreach (var control in FormControlCatalog.All.Where(c => c.SupportsTarget(FormTarget.WinForms)))
        {
            foreach (var property in control.Properties.Where(p => p.Type == FormPropertyType.Enum))
            {
                foreach (var value in property.AllowedValues ?? Array.Empty<string>())
                {
                    var literal = property.WinFormsLiteral(value);
                    if (literal == null || !literal.Contains('.'))
                    {
                        unmapped.Add($"{control.Kind}.{property.Name} = {value}");
                    }
                }
            }
        }

        Assert.That(unmapped, Is.Empty, "every allowed value must emit a qualified enum member");
    }

    // ==================================================================
    // The sweep: every control, every property, through the real emitter
    // ==================================================================

    [TestCaseSource(nameof(EveryWinFormsControl))]
    [Category("Integration")]
    public void EveryProperty_OfEveryControl_EmitsCSharpThatCscAccepts(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "SweepForm", Width = 800, Height = 450, Text = "Sweep"
        };

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = "ctl",
            TabIndex = 0,
            Geometry = new PixelGeometry { X = 96, Y = 80, Width = 120, Height = 24, Anchor = "Top" }
        };

        foreach (var property in definition.Properties)
        {
            control.Properties[property.Name] = SampleValue(property);
        }

        form.Controls.Add(control);

        var generated = GenerateCSharp(form);
        WinFormsCompile.AssertCompiles(generated,
            $"the designer's own output for a '{kind}' with every catalog property set must compile.");
    }

    [Test]
    [Category("Integration")]
    public void AHexColour_Compiles_EvenThoughItCannotBeEmittedVerbatim()
    {
        // ⛔ '#RRGGBB' is a value the catalog ACCEPTS, so it is Canon in the document and round-trips
        // untouched — and it cannot be spliced into source, because '#' starts a preprocessor
        // directive. Measured: `lbl.ForeColor = #FF0000` does not even LEX. It has to become a
        // Color.FromArgb call, and this is what says so.
        var form = FormWith(c => c.Properties["ForeColor"] = "#FF8800");

        var generated = GenerateCSharp(form);

        Assert.That(generated, Does.Contain("FromArgb"));
        WinFormsCompile.AssertCompiles(generated, "a hex colour must survive into compilable source.");
    }

    [Test]
    [Category("Integration")]
    public void ANamedColour_BecomesAColorMember()
    {
        var generated = GenerateCSharp(FormWith(c => c.Properties["ForeColor"] = "Red"));

        Assert.That(generated, Does.Contain("Color.Red"));
        WinFormsCompile.AssertCompiles(generated, "a named colour must qualify as a Color member.");
    }

    [Test]
    [Category("Integration")]
    public void Geometry_FansIntoOneStatementPerPair()
    {
        // ⛔⛔ ONE statement. `Location.X = 96` is CS1612 (pinned above), and BasicLang catches none
        // of it because WinForms member access degrades to Object with no diagnostic.
        var generated = GenerateCSharp(FormWith(_ => { }));

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("new Point(96, 80)"));
            Assert.That(generated, Does.Contain("new Size(120, 24)"));
            Assert.That(generated, Does.Not.Contain("Location.X"));
            Assert.That(generated, Does.Not.Contain("Size.Width"));
        });
    }

    [Test]
    [Category("Integration")]
    public void TheFormsOwnCaptionAndSize_ReachTheGeneratedCode()
    {
        var generated = GenerateCSharp(FormWith(_ => { }));

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("this.Text = \"Sweep\""));
            Assert.That(generated, Does.Contain("this.ClientSize = new Size(800, 450)"));
        });
    }

    [Test]
    public void AMultiEdgeAnchor_IsRefused_RatherThanEmittedWrong()
    {
        // ⛔⛔ BasicLang cannot express a combined flags value — measured three ways: `Or` wants
        // Boolean operands, `CType(7, AnchorStyles)` finds no conversion (the enum is an
        // unresolvable .NET type), and `|` lexes but the parser never consumes it. Emitting one
        // flag would put geometry on screen the running program does not reproduce; emitting all
        // of them would not compile. Refusing says so once, at design time.
        var form = FormWith(c => ((PixelGeometry)c.Geometry!).Anchor = "Left,Top,Right");

        var result = RegionWriter.Write("SweepForm.bas", Scaffold(), form, "SweepForm.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Changed, Is.False, "a refused write leaves the user's file alone");
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.AnchorNotExpressible));
        });
    }

    [Test]
    [Category("Integration")]
    public void ASingleEdgeAnchor_IsFine()
    {
        var generated = GenerateCSharp(FormWith(c => ((PixelGeometry)c.Geometry!).Anchor = "Bottom"));

        Assert.That(generated, Does.Contain("AnchorStyles.Bottom"));
        WinFormsCompile.AssertCompiles(generated, "one anchor edge is expressible and must work.");
    }

    // ==================================================================
    // Harness
    // ==================================================================

    private static string Scaffold() =>
        FormScaffolder.Create("SweepForm", FormTarget.WinForms).CodeText;

    private static FormDocument FormWith(Action<FormControl> customise)
    {
        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "SweepForm", Width = 800, Height = 450, Text = "Sweep"
        };

        var control = new FormControl
        {
            Kind = "Label",
            Id = "ctl",
            TabIndex = 0,
            Geometry = new PixelGeometry { X = 96, Y = 80, Width = 120, Height = 24 }
        };

        customise(control);
        form.Controls.Add(control);
        return form;
    }

    /// <summary>
    /// The whole real chain: the region writer's output, compiled by the REAL BasicLang compiler
    /// to C#. Nothing here is a stand-in — a gate built on a reimplementation of the emitter would
    /// prove only that the reimplementation agrees with itself.
    /// </summary>
    private static string GenerateCSharp(FormDocument form)
    {
        var written = RegionWriter.Write("SweepForm.bas", Scaffold(), form, "SweepForm.blform");
        Assert.That(written.Refused, Is.False,
            "the region writer refused: " + string.Join("; ", written.Diagnostics.Select(d => d.Format())));

        return CompileToCSharp(written.Text);
    }

    private static string CompileToCSharp(string basicLangSource)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-wfsweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "SweepForm.bas");
            File.WriteAllText(source, basicLangSource);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { source, "--target=csharp" }, dir, timeoutMs: 120_000);

            Assert.That(exit, Is.Zero,
                $"the real compiler rejected the designer's own output.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}\n" +
                $"--- generated BasicLang ---\n{basicLangSource}");

            var emitted = Path.Combine(dir, "SweepForm.cs");
            Assert.That(File.Exists(emitted), Is.True,
                $"compiler reported success but emitted no C#.\nSTDOUT:\n{stdout}");

            return File.ReadAllText(emitted);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>A value that is valid for the property's declared type — the Canon case.</summary>
    private static string SampleValue(FormPropertyDef property) => property.Type switch
    {
        FormPropertyType.String => "sample",
        FormPropertyType.Int => "12",
        FormPropertyType.Bool => "true",
        FormPropertyType.Color => "Red",
        FormPropertyType.Enum => property.AllowedValues![0],
        _ => "sample"
    };
}
