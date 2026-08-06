using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Chip task_8f4dcdb2 — a member reference must emit the member's DECLARED name, whatever
/// casing the call site used.
///
/// <para>BasicLang is case-insensitive, so the front end correctly accepts
/// <c>b.textContent</c> against a field declared <c>TextContent</c>. Codegen then emitted the
/// USE-SITE spelling verbatim, so the two became two different members:</para>
///
/// <code>
/// class Box { TextContent = ""; }
/// b.textContent = "hi";        // writes one property
/// const t2 = b.TextContent;    // reads a DIFFERENT one
/// </code>
///
/// <para>Silently wrong on JavaScript from a green build; a late CS1061 on the C# backend,
/// pointing at generated C# rather than the user's source. Same shape as the operator bug
/// fixed in <c>cd4f04d</c>: case-insensitive front end, case-sensitive back end.</para>
///
/// <para>This also gates plan 2b — the DOM is declared in camelCase and users will type
/// PascalCase out of BasicLang habit.</para>
/// </summary>
[TestFixture]
public class MemberCasingTests
{
    private const string Box =
        "Class Box\n" +
        "Public TextContent As String\n" +
        "End Class\n";

    [Test]
    public void FieldWrite_UsesDeclaredCasing()
        => Assert.That(JsTestSupport.Compile(
                Box + "Sub Main()\nDim b As New Box()\nb.textContent = \"hi\"\nEnd Sub"),
            Does.Contain(".TextContent = ").And.Not.Contain(".textContent"));

    [Test]
    public void FieldRead_UsesDeclaredCasing()
        => Assert.That(JsTestSupport.Compile(
                Box + "Sub Main()\nDim b As New Box()\nConsole.WriteLine(b.textcontent)\nEnd Sub"),
            Does.Contain(".TextContent").And.Not.Contain(".textcontent"));

    /// <summary>
    /// THE bug as reported: a write and a read that disagree on casing must reach the SAME
    /// property. Before the fix this emitted two.
    /// </summary>
    [Test]
    public void WriteThenRead_WithDifferentCasing_HitTheSameMember()
    {
        var js = JsTestSupport.Compile(
            Box + "Sub Main()\nDim b As New Box()\nb.textContent = \"hi\"\n" +
            "Console.WriteLine(b.TextContent)\nEnd Sub");

        Assert.That(js, Does.Not.Contain(".textContent"),
            "the lowercase spelling must have been canonicalised away");
    }

    /// <summary>
    /// A class declared AFTER its use. If canonicalisation happens at IR-construction time
    /// this only works when declarations are registered first; a post-pass makes it
    /// order-independent. Either way the behaviour must not depend on source order.
    /// </summary>
    [Test]
    public void ForwardDeclaredClass_StillUsesDeclaredCasing()
        => Assert.That(JsTestSupport.Compile(
                "Sub Main()\nDim b As New Box()\nb.textContent = \"hi\"\nEnd Sub\n" + Box),
            Does.Contain(".TextContent = ").And.Not.Contain(".textContent"));

    /// <summary>Properties resolve the same way fields do.</summary>
    [Test]
    public void Property_UsesDeclaredCasing()
        => Assert.That(JsTestSupport.Compile(
                "Class Person\n" +
                "Private _n As String\n" +
                "Public Property FullName As String\n" +
                "Get\nReturn _n\nEnd Get\n" +
                "Set(value As String)\n_n = value\nEnd Set\n" +
                "End Property\n" +
                "End Class\n" +
                "Sub Main()\nDim p As New Person()\nConsole.WriteLine(p.fullname)\nEnd Sub"),
            Does.Not.Contain(".fullname"));

    /// <summary>
    /// An UNKNOWN receiver type must be left completely alone — a `::` foreign name, a .NET
    /// type, or anything this module did not declare. Canonicalising what we cannot resolve
    /// would rewrite names that were already correct.
    /// </summary>
    [Test]
    public void UnknownReceiver_IsLeftAlone()
        => Assert.That(JsTestSupport.Compile(
                "Sub Main()\nDim s As String = \"ab\"\nConsole.WriteLine(s.Length)\nEnd Sub"),
            Does.Contain("length"), "String.Length lowers through the collection/stdlib path");
}

/// <summary>The same bug, executed — the report's exact program printed nothing.</summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class MemberCasingExecutionTests
{
    [Test]
    public void MisCasedWriteThenRead_RoundTrips()
        => Assert.That(JavaScriptExecutionTests.RunJs(
                "Class Box\nPublic TextContent As String\nEnd Class\n" +
                "Sub Main()\nDim b As New Box()\nb.textContent = \"hi\"\n" +
                "Console.WriteLine(b.TextContent)\nEnd Sub"),
            Is.EqualTo("hi"));

    [Test]
    public void AllLowercaseMemberUse_Works()
        => Assert.That(JavaScriptExecutionTests.RunJs(
                "Class Counter\nPublic Count As Integer\n" +
                "Public Sub Bump()\nCount = Count + 1\nEnd Sub\n" +
                "End Class\n" +
                "Sub Main()\nDim c As New Counter()\nc.bump()\nc.bump()\n" +
                "Console.WriteLine(c.count)\nEnd Sub"),
            Is.EqualTo("2"));
}
