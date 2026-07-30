using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Net;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-1's central claim, made observable.
///
/// <para><b>Why "0 failed" and byte-identical stdout cannot cover this.</b> Task 8 makes the .NET
/// resolver reachable from the analyzer on both backends. The failure mode it introduces is a NEW
/// WARNING emitted on programs that compiled clean before — and a warning changes no exit code, no
/// generated C++, and no stdout, so the whole existing suite can stay green while every build in
/// the IDE grows a diagnostic line and every file in the editor grows a squiggle.</para>
///
/// <para>This fixture compiles a corpus of known-good programs — the <c>IDE/</c> console and game
/// templates plus all 13 P1 parity sources — through the REAL production path
/// (<see cref="BasicCompiler"/> with a live <see cref="NetTypeResolver"/> factory, exactly as
/// <c>CppProjectBuilder</c> wires it) and asserts no BL60xx comes out.</para>
///
/// <para><b>Keep this fixture through Tasks 9-16.</b> It is the standing guard on the plan's
/// central claim, and every later task widens what the resolver sees.</para>
/// </summary>
[TestFixture]
public class NetInertnessTests
{
    /// <summary>
    /// The production wiring: one memoized resolver over the framework closure, which is what a
    /// project that declares no &lt;Reference&gt; gets — i.e. every native project that exists
    /// today. Mirrors CppProjectBuilder's factory exactly; if that changes shape, this must too or
    /// the fixture stops testing the real path.
    /// </summary>
    private static Func<NetTypeResolver> LiveResolverFactory()
    {
        NetTypeResolver resolver = null;
        return () => resolver ??= NetTypeResolver.Create(NetReferenceResolver.FrameworkAssemblies);
    }

    private static (bool Success, IReadOnlyList<NetReferenceDiagnostic> NetDiagnostics, string Errors)
        CompileCorpusProgram(string label, IReadOnlyDictionary<string, string> files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "blnet-inert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            foreach (var file in files)
            {
                var path = Path.Combine(dir, file.Key);
                File.WriteAllText(path, file.Value);
                paths.Add(path);
            }

            var compiler = new BasicCompiler(new CompilerOptions
            {
                TargetBackend = "cpp",
                NetResolverFactory = LiveResolverFactory(),
            });
            var result = compiler.CompileProjectFiles(paths);
            return (result.Success,
                    result.NetDiagnostics.ToList(),
                    string.Join("; ", result.AllErrors.Select(e => e.Message)));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static void AssertNoNetDiagnostics(
        string label, IReadOnlyList<NetReferenceDiagnostic> diagnostics)
    {
        var codes = diagnostics.Select(d => d.Code).Where(c => c.StartsWith("BL60", StringComparison.Ordinal));

        Assert.That(codes, Is.Empty,
            $"'{label}' drew a new BL60xx diagnostic: "
            + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message))
            + ". P2a-1 must emit NO new BL60xx diagnostic on a program that compiled clean at "
            + "dfee728. A warning is still new output: it changes CLI stdout, the IDE error list, "
            + "and LSP squiggles. Fix the claim predicate or the analyzer's probe gating "
            + "(SemanticAnalyzer.ProbeNetTypeReference / ProbeNetMemberAccess) — do NOT relax this "
            + "assertion.");
    }

    // ---- The 13 P1 parity programs -------------------------------------------------------

    [TestCaseSource(typeof(BclBackendParityTests), nameof(BclBackendParityTests.ParityCases))]
    public void ParityProgramsDrawNoNetDiagnostics(BclBackendParityTests.ParityProgram program)
    {
        var (success, netDiagnostics, errors) =
            CompileCorpusProgram(program.Name, new Dictionary<string, string> { ["Main.bas"] = program.Source });

        Assert.That(success, Is.True,
            $"Parity program '{program.Name}' no longer compiles: {errors}. That is a regression in "
            + "the compiler, not in this fixture.");
        AssertNoNetDiagnostics(program.Name, netDiagnostics);
    }

    // ---- The IDE console + game templates ------------------------------------------------

    [TestCase("console")]
    [TestCase("game")]
    public void ProjectTemplatesDrawNoNetDiagnostics(string templateName)
    {
        var template = new TemplateEngine().GetTemplate(templateName);
        Assert.That(template, Is.Not.Null,
            $"TemplateEngine no longer has a '{templateName}' template. Update this fixture's "
            + "corpus to the template's new name rather than dropping the coverage.");

        var files = template.Files
            .Where(f => IsBasicLangSource(f.Key))
            .ToDictionary(f => f.Key,
                          f => f.Value.Replace("{{ProjectName}}", "InertnessProbe")
                                      .Replace("{{Date}}", "2026-07-30"));

        Assert.That(files, Is.Not.Empty,
            $"The '{templateName}' template contributed no BasicLang sources, so this case proves "
            + "nothing. Fix the source-extension filter in this fixture.");

        var (success, netDiagnostics, errors) = CompileCorpusProgram(templateName, files);

        // The BL60xx contract is asserted unconditionally — the analyzer runs to completion and
        // its .NET findings are collected even for a unit that fails for an unrelated reason, so
        // this coverage does not depend on the template compiling.
        AssertNoNetDiagnostics(templateName, netDiagnostics);

        // PRE-EXISTING, NOT introduced by P2a-1: TemplateEngine's "game" template calls
        // ClearBackground with FOUR arguments (TemplateEngine.cs:222) against the stdlib's
        // three-parameter signature, so it has never compiled. The IDE's own copy of the same
        // template (ProjectTemplateService.cs:497) passes three and is correct — the two drifted
        // and TemplateBuildSweepTests only guards the cpp- templates. Asserting success here would
        // make this fixture red for a defect it does not own; asserting the CURRENT state keeps
        // the console template honestly gated and turns the game template green the moment the
        // real bug is fixed (at which point delete this branch).
        if (templateName == "game")
        {
            Assert.That(errors, Does.Contain("ClearBackground"),
                "The 'game' template's pre-existing ClearBackground arity bug appears to be fixed. "
                + "Delete this branch and assert Success is True for every template.");
            return;
        }

        Assert.That(success, Is.True,
            $"The '{templateName}' template no longer compiles: {errors}. That is a regression in "
            + "the compiler or the template, not in this fixture.");
    }

    private static bool IsBasicLangSource(string fileName) =>
        fileName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".mod", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".cls", StringComparison.OrdinalIgnoreCase);

    // ---- Guards on the fixture itself ----------------------------------------------------

    /// <summary>
    /// A gate that cannot fail is worse than no gate. This proves the corpus really is running
    /// with .NET resolution ACTIVE — without it, every assertion above would pass vacuously
    /// because a null factory disables every probe in the analyzer.
    /// </summary>
    [Test]
    public void TheGateIsArmed_AnUnresolvableSystemTypeIsReported()
    {
        const string source = @"Module M
 Sub Main()
  Dim x As System.ThisTypeDoesNotExistAnywhere
  Console.WriteLine(""done"")
 End Sub
End Module";

        var (_, netDiagnostics, _) =
            CompileCorpusProgram("armed-probe", new Dictionary<string, string> { ["Main.bas"] = source });

        Assert.That(netDiagnostics.Select(d => d.Code), Does.Contain("BL6016"),
            "The inertness fixture is not actually exercising .NET resolution — every other "
            + "assertion in this file is therefore vacuous. Check that CompilerOptions."
            + "NetResolverFactory still reaches SemanticAnalyzer.ConfigureNetResolution.");
    }

    /// <summary>
    /// The warning-only contract, asserted directly. <c>CompilationResult.HasErrors</c> is
    /// <c>AllErrors.Count > 0</c> with NO severity filter and <c>Success = !HasErrors</c>, so a
    /// finding placed on <c>AllErrors</c> fails the build whatever severity it claims. This is why
    /// the .NET findings live on their own list.
    /// </summary>
    [Test]
    public void NetFindingsAreWarningsAndDoNotFailTheBuild()
    {
        const string source = @"Module M
 Sub Main()
  Dim x As System.ThisTypeDoesNotExistAnywhere
  Console.WriteLine(""done"")
 End Sub
End Module";

        var (success, netDiagnostics, errors) =
            CompileCorpusProgram("warning-only", new Dictionary<string, string> { ["Main.bas"] = source });

        Assert.That(success, Is.True,
            "A BL6016 turned into a FAILED build: " + errors + ". P2a-1 is warning-only — §6.3's "
            + "native-error behavior lands in P2a-2. Findings must not reach "
            + "CompilationResult.AllErrors, because HasErrors does not filter by severity.");
        Assert.That(netDiagnostics.Where(d => d.Code.StartsWith("BL60", StringComparison.Ordinal)),
            Is.All.Matches<NetReferenceDiagnostic>(d => d.IsWarning),
            "A P2a-1 .NET finding is marked as an error. Every one of them is IsWarning: true "
            + "until the P2a-2 flip.");
    }

    /// <summary>
    /// Constraint (a) from the Task 8 review: <see cref="NetTypeResolver"/> REQUIRES generic arity
    /// and never guesses, so an UNCLAIMED generic must be asked for as <c>Queue`1</c>. The claimed
    /// generics (List/Dictionary/HashSet/Task/Func/Action) never reach the resolver and so cannot
    /// expose this; <c>Queue(Of Integer)</c> can, and would draw a spurious BL6016 on a valid
    /// program if the arity mapping were missing.
    /// </summary>
    [TestCase("Queue")]
    [TestCase("Stack")]
    [TestCase("SortedDictionary")]
    [TestCase("LinkedList")]
    public void BareUnclaimedGenericsDrawNoDiagnostic(string typeName)
    {
        var arguments = typeName == "SortedDictionary" ? "Of Integer, String" : "Of Integer";
        var source = $@"Module M
 Sub Main()
  Dim q As {typeName}({arguments})
  Console.WriteLine(""done"")
 End Sub
End Module";

        var (_, netDiagnostics, _) =
            CompileCorpusProgram(typeName, new Dictionary<string, string> { ["Main.bas"] = source });

        AssertNoNetDiagnostics(typeName + "(" + arguments + ")", netDiagnostics);
    }
}
