using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Task #177 — MSIL: boxing a value into an <c>Object</c> slot, converting out of one, and
/// comparing Objects late-bound. FAST SUBSET, text only: nothing here spawns <c>ilasm</c> or
/// <c>dotnet</c> — every assertion reads the <c>.il</c> text <see cref="MsilHarness.CompileToIl"/>
/// returns in process. <see cref="MsilObjectBoxingExecutionTests"/> (<c>[Category("Integration")]</c>)
/// is the sibling fixture that assembles and RUNS these shapes at all three entry points.
///
/// <para>Every assertion below runs on BOTH the PLAIN pipeline (<c>optimize: false</c>, no IR pass
/// at all) and the OPTIMIZER-RUNNING one (<c>optimize: true</c>, <c>AddStandardPasses</c> — the
/// same "IR optimizer" CLAUDE.md means by that phrase) through <see cref="OnBothPipelines"/>, per
/// the project convention that a backend fix must be validated through the optimizer as well as
/// the plain unit-test helper: the green suite has hidden bugs the optimizer has exposed before.
/// </para>
///
/// <para>Every probe that carries a value into or out of an Object slot is read through a
/// Function call, never a bare literal — a literal-only shape lets constant folding compute the
/// whole expression at compile time and never reach <c>MSILBackend</c>'s coercion/comparison
/// emitters at all (the same discipline <see cref="MsilBinaryOperandCoercionTests"/> documents).
/// </para>
/// </summary>
[TestFixture]
public class MsilObjectBoxingTests
{
    // ====================================================================================
    // Shared helpers.
    // ====================================================================================

    private static string Plain(string source) => CompileToIl(source, optimize: false);
    private static string Optimized(string source) => CompileToIl(source, optimize: true);

    /// <summary>Runs <paramref name="assert"/> against the IL from both pipelines, tagging a
    /// failure with which one it came from.</summary>
    private static void OnBothPipelines(string source, Action<string, string> assert)
    {
        assert(Plain(source), "plain (optimize: false)");
        assert(Optimized(source), "optimizer-running (optimize: true, AddStandardPasses)");
    }

    // ====================================================================================
    // 1. Boxing — the right token per VALUE type, through a Dim initializer (EmitStoreLocal).
    // ====================================================================================

    [TestCase("Integer", "5", "[mscorlib]System.Int32")]
    [TestCase("Double", "2.5", "[mscorlib]System.Double")]
    [TestCase("Boolean", "True", "[mscorlib]System.Boolean")]
    [TestCase("Char", "\"x\"c", "[mscorlib]System.Char")]
    [TestCase("Long", "9000000000", "[mscorlib]System.Int64")]
    public void DimInitializer_ValueIntoObjectLocal_BoxesToItsOwnType(string type, string literal, string token)
    {
        var program =
            $"Function F() As {type}\n Return {literal}\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = F()\n Console.WriteLine(o)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {token}"),
                $"a {type} into an Object local must box to its own type ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 2. Boxing reaches every site the fix's contract lists, not just a Dim initializer.
    //    Each uses Integer/box [mscorlib]System.Int32 — the per-type check above already
    //    covers the token table; this covers which STORE FUNNELS call it.
    // ====================================================================================

    private const string Int32Token = "[mscorlib]System.Int32";

    [Test]
    public void Assignment_ValueIntoAnAlreadyDeclaredObjectLocal_Boxes()
    {
        const string program =
            "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = Nothing\n o = F()\n Console.WriteLine(o)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"assignment ({pipeline}):\n{il}"));
    }

    [Test]
    public void Field_ValueIntoAnObjectField_Boxes()
    {
        const string program =
            "Class Box\n Public O As Object\nEnd Class\n" +
            "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim b As New Box()\n b.O = F()\n Console.WriteLine(b.O)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"field store ({pipeline}):\n{il}"));
    }

    [Test]
    public void ClassProperty_ValueIntoAnObjectAutoProperty_Boxes()
    {
        const string program =
            "Class Bag\n Public Property Tag As Object\nEnd Class\n" +
            "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim b As New Bag()\n b.Tag = F()\n Console.WriteLine(b.Tag)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"property setter ({pipeline}):\n{il}"));
    }

    [Test]
    public void ArrayElement_ValueIntoAnObjectArrayElement_Boxes()
    {
        const string program =
            "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim arr(2) As Object\n arr(0) = F()\n Console.WriteLine(arr(0))\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"array element store ({pipeline}):\n{il}"));
    }

    [Test]
    public void Argument_ValueIntoAnObjectParameter_Boxes()
    {
        const string program =
            "Sub Show(o As Object)\n Console.WriteLine(o)\nEnd Sub\n" +
            "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Show(F())\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"call argument ({pipeline}):\n{il}"));
    }

    [Test]
    public void Return_ValueFromAnAsObjectFunction_Boxes()
    {
        const string program =
            "Function Pick() As Object\n Return 42\nEnd Function\n" +
            "Sub Main()\n Console.WriteLine(Pick())\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"Return ({pipeline}):\n{il}"));
    }

    [Test]
    public void ByRefParameter_ValueWrittenThroughAnObjectByRefParameter_Boxes()
    {
        const string program =
            "Sub SetIt(ByRef o As Object, v As Integer)\n o = v * 3\nEnd Sub\n" +
            "Sub Main()\n Dim a As Object = Nothing\n SetIt(a, 4)\n Console.WriteLine(a)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"ByRef store ({pipeline}):\n{il}"));
    }

    [Test]
    public void CollectionAdd_ValueIntoAListOfObject_Boxes()
    {
        const string program =
            "Function F() As Integer\n Return 7\nEnd Function\n" +
            "Sub Main()\n Dim l As New List(Of Object)()\n l.Add(F())\n Console.WriteLine(l(0))\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain($"    box {Int32Token}"), $"List(Of Object).Add ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 3. A value stored into a NON-Object slot never boxes — the narrow rule (⚠ mutant E's
    //    target). Integer, String and a class-typed slot, each a same-type store.
    // ====================================================================================

    [Test]
    public void IntegerLocal_FromAnIntegerValue_EmitsNoBox()
    {
        const string program = "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim i As Integer = F()\n Console.WriteLine(i)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain("    box "), $"Integer into Integer ({pipeline}):\n{il}"));
    }

    [Test]
    public void StringLocal_FromAStringValue_EmitsNoBox()
    {
        const string program = "Function F() As String\n Return \"x\"\nEnd Function\n" +
            "Sub Main()\n Dim s As String = F()\n Console.WriteLine(s)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain("    box "), $"String into String ({pipeline}):\n{il}"));
    }

    [Test]
    public void ClassLocal_FromAClassValue_EmitsNoBox()
    {
        const string program = "Class C\n Public V As Integer\nEnd Class\n" +
            "Function F() As C\n Return New C()\nEnd Function\n" +
            "Sub Main()\n Dim c As C = F()\n Console.WriteLine(c.V)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain("    box "), $"class into class ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 4. An Object() element store is a REFERENCE store (mutant D's target): stind.ref /
    //    stelem.ref, never stind.i4 — the suffix must follow the boxed element, not the raw
    //    value that was boxed into it.
    // ====================================================================================

    [Test]
    public void ObjectArrayElementStore_UsesTheReferenceIndirectSuffix_NeverTheIntegerOne()
    {
        const string program =
            "Function F() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim arr(2) As Object\n arr(0) = F()\n Console.WriteLine(arr(0))\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
        {
            Assert.That(il, Does.Contain("stind.ref").Or.Contain("stelem.ref"),
                $"Object() element store must be a reference store ({pipeline}):\n{il}");
            Assert.That(il, Does.Not.Contain("stind.i4"),
                $"the raw Integer's own suffix must never reach the boxed element's store ({pipeline}):\n{il}");
        });
    }

    // ====================================================================================
    // 5. Conversion OUT of an Object: CInt/CLng/CDbl/CSng/CStr/CBool call Convert.To*(object).
    // ====================================================================================

    [TestCase("CInt", "ToInt32", "int32")]
    [TestCase("CLng", "ToInt64", "int64")]
    [TestCase("CDbl", "ToDouble", "float64")]
    [TestCase("CSng", "ToSingle", "float32")]
    [TestCase("CStr", "ToString", "string")]
    [TestCase("CBool", "ToBoolean", "bool")]
    public void ConversionIntrinsic_OfAnObjectOperand_CallsConvertToXxx(
        string intrinsic, string convertMethod, string resultSpec)
    {
        var program =
            "Function MakeO() As Object\n Return 5\nEnd Function\n" +
            $"Sub Main()\n Dim o As Object = MakeO()\n Console.WriteLine({intrinsic}(o))\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il,
                Does.Contain($"call {resultSpec} [mscorlib]System.Convert::{convertMethod}(object)"),
                $"{intrinsic}(o) ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 6. CType/DirectCast to a value type UNBOXES (unbox.any), never converts.
    // ====================================================================================

    [Test]
    public void CTypeToAValueType_OfAnObjectOperand_Unboxes()
    {
        const string program =
            "Function MakeO() As Object\n Return 7\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = MakeO()\n Dim n As Short = CType(o, Short)\n" +
            " Console.WriteLine(n)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain("unbox.any"), $"CType(o, Short) ({pipeline}):\n{il}"));
    }

    [Test]
    public void DirectCastToAValueType_OfAnObjectOperand_Unboxes()
    {
        const string program =
            "Function MakeO() As Object\n Return 20\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = MakeO()\n" +
            " Dim n As Integer = DirectCast(o, Integer)\n Console.WriteLine(n)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain("unbox.any"), $"DirectCast(o, Integer) ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 7. Late-bound comparison: an Object operand routes `=`/`<>`/`<`/`<=`/`>`/`>=` through
    //    Operators.ConditionalCompareObject*(object, object, False) — never ceq/clt/cgt.
    // ====================================================================================

    [TestCase("=", "Equal")]
    [TestCase("<>", "NotEqual")]
    [TestCase("<", "Less")]
    [TestCase("<=", "LessEqual")]
    [TestCase(">", "Greater")]
    [TestCase(">=", "GreaterEqual")]
    public void ComparisonWithAnObjectOperand_IsLateBound(string op, string helper)
    {
        var program =
            "Function MakeO() As Object\n Return 20\nEnd Function\n" +
            $"Sub Main()\n Dim o As Object = MakeO()\n Console.WriteLine(o {op} 5)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
        {
            Assert.That(il, Does.Contain("    ldc.i4.0"),
                $"TextCompare must be pushed False ({pipeline}):\n{il}");
            Assert.That(il,
                Does.Contain(
                    $"call bool [Microsoft.VisualBasic.Core]Microsoft.VisualBasic.CompilerServices.Operators::ConditionalCompareObject{helper}(object, object, bool)"),
                $"'{op}' with an Object operand ({pipeline}):\n{il}");
        });
    }

    [Test]
    public void ComparisonWithNoObjectOperand_StaysTheOrdinaryOpcodes()
    {
        const string program =
            "Function A() As Integer\n Return 5\nEnd Function\n" +
            "Function B() As Integer\n Return 6\nEnd Function\n" +
            "Sub Main()\n Console.WriteLine(A() = B())\n Console.WriteLine(A() < B())\n" +
            " Console.WriteLine(A() > B())\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
        {
            Assert.That(il, Does.Not.Contain("ConditionalCompareObject"),
                $"an all-Integer comparison must never go late-bound ({pipeline}):\n{il}");
            Assert.That(il, Does.Contain("    ceq").Or.Contain("    clt").Or.Contain("    cgt"),
                $"and must still use IL's own compare opcodes ({pipeline}):\n{il}");
        });
    }

    // ====================================================================================
    // 8. The Nothing LITERAL never makes a comparison late-bound (L12): `i = Nothing` on an
    //    Integer and `s = Nothing` on a String are UNCHANGED — kills "the Nothing literal
    //    counts as Object".
    // ====================================================================================

    [Test]
    public void IntegerEqualsNothing_StaysAnOrdinaryIntegerComparison()
    {
        const string program =
            "Function GetI() As Integer\n Return 0\nEnd Function\n" +
            "Sub Main()\n Dim i As Integer = GetI()\n Console.WriteLine(i = Nothing)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain("ConditionalCompareObject"),
                $"i = Nothing on an Integer must stay Integer equality ({pipeline}):\n{il}"));
    }

    [Test]
    public void StringEqualsNothing_StaysAnOrdinaryStringComparison()
    {
        const string program =
            "Function GetS() As String\n Return Nothing\nEnd Function\n" +
            "Sub Main()\n Dim s As String = GetS()\n Console.WriteLine(s = Nothing)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain("ConditionalCompareObject"),
                $"s = Nothing on a String must stay String equality ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 9. The `.assembly extern Microsoft.VisualBasic.Core` is present EXACTLY when a
    //    late-bound compare is emitted, absent otherwise. Both text-only mutants ("extern
    //    never emitted" and "extern always emitted") survive a run on Linux ilasm (it infers
    //    the reference itself), so this pair of assertions is their only kill.
    // ====================================================================================

    private const string VbRuntimeExternMarker = ".assembly extern Microsoft.VisualBasic.Core";

    [Test]
    public void ProgramWithALateBoundCompare_DeclaresTheVbRuntimeExtern()
    {
        const string program =
            "Function MakeO() As Object\n Return 20\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = MakeO()\n Console.WriteLine(o = 20)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain(VbRuntimeExternMarker),
                $"a late-bound compare must declare the VB runtime extern ({pipeline}):\n{il}"));
    }

    [Test]
    public void ProgramWithNoLateBoundCompare_NeverDeclaresTheVbRuntimeExtern()
    {
        const string program =
            "Function A() As Integer\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = A()\n Console.WriteLine(o)\n" +
            " Console.WriteLine(A() = 5)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain(VbRuntimeExternMarker),
                $"boxing alone, and an ordinary Integer compare, must not pull in the VB runtime " +
                $"({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 10. `Is`/`IsNot` and `Case Is Nothing` stay identity (ADR-0011) — never Operators.
    //     `Case Nothing` on an Object subject IS late-bound.
    // ====================================================================================

    [Test]
    public void IsOperator_OnObjectOperands_StaysIdentity_NeverLateBound()
    {
        const string program =
            "Function MakeO() As Object\n Return 5\nEnd Function\n" +
            "Sub Main()\n Dim a As Object = MakeO()\n Dim b As Object = MakeO()\n" +
            " Console.WriteLine(a Is b)\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
        {
            Assert.That(il, Does.Not.Contain("ConditionalCompareObject"),
                $"'Is' must never go late-bound ({pipeline}):\n{il}");
            Assert.That(il, Does.Contain("    ceq"), $"'Is' must still be ceq ({pipeline}):\n{il}");
        });
    }

    [Test]
    public void CaseIsNothing_OnAnObjectSubject_StaysIdentity_NeverLateBound()
    {
        const string program =
            "Function MakeN() As Object\n Return Nothing\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = MakeN()\n Select Case o\n" +
            "  Case Is Nothing\n   Console.WriteLine(\"is-nothing\")\n" +
            "  Case Else\n   Console.WriteLine(\"not-nothing\")\n End Select\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Not.Contain("ConditionalCompareObject"),
                $"'Case Is Nothing' must never go late-bound ({pipeline}):\n{il}"));
    }

    [Test]
    public void CaseNothing_OnAnObjectSubject_IsLateBound()
    {
        const string program =
            "Function MakeN() As Object\n Return Nothing\nEnd Function\n" +
            "Sub Main()\n Dim o As Object = MakeN()\n Select Case o\n" +
            "  Case Nothing\n   Console.WriteLine(\"nothing\")\n" +
            "  Case Else\n   Console.WriteLine(\"something\")\n End Select\nEnd Sub\n";
        OnBothPipelines(program, (il, pipeline) =>
            Assert.That(il, Does.Contain("ConditionalCompareObjectEqual"),
                $"'Case Nothing' on an Object subject must be late-bound ({pipeline}):\n{il}"));
    }

    // ====================================================================================
    // 11. IRNothingPatternCase.WrittenWithIs: true for `Case Is Nothing`, false for
    //     `Case Nothing`. IR-level, not MSIL text — the two spellings reach the SAME node and
    //     differ only by this flag (MSILBackend.EmitNothingTest's own doc comment).
    // ====================================================================================

    private static IRModule BuildModule(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(program), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.ToString())));

        return new IRBuilder(analyzer).Build(program, "T");
    }

    private static bool AnyNothingPatternCase(IRModule module, Func<IRNothingPatternCase, bool> predicate) =>
        module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions)
            .OfType<IRSwitch>().SelectMany(s => s.PatternCases).OfType<IRNothingPatternCase>().Any(predicate);

    [Test]
    public void CaseIsNothing_MarksWrittenWithIsTrue()
    {
        var module = BuildModule(
            "Sub Main()\n Dim o As Object = Nothing\n Select Case o\n" +
            "  Case Is Nothing\n   Console.WriteLine(\"n\")\n" +
            "  Case Else\n   Console.WriteLine(\"s\")\n End Select\nEnd Sub\n");
        Assert.That(AnyNothingPatternCase(module, p => p.WrittenWithIs), Is.True,
            "'Case Is Nothing' must lower to an IRNothingPatternCase with WrittenWithIs = true");
    }

    [Test]
    public void CaseNothing_MarksWrittenWithIsFalse()
    {
        var module = BuildModule(
            "Sub Main()\n Dim o As Object = Nothing\n Select Case o\n" +
            "  Case Nothing\n   Console.WriteLine(\"n\")\n" +
            "  Case Else\n   Console.WriteLine(\"s\")\n End Select\nEnd Sub\n");
        Assert.That(AnyNothingPatternCase(module, p => !p.WrittenWithIs), Is.True,
            "'Case Nothing' must lower to an IRNothingPatternCase with WrittenWithIs = false");
    }

    // ====================================================================================
    // 12. Structural — #175's interface property setter boxes through the SAME shared helper
    //     (EmitCoerceToSlot), not a detached copy of the rule.
    //
    //     O3 (an Integer/String/Boolean value into IBag.Tag) exercises the site but cannot
    //     DISTINGUISH mutant "C_iface_set_detached_oldrule" from the real code: its inlined
    //     copy of the rule (`BoxableSpecs.Contains(IlTypeSpec(valueType)) &&
    //     !BoxableSpecs.Contains(propType)`) reaches the identical `box <token>` decision as
    //     EmitCoerceToSlot/ValueTypeBoxToken for every primitive, because the front end never
    //     lets a primitive flow into a String/class-typed slot to expose the mutant's "any
    //     non-primitive slot boxes" widening (⚠ this is the SAME invariant EmitCoerceToSlot's
    //     own doc comment gives for why the narrow-vs-wide rule change is a no-op over the
    //     whole corpus). The ONE place the two rules diverge on paper is Enum/Structure —
    //     ValueTypeBoxToken has an explicit TypeKind.Enum/Structure arm the mutant's inline
    //     copy never carries — but constructing a program that reaches an interface Object
    //     property setter with an Enum-typed VALUE hits a separate, pre-existing MSIL gap
    //     measured while writing this fixture: a bare `Color.Green` used directly as a setter
    //     argument (rather than as a `Dim x As Object = …` initializer, the only shape E17 and
    //     the probe corpus exercise) resolves through the WRONG member-access arm and emits
    //     `ldfld object 'Color'::'Green'` with a "// WARNING: Unknown local 'Color'" comment —
    //     not a box decision at all, so the assertion would pin THAT gap, not this one. Routing
    //     the enum through a `Dim c As Color = Color.Green` local first hits a THIRD, also
    //     pre-existing gap: the analyzer types `Color.Green` itself as `Object`, so `Dim c As
    //     Color = Color.Green` is refused ("Cannot assign value of type 'Object' to variable of
    //     type 'Color'") before any codegen runs. Neither gap is task #177's to fix.
    //
    //     RECORDED AS EQUIVALENT: no program buildable with today's front end reaches the one
    //     shape (Enum/Structure into a reference slot) where the mutant's rule and the real
    //     rule disagree. This matches the measured execution-mutation result (mut/res-
    //     C_iface_set_detached_oldrule.txt): every existing O/E/B/I probe answers OK against
    //     the mutant, identically to the real build.
    // ====================================================================================
}
