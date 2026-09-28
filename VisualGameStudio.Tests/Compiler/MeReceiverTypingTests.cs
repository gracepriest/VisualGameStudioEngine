using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #176 — one `Me` per member, typed as its own class (`IRBuilder.MeOfCurrentMember`).
//
//  `IRBuilder._variableVersions` is not scoped per function, and nothing ever popped "Me": the
//  FIRST class in a file to use `Me` — bare (an accessor-backed property's own name) or explicit
//  (`Me`/`Me.X`) — used to fix its type for every class built after it. MSIL spells a member
//  token from the receiver's IR type, so it alone showed the bug (`stfld int32 'Animal'::'V'` for
//  a LATER class's own field); C#, JavaScript and C++ print `this` and never read the type.
//
//  This fixture is the IR-level sibling of <see cref="MeReceiverTypingExecutionTests"/>: it
//  inspects the `Me` `IRVariable` `AccessorMemberReceiver`/`Visit(IdentifierExpressionNode)`
//  produce directly — no process, no Node, no ilasm — so it belongs in the fast subset.
//
//  Every probe below has an EARLIER class establish a `Me` (bare, explicit, or both) before a
//  LATER class asks for its own — the shape that was silently wrong before the fix.
// ================================================================================================

[TestFixture]
public class MeReceiverTypingTests
{
    /// <summary>
    /// Animal (earlier) uses BOTH forms across two methods; Counter (later) uses both forms in
    /// ONE method. Sensitive, at the unit level, to M1 (both call sites reverted), M2 (only the
    /// accessor/bare-property site reverted), M3 (only the explicit site reverted) and M5 (one
    /// `Me` per whole PROGRAM, not per function) — see the mutant table in the test-writer's hand
    /// back for the walk-through of why each one flips <see cref="CounterProbeReceiverTypes"/> to
    /// "Animal". M4 (the nested-class restore) has its own dedicated probe below, because this
    /// shape has no nested class to exercise it.
    /// </summary>
    private const string TwoClasses = """
        Class Animal
            Private _sound As String
            Public Property Sound As String
                Get
                    Return _sound
                End Get
                Set(value As String)
                    _sound = value
                End Set
            End Property
            Public Function SpeakBare() As String
                Return Sound & "!"
            End Function
            Public Function SpeakExplicit() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Counter
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Probe() As Integer
                V = 2
                Return Me.V
            End Function
        End Class

        Sub Main()
        End Sub
        """;

    /// <summary>Counter (later) reads a bare property inside a lambda it creates.</summary>
    private const string LambdaInLaterClass = """
        Class Animal
            Private _sound As String
            Public Property Sound As String
                Get
                    Return _sound
                End Get
                Set(value As String)
                    _sound = value
                End Set
            End Property
            Public Function Speak() As String
                Return Sound & "!"
            End Function
        End Class

        Class Counter
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Probe() As Integer
                Dim f = Function() V + 1
                V = 3
                Return f()
            End Function
        End Class

        Sub Main()
        End Sub
        """;

    /// <summary>`MyBase.V` beside `Me.V`, in the same method — the deliberate exception to
    /// <c>MeOfCurrentMember</c>: the SAME object seen as its base class, minted fresh by
    /// <c>Visit(MyBaseExpressionNode)</c> rather than through <c>_meByFunction</c>.</summary>
    private const string BaseAndDerived = """
        Class Base
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
        End Class

        Class Derived
            Inherits Base
            Public Function Probe() As Integer
                Return MyBase.V + Me.V
            End Function
        End Class

        Sub Main()
        End Sub
        """;

    /// <summary>Outer's own property `V` is declared AFTER a nested nested `Inner` class — the
    /// exact shape `Visit(ClassNode)`'s save/restore (rather than null-out) of
    /// `_currentClassName`/`_currentClassMethodNames` exists for (M4). `Probe` never even calls
    /// `Inner`; the nested DECLARATION alone is what used to leave every later outer member with
    /// no enclosing class at all.</summary>
    private const string NestedThenProperty = """
        Class Outer
            Class Inner
                Public Function One() As Integer
                    Return 1
                End Function
            End Class
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
            Public Function Probe() As Integer
                V = 2
                Return Me.V
            End Function
        End Class

        Sub Main()
        End Sub
        """;

    private static IRFunction MethodOf(IRModule module, string className, string methodName) =>
        module.Classes[className].Methods.Single(m => m.Name == methodName).Implementation;

    /// <summary>Every <see cref="IRVariable"/> named "Me" (ordinal-insensitive, matching the
    /// parser's own keyword spelling) that is the RECEIVER of a field access/store instruction in
    /// <paramref name="function"/> — exactly the node <c>AccessorMemberReceiver</c> /
    /// <c>Visit(IdentifierExpressionNode)</c> produce for a bare or explicit member reference, in
    /// instruction order. Deliberately shallow (direct instruction operands, not
    /// <c>OptimizationPass.UsesOf</c>'s recursive walk): IRBuilder emits a field access/store as
    /// its own flat instruction, so a nested walk is not needed to find it.</summary>
    private static List<IRVariable> MeReceiversIn(IRFunction function)
    {
        var found = new List<IRVariable>();
        if (function?.Blocks == null) return found;
        foreach (var block in function.Blocks)
        {
            if (block?.Instructions == null) continue;
            foreach (var inst in block.Instructions)
            {
                if (inst is IRFieldAccess { Object: IRVariable v1 } fa
                    && string.Equals(v1.Name, "Me", StringComparison.OrdinalIgnoreCase))
                    found.Add(v1);
                if (inst is IRFieldStore { Object: IRVariable v2 } fs
                    && string.Equals(v2.Name, "Me", StringComparison.OrdinalIgnoreCase))
                    found.Add(v2);
            }
        }
        return found;
    }

    private static IRModule Standard(string source)
    {
        var module = CSharpTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    /// <summary>Shared assertions for <see cref="TwoClasses"/>, run against both an unoptimized
    /// and a standard-optimized module (item 2's "on both pipelines"). Covers: every method's
    /// receiver typed as ITS OWN class (bare and explicit alike); one `Me` per FUNCTION (the two
    /// receivers inside <c>Counter.Probe</c> are the SAME instance); and the leakage claim itself
    /// — <c>Counter</c> (built after <c>Animal</c>, which used `Me` both ways) never inherits
    /// `Animal`'s type. That last check is the unit form of mutants M1, M2, M3 and M5: reverting
    /// either call site (M2/M3), both (M1), or caching one `Me` for the whole program instead of
    /// per function (M5) each make `Counter.Probe`'s receivers report "Animal".</summary>
    private static void AssertReceiverTypes(IRModule module)
    {
        var animalBare = MeReceiversIn(MethodOf(module, "Animal", "SpeakBare"));
        var animalExplicit = MeReceiversIn(MethodOf(module, "Animal", "SpeakExplicit"));
        var counterReceivers = MeReceiversIn(MethodOf(module, "Counter", "Probe"));

        Assert.Multiple(() =>
        {
            Assert.That(animalBare, Has.Count.EqualTo(1), "Animal.SpeakBare's bare receiver");
            Assert.That(animalBare[0].Type?.Name, Is.EqualTo("Animal"), "bare receiver in the FIRST class");

            Assert.That(animalExplicit, Has.Count.EqualTo(1), "Animal.SpeakExplicit's explicit receiver");
            Assert.That(animalExplicit[0].Type?.Name, Is.EqualTo("Animal"), "explicit receiver in the FIRST class");

            Assert.That(counterReceivers, Has.Count.EqualTo(2), "Counter.Probe's bare write + explicit read");
            Assert.That(counterReceivers[0].Type?.Name, Is.EqualTo("Counter"),
                "Counter's BARE receiver must be Counter-typed, never the earlier class Animal's " +
                "(the unit form of M1/M2/M5)");
            Assert.That(counterReceivers[1].Type?.Name, Is.EqualTo("Counter"),
                "Counter's EXPLICIT receiver must be Counter-typed, never Animal's (the unit form " +
                "of M1/M3/M5)");
            Assert.That(counterReceivers[0], Is.SameAs(counterReceivers[1]),
                "one Me per FUNCTION: Counter.Probe's bare and explicit receivers must be the SAME IRVariable instance");
        });
    }

    [Test]
    public void BareAndExplicitReceivers_AreTypedAsTheirOwnMethodsClass_NoOptimizer()
        => AssertReceiverTypes(CSharpTestSupport.BuildModule(TwoClasses, sourceFilePath: "prog.bas"));

    [Test]
    public void BareAndExplicitReceivers_AreTypedAsTheirOwnMethodsClass_StandardPipeline()
        => AssertReceiverTypes(Standard(TwoClasses));

    [Test]
    public void LambdaInALaterClass_GetsItsOwnMeTypedAsThatClass()
    {
        var module = CSharpTestSupport.BuildModule(LambdaInLaterClass, sourceFilePath: "prog.bas");

        var probe = MethodOf(module, "Counter", "Probe");
        var probeReceivers = MeReceiversIn(probe);
        var lambda = module.Functions.Single(f => f.IsLambda);
        var lambdaReceivers = MeReceiversIn(lambda);

        Assert.Multiple(() =>
        {
            Assert.That(probeReceivers, Has.Count.EqualTo(1), "Probe's own bare write, V = 3");
            Assert.That(probeReceivers[0].Type?.Name, Is.EqualTo("Counter"));

            Assert.That(lambdaReceivers, Has.Count.EqualTo(1), "the lambda's own bare read, V + 1");
            Assert.That(lambdaReceivers[0].Type?.Name, Is.EqualTo("Counter"),
                "a lambda body inside a later class must get a Me typed as THAT class");

            Assert.That(lambdaReceivers[0], Is.Not.SameAs(probeReceivers[0]),
                "a lambda is its own IRFunction and must get its OWN Me — never its creator's instance");
        });
    }

    [Test]
    public void MyBase_KeepsTheBaseType_DistinctFromMe()
    {
        var module = CSharpTestSupport.BuildModule(BaseAndDerived, sourceFilePath: "prog.bas");
        var receivers = MeReceiversIn(MethodOf(module, "Derived", "Probe"));

        Assert.That(receivers, Has.Count.EqualTo(2), "MyBase.V and Me.V");
        var baseReceiver = receivers.SingleOrDefault(v => v.Type?.Name == "Base");
        var derivedReceiver = receivers.SingleOrDefault(v => v.Type?.Name == "Derived");

        Assert.Multiple(() =>
        {
            Assert.That(baseReceiver, Is.Not.Null, "MyBase.V must keep the BASE type");
            Assert.That(derivedReceiver, Is.Not.Null, "Me.V must still be typed as the class being built");
            Assert.That(baseReceiver, Is.Not.SameAs(derivedReceiver),
                "MyBase is a DIFFERENT IRVariable of the same name, never MeOfCurrentMember's instance");
        });
    }

    [Test]
    public void NestedClass_OuterPropertyDeclaredAfterIt_IsBuiltWithTheOuterClass()
    {
        var module = CSharpTestSupport.BuildModule(NestedThenProperty, sourceFilePath: "prog.bas");
        var probe = MethodOf(module, "Outer", "Probe");
        var receivers = MeReceiversIn(probe);

        // Invariant F (ADR-0007): a bare accessor-backed property must lower to a field
        // access/store, never survive as a plain IRVariable/IRAssignment — which is exactly what
        // reverting the save/restore (M4) breaks for a member declared after a nested class.
        var violations = IRVerifier.CheckInvariantF(module);
        var noStrayAssignment = probe.Blocks
            .SelectMany(b => b.Instructions)
            .OfType<IRAssignment>()
            .Where(a => a.Target?.Name == "V")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(violations, Is.Empty, "Invariant F must hold for a property built after a nested class");
            Assert.That(noStrayAssignment, Is.Empty,
                "the bare write 'V = 2' must lower to an IRFieldStore, not a plain variable assignment");
            Assert.That(receivers, Has.Count.EqualTo(2), "the bare write and the explicit read");
            Assert.That(receivers[0].Type?.Name, Is.EqualTo("Outer"), "bare receiver after a nested class");
            Assert.That(receivers[1].Type?.Name, Is.EqualTo("Outer"), "explicit receiver after a nested class");
            Assert.That(receivers[0], Is.SameAs(receivers[1]), "still one Me per function");
        });
    }
}
