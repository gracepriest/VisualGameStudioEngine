using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  ADR-0007 — bare-name property lowering fidelity
//  (docs/superpowers/decisions/0007-bare-name-property-lowering-fidelity.md).
//
//  IRBuilder.AccessorMemberOf / AccessorMemberReceiver lower a bare name the analyzer binds to an
//  ACCESSOR-BACKED property of the enclosing class (or a base present in the module) to EXACTLY
//  the node its qualified `Me.`/`Class.` form already produces — IRFieldStore (a write, compound
//  `P += 1` included) / IRFieldAccess (a read) — never to a plain IRAssignment/IRVariable. A plain
//  non-virtual auto-property is storage, like a field, and stays out of scope. IRVerifier's new
//  Invariant F (beside V and S′, in VerifyAfterOptimization) makes a regression of that lowering
//  structurally detectable: it fires on any IRVariable — operand or destination, a renamed value
//  included — that spells an accessor-backed member of the function's own class or a base.
//
//  Every probe below is verbatim from the implementer's evidence
//  (S/adr7/probes/<name>.bas — see this session's scratchpad), each value cross-checked against
//  that probe's own .exp AND against S/adr7/probes/matrix-after.txt cell by cell before being
//  pinned here, matching the convention KillVocabularyExtensionsTests.cs already established for
//  ADR-0006 D1. C++ did not build ANY of these Get/Set probes until task #148 (every property read
//  and write lowered to a field access); they now run on all four backends.
// ================================================================================================

internal static class BarePropertyLoweringProbes
{
    // ---- Qualified twins (P1/P3), needed only for the structural no-optimizer-IR comparison ----

    /// <summary>P1 — Me.P = 10, the qualified twin the bare form P4 must lower identically to.</summary>
    internal const string P1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Me.P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    /// <summary>P3 — t = Me.Tick, the qualified twin the bare form P5 must lower identically to.</summary>
    internal const string P3 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            ReadOnly Property Tick As Integer
                Get
                    K = K + 10
                    Return K
                End Get
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Dim t As Integer = Me.Tick
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(t))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    // ---- The bare-name probes themselves --------------------------------------------------------

    /// <summary>P4 — P = 10, bare, inside Box (a Get/Set property backed by field K).</summary>
    internal const string P4 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P4Expected = "seed\nseed\n12,3";

    /// <summary>P5 — t = Tick, bare; the Getter itself bumps K, so the mere READ must act as a call.</summary>
    internal const string P5 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            ReadOnly Property Tick As Integer
                Get
                    K = K + 10
                    Return K
                End Get
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Dim t As Integer = Tick
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(t))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P5Expected = "seed\nseed\n13,3,11";

    /// <summary>P6 — a SHARED property used bare inside a Shared method; receiver is the
    /// DECLARING CLASS (Box), never Me (`this.P` on a static member is CS0176).</summary>
    internal const string P6 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public Shared K As Integer

            Shared Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Shared Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Box.Work(Seed(2))
        End Sub
        """;

    internal const string P6Expected = "seed\nseed\n12,3";

    /// <summary>P6q — Box.P = 10, the qualified twin the bare Shared form P6 must lower identically to.</summary>
    internal const string P6q = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public Shared K As Integer

            Shared Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Shared Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Box.P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Box.Work(Seed(2))
        End Sub
        """;

    /// <summary>P6g — a SHARED Getter used bare; the Getter itself bumps the Shared field K.</summary>
    internal const string P6g = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public Shared K As Integer

            Shared ReadOnly Property Tick As Integer
                Get
                    K = K + 10
                    Return K
                End Get
            End Property

            Shared Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Dim t As Integer = Tick
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(t))
            End Sub
        End Class

        Sub Main()
            Box.Work(Seed(2))
        End Sub
        """;

    internal const string P6gExpected = "seed\nseed\n13,3,11";

    /// <summary>P6gq — Box.Tick, the qualified twin P6g must lower identically to.</summary>
    internal const string P6gq = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public Shared K As Integer

            Shared ReadOnly Property Tick As Integer
                Get
                    K = K + 10
                    Return K
                End Get
            End Property

            Shared Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Dim t As Integer = Box.Tick
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(t))
            End Sub
        End Class

        Sub Main()
            Box.Work(Seed(2))
        End Sub
        """;

    /// <summary>P7 — a COMPOUND bare write, P += 10. Used to lower to an add RENAMED "P", with no
    /// IRAssignment at all (Invariant F's renamed-destination arm exists for exactly this shape).</summary>
    internal const string P7 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                P += 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P7Expected = "seed\nseed\n13,3";

    /// <summary>P8 — an INHERITED property (declared on BaseBox) used bare from a derived class's
    /// method. The nearest member of the name up the base chain is the property itself.</summary>
    internal const string P8 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property
        End Class

        Class Box
            Inherits BaseBox

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P8Expected = "seed\nseed\n12,3";

    /// <summary>P9 — a SHARED property used bare from an INSTANCE method (Work is not Shared).</summary>
    internal const string P9 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public Shared K As Integer

            Shared Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P9Expected = "seed\nseed\n12,3";

    /// <summary>P10 — P = K + 5, a RENAMED VALUE whose right side itself reads the field the
    /// Setter also writes (K = K + 100), and the printed line reads P bare too (a Getter call).</summary>
    internal const string P10 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer
            Public S As Integer

            Property P As Integer
                Get
                    Return S
                End Get
                Set(value As Integer)
                    S = value
                    K = K + 100
                End Set
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                P = K + 5
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(P))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P10Expected = "seed\nseed\n103,3,6";

    /// <summary>CP3 — CopyPropagation's own bare-property probe: P = 5 : Inc() : P + 1, Inc()
    /// writing the backing field K bare. Was ALSO wrong on C# (printed 6 for 16) before this
    /// ADR — CopyPropagation reads the same IR IRBuilder now lowers correctly (Obligation 5),
    /// no property-specific kill rule needed.</summary>
    internal const string CP3 = """
        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Inc()
                K = K + 10
            End Sub

            Sub Work()
                P = 5
                Inc()
                Console.WriteLine(CStr(P + 1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string CP3Expected = "16";

    /// <summary>CP4 — the copy fact `P = 5` propagated THROUGH the Getter itself (K * 2), with no
    /// intervening call at all. Was ALSO wrong on C# (printed 6 for 11) before this ADR.</summary>
    internal const string CP4 = """
        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K * 2
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Work()
                P = 5
                Console.WriteLine(CStr(P + 1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string CP4Expected = "11";

    /// <summary>P16 — the Overridable case: BaseBox.Work writes its OWN Overridable auto-property
    /// V bare; Box Overrides V with a Get/Set backed by K. MSIL was the one backend this ADR
    /// measured wrong→right for this shape (3,3 -&gt; 12,3); JavaScript is wrong for BOTH the bare
    /// and the Me.-qualified form, before and after (a SEPARATE, pre-existing defect: a class-field
    /// initializer shadows the derived accessor). Tested on MSIL only, per the brief's scope.</summary>
    internal const string P16 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Overridable Property V As Integer

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                V = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Class Box
            Inherits BaseBox

            Overrides Property V As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P16Expected = "seed\nseed\n12,3";

    // ---- Controls: must not move -----------------------------------------------------------------

    /// <summary>P12 — a bare PLAIN auto-property (no Get/Set block, not Overridable/Overrides): out
    /// of ADR-0007's scope by definition, storage like a field. MEASURED green on ALL FOUR
    /// backends including C++ — the one bare-property shape C++ both builds and runs right
    /// today — so Obligation 2 (fix C++ property codegen first) was never triggered.</summary>
    internal const string P12 = """
        Class Box
            Property V As Integer

            Sub Work()
                V = 5
                Dim a As Integer = V + 1
                Console.WriteLine(CStr(a) & "," & CStr(Me.V))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string P12Expected = "6,5";

    /// <summary>P13 — a LOCAL named P shadows the class's Get/Set property P inside Work: the
    /// local wins lexically, so `P = P + 1` never touches the accessor or its backing field K —
    /// only the constructor's bare `P = 4` (unshadowed there) does.</summary>
    internal const string P13 = """
        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value * 2
                End Set
            End Property

            Sub New()
                P = 4
            End Sub

            Sub Work()
                Dim P As Integer = 7
                P = P + 1
                Console.WriteLine(CStr(P) & "," & CStr(K) & "," & CStr(Me.P))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string P13Expected = "8,8,8";

    /// <summary>P15 — KNOWN-WRONG, task #151, UNRELATED to and UNCHANGED by ADR-0007: `Message`
    /// read bare is a BUILT-IN base property (System.Exception), not a property declared in the
    /// IR module, so AccessorMemberOf/Invariant F do not touch it (F only checks bases PRESENT in
    /// the IR module) — it is not flagged, so it is not lowered, exactly as before this ADR.</summary>
    internal const string P15 = """
        Class MyErr
            Inherits Exception

            Sub New()
                MyBase.New("boom")
            End Sub

            Function Describe() As String
                Return "E:" & Message
            End Function
        End Class

        Sub Main()
            Dim e As New MyErr()
            Console.WriteLine(e.Describe())
        End Sub
        """;

    internal const string P15Expected = "E:boom";

    /// <summary>CP1 — task #146's own probe, a plain FIELD (not a property) read across a call.
    /// Before #146, CopyPropagation kept its OWN kill rules (it was not a consumer of the shared
    /// kill vocabulary at all) and let the stale copy fact <c>K -&gt; 5</c> survive <c>Inc()</c>,
    /// printing "6" where "16" is correct. #146 made <c>CopyPropagationPass</c> a consumer of
    /// <c>OptimizationPass.NamesWrittenBy</c>/<c>IsCallVisible</c>/<c>ReadsCallVisible</c>, the
    /// same vocabulary ADR-0006 gave CSE/LICM/the verifier, so the call now kills the fact and
    /// this probe is RIGHT on all four backends. Unaffected by ADR-0007 either way — that ADR is
    /// scoped to ACCESSOR-BACKED members, and K here is a plain field.</summary>
    internal const string CP1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Inc()
                K = K + 10
            End Sub

            Sub Work()
                K = 5
                Inc()
                Console.WriteLine(CStr(K + 1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string CP1Expected = "16";
}

/// <summary>
/// STRUCTURAL pins on the no-optimizer IR (IRBuilder alone, no <c>OptimizationPipeline</c> run) —
/// the contract's own words: "no-optimizer IR dump of P4/P5 shows no IRVariable spelled P or
/// Tick" and "exactly the node its qualified form produces". The dump format is the implementer's
/// own tool's (S/adr7/tool/Program.cs's <c>ir</c> mode) reproduced verbatim, matching the task's
/// instruction to "compare the IR dumps as text, the same way the implementer's tool does" — a
/// receiver line for IRFieldAccess/IRFieldStore included, so a receiver TYPE regression (Me vs.
/// the wrong class) would show up as a text diff too, not just a name match.
/// </summary>
[TestFixture]
public class BarePropertyLoweringStructuralIrTests
{
    private static string DumpIr(IRModule module)
    {
        var sb = new StringBuilder();
        foreach (var fn in module.Functions)
        {
            sb.AppendLine($"fn {fn.Name} lambda={fn.IsLambda} params=[{string.Join(",", fn.Parameters.Select(x => x.Name + (x.IsByRef ? "&" : "")))}] locals=[{string.Join(",", fn.LocalVariables.Select(x => x.Name))}]");
            foreach (var b in fn.Blocks)
            {
                sb.AppendLine("  " + b.Name + ":");
                foreach (var ins in b.Instructions)
                    sb.AppendLine("    " + ins.GetType().Name + "  " + ins + ReceiverInfo(ins));
            }
        }
        return sb.ToString();
    }

    private static string ReceiverInfo(IRInstruction i) => i switch
    {
        IRFieldAccess fa => $"   [recv {fa.Object?.GetType().Name}:{fa.Object?.Name} type={fa.Object?.Type?.Name} kind={fa.Object?.Type?.Kind} ftype={fa.Type?.Name}]",
        IRFieldStore fs => $"   [recv {fs.Object?.GetType().Name}:{fs.Object?.Name} type={fs.Object?.Type?.Name} kind={fs.Object?.Type?.Kind}]",
        _ => "",
    };

    private static string ProbeSource(string name) => name switch
    {
        nameof(BarePropertyLoweringProbes.P1) => BarePropertyLoweringProbes.P1,
        nameof(BarePropertyLoweringProbes.P3) => BarePropertyLoweringProbes.P3,
        nameof(BarePropertyLoweringProbes.P4) => BarePropertyLoweringProbes.P4,
        nameof(BarePropertyLoweringProbes.P5) => BarePropertyLoweringProbes.P5,
        nameof(BarePropertyLoweringProbes.P6) => BarePropertyLoweringProbes.P6,
        nameof(BarePropertyLoweringProbes.P6q) => BarePropertyLoweringProbes.P6q,
        nameof(BarePropertyLoweringProbes.P6g) => BarePropertyLoweringProbes.P6g,
        nameof(BarePropertyLoweringProbes.P6gq) => BarePropertyLoweringProbes.P6gq,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>P4≡P1, P5≡P3, P6≡P6q, P6g≡P6gq — MEASURED byte-identical against this exact
    /// working tree before being pinned (both diff empty, no name-based normalisation needed:
    /// class/member/function names are identical between a bare probe and its qualified twin —
    /// only the RECEIVER EXPRESSION differs in the source, and after lowering it does not).</summary>
    [TestCase(nameof(BarePropertyLoweringProbes.P4), nameof(BarePropertyLoweringProbes.P1), TestName = "P4_EqualsP1_ItsMeQualifiedTwin")]
    [TestCase(nameof(BarePropertyLoweringProbes.P5), nameof(BarePropertyLoweringProbes.P3), TestName = "P5_EqualsP3_ItsMeQualifiedTwin")]
    [TestCase(nameof(BarePropertyLoweringProbes.P6), nameof(BarePropertyLoweringProbes.P6q), TestName = "P6_EqualsP6q_ItsBoxQualifiedTwin")]
    [TestCase(nameof(BarePropertyLoweringProbes.P6g), nameof(BarePropertyLoweringProbes.P6gq), TestName = "P6g_EqualsP6gq_ItsBoxQualifiedTwin")]
    public void BareForm_NoOptimizerIr_EqualsItsQualifiedTwin(string bareName, string qualifiedName)
    {
        var bareDump = DumpIr(JsTestSupport.BuildModule(ProbeSource(bareName), sourceFilePath: "prog.bas"));
        var qualifiedDump = DumpIr(JsTestSupport.BuildModule(ProbeSource(qualifiedName), sourceFilePath: "prog.bas"));

        Assert.That(bareDump, Is.EqualTo(qualifiedDump),
            $"{bareName}'s no-optimizer IR must be byte-identical to {qualifiedName}'s — the bare "
            + "name must lower to EXACTLY the node its qualified form produces (ADR-0007 D1).");
    }

    /// <summary>No <see cref="IRVariable"/> anywhere in the module — operand, destination, or a
    /// value renamed after the name — spells the bare-used member, walking the SAME operand trees
    /// (<see cref="OptimizationPass.UsesOf"/>) <see cref="IRVerifier.CheckInvariantF"/> itself
    /// walks, rather than a fragile text search on the dump.</summary>
    [TestCase(nameof(BarePropertyLoweringProbes.P4), "P")]
    [TestCase(nameof(BarePropertyLoweringProbes.P5), "Tick")]
    public void NoOptimizerIr_HasNoIRVariableSpelled(string probeName, string memberName)
    {
        var module = JsTestSupport.BuildModule(ProbeSource(probeName), sourceFilePath: "prog.bas");
        AssertNoVariableSpelled(module, memberName);
    }

    private static void AssertNoVariableSpelled(IRModule module, string name)
    {
        foreach (var fn in module.Functions)
        {
            if (fn?.Blocks == null) continue;
            var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
            foreach (var block in fn.Blocks)
            {
                if (block?.Instructions == null) continue;
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRAssignment a && a.Target?.Name == name)
                        Assert.Fail($"{fn.Name}/{block.Name}: an IRAssignment's TARGET spells '{name}' — {a}");

                    foreach (var used in OptimizationPass.UsesOf(inst))
                        WalkForVariable(used, name, seen, fn.Name, block.Name);
                }
            }
        }
    }

    private static void WalkForVariable(IRValue operand, string name, HashSet<IRValue> seen, string fnName, string blockName)
    {
        if (operand == null || !seen.Add(operand)) return;
        if (operand is IRVariable variable && variable.Name == name)
            Assert.Fail($"{fnName}/{blockName}: an IRVariable OPERAND spells '{name}' — {variable}");
        if (operand is IRInstruction nested)
            foreach (var used in OptimizationPass.UsesOf(nested))
                WalkForVariable(used, name, seen, fnName, blockName);
    }
}

// ================================================================================================
//  EXECUTION — real compiled-and-run programs, four backends where they build, both pipelines,
//  cross-checked against S/adr7/probes/matrix-after.txt. This fixture ends in "ExecutionTests" so
//  JsExecutionTierRosterTests's widened name match WOULD catch it on its own; it is still listed
//  explicitly in that roster's array, matching every JS-spawning fixture there.
// ================================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class BarePropertyLoweringExecutionTests
{
    private static (string Source, string Expected) Probe(string name) => name switch
    {
        "P4" => (BarePropertyLoweringProbes.P4, BarePropertyLoweringProbes.P4Expected),
        "P5" => (BarePropertyLoweringProbes.P5, BarePropertyLoweringProbes.P5Expected),
        "P6" => (BarePropertyLoweringProbes.P6, BarePropertyLoweringProbes.P6Expected),
        "P6g" => (BarePropertyLoweringProbes.P6g, BarePropertyLoweringProbes.P6gExpected),
        "P7" => (BarePropertyLoweringProbes.P7, BarePropertyLoweringProbes.P7Expected),
        "P8" => (BarePropertyLoweringProbes.P8, BarePropertyLoweringProbes.P8Expected),
        "P9" => (BarePropertyLoweringProbes.P9, BarePropertyLoweringProbes.P9Expected),
        "P10" => (BarePropertyLoweringProbes.P10, BarePropertyLoweringProbes.P10Expected),
        "CP3" => (BarePropertyLoweringProbes.CP3, BarePropertyLoweringProbes.CP3Expected),
        "CP4" => (BarePropertyLoweringProbes.CP4, BarePropertyLoweringProbes.CP4Expected),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private const string AllProbeNames = "P4,P5,P6,P6g,P7,P8,P9,P10,CP3,CP4";

    // ---- All four backends — standard and aggressive pipelines. C++ was excluded until task #148:
    //      it lowered every property read and write to a field access, so no Get/Set probe built. ----

    /// <summary>
    /// ⛔ The JavaScript leg is <see cref="JavaScriptOptimizedExecutionTests.RunOptimized"/>
    /// (<c>AddStandardPasses</c>), NOT <see cref="JavaScriptExecutionTests.RunJs"/> — MEASURED:
    /// <c>RunJs</c> runs ZERO optimizer passes (<c>JsTestSupport.Compile</c>), so CSE and
    /// CopyPropagation — the passes whose merge-across-the-accessor is the whole defect this ADR
    /// fixes — never run at all on that path, which would make this assertion pass whether the
    /// fix is present or not. CopyPropagationPass is in <c>AddStandardPasses</c> (CP3/CP4 read
    /// through it), so the standard-pipeline claim needs the pipeline that actually contains it.
    /// C# (<c>RunEmittedCSharp</c>) and MSIL (<c>RunExpectingSuccess</c>) already run
    /// <c>AddStandardPasses</c> by default — only the JavaScript helper was the zero-optimizer
    /// outlier.
    /// </summary>
    [TestCase("P4")] [TestCase("P5")] [TestCase("P6")] [TestCase("P6g")] [TestCase("P7")]
    [TestCase("P8")] [TestCase("P9")] [TestCase("P10")] [TestCase("CP3")] [TestCase("CP4")]
    public void StandardPipeline_AllFourBackendsAgree(string probeName)
    {
        var (source, expected) = Probe(probeName);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL");
        });
    }

    [TestCase("P4")] [TestCase("P5")] [TestCase("P6")] [TestCase("P6g")] [TestCase("P7")]
    [TestCase("P8")] [TestCase("P9")] [TestCase("P10")] [TestCase("CP3")] [TestCase("CP4")]
    public void AggressivePipeline_AllFourBackendsAgree(string probeName)
    {
        var (source, expected) = Probe(probeName);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(source)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(source))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>Both TestCase lists above must stay in sync — a name added to one and not the
    /// other silently drops a pipeline's coverage for that probe.</summary>
    [Test]
    public void BothPipelineTestCaseListsCoverTheSameProbes()
    {
        var expected = AllProbeNames.Split(',');
        var standard = GetType().GetMethod(nameof(StandardPipeline_AllFourBackendsAgree))!
            .GetCustomAttributes(typeof(TestCaseAttribute), false).Cast<TestCaseAttribute>()
            .Select(a => (string)a.Arguments[0]).ToArray();
        var aggressive = GetType().GetMethod(nameof(AggressivePipeline_AllFourBackendsAgree))!
            .GetCustomAttributes(typeof(TestCaseAttribute), false).Cast<TestCaseAttribute>()
            .Select(a => (string)a.Arguments[0]).ToArray();

        Assert.That(standard, Is.EquivalentTo(expected), "standard-pipeline TestCases drifted from AllProbeNames");
        Assert.That(aggressive, Is.EquivalentTo(expected), "aggressive-pipeline TestCases drifted from AllProbeNames");
    }

    // ---- P16: the Overridable case, MSIL only per the brief's scope ------------------------------

    [Test]
    public void P16_OverridableAutoProperty_StandardPipeline_Msil()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(BarePropertyLoweringProbes.P16)),
            Is.EqualTo(BarePropertyLoweringProbes.P16Expected));

    [Test]
    public void P16_OverridableAutoProperty_AggressivePipeline_Msil()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(BarePropertyLoweringProbes.P16)),
            Is.EqualTo(BarePropertyLoweringProbes.P16Expected));

    // ---- Controls: must not move ------------------------------------------------------------------

    /// <summary>P12 — a bare plain auto-property, green on ALL FOUR backends including C++.</summary>
    [Test]
    public void P12_BarePlainAutoProperty_StandardPipeline_AllFourBackendsGreen()
        => FourBackends.RunsOnEveryBackend(BarePropertyLoweringProbes.P12, BarePropertyLoweringProbes.P12Expected);

    [Test]
    public void P12_BarePlainAutoProperty_AggressivePipeline_AllFourBackendsGreen()
        => FourBackends.RunsOnEveryBackendAggressive(BarePropertyLoweringProbes.P12, BarePropertyLoweringProbes.P12Expected);

    /// <summary>P13 — a shadowing LOCAL, on all four backends.</summary>
    [Test]
    public void P13_ShadowingLocal_StandardPipeline_AllFourBackendsAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(BarePropertyLoweringProbes.P13)),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(BarePropertyLoweringProbes.P13))),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(BarePropertyLoweringProbes.P13)),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(BarePropertyLoweringProbes.P13)),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "MSIL");
        });

    [Test]
    public void P13_ShadowingLocal_AggressivePipeline_AllFourBackendsAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(BarePropertyLoweringProbes.P13)),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(BarePropertyLoweringProbes.P13))),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(BarePropertyLoweringProbes.P13)),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(BarePropertyLoweringProbes.P13)),
                Is.EqualTo(BarePropertyLoweringProbes.P13Expected), "MSIL");
        });

    /// <summary>CP1 — task #146's own plain-FIELD probe: NOW "16" on all four backends, both
    /// pipelines. Before #146 this printed "6" (the stale copy fact <c>K -&gt; 5</c> survived
    /// <c>Inc()</c>) and was pinned KNOWN-WRONG; #146 made <see cref="CopyPropagationPass"/> a
    /// consumer of the shared kill vocabulary (ADR-0006), so the call now kills the fact and this
    /// is RIGHT — re-pinned here so a regression changes this loudly. Unaffected by ADR-0007 (K
    /// here is a plain field, out of that ADR's accessor-backed scope). The JavaScript "standard"
    /// leg MUST go through <see cref="JavaScriptOptimizedExecutionTests.RunOptimized"/>
    /// (AddStandardPasses, which contains CopyPropagationPass) — <see cref="JavaScriptExecutionTests.RunJs"/>
    /// runs no optimizer at all (task #153) and would print "16" whether or not #146's fix is
    /// present, silently certifying nothing about this pass.</summary>
    [Test]
    public void CP1_PlainFieldAcrossACall_NowCorrect_Task146()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(BarePropertyLoweringProbes.CP1)),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(BarePropertyLoweringProbes.CP1))),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "C++, standard");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(BarePropertyLoweringProbes.CP1)),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(BarePropertyLoweringProbes.CP1)),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "MSIL, standard");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(BarePropertyLoweringProbes.CP1)),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(BarePropertyLoweringProbes.CP1))),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "C++, aggressive");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(BarePropertyLoweringProbes.CP1)),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "JavaScript, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(BarePropertyLoweringProbes.CP1)),
                Is.EqualTo(BarePropertyLoweringProbes.CP1Expected), "MSIL, aggressive");
        });

    /// <summary>P15 — KNOWN-WRONG, task #151, UNCHANGED by ADR-0007 (see the probe's own doc
    /// comment: <c>Message</c> is a BUILT-IN base property, outside the IR module, so Invariant F
    /// and the lowering never touch it). C# is RIGHT and unaffected; C++ still does not build
    /// (Exception is unmapped — task #141's family); JavaScript and MSIL crash exactly as before.</summary>
    [Test]
    public void P15_ExceptionMessageBareInSubclass_KnownWrong_Task151()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(BarePropertyLoweringProbes.P15)),
                Is.EqualTo(BarePropertyLoweringProbes.P15Expected), "C# — correct, unaffected control");

            var cppEx = Assert.Throws<AssertionException>(
                () => BclE2E.CompileRun(BclE2E.CompileToCppOptimized(BarePropertyLoweringProbes.P15)));
            Assert.That(cppEx!.Message, Does.Contain("C++ compilation failed"), "C++ — still does not build");

            var (jsExit, _, jsErr) = RunNodeAllowingFailure(JsTestSupport.Compile(BarePropertyLoweringProbes.P15));
            Assert.That(jsExit, Is.Not.Zero, "JavaScript was expected to CRASH (task #151, pre-existing)");
            Assert.That(jsErr, Does.Contain("ReferenceError: Message is not defined"),
                "expected the specific, MEASURED failure mode — a different error means the gap moved");

            var msilRun = MsilHarness.Run(BarePropertyLoweringProbes.P15);
            Assert.That(msilRun.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), msilRun.Report);
            Assert.That(msilRun.Output, Does.Contain("InvalidProgramException"), "MSIL — the same pre-existing crash");
        });

    // ---- The MSIL ByRef ladder's "is a PROPERTY" arm — now reached ONLY by a bare PLAIN
    //      auto-property (MsilByRefTests.BarePropertyNameArgument_IsRefused's Get/Set shape now
    //      hits the generic temporary-value arm instead; see that test's own updated doc comment).

    /// <summary>
    /// MSILBackend.EmitByRefArgument's "'{name}' is a PROPERTY" arm fires only for an
    /// <c>IRVariable</c> whose name is a class property — which, since ADR-0007, a bare Get/Set
    /// (or Overridable/Overrides) property no longer is (it is an <c>IRFieldAccess</c> before the
    /// ladder ever sees a variable). The ONE shape that still reaches this arm is a bare PLAIN
    /// auto-property, never accessor-backed. MEASURED against this exact working tree before being
    /// pinned. Exists so ADR-0007's churn does not leave the arm silently untested.
    /// </summary>
    [Test]
    public void MsilByRefLadder_IsAPropertyArm_StillReachedByABarePlainAutoProperty()
    {
        const string program = """
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Class Holder
             Public Property X As Integer
             Public Sub Go()
              Bump(X)
             End Sub
            End Class
            Sub Main()
             Dim h As New Holder()
             h.Go()
             PrintLine(CStr(h.X))
            End Sub
            """;
        var r = MsilHarness.Run(program);
        Assert.Multiple(() =>
        {
            Assert.That(r.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.GenerateFailed), r.Report);
            Assert.That(r.Detail, Does.Contain("is a PROPERTY"));
            Assert.That(r.Detail, Does.Contain("'X'"));
        });
    }

    // ---- One CLI leg for C#, one Release .blproj leg for MSIL --------------------------------------

    /// <summary>C#, through the CLI single-file entry point (<c>BasicCompiler.CompileFile</c>),
    /// matching <c>KillVocabularyExtensionsExecutionTests.RunThroughEntryPoint</c>'s convention:
    /// in-process, so this runs on Linux without depending on the deployed apphost.</summary>
    [Test]
    public void P4_TheCliSingleFileEntryPoint_CSharp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_BarePropCliEntry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, BarePropertyLoweringProbes.P4);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            var csharp = new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR);
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(csharp)),
                Is.EqualTo(BarePropertyLoweringProbes.P4Expected),
                "BasicCompiler.CompileFile printed the wrong answer.");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    /// <summary>MSIL, through the Release <c>.blproj</c> entry point — the actual CLI process
    /// (<c>BasicCompiler.CompileProjectFiles</c> under the CLI's own <c>-c Release</c> ->
    /// <c>OptimizeAggressive</c> mapping), matching
    /// <c>MsilBinaryOperandCoercionTests.BuildReleaseMsilAndRun</c>'s convention: the CLI's MSIL
    /// build step only EMITS the <c>.il</c> (it never shells to <c>ilasm</c> itself), so
    /// ilasm/dotnet still run through <see cref="MsilHarness.RunIlExpectingSuccess"/>.</summary>
    [Test]
    public void P4_TheReleaseBlprojEntryPoint_Msil()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-barepropmsil-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), BarePropertyLoweringProbes.P4);
            File.WriteAllText(Path.Combine(dir, "App.blproj"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>MSIL</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                  </ItemGroup>
                </BasicLangProject>
                """);

            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(),
                new[] { "build", Path.Combine(dir, "App.blproj"), "-c", "Release" },
                dir,
                timeoutMs: 120_000);
            Assert.That(buildExit, Is.EqualTo(0),
                $"CLI Release MSIL build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

            var ilFiles = Directory.GetFiles(dir, "App.il", SearchOption.AllDirectories);
            Assert.That(ilFiles, Is.Not.Empty,
                $"CLI build claimed success but produced no App.il.\nSTDOUT:\n{buildOut}");

            var output = MsilHarness.RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App");
            Assert.That(FourBackends.Norm(output), Is.EqualTo(BarePropertyLoweringProbes.P4Expected));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    /// <summary>Runs already-generated JS under Node and returns (exit code, stdout, stderr)
    /// instead of hard-asserting success — <c>JavaScriptExecutionTests.RunNodeScript</c> asserts
    /// exit code ZERO unconditionally, wrong for pinning a KNOWN crash (P15). Mirrors
    /// <c>CallVisibilityDeclarationsRuleTests.RunNodeAllowingFailure</c>'s process handling.</summary>
    private static (int ExitCode, string Stdout, string Stderr) RunNodeAllowingFailure(string js)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null)
            Assert.Ignore("Node.js not found — the JS execution tier cannot run on this machine.");

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_BarePropJsCrash_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "program.mjs");
            File.WriteAllText(file, js);

            var psi = new ProcessStartInfo(node!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(file);

            using var p = Process.Start(psi)!;
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(30000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                Assert.Fail($"node did not exit within 30s.\n--- generated JS ---\n{js}");
            }

            return (p.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}

// ================================================================================================
//  Invariant F — hand-built IR, ported from S/adr7/tool's `hand` cases (Program.cs, this session's
//  scratchpad), plus four shapes the tool's own hand mode did not cover (operand read, renamed
//  destination, lambda attribution, a function in no class) that were added to it and MEASURED
//  against this exact working tree before being ported here.
// ================================================================================================

/// <summary>Builds the exact hand-IR shapes S/adr7/tool/Program.cs's <c>hand</c> mode builds, so
/// every case here is a direct port of MEASURED evidence rather than an independent guess at
/// <see cref="IRVerifier.CheckInvariantF"/>'s behaviour.</summary>
internal static class InvariantFHandIr
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Void);

    /// <param name="ownerFunctionName">The function the one expected violation (if any) should be
    /// reported against — "Work" for every base variant, "__lambda_0" for "lambda", "Main" for
    /// "noclass".</param>
    internal static IRModule Build(string variant, out string ownerFunctionName)
    {
        var m = new IRModule("Hand");
        var baseCls = new IRClass("BaseBox");
        var cls = new IRClass("Box") { BaseClass = variant is "baseprop" or "baseprop-derivedfield" ? "BaseBox" : null };
        m.Classes["Box"] = cls;
        m.Classes["BaseBox"] = baseCls;
        var getter = m.CreateFunction("Box.get_P", IntType);
        getter.CreateBlock("entry").Instructions.Add(new IRReturn(new IRConstant(1, IntType)));

        switch (variant)
        {
            case "operand":
            {
                cls.Properties.Add(new IRProperty { Name = "P", Type = IntType, Getter = getter });
                var w = m.CreateFunction("Work", VoidType);
                var b = w.CreateBlock("entry");
                var pv = new IRVariable("P", IntType);
                b.Instructions.Add(new IRBinaryOp("t0", BinaryOpKind.Add, pv, new IRConstant(1, IntType), IntType));
                b.Instructions.Add(new IRReturn());
                cls.Methods.Add(new IRMethod { Name = "Work", Implementation = w });
                ownerFunctionName = "Work";
                return m;
            }
            case "renamed":
            {
                cls.Properties.Add(new IRProperty { Name = "P", Type = IntType, Getter = getter });
                var w = m.CreateFunction("Work", VoidType);
                var b = w.CreateBlock("entry");
                var k = new IRVariable("K", IntType);
                b.Instructions.Add(new IRBinaryOp("P", BinaryOpKind.Add, k, new IRConstant(5, IntType), IntType) { NamedAfterVariable = true });
                b.Instructions.Add(new IRReturn());
                cls.Methods.Add(new IRMethod { Name = "Work", Implementation = w });
                ownerFunctionName = "Work";
                return m;
            }
            case "lambda":
            {
                cls.Properties.Add(new IRProperty { Name = "P", Type = IntType, Getter = getter });
                var w = m.CreateFunction("Work", VoidType);
                var wb = w.CreateBlock("entry");
                cls.Methods.Add(new IRMethod { Name = "Work", Implementation = w });

                var lambda = m.CreateFunction("__lambda_0", VoidType);
                lambda.IsLambda = true;
                var lb = lambda.CreateBlock("entry");
                var pv = new IRVariable("P", IntType);
                lb.Instructions.Add(new IRAssignment(pv, new IRConstant(10, IntType)));
                lb.Instructions.Add(new IRReturn());

                // IRBuilder leaves the creator a VARIABLE reference to the lambda's own name —
                // this is what links __lambda_0 to Work's (and so Box's) owner in the F walk.
                var lambdaRef = new IRVariable("__lambda_0", IntType);
                var show = new IRCall("", "Show", IntType);
                show.Arguments.Add(lambdaRef);
                wb.Instructions.Add(show);
                wb.Instructions.Add(new IRReturn());

                ownerFunctionName = "__lambda_0";
                return m;
            }
            case "noclass":
            {
                cls.Properties.Add(new IRProperty { Name = "P", Type = IntType, Getter = getter });
                // Main is never added to any class's Methods/Constructors/Properties.
                var main = m.CreateFunction("Main", VoidType);
                var mb = main.CreateBlock("entry");
                var pv = new IRVariable("P", IntType);
                mb.Instructions.Add(new IRAssignment(pv, new IRConstant(10, IntType)));
                mb.Instructions.Add(new IRReturn());
                ownerFunctionName = "Main";
                return m;
            }
        }

        // prop | field | autoprop | baseprop | shadowlocal | global | overridable | baseprop-derivedfield
        var work = m.CreateFunction("Work", VoidType);
        var bb = work.CreateBlock("entry");
        var target = variant.StartsWith("baseprop") ? baseCls : cls;
        if (variant is "prop" or "shadowlocal" or "global" || variant.StartsWith("baseprop"))
            target.Properties.Add(new IRProperty { Name = "P", Type = IntType, Getter = getter });
        if (variant == "autoprop") cls.Properties.Add(new IRProperty { Name = "P", Type = IntType });
        if (variant == "overridable") cls.Properties.Add(new IRProperty { Name = "P", Type = IntType, IsVirtual = true });
        if (variant is "field" or "baseprop-derivedfield") cls.Fields.Add(new IRField { Name = "P", Type = IntType });
        cls.Methods.Add(new IRMethod { Name = "Work", Implementation = work });
        var pv2 = new IRVariable("P", IntType) { IsGlobal = variant == "global" };
        if (variant == "shadowlocal") work.LocalVariables.Add(new IRVariable("P", IntType));
        bb.Instructions.Add(new IRAssignment(pv2, new IRConstant(10, IntType)));
        bb.Instructions.Add(new IRReturn());
        ownerFunctionName = "Work";
        return m;
    }
}

[TestFixture]
public class InvariantFHandBuiltIrTests
{
    /// <summary>F FIRES exactly once for a destination write, for: a Get/Set property ("prop"),
    /// an Overridable auto-property ("overridable"), and an inherited base-class property
    /// ("baseprop"). MEASURED against this exact working tree via S/adr7/tool's hand mode.</summary>
    [TestCase("prop", TestName = "Fires_ForAGetSetProperty")]
    [TestCase("overridable", TestName = "Fires_ForAnOverridableAutoProperty")]
    [TestCase("baseprop", TestName = "Fires_ForAnInheritedBaseClassProperty")]
    public void F_FiresOnce_ForTheDestinationWrite(string variant)
    {
        var module = InvariantFHandIr.Build(variant, out var owner);
        var violations = IRVerifier.CheckInvariantF(module);

        Assert.That(violations, Has.Count.EqualTo(1), string.Join(" | ", violations.Select(v => v.ToString())));
        Assert.Multiple(() =>
        {
            Assert.That(violations[0].IsDestination, Is.True);
            Assert.That(violations[0].Variable, Is.EqualTo("P"));
            Assert.That(violations[0].Function, Is.EqualTo(owner));
        });
    }

    /// <summary>F is QUIET for: a plain field ("field"), a plain auto-property ("autoprop"), a
    /// DERIVED field shadowing a base property ("baseprop-derivedfield"), a shadowing LOCAL
    /// ("shadowlocal"), and a module GLOBAL ("global").</summary>
    [TestCase("field", TestName = "Quiet_ForAPlainField")]
    [TestCase("autoprop", TestName = "Quiet_ForAPlainAutoProperty")]
    [TestCase("baseprop-derivedfield", TestName = "Quiet_ForADerivedFieldShadowingABaseProperty")]
    [TestCase("shadowlocal", TestName = "Quiet_ForAShadowingLocal")]
    [TestCase("global", TestName = "Quiet_ForAModuleGlobal")]
    public void F_IsQuiet(string variant)
    {
        var module = InvariantFHandIr.Build(variant, out _);
        Assert.That(IRVerifier.CheckInvariantF(module), Is.Empty);
    }

    /// <summary>F attributes a base-class property's violation to the DECLARING base, not the
    /// derived class using it bare.</summary>
    [Test]
    public void F_ForABaseClassProperty_AttributesTheDeclaringBase()
    {
        var module = InvariantFHandIr.Build("baseprop", out _);
        var v = IRVerifier.CheckInvariantF(module).Single();
        Assert.That(v.DeclaringClass, Is.EqualTo("BaseBox"));
    }

    /// <summary>F fires for an OPERAND read, not only a destination write — the shape
    /// P5's `t = Tick` had before this ADR: a plain IRVariable read, no assignment TARGET at all.</summary>
    [Test]
    public void F_FiresForAnOperandRead_NotOnlyADestinationWrite()
    {
        var module = InvariantFHandIr.Build("operand", out _);
        var v = IRVerifier.CheckInvariantF(module).Single();
        Assert.Multiple(() =>
        {
            Assert.That(v.IsDestination, Is.False);
            Assert.That(v.Variable, Is.EqualTo("P"));
        });
    }

    /// <summary>F fires for a RENAMED destination — a value renamed after the bare name
    /// (<c>NamedAfterVariable</c>), the shape <c>P = K + 5</c> / <c>P += 10</c> used to lower to,
    /// with no <c>IRAssignment</c> at all.</summary>
    [Test]
    public void F_FiresForARenamedDestination()
    {
        var module = InvariantFHandIr.Build("renamed", out _);
        var v = IRVerifier.CheckInvariantF(module).Single();
        Assert.Multiple(() =>
        {
            Assert.That(v.IsDestination, Is.True);
            Assert.That(v.Variable, Is.EqualTo("P"));
        });
    }

    /// <summary>F fires INSIDE a lambda of the class, attributed to the lambda's own function
    /// name but the CREATOR's (and so the class's) declarations.</summary>
    [Test]
    public void F_FiresInsideALambda_AttributedToTheCreatorsClass()
    {
        var module = InvariantFHandIr.Build("lambda", out var owner);
        var v = IRVerifier.CheckInvariantF(module).Single();
        Assert.Multiple(() =>
        {
            Assert.That(v.Function, Is.EqualTo(owner));
            Assert.That(v.Function, Is.EqualTo("__lambda_0"));
            Assert.That(v.DeclaringClass, Is.EqualTo("Box"));
        });
    }

    /// <summary>F is QUIET for a function in NO class at all — even one whose bare assignment
    /// happens to spell the exact name of an unrelated class's accessor-backed property. A
    /// module-level bare name is never resolved against a class in the first place.</summary>
    [Test]
    public void F_IsQuiet_ForAFunctionInNoClass_EvenWhenItsBareNameMatchesAClassProperty()
    {
        var module = InvariantFHandIr.Build("noclass", out _);
        Assert.That(IRVerifier.CheckInvariantF(module), Is.Empty);
    }
}

/// <summary>Pins that F is actually WIRED into <see cref="IRVerifier.VerifyAfterOptimization"/> —
/// hand-built IR proves the CHECK is right, this proves it is not dead code nobody calls.</summary>
[TestFixture]
[NonParallelizable] // mutates IRVerifier.Mode, like IRVerifierModeResolutionTests
public class InvariantFWiringTests
{
    [Test]
    public void VerifyAfterOptimization_ThrowMode_ThrowsWithInvariantF()
    {
        var previousMode = IRVerifier.Mode;
        var previousLogPath = IRVerifier.LogPath;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            var module = InvariantFHandIr.Build("prop", out _);
            var ex = Assert.Throws<IRVerificationException>(() => IRVerifier.VerifyAfterOptimization(module));

            Assert.That(ex!.Violations.Any(v => v.Invariant == "F"), Is.True,
                "expected at least one Invariant F violation in the thrown exception; got: "
                + string.Join(" | ", ex.Violations.Select(v => v.ToString())));
        }
        finally
        {
            IRVerifier.Mode = previousMode;
            IRVerifier.LogPath = previousLogPath;
        }
    }

    /// <summary>Off must be a true no-op, even over IR that violates Invariant F — mirroring
    /// <c>IRVerifierModeResolutionTests.EnvironmentVariable_Zero_OverridesTheSwitchToOff</c>'s own
    /// non-vacuity check for S′.</summary>
    [Test]
    public void VerifyAfterOptimization_OffMode_DoesNotThrow_EvenOverAnFViolation()
    {
        var previousMode = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Off;
        try
        {
            var module = InvariantFHandIr.Build("prop", out _);
            Assert.DoesNotThrow(() => IRVerifier.VerifyAfterOptimization(module));
        }
        finally { IRVerifier.Mode = previousMode; }
    }
}
