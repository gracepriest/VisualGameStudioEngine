using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-25 §2.6/§8 — the catalog against the REAL WinForms, measured by reflection.
///
/// <para>⛔ The snapshot is the ORACLE, never the author (D4). Nothing reads it at build time; the
/// catalog stays hand-written. What this fixture adds is the one check csc cannot make: csc says a
/// property EXISTS, never that its default, category or description is what WinForms says — and
/// spec §2.7 makes Default user-visible, so a wrong one is a grid that shows one value while the
/// program runs another.</para>
///
/// <para>⛔ Fast subset on purpose: it reads a JSON file, so it runs on Linux too.</para>
/// </summary>
[TestFixture]
public class WinFormsCatalogParityTests
{
    [Test]
    public void TheSnapshot_CoversEveryWinFormsKindInTheCatalog_AndTheForm()
    {
        var snapshot = WinFormsMetadata.Load();
        var have = snapshot.Types.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        // FormRoot is kept OUT of All, so the Form's kind is appended by name.
        var missing = FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.WinForms))
            .Select(d => d.Kind)
            .Append(FormControlCatalog.FormRoot.Kind)
            .Where(k => !have.Contains(k))
            .ToList();

        Assert.That(missing, Is.Empty,
            "these kinds are not in winforms-metadata.json, so nothing checks their rows against WinForms. " +
            "Add them to tools/WinFormsMetadataDump/Program.cs's Types table and regenerate (see its README): " +
            string.Join(", ", missing));
    }

    [Test]
    public void EverySnapshotDefaultKind_IsInTheLoadersVocabulary()
    {
        // ⛔ A kind the loader does not know would reach Task 8's comparer as "unknown" — and a tool
        // change that invents one must update README + loader together, not silently.
        var unknown = WinFormsMetadata.Load().Types
            .SelectMany(t => t.Properties.Select(p => (Where: $"{t.Name}.{p.Name}", p.DefaultKind)))
            .Where(x => !WinFormsPropertyEntry.DefaultKinds.Contains(x.DefaultKind))
            .Select(x => $"{x.Where}='{x.DefaultKind}'")
            .ToList();

        Assert.That(unknown, Is.Empty,
            "defaultKind values outside WinFormsPropertyEntry.DefaultKinds (tools/WinFormsMetadataDump/README.md): " +
            string.Join(", ", unknown));
    }

    private static IEnumerable<TestCaseData> EveryWinFormsKind() =>
        FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.WinForms))
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    private static FormControlDef DefinitionOf(string kind) =>
        kind == FormControlCatalog.FormRoot.Kind ? FormControlCatalog.FormRoot : FormControlCatalog.Find(kind)!;

    private static void AssertParity(string kind)
    {
        var definition = DefinitionOf(kind);
        var snap = WinFormsMetadata.Load().Type(kind);
        Assert.That(snap, Is.Not.Null, $"'{kind}' is not in the snapshot — see TheSnapshot_Covers…");

        var findings = definition.Properties
            .Where(p => p.AppliesTo(FormTarget.WinForms))
            .SelectMany(p => CatalogParity.CompareProperty(kind, p, snap!.Property(p.Name)))
            .Concat((definition.Events ?? Array.Empty<FormEventDef>())
                .SelectMany(e => CatalogParity.CompareEvent(kind, e, snap!.Event(e.Name))))
            .ToList();

        var exempt = definition.Properties.Where(p => p.OracleExemption != null)
            .Select(p => $"{kind}.{p.Name}: EXEMPT — {p.OracleExemption}")
            .Concat((definition.Events ?? Array.Empty<FormEventDef>()).Where(e => e.OracleExemption != null)
                .Select(e => $"{kind}.{e.Name} (event): EXEMPT — {e.OracleExemption}"));
        foreach (var line in exempt)
        {
            TestContext.WriteLine(line);
        }

        Assert.That(findings, Is.Empty,
            $"'{kind}' disagrees with WinForms (the grid would show one thing while the program runs another):\n" +
            string.Join("\n", findings));
    }

    [TestCaseSource(nameof(EveryWinFormsKind))]
    public void EveryWinFormsRowAndEvent_MatchesTheSnapshot(string kind) => AssertParity(kind);

    [Test]
    public void TheFormRoot_MatchesTheSnapshotsForm() => AssertParity(FormControlCatalog.FormRoot.Kind);

    /// <summary>Cheap, fast, and catches a misspelled enum member with no csc at all.</summary>
    [Test]
    public void EveryEnumMemberTheCatalogOffers_IsAMemberOfTheReflectedEnum()
    {
        var snapshot = WinFormsMetadata.Load();
        var offenders = new List<string>();

        foreach (var definition in FormControlCatalog.All.Where(d => d.SupportsTarget(FormTarget.WinForms))
                     .Append(FormControlCatalog.FormRoot))
        {
            foreach (var row in definition.Properties.Where(p => p.Type == FormPropertyType.Enum &&
                                                                 p.AppliesTo(FormTarget.WinForms) &&
                                                                 p.OracleExemption == null))
            {
                var members = snapshot.Type(definition.Kind)?.Property(row.Name)?.EnumMembers;
                if (members == null)
                {
                    continue; // the per-kind parity test names a missing property
                }

                offenders.AddRange(row.AllowedValues!.Where(v => !members.Contains(v, StringComparer.Ordinal))
                    .Select(v => $"{definition.Kind}.{row.Name} = {v}"));
                offenders.AddRange((row.Aliases?.Values ?? Enumerable.Empty<string>())
                    .Where(v => !row.AllowedValues!.Contains(v, StringComparer.Ordinal))
                    .Select(v => $"{definition.Kind}.{row.Name}: alias target {v} is not an allowed value"));
            }
        }

        Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
    }

    /// <summary>Every row — web-only and FormRoot included — declares a Category and a Description.</summary>
    [Test]
    public void EveryRowAndEvent_DeclaresACategoryAndADescription()
    {
        var missing = FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .SelectMany(d => d.Properties
                .Where(p => p.Category == null || string.IsNullOrWhiteSpace(p.Description))
                .Select(p => $"{d.Kind}.{p.Name}")
                .Concat((d.Events ?? Array.Empty<FormEventDef>())
                    .Where(e => e.Category == null || string.IsNullOrWhiteSpace(e.Description))
                    .Select(e => $"{d.Kind}.{e.Name} (event)")))
            .ToList();

        Assert.That(missing, Is.Empty, "the grid's category header and description pane read these:\n" +
                                       string.Join("\n", missing));
    }

    /// <summary>⛔ Proves the instrument before trusting it: the spec's own known disagreement must be caught.</summary>
    [Test]
    public void TheInstrument_CatchesTheKnownGripStyleDisagreement()
    {
        var snap = WinFormsMetadata.Load().Type("ToolStrip")!.Property("GripStyle")!;
        var wrong = new FormPropertyDef("GripStyle", FormPropertyType.Enum, "Hidden", new[] { "Hidden", "Visible" },
            WinFormsEnumType: "ToolStripGripStyle", Category: FormPropertyCategory.Appearance,
            Description: snap.Description.Trim());

        Assert.That(CatalogParity.CompareProperty("ToolStrip", wrong, snap).ToList(),
            Has.Some.Contains("Default is 'Hidden'"),
            "if this passes silently, every parity row above reports success about nothing");
    }
}
