using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 30 — the guard that keeps the JavaScript execution tier from vanishing quietly.
///
/// <para><b>Why the existing guard is not enough.</b>
/// <c>JavaScriptExecutionTests.ExecutionTier_IsNotSilentlySkipped</c> asserts that Node was
/// FOUND. That is a different claim from "the tier ran": delete every execution fixture,
/// empty every test body, or strip every <c>[Category("Integration")]</c>, and it still
/// passes. Worse, it lives INSIDE the Integration fixture, so
/// <c>--filter "TestCategory!=Integration"</c> — the command this project uses to declare
/// green — never runs it at all.</para>
///
/// <para>So this fixture carries NO category. It loads nothing, spawns nothing, and cannot
/// skip; it is structural, and it runs in the gate that gets used. The idiom is the one
/// <c>RaylibColorParityTests</c> already established: an explicit roster closed by a length
/// pin, so shrinking the roster fails instead of quietly weakening the guard.</para>
///
/// <para><b>The roster is <c>typeof</c>, not strings, deliberately.</b> Deleting a fixture
/// then becomes a COMPILE error rather than a runtime one — the earliest possible failure.</para>
/// </summary>
[TestFixture]
public class JsExecutionTierRosterTests
{
    /// <summary>
    /// Every fixture that compiles BasicLang and RUNS the result under Node. Keyed on TYPES,
    /// not files: JavaScriptStringTests.cs holds two fixtures and only one is in the tier, so
    /// a file-based count would be wrong.
    /// </summary>
    private static readonly Type[] ExecutionTier =
    {
        typeof(JavaScriptExecutionTests),
        typeof(JavaScriptNumericTests),
        typeof(JavaScriptControlFlowTests),
        typeof(JavaScriptStringExecutionTests),
        typeof(JavaScriptArrayTests),
        typeof(JavaScriptClassTests),
        typeof(JavaScriptCollectionTests),
        typeof(JavaScriptForEachTests),
        typeof(JavaScriptSelectCaseTests),
        typeof(JavaScriptExceptionTests),
        typeof(JavaScriptLambdaTests),
        typeof(JavaScriptAsyncTests),
        typeof(JavaScriptIteratorTests),
        typeof(JavaScriptGenericsLinqTests),
        typeof(JavaScriptStdLibTests),
        typeof(JavaScriptOptimizedExecutionTests),
        typeof(JavaScriptCliProcessTests),
        typeof(JavaScriptInteropExecutionTests),
    };

    /// <summary>
    /// The floor, not the current count (measured at 191 when this landed). Pinned BELOW the
    /// real total so adding tests never breaks the build while removing a meaningful chunk
    /// does — a guard that must be edited on every commit gets edited without thought.
    ///
    /// <para>Per-fixture deletion is caught precisely by the roster instead: <c>typeof</c>
    /// makes it a COMPILE error, and EveryFixtureStillHasTests catches an emptied one. This
    /// number is the backstop for the diffuse case — many tests removed across many files.</para>
    /// </summary>
    private const int MinimumExecutionCases = 150;

    /// <summary>Counts NUnit cases: a [TestCase]-driven method contributes one per attribute.</summary>
    private static int CaseCount(Type fixture)
    {
        var total = 0;
        foreach (var method in fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var cases = method.GetCustomAttributes<TestCaseAttribute>(inherit: true).Count();
            if (cases > 0) { total += cases; continue; }
            if (method.GetCustomAttributes<TestAttribute>(inherit: true).Any()) total++;
        }
        return total;
    }

    [Test]
    public void RosterIsPinned()
        => Assert.That(ExecutionTier, Has.Length.EqualTo(18),
            "The execution-tier roster changed. That is fine — update the number — but it must " +
            "be a deliberate edit, not a silent shrink.");

    /// <summary>
    /// Every fixture in the roster must still be a fixture with tests in it. An emptied
    /// fixture is the shape a false green takes when someone comments a file out.
    /// </summary>
    [Test]
    public void EveryFixtureStillHasTests()
    {
        foreach (var fixture in ExecutionTier)
        {
            Assert.That(fixture.GetCustomAttributes<TestFixtureAttribute>(true).Any(), Is.True,
                $"{fixture.Name} is no longer a [TestFixture].");
            Assert.That(CaseCount(fixture), Is.GreaterThan(0),
                $"{fixture.Name} has no tests left — it is in the roster but proves nothing.");
        }
    }

    /// <summary>
    /// The tier is defined by running under Node, which is why it is excluded from the fast
    /// subset. A fixture that loses its category silently changes which gate covers it.
    /// </summary>
    [Test]
    public void EveryFixtureIsStillCategorisedIntegration()
    {
        foreach (var fixture in ExecutionTier)
        {
            var categories = fixture.GetCustomAttributes<CategoryAttribute>(true)
                .Select(c => c.Name).ToList();

            Assert.That(categories, Does.Contain("Integration"),
                $"{fixture.Name} lost [Category(\"Integration\")]. It spawns a process, so it " +
                "must stay out of the fast subset — and the full run is what proves the " +
                "backend works.");
        }
    }

    /// <summary>
    /// THE headline assertion: the tier has not been hollowed out. Counts CASES rather than
    /// methods, because a [TestCase]-driven method is worth as many checks as it has rows.
    /// </summary>
    [Test]
    public void ExecutionTierHasNotShrunk()
    {
        var perFixture = ExecutionTier.ToDictionary(t => t.Name, CaseCount);
        var total = perFixture.Values.Sum();

        Assert.That(total, Is.GreaterThanOrEqualTo(MinimumExecutionCases),
            $"The JavaScript execution tier has shrunk to {total} cases (floor is " +
            $"{MinimumExecutionCases}). This tier is the ONLY thing that proves the backend " +
            "emits JavaScript that actually runs — text assertions cannot tell correct " +
            "lowering from lowering that compiles and behaves wrongly.\nPer fixture: " +
            string.Join(", ", perFixture.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
    }

    /// <summary>
    /// Catches a NEW node-spawning fixture that never got added to the roster — otherwise the
    /// roster slowly stops describing the tier and the floor stops meaning anything.
    /// </summary>
    [Test]
    public void RosterCoversEveryJavaScriptIntegrationFixture()
    {
        var rostered = new HashSet<Type>(ExecutionTier);

        var discovered = typeof(JavaScriptExecutionTests).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace == typeof(JavaScriptExecutionTests).Namespace)
            .Where(t => t.Name.StartsWith("JavaScript", StringComparison.Ordinal) ||
                        t.Name.StartsWith("Js", StringComparison.Ordinal))
            .Where(t => t.GetCustomAttributes<CategoryAttribute>(true).Any(c => c.Name == "Integration"))
            .ToList();

        var missing = discovered.Where(t => !rostered.Contains(t)).Select(t => t.Name).ToList();

        Assert.That(missing, Is.Empty,
            "These JavaScript Integration fixtures are not in the roster, so the floor above " +
            "does not account for them: " + string.Join(", ", missing));
    }
}
