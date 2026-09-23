using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The two hand-written operand walkers this compiler keeps — <c>OptimizationPass.ReplaceUsesIn</c>
/// (the optimizer's) and <c>CSharpBackend.GetOperands</c> (the reference backend's use-count
/// census) — asserted against the IR node types THEMSELVES, by reflection.
///
/// <para>⛔ <b>Why this fixture exists.</b> Both are one-arm-per-node-kind switches, and a MISSING
/// ARM is not an absent feature — it is a wrong answer. A missing arm in the optimizer's walker
/// leaves a use pointing at a removed instruction, which is the entire defect
/// <see cref="CseAndPeepholeDanglingOperandTests"/> pins; a missing arm in <c>GetOperands</c>
/// yields a silently ZERO use count, which ADR-0001's Contract calls out by name
/// ("<c>GetOperands</c> must be total over IR node kinds; its default case must ASSERT, not return
/// empty"). Neither can be checked by reading the other, and neither can be checked by a program:
/// a node kind nothing constructs today is unreachable end to end and still wrong the day
/// something constructs it. <c>IRPhi</c> was exactly that — present in the backend's walker,
/// absent from the optimizer's, for as long as both have existed.</para>
///
/// <para>⛔ This fixture is the CHEAP guard, not a merge. The two walkers answer deliberately
/// different questions (the optimizer's skips DEFINITION slots; the backend's counts them), so
/// unifying them is a five-backend refactor. Asserting that they AGREE about which slots exist —
/// and pinning every place they do not — costs one test and would have caught the <c>IRPhi</c>
/// asymmetry the day it appeared.</para>
///
/// <para>⚠ The instances are built with <c>GetUninitializedObject</c> and populated by reflection,
/// so no constructor has to be known per node kind. The fixture asserts it could actually PROBE
/// every slot it found, so it cannot go quietly vacuous if a future node kind exposes an operand
/// through something reflection cannot set.</para>
/// </summary>
[TestFixture]
public class OperandWalkerTotalityTests
{
    private static readonly BasicLang.Compiler.SemanticAnalysis.TypeInfo IntType =
        new BasicLang.Compiler.SemanticAnalysis.TypeInfo("Integer", TypeKind.Primitive);

    /// <summary>
    /// ⛔ CONTRACT ITEM 2: <c>ReplaceUsesIn</c> is TOTAL over operand-bearing node kinds.
    ///
    /// <para>Exactly three slots are exempt, and each is exempt for a stated reason rather than by
    /// omission. The assertion is an EQUALITY, not a subset: a new uncovered slot fails it, and so
    /// does covering one of these three without updating the reason here.</para>
    ///
    /// <list type="bullet">
    /// <item><c>IRAssignment.Target</c> — a DEFINITION, not a use. The walker's own summary says
    /// definition slots are deliberately absent, and re-pointing one would rewrite which variable
    /// is being written rather than which value is read.</item>
    /// <item><c>IRVariable.DefaultValue</c> / <c>IRVariable.InitialValue</c> — declaration data
    /// hanging off a LEAF value, not an operand in the instruction stream. ⚠ Neither walker covers
    /// them; recorded here because that is a fact worth knowing rather than one worth assuming.
    /// A pass that started rewriting an initializer expression would need an arm for these.</item>
    /// </list>
    /// </summary>
    [Test]
    public void TheOptimizersOperandWalkerIsTotal_ExceptForThreeNamedSlots()
    {
        var census = Census();

        Assert.That(census.Unprobeable, Is.Empty,
            "a slot could not be probed at all, so this fixture is NOT checking it");
        Assert.That(census.ReplaceUsesMisses.OrderBy(s => s).ToArray(), Is.EqualTo(new[]
        {
            "IRAssignment.Target",
            "IRVariable.DefaultValue",
            "IRVariable.InitialValue",
        }), "OptimizationPass.ReplaceUsesIn no longer matches its documented coverage. Every slot "
          + "it misses leaves a use pointing at a replaced instruction — the backends then render "
          + "an identifier nothing declares. Add the arm, or add the slot here with the reason it "
          + "is not a use.");
    }

    /// <summary>
    /// ⭐ PROMOTED — renamed from <c>TheCSharpBackendsOperandWalkerIsNotYetTotal_PinnedAdr0001Obligation</c>,
    /// which is no longer true. ADR-0001's Contract item 1 ("<c>GetOperands</c> must be total over
    /// IR node kinds; its default case must ASSERT, not return empty") is now DISCHARGED:
    /// <c>GetOperands</c> gained arms for <c>IRArrayStore</c> (Array/Index/Value),
    /// <c>IRFieldStore</c> (Object/Value), <c>IRForEach.Collection</c>, <c>IRSwitch.Cases[].Value</c>
    /// (plus <c>PatternCases</c>, recursed via <c>AddPatternOperands</c> — not visible to this
    /// reflection census, since <c>PatternCases</c> is not an <c>IRValue</c>-typed slot; see
    /// <see cref="TheOptimizersWalkerRecursesIntoPatternCases"/>), <c>IRThrow.Exception</c> and
    /// <c>IRYield.Value</c>, and every remaining node kind is listed with an EMPTY arm so the
    /// switch's default case can throw instead of silently returning zero uses.
    ///
    /// <para>Mirrors <see cref="TheOptimizersOperandWalkerIsTotal_ExceptForThreeNamedSlots"/>: an
    /// EQUALITY assertion, not a subset, against exactly the two deliberately-excluded slots —
    /// <c>IRVariable.DefaultValue</c> / <c>.InitialValue</c>, declaration data hanging off a leaf
    /// value rather than an operand in the instruction stream, excluded for the identical reason
    /// the optimizer's walker excludes them (see that test's own docstring). A NEW gap here is a
    /// NEW silently-zero use count (ADR-0001 E1); this test failing is the notification.</para>
    /// </summary>
    [Test]
    public void TheCSharpBackendsOperandWalkerIsTotal_ExceptForTwoNamedSlots()
    {
        var census = Census();

        Assert.That(census.Unprobeable, Is.Empty,
            "a slot could not be probed at all, so this fixture is NOT checking it");
        Assert.That(census.GetOperandsMisses.OrderBy(s => s).ToArray(), Is.EqualTo(new[]
        {
            "IRVariable.DefaultValue",
            "IRVariable.InitialValue",
        }), "CSharpBackend.GetOperands' coverage changed. A NEW entry is a new silently-zero use "
          + "count (ADR-0001 E1) — the default arm should have thrown for it, not GetOperands "
          + "returning empty; a REMOVED entry (down to nothing) means even the two deliberate "
          + "exclusions above have gained real coverage and this test should be revisited.");
    }

    /// <summary>
    /// ⛔ The pattern-case recursion the switch arm delegates to. <c>IRSwitch.PatternCases</c> is
    /// not an <c>IRValue</c> list, so the reflection census above cannot see inside it, and the
    /// values a <c>When</c> guard or a range bound holds are ordinary uses that a pass may replace.
    /// </summary>
    [Test]
    public void TheOptimizersWalkerRecursesIntoPatternCases()
    {
        var guard = new IRVariable("guard", IntType);
        var lower = new IRVariable("lo", IntType);
        var upper = new IRVariable("hi", IntType);
        var replacement = new IRVariable("replaced", IntType);

        var target = new BasicBlock("case");
        var range = new IRRangePatternCase(lower, upper, target) { WhenGuard = guard };
        var nested = new IRConstantPatternCase(lower, target);
        var alternatives = new IROrPatternCase(target);
        alternatives.Alternatives.Add(nested);

        var switchInst = new IRSwitch(new IRVariable("subject", IntType), target);
        switchInst.PatternCases.Add(range);
        switchInst.PatternCases.Add(alternatives);

        WalkerProbePass.Replace(switchInst, lower, replacement);

        Assert.Multiple(() =>
        {
            Assert.That(range.LowerBound, Is.SameAs(replacement), "the range bound");
            Assert.That(nested.Value, Is.SameAs(replacement),
                "and a NESTED alternative — Or patterns recurse");
            Assert.That(range.UpperBound, Is.SameAs(upper), "and nothing else was touched");
            Assert.That(range.WhenGuard, Is.SameAs(guard), "including the guard");
        });
    }

    /// <summary>
    /// ⛔ <c>IRPhi</c>: the arm that no program can reach, killed by construction.
    ///
    /// <para>Nothing in the pipeline BUILDS a phi today — IRBuilder emits none and no pass
    /// introduces one — so this arm is UNREACHABLE end to end, and an end-to-end fixture could
    /// only leave it unasserted. It is asserted anyway, and through a REAL PASS rather than by
    /// calling the walker directly, because the point is not that a switch has a case: it is that
    /// a phi sitting in a block gets its operands re-pointed when the instruction defining one of
    /// them is merged away.</para>
    ///
    /// <para>⚠ The phi's operand list is a list of VALUE TUPLES, so it cannot be rewritten in
    /// place the way every other arm rewrites a property — it has to be assigned back by index.
    /// That is the specific way this arm can be written wrong while looking right, which is
    /// another reason not to leave it unasserted.</para>
    /// </summary>
    [Test]
    public void APhiOperandIsRePointedWhenItsDefinitionIsMergedAway()
    {
        var a = new IRVariable("a", IntType);
        var b = new IRVariable("b", IntType);

        var first = new IRBinaryOp("t0", BinaryOpKind.Add, a, b, IntType);
        var duplicate = new IRBinaryOp("t1", BinaryOpKind.Add, a, b, IntType);

        var entry = new BasicBlock("entry");
        var phi = new IRPhi("t2", IntType);
        phi.Operands.Add((duplicate, entry));

        entry.AddInstruction(first);
        entry.AddInstruction(duplicate);
        entry.AddInstruction(phi);

        var function = new IRFunction("F", IntType);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;

        var module = new IRModule("PhiProbe");
        module.Functions.Add(function);

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(new CommonSubexpressionEliminationPass());
        pipeline.Run(module);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Instructions.OfType<IRBinaryOp>().ToList(), Has.Count.EqualTo(1),
                "CSE did not merge the duplicate at all");
            Assert.That(phi.Operands[0].Value, Is.SameAs(first),
                "the phi operand must follow the merge. A value tuple has to be assigned back by "
                + "index; an arm that mutates the tuple it read changes nothing and looks correct.");
            Assert.That(phi.Operands[0].Block, Is.SameAs(entry),
                "and rewriting the value must not lose the incoming block");
        });
    }

    // ====================================================================================
    // The census.
    // ====================================================================================

    private sealed record WalkerCensus(
        List<string> ReplaceUsesMisses, List<string> GetOperandsMisses, List<string> Unprobeable);

    /// <summary>
    /// For every concrete <c>IRInstruction</c> subclass, put a distinct sentinel in every slot that
    /// can hold an <c>IRValue</c>, then ask each walker whether it sees that sentinel.
    /// </summary>
    private static WalkerCensus Census()
    {
        var replaceMisses = new List<string>();
        var operandMisses = new List<string>();
        var unprobeable = new List<string>();

        var generatorType = typeof(BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator);
        var getOperands = generatorType.GetMethod(
            "GetOperands", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(getOperands, Is.Not.Null,
            "CSharpBackend.GetOperands has been renamed or removed — this fixture is pinned to it "
            + "by name and would otherwise pass while checking nothing");
        var generator = RuntimeHelpers.GetUninitializedObject(generatorType);

        var nodeTypes = typeof(IRInstruction).Assembly.GetTypes()
            .Where(t => typeof(IRInstruction).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name);

        foreach (var nodeType in nodeTypes)
        {
            var instance = (IRInstruction)RuntimeHelpers.GetUninitializedObject(nodeType);
            var slots = new List<(string Name, IRValue Sentinel)>();

            foreach (var property in nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;

                var qualified = $"{nodeType.Name}.{property.Name}";

                if (!property.CanWrite)
                {
                    // A read-only slot that can still HOLD a value is one this fixture cannot
                    // probe, so it is reported rather than quietly skipped.
                    if (typeof(IRValue).IsAssignableFrom(property.PropertyType)
                        || IsListOf(property.PropertyType, typeof(IRValue)))
                        unprobeable.Add(qualified + " (read-only)");
                    continue;
                }

                if (typeof(IRValue).IsAssignableFrom(property.PropertyType))
                {
                    var sentinel = MakeSentinel(property.PropertyType, qualified, unprobeable);
                    if (sentinel == null) continue;
                    if (TrySet(property, instance, sentinel, qualified, unprobeable))
                        slots.Add((qualified, sentinel));
                }
                else if (IsListOf(property.PropertyType, typeof(IRValue)))
                {
                    var sentinel = new IRVariable("S_" + qualified, IntType);
                    if (TrySet(property, instance, new List<IRValue> { sentinel }, qualified + "[0]", unprobeable))
                        slots.Add((qualified + "[0]", sentinel));
                }
                else if (IsListOfValueTupleCarrying(property.PropertyType, out var tupleName))
                {
                    var sentinel = new IRVariable("S_" + qualified, IntType);
                    var list = (IList)Activator.CreateInstance(property.PropertyType)!;
                    list.Add(MakeTuple(property.PropertyType, sentinel));
                    if (TrySet(property, instance, list, $"{qualified}[0].{tupleName}", unprobeable))
                        slots.Add(($"{qualified}[0].{tupleName}", sentinel));
                }
            }

            if (slots.Count == 0) continue;

            foreach (var (name, sentinel) in slots)
            {
                var fresh = new IRVariable("REPLACED_" + name, IntType);
                WalkerProbePass.Replace(instance, sentinel, fresh);
                if (StillHolds(instance, nodeType, sentinel)) replaceMisses.Add(name);
                WalkerProbePass.Replace(instance, fresh, sentinel);   // put it back for the next probe
            }

            var seen = ((IEnumerable<IRValue>)getOperands!.Invoke(generator, new object[] { instance })!)
                .Where(v => v != null).ToList();
            foreach (var (name, sentinel) in slots)
                if (!seen.Any(v => ReferenceEquals(v, sentinel)))
                    operandMisses.Add(name);
        }

        return new WalkerCensus(replaceMisses, operandMisses, unprobeable);
    }

    private static IRValue? MakeSentinel(Type slotType, string qualified, List<string> unprobeable)
    {
        if (slotType.IsAssignableFrom(typeof(IRVariable)))
            return new IRVariable("S_" + qualified, IntType);
        if (!slotType.IsAbstract)
            return (IRValue)RuntimeHelpers.GetUninitializedObject(slotType);
        unprobeable.Add(qualified + " (abstract slot type " + slotType.Name + ")");
        return null;
    }

    private static bool TrySet(
        PropertyInfo property, object instance, object value, string qualified, List<string> unprobeable)
    {
        try { property.SetValue(instance, value); return true; }
        catch (Exception ex) { unprobeable.Add($"{qualified} ({ex.GetType().Name})"); return false; }
    }

    private static bool IsListOf(Type type, Type element) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
        && type.GetGenericArguments()[0] == element;

    /// <summary>
    /// A <c>List&lt;(IRValue X, BasicBlock Y)&gt;</c> — phi operands and switch cases. Both are
    /// value tuples, which is why the walker has to assign them back by index rather than mutate
    /// what it read; <paramref name="valueFieldName"/> is only the label used in the census.
    /// </summary>
    private static bool IsListOfValueTupleCarrying(Type type, out string valueFieldName)
    {
        valueFieldName = "Value";
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>)) return false;

        var element = type.GetGenericArguments()[0];
        if (!element.IsGenericType || !element.Name.StartsWith("ValueTuple")) return false;

        return element.GetFields().Any(f => typeof(IRValue).IsAssignableFrom(f.FieldType));
    }

    private static object MakeTuple(Type listType, IRValue value)
    {
        var element = listType.GetGenericArguments()[0];
        var tuple = RuntimeHelpers.GetUninitializedObject(element);
        var field = element.GetFields().First(f => typeof(IRValue).IsAssignableFrom(f.FieldType));
        field.SetValue(tuple, value);
        return tuple;
    }

    /// <summary>True when any slot of <paramref name="instance"/> still references the sentinel.</summary>
    private static bool StillHolds(object instance, Type nodeType, IRValue sentinel)
    {
        foreach (var property in nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;

            object? value;
            try { value = property.GetValue(instance); } catch { continue; }
            if (ReferenceEquals(value, sentinel)) return true;
            if (value is not IEnumerable items || value is string) continue;

            foreach (var item in items)
            {
                if (ReferenceEquals(item, sentinel)) return true;
                var type = item?.GetType();
                if (type is { IsGenericType: true } && type.Name.StartsWith("ValueTuple"))
                    foreach (var field in type.GetFields())
                        if (ReferenceEquals(field.GetValue(item), sentinel)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The only way to reach <c>OptimizationPass.ReplaceUses</c>, which is <c>protected static</c>.
    /// A subclass is the intended door, and using it keeps the fixture honest: it exercises the
    /// SAME entry point every pass uses, not a reflected private method.
    /// </summary>
    private sealed class WalkerProbePass : OptimizationPass
    {
        private WalkerProbePass() : base("WalkerProbe") { }

        public override bool Run(IRModule module) => false;

        internal static void Replace(IRInstruction instruction, IRValue oldValue, IRValue newValue)
            => ReplaceUses(new[] { instruction }, oldValue, newValue);
    }
}
