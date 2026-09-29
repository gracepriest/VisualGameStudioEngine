using System.Globalization;
using BasicLang.Forms;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Compares one catalog row with its WinForms snapshot entry and says, in paste-ready terms, what
/// differs. The COMPARISON RULE for defaults (spec §2.7, plan Task 8):
///
///   attribute / reset         → the row's Default must EQUAL the snapshot's (type-aware);
///   ambient / volatile /
///   serialized / collection   → the row's Default must be NULL ("no static default");
///   unreadable                → the value was never measured — the row needs an OracleExemption.
///
/// An OracleExemption (its reason is printed by the caller) covers exactly what a snapshot can get
/// WRONG for a row: a missing entry (a [Browsable(false)] property), the Default (a parent-dependent or
/// unmeasurable read) and the Description (a type with no metadata). It never covers the Type or the
/// Category — when the snapshot HAS the property those are facts about the real type, a wrong one is a
/// row defect no reason excuses, and csc cannot see a Category at all. An exemption that suppresses
/// nothing is stale (WinFormsCatalogParityTests.EveryOracleExemption_StillSuppressesAFinding).
/// Only the WinForms Default is compared — WebDefault has no oracle.
/// </summary>
internal static class CatalogParity
{
    public static IEnumerable<string> CompareProperty(string kind, FormPropertyDef row, WinFormsPropertyEntry? snap)
    {
        var exempt = row.OracleExemption != null;

        if (snap == null)
        {
            if (!exempt)
            {
                yield return $"{kind}.{row.Name}: WinForms has no browsable property of that name — a misspelled " +
                             "row compiles green through BasicLang — OR a real [Browsable(false)] property. " +
                             "Fix the name, or give the row an OracleExemption with a reason.";
            }

            yield break;
        }

        var category = ParsePropertyCategory(snap.Category);
        if (category == null)
        {
            yield return $"{kind}.{row.Name}: the snapshot's category '{snap.Category}' has no FormPropertyCategory member — add one.";
        }
        else if (row.Category != category)
        {
            yield return $"{kind}.{row.Name}: Category is {row.Category?.ToString() ?? "null"}, WinForms says " +
                         $"{snap.Category} → Category: FormPropertyCategory.{category}";
        }

        if (!TypeFits(kind, row, snap))
        {
            yield return $"{kind}.{row.Name}: declared {row.Type}" +
                         (row.WinFormsEnumType != null ? $" ({row.WinFormsEnumType})" : "") +
                         $", WinForms' type is {snap.TypeFullName}. Fix the row's type — an exemption does not " +
                         "cover a type the snapshot measured.";
        }

        if (exempt)
        {
            yield break;
        }

        if (CompareDefault(row, snap) is { } defaultFinding)
        {
            yield return $"{kind}.{row.Name}: {defaultFinding}";
        }

        if (!string.Equals((row.Description ?? "").Trim(), snap.Description.Trim(), StringComparison.Ordinal))
        {
            yield return $"{kind}.{row.Name}: Description differs → Description: \"{Escape(snap.Description.Trim())}\"";
        }
    }

    /// <summary>
    /// The event twin of <see cref="CompareProperty"/>: an exemption covers a missing entry (GroupBox.Click
    /// is [Browsable(false)]) and the Description (BackgroundWorker has no metadata on .NET) — never the
    /// handler args or the Category, which are facts about the real event.
    /// </summary>
    public static IEnumerable<string> CompareEvent(string kind, FormEventDef evt, WinFormsEventEntry? snap)
    {
        var exempt = evt.OracleExemption != null;

        if (snap == null)
        {
            if (!exempt)
            {
                yield return $"{kind}.{evt.Name} (event): WinForms has no browsable event of that name — a " +
                             "misspelled event compiles green through BasicLang — OR a real [Browsable(false)] event. " +
                             "Fix the name, or give the event an OracleExemption with a reason.";
            }

            yield break;
        }

        var args = evt.WinFormsArgs ?? "EventArgs";
        var lastSegment = args.Contains('.') ? args[(args.LastIndexOf('.') + 1)..] : args;
        if (!string.Equals(lastSegment, snap.ArgsType, StringComparison.Ordinal))
        {
            yield return $"{kind}.{evt.Name} (event): handler args are {args}, WinForms' are {snap.ArgsFullName} " +
                         $"→ args: \"{snap.ArgsType}\" (qualify it if its namespace is not System.Windows.Forms). " +
                         "Fix the args — an exemption does not cover args the snapshot measured.";
        }

        var category = ParseEventCategory(snap.Category);
        if (category == null)
        {
            yield return $"{kind}.{evt.Name} (event): the snapshot's category '{snap.Category}' has no FormEventCategory member — add one.";
        }
        else if (evt.Category != category)
        {
            yield return $"{kind}.{evt.Name} (event): Category is {evt.Category?.ToString() ?? "null"} → category: FormEventCategory.{category}";
        }

        if (!exempt && !string.Equals((evt.Description ?? "").Trim(), snap.Description.Trim(), StringComparison.Ordinal))
        {
            yield return $"{kind}.{evt.Name} (event): Description differs → description: \"{Escape(snap.Description.Trim())}\"";
        }
    }

    public static FormPropertyCategory? ParsePropertyCategory(string snapshotCategory) =>
        Enum.TryParse<FormPropertyCategory>(snapshotCategory.Replace(" ", ""), ignoreCase: false, out var c) ? c : null;

    public static FormEventCategory? ParseEventCategory(string snapshotCategory) =>
        Enum.TryParse<FormEventCategory>(snapshotCategory.Replace(" ", ""), ignoreCase: false, out var c) ? c : null;

    private static bool TypeFits(string kind, FormPropertyDef row, WinFormsPropertyEntry s) => row.Type switch
    {
        FormPropertyType.String when row.IsItemCollection => s.IsCollection,
        FormPropertyType.String when row.WinFormsFactory == "Convert.ToChar" => s.Type == "Char",
        FormPropertyType.String when row.WinFormsFactory == "Image.FromFile" => s.Type == "Image",
        FormPropertyType.String => s.Type == "String",
        // ⚠ Decimal ONLY for NumericUpDown's Minimum/Maximum/Value/Increment — the deliberate Int-over-Decimal
        // rows (see their comment in FormControlCatalog). Anywhere else it would be a shape error csc
        // cannot see, because an int literal widens to decimal.
        FormPropertyType.Int => s.Type == "Int32" ||
                                (s.Type == "Decimal" && kind == "NumericUpDown" &&
                                 row.Name is "Minimum" or "Maximum" or "Value" or "Increment"),
        FormPropertyType.Bool => s.Type == "Boolean",
        FormPropertyType.Color => s.Type == "Color",
        FormPropertyType.Size => s.Type == "Size",
        FormPropertyType.Enum => s.IsEnum && row.WinFormsEnumType != null &&
                                 string.Equals(LastSegment(row.WinFormsEnumType), s.Type, StringComparison.Ordinal),
        _ => false
    };

    private static string? CompareDefault(FormPropertyDef row, WinFormsPropertyEntry s)
    {
        switch (s.DefaultKind)
        {
            case "ambient":
            case "volatile":
            case "serialized":
            case "collection":
                return row.Default == null
                    ? null
                    : $"Default is '{row.Default}', but WinForms has no static default ({s.DefaultKind}) → Default: null";

            case "attribute":
            case "reset":
                return SameDefault(row, row.Default, s.Default)
                    ? null
                    : $"Default is '{row.Default ?? "null"}', WinForms says '{s.Default ?? "null"}' ({s.DefaultKind}) → " +
                      $"Default: {(s.Default == null ? "null" : $"\"{s.Default}\"")}";

            // ⚠ The tool records `unreadable` when a getter or ShouldSerializeValue threw — the value
            // was NOT measured, so no comparison can be honest.
            case "unreadable":
                return "the snapshot could not read this default (a getter or ShouldSerializeValue threw) — " +
                       "it was not measured; give the row an OracleExemption with the reason";

            default:
                return $"unknown defaultKind '{s.DefaultKind}' — regenerate the snapshot with the current tool";
        }
    }

    private static bool SameDefault(FormPropertyDef row, string? mine, string? theirs)
    {
        if (string.IsNullOrEmpty(mine) && string.IsNullOrEmpty(theirs))
        {
            return true;
        }

        if (mine == null || theirs == null)
        {
            return false;
        }

        return row.Type switch
        {
            FormPropertyType.Bool => bool.TryParse(mine, out var a) && bool.TryParse(theirs, out var b) && a == b,
            FormPropertyType.Int =>
                decimal.TryParse(mine, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) &&
                decimal.TryParse(theirs, NumberStyles.Number, CultureInfo.InvariantCulture, out var y) && x == y,
            FormPropertyType.Enum => EnumSet(row.Canonical(mine)).SetEquals(EnumSet(theirs)),
            FormPropertyType.Color => string.Equals(NormalColor(mine), NormalColor(theirs), StringComparison.OrdinalIgnoreCase),
            FormPropertyType.Size => FormPropertyDef.TryParseSize(mine, out var w1, out var h1) &&
                                     FormPropertyDef.TryParseSize(theirs, out var w2, out var h2) && (w1, h1) == (w2, h2),
            _ => string.Equals(mine, theirs, StringComparison.Ordinal)
        };
    }

    private static HashSet<string> EnumSet(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>#rgb / #rrggbb / #aarrggbb → #AARRGGBB; a name as-is.</summary>
    private static string NormalColor(string value)
    {
        if (value.Length == 0 || value[0] != '#')
        {
            return value;
        }

        var digits = value[1..];
        if (digits.Length == 3) digits = string.Concat(digits.Select(c => new string(c, 2)));
        if (digits.Length == 6) digits = "FF" + digits;
        return "#" + digits.ToUpperInvariant();
    }

    private static string LastSegment(string name) => name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
