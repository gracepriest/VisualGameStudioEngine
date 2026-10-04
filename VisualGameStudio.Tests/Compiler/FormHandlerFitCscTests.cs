using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 5 D-4: <see cref="FormHandlers.Fits"/> (and its <see cref="FormEvents.ArgsBases"/> table) falsified EXHAUSTIVELY
/// against csc. Per WinForms kind and the Form: ONE C# file declaring a handler <c>H(object, T)</c> for every args type
/// <c>T</c> the catalog names (plus <c>EventArgs</c> and <c>CancelEventArgs</c>), and one line <c>c.E += H;</c> per
/// (event, T). Each line's verdict is read off the diagnostics' line numbers; <c>Fits</c> must agree in BOTH directions —
/// a missing base and an invented one both fail.
///
/// <para>⚠ FAST, not Integration: in-process Roslyn over the WindowsDesktop reference PACKAGE (<see cref="WinFormsCompile"/>),
/// so it runs on Linux too. Type names come from the snapshot's <c>argsFullName</c>, never a hand list.</para>
/// </summary>
[TestFixture]
public class FormHandlerFitCscTests
{
    private static IEnumerable<FormControlDef> WinFormsDefinitions() =>
        FormControlCatalog.All.Append(FormControlCatalog.FormRoot).Where(d => d.SupportsTarget(FormTarget.WinForms));

    private static IEnumerable<TestCaseData> EveryWinFormsKind() =>
        WinFormsDefinitions().Where(d => d.Events is { Count: > 0 })
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    /// <summary>EventArgs, CancelEventArgs, and every args type any catalog event names — FULL names, from the snapshot.</summary>
    private static readonly Lazy<IReadOnlyList<string>> ArgsTypes = new(() =>
    {
        var snapshot = WinFormsMetadata.Load();
        return WinFormsDefinitions()
            .SelectMany(d => (d.Events ?? Array.Empty<FormEventDef>())
                .Select(e => snapshot.Type(d.Kind)?.Event(e.Name)?.ArgsFullName))
            .OfType<string>()
            .Concat(new[] { "System.EventArgs", "System.ComponentModel.CancelEventArgs" })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    });

    [Test]
    public void TheArgsTypeSet_IsRich() =>
        Assert.That(ArgsTypes.Value.Count, Is.GreaterThan(25), string.Join(", ", ArgsTypes.Value));

    [TestCaseSource(nameof(EveryWinFormsKind))]
    public void Fits_AgreesWithCsc_ForEveryEventAndEveryArgsType(string kind)
    {
        var definition = kind == FormControlCatalog.FormRoot.Kind ? FormControlCatalog.FormRoot : FormControlCatalog.Find(kind)!;
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F" };
        var owner = definition == FormControlCatalog.FormRoot
            ? new FormBindOwner(form)
            : new FormBindOwner(form, new FormControl { Kind = kind, Id = "c" });
        var type = definition.WinFormsType!.Contains('.') ? definition.WinFormsType : "System.Windows.Forms." + definition.WinFormsType;
        var types = ArgsTypes.Value;

        var source = new StringBuilder();
        var line = 1;
        void Line(string text) { source.Append(text).Append('\n'); line++; }

        Line("class Probe");
        Line("{");
        for (var i = 0; i < types.Count; i++)
        {
            Line($"    static void H{i}(object sender, {types[i]} e) {{ }}");
        }

        Line($"    static void Wire({type} c)");
        Line("    {");
        var cells = new List<(int Line, FormEventDef Event, string Args, bool Fits)>();
        foreach (var evt in FormEvents.WiredOn(definition, FormTarget.WinForms))
        {
            for (var i = 0; i < types.Count; i++)
            {
                var sub = new FormDeclaredSub("H", 1, new[]
                {
                    new FormCodeParameter("sender", "Object"), new FormCodeParameter("e", types[i])
                });
                cells.Add((line, evt, types[i], FormHandlers.Fits(owner, evt, FormTarget.WinForms, sub)));
                Line($"        c.{evt.Name} += H{i};");
            }
        }

        Line("    }");
        Line("}");

        var watch = Stopwatch.StartNew();
        var errors = WinFormsCompile.Errors(source.ToString());
        TestContext.WriteLine($"{kind}: {cells.Count} cells, csc {watch.ElapsedMilliseconds} ms");

        var failing = errors
            .Select(e => Regex.Match(e, @"^\((\d+),"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();
        Assert.That(errors.Where(e => !Regex.IsMatch(e, @"^\(\d+,")), Is.Empty, "an error with no line: " + string.Join("\n", errors));
        Assert.That(failing.All(l => cells.Any(c => c.Line == l)), Is.True,
            "csc refused a line that is not a wiring cell (a declaration?):\n" + string.Join("\n", errors.Take(10)));

        var disagreements = cells
            .Where(c => c.Fits == failing.Contains(c.Line))
            .Select(c => $"{kind}.{c.Event.Name} with {c.Args}: Fits says {c.Fits}, csc says {!failing.Contains(c.Line)}")
            .ToList();

        Assert.That(disagreements, Is.Empty, string.Join("\n", disagreements));
    }
}
