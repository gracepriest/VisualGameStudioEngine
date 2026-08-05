using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 15 — string builtins.
///
/// <para><b>The hole this closes.</b> Len/Mid/UCase and friends arrive as an IRCall with a
/// BARE FunctionName — not dotted, not a BinaryOpKind. <c>CallTarget</c> passes any
/// unqualified name straight through on the assumption it is a user function, so
/// <c>Len(s)</c> emitted <c>Len(s)</c>: a call to a function that exists nowhere in
/// JavaScript, i.e. a ReferenceError at runtime from a build that reported success. The
/// dotted-name path already throws to prevent exactly that; the bare-name channel was
/// unguarded.</para>
///
/// <para><b>Why a second seam was needed.</b> These do not lower to a renamed FUNCTION —
/// <c>Len(s)</c> must become <c>s.length</c>, a MEMBER expression. A CallTarget that can only
/// return a callee name cannot express that, so builtins are rendered whole.</para>
///
/// <para>These assertions are on emitted CODE rather than behaviour, so they run without
/// Node; <see cref="JavaScriptStringExecutionTests"/> proves the semantics.</para>
/// </summary>
[TestFixture]
public class JavaScriptStringTests
{
    private static string Compile(string body) =>
        JsTestSupport.Compile($"Sub Main()\nDim s As String\ns = \"Hello\"\n{body}\nEnd Sub");

    [TestCase("Console.WriteLine(Len(s))", ".length", TestName = "Len_IsAMemberNotACall")]
    [TestCase("Console.WriteLine(UCase(s))", ".toUpperCase()", TestName = "UCase")]
    [TestCase("Console.WriteLine(LCase(s))", ".toLowerCase()", TestName = "LCase")]
    [TestCase("Console.WriteLine(Trim(s))", ".trim()", TestName = "Trim")]
    public void StringBuiltin_EmitsTheJsMemberForm(string body, string expected)
    {
        var js = Compile(body);

        Assert.That(js, Does.Contain(expected));
        Assert.That(js, Does.Not.Match(@"\b(Len|UCase|LCase|Trim)\s*\("),
            "the BasicLang name must not survive into the output — it exists nowhere in JS");
    }

    /// <summary>
    /// A user's own function must WIN over the builtin. Without a shadowing check, declaring
    /// `Function Len(...)` silently rebinds every call to the JS member form and the user's
    /// code never runs.
    /// </summary>
    [Test]
    public void UserFunction_ShadowsTheBuiltin()
    {
        var js = JsTestSupport.Compile(
            "Function Len(x As String) As Integer\nReturn 42\nEnd Function\n" +
            "Sub Main()\nDim s As String\ns = \"a\"\nConsole.WriteLine(Len(s))\nEnd Sub");

        Assert.That(js, Does.Contain("function Len("), "the user's function must be emitted");
        Assert.That(js, Does.Not.Contain(".length"),
            "the call must reach the user's Len, not the builtin");
    }

    /// <summary>
    /// Replace must replace EVERY occurrence. JS String.replace with a string pattern
    /// replaces only the FIRST — a difference that produces a plausible-looking wrong answer
    /// rather than an error.
    /// </summary>
    [Test]
    public void Replace_DoesNotUseSingleOccurrenceReplace()
    {
        var js = Compile("Console.WriteLine(Replace(s, \"l\", \"L\"))");

        Assert.That(js, Does.Match(@"replaceAll|\.split\(.*\)\.join\("),
            "String.replace(string, string) changes only the first occurrence");
    }
}

/// <summary>
/// Behavioural half of plan task 15. The 1-based conversions are the reason these exist:
/// BasicLang's Mid and InStr are 1-based and JavaScript's are 0-based, so an off-by-one here
/// compiles perfectly and returns the wrong characters.
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptStringExecutionTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs(
            $"Sub Main()\nDim s As String\ns = \"Hello\"\n{body}\nEnd Sub");

    [TestCase("Console.WriteLine(Len(s))", "5", TestName = "Len")]
    [TestCase("Console.WriteLine(UCase(s))", "HELLO", TestName = "UCase")]
    [TestCase("Console.WriteLine(LCase(s))", "hello", TestName = "LCase")]
    public void SimpleBuiltins(string body, string expected)
        => Assert.That(Run(body), Is.EqualTo(expected));

    /// <summary>Mid's start is 1-BASED: Mid("Hello",1,2) is "He", not "el".</summary>
    [TestCase("Console.WriteLine(Mid(s, 1, 2))", "He", TestName = "Mid_StartsAtOne")]
    [TestCase("Console.WriteLine(Mid(s, 2, 3))", "ell", TestName = "Mid_Offset")]
    public void Mid_IsOneBased(string body, string expected)
        => Assert.That(Run(body), Is.EqualTo(expected));

    [TestCase("Console.WriteLine(Left(s, 2))", "He", TestName = "Left")]
    [TestCase("Console.WriteLine(Right(s, 3))", "llo", TestName = "Right")]
    public void LeftAndRight(string body, string expected)
        => Assert.That(Run(body), Is.EqualTo(expected));

    /// <summary>InStr is 1-based and returns 0 when not found — not JS's -1.</summary>
    [TestCase("Console.WriteLine(InStr(s, \"H\"))", "1", TestName = "InStr_FirstCharIsOne")]
    [TestCase("Console.WriteLine(InStr(s, \"llo\"))", "3", TestName = "InStr_Offset")]
    [TestCase("Console.WriteLine(InStr(s, \"z\"))", "0", TestName = "InStr_NotFoundIsZero")]
    public void InStr_IsOneBased(string body, string expected)
        => Assert.That(Run(body), Is.EqualTo(expected));

    /// <summary>Every occurrence, not just the first.</summary>
    [Test]
    public void Replace_ReplacesAllOccurrences()
        => Assert.That(Run("Console.WriteLine(Replace(s, \"l\", \"L\"))"), Is.EqualTo("HeLLo"));

    [Test]
    public void Trim_RemovesSurroundingWhitespace()
        => Assert.That(Run("Console.WriteLine(\"[\" & Trim(\"  x  \") & \"]\")"), Is.EqualTo("[x]"));

    [Test]
    public void Concatenation_And_Length_StillWork()
        => Assert.That(Run("Console.WriteLine((s & \"!\").Length)"), Is.EqualTo("6"));
}
