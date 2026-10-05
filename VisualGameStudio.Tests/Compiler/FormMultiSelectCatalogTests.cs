using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 6 (pre-flight 2026-10-05, D-2): which rows a MULTI-selection may offer is Visual Studio's merge rule,
/// and the parts of it that are catalog data are judged here against the WinForms oracle.
///
/// <para>Rule 1 — the same row: <see cref="FormPropertyDef.SharesShapeWith"/> (same name, same type, same enum type, same
/// members). Rule 2 — mergeable: VS hides a <c>[MergableProperty(false)]</c> property for a multi-selection, and
/// <see cref="FormPropertyDef.Mergeable"/> is that attribute as catalog data, MEASURED by tools/WinFormsMetadataDump. Rule 3
/// — the intrinsic rows: the grid's own decision (Name and TabIndex never offered, the geometry rows offered) leans on the
/// snapshot's Location/Size/TabIndex/Anchor/Dock entries, pinned below so a regenerated oracle that disagreed would fail
/// here rather than silently keep a decision the measurement no longer supports.</para>
///
/// <para>⛔ Fast subset: it reads the committed JSON, so it runs on Linux too.</para>
/// </summary>
[TestFixture]
public class FormMultiSelectCatalogTests
{
    private static IEnumerable<FormControlDef> WinFormsDefinitions() =>
        FormControlCatalog.All.Where(d => d.SupportsTarget(FormTarget.WinForms)).Append(FormControlCatalog.FormRoot);

    // ==================================================================
    // Rule 2 — Mergeable is the oracle's measurement, row by row
    // ==================================================================

    [Test]
    public void EveryWinFormsRowsMergeable_IsTheSnapshotsMeasuredAttribute()
    {
        var snapshot = WinFormsMetadata.Load();
        var judged = 0;
        var findings = new List<string>();

        foreach (var definition in WinFormsDefinitions())
        {
            var type = snapshot.Type(definition.Kind);
            Assert.That(type, Is.Not.Null, $"'{definition.Kind}' is not in the snapshot");

            foreach (var row in definition.Properties.Where(p => p.AppliesTo(FormTarget.WinForms) && p.OracleExemption == null))
            {
                var entry = type!.Property(row.Name);
                if (entry == null)
                {
                    continue; // the parity fixture reports a missing entry
                }

                judged++;
                if (entry.Mergeable == null)
                {
                    findings.Add($"{definition.Kind}.{row.Name}: the snapshot carries no 'mergeable' — regenerate it");
                }
                else if (entry.Mergeable != row.Mergeable)
                {
                    findings.Add($"{definition.Kind}.{row.Name}: Mergeable is {row.Mergeable}, WinForms' " +
                                 $"[MergableProperty] says {entry.Mergeable} → Mergeable: {entry.Mergeable.Value.ToString().ToLowerInvariant()}");
                }
            }
        }

        Assert.That(judged, Is.GreaterThan(100), "precondition: the sweep judged the catalog, not nothing");
        Assert.That(findings, Is.Empty, string.Join("\n", findings));
    }

    /// <summary>M1 (pre-flight §6): the item collections are the catalog rows WinForms refuses to merge.</summary>
    [Test]
    public void TheItemCollections_AreNotMergeable_AndAPlainRowIs()
    {
        Assert.Multiple(() =>
        {
            foreach (var kind in new[] { "ComboBox", "ListBox", "CheckedListBox" })
            {
                Assert.That(FormControlCatalog.Find(kind)!.Properties.Single(p => p.Name == "Items").Mergeable, Is.False,
                    $"{kind}.Items carries [MergableProperty(false)]");
            }

            Assert.That(FormControlCatalog.Find("Button")!.Properties.Single(p => p.Name == "Text").Mergeable, Is.True);
        });
    }

    // ==================================================================
    // Rule 3 — the intrinsic rows' decision, against the snapshot
    // ==================================================================

    /// <summary>
    /// ⛔ D-2 rule 3 offers Location/Size/Anchor/Dock for a multi-selection and NEVER TabIndex. Both halves are WinForms'
    /// own attribute on <c>Control</c>, read here on every positioned WinForms kind the snapshot holds. If a regenerated
    /// oracle measured TabIndex mergeable (or Location not), this fails and the decision is re-opened — never silently kept.
    /// </summary>
    [TestCase("Location", true)]
    [TestCase("Size", true)]
    [TestCase("Anchor", true)]
    [TestCase("Dock", true)]
    [TestCase("TabIndex", false)]
    public void TheIntrinsicRowsDecision_IsWhatWinFormsMeasures(string property, bool mergeable)
    {
        var snapshot = WinFormsMetadata.Load();
        var seen = FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.WinForms) && d.Place == FormPlace.Positioned)
            .Select(d => (d.Kind, Entry: snapshot.Type(d.Kind)?.Property(property)))
            .Where(x => x.Entry != null)
            .ToList();

        Assert.That(seen, Is.Not.Empty, $"precondition: some positioned kind has {property}");
        Assert.That(seen.Where(x => x.Entry!.Mergeable != mergeable).Select(x => $"{x.Kind}.{property}={x.Entry!.Mergeable}"),
            Is.Empty, $"{property} must measure mergeable={mergeable} on every positioned kind (D-2 rule 3)");
    }

    // ==================================================================
    // Rule 1 — SharesShapeWith
    // ==================================================================

    private static FormPropertyDef RowOf(string kind, string name) =>
        FormControlCatalog.Find(kind)!.Properties.First(p => p.Name == name);

    /// <summary>
    /// Catalog-driven: for EVERY pair of same-named rows across kinds (FormRoot included), the predicate is symmetric and
    /// says exactly "same type, same enum type, same members" — computed here independently of its implementation.
    /// ⚠ One test over a loop, not a TestCaseSource: the pairs number in the thousands.
    /// </summary>
    [Test]
    public void SharesShapeWith_OverEveryPairOfSameNamedRows_IsSymmetric_AndComparesTypeEnumTypeAndMembers()
    {
        var rows = FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .SelectMany(d => d.Properties.Select(p => (d.Kind, Row: p))).ToList();
        var pairs = 0;
        var differentShape = 0;
        var findings = new List<string>();

        for (var i = 0; i < rows.Count; i++)
        {
            for (var j = i + 1; j < rows.Count; j++)
            {
                var (kindA, a) = rows[i];
                var (kindB, b) = rows[j];
                if (a.Name != b.Name)
                {
                    continue;
                }

                pairs++;
                var expected = a.Type == b.Type &&
                               string.Equals(a.WinFormsEnumType, b.WinFormsEnumType, StringComparison.Ordinal) &&
                               (a.AllowedValues ?? Array.Empty<string>()).SequenceEqual(
                                   b.AllowedValues ?? Array.Empty<string>(), StringComparer.Ordinal);
                differentShape += expected ? 0 : 1;

                if (a.SharesShapeWith(b) != expected)
                {
                    findings.Add($"{kindA}.{a.Name} x {kindB}.{b.Name}: SharesShapeWith={a.SharesShapeWith(b)}, expected {expected}");
                }

                if (b.SharesShapeWith(a) != a.SharesShapeWith(b))
                {
                    findings.Add($"{kindA}.{a.Name} x {kindB}.{b.Name}: not symmetric");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(pairs, Is.GreaterThan(100), "precondition: the sweep has pairs to sweep");
            Assert.That(differentShape, Is.GreaterThan(0), "precondition: some same-named rows really differ in shape");
            Assert.That(findings, Is.Empty, string.Join("\n", findings));
        });
    }

    /// <summary>
    /// ⛔ The pre-flight's plan-text error 4: TextAlign is an Enum on BOTH a Button (ContentAlignment) and a TextBox
    /// (HorizontalAlignment). Comparing only name and <see cref="FormPropertyType"/> would merge them and write
    /// <c>MiddleCenter</c> into a TextBox.
    /// </summary>
    [Test]
    public void TextAlign_OnAButtonAndATextBox_IsNotTheSameRow_ButBackColorOnAButtonAndALabelIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RowOf("Button", "TextAlign").SharesShapeWith(RowOf("TextBox", "TextAlign")), Is.False,
                "ContentAlignment × HorizontalAlignment");
            Assert.That(RowOf("TextBox", "TextAlign").SharesShapeWith(RowOf("Button", "TextAlign")), Is.False, "symmetric");
            Assert.That(RowOf("Button", "BackColor").SharesShapeWith(RowOf("Label", "BackColor")), Is.True);
            Assert.That(RowOf("Button", "Text").SharesShapeWith(RowOf("Button", "BackColor")), Is.False, "different names");
        });
    }

    /// <summary>
    /// The enum TYPE is part of the shape on its own, not only through its members: two enums can share member names
    /// (both have a <c>Normal</c>), and a value written into the other enum's property is CS0029 at csc.
    /// </summary>
    [Test]
    public void TwoEnumRows_WithTheSameMembers_ButDifferentEnumTypes_AreNotTheSameRow()
    {
        var members = new[] { "Normal", "Button" };
        var a = new FormPropertyDef("Appearance", FormPropertyType.Enum, "Normal", members, WinFormsEnumType: "Appearance");
        var b = a with { WinFormsEnumType = "TabAppearance" };

        Assert.Multiple(() =>
        {
            Assert.That(a.SharesShapeWith(b), Is.False);
            Assert.That(a.SharesShapeWith(a with { Default = "Button" }), Is.True, "the default is not part of the shape");
            Assert.That(a.SharesShapeWith(a with { AllowedValues = new[] { "Button", "Normal" } }), Is.False,
                "the member ORDER is (the drop-down lists it)");
        });
    }

    [Test]
    public void SharesShapeWith_IsReflexive_ForEveryRow()
    {
        var notReflexive = FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .SelectMany(d => d.Properties.Where(p => !p.SharesShapeWith(p)).Select(p => $"{d.Kind}.{p.Name}"))
            .ToList();
        Assert.That(notReflexive, Is.Empty);
    }
}
