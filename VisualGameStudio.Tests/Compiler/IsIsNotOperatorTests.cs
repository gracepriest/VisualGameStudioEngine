using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;                  // Lexer, Parser
using BasicLang.Compiler.AST;              // the node types
using BasicLang.Compiler.SemanticAnalysis; // SemanticAnalyzer, TypeInfo, ErrorSeverity
using BasicLang.Compiler.IR;               // IRBuilder etc.
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Net;
using NUnit.Framework;
using VisualGameStudio.Tests.Blnet;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #185 (fix commit <c>ffed9fc1</c>) — <c>Is</c> / <c>IsNot</c> reference identity, front
/// end and IR only (fast subset; the four-backend RUN is <see cref="IsIsNotOperatorExecutionTests"/>).
/// The architect's ruling is ADR-0011 (<c>docs/superpowers/decisions/0011-is-isnot-reference-identity.md</c>);
/// every invariant asserted here is one of D1-D5's numbered items. Expected messages are taken
/// verbatim from the implementer's measured probe matrix (<c>S/t185/matrix-after.txt</c> /
/// <c>matrix-after2-rev.txt</c>), never re-derived.
/// </summary>
[TestFixture]
public class IsIsNotOperatorTests
{
    // ============================================================================================
    // Shared helpers.
    // ============================================================================================

    private const string Prelude =
        "Class C\nEnd Class\nInterface I\nEnd Interface\nDelegate Sub UserDel(x As Integer)\n";

    private static ProgramNode Parse(string body, string prelude = "")
    {
        var source = (prelude.Length > 0 ? prelude + "\n" : "") + body;
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        return program;
    }

    private static (bool ok, List<string> errors, SemanticAnalyzer analyzer, ProgramNode program) Analyze(
        string body, string prelude = "", bool configureNetResolution = false)
    {
        var program = Parse(body, prelude);
        var analyzer = new SemanticAnalyzer();
        if (configureNetResolution)
        {
            // The SAME shared, framework-reading resolver every .NET-boundary fixture in this
            // assembly pays for once (Blnet.NetStubHarness) — production wires an equivalent
            // resolver from CompilerOptions (Compiler.cs:771) for every real CLI/project build.
            // Without it a .NET type name is "unresolvable" and ADMIT-AND-DEFERs (IsNetDelegateType's
            // own doc comment), which is why the D4 .NET-delegate rows below need this explicitly.
            analyzer.ConfigureNetResolution(() => NetStubHarness.SharedResolver.Value, nativeBackend: false);
        }
        var ok = analyzer.Analyze(program);
        var errors = analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();
        return (ok, errors, analyzer, program);
    }

    private static IRModule BuildIrModule(ProgramNode program, SemanticAnalyzer analyzer, bool optimize = false)
    {
        var module = new IRBuilder(analyzer).Build(program, "T");
        if (optimize)
        {
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(module);
        }
        return module;
    }

    private static (bool ok, List<string> errors, IRModule module) AnalyzeAndBuild(
        string body, string prelude = "", bool optimize = false)
    {
        var (ok, errors, analyzer, program) = Analyze(body, prelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
        return (ok, errors, BuildIrModule(program, analyzer, optimize));
    }

    /// <summary>
    /// The first <see cref="BinaryExpressionNode"/> anywhere under <paramref name="root"/> whose
    /// operator is (case-insensitively) <c>Is</c>/<c>IsNot</c> — found by a generic reflection
    /// walk over every public, readable <c>ASTNode</c>-typed or <c>IEnumerable</c>-of-<c>ASTNode</c>
    /// property, so a test never has to know the exact shape (statement/expression nesting) of
    /// the source it wrote. Mirrors <c>OperandWalkerTotalityTests</c>' reflection idiom.
    /// </summary>
    private static BinaryExpressionNode FindIdentityNode(object root)
    {
        var seen = new HashSet<object>();
        BinaryExpressionNode Walk(object node)
        {
            if (node == null || !seen.Add(node)) return null;
            if (node is BinaryExpressionNode bin
                && (string.Equals(bin.Operator, "Is", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(bin.Operator, "IsNot", StringComparison.OrdinalIgnoreCase)))
                return bin;

            if (node is ASTNode)
            {
                foreach (var prop in node.GetType().GetProperties())
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    object value;
                    try { value = prop.GetValue(node); } catch { continue; }
                    if (value is ASTNode child)
                    {
                        var found = Walk(child);
                        if (found != null) return found;
                    }
                    else if (value is IEnumerable list && value is not string)
                    {
                        foreach (var item in list)
                        {
                            if (item is not ASTNode) continue;
                            var found = Walk(item);
                            if (found != null) return found;
                        }
                    }
                }
            }
            return null;
        }
        return Walk(root);
    }

    private static IEnumerable<IRIdentityCompare> AllIdentityInstructions(IRModule module) =>
        module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IRIdentityCompare>();

    // ============================================================================================
    // D1 (1): same source -> identical operator string / IR node from BOTH parsers, at every
    // position the ruling names.
    // ============================================================================================

    private const string ClassPrelude = "Class Foo\nPublic V As Integer\nEnd Class\n";

    [TestCase("Sub Main()\nDim x As Foo = Nothing\nIf x Is Nothing Then\nConsole.WriteLine(\"a\")\nEnd If\nEnd Sub",
        "Is", false, "statement-leading If")]
    [TestCase("Sub Main()\nDim x As Foo = Nothing\nDim b = x Is x\nEnd Sub",
        "Is", false, "Dim initializer (recursive-descent ParseEquality)")]
    [TestCase("Function F(x As Foo) As Boolean\nReturn x IsNot Nothing\nEnd Function\nSub Main()\nEnd Sub",
        "IsNot", true, "Return")]
    [TestCase("Sub Main()\nDim x As Foo = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub",
        "Is", false, "argument position")]
    public void D1_1_SamePosition_GivesTheCanonicalOperatorString_AndAnIRIdentityCompareNode(
        string body, string expectedOp, bool negated, string label)
    {
        var (ok, errors, analyzer, program) = Analyze(body, ClassPrelude);
        Assert.That(ok, Is.True, $"[{label}] " + string.Join("; ", errors));

        var node = FindIdentityNode(program);
        Assert.That(node, Is.Not.Null, $"[{label}] no Is/IsNot BinaryExpressionNode found");
        Assert.That(node.Operator, Is.EqualTo(expectedOp), $"[{label}] operator spelling");
        Assert.That(analyzer.GetNodeType(node)?.Name, Is.EqualTo("Boolean"), $"[{label}] result type");

        var module = BuildIrModule(program, analyzer);
        var identities = AllIdentityInstructions(module).ToList();
        Assert.That(identities, Has.Count.GreaterThanOrEqualTo(1), $"[{label}] no IRIdentityCompare emitted");
        Assert.That(identities.Any(i => i.Negated == negated), Is.True,
            $"[{label}] no IRIdentityCompare with Negated={negated}");
    }

    /// <summary>
    /// D1 (1), the OTHER parser path: a BARE expression statement (no <c>Dim</c>, no assignment,
    /// no call, no <c>If</c>/<c>Return</c>) is parsed by <c>ParseAssignmentOrExpressionStatement</c>
    /// via <c>ParseAssignmentTarget</c> (postfix only) THEN, once it sees the statement is not an
    /// assignment and not a VB-style bare call, <c>ParseExpressionContinuation</c> →
    /// <c>ParseBinaryExpressionContinuation</c> — the precedence-climbing table
    /// (<c>IsBinaryOperator</c>/<c>GetPrecedence</c>), a SEPARATE mechanism from the recursive-descent
    /// chain every other position above uses. Confirmed NOT to be
    /// <c>b = x Is y</c> (an assignment statement): that shape's RHS goes through
    /// <c>ParseExpression()</c> (recursive descent) instead — measured directly, by mutating each
    /// table independently and observing which shapes stop parsing.
    ///
    /// <para>The SEMANTIC ANALYZER separately refuses a discarded-value expression statement
    /// ("Expression is not a statement: its value would be discarded"), unrelated to `Is`/`IsNot`,
    /// so this test checks the PARSE only (operator spelling), not full analysis.</para>
    /// </summary>
    [Test]
    public void D1_1_BareExpressionStatement_UsesThePrecedenceClimbingTable()
    {
        var program = Parse("Sub Main()\nDim x As Foo = Nothing\nx Is x\nEnd Sub", ClassPrelude);
        var node = FindIdentityNode(program);
        Assert.That(node, Is.Not.Null, "no Is/IsNot BinaryExpressionNode found");
        Assert.That(node.Operator, Is.EqualTo("Is"));
    }

    /// <summary>The combination shape (D1 (1)): <c>a IsNot Nothing AndAlso a.V &gt; 0</c> — one
    /// identity node, folded into a short-circuit, plus an ordinary IRCompare for the other half.</summary>
    [Test]
    public void D1_1_AndAlsoCombination_EmitsOneIRIdentityCompare_PlusAnOrdinaryCompare()
    {
        var (ok, errors, module) = AnalyzeAndBuild(
            "Sub Main()\nDim a As Foo = New Foo()\nIf a IsNot Nothing AndAlso a.V > 0 Then\n" +
            "Console.WriteLine(\"y\")\nEnd If\nEnd Sub", ClassPrelude);
        var identities = AllIdentityInstructions(module).ToList();
        Assert.That(identities, Has.Count.EqualTo(1));
        Assert.That(identities[0].Negated, Is.True);

        var compares = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IRCompare>();
        Assert.That(compares.Any(c => c.Comparison == CompareKind.Gt), Is.True,
            "the `a.V > 0` half must still be an ordinary IRCompare, untouched by the identity node");
    }

    /// <summary>D1 (1): whatever case the source used, the STORED operator string is always the
    /// one canonical spelling — <c>BinaryOperatorSpelling</c>'s whole point (E9 probe).</summary>
    [TestCase("is", "Is")]
    [TestCase("Is", "Is")]
    [TestCase("ISNOT", "IsNot")]
    [TestCase("isnot", "IsNot")]
    [TestCase("IsNot", "IsNot")]
    public void D1_1_OperatorSpelling_IsCanonical_RegardlessOfSourceCasing(string written, string canonical)
    {
        var (ok, errors, analyzer, program) = Analyze(
            $"Sub Main()\nDim f As Foo = Nothing\nConsole.WriteLine(f {written} Nothing)\nEnd Sub", ClassPrelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
        var node = FindIdentityNode(program);
        Assert.That(node!.Operator, Is.EqualTo(canonical));
    }

    // ============================================================================================
    // D1 (2): `Case Is Nothing` / `Case Is > 5` keep their dispatch; `Case Is Nothing` still marks
    // WrittenWithIs and lowers to IRNothingPatternCase.
    // ============================================================================================

    [Test]
    public void D1_2_CaseIsNothing_MarksWrittenWithIs_AndStillLowersToIRNothingPatternCase()
    {
        var (ok, errors, analyzer, program) = Analyze(
            "Sub Main()\nDim x As Foo = Nothing\nSelect Case x\nCase Is Nothing\n" +
            "Console.WriteLine(\"n\")\nCase Else\nConsole.WriteLine(\"s\")\nEnd Select\nEnd Sub", ClassPrelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));

        var nothingPattern = FindNode<NothingPatternNode>(program);
        Assert.That(nothingPattern, Is.Not.Null, "no NothingPatternNode found in the AST");
        Assert.That(nothingPattern.WrittenWithIs, Is.True,
            "`Case Is Nothing` must mark WrittenWithIs=true — mutant target: dropping this flag");

        var module = BuildIrModule(program, analyzer);
        var hasNothingPatternCase = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions)
            .OfType<IRSwitch>().SelectMany(s => s.PatternCases).OfType<IRNothingPatternCase>().Any();
        Assert.That(hasNothingPatternCase, Is.True,
            "`Case Is Nothing` must still lower to IRNothingPatternCase, not the new identity node");
    }

    [Test]
    public void D1_2_CaseIsGreaterThan_StillParsesAndRuns()
    {
        var (ok, errors, module) = AnalyzeAndBuild(
            "Sub Main()\nDim i As Integer = 7\nSelect Case i\nCase Is > 5\n" +
            "Console.WriteLine(\"big\")\nCase Else\nConsole.WriteLine(\"small\")\nEnd Select\nEnd Sub");
        var hasComparisonPattern = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions)
            .OfType<IRSwitch>().SelectMany(s => s.PatternCases).OfType<IRComparisonPatternCase>().Any();
        Assert.That(hasComparisonPattern, Is.True, "`Case Is > 5` must still lower to IRComparisonPatternCase");
    }

    private static T FindNode<T>(object root) where T : class
    {
        var seen = new HashSet<object>();
        T Walk(object node)
        {
            if (node == null || !seen.Add(node)) return null;
            if (node is T match) return match;
            if (node is ASTNode)
            {
                foreach (var prop in node.GetType().GetProperties())
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    object value;
                    try { value = prop.GetValue(node); } catch { continue; }
                    if (value is ASTNode child)
                    {
                        var found = Walk(child);
                        if (found != null) return found;
                    }
                    else if (value is IEnumerable list && value is not string)
                    {
                        foreach (var item in list)
                        {
                            if (item is T directMatch) return directMatch;
                            if (item is ASTNode) { var found = Walk(item); if (found != null) return found; }
                        }
                    }
                }
            }
            return null;
        }
        return Walk(root);
    }

    // ============================================================================================
    // D1 (3): `Not x Is Nothing` is never silently `(Not x) Is Nothing`. Since #195 it is VB's
    // `Not (x Is Nothing)`; the explicit tight shape is refused, naming `x IsNot Nothing`.
    // ============================================================================================

    [Test]
    public void D1_3_NotXIsNothing_IsVbsNotOfTheIdentityTest()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim x As Foo = New Foo()\nIf Not x Is Nothing Then\n" +
            "Console.WriteLine(\"set\")\nEnd If\nEnd Sub", ClassPrelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void D1_3_ParenthesisedNotX_IsNothing_IsRefused_NamingTheIsNotForm()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim x As Foo = New Foo()\nIf (Not x) Is Nothing Then\n" +
            "Console.WriteLine(\"set\")\nEnd If\nEnd Sub", ClassPrelude);
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(
            "'(Not x) Is Nothing' applies 'Is' to a Boolean, which is a value and never Nothing. " +
            "Write 'x IsNot Nothing' (or 'Not x Is Nothing')"));
    }

    // ============================================================================================
    // D2: the operand rule, data-driven. Every refusal names its fix.
    // ============================================================================================

    [TestCase("Class C\nEnd Class\nSub Main()\nDim x As C = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "class")]
    [TestCase("Interface I\nEnd Interface\nSub Main()\nDim x As I = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "interface")]
    [TestCase("Sub Main()\nDim x As Object = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "Object")]
    [TestCase("Sub Main()\nDim x As List(Of Integer) = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "List(Of T)")]
    [TestCase("Sub Main()\nDim x As Integer() = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "array")]
    [TestCase("Sub Main()\nDim x As Action = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "Action")]
    [TestCase("Sub Main()\nDim x As Func(Of Integer) = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "Func(Of T)")]
    [TestCase("Delegate Sub UD(n As Integer)\nSub Main()\nDim x As UD = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "user Delegate")]
    [TestCase("Sub Main()\nDim x As String = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "String")]
    [TestCase("Sub Main()\nDim x As Integer? = Nothing\nConsole.WriteLine(x Is Nothing)\nEnd Sub", "nullable")]
    public void D2_AdmittedKinds_AgainstNothing_Analyze(string body, string label)
    {
        var (ok, errors, _, _) = Analyze(body);
        Assert.That(ok, Is.True, $"[{label}] " + string.Join("; ", errors));
    }

    [TestCase("Sub Main()\nDim i As Integer = 1\nDim j As Integer = 1\nConsole.WriteLine(i Is j)\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'i' is 'Integer', a value type. Compare values with '=' instead", "Integer")]
    [TestCase("Sub Main()\nDim i As Double = 1\nConsole.WriteLine(i Is Nothing)\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'i' is 'Double', a value type. Compare values with '=' instead", "Double")]
    [TestCase("Sub Main()\nDim i As Boolean = True\nConsole.WriteLine(i Is Nothing)\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'i' is 'Boolean', a value type. Compare values with '=' instead", "Boolean")]
    [TestCase("Sub Main()\nDim i As Char = \"x\"c\nConsole.WriteLine(i Is Nothing)\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'i' is 'Char', a value type. Compare values with '=' instead", "Char")]
    [TestCase("Enum E\nA\nEnd Enum\nSub Main()\nDim i As E = E.A\nConsole.WriteLine(i Is Nothing)\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'i' is 'E', a value type. Compare values with '=' instead", "enum")]
    [TestCase("Structure Pt\nPublic X As Integer\nEnd Structure\nSub Main()\nDim p As Pt = New Pt()\nConsole.WriteLine(p Is Nothing)\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'p' is 'Pt', a value type. Compare values with '=' instead", "Structure")]
    [TestCase("Function Pick(Of T)(x As T) As Boolean\nReturn x Is Nothing\nEnd Function\nSub Main()\nEnd Sub",
        "'Is' requires operands of a reference or nullable type, but 'x' is 'T', a type parameter that may be a value type. Compare values with '=' instead", "type parameter T")]
    public void D2_RefusedValueTypes_NameTheirOwnFix(string body, string expectedMessage, string label)
    {
        var (ok, errors, _, _) = Analyze(body);
        Assert.That(ok, Is.False, $"[{label}] expected a refusal");
        Assert.That(errors, Has.Some.Contains(expectedMessage), $"[{label}] " + string.Join("; ", errors));
    }

    [Test]
    public void D2_NullableAgainstNonNothingOperand_IsRefused_NamingHasValue()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim n As Integer? = Nothing\nDim m As Integer? = Nothing\nConsole.WriteLine(n Is m)\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(
            "'Is' compares a nullable ('Integer?') only with Nothing. Test '.HasValue' to ask " +
            "whether it holds a value, or compare values with '='"));
    }

    [Test]
    public void D2_UnrelatedClasses_AreRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Class Foo\nEnd Class\nClass Bar\nEnd Class\nSub Main()\nDim f As Foo = New Foo()\n" +
            "Dim b As Bar = New Bar()\nConsole.WriteLine(f Is b)\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(
            "'Is' compares 'Foo' and 'Bar', unrelated types: neither converts to the other, so " +
            "'Is' could never be True. Compare values with '=' if that is what you meant"));
    }

    [TestCase("Class Base\nEnd Class\nClass Derived\nInherits Base\nEnd Class\n" +
        "Sub Main()\nDim d As Derived = New Derived()\nDim b As Base = d\nConsole.WriteLine(b Is d)\nEnd Sub", "base/derived")]
    [TestCase("Interface IThing\nEnd Interface\nClass Thing\nImplements IThing\nEnd Class\n" +
        "Sub Main()\nDim t As Thing = New Thing()\nDim it As IThing = t\nConsole.WriteLine(it Is t)\nEnd Sub", "interface/implementer")]
    [TestCase("Class Foo\nEnd Class\nSub Main()\nDim o As Object = New Foo()\nDim f As Foo = New Foo()\n" +
        "Console.WriteLine(o Is f)\nEnd Sub", "Object/anything")]
    public void D2_RelatedTypes_AreAdmitted(string body, string label)
    {
        var (ok, errors, _, _) = Analyze(body);
        Assert.That(ok, Is.True, $"[{label}] " + string.Join("; ", errors));
    }

    [Test]
    public void D2_NothingIsNothing_IsAdmitted_ResultTypeBoolean()
    {
        var (ok, errors, analyzer, program) = Analyze("Sub Main()\nConsole.WriteLine(Nothing Is Nothing)\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
        var node = FindIdentityNode(program);
        Assert.That(analyzer.GetNodeType(node)?.Name, Is.EqualTo("Boolean"));
    }

    [Test]
    public void D2_ResultType_IsBoolean_OnARefusedComparisonToo()
    {
        // Even when the comparison is refused (D2's own contract: "always typed Boolean, refused
        // or not, so a refusal never cascades into a second error" — VisitIdentityComparison's
        // own doc comment).
        var (ok, errors, analyzer, program) = Analyze(
            "Sub Main()\nDim i As Integer = 1\nDim j As Integer = 1\nConsole.WriteLine(i Is j)\nEnd Sub");
        Assert.That(ok, Is.False);
        var node = FindIdentityNode(program);
        Assert.That(analyzer.GetNodeType(node)?.Name, Is.EqualTo("Boolean"));
        Assert.That(errors, Has.Count.EqualTo(1), "a typed-anyway refusal must not cascade into a second error");
    }

    // ============================================================================================
    // D2 (2): `Case Is Nothing` follows the SAME operand rule. `Case Nothing` (no `Is`) is VB's
    // value comparison and is NOT judged here — MsilRoundTripTests.SelectCase_Nothing_MatchesTheDefaultValue
    // pins that it stays legal on a value type.
    // ============================================================================================

    [Test]
    public void D2_2_CaseIsNothing_OnAValueType_IsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim i As Integer = 0\nSelect Case i\nCase Is Nothing\n" +
            "Console.WriteLine(\"null\")\nCase Else\nConsole.WriteLine(\"other\")\nEnd Select\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(
            "'Case Is Nothing' requires a Select Case value of a reference or nullable type, but " +
            "it is 'Integer', a value type. Compare values instead ('Case …' / 'Case Is = …')"));
    }

    [Test]
    public void D2_2_PlainCaseNothing_OnAValueType_IsNotRefused()
    {
        // Contrast case for the mutant that would fold this into the same rule: `Case Nothing`
        // (no `Is`) is VB's value comparison against the type's default, never judged by
        // CheckCaseIsNothingOperand — see MsilRoundTripTests.SelectCase_Nothing_MatchesTheDefaultValue.
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim i As Integer = 0\nSelect Case i\nCase 0\n" +
            "Console.WriteLine(\"zero\")\nCase Else\nConsole.WriteLine(\"other\")\nEnd Select\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ============================================================================================
    // D4: non-portable identity — String and delegate — refused on every target because it is a
    // FRONT-END refusal (never reaches emission at all).
    // ============================================================================================

    [Test]
    public void D4_StringIdentity_IsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim s1 As String = \"a\"\nDim s2 As String = \"a\"\nConsole.WriteLine(s1 Is s2)\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(
            "'Is' between String operands is refused: String identity is not portable (it depends " +
            "on interning on .NET, and is a value comparison on JavaScript and C++). Compare values " +
            "with '=', or test a String against Nothing ('s Is Nothing')"));
    }

    [Test]
    public void D4_ActionIdentity_IsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim f As Action = Sub()\nConsole.Write(\"\")\nEnd Sub\nDim g As Action = f\n" +
            "Console.WriteLine(f Is g)\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(
            "'Is' between delegate operands ('Action') is refused: delegate identity is not " +
            "portable (a C++ delegate has none). Test a delegate against Nothing instead ('f Is Nothing')"));
    }

    [Test]
    public void D4_UserDelegateIdentity_IsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Delegate Sub UD(n As Integer)\nSub Main()\nDim a As UD = Nothing\nDim b As UD = a\n" +
            "Console.WriteLine(a Is b)\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains("'Is' between delegate operands ('UD') is refused"));
    }

    [Test]
    public void D4_StringAgainstNothing_AndDelegateAgainstNothing_StayLegal()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim s As String = Nothing\nConsole.WriteLine(s Is Nothing)\n" +
            "Dim d As Action = Nothing\nConsole.WriteLine(d IsNot Nothing)\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    /// <summary>
    /// D4, the .NET-delegate classification (<c>IsNetDelegateType</c>): <c>EventHandler</c> is a
    /// real .NET delegate the type table nonetheless types as a plain CLASS (per
    /// <c>IsDelegateForIdentity</c>'s own doc comment) — it is caught only by asking the .NET
    /// resolver. Kills the mutant "the IsNetDelegateType call removed": without that call,
    /// <c>IsDelegateForIdentity</c> would fall through, <c>AreRelatedForIdentity</c> would admit
    /// two same-typed EventHandlers (assignable to itself), and this would silently COMPILE.
    /// Measured (probe R10, matrix-after2-rev.txt): refused on all four backends.
    /// </summary>
    [Test]
    public void D4_NetDelegateIdentity_EventHandler_IsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim h As EventHandler = Nothing\nDim g As EventHandler = Nothing\n" +
            "Console.WriteLine(h Is g)\nEnd Sub", "", configureNetResolution: true);
        Assert.That(ok, Is.False,
            "expected a refusal — if this is green, IsNetDelegateType's call was dropped " +
            "(IsDelegateForIdentity falls through and AreRelatedForIdentity admits it)");
        // The message hardcodes the placeholder spelling "'f Is Nothing'" (VisitIdentityComparison's
        // literal text), independent of the program's own variable name — matches R10's measured text.
        Assert.That(errors, Has.Some.Contains(
            "'Is' between delegate operands ('EventHandler') is refused: delegate identity is " +
            "not portable (a C++ delegate has none). Test a delegate against Nothing instead " +
            "('f Is Nothing')"));
    }

    /// <summary>Same classification, the <c>IsNot</c> spelling and a GENERIC .NET delegate
    /// (<c>Predicate(Of Integer)</c> — Roslyn resolves its generic arity too). Probe R10b.</summary>
    [Test]
    public void D4_NetDelegateIdentity_PredicateOfInteger_IsNotIsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim p As Predicate(Of Integer) = Nothing\nDim q As Predicate(Of Integer) = Nothing\n" +
            "Console.WriteLine(p IsNot q)\nEnd Sub", "", configureNetResolution: true);
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Matches<string>(m =>
            m.Contains("'IsNot' between delegate operands") && m.Contains("Predicate")));
    }

    /// <summary>Anti-vacuity control for the two tests above: WITHOUT a configured .NET resolver
    /// (the shape a bare `new SemanticAnalyzer()` unit test would otherwise use), the identical
    /// source is ADMITTED — IsNetDelegateType's own doc comment: "When there is no resolver ...
    /// the answer is FALSE: the type is admitted and the target compiler decides." Proves the
    /// two tests above are actually exercising resolver-backed classification, not merely
    /// succeeding for an unrelated reason.</summary>
    [Test]
    public void D4_NetDelegateIdentity_EventHandler_WithNoResolverConfigured_IsAdmitted()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim h As EventHandler = Nothing\nDim g As EventHandler = Nothing\n" +
            "Console.WriteLine(h Is g)\nEnd Sub", "", configureNetResolution: false);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    /// <summary>
    /// A BARE <c>Delegate</c> as a type name cannot be spelled — <c>Delegate</c> is a lexer
    /// keyword (<c>BasicLangLexer.cs</c>: <c>{ "Delegate", TokenType.Delegate }</c>) and
    /// <c>ParseTypeReferenceCore</c>'s base-type-name branch requires
    /// <c>Check(TokenType.Identifier)</c>, which a keyword token never satisfies. Recorded here
    /// (not asserted as unreachable) because the DOTTED spelling below IS reachable — see that
    /// test's own doc comment for why the two differ.
    /// </summary>
    [Test]
    public void D4_BareDelegateKeyword_CannotBeSpelled_AsABasicLangTypeReference()
    {
        // Parser.Parse() recovers from a ParseException per statement (Synchronize()) rather than
        // throwing it out, so the failure shows up as a recorded parse error, not a thrown
        // exception — confirmed by reading Parser.cs's per-statement try/catch sites.
        var parser = new Parser(new Lexer("Sub Main()\nDim d As Delegate = Nothing\nEnd Sub").Tokenize());
        parser.Parse();
        var messages = parser.Errors.Select(e => e.Message).ToList();
        Assert.That(messages, Is.Not.Empty, "expected a parse failure");
        Assert.That(messages.Any(m => m.Contains("type name")), Is.True,
            "expected an 'Expected type name...' parse error; got: " + string.Join("; ", messages));
    }

    /// <summary>
    /// D4's .NET-delegate classification, the OTHER mutant: the <c>System.Delegate</c> /
    /// <c>System.MulticastDelegate</c> ROOT CHECK inside <c>IsNetDelegateType</c> — the arm that
    /// answers <c>true</c> when the RESOLVED full name is one of the two delegate roots
    /// THEMSELVES (which Roslyn types <c>TypeKind.Class</c>, not <c>TypeKind.Delegate</c>, so the
    /// fallback <c>NetResolver().ResolveType(fullName)?.Kind == Delegate</c> answers false for
    /// them and the root check is the only thing that catches this shape).
    ///
    /// <para>⛔ <b>IS constructible — verified by lexing it, not assumed from the keyword table.</b>
    /// The bare word <c>Delegate</c> is a keyword, but <c>BasicLangLexer</c> has a DELIBERATE
    /// contextual exception: "After a dot, treat everything as an identifier (member access) —
    /// this allows using keywords as member names like obj.Property." So the DOTTED name
    /// <c>System.Delegate</c> lexes as <c>Identifier(System) Dot Identifier(Delegate)</c> (measured
    /// by tokenizing it directly) and parses as an ordinary qualified type reference — unlike the
    /// bare keyword above. <c>ResolveNetType</c>'s last candidate is the name AS WRITTEN
    /// (<c>NetCandidateNames</c>'s final <c>yield return name + suffix</c>), so
    /// <c>"System.Delegate"</c> resolves directly to itself without needing any ambient-namespace
    /// search.</para>
    /// </summary>
    [Test]
    public void D4_SystemDelegateRootType_Identity_IsRefused()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim d As System.Delegate = Nothing\nDim e As System.Delegate = Nothing\n" +
            "Console.WriteLine(d Is e)\nEnd Sub", "", configureNetResolution: true);
        Assert.That(ok, Is.False,
            "expected a refusal — mutant target: the System.Delegate/MulticastDelegate root " +
            "check removed (IsNetDelegateType would then answer false for System.Delegate itself, " +
            "since Roslyn types the root TypeKind.Class rather than TypeKind.Delegate, and " +
            "AreRelatedForIdentity would admit two same-typed operands)");
        Assert.That(errors, Has.Some.Matches<string>(m =>
            m.Contains("'Is' between delegate operands") && m.Contains("System.Delegate")),
            string.Join("; ", errors));
    }

    /// <summary>Anti-vacuity control: against <c>Nothing</c> a <c>System.Delegate</c>-typed
    /// operand stays legal (D4 (2)) — the refusal above is about TWO delegate operands, not about
    /// the type being unsupported outright.</summary>
    [Test]
    public void D4_SystemDelegateRootType_AgainstNothing_StaysLegal()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim d As System.Delegate = Nothing\nConsole.WriteLine(d Is Nothing)\nEnd Sub",
            "", configureNetResolution: true);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    /// <summary>Anti-vacuity control mirroring <c>D4_NetDelegateIdentity_EventHandler_WithNoResolverConfigured_IsAdmitted</c>:
    /// without a configured resolver the SAME source is admitted (defer-to-target-compiler), so
    /// the refusal above is genuinely resolver-driven, not incidental.</summary>
    [Test]
    public void D4_SystemDelegateRootType_WithNoResolverConfigured_IsAdmitted()
    {
        var (ok, errors, _, _) = Analyze(
            "Sub Main()\nDim d As System.Delegate = Nothing\nDim e As System.Delegate = Nothing\n" +
            "Console.WriteLine(d Is e)\nEnd Sub", "", configureNetResolution: false);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ============================================================================================
    // D5: IR shape, at the optimizer level (no native compile/run needed for these — see
    // IsIsNotOperatorExecutionTests for the four-backend RUN of the same invariants).
    // ============================================================================================

    [Test]
    public void D5_TheNode_IsIRIdentityCompare_NeverIRBinaryOpOrIRCompare_OnEitherPipeline()
    {
        const string src = "Class Foo\nEnd Class\nSub Main()\nDim a As Foo = New Foo()\nDim b As Foo = New Foo()\n" +
            "Console.WriteLine(a Is b)\nEnd Sub";

        var (_, _, analyzer, program) = Analyze(src);
        Assert.That(analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error), Is.Empty);

        var unoptimized = BuildIrModule(program, analyzer, optimize: false);
        AssertOnlyIdentityNodeForThisComparison(unoptimized, "unoptimized (mutant: 'lowered to Eq')");

        var (_, _, analyzer2, program2) = Analyze(src);
        var optimized = BuildIrModule(program2, analyzer2, optimize: true);
        AssertOnlyIdentityNodeForThisComparison(optimized, "optimized (mutant: 'lowered to Eq')");
    }

    private static void AssertOnlyIdentityNodeForThisComparison(IRModule module, string label)
    {
        var identities = AllIdentityInstructions(module).ToList();
        Assert.That(identities, Has.Count.EqualTo(1), $"[{label}]");
        var eqCompares = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions)
            .OfType<IRCompare>().Where(c => c.Comparison is CompareKind.Eq or CompareKind.Ne).ToList();
        Assert.That(eqCompares, Is.Empty,
            $"[{label}] `a Is b` must never be lowered to an IRCompare Eq/Ne — that would let a " +
            "user Operator=/Delegate.op_Equality/String value equality answer it");
    }

    [Test]
    public void D5_1_Optimizer_Folds_NothingIsNothing_AndNothingIsNotNothing_ToAConstant()
    {
        foreach (var (op, expected) in new[] { ("Is", true), ("IsNot", false) })
        {
            var (_, _, module) = AnalyzeAndBuild(
                $"Sub Main()\nConsole.WriteLine(Nothing {op} Nothing)\nEnd Sub", optimize: true);
            var identities = AllIdentityInstructions(module).ToList();
            Assert.That(identities, Is.Empty, $"[{op}] Nothing {op} Nothing must be folded away, not left as a node");

            // The folded constant may show up as an IRAssignment's value OR directly as a call
            // ARGUMENT (`Console.WriteLine(Nothing Is Nothing)` has no Dim to assign into) — check
            // both rather than assuming a shape.
            var instructions = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).ToList();
            var candidates = instructions.OfType<IRAssignment>().Select(a => a.Value)
                .Concat(instructions.OfType<IRCall>().SelectMany(c => c.Arguments))
                .OfType<IRConstant>().Where(c => c.Value is bool).ToList();
            Assert.That(candidates.Any(c => (bool)c.Value == expected), Is.True,
                $"[{op}] expected a folded constant {expected}");
        }
    }

    /// <summary>D5 (1): the fold is EXACTLY "both operands Nothing" — never widened to fold
    /// `x Is x` (same-variable identity), which is always semantically True but is NOT one of the
    /// two folds the ruling permits. Hand-built IR: the SAME <c>IRVariable</c> instance on both
    /// sides, non-Nothing, run through <c>ConstantFoldingPass</c> directly.</summary>
    [Test]
    public void D5_1_Optimizer_DoesNotFold_XIsX_EvenThoughAlwaysTrue()
    {
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);
        var module = new IRModule("M");
        var function = new IRFunction("Main", boolType);
        var a = new IRVariable("a", intType) { IsParameter = true };
        function.Parameters.Add(a);
        var block = function.CreateBlock("entry");
        var identity = new IRIdentityCompare("t0", a, a, false, boolType);
        block.AddInstruction(identity);
        var show = new IRCall("", "Show", boolType); show.Arguments.Add(identity);
        block.AddInstruction(show);
        block.AddInstruction(new IRReturn(null));
        module.Functions.Add(function);

        new ConstantFoldingPass().Run(module);

        Assert.That(block.Instructions.OfType<IRIdentityCompare>().Any(i => ReferenceEquals(i, identity)), Is.True,
            "mutant target: the fold widened to also fold `x Is x` — it must not, per the ruling " +
            "(D5 (1): fold ONLY Nothing Is/IsNot Nothing)");
    }

    /// <summary>D5 (2): CSE must never merge an <c>IRIdentityCompare</c> with an <c>IRCompare Eq</c>
    /// of the SAME operands — they can answer differently (a user <c>Operator=</c> may run on the
    /// Eq side and never on the identity side, D4 (1)). Hand-built IR, run through
    /// <c>CommonSubexpressionEliminationPass</c> directly.</summary>
    [Test]
    public void D5_2_Cse_NeverMerges_IRIdentityCompare_WithAnIRCompareEq_OfTheSameOperands()
    {
        var refType = new TypeInfo("Foo", TypeKind.Class);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);
        var module = new IRModule("M");
        var function = new IRFunction("F", boolType);
        var a = new IRVariable("a", refType) { IsParameter = true };
        var b = new IRVariable("b", refType) { IsParameter = true };
        function.Parameters.Add(a); function.Parameters.Add(b);
        var block = function.CreateBlock("entry");

        var identity = new IRIdentityCompare("t0", a, b, false, boolType);
        block.AddInstruction(identity);
        var show1 = new IRCall("", "Show", boolType); show1.Arguments.Add(identity);
        block.AddInstruction(show1);

        var compare = new IRCompare("t1", CompareKind.Eq, a, b, boolType);
        block.AddInstruction(compare);
        var show2 = new IRCall("", "Show", boolType); show2.Arguments.Add(compare);
        block.AddInstruction(show2);
        block.AddInstruction(new IRReturn(null));
        module.Functions.Add(function);

        var cse = new CommonSubexpressionEliminationPass();
        cse.Run(module);

        Assert.That(cse.ModificationCount, Is.EqualTo(0),
            "CSE must not treat `a Is b` and `a = b` (Eq) over the same operands as the same " +
            "expression — they can answer differently");
        Assert.That(block.Instructions.OfType<IRIdentityCompare>().Count(), Is.EqualTo(1));
        Assert.That(block.Instructions.OfType<IRCompare>().Count(), Is.EqualTo(1));
    }

    /// <summary>D5's operand-visibility obligations: a <c>ReplaceUses</c> rewrite reaches BOTH
    /// operands (a missing walker arm leaves a use pointing at a replaced instruction).</summary>
    [Test]
    public void D5_ReplaceUses_RewritesBothOperandsOfAnIRIdentityCompare()
    {
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);
        var left = new IRVariable("left", intType);
        var right = new IRVariable("right", intType);
        var identity = new IRIdentityCompare("t0", left, right, false, boolType);

        var newLeft = new IRVariable("newLeft", intType);
        var newRight = new IRVariable("newRight", intType);
        ReplaceUsesProbe.Replace(identity, left, newLeft);
        ReplaceUsesProbe.Replace(identity, right, newRight);

        Assert.That(identity.Left, Is.SameAs(newLeft), "the Left operand must be rewritten");
        Assert.That(identity.Right, Is.SameAs(newRight), "the Right operand must be rewritten");
    }

    private sealed class ReplaceUsesProbe : OptimizationPass
    {
        private ReplaceUsesProbe() : base("ReplaceUsesProbe") { }
        public override bool Run(IRModule module) => false;
        internal static void Replace(IRInstruction instruction, IRValue oldValue, IRValue newValue)
            => ReplaceUses(new[] { instruction }, oldValue, newValue);
    }

    /// <summary>CopyPropagation must propagate into BOTH operands of an <c>IRIdentityCompare</c>
    /// when they are ordinary copies — the "removing this only falls back to conservative
    /// behaviour" class of mutant the implementer flagged; asserted directly at the IR level so a
    /// dropped arm shows up as a MISSING rewrite, not merely as "still correct by accident".</summary>
    [Test]
    public void D5_CopyPropagation_PropagatesIntoBothOperandsOfAnIRIdentityCompare()
    {
        var refType = new TypeInfo("Foo", TypeKind.Class);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);
        var module = new IRModule("M");
        var function = new IRFunction("F", boolType);
        var src1 = new IRVariable("src1", refType) { IsParameter = true };
        var src2 = new IRVariable("src2", refType) { IsParameter = true };
        function.Parameters.Add(src1); function.Parameters.Add(src2);
        var copy1 = new IRVariable("copy1", refType);
        var copy2 = new IRVariable("copy2", refType);
        function.LocalVariables.Add(copy1); function.LocalVariables.Add(copy2);

        var block = function.CreateBlock("entry");
        block.AddInstruction(new IRAssignment(copy1, src1));
        block.AddInstruction(new IRAssignment(copy2, src2));
        var identity = new IRIdentityCompare("t0", copy1, copy2, false, boolType);
        block.AddInstruction(identity);
        var show = new IRCall("", "Show", boolType); show.Arguments.Add(identity);
        block.AddInstruction(show);
        block.AddInstruction(new IRReturn(null));
        module.Functions.Add(function);

        new CopyPropagationPass().Run(module);

        Assert.Multiple(() =>
        {
            Assert.That(identity.Left, Is.SameAs(src1), "Left must be propagated to the copy's source");
            Assert.That(identity.Right, Is.SameAs(src2), "Right must be propagated to the copy's source");
        });
    }

    /// <summary>The CopyProp LAMBDA EXCLUSION (mutant: "the CopyProp lambda exclusion removed"):
    /// a variable holding a lambda reference (named <c>__lambda_*</c>, ClosureLowering's/every
    /// backend's own convention for a lambda value) must NOT be propagated INTO an
    /// <c>IRIdentityCompare</c> operand — every backend renders a lambda reference as the
    /// expression itself at its use site, and <c>(() => {…}) === null</c> /
    /// <c>[=]() {…} == nullptr</c> is not valid on either target (measured by the implementer).
    /// The variable holding it is left in place, which is the same answer at the value level.</summary>
    [Test]
    public void D5_CopyPropagation_DoesNotPropagate_ALambdaReferenceVariable_IntoAnIdentityOperand()
    {
        var delegateType = new TypeInfo("Delegate", TypeKind.Delegate);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);
        var module = new IRModule("M");
        var function = new IRFunction("F", boolType);
        var lambdaRef = new IRVariable("__lambda_0", delegateType);
        var copy = new IRVariable("copy", delegateType);
        function.LocalVariables.Add(copy);

        var block = function.CreateBlock("entry");
        block.AddInstruction(new IRAssignment(copy, lambdaRef));
        var nothingLiteral = new IRConstant(null, delegateType);
        var identity = new IRIdentityCompare("t0", copy, nothingLiteral, false, boolType);
        block.AddInstruction(identity);
        var show = new IRCall("", "Show", boolType); show.Arguments.Add(identity);
        block.AddInstruction(show);
        block.AddInstruction(new IRReturn(null));
        module.Functions.Add(function);

        new CopyPropagationPass().Run(module);

        Assert.That(identity.Left, Is.SameAs(copy),
            "a lambda-reference copy must NOT be propagated into the identity node's operand — " +
            "mutant target: the CopyProp lambda exclusion removed");
    }

    /// <summary>
    /// <c>OptimizationPass.UsesOf</c> (the walker DCE's <c>UsedValues</c>, CSE and every other
    /// consumer of "what does this instruction use" is built on — <see cref="MapUses"/>'s own
    /// arm set) must yield BOTH operands of an <c>IRIdentityCompare</c>. A missing arm here is
    /// exactly how a live value gets deleted out from under its consumer — this fixture's sibling
    /// <c>OperandWalkerTotalityTests</c> covers the SAME arm generically, by reflection, for
    /// every IR node kind; this test names <c>IRIdentityCompare</c> specifically, for this task.
    ///
    /// <para>⚠ <c>DeadCodeEliminationPass</c> itself is NOT the right oracle for "is this pure
    /// node's dead-ness recognised": its own doc comment records that its removal guard skips any
    /// value whose name does not start with <c>_tmp</c>, and IRBuilder names every real temp
    /// <c>t0</c>, <c>t1</c>, … — so in a real program this pass removes NO instruction at all
    /// (only unreachable blocks). <c>UsesOf</c> is the shared mechanism that actually matters.</para>
    /// </summary>
    [Test]
    public void D5_UsesOf_YieldsBothOperandsOfAnIRIdentityCompare()
    {
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);
        var left = new IRVariable("left", intType);
        var right = new IRVariable("right", intType);
        var identity = new IRIdentityCompare("t0", left, right, false, boolType);

        var uses = OptimizationPass.UsesOf(identity);

        Assert.That(uses.Any(v => ReferenceEquals(v, left)), Is.True, "Left must be a use");
        Assert.That(uses.Any(v => ReferenceEquals(v, right)), Is.True, "Right must be a use");
    }
}
