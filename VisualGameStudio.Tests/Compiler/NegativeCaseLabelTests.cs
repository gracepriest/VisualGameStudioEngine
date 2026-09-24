using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Signed <c>Select Case</c> labels — <c>Case -1</c>, <c>Case -3.75</c>, <c>Case +7</c>.
///
/// <para><b>MEASURED before the fix:</b> <c>Case -3.75</c> failed with "Unexpected token in
/// expression: '-'". A plain Case value is parsed by <c>Parser.ParsePrimaryExpression</c> —
/// deliberately primary-only, so <c>Case 1 Or 2</c> splits at <c>Or</c> into two alternatives
/// instead of parsing as a bitwise Or — and it accepted only literals and identifiers, so the
/// leading sign fell through and the rest of the line was re-parsed as a statement.
/// (<c>Case -1 To 5</c> and <c>Case Is &lt; -2</c> already parsed: those paths use the full
/// expression parser.)</para>
///
/// <para>Unary <c>+</c> had a second, deeper gap: the analyzer typed it but
/// <c>IRBuilder.MapUnaryOperator</c> had no row for it, so ANY unary plus (<c>Case +7</c>,
/// <c>x = +y</c>) threw "Unknown unary operator: +" at IR build. It is now the identity.</para>
/// </summary>
[TestFixture]
public class NegativeCaseLabelParserTests
{
    private static List<CaseClauseNode> ParseCases(string caseLines, out Parser parser)
    {
        var source = "Sub Main()\nDim v As Double\nSelect Case v\n" + caseLines + "\nEnd Select\nEnd Sub";
        parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        var main = ast.Declarations.OfType<SubroutineNode>().Single(s => s.Name == "Main");
        var select = main.Body.Statements.OfType<SelectStatementNode>().Single();
        return select.Cases;
    }

    private static List<CaseClauseNode> ParseCasesClean(string caseLines)
    {
        var cases = ParseCases(caseLines, out var parser);
        Assert.That(parser.Errors, Is.Empty,
            string.Join("; ", parser.Errors.Select(e => e.Message)));
        return cases;
    }

    /// <summary>Assert <paramref name="e"/> is <c>op literal</c> with the given literal text.</summary>
    private static void AssertSigned(ExpressionNode e, string op, string literal)
    {
        Assert.That(e, Is.InstanceOf<UnaryExpressionNode>(), $"expected a signed value, got {e?.GetType().Name}");
        var u = (UnaryExpressionNode)e;
        Assert.That(u.Operator, Is.EqualTo(op));
        Assert.That(u.IsPostfix, Is.False);
        Assert.That(u.Operand, Is.InstanceOf<LiteralExpressionNode>());
        Assert.That(((LiteralExpressionNode)u.Operand).Text, Is.EqualTo(literal));
    }

    [TestCase("-1", "-", "1")]
    [TestCase("-3.75", "-", "3.75")]
    [TestCase("+7", "+", "7")]
    public void SignedConstant_ParsesAsConstantPattern(string label, string op, string literal)
    {
        var cases = ParseCasesClean($"Case {label}\nv = 0\nCase Else\nv = 1");
        Assert.That(cases, Has.Count.EqualTo(2));
        var pattern = cases[0].Patterns.Single();
        Assert.That(pattern, Is.InstanceOf<ConstantPatternNode>());
        AssertSigned(((ConstantPatternNode)pattern).Value, op, literal);
        Assert.That(cases[0].Body.Statements, Has.Count.EqualTo(1),
            "the case body must be the assignment, not the leftover of a mis-parsed label");
    }

    [Test]
    public void DoubleNegative_Nests()
    {
        var pattern = (ConstantPatternNode)ParseCasesClean("Case - -2\nv = 0").Single().Patterns.Single();
        var outer = (UnaryExpressionNode)pattern.Value;
        Assert.That(outer.Operator, Is.EqualTo("-"));
        AssertSigned(outer.Operand, "-", "2");
    }

    [Test]
    public void NegatedIdentifier_ParsesAsConstantPattern()
    {
        var pattern = (ConstantPatternNode)ParseCasesClean("Case -v\nv = 0").Single().Patterns.Single();
        var u = (UnaryExpressionNode)pattern.Value;
        Assert.That(u.Operator, Is.EqualTo("-"));
        Assert.That(((IdentifierExpressionNode)u.Operand).Name, Is.EqualTo("v"));
    }

    [Test]
    public void CommaSeparatedNegatives_AreSeparatePatterns()
    {
        var patterns = ParseCasesClean("Case -1, -2, 3\nv = 0").Single().Patterns;
        Assert.That(patterns, Has.Count.EqualTo(3));
        AssertSigned(((ConstantPatternNode)patterns[0]).Value, "-", "1");
        AssertSigned(((ConstantPatternNode)patterns[1]).Value, "-", "2");
        Assert.That(((ConstantPatternNode)patterns[2]).Value, Is.InstanceOf<LiteralExpressionNode>());
    }

    /// <summary>The sign must bind to ONE primary: <c>Case -8 Or -9</c> is two alternatives,
    /// never the bitwise Or <c>-(8 Or -9)</c>.</summary>
    [Test]
    public void OrOfNegatives_IsAnOrPattern_NotABitwiseOr()
    {
        var pattern = ParseCasesClean("Case -8 Or -9\nv = 0").Single().Patterns.Single();
        Assert.That(pattern, Is.InstanceOf<OrPatternNode>());
        var alts = ((OrPatternNode)pattern).Alternatives;
        Assert.That(alts, Has.Count.EqualTo(2));
        AssertSigned(((ConstantPatternNode)alts[0]).Value, "-", "8");
        AssertSigned(((ConstantPatternNode)alts[1]).Value, "-", "9");
    }

    [Test]
    public void NegativeRange_ParsesAsRangePattern()
    {
        var pattern = ParseCasesClean("Case -1 To 5\nv = 0").Single().Patterns.Single();
        Assert.That(pattern, Is.InstanceOf<RangePatternNode>());
        var range = (RangePatternNode)pattern;
        AssertSigned(range.LowerBound, "-", "1");
        Assert.That(((LiteralExpressionNode)range.UpperBound).Text, Is.EqualTo("5"));
    }

    [Test]
    public void BothBoundsNegative_ParsesAsRangePattern()
    {
        var range = (RangePatternNode)ParseCasesClean("Case -10 To -5\nv = 0").Single().Patterns.Single();
        AssertSigned(range.LowerBound, "-", "10");
        AssertSigned(range.UpperBound, "-", "5");
    }

    [Test]
    public void IsComparisonAgainstNegative_ParsesAsComparisonPattern()
    {
        var pattern = ParseCasesClean("Case Is < -2\nv = 0").Single().Patterns.Single();
        Assert.That(pattern, Is.InstanceOf<ComparisonPatternNode>());
        var cmp = (ComparisonPatternNode)pattern;
        Assert.That(cmp.Operator, Is.EqualTo("<"));
        AssertSigned(cmp.Value, "-", "2");
    }

    [Test]
    public void NegativeInWhenGuard_Parses()
    {
        var pattern = ParseCasesClean("Case Is < -2 When v > -5\nv = 0").Single().Patterns.Single();
        var cmp = (ComparisonPatternNode)pattern;
        AssertSigned(cmp.Value, "-", "2");
        Assert.That(cmp.WhenGuard, Is.InstanceOf<BinaryExpressionNode>());
        AssertSigned(((BinaryExpressionNode)cmp.WhenGuard).Right, "-", "5");
    }

    [Test]
    public void NegativeConstantWithWhenGuard_Parses()
    {
        var pattern = ParseCasesClean("Case -1 When v < 0\nv = 0").Single().Patterns.Single();
        Assert.That(pattern, Is.InstanceOf<ConstantPatternNode>());
        AssertSigned(((ConstantPatternNode)pattern).Value, "-", "1");
        Assert.That(pattern.WhenGuard, Is.Not.Null);
    }

    /// <summary>A bare sign with nothing signable after it is still an error, not a silent
    /// empty label.</summary>
    [Test]
    public void BareSign_IsStillAnError()
    {
        var parser = new Parser(new Lexer(
            "Sub Main()\nDim v As Double\nSelect Case v\nCase -\nv = 0\nEnd Select\nEnd Sub").Tokenize());
        parser.Parse();
        Assert.That(parser.Errors, Is.Not.Empty);
    }
}

/// <summary>
/// End-to-end: the RIGHT branch is taken for a signed discriminant on every shipping backend
/// (C#, C++, JavaScript), each through the optimizer (the CLI for C#; the standard passes for
/// C++ and JavaScript). One classifier exercises every signed label shape; each call's output
/// line names the branch taken, so a wrong branch — or a sign dropped by the lowering — shows
/// up as a mismatched line.
/// </summary>
[TestFixture]
[Category("Integration")]   // compiles/runs native code, spawns dotnet and node
public class NegativeCaseLabelExecutionTests
{
    private const string Program = @"
Function Classify(v As Double) As String
    Select Case v
        Case -3.75
            Return ""exact-neg""
        Case -1 To 5
            Return ""range""
        Case Is < -10
            Return ""very-neg""
        Case +7
            Return ""plus-seven""
        Case -8 Or -9
            Return ""or-neg""
        Case Is < -2 When v > -5
            Return ""guard""
        Case Else
            Return ""else""
    End Select
End Function

Sub Main()
    Console.WriteLine(Classify(-3.75))
    Console.WriteLine(Classify(-1))
    Console.WriteLine(Classify(-20))
    Console.WriteLine(Classify(7))
    Console.WriteLine(Classify(-9))
    Console.WriteLine(Classify(-4))
    Console.WriteLine(Classify(-6))
    Console.WriteLine(Classify(3.75))
End Sub
";

    // -3.75 exact; -1 lower bound of range; -20 < -10; +7; -9 second Or alternative;
    // -4 guard true; -6 guard false -> else; +3.75 must NOT hit `Case -3.75` (sign kept).
    private const string Expected =
        "exact-neg\nrange\nvery-neg\nplus-seven\nor-neg\nguard\nelse\nrange";

    [Test]
    public void NegativeCaseLabels_TakeRightBranch_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(Program).Replace("\r\n", "\n").Trim(),
            Is.EqualTo(Expected));

    [Test]
    public void NegativeCaseLabels_TakeRightBranch_Cpp()
        => Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program)).Replace("\r\n", "\n").Trim(),
            Is.EqualTo(Expected));

    [Test]
    public void NegativeCaseLabels_TakeRightBranch_JavaScript()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(Program).Replace("\r\n", "\n").Trim(),
            Is.EqualTo(Expected));
}
