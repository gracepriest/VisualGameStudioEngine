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

        // ⚠ Plan Task 6 Step 9 restores `.Append(FormControlCatalog.FormRoot.Kind)` here once
        // FormRoot exists; until then the Form's kind is spelled literally.
        var missing = FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.WinForms))
            .Select(d => d.Kind)
            .Append("Form")
            .Where(k => !have.Contains(k))
            .ToList();

        Assert.That(missing, Is.Empty,
            "these kinds are not in winforms-metadata.json, so nothing checks their rows against WinForms. " +
            "Add them to tools/WinFormsMetadataDump/Program.cs's Types table and regenerate (see its README): " +
            string.Join(", ", missing));
    }
}
