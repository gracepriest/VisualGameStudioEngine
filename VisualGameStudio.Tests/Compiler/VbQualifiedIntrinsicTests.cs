using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Portable-controls Task 7c review: VB's qualified spellings of a built-in — <c>Strings.Left(…)</c>,
/// <c>Microsoft.VisualBasic.Left(…)</c>, <c>Microsoft.VisualBasic.Strings.Left(…)</c> — for EVERY built-in
/// BasicLang lowers that a <c>Microsoft.VisualBasic</c> module declares with the same shape
/// (<c>VbIntrinsicQualifiers</c>). Front end only here; <c>CrossFileBindingTests</c> RUNS them on three backends.
/// </summary>
[TestFixture]
public class VbQualifiedIntrinsicTests
{
    private static string Messages(string source)
    {
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join(" | ", analyzer.Errors.Select(e => e.Message));
    }

    /// <summary>Every built-in function, as the analyzer registers them (on analysis).</summary>
    private static IReadOnlyCollection<Symbol> BuiltIns()
    {
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(new Parser(new Lexer("Sub Main()\nEnd Sub\n").Tokenize()).Parse());
        return analyzer.StdLibSymbolsForTest;
    }

    /// <summary>The built-ins with a VB-qualified spelling, by module — computed, never hand-listed.</summary>
    private static IEnumerable<(Symbol Intrinsic, string Module)> Qualifiable() =>
        BuiltIns()
            .Select(s => (s, VbIntrinsicQualifiers.ModuleOf(s)))
            .Where(p => p.Item2 != null)
            .OrderBy(p => p.s.Name);

    /// <summary>Guards the guard: the enumeration is not vacuous.</summary>
    [Test]
    public void TheBuiltInTable_IsNotEmpty() => Assert.That(BuiltIns(), Has.Count.GreaterThan(50));

    /// <summary>
    /// ⚠ A PIN, so a change to the built-in table or to the shape rule is a visible diff. Every name here is
    /// one VB declares with BasicLang's signature; a name that drops out (or appears) must be looked at.
    /// </summary>
    [Test]
    public void TheQualifiableBuiltIns_AreTheOnesVbDeclaresWithTheSameShape() =>
        Assert.That(Qualifiable().Select(p => $"{p.Module}.{p.Intrinsic.Name}"), Is.EquivalentTo(new[]
        {
            "Strings.Len", "Strings.Mid", "Strings.Left", "Strings.Right", "Strings.UCase", "Strings.LCase",
            "Strings.Trim", "Strings.InStr", "Strings.Replace", "Conversion.Str", "Conversion.Val",
            "Interaction.Beep", "Information.LBound", "Information.UBound", "VBMath.Randomize",
            "DateAndTime.Year", "DateAndTime.Month", "DateAndTime.Day", "DateAndTime.Hour", "DateAndTime.Minute",
            "DateAndTime.Second", "FileSystem.FileCopy", "FileSystem.FileLen",
        }), "printed for review: " + string.Join(", ", Qualifiable().Select(p => $"{p.Module}.{p.Intrinsic.Name}")));

    /// <summary>Every qualifiable built-in compiles under all three of VB's spellings, from a class that has a
    /// member of the same name — where the bare spelling names the member.</summary>
    [Test]
    public void EveryQualifiableBuiltIn_CompilesUnderEachQualifiedSpelling_InsideAClassThatHidesIt()
    {
        var failures = new List<string>();
        foreach (var (intrinsic, module) in Qualifiable())
        {
            var args = string.Join(", ", intrinsic.Parameters.Select(p => SampleArgument(p.Type?.Name)));
            foreach (var qualifier in new[] { module, "Microsoft.VisualBasic", "Microsoft.VisualBasic." + module })
            {
                var call = $"{qualifier}.{intrinsic.Name}({args})";
                // TYPED, so an unbound call — typed Object by the permissive .NET arm — is refused, not accepted.
                var statement = intrinsic.Kind == SymbolKind.Subroutine
                    ? $"  {call}\n"
                    : $"  Dim r As {intrinsic.ReturnType.Name} = {call}\n";
                var source = $"Public Class C\n Public {intrinsic.Name} As Integer\n Public Sub Go()\n{statement} End Sub\nEnd Class\n" +
                             "Sub Main()\nEnd Sub\n";
                var messages = Messages(source);
                if (messages.Length > 0) failures.Add($"{call}: {messages}");
            }
        }
        Assert.That(failures, Is.Empty);
    }

    private static string SampleArgument(string typeName) => typeName switch
    {
        "String" => "\"abc\"",
        "Integer" or "Long" or "Short" or "Byte" => "1",
        "Double" or "Single" or "Decimal" => "1.5",
        "Boolean" => "True",
        "DateTime" or "Date" => "Now()",
        _ => "\"1\""
    };

    /// <summary>A conversion is a VB OPERATOR, not a module function: <c>Microsoft.VisualBasic.CStr</c> is no
    /// spelling VB has, and is not made one here.</summary>
    [Test]
    public void AConversionOperator_HasNoQualifiedSpelling() =>
        Assert.That(BuiltIns().Where(s => s.Name is "CStr" or "CInt" or "CDbl")
            .Select(VbIntrinsicQualifiers.ModuleOf), Is.All.Null);

    /// <summary>Shapes alone are not enough: <c>Shell</c> matches VB's shape but returns the EXIT CODE where VB's returns
    /// the process id, and <c>Print</c> must not match VB's <c>FileSystem.Print(FileNumber, …)</c>.</summary>
    [Test]
    public void ABuiltInThatMeansSomethingElseInVb_HasNoQualifiedSpelling() =>
        Assert.That(BuiltIns().Where(s => s.Name is "Shell" or "Print" or "PrintLine").Select(VbIntrinsicQualifiers.ModuleOf),
            Is.All.Null);

    /// <summary>A user declaration named like the qualifier keeps its meaning — no built-in is claimed.</summary>
    [Test]
    public void AUserModuleNamedStrings_KeepsItsMeaning() =>
        Assert.That(Messages("Module Strings\n Public Function Left(s As String, n As Integer) As Integer\n  Return 7\n End Function\nEnd Module\n" +
                             "Sub Main()\n Dim n As Integer = Strings.Left(\"hello\", 2)\nEnd Sub\n"), Is.Empty);

    /// <summary>Bare <c>Left(…)</c> inside a class with an Integer <c>Left</c> member is VB's BC30471 — and the
    /// message names VB's way out.</summary>
    [Test]
    public void ABareBuiltIn_HiddenByAScalarMember_IsBC30471_WithTheQualifiedSpellingAsTheHint() =>
        Assert.That(Messages("Public Class Ctl\n Public Property Left As Integer\n Public Sub Go()\n  Dim s As String = Left(\"hello\", 2)\n End Sub\nEnd Class\n" +
                             "Sub Main()\nEnd Sub\n"),
            Does.Contain("BC30471").And.Contain("use Strings.Left(…)"));
}
