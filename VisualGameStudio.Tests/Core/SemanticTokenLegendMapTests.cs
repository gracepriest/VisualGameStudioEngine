using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

/// <summary>
/// Pins <see cref="SemanticTokenLegendMap"/> — the duplicate-tolerant remap from a
/// foreign server's semantic-token legend onto BasicLang's canonical client-side legend.
/// The centerpiece is the MEASURED clangd 22.1.6 legend (Phase 3b Step 0.2 fixture,
/// verbatim — duplicates and all) and its expected remap table, which was independently
/// recomputed twice during plan review. These tests are the executable form of that table.
/// </summary>
[TestFixture]
public class SemanticTokenLegendMapTests
{
    // MEASURED clangd 22.1.6 legend (S0.2 fixture — verbatim; "variable" at [0], [1], [7],
    // "type" at [12], [13], [18], "function" at [3], [5] — positional wire tables, not sets).
    private static readonly string[] MeasuredClangdTokenTypes =
    {
        "variable", "variable", "parameter", "function", "method", "function", "property",
        "variable", "class", "interface", "enum", "enumMember", "type", "type", "unknown",
        "namespace", "typeParameter", "concept", "type", "macro", "modifier", "operator",
        "bracket", "label", "comment"
    };

    private static readonly string[] MeasuredClangdTokenModifiers =
    {
        "declaration", "definition", "deprecated", "deduced", "readonly", "static",
        "abstract", "virtual", "dependentName", "defaultLibrary", "usedAsMutableReference",
        "usedAsMutablePointer", "constructorOrDestructor", "userDefined", "functionScope",
        "classScope", "fileScope", "globalScope"
    };

    // BasicLang's own legend, exactly as SemanticTokensHandler.cs registers it (wire names).
    private static readonly string[] BasicLangTokenTypes =
    {
        "namespace", "type", "class", "enum", "interface", "struct", "typeParameter",
        "parameter", "variable", "property", "enumMember", "function", "method", "keyword",
        "modifier", "comment", "string", "number", "operator"
    };

    private static readonly string[] BasicLangTokenModifiers =
    {
        "declaration", "definition", "readonly", "static", "deprecated", "abstract",
        "async", "modification", "documentation", "defaultLibrary"
    };

    private static SemanticTokensLegend ClangdLegend() =>
        new(MeasuredClangdTokenTypes, MeasuredClangdTokenModifiers);

    private static SemanticTokensLegend BasicLangLegend() =>
        new(BasicLangTokenTypes, BasicLangTokenModifiers);

    [Test]
    public void Build_FromTheMeasuredClangdLegend_ProducesTheExactTable()
    {
        var map = SemanticTokenLegendMap.Build(ClangdLegend());

        // clangd idx -> BasicLang canonical idx; -1 = no canonical slot (render uncolored):
        // unknown(14), concept(17), macro(19), bracket(22), label(23).
        var expected = new[]
        {
            8, 8, 7, 11, 12, 11, 9, 8, 2, 4, 3, 10, 1, 1, -1, 0, 6, -1, 1, -1, 14, 18, -1, -1, 15
        };

        Assert.That(map.TokenTypeMap, Is.EqualTo(expected).AsCollection);
    }

    [Test]
    public void Build_FromBasicLangsOwnLegend_ReturnsIdentity()
    {
        var map = SemanticTokenLegendMap.Build(BasicLangLegend());

        // Reference-equal, not merely behaviorally identity: BasicLang's path must
        // allocate nothing per fetch.
        Assert.That(map, Is.SameAs(SemanticTokenLegendMap.Identity));
    }

    [Test]
    public void Build_DuplicateNames_MapToTheSameSlot_NoThrow()
    {
        // The Dictionary.Add landmine, pinned: real clangd repeats names ("variable" x3,
        // "type" x3, "function" x2). Same name -> same canonical slot, no exception.
        var legend = new SemanticTokensLegend(
            new[] { "variable", "variable", "type", "variable" },
            new[] { "declaration", "declaration" });

        SemanticTokenLegendMap map = null!;
        Assert.DoesNotThrow(() => map = SemanticTokenLegendMap.Build(legend));

        Assert.That(map.TokenTypeMap, Is.EqualTo(new[] { 8, 8, 1, 8 }).AsCollection);

        // Duplicate modifier names collapse onto the same canonical bit.
        var remapped = map.RemapData(new[] { 0, 0, 1, 0, (1 << 0) | (1 << 1) });
        Assert.That(remapped[4], Is.EqualTo(1 << 0));
    }

    [Test]
    public void Build_NameMatching_IsCaseInsensitive()
    {
        // LSP names arrive camelCase ("enumMember"); matching is case-insensitive ordinal
        // so a server casing its names differently still lands on the canonical slots.
        var legend = new SemanticTokensLegend(
            new[] { "Variable", "ENUMMEMBER", "typeparameter" },
            new[] { "Readonly" });

        var map = SemanticTokenLegendMap.Build(legend);

        Assert.That(map.TokenTypeMap, Is.EqualTo(new[] { 8, 10, 6 }).AsCollection);

        var remapped = map.RemapData(new[] { 0, 0, 1, 0, 1 << 0 });
        Assert.That(remapped[4], Is.EqualTo(1 << 2)); // canonical Readonly bit
    }

    [Test]
    public void Build_FromAnEmptyLegend_RemapsEverythingToUncolored()
    {
        // Empty != null: a server sending "tokenTypes":[] yields a NON-NULL legend with
        // zero entries. Every type index in the data is then out of range -> the 999
        // sentinel; every modifier bit is undeclared -> masked out.
        var map = SemanticTokenLegendMap.Build(new SemanticTokensLegend(
            Array.Empty<string>(), Array.Empty<string>()));

        Assert.That(map, Is.Not.SameAs(SemanticTokenLegendMap.Identity));
        Assert.That(map.TokenTypeMap, Is.Empty);

        var result = map.RemapData(new[] { 0, 0, 3, 0, (1 << 0) | (1 << 4), 1, 2, 4, 8, 1 << 2 });

        Assert.That(result, Is.EqualTo(new[] { 0, 0, 3, 999, 0, 1, 2, 4, 999, 0 }).AsCollection);
    }

    [Test]
    public void RemapData_RewritesTypeIndicesAndModifierBits()
    {
        var map = SemanticTokenLegendMap.Build(ClangdLegend());

        // LSP quintuples [deltaLine, deltaStartChar, length, tokenType, tokenModifiers]*.
        var input = new[]
        {
            0, 4, 6, 8, 1 << 2,                       // clangd class, clangd deprecated (bit 2)
            1, 2, 5, 4, 1 << 4,                       // clangd method, clangd readonly (bit 4)
            0, 9, 3, 15, (1 << 0) | (1 << 5) | (1 << 9) // clangd namespace, declaration|static|defaultLibrary
        };
        var pristine = (int[])input.Clone();

        var result = map.RemapData(input);

        Assert.That(result, Is.Not.SameAs(input), "non-identity remap must return a rewritten copy");
        Assert.That(input, Is.EqualTo(pristine).AsCollection, "the input array must not be mutated");
        Assert.That(result, Is.EqualTo(new[]
        {
            0, 4, 6, 2, 1 << 4,                       // class -> 2; deprecated -> canonical bit 4
            1, 2, 5, 12, 1 << 2,                      // method -> 12; readonly -> canonical bit 2
            0, 9, 3, 0, (1 << 0) | (1 << 3) | (1 << 9) // namespace -> 0; declaration stays 0, static -> 3, defaultLibrary stays 9
        }).AsCollection);
    }

    [Test]
    public void RemapData_ClangdReadonly_DoesNotBecomeDeprecated()
    {
        // The const-strikethrough bug, pinned by name: clangd's readonly is bit 4, but
        // canonical bit 4 is Deprecated — passed through unremapped, every `const` in a
        // C++ file renders struck-through (the highlighter's deprecated test is bit 4).
        var map = SemanticTokenLegendMap.Build(ClangdLegend());

        var result = map.RemapData(new[] { 0, 0, 5, 0, 1 << 4 });

        Assert.That(result[4], Is.EqualTo(1 << 2),
            "clangd readonly (bit 4) must land on canonical Readonly (bit 2)");
        Assert.That(result[4] & (1 << 4), Is.Zero,
            "clangd readonly must NOT land on canonical Deprecated (bit 4)");
    }

    [Test]
    public void RemapData_UnknownType_BecomesThe999Sentinel()
    {
        // 999 EXACTLY: any value >= 19 hits the highlighter's null-brush default arm, and
        // the absurd magnitude keeps anyone from "tidying" it into a meaningful index.
        Assert.That(SemanticTokenLegendMap.UncoloredIndex, Is.EqualTo(999));

        var map = SemanticTokenLegendMap.Build(ClangdLegend());

        var result = map.RemapData(new[]
        {
            0, 0, 2, 14, 0,  // clangd "unknown" -> no canonical slot
            0, 3, 2, 17, 0,  // clangd "concept" -> no canonical slot
            0, 6, 1, 60, 0   // index beyond the server legend entirely
        });

        Assert.That(result[3], Is.EqualTo(999));
        Assert.That(result[8], Is.EqualTo(999));
        Assert.That(result[13], Is.EqualTo(999));
    }

    [Test]
    public void RemapData_UnmappedModifierBits_AreMaskedOut()
    {
        var map = SemanticTokenLegendMap.Build(ClangdLegend());

        // deduced(3), virtual(7), dependentName(8), and the clangd-only bits 10-17
        // have no canonical slot -> masked out.
        int unmapped = (1 << 3) | (1 << 7) | (1 << 8);
        for (int bit = 10; bit <= 17; bit++)
            unmapped |= 1 << bit;

        var result = map.RemapData(new[] { 0, 0, 4, 0, unmapped });
        Assert.That(result[4], Is.Zero);

        // Mixed with a mapped bit: only the mapped bit survives, on its canonical slot.
        var mixed = map.RemapData(new[] { 0, 0, 4, 0, unmapped | (1 << 4) });
        Assert.That(mixed[4], Is.EqualTo(1 << 2));
    }

    [Test]
    public void RemapData_Identity_ReturnsTheSameArrayReference()
    {
        // Keystroke-path allocation matters: identity must hand back the input array
        // itself, not an equal copy.
        var input = new[] { 0, 0, 3, 2, 1 << 2 };

        Assert.That(SemanticTokenLegendMap.Identity.RemapData(input), Is.SameAs(input));
        Assert.That(SemanticTokenLegendMap.Build(BasicLangLegend()).RemapData(input), Is.SameAs(input));
    }

    [Test]
    public void RemapData_LengthNotMultipleOfFive_ReturnsInputUntouched()
    {
        // Mirror of SemanticTokenHighlighter.Update's guard: malformed data is not
        // half-rewritten, it is passed through for the highlighter to reject whole.
        var map = SemanticTokenLegendMap.Build(ClangdLegend());
        var input = new[] { 0, 0, 3 };

        var result = map.RemapData(input);

        Assert.That(result, Is.SameAs(input));
        Assert.That(result, Is.EqualTo(new[] { 0, 0, 3 }).AsCollection);
    }
}
