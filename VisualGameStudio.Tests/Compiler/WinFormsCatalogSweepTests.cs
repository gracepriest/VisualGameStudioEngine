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

    /// <summary>The rows with at least one Enum property — the ones whose member names need every value gated.</summary>
    private static IEnumerable<TestCaseData> EveryWinFormsControlWithAnEnum() =>
        FormControlCatalog.All
            .Where(c => c.SupportsTarget(FormTarget.WinForms) && c.Properties.Any(p => p.Type == FormPropertyType.Enum))
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

        // ⚠ Task 24, commit 24b: the shape (Tray/Docked/Item/Positioned) is now the ONE answer in
        // FormCatalogShapes.Canonical, not hand-built here. A component (Task 25) has no place: no
        // geometry, and it goes in the tray, not on the form. Measured 2026-09-19 with the shape
        // forced on: csc rejects Location/Size/Anchor (CS1061) and Controls.Add (CS1503, not a
        // Control) for all four — and NOTHING else, which is why the gate still runs every one of
        // their properties through csc here.
        var control = FormCatalogShapes.Canonical(form, definition, "ctl");

        foreach (var property in definition.Properties)
        {
            control.Properties[property.Name] = SampleValue(property);
        }

        var generated = GenerateCSharp(form);
        WinFormsCompile.AssertCompiles(generated,
            $"the designer's own output for a '{kind}' with every catalog property set must compile.");
    }

    /// <summary>
    /// ⛔ The property sweep above compiles only the FIRST allowed value of each Enum row, and
    /// <see cref="EveryEnumValue_MapsToAMemberName"/> checks only that the literal has a dot — so a
    /// misspelled SECOND member (<c>AlwaysBlnk</c>) was gated by nothing (review, 2026-09-19). One
    /// control per allowed value, so ONE csc compile per kind covers every member of every Enum
    /// property it has; a kind with several Enum properties cycles through each in step.
    /// </summary>
    [TestCaseSource(nameof(EveryWinFormsControlWithAnEnum))]
    [Category("Integration")]
    public void EveryEnumValue_OfEveryControl_EmitsCSharpThatCscAccepts(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var enums = definition.Properties.Where(p => p.Type == FormPropertyType.Enum).ToList();
        var width = enums.Max(p => p.AllowedValues!.Count);

        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "SweepForm", Width = 800, Height = 450, Text = "Sweep"
        };

        for (var i = 0; i < width; i++)
        {
            var control = FormCatalogShapes.Canonical(form, definition, $"ctl{i}", hostId: $"ctl{i}Host");

            foreach (var property in enums)
            {
                control.Properties[property.Name] = property.AllowedValues![i % property.AllowedValues.Count];
            }
        }

        WinFormsCompile.AssertCompiles(GenerateCSharp(form),
            $"every allowed value of every Enum property on '{kind}' must be a member csc knows: " +
            string.Join("; ", enums.Select(p => $"{p.Name} = {string.Join("|", p.AllowedValues!)}")));
    }

    /// <summary>
    /// Task 22's half of the same problem: an EVENT name is exactly as unfalsifiable as a property
    /// name, and by the same mechanism.
    ///
    /// <para>⛔⛔ <c>AddHandler ctl.CheckedChange, AddressOf ctl_CheckedChange</c> — one letter short
    /// — types as <c>Object</c> in BasicLang, emits without a word of complaint, and produces a
    /// control whose handler never fires. Nothing in the designer, the document or the region writer
    /// can tell that from the correct spelling. csc can, because the emitted C# declares the real
    /// <c>CheckBox</c>, so this drives the WHOLE gesture — catalog event, generated stub, generated
    /// wiring — through it.</para>
    ///
    /// <para>⚠ Deliberately built from <see cref="FormHandlers.PlanDefault"/> rather than a
    /// hand-written stub. A test that wrote its own <c>Sub</c> would gate the catalog's event name
    /// and silently stop gating the signature the designer actually generates.</para>
    /// </summary>
    [TestCaseSource(nameof(EveryWinFormsControl))]
    [Category("Integration")]
    public void TheDefaultEvent_OfEveryControl_WiresIntoCSharpThatCscAccepts(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "SweepForm", Width = 800, Height = 450, Text = "Sweep"
        };

        var control = FormCatalogShapes.Canonical(form, definition, "ctl");

        // The gesture's own output: the stub it writes, where it chooses to put it. For a component
        // that includes the row's WinFormsEventArgs — DoWorkEventArgs, PopupEventArgs — which is
        // exactly as unfalsifiable as the event name and gated the same way.
        var plan = FormHandlers.PlanDefault(
            form, control, FormScaffolder.Create("SweepForm", FormTarget.WinForms).CodeText);

        Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created),
            $"'{kind}' could not be double-clicked: {plan.Refusal}");

        FormHandlers.EnsureBind(control, plan.EventName, plan.Handler);

        var written = RegionWriter.Write("SweepForm.bas", plan.CodeText, form, "SweepForm.blform");
        Assert.That(written.Refused, Is.False,
            "the region writer refused: " + string.Join("; ", written.Diagnostics.Select(d => d.Format())));

        WinFormsCompile.AssertCompiles(
            CompileToCSharp(written.Text),
            $"a '{kind}' wired to its default event '{plan.EventName}' must compile — a misspelled " +
            "event name is invisible everywhere else.");
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

    /// <summary>
    /// ⚠ This test USED to assert the opposite — that a multi-edge anchor is REFUSED — and its
    /// premise was true when written: BasicLang could not express a combined flags value, measured
    /// three ways, and <c>docs/HANDOFF.md</c> carried the fix as an open decision for the owner.
    ///
    /// <para>Re-measured 2026-09-18: <c>Or</c> and <c>|</c> still fail, but <c>CType</c> was being
    /// refused by <c>SemanticAnalyzer.RejectImpossibleConversion</c> rather than by the language —
    /// and csc accepts <c>(AnchorStyles)13</c> perfectly well. That arm now exempts unresolvable
    /// .NET types, so the designer emits what VS's own Anchor picker produces.</para>
    ///
    /// <para>⛔ The refusal it was protecting has NOT been dropped, only narrowed: an edge name the
    /// enum does not have is still refused, because summing it as zero would silently anchor the
    /// control to nothing. That case is covered here and in <c>FormAnchorEmissionTests</c>.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AMultiEdgeAnchor_IsEmittedAndCompiles()
    {
        var generated = GenerateCSharp(
            FormWith(c => ((PixelGeometry)c.Geometry!).Anchor = "Left,Top,Right"));

        // Top(1) + Left(4) + Right(8) = 13
        Assert.That(generated, Does.Contain("(AnchorStyles)(13)"));
        WinFormsCompile.AssertCompiles(
            generated, "a multi-edge anchor must produce C# csc accepts.");
    }

    /// <summary>⛔ The narrowed refusal: an edge AnchorStyles does not have.</summary>
    [Test]
    public void AnAnchorNamingAnEdgeThatDoesNotExist_IsStillRefused()
    {
        var form = FormWith(c => ((PixelGeometry)c.Geometry!).Anchor = "Left,Sideways");

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

    /// <summary>
    /// Spec §8 "one control per new value shape": every system colour through csc in ONE compile. The
    /// name table is the unfalsifiable part — a misspelled SystemColors member is invisible to BasicLang.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void EverySystemColour_EmitsCSharpThatCscAccepts()
    {
        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "SweepForm", Width = 800, Height = 450, Text = "Sweep"
        };

        var i = 0;
        foreach (var name in FormSystemColors.Names)
        {
            var label = new FormControl
            {
                Kind = "Label", Id = $"lbl{i}", TabIndex = i,
                Geometry = new PixelGeometry { X = 0, Y = i * 4, Width = 10, Height = 4 }
            };
            label.Properties["ForeColor"] = name;
            form.Controls.Add(label);
            i++;
        }

        // ⛔ Not GenerateCSharp: a Degraded value is DROPPED with a warning, so a compile that succeeds
        // proves nothing about the names that never reached it. Every name must be written, as its
        // own whole statement, with no Degraded diagnostic.
        var written = RegionWriter.Write("SweepForm.bas", Scaffold(), form, "SweepForm.blform");
        Assert.That(written.Refused, Is.False,
            "the region writer refused: " + string.Join("; ", written.Diagnostics.Select(d => d.Format())));

        Assert.Multiple(() =>
        {
            Assert.That(written.Diagnostics.Where(d => d.Code == DesignCodes.DegradedProperty).Select(d => d.Format()),
                Is.Empty, "every system colour is a valid WinForms value");

            for (var n = 0; n < FormSystemColors.Names.Count; n++)
            {
                var statement = $@"^\s*lbl{n}\.ForeColor = SystemColors\.{FormSystemColors.Names[n]}\r?$";
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(written.Text, statement,
                        System.Text.RegularExpressions.RegexOptions.Multiline), Is.True,
                    $"lbl{n}.ForeColor = SystemColors.{FormSystemColors.Names[n]} must be written as a whole statement");
            }
        });

        WinFormsCompile.AssertCompiles(CompileToCSharp(written.Text),
            "every SystemColors name the catalog emits must be a member csc knows.");
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

    /// <summary>
    /// ⚠ <c>internal</c> so <c>FormAnchorEmissionTests</c> drives the SAME real CLI rather than
    /// standing up a second spawn harness that could drift from this one.
    /// </summary>
    internal static string CompileToCSharp(string basicLangSource)
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
