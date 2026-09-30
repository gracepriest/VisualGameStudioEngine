using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.StdLib;
using BasicLang.Compiler.StdLib.Framework;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #123 — the three small fixes the sample programs forced, each of which turns a REFUSAL into an acceptance:
//
//    1. IRBuilder.SubstituteFoldedConstGlobals — a module-level Const is a leaf of a constant expression, so
//       `Dim ballVY As Single = BALL_SPEED / 2` (Samples/Pong) folds instead of "cannot be computed at compile time".
//    2. WideningCastFoldingPass — Integer -> Single folds exactly within +-2^24 and NOT outside it.
//    3. SemanticAnalyzer — DrawTriangle is registered in the analyzer's MIRROR of FrameworkStdLib's table. Without it a
//       call had no signature: C# passed Single positions raw (CS1503) and C++ stored a Sub's "result" in a temp.
//
//  Each fix has positive rows (the thing now accepted, with its VALUE) and negative rows (the neighbours that must stay
//  refused or unchanged), so a mutant that widens it and a mutant that removes it both fail.
//
//  ⛔ NOT ASSERTED, NAMED: what the substitution REACHES that is wrong. `Const E As Boolean = D <= 3` (D = 3.14) is
//  accepted since #123 and folds to `true` (vbc: False) — IROptimizer.TryFoldCompare has no arm for a Double/Integer
//  pair, and the literal form `3.14 <= 3` was wrong on the before build too. See UntypedConstTests' header and the #123
//  hand-back; no expectation is written for a wrong answer.
// ================================================================================================

/// <summary>#123: a module-level Const is substituted into a module initializer before the fold.</summary>
[TestFixture]
public class ConstFoldingFixTests
{
    private static IRVariable GlobalOf(string source, string name) => JsTestSupport.BuildModule(source).GlobalVariables[name];

    private static object InitialValue(string source, string name) => ((IRConstant)GlobalOf(source, name).InitialValue).Value;

    private const string Main = "\nSub Main()\n    Console.WriteLine(1)\nEnd Sub\n";

    // ============================================================================================
    // 1. SubstituteFoldedConstGlobals
    // ============================================================================================

    private static IEnumerable<TestCaseData> Substituted()
    {
        // The Samples/Pong line itself, and the same over an UNTYPED Const (c4init's shape: `Dim half As Double = SPEED / 2`).
        yield return new TestCaseData("Const BALL_SPEED As Single = 350.0\nDim ballVY As Single = BALL_SPEED / 2", "ballVY", 175f, "Single")
            .SetName("Pong_ConstOverTwo_IsSingle175");
        yield return new TestCaseData("Const SPEED = 350.0\nDim half As Double = SPEED / 2", "half", 175.0, "Double")
            .SetName("UntypedConstOverTwo_IsDouble175");
        yield return new TestCaseData("Const N As Single = -350.0\nDim m As Single = N / 2", "m", -175f, "Single")
            .SetName("NegativeConstOverTwo");
        // Each operand kind the fixpoint folds: binary, compare, unary, and a chain of Consts.
        yield return new TestCaseData("Const K As Integer = 6\nDim g As Integer = K * 7", "g", 42, "Integer").SetName("Binary");
        yield return new TestCaseData("Const K = 6\nDim g As Integer = K * 7", "g", 42, "Integer").SetName("Binary_UntypedConst");
        yield return new TestCaseData("Const K As Integer = 6\nDim g As Boolean = K > 3", "g", true, "Boolean").SetName("Compare");
        yield return new TestCaseData("Const K As Integer = 6\nDim g As Boolean = K = 7", "g", false, "Boolean").SetName("Compare_False");
        yield return new TestCaseData("Const K As Integer = 6\nDim g As Integer = -K", "g", -6, "Integer").SetName("Unary");
        yield return new TestCaseData("Const A As Integer = 2\nConst B As Integer = A * 3\nDim g As Integer = B + 1", "g", 7, "Integer").SetName("Chain");
        yield return new TestCaseData("Const W As Integer = 800\nDim edge As Integer = W - 30", "edge", 770, "Integer").SetName("SpaceShooterShape");
    }

    /// <summary>
    /// A module initializer over a module Const folds to that Const's value and stamps the declared type: what the C#, C++
    /// and MSIL backends need a global's <c>InitialValue</c> to be. Before #123 every one of these was refused.
    /// </summary>
    [TestCaseSource(nameof(Substituted))]
    public void AModuleInitializerOverAConst_Folds(string declarations, string name, object expected, string type)
    {
        var source = declarations + Main;
        var global = GlobalOf(source, name);

        Assert.Multiple(() =>
        {
            Assert.That(((IRConstant)global.InitialValue).Value, Is.EqualTo(expected), "the folded value");
            Assert.That(((IRConstant)global.InitialValue).Value, Is.TypeOf(expected.GetType()), "the CLR type of the folded value");
            Assert.That(global.Type.Name, Is.EqualTo(type));
        });
    }

    /// <summary>...and through both compiler entry points, standard and aggressive passes.</summary>
    [TestCaseSource(nameof(Substituted))]
    public void AModuleInitializerOverAConst_Compiles_ThroughEveryEntryPoint(string declarations, string name, object expected, string type)
    {
        var source = declarations + Main;

        Assert.Multiple(() =>
        {
            foreach (var project in new[] { false, true })
                foreach (var aggressive in new[] { false, true })
                    Assert.That(T123Front.CompilerErrors(source, project, aggressive), Is.Empty, $"project={project} aggressive={aggressive}");
        });
    }

    private static IEnumerable<TestCaseData> StillRefused()
    {
        // A global that is NOT a Const is not a leaf of a constant expression (it is assigned at run time), and the
        // substitution must not be widened to it: `a + 1` would fold to 6 and be wrong the day anything writes `a` first.
        yield return new TestCaseData("Dim a As Integer = 5\nDim g As Integer = a + 1", "the module-level variable 'g'")
            .SetName("APlainGlobal_IsNotAConstantLeaf");
        yield return new TestCaseData("Dim a As Integer = 5\nDim g As Boolean = a > 3", "the module-level variable 'g'")
            .SetName("APlainGlobal_InACompare");
        yield return new TestCaseData("Dim a As Integer = 5\nDim g As Integer = -a", "the module-level variable 'g'")
            .SetName("APlainGlobal_Negated");
        // The operand kinds the fixpoint does not fold: a Const read in a call's argument.
        yield return new TestCaseData("Const K As Integer = 6\nFunction H(x As Integer) As Integer\n    Return x + 1\nEnd Function\nDim g As Integer = H(K)", "the module-level variable 'g'")
            .SetName("AConstInACallArgument");
        // A narrowing conversion stays a run-time thing: the backends disagree about what it means.
        yield return new TestCaseData("Const D As Double = 7.5\nDim g As Integer = CInt(D)", "the module-level variable 'g'")
            .SetName("ANarrowingConversionOfAConst");
        // Single * Integer has no promoting cast for the folder to reduce, Const or not (pre-existing).
        yield return new TestCaseData("Const S As Single = 350.0\nDim k As Single = S * 2", "the module-level variable 'k'")
            .SetName("MixedNumericTypes_AreStillNotFolded");
    }

    /// <summary>
    /// The neighbours stay refused, with the message that names the workaround, not a crash: a non-Const global, a Const
    /// in a call, a narrowing, and a mixed numeric product.
    /// </summary>
    [TestCaseSource(nameof(StillRefused))]
    public void TheNeighboursOfTheSubstitution_AreStillRefused(string declarations, string what)
    {
        var refusal = T123Front.IrRefusal(declarations + Main);

        Assert.That(refusal, Is.Not.Null, "it built: the substitution reached something it must not");
        Assert.That(refusal, Does.Contain(what).And.Contain("has an initializer that cannot be computed at compile time")
            .And.Not.Contain("Object reference not set"));
    }

    // ============================================================================================
    // 2. Integer -> Single folds exactly within +-2^24, and not outside it
    // ============================================================================================

    private static TypeInfo Type(string name) => new(name, TypeKind.Primitive);

    /// <summary>Runs <see cref="WideningCastFoldingPass"/> over one <c>IRCast</c> of a constant; returns what the instruction is afterwards.</summary>
    private static IRInstruction FoldOne(object value, string source, string target)
    {
        var function = new IRFunction("F", new TypeInfo("Void", TypeKind.Void));
        var block = function.CreateBlock("entry");
        block.Instructions.Add(new IRCast("c0", new IRConstant(value, Type(source)), Type(source), Type(target), CastKind.SIToFP));
        var module = new IRModule("widen");
        module.Functions.Add(function);

        new WideningCastFoldingPass().Run(module);
        return block.Instructions[0];
    }

    private const int TwoTo24 = 1 << 24;

    /// <summary>
    /// Every Integer in [-2^24, 2^24] is exactly a float, so the fold gives the same value the backends' run-time
    /// conversion does. The boundary is INCLUSIVE, both ends.
    /// </summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(350)]
    [TestCase(TwoTo24 - 1)]
    [TestCase(TwoTo24)]
    [TestCase(-(TwoTo24 - 1))]
    [TestCase(-TwoTo24)]
    public void AnIntegerWithinTwoToThe24_FoldsToTheExactSingle(int value)
    {
        var after = FoldOne(value, "Integer", "Single");

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.InstanceOf<IRConstant>(), "the cast was folded");
            Assert.That(((IRConstant)after).Value, Is.TypeOf<float>());
            Assert.That(((IRConstant)after).Value, Is.EqualTo((float)value));
            Assert.That((long)(float)((IRConstant)after).Value, Is.EqualTo(value), "exact: nothing was rounded");
            Assert.That(((IRConstant)after).Type.Name, Is.EqualTo("Single"));
        });
    }

    /// <summary>
    /// One past 2^24 the float ROUNDS (16777217 becomes 16777216), so the fold must not choose a rounding the backends
    /// might not: the cast stays a cast. The mutant that folds at every magnitude fails here.
    /// </summary>
    [TestCase(TwoTo24 + 1)]
    [TestCase(TwoTo24 + 2)]
    [TestCase(123456789)]
    [TestCase(int.MaxValue)]
    [TestCase(-(TwoTo24 + 1))]
    [TestCase(-123456789)]
    [TestCase(int.MinValue)]
    public void AnIntegerBeyondTwoToThe24_IsNotFolded(int value)
    {
        var after = FoldOne(value, "Integer", "Single");

        Assert.That(after, Is.InstanceOf<IRCast>(), $"{value} is not exactly a Single; the cast must remain");
    }

    /// <summary>The other exact widenings are unchanged, and everything the pass refuses still stays a cast.</summary>
    [Test]
    public void TheOtherWidenings_AreUnchanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(((IRConstant)FoldOne(int.MaxValue, "Integer", "Double")).Value, Is.EqualTo((double)int.MaxValue), "Integer -> Double is exact for every int");
            Assert.That(((IRConstant)FoldOne(int.MinValue, "Integer", "Long")).Value, Is.EqualTo((long)int.MinValue));
            Assert.That(((IRConstant)FoldOne(1.5f, "Single", "Double")).Value, Is.EqualTo(1.5));
            Assert.That(FoldOne(5L, "Long", "Double"), Is.InstanceOf<IRCast>(), "Long -> Double is not exact past 2^53, never folded");
            Assert.That(FoldOne(2.5, "Double", "Integer"), Is.InstanceOf<IRCast>(), "a narrowing is never folded");
            Assert.That(FoldOne(2.5, "Double", "Single"), Is.InstanceOf<IRCast>(), "a narrowing is never folded");
            Assert.That(FoldOne(5, "Long", "Single"), Is.InstanceOf<IRCast>(), "only an Integer source gets the Single arm");
        });
    }

    /// <summary>
    /// The same boundary through a real module initializer: `Single / Integer-literal` promotes the literal with an
    /// Integer -> Single cast, so `S / 16777216` (2^24) folds and `S / 16777217` is refused, in both signs. Before #123
    /// nothing with a Single and an Integer operand folded.
    /// </summary>
    [TestCase("S / 2", 175f)]
    [TestCase("S / 16777216", 350f / 16777216f)]
    [TestCase("S / -16777216", 350f / -16777216f)]
    public void ASingleOverAnIntegerWithinTheBoundary_FoldsInAModuleInitializer(string expression, float expected)
    {
        var source = "Const S As Single = 350.0\nDim g As Single = " + expression + Main;
        var global = GlobalOf(source, "g");

        Assert.Multiple(() =>
        {
            Assert.That(((IRConstant)global.InitialValue).Value, Is.TypeOf<float>().And.EqualTo(expected));
            Assert.That(T123Front.CompilerErrors(source, projectRoute: false), Is.Empty);
            Assert.That(T123Front.CompilerErrors(source, projectRoute: true, aggressive: true), Is.Empty);
        });
    }

    [TestCase("S / 16777217")]
    [TestCase("S / -16777217")]
    [TestCase("S / 2147483647")]
    public void ASingleOverAnIntegerBeyondTheBoundary_IsStillRefusedInAModuleInitializer(string expression)
    {
        var refusal = T123Front.IrRefusal("Const S As Single = 350.0\nDim g As Single = " + expression + Main);

        Assert.That(refusal, Does.Contain("the module-level variable 'g' has an initializer that cannot be computed at compile time"));
    }

    // ============================================================================================
    // 3. DrawTriangle is registered in the analyzer's mirror of FrameworkStdLib
    // ============================================================================================

    private static Dictionary<string, StdLibFunction> ProviderRows()
    {
        var field = typeof(FrameworkStdLibProvider).GetField("_functions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(field, Is.Not.Null, "FrameworkStdLibProvider._functions moved: update this reflection, do not delete the test");
        return (Dictionary<string, StdLibFunction>)field.GetValue(null);
    }

    private static SemanticAnalyzer AnalyzerWithItsBuiltIns()
    {
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(new BasicLang.Compiler.AST.ProgramNode(0, 0));
        return analyzer;
    }

    /// <summary>
    /// DrawTriangle is in BOTH tables, and they agree: ten Integer parameters, no result. Dropping the analyzer's
    /// registration (mutant) fails here, and in the sample build, where C# rejects the call with CS1503.
    /// </summary>
    [Test]
    public void DrawTriangle_IsInTheAnalyzersMirror_WithFrameworkStdLibsSignature()
    {
        var row = ProviderRows()["DrawTriangle"];
        var analyzer = AnalyzerWithItsBuiltIns();
        var symbol = analyzer.GlobalScope.Symbols.TryGetValue("DrawTriangle", out var found) ? found : null;

        Assert.That(symbol, Is.Not.Null, "DrawTriangle has a FrameworkStdLib row and no analyzer registration: a call has NO signature");
        Assert.Multiple(() =>
        {
            Assert.That(row.ParameterTypes, Is.EqualTo(Enumerable.Repeat("Integer", 10)));
            Assert.That(row.ReturnType, Is.EqualTo("Void"));
            Assert.That(symbol.Parameters.Select(p => p.Type.ToString()), Is.EqualTo(row.ParameterTypes));
            Assert.That(symbol.ReturnType.ToString(), Is.EqualTo(row.ReturnType));
            Assert.That(symbol.Kind, Is.EqualTo(SymbolKind.Subroutine));
        });
    }

    /// <summary>
    /// The general form of the mirror contract: EVERY function the analyzer registers that FrameworkStdLib also has agrees
    /// with it in parameter types and result type. Measured on this base: 23 of FrameworkStdLib's 134 rows are mirrored and
    /// all 23 agree. ⚠ The other 111 rows have NO analyzer registration (Camera*, AnimCtrl*, LoadFont, Particles*, …), so a call
    /// to one is accepted with no signature — DrawTriangle was one; it is not asserted that they get one.
    /// </summary>
    [Test]
    public void EveryMirroredRow_AgreesWithFrameworkStdLib()
    {
        var rows = ProviderRows();
        var analyzer = AnalyzerWithItsBuiltIns();
        var mirrored = rows.Where(r => analyzer.GlobalScope.Symbols.ContainsKey(r.Key)).ToList();
        var disagreements = new List<string>();

        foreach (var (name, row) in mirrored)
        {
            var symbol = analyzer.GlobalScope.Symbols[name];
            var parameters = symbol.Parameters.Select(p => p.Type.ToString()).ToArray();
            if (!parameters.SequenceEqual(row.ParameterTypes) || symbol.ReturnType?.ToString() != row.ReturnType)
                disagreements.Add($"{name}: analyzer ({string.Join(", ", parameters)}) -> {symbol.ReturnType}; FrameworkStdLib ({string.Join(", ", row.ParameterTypes)}) -> {row.ReturnType}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(disagreements, Is.Empty, string.Join("\n", disagreements));
            Assert.That(mirrored.Count, Is.GreaterThanOrEqualTo(23), "the mirror has not shrunk (DrawTriangle is the 23rd)");
            Assert.That(mirrored.Select(r => r.Key), Does.Contain("DrawTriangle").And.Contain("DrawRectangle").And.Contain("DrawText"));
        });
    }

    /// <summary>
    /// What the registration buys, at the IR: a call with Single arguments gets its six position arguments coerced to
    /// Integer (`Convert.ToInt32(x)` on C#), so the C# build no longer fails with CS1503. Through the standard AND aggressive
    /// passes.
    /// </summary>
    [Test]
    public void ADrawTriangleCall_HasItsPositionArgumentsCoercedToInteger()
    {
        const string program = """
            Sub Main()
                Dim sx As Single = 1.5
                Dim sy As Single = 2.5
                DrawTriangle(sx, sy, sx + 10, sy, sx, sy + 10, 255, 0, 0, 255)
            End Sub
            """;
        var call = JsTestSupport.BuildModule(program).Functions.Single(f => f.Name == "Main")
            .Blocks.SelectMany(b => b.Instructions).OfType<IRCall>().Single(c => c.FunctionName == "DrawTriangle");

        Assert.Multiple(() =>
        {
            Assert.That(call.Arguments, Has.Count.EqualTo(10));
            Assert.That(call.Arguments.Take(6).Select(a => a.Type.Name), Is.All.EqualTo("Integer"),
                "each Single position is converted, not passed raw");
        });

        var csharp = ReturnCoercionTests.EmitCSharpForTest(program);
        Assert.That(csharp, Does.Contain("Framework_DrawTriangle(Convert.ToInt32(").And.Not.Contain("Framework_DrawTriangle(sx,"),
            csharp);
    }
}
