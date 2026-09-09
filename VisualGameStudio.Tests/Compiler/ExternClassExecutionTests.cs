using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Extern Class</c>, RUN under Node rather than string-matched.
///
/// <para><b>Node has no DOM</b>, so these declare extern types over things Node DOES have —
/// <c>JSON</c>, <c>Math</c>-like objects, a hand-made global. The mechanism under test is
/// identical to the DOM case and is the whole feature: DECLARE a type that already exists,
/// emit nothing for it, obtain a value from the runtime, and call members by their declared
/// name. Only the object differs.</para>
///
/// <para><b>Why running matters more here than usual.</b> The bug this feature fixed was
/// invisible to text assertions in the worst way: the backend emitted
/// <c>class Element { querySelector(sel) { return null; } }</c>, which LOOKS like support.
/// A codegen test asserting "the member is called" passed while every call answered null.
/// stdout is the only oracle that can tell those apart.</para>
///
/// <para>Separate from <see cref="ExternClassTests"/> because the execution-tier roster reads
/// TYPE-level <c>[Category]</c>, so a fixture mixing fast and Integration tests cannot be
/// rostered at all.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class ExternClassExecutionTests
{
    /// <summary>
    /// ⭐ THE WHOLE FEATURE IN ONE TEST: an extern type is obtained from the runtime and its
    /// member call reaches the REAL object.
    ///
    /// <para>This also answers the question the plan flagged as open — declaring a type is
    /// useless if no expression can produce one. A <c>javascript{ }</c> block binds the local
    /// (the declaration emits <c>let api;</c> in the same scope), and from there the call is
    /// ordinary, type-checked BasicLang.</para>
    /// </summary>
    [Test]
    public void ExternClass_ObtainedFromTheRuntime_MemberCallReachesTheRealObject()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Extern Class JsonApi\nPublic Function stringify(v As Object) As String\nEnd Class\n" +
            "Sub Main()\nDim api As JsonApi\njavascript{ api = JSON; }\n" +
            "Console.WriteLine(api.stringify(42))\nEnd Sub"),
            Is.EqualTo("42"));

    /// <summary>
    /// ⛔ THE REGRESSION THIS FEATURE EXISTS TO PREVENT. Before the emission guard, the backend
    /// synthesized <c>stringify(v) { return null; }</c> — so this program printed "null" from a
    /// build that reported success. If the extern class is ever emitted again, this goes red
    /// with a wrong VALUE rather than an error, which is exactly why it must run.
    /// </summary>
    [Test]
    public void ExternClass_MemberDoesNotReturnASynthesizedNull()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Extern Class JsonApi\nPublic Function stringify(v As Object) As String\nEnd Class\n" +
            "Sub Main()\nDim api As JsonApi\njavascript{ api = JSON; }\n" +
            "Console.WriteLine(api.stringify(7))\nEnd Sub"),
            Is.Not.EqualTo("null").And.EqualTo("7"));

    /// <summary>
    /// ⭐ THE PAYOFF, executed: a BasicLang user writing PascalCase still reaches the real
    /// camelCase member. Codegen tests pin the emitted text; only running proves the runtime
    /// object actually answers.
    /// </summary>
    [Test]
    public void ExternClass_PascalCaseUseSite_StillReachesTheCamelCaseMember()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Extern Class Box\nPublic Property innerText As String\nEnd Class\n" +
            "Sub Main()\nDim b As Box\njavascript{ b = { innerText: \"from the runtime\" }; }\n" +
            "Console.WriteLine(b.InnerText)\nEnd Sub"),
            Is.EqualTo("from the runtime"));

    /// <summary>A property WRITE through an extern type reaches the real object too.</summary>
    [Test]
    public void ExternClass_PropertyWrite_ReachesTheRealObject()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Extern Class Box\nPublic Property innerText As String\nEnd Class\n" +
            "Sub Main()\nDim b As Box\njavascript{ b = {}; }\n" +
            "b.innerText = \"written\"\njavascript{ console.log(b.innerText); }\nEnd Sub"),
            Is.EqualTo("written"));

    /// <summary>
    /// ⛔ THE SHIPPING IR. Every route runs OptimizationPipeline.AddStandardPasses()
    /// unconditionally while JsTestSupport.Compile runs none of it — the gap that once hid six
    /// live defects behind 351 green tests. An extern type is erased and its members have no
    /// bodies, which is an unusual shape for the passes to meet.
    /// </summary>
    [Test]
    public void Optimized_ExternClass_StillReachesTheRealObject()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(
            "Extern Class JsonApi\nPublic Function stringify(v As Object) As String\nEnd Class\n" +
            "Sub Main()\nDim n As Integer = 20 + 22\nDim api As JsonApi\n" +
            "javascript{ api = JSON; }\nConsole.WriteLine(api.stringify(n))\nEnd Sub"),
            Is.EqualTo("42"));
}
