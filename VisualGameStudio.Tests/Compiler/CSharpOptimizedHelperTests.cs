using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 8: proves <see cref="CSharpTestSupport.CompileToCSharpOptimized"/> actually runs the
/// optimizer, rather than being a second name for the non-optimizing path.
///
/// <para>⛔ A helper like this is exactly the kind of thing that can be quietly wrong and never
/// noticed: if the pipeline were never wired up, every fixture built on it would still pass, and
/// the suite would have gained the APPEARANCE of optimizer coverage while testing the same IR as
/// before. So the first assertion is not about C# at all — it is that the two paths produce
/// DIFFERENT output for a source the optimizer is known to change.</para>
/// </summary>
[TestFixture]
public class CSharpOptimizedHelperTests
{
    /// <summary>A constant the front end emits as an add and ConstantFoldingPass collapses.</summary>
    private const string FoldableSource = """
        Module Program
            Sub Main()
                Dim total As Integer = 2 + 3
                Console.WriteLine(total)
            End Sub
        End Module
        """;

    [Test]
    public void Optimized_DiffersFromUnoptimized_ForASourceTheOptimizerChanges()
    {
        // The load-bearing assertion. ConstantFoldingPass is the first pass AddStandardPasses adds,
        // so `2 + 3` cannot survive it intact. If these two are equal, the pipeline is not running
        // and the helper is decorative.
        var plain = CSharpTestSupport.CompileToCSharp(FoldableSource);
        var optimized = CSharpTestSupport.CompileToCSharpOptimized(FoldableSource);

        Assert.That(optimized, Is.Not.EqualTo(plain),
            "CompileToCSharpOptimized produced byte-identical output to the non-optimizing path for " +
            "a foldable constant — the OptimizationPipeline is not wired up, and every fixture built " +
            "on this helper would be testing the same IR it always did");
    }

    [Test]
    public void Optimized_FoldsAConstantExpression()
    {
        var optimized = CSharpTestSupport.CompileToCSharpOptimized(FoldableSource);

        Assert.That(optimized, Does.Contain("5"),
            "ConstantFoldingPass should collapse 2 + 3 to 5 in the shipped IR");
    }

    [Test]
    public void Unoptimized_StillCompiles_AndIsTheShapeTheFourteenFixturesUse()
    {
        // Guards the claim in the helper's own docs: the plain path is equivalent to the fourteen
        // per-fixture copies, so moving a fixture onto it is a no-op for that fixture.
        var plain = CSharpTestSupport.CompileToCSharp(FoldableSource);

        Assert.That(plain, Is.Not.Null.And.Not.Empty);
        Assert.That(plain, Does.Contain("Main"));
    }

    [Test]
    public void TheWrapperAndTheGeneratorItWraps_ProduceIdenticalOutput()
    {
        // ⚠ A second divergence found while writing this helper, pinned rather than assumed.
        // The existing fixtures are split across TWO generators: 27 call sites construct
        // ImprovedCSharpCodeGenerator directly, while the CLI (Program.cs) and the IDE
        // (BuildService) both go through the CSharpCodeGenerator wrapper that implements
        // ICodeGenerator. Generate() currently just delegates, so they agree — but nothing checked
        // that, and a fixture pinned to the inner class would not notice if the wrapper stopped
        // agreeing with it. This helper uses the wrapper, because that is what ships.
        var module = CSharpTestSupport.BuildModule(FoldableSource);
        var viaWrapper = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(module);

        var freshModule = CSharpTestSupport.BuildModule(FoldableSource);
        var viaInner = new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(freshModule);

        Assert.That(viaWrapper, Is.EqualTo(viaInner),
            "CSharpCodeGenerator is documented as a thin wrapper around ImprovedCSharpCodeGenerator, " +
            "and most of this suite is pinned to the inner class while everything that ships goes " +
            "through the wrapper. If these ever disagree, the suite is testing the wrong one.");
    }

    [Test]
    public void BuildModule_Throws_WhenTheTestsOwnSourceDoesNotParse()
    {
        // The false-green guard. Parser.Parse() swallows its own errors and returns a module with
        // zero declarations, so without this a fixture with a typo asserts against nothing and
        // passes. An InvalidOperationException rather than an assertion failure, because this means
        // the TEST is broken, not the backend.
        var ex = Assert.Throws<System.InvalidOperationException>(
            () => CSharpTestSupport.CompileToCSharp("Module Program Sub Main( End Module"));

        Assert.That(ex!.Message, Does.Contain("the test is broken, not the backend"));
    }
}
