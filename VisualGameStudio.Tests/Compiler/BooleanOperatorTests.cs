using BasicLang.Compiler.CodeGen;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Boolean operators: <c>And</c>, <c>Or</c>, <c>AndAlso</c>, <c>OrElse</c>, and keyword casing.
///
/// <para><b>Three separate defects met here, and only the first was visible.</b>
/// <c>If x &gt; 0 And y &gt; 0</c> did not compile on the JavaScript backend; <c>AndAlso</c> and
/// <c>OrElse</c> did not parse on ANY backend; and a lowercase <c>and</c> failed everywhere
/// with "Unknown binary operator 'and'" even though BasicLang keywords are otherwise
/// case-insensitive.</para>
///
/// <para><b>What <c>And</c> actually means here.</b> Real VB overloads it — bitwise on integral
/// operands, non-short-circuit logical on Boolean. BasicLang implements only the second half:
/// the SemanticAnalyzer requires Boolean operands, so <c>12 And 10</c> is a type error and
/// there is no way to reach bitwise AND at all (<c>&amp;</c> is string concatenation). Every
/// <c>BinaryOpKind.And</c> that reaches a backend therefore provably has Boolean operands,
/// which is what makes an unconditional <c>&amp;&amp;</c> correct rather than a guess.</para>
/// </summary>
[TestFixture]
public class BooleanOperatorCodeGenTests
{
    private static string Js(string body) =>
        JsTestSupport.Compile($"Sub Main()\n{body}\nEnd Sub");

    /// <summary>
    /// <c>&amp;</c> would be wrong even for Booleans: in JavaScript it coerces to int32, so
    /// <c>true &amp; false</c> is <c>0</c> — a Boolean-typed expression that prints 0 instead
    /// of false.
    /// </summary>
    [Test]
    public void And_LowersToLogicalAnd_NotBitwise()
    {
        var js = Js("Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a And b\nConsole.WriteLine(c)");

        Assert.That(js, Does.Contain("&&"));
        Assert.That(js, Does.Not.Contain(" & "));
    }

    [Test]
    public void Or_LowersToLogicalOr()
        => Assert.That(
            Js("Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a Or b\nConsole.WriteLine(c)"),
            Does.Contain("||"));

    // ---------------------------------------------------------------- casing

    /// <summary>
    /// BasicLang keywords are case-insensitive — the lexer's table is OrdinalIgnoreCase — but
    /// the AST stores the operator as the RAW source lexeme and IRBuilder's map was a
    /// case-sensitive switch. So `and` reached "Unknown binary operator 'and'" while `And`
    /// worked, on every backend.
    /// </summary>
    [TestCase("and")]
    [TestCase("AND")]
    [TestCase("And")]
    [TestCase("aNd")]
    public void And_IsCaseInsensitive(string spelling)
        => Assert.That(
            Js($"Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a {spelling} b\nConsole.WriteLine(c)"),
            Does.Contain("&&"));

    [TestCase("or")]
    [TestCase("OR")]
    public void Or_IsCaseInsensitive(string spelling)
        => Assert.That(
            Js($"Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a {spelling} b\nConsole.WriteLine(c)"),
            Does.Contain("||"));

    /// <summary>The same case-sensitive switch governed every WORD operator, not just And/Or.</summary>
    [TestCase("mod")]
    [TestCase("Mod")]
    public void Mod_IsCaseInsensitive(string spelling)
        => Assert.That(Js($"Dim x As Integer = 7 {spelling} 3\nConsole.WriteLine(x)"),
            Does.Contain("%"));

    [TestCase("not")]
    [TestCase("Not")]
    public void Not_IsCaseInsensitive(string spelling)
        => Assert.That(Js($"Dim a As Boolean = True\nDim b As Boolean = {spelling} a\nConsole.WriteLine(b)"),
            Does.Contain("!"));

    // ---------------------------------------------------------------- AndAlso / OrElse

    /// <summary>
    /// These did not lex at all — no TokenType, no keyword-table row — so a source file using
    /// them died with "Unexpected token in expression". Meanwhile the LSP was already serving
    /// hover text describing their short-circuit behaviour, and the FORMATTER auto-capitalised
    /// `andalso` into `AndAlso` as you typed: the IDE actively steered users into a spelling
    /// the compiler rejected.
    /// </summary>
    [Test]
    public void AndAlso_Parses()
        => Assert.That(
            Js("Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a AndAlso b\nConsole.WriteLine(c)"),
            Does.Contain("&&"));

    [Test]
    public void OrElse_Parses()
        => Assert.That(
            Js("Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a OrElse b\nConsole.WriteLine(c)"),
            Does.Contain("||"));

    [TestCase("andalso")]
    [TestCase("ANDALSO")]
    public void AndAlso_IsCaseInsensitive(string spelling)
        => Assert.That(
            Js($"Dim a As Boolean = True\nDim b As Boolean = False\nDim c As Boolean = a {spelling} b\nConsole.WriteLine(c)"),
            Does.Contain("&&"));

    /// <summary>
    /// ⚠ The lexer scans one maximal alphanumeric run, so <c>AndAlso</c> is a single token —
    /// it must NOT be read as <c>And</c> followed by an identifier <c>Also</c>. A file using
    /// both spellings proves the longer keyword does not shadow the shorter one either.
    /// </summary>
    [Test]
    public void AndAlso_AndBareAnd_CoexistInOneExpression()
        => Assert.That(
            Js("Dim a As Boolean = True\nDim b As Boolean = True\nDim c As Boolean = False\n" +
               "Dim r As Boolean = a AndAlso b And c\nConsole.WriteLine(r)"),
            Does.Contain("&&"));

    /// <summary>
    /// Parser.cs has TWO independent expression parsers with different operator tables — a
    /// recursive-descent chain and a precedence-climbing continuation reached from a bare
    /// expression statement. Adding a keyword to only one gives position-dependent parsing.
    /// </summary>
    [Test]
    public void AndAlso_ParsesInBothExpressionParsers()
    {
        // Declaration initialiser — recursive-descent chain.
        Assert.That(Js("Dim a As Boolean = True\nDim r As Boolean = a AndAlso a\nConsole.WriteLine(r)"),
            Does.Contain("&&"));

        // Assignment to an existing local — the continuation chain.
        Assert.That(Js("Dim a As Boolean = True\nDim r As Boolean = False\nr = a AndAlso a\nConsole.WriteLine(r)"),
            Does.Contain("&&"));
    }

    // ---------------------------------------------------------------- what stays refused

    /// <summary>
    /// Bitwise <c>And</c> on integers is NOT part of this fix and must stay a clean error.
    /// BasicLang has never supported it — the analyzer types <c>And</c> as Boolean — and
    /// making it type-directed would be a language change admitting new legal programs.
    /// </summary>
    [Test]
    public void BitwiseAndOnIntegers_IsStillRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "Sub Main()\nDim f As Integer = 12 And 10\nConsole.WriteLine(f)\nEnd Sub"),
            Throws.Exception, "integral And is not supported by the language");
}

/// <summary>The same operators, actually executed.</summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class BooleanOperatorExecutionTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    [Test]
    public void And_TruthTable()
        => Assert.That(Run(
            "Console.WriteLine(True And True)\nConsole.WriteLine(True And False)\n" +
            "Console.WriteLine(False And True)\nConsole.WriteLine(False And False)"),
            Is.EqualTo("true\nfalse\nfalse\nfalse"));

    [Test]
    public void Or_TruthTable()
        => Assert.That(Run(
            "Console.WriteLine(True Or True)\nConsole.WriteLine(True Or False)\n" +
            "Console.WriteLine(False Or True)\nConsole.WriteLine(False Or False)"),
            Is.EqualTo("true\ntrue\ntrue\nfalse"));

    /// <summary>
    /// THE point of using <c>&amp;&amp;</c> over <c>&amp;</c>: a Boolean result must print as a
    /// boolean. JavaScript's <c>&amp;</c> coerces to int32, so this would read 1/0.
    /// </summary>
    [Test]
    public void BooleanResult_PrintsAsBooleanNotAsNumber()
        => Assert.That(Run("Dim a As Boolean = True\nDim b As Boolean = True\nConsole.WriteLine(a And b)"),
            Is.EqualTo("true"));

    /// <summary>The condition that started this: an everyday compound guard.</summary>
    [Test]
    public void CompoundConditionInAnIf()
        => Assert.That(Run(
            "Dim x As Integer = Len(\"hello\")\nDim y As Integer = Len(\"hi\")\n" +
            "If x > 0 And y > 0 Then\nConsole.WriteLine(\"both positive\")\nEnd If\n" +
            "If x > 99 Or y > 0 Then\nConsole.WriteLine(\"either\")\nEnd If"),
            Is.EqualTo("both positive\neither"));

    [Test]
    public void AndAlso_And_OrElse_Execute()
        => Assert.That(Run(
            "Dim x As Integer = 5\n" +
            "If x > 0 AndAlso x < 10 Then\nConsole.WriteLine(\"in range\")\nEnd If\n" +
            "If x > 99 OrElse x = 5 Then\nConsole.WriteLine(\"matched\")\nEnd If"),
            Is.EqualTo("in range\nmatched"));

    [Test]
    public void Not_Executes()
        => Assert.That(Run("Dim a As Boolean = False\nConsole.WriteLine(Not a)"),
            Is.EqualTo("true"));

    /// <summary>Mixed casing, end to end — VB is a case-insensitive language.</summary>
    [Test]
    public void LowercaseKeywords_Execute()
        => Assert.That(Run(
            "Dim a As Boolean = True\nDim b As Boolean = False\n" +
            "Console.WriteLine(a and b)\nConsole.WriteLine(a or b)\nConsole.WriteLine(not b)"),
            Is.EqualTo("false\ntrue\ntrue"));

    /// <summary>Chained operators must associate left and respect And-binds-tighter-than-Or.</summary>
    [Test]
    public void PrecedenceAndBindsTighterThanOr()
        => Assert.That(Run("Console.WriteLine(True Or False And False)"),
            Is.EqualTo("true"), "must parse as True Or (False And False)");
}
