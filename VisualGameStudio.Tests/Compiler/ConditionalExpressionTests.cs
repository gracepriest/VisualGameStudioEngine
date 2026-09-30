using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #123, D2 — VB's conditional operator `If(cond, whenTrue, whenFalse)`.
//
//  ⭐ THE ORACLE IS vbc. The result-type table is what vbc says the STATIC type of each expression is (a generic
//  `TN(Of T)(v As T) = GetType(T).Name` over the expression, so `If(t, 1, "one")` reads Object and not the runtime type
//  Integer; S/t123/tw/d2gen.py). The diagnostics are vbc's where vbc has one (BC30491 for a Sub as an operand) and
//  BasicLang's own choice where it differs, each named below.
//
//  ⛔ WHERE BASICLANG DELIBERATELY DIFFERS FROM vbc — pinned as the DECISION, not as a defect:
//    - `If(c, 1, Nothing)` is refused ("Nothing has no value of type 'Integer'; write 0"); vbc gives 0. It is the rule
//      `Dim x As Integer = Nothing` already has.
//    - The two-argument `If(value, fallback)` (coalesce) is a parse diagnostic; vbc accepts it. Filed as a follow-up.
//    - A module-scope `Const K = If(True, 1, 2.5)` is refused ("cannot be computed at compile time"); vbc folds it. The
//      LOCAL spelling compiles (i8const runs it).
//  ⛔ NOT ASSERTED — `CByte(1)` and `CShort(1)` are typed Object by the analyzer (pre-existing), so `If(t, CByte(1), 2)` is
//  Object here and Integer in vbc. The Byte/Short rows use typed variables, where the rule matches.
//
//  The EVALUATION contract (only the chosen operand runs; the same in every backend) is in the execution fixture
//  UntypedConstAndConditionalExecutionTests. Here: parse shape, type rule, diagnostics, where it is refused, IR shape.
// ================================================================================================

internal static class ConditionalProbes
{
    /// <summary>(name, expression, vbc's static type). The preamble declares the names the expressions read.</summary>
    internal static readonly (string Name, string Expr, string Vb)[] Rows =
    {
        ("int_int", "If(t, 1, 2)", "Integer"),
        ("int_dbl", "If(t, 1, 2.5)", "Double"),
        ("dbl_int", "If(t, 2.5, 1)", "Double"),
        ("int_lng", "If(t, 1, 2L)", "Long"),
        ("lng_int", "If(t, 1L, 2)", "Long"),
        ("sng_int", "If(t, 1.5F, 3)", "Single"),
        ("sng_dbl", "If(t, 1.5F, 2.5)", "Double"),
        ("lng_sng", "If(t, 1L, 2.5F)", "Single"),
        ("str_str", "If(t, \"a\", \"b\")", "String"),
        ("bool_bool", "If(t, True, False)", "Boolean"),
        ("chr_chr", "If(t, \"a\"c, \"b\"c)", "Char"),
        ("int_str", "If(t, 1, \"one\")", "Object"),
        ("str_int", "If(t, \"one\", 1)", "Object"),
        ("nothing_str", "If(t, Nothing, \"x\")", "String"),
        ("str_nothing", "If(t, \"x\", Nothing)", "String"),
        ("dog_nothing", "If(t, New Dog(), Nothing)", "Dog"),
        ("nothing_dog", "If(t, Nothing, New Dog())", "Dog"),
        ("dog_animal", "If(t, New Dog(), New Animal())", "Animal"),
        ("animal_dog", "If(t, New Animal(), New Dog())", "Animal"),
        ("dog_cat", "If(t, New Dog(), New Cat())", "Object"),
        ("nothing_nothing", "If(t, Nothing, Nothing)", "Object"),
        ("nested", "If(t, If(f, 1, 2.5), 3)", "Double"),
        ("int_bool", "If(t, 1, True)", "Object"),
        ("obj_int", "If(t, o, 1)", "Object"),
        ("dog_iface", "If(t, New Dog(), d2)", "IPet"),
        ("byte_int", "If(t, bv, 2)", "Integer"),
        ("short_short", "If(t, sv, sv)", "Short"),
        ("short_int", "If(t, sv, 2)", "Integer"),
        ("byte_byte", "If(t, bv, bv)", "Byte"),
        ("int_dvar", "If(t, 1, dv)", "Double"),
    };

    private const string Preamble = """
        Class Animal
        End Class
        Class Dog
            Inherits Animal
            Implements IPet
        End Class
        Class Cat
            Inherits Animal
        End Class
        Interface IPet
        End Interface

        """;

    /// <summary>A program whose <c>Main</c> declares the names a row reads, then <c>Dim x = &lt;expr&gt;</c>.</summary>
    internal static string ProgramOf(string expression) => Preamble + $"""
        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Dim o As Object = 5
            Dim d2 As IPet = New Dog()
            Dim bv As Byte = 1
            Dim sv As Short = 1
            Dim dv As Double = 1.5
            Dim x = {expression}
        End Sub
        """;

    internal static string InMain(string body) => "Sub Main()\n" + string.Join("\n", body.Split('\n').Select(l => "    " + l)) + "\nEnd Sub\n";

    internal const string Callees = """
        Function F() As Integer
            Return 1
        End Function
        Function G() As Integer
            Return 2
        End Function

        """;
}

/// <summary>D2 (#123): the conditional operator's parse shape, result type, diagnostics and IR shape.</summary>
[TestFixture]
public class ConditionalExpressionTests
{
    private static IEnumerable<TestCaseData> TypeRows()
        => ConditionalProbes.Rows.Select(r => new TestCaseData(r.Name, r.Expr, r.Vb).SetName($"{r.Name}_is_{r.Vb}"));

    /// <summary>The table is the proof, so its shape is pinned (and this is the fixture's plain <c>[Test]</c>).</summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConditionalProbes.Rows, Has.Length.EqualTo(30));
            Assert.That(ConditionalProbes.Rows.Select(r => r.Name).Distinct().Count(), Is.EqualTo(30));
            Assert.That(ConditionalProbes.Rows.Select(r => r.Vb).Distinct().OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Animal", "Boolean", "Byte", "Char", "Dog", "Double", "IPet", "Integer", "Long", "Object", "Short", "Single", "String" }),
                "dominant widening, Object for no dominant type, a class and its base, a class and its interface");
        });
    }

    // ============================================================================================
    // The result type — the dominant type of the two operands
    // ============================================================================================

    /// <summary>
    /// The analyzer's type for an <c>If()</c> is vbc's static type: the operand type the other widens to (Double for
    /// Integer and Double, Long for Integer and Long, Single for Long and Single, the base for a derived class and its
    /// base, the interface for a class and an interface it implements), Object when neither widens, and a
    /// <c>Nothing</c> operand takes the other's type. Mutant M4first (the first operand's type) fails the Double rows.
    /// </summary>
    [TestCaseSource(nameof(TypeRows))]
    public void TheResultType_IsVbcsStaticType(string name, string expression, string vb)
    {
        var run = T123Front.Run(ConditionalProbes.ProgramOf(expression));
        var node = run.Nodes<ConditionalExpressionNode>().FirstOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(run.AllErrors, Is.Empty, "the program is accepted");
            Assert.That(node, Is.Not.Null, "the expression parses to a ConditionalExpressionNode");
            Assert.That(run.TypeOf(node), Is.EqualTo(vb), expression);
        });
    }

    /// <summary>The type is what a later `Dim` infers: a Double conditional keeps 2.5, an Integer one narrows it.</summary>
    [Test]
    public void AnInferredDim_TakesTheConditionalsType()
    {
        var run = T123Front.Run(ConditionalProbes.InMain("""
            Dim t As Boolean = True
            Dim w = If(t, 1, 2.5)
            Dim n = If(t, 1, 2)
            """));
        var declarations = run.Nodes<VariableDeclarationNode>().ToDictionary(v => v.Name);

        Assert.Multiple(() =>
        {
            Assert.That(run.AllErrors, Is.Empty);
            Assert.That(run.TypeOf(declarations["w"]), Is.EqualTo("Double"));
            Assert.That(run.TypeOf(declarations["n"]), Is.EqualTo("Integer"));
        });
    }

    // ============================================================================================
    // The parse shape
    // ============================================================================================

    [Test]
    public void IfWithThreeArguments_InExpressionPosition_IsAConditionalExpressionNode()
    {
        var run = T123Front.Run(ConditionalProbes.InMain("""
            Dim a As Boolean = True
            Dim r = If(a, 1, 2)
            """));
        var conditionals = run.Nodes<ConditionalExpressionNode>();

        Assert.Multiple(() =>
        {
            Assert.That(run.AllErrors, Is.Empty);
            Assert.That(conditionals, Has.Count.EqualTo(1));
            Assert.That(conditionals[0].Condition, Is.InstanceOf<IdentifierExpressionNode>());
            Assert.That(conditionals[0].WhenTrue, Is.InstanceOf<LiteralExpressionNode>());
            Assert.That(conditionals[0].WhenFalse, Is.InstanceOf<LiteralExpressionNode>());
        });
    }

    [Test]
    public void ANestedIf_IsAConditionalInsideTheOperandOfAnother()
    {
        var run = T123Front.Run(ConditionalProbes.InMain("""
            Dim a As Boolean = True
            Dim b As Boolean = False
            Dim r = If(a, If(b, 1, 2), 3)
            """));
        var conditionals = run.Nodes<ConditionalExpressionNode>();

        Assert.Multiple(() =>
        {
            Assert.That(run.AllErrors, Is.Empty);
            Assert.That(conditionals, Has.Count.EqualTo(2));
            Assert.That(conditionals[0].WhenTrue, Is.SameAs(conditionals[1]), "the inner one is the outer's whenTrue");
        });
    }

    /// <summary>
    /// `If (x > 0) Then` at the START of a statement is the If STATEMENT, however it is spelled: the conditional is
    /// expression position only. Ten expression positions, each one conditional, is ten nodes.
    /// </summary>
    [Test]
    public void AnIfStatement_IsNotAConditional_AndEveryExpressionPositionIs()
    {
        var statements = T123Front.Run("""
            Sub Main()
                Dim x As Integer = 1
                Dim y As Integer = 0
                If (x > 0) Then
                    y = 1
                End If
                If (x > 5) Then y = 2
                If (x > 5) Then
                    y = 3
                ElseIf (x > 0) Then
                    y = 4
                Else
                    y = 5
                End If
            End Sub
            """);
        var positions = T123Front.Run("""
            Class P
                Public V As Integer
                Public Sub New(v As Integer)
                    Me.V = v
                End Sub
            End Class
            Function Sgn2(n As Integer) As Integer
                Return If(n > 0, 1, If(n < 0, -1, 0))
            End Function
            Sub Main()
                Dim t As Boolean = True
                Dim n As Integer = 5
                If If(t, n > 3, n < 3) Then
                    Console.WriteLine("cond")
                End If
                Dim a = {If(t, 1, 2), 5}
                Dim p As New P(If(t, 7, 8))
                Console.WriteLine($"{If(t, "x", "y")}")
                Select Case If(t, n, -n)
                    Case 1
                        Console.WriteLine("one")
                End Select
                Console.WriteLine(If(t, "a", "b") & "c")
                Dim q = If(t, 1, 2) + If(t, 3, 4)
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(statements.AllErrors, Is.Empty);
            Assert.That(statements.Nodes<ConditionalExpressionNode>(), Is.Empty, "an If statement is not a conditional");
            Assert.That(statements.Nodes<IfStatementNode>(), Has.Count.EqualTo(3));
            Assert.That(positions.AllErrors, Is.Empty);
            Assert.That(positions.Nodes<ConditionalExpressionNode>(), Has.Count.EqualTo(10),
                "return + nested, an If statement's condition, an array element, a constructor argument, an interpolation hole, a Select Case subject, a concatenation operand, and two operands of `+`");
        });
    }

    /// <summary>The AST printer knows the node (every visitor implements the interface, so a missing arm would not compile).</summary>
    [Test]
    public void TheAstPrinter_PrintsTheConditional()
    {
        var run = T123Front.Run(ConditionalProbes.InMain("Dim r = If(True, 1, 2)"));
        var printer = new ASTPrettyPrinter();
        run.Ast.Accept(printer);

        Assert.That(printer.GetOutput(), Does.Contain("If():"));
    }

    // ============================================================================================
    // Arity — only the three-argument form exists
    // ============================================================================================

    private static IEnumerable<TestCaseData> ArityRows()
    {
        yield return new TestCaseData("Dim s As String = Nothing\nDim r = If(s, \"fallback\")",
            "The two-argument If(value, fallback) is not supported yet").SetName("TwoArguments_IsTheCoalesceFollowUp");
        yield return new TestCaseData("Dim r = If(True)",
            "If() takes three arguments — If(condition, whenTrue, whenFalse) — but was given 1").SetName("OneArgument");
        yield return new TestCaseData("Dim r = If()",
            "If() takes three arguments — If(condition, whenTrue, whenFalse) — but was given 0").SetName("NoArguments");
        yield return new TestCaseData("Dim r = If(True, 1, 2, 3)",
            "If() takes three arguments — If(condition, whenTrue, whenFalse) — but was given 4").SetName("FourArguments");
    }

    /// <summary>
    /// The parser refuses every other arity with a message that names the form to write. The two-argument coalesce vbc
    /// accepts is the filed follow-up (it needs a `Nothing` test on the carrier whose meaning differs for a String and a
    /// nullable value type); until then it must not parse as something else.
    /// </summary>
    [TestCaseSource(nameof(ArityRows))]
    public void AnyArityButThree_IsAParseDiagnostic(string body, string message)
    {
        var run = T123Front.Run(ConditionalProbes.InMain(body));

        Assert.Multiple(() =>
        {
            Assert.That(run.ParseErrors, Has.Count.EqualTo(1), string.Join(" | ", run.ParseErrors));
            Assert.That(run.ParseErrors[0], Does.Contain(message));
        });
    }

    /// <summary>The same refusal through both compiler entry points, the CLI's single-file route and the project route.</summary>
    [TestCaseSource(nameof(ArityRows))]
    public void AnyArityButThree_IsRefusedByEveryEntryPoint(string body, string message)
    {
        var source = ConditionalProbes.InMain(body);

        Assert.Multiple(() =>
        {
            foreach (var project in new[] { false, true })
            {
                var errors = T123Front.CompilerErrors(source, project);
                Assert.That(errors, Has.Count.EqualTo(1), $"project route={project}: " + string.Join(" | ", errors));
                Assert.That(errors[0], Does.Contain(message), $"project route={project}");
            }
        });
    }

    // ============================================================================================
    // Analyzer diagnostics
    // ============================================================================================

    /// <summary>
    /// A non-Boolean condition is a WARNING, as an If statement's is (vbc accepts `If(n, "a", "b")` with Option Strict
    /// off). The program still compiles.
    /// </summary>
    [Test]
    public void ANonBooleanCondition_IsAWarning_NotAnError()
    {
        var run = T123Front.Run(ConditionalProbes.InMain("Dim n As Integer = 1\nDim x = If(n, \"a\", \"b\")"));

        Assert.Multiple(() =>
        {
            Assert.That(run.AllErrors, Is.Empty);
            Assert.That(run.Analyzed, Is.True);
            Assert.That(run.Warnings, Has.Some.Contains("If() condition should be Boolean, got 'Integer'"));
        });
    }

    /// <summary>A Boolean condition has no warning.</summary>
    [Test]
    public void ABooleanCondition_HasNoWarning()
        => Assert.That(T123Front.Run(ConditionalProbes.InMain("Dim n As Integer = 1\nDim x = If(n > 0, \"a\", \"b\")")).Warnings, Is.Empty);

    /// <summary>A Sub as an operand produces no value (vbc BC30491).</summary>
    [Test]
    public void ASubAsAnOperand_IsRefused()
    {
        var errors = T123Front.Errors("Sub DoIt()\nEnd Sub\n" + ConditionalProbes.InMain("Dim t As Boolean = True\nDim x = If(t, DoIt(), 1)"));

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("An operand of If() does not produce a value"));
    }

    /// <summary>
    /// `Nothing` takes the other operand's type and is then judged against it: fine for a reference type, refused with
    /// advice for a value type, exactly as `Dim x As Integer = Nothing` is. ⚠ vbc gives 0 for `If(t, 1, Nothing)`; the
    /// refusal is BasicLang's rule, not a defect.
    /// </summary>
    [TestCase("If(t, 1, Nothing)", "Nothing has no value of type 'Integer'; write 0", TestName = "NothingSecond_OfAValueType_IsRefused")]
    [TestCase("If(t, Nothing, 1)", "Nothing has no value of type 'Integer'; write 0", TestName = "NothingFirst_OfAValueType_IsRefused")]
    [TestCase("If(t, 1.5, Nothing)", "Nothing has no value of type 'Double'", TestName = "NothingSecond_OfADouble_IsRefused")]
    public void ANothingOperand_OfAValueType_IsRefusedWithAdvice(string expression, string message)
    {
        var errors = T123Front.Errors(ConditionalProbes.InMain("Dim t As Boolean = True\nDim x = " + expression));

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain(message));
    }

    [TestCase("If(t, \"x\", Nothing)")]
    [TestCase("If(t, Nothing, \"x\")")]
    [TestCase("If(t, Nothing, Nothing)")]
    public void ANothingOperand_OfAReferenceType_IsAccepted(string expression)
        => Assert.That(T123Front.Errors(ConditionalProbes.InMain("Dim t As Boolean = True\nDim x = " + expression)), Is.Empty);

    // ============================================================================================
    // Where an If() cannot be lowered — refused with a message, never a crash and never a wrong program
    // ============================================================================================

    private static IEnumerable<TestCaseData> RefusedPositions()
    {
        yield return new TestCaseData("Dim flag As Boolean = True\nDim g As Integer = If(flag, 1, 2)\nSub Main()\n    Console.WriteLine(g)\nEnd Sub\n",
            "the module-level variable 'g' has an initializer that cannot be computed at compile time")
            .SetName("ModuleDim");
        yield return new TestCaseData("Const K = If(True, 1, 2.5)\nSub Main()\n    Console.WriteLine(K)\nEnd Sub\n",
            "the module-level constant 'K' has an initializer that cannot be computed at compile time")
            .SetName("ModuleConst_vbcFoldsThisOne");
        yield return new TestCaseData("Class Box\n    Public F As Integer = If(True, 1, 2)\nEnd Class\nSub Main()\n    Console.WriteLine(New Box().F)\nEnd Sub\n",
            "the field 'F' has an initializer that cannot be computed at compile time")
            .SetName("ClassField");
        yield return new TestCaseData("Sub Main()\n    Dim n As Integer = 3\n    Dim t As Boolean = True\n    Select Case n\n        Case Is > 0 When If(t, n > 1, n > 2)\n            Console.WriteLine(\"big\")\n        Case Else\n            Console.WriteLine(\"other\")\n    End Select\nEnd Sub\n",
            "this 'When' guard needs control flow the compiler cannot build inside a Case guard")
            .SetName("WhenGuard");
    }

    /// <summary>
    /// The four places the lowering cannot hold control flow are refused by the IR builder with a message that names the
    /// workaround — a module-scope initializer must fold to a constant, a Case guard is built with emission off. Not a
    /// NullReferenceException, and not the carrier of a function that is thrown away.
    /// </summary>
    [TestCaseSource(nameof(RefusedPositions))]
    public void AnIfWhereControlFlowCannotLive_IsRefusedByTheIrBuilder(string source, string message)
    {
        var refusal = T123Front.IrRefusal(source);

        Assert.That(refusal, Is.Not.Null, "it built; the conditional was lowered somewhere it cannot run");
        Assert.That(refusal, Does.Contain(message).And.Not.Contain("Object reference not set"));
    }

    /// <summary>...and through both compiler entry points: the CLI's single-file route and <c>CompileProjectFiles</c>.</summary>
    [TestCaseSource(nameof(RefusedPositions))]
    public void AnIfWhereControlFlowCannotLive_IsRefusedByEveryEntryPoint(string source, string message)
    {
        Assert.Multiple(() =>
        {
            foreach (var project in new[] { false, true })
                foreach (var aggressive in new[] { false, true })
                {
                    var errors = T123Front.CompilerErrors(source, project, aggressive);
                    Assert.That(errors, Has.Count.EqualTo(1), $"project={project} aggressive={aggressive}: " + string.Join(" | ", errors));
                    Assert.That(errors[0], Does.Contain(message).And.Not.Contain("Object reference not set"), $"project={project} aggressive={aggressive}");
                }
        });
    }

    /// <summary>The controls: the same conditionals where control flow CAN live build.</summary>
    [TestCase("Sub Main()\n    Const K = If(True, 1, 2.5)\n    Dim v = K\n    Console.WriteLine(v)\nEnd Sub\n", TestName = "LocalConst")]
    [TestCase("Sub Main()\n    Dim t As Boolean = True\n    Dim g As Integer = If(t, 1, 2)\n    Console.WriteLine(g)\nEnd Sub\n", TestName = "LocalDim")]
    [TestCase("Class Box\n    Public F As Integer\n    Public Sub New()\n        F = If(True, 1, 2)\n    End Sub\nEnd Class\nSub Main()\n    Console.WriteLine(New Box().F)\nEnd Sub\n", TestName = "FieldSetInTheConstructor")]
    [TestCase("Sub Main()\n    Dim n As Integer = 3\n    Dim t As Boolean = True\n    Dim big As Boolean = If(t, n > 1, n > 2)\n    Select Case n\n        Case Is > 0 When big\n            Console.WriteLine(\"big\")\n        Case Else\n            Console.WriteLine(\"other\")\n    End Select\nEnd Sub\n", TestName = "TheGuardComputedFirst")]
    public void TheSameConditional_WhereControlFlowCanLive_Builds(string source)
        => Assert.That(T123Front.IrRefusal(source), Is.Null);

    // ============================================================================================
    // The IR shape — a carrier and branches, exactly the If statement's blocks; no new IR node
    // ============================================================================================

    private static IRFunction MainOf(string source) => JsTestSupport.BuildModule(source).Functions.Single(f => f.Name == "Main");

    private static string[] BlockNames(IRFunction f) => f.Blocks.Select(b => b.Name).ToArray();

    /// <summary>
    /// `Dim r = If(t, 1, 2.5)` is four blocks — the one holding the branch, then/else/end named as an If statement's —
    /// with ONE carrier local, typed as the result (Double), written in each arm with its operand coerced IN THAT ARM
    /// (the Integer 1 is a Double constant in the then-arm), and read once, at the end.
    /// </summary>
    [Test]
    public void TheLowering_IsACarrierAndBranches_WithEachArmCoercedInItsOwnBlock()
    {
        var main = MainOf(ConditionalProbes.InMain("Dim t As Boolean = True\nDim r = If(t, 1, 2.5)\nConsole.WriteLine(r)"));
        var entry = main.Blocks[0];
        var branch = entry.Instructions.OfType<IRConditionalBranch>().Single();
        var thenBlock = main.Blocks.Single(b => b.Name == "if0.then");
        var elseBlock = main.Blocks.Single(b => b.Name == "if0.else");
        var endBlock = main.Blocks.Single(b => b.Name == "if0.end");
        var carrier = main.LocalVariables.Single(v => v.Name == "__sc0");
        var thenStore = thenBlock.Instructions.OfType<IRAssignment>().Single();
        var elseStore = elseBlock.Instructions.OfType<IRAssignment>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(BlockNames(main), Is.EqualTo(new[] { "entry", "if0.then", "if0.else", "if0.end" }));
            Assert.That(branch.TrueTarget, Is.SameAs(thenBlock));
            Assert.That(branch.FalseTarget, Is.SameAs(elseBlock));
            Assert.That(carrier.Type.Name, Is.EqualTo("Double"), "the carrier has the result type");
            Assert.That(thenStore.Target.Name, Is.EqualTo("__sc0"));
            Assert.That(elseStore.Target.Name, Is.EqualTo("__sc0"));
            Assert.That(((IRConstant)thenStore.Value).Type.Name, Is.EqualTo("Double"), "the Integer arm is widened in its own arm");
            Assert.That(((IRConstant)thenStore.Value).Value, Is.TypeOf<double>().And.EqualTo(1.0));
            Assert.That(((IRConstant)elseStore.Value).Value, Is.EqualTo(2.5));
            Assert.That(thenBlock.Instructions.Last(), Is.InstanceOf<IRBranch>().With.Property("Target").SameAs(endBlock));
            Assert.That(elseBlock.Instructions.Last(), Is.InstanceOf<IRBranch>().With.Property("Target").SameAs(endBlock));
            var read = endBlock.Instructions.OfType<IRAssignment>().First();
            Assert.That(read.Target.Name, Does.StartWith("r"));
            Assert.That(((IRVariable)read.Value).Name, Is.EqualTo("__sc0"), "the result is read from the carrier in the merge block");
        });
    }

    /// <summary>
    /// ONLY THE CHOSEN OPERAND IS EVALUATED, structurally: the call that is the true operand lives in the then-block and
    /// nowhere else, the call that is the false operand in the else-block and nowhere else. A lowering that evaluates both
    /// (mutant M2both) puts them in the branching block. The execution fixture proves it by running, on every backend.
    /// </summary>
    [Test]
    public void AnOperandsCall_LivesOnlyInItsOwnArm()
    {
        var main = MainOf(ConditionalProbes.Callees + ConditionalProbes.InMain("Dim t As Boolean = True\nDim r = If(t, F(), G())\nConsole.WriteLine(r)"));
        static List<string> Calls(BasicBlock b) => b.Instructions.OfType<IRCall>().Select(c => c.FunctionName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Calls(main.Blocks.Single(b => b.Name == "if0.then")), Is.EqualTo(new[] { "F" }));
            Assert.That(Calls(main.Blocks.Single(b => b.Name == "if0.else")), Is.EqualTo(new[] { "G" }));
            Assert.That(Calls(main.Blocks[0]), Is.Empty, "neither operand runs before the branch");
            Assert.That(Calls(main.Blocks.Single(b => b.Name == "if0.end")), Does.Not.Contain("F").And.Not.Contain("G"),
                "neither operand runs after the merge either");
        });
    }

    /// <summary>
    /// The lowering is the If STATEMENT's shape: the same block names in the same order, then the same branch kinds. A
    /// hand-written `If t Then r = F() Else r = G() End If` and `r = If(t, F(), G())` build the same four blocks; the
    /// backends recognise structure by those names.
    /// </summary>
    [Test]
    public void TheLowering_HasTheIfStatementsBlocks()
    {
        var expression = MainOf(ConditionalProbes.Callees + ConditionalProbes.InMain("Dim t As Boolean = True\nDim r As Integer = If(t, F(), G())\nConsole.WriteLine(r)"));
        var statement = MainOf(ConditionalProbes.Callees + ConditionalProbes.InMain("Dim t As Boolean = True\nDim r As Integer\nIf t Then\n    r = F()\nElse\n    r = G()\nEnd If\nConsole.WriteLine(r)"));

        Assert.Multiple(() =>
        {
            Assert.That(BlockNames(expression), Is.EqualTo(BlockNames(statement)));
            Assert.That(expression.Blocks.Select(b => b.Instructions.Last().GetType()),
                Is.EqualTo(statement.Blocks.Select(b => b.Instructions.Last().GetType())), "the same terminator in every block");
        });
    }

    /// <summary>Two conditionals in one function get distinct ids and carriers, and so does one beside an AndAlso.</summary>
    [Test]
    public void SeveralConditionals_GetDistinctBlocksAndCarriers()
    {
        var main = MainOf(ConditionalProbes.InMain("""
            Dim t As Boolean = True
            Dim f As Boolean = False
            Dim a = If(t, 1, 2) + If(f, 3, 4)
            Dim b = t AndAlso f
            Dim c = If(If(t, f, t), 5, 6)
            Console.WriteLine(a + c)
            """));
        var carriers = main.LocalVariables.Where(v => v.Name.StartsWith("__sc")).Select(v => v.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(carriers, Is.Unique);
            Assert.That(carriers, Has.Count.EqualTo(5), "four conditionals and one AndAlso, each with its own carrier");
            Assert.That(BlockNames(main), Is.Unique, "no two blocks share a name");
        });
    }

    /// <summary>
    /// No new IR node (ADR: an IR node is an architecture decision). The conditional is lowered to nodes that already
    /// exist, and no node type named for it, for a ternary or for a value select has appeared.
    /// </summary>
    [Test]
    public void NoIrNodeExists_ForTheConditional()
    {
        var nodeTypes = typeof(IRInstruction).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IRInstruction).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();
        var main = MainOf(ConditionalProbes.InMain("Dim t As Boolean = True\nDim r = If(t, 1, 2.5)\nConsole.WriteLine(r)"));
        var used = main.Blocks.SelectMany(b => b.Instructions).Select(i => i.GetType().Name).Distinct().OrderBy(n => n).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(nodeTypes.Where(n => Regex.IsMatch(n, "Ternary|Choose|IfExpr|ConditionalExpr|ConditionalValue|SelectValue|Select$")),
                Is.Empty, "an IR node for the conditional would be an architecture decision");
            Assert.That(nodeTypes.Where(n => n.Contains("Conditional")), Is.EqualTo(new[] { "IRConditionalBranch" }));
            Assert.That(used, Is.EqualTo(new[] { "IRAssignment", "IRBranch", "IRCall", "IRConditionalBranch", "IRReturn" }),
                "a carrier, branches, and the program's own call and return");
        });
    }

    /// <summary>The same shape survives the optimizer (standard and aggressive) without the verifier saying anything.</summary>
    [Test]
    public void TheLowering_SurvivesTheOptimizer_StandardAndAggressive()
    {
        const string program = """
            Function Pick(t As Boolean, a As Integer, b As Double) As Double
                Return If(t, a, b)
            End Function
            Sub Main()
                Console.WriteLine(Pick(True, 1, 2.5))
                Console.WriteLine(Pick(False, 1, 2.5))
            End Sub
            """;
        foreach (var aggressive in new[] { false, true })
        {
            var module = JsTestSupport.BuildModule(program);
            if (aggressive) AggressivePipeline.Apply(module);
            else
            {
                var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
                pipeline.AddStandardPasses();
                pipeline.Run(module);
            }
            var pick = module.Functions.Single(f => f.Name == "Pick");

            Assert.Multiple(() =>
            {
                Assert.That(pick.Blocks.SelectMany(b => b.Instructions).OfType<IRConditionalBranch>(), Is.Not.Empty,
                    $"aggressive={aggressive}: the branch is still a branch");
                Assert.That(BasicLang.Compiler.IR.Optimization.IRVerifier.CheckInvariantR(module).Concat(BasicLang.Compiler.IR.Optimization.IRVerifier.CheckInvariantT(module)), Is.Empty,
                    $"aggressive={aggressive}: the reservation invariants hold");
            });
        }
    }
}
