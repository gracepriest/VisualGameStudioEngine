using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0018 D2's proof with no shipping caller: a TEST-ONLY optimizer pass, registered through the real
/// <see cref="OptimizationPipeline.AddPass"/>, that mints ONE temp through <see cref="IRFunction.DeclareTemp"/> in
/// one function and writes <see cref="Poison"/> into it at the top of EVERY block of that function. If the minted
/// name were a name the program owns there — a local, a parameter, a For Each / Catch / pattern / LINQ range
/// variable, a lambda parameter, a module-level function — the write would land on the program's storage (or
/// the two declarations would clash), and the program would stop printing VB's answer.
/// </summary>
internal sealed class MintOneTempPass : OptimizationPass
{
    internal const int Poison = 12345;

    private static readonly TypeInfo IntegerType = new("Integer", TypeKind.Primitive);
    private readonly Func<IRFunction, bool> _target;
    private readonly HashSet<IRFunction> _done = new(ReferenceEqualityComparer.Instance);

    internal MintOneTempPass(Func<IRFunction, bool> target) : base("Test: mint one temp (ADR-0018 D2)")
    {
        _target = target;
    }

    /// <summary>The variable minted in each target function, by the function's name.</summary>
    internal Dictionary<string, IRVariable> Minted { get; } = new();

    public override bool Run(IRModule module)
    {
        ModificationCount = 0;
        foreach (var function in IRTempNames.AllFunctions(module))
        {
            if (function.IsExternal || function.Blocks.Count == 0 || !_target(function) || !_done.Add(function)) continue;

            var temp = function.DeclareTemp(IntegerType);
            Minted[function.Name] = temp;
            foreach (var block in function.Blocks)
                block.Instructions.Insert(0, new IRAssignment(temp, new IRConstant(Poison, IntegerType)) { ParentBlock = block });
            ReportModification();
        }
        return ModificationCount > 0;
    }
}

/// <summary>One position a program can own a temp-shaped name in, as a program whose target function holds it.</summary>
public sealed record MintPosition(string Id, string Template, string Vb, bool TargetIsLambda = false, Bk Runs = Bk.All)
{
    /// <summary>The program with the position's name spelled <paramref name="name"/>.</summary>
    public string Source(string name) => Template.Replace("{V}", name);

    public override string ToString() => Id;
}

/// <summary>
/// ⭐ ADR-0018 D2 (the minting facility) and clarification C1 (the one door), proven without a shipping caller.
///
/// <para><b>How a witness is made.</b> A pass mints the NEXT name its function's counter would hand out, so a user
/// <c>t0</c> is never at risk — the builder's own temps took the low numbers. Each program is therefore built twice:
/// first with an ordinary spelling (the control), which tells the pass's minted name <c>t{N}</c>; then with the
/// position's variable spelled exactly <c>t{N}</c> (and <c>T{N}</c>). A name does not change the IR's shape, so the
/// counter stands at N again and the ONLY thing between the pass and the program's own storage is the
/// reservation. Without it (or with the minter not consulting it) the pass takes <c>t{N}</c> and poisons it.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class TempMintingFacilityTests
{
    private const string Show = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        """;

    internal static readonly MintPosition[] Positions =
    {
        new("Dim", Show + """
            Sub Run(a As Integer)
                Dim {V} As Integer = a + 1
                If a > 0 Then
                    Show({V})
                End If
                Show({V} * 2)
            End Sub

            Sub Main()
                Run(5)
            End Sub
            """, "6\n12"),
        new("Parameter", Show + """
            Sub Run({V} As Integer)
                If {V} > 0 Then
                    Show({V})
                End If
                Show({V} * 2)
            End Sub

            Sub Main()
                Run(5)
            End Sub
            """, "5\n10"),
        new("ForEach", Show + """
            Sub Run(lst As List(Of Integer))
                For Each {V} As Integer In lst
                    Show({V})
                Next
            End Sub

            Sub Main()
                Dim lst As New List(Of Integer)
                lst.Add(3)
                lst.Add(4)
                Run(lst)
            End Sub
            """, "3\n4"),
        new("Catch", Show + """
            Sub Run(a As Integer)
                Try
                    Throw New Exception("x" & CStr(a))
                Catch {V} As Exception
                    Console.WriteLine({V}.Message)
                End Try
            End Sub

            Sub Main()
                Run(5)
            End Sub
            """, "x5"),
        new("Pattern", Show + """
            Sub Run(o As Object)
                Select Case o
                    Case {V} As Integer
                        Show({V})
                    Case Else
                        Show(0)
                End Select
            End Sub

            Sub Main()
                Run(4)
                Run("s")
            End Sub
            """, "4\n0", Runs: Bk.CSharp),
        new("LambdaParameter", Show + """
            Sub Run(a As Integer)
                Dim f As Func(Of Integer, Integer) = Function({V} As Integer) {V} * 2
                Show(f(a))
            End Sub

            Sub Main()
                Run(5)
            End Sub
            """, "10", TargetIsLambda: true),
        // A lambda that READS its creator's For Each variable: the name is the creator's, and the lambda mints from
        // its own counter (ADR-0018 D1: a lambda reserves every name of the function that creates it).
        new("CapturedByLambda", Show + """
            Sub Run(lst As List(Of Integer))
                For Each {V} As Integer In lst
                    Dim f As Func(Of Integer) = Function() {V} * 2
                    Show(f())
                Next
            End Sub

            Sub Main()
                Dim lst As New List(Of Integer)
                lst.Add(3)
                lst.Add(4)
                Run(lst)
            End Sub
            """, "6\n8", TargetIsLambda: true),
        new("ModuleFunction", Show + """
            Function {V}() As Integer
                Return 42
            End Function

            Sub Run(a As Integer)
                If a > 0 Then
                    Show({V}())
                End If
                Show(a)
            End Sub

            Sub Main()
                Run(5)
            End Sub
            """, "42\n5"),
    };

    /// <summary>The LINQ range variable: no backend runs a LINQ query of this shape today, before or after #121
    /// (ADR-0017's witness `LQ_*`, and this program's own control), so its proof is at the IR level only.</summary>
    internal static readonly MintPosition Linq = new("LinqRange", Show + """
        Sub Run(lst As List(Of Integer))
            Dim q = From {V} In lst Select CInt({V}) * 2
            For Each y As Integer In q
                Show(y)
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            lst.Add(4)
            Run(lst)
        End Sub
        """, "6\n8", Runs: 0);

    private static bool IsTarget(MintPosition position, IRFunction function)
        => position.TargetIsLambda ? function.IsLambda : function.Name == "Run";

    /// <summary>IR through the builder, then the shipped passes with the test pass registered after them.</summary>
    internal static (IRModule Module, MintOneTempPass Pass) BuildAndMint(MintPosition position, string name, bool aggressive)
    {
        var module = TempIr.Build(position.Source(name));
        var pass = new MintOneTempPass(f => IsTarget(position, f));
        var pipeline = new OptimizationPipeline();
        if (aggressive) pipeline.AddAggressivePasses(); else pipeline.AddStandardPasses();
        pipeline.AddPass(pass);
        pipeline.Run(module);
        Assert.That(pass.Minted, Has.Count.EqualTo(1), $"{position.Id}: the test pass must find exactly one target function");
        return (module, pass);
    }

    /// <summary>What the pass mints in the control spelling: the name an UNRESERVED position would lose.</summary>
    internal static string WitnessName(MintPosition position, bool aggressive)
    {
        var control = position.Id == "ModuleFunction" ? "Fx" : "x";
        var (_, pass) = BuildAndMint(position, control, aggressive);
        return pass.Minted.Values.Single().Name;
    }

    private static string Emit(Bk backend, IRModule module) => backend switch
    {
        Bk.CSharp => new ImprovedCSharpCodeGenerator().Generate(module),
        Bk.Cpp => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module),
        Bk.JavaScript => new JavaScriptCodeGenerator().Generate(module),
        Bk.Msil => new MSILCodeGenerator().Generate(module),
        _ => throw new ArgumentException(backend.ToString()),
    };

    private static IEnumerable<TestCaseData> Cells()
    {
        foreach (var position in Positions)
            foreach (var backend in TempExec.Backends(position.Runs))
                foreach (var upper in new[] { false, true })
                    foreach (var aggressive in new[] { false, true })
                        yield return new TestCaseData(position, backend, upper, aggressive)
                            .SetName($"{position.Id}_{(upper ? "T" : "t")}N_{backend}_{(aggressive ? "aggressive" : "standard")}");
    }

    /// <summary>
    /// The positions are the proof; a table that quietly loses a row is a proof with a hole in it. Pinned by count, and (since the
    /// only other test here is table-driven, which <c>JsExecutionTierRosterTests</c> cannot count) the one plain test that lets this
    /// fixture be in that roster: seven of its eight positions run under Node.
    /// </summary>
    [Test]
    public void ThePositionTable_HasItsRows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Positions.Select(p => p.Id), Is.EqualTo(new[]
                { "Dim", "Parameter", "ForEach", "Catch", "Pattern", "LambdaParameter", "CapturedByLambda", "ModuleFunction" }));
            Assert.That(Cells().Count(), Is.EqualTo(Positions.Sum(p => TempExec.Backends(p.Runs).Count()) * 4),
                "every position on each backend it runs on, in both spellings and both pipelines");
            Assert.That(Cells().Count(), Is.EqualTo(116), "seven positions on four backends and Pattern on C# only, in two spellings and two pipelines: (7 × 4 + 1) × 2 × 2");
            Assert.That(Linq.Runs, Is.EqualTo((Bk)0), "the LINQ range variable runs on no backend: its proof is the IR-level test in TempMintingDoorTests");
        });
    }

    /// <summary>
    /// ⭐ Falsifier 5: the position's variable is spelled exactly the name the pass would otherwise take — lower and
    /// upper case — and the program still prints VB's answer on the backend, with the temp declared and minted.
    /// </summary>
    [TestCaseSource(nameof(Cells))]
    public void AMintedTemp_NeverTakesANameTheProgramOwns_AndTheProgramRuns(MintPosition position, Bk backend, bool upper, bool aggressive)
    {
        TempExec.RequireTool(backend);
        var witness = WitnessName(position, aggressive);
        var spelled = upper ? witness.ToUpperInvariant() : witness;
        var (module, pass) = BuildAndMint(position, spelled, aggressive);
        var function = IRTempNames.AllFunctions(module).Single(f => IsTarget(position, f));
        var temp = pass.Minted[function.Name];

        Assert.Multiple(() =>
        {
            Assert.That(temp.Name, Is.Not.EqualTo(spelled).IgnoreCase, "the pass minted the name the program owns");
            Assert.That(temp.IsCompilerTemp, Is.True, "DeclareTemp marks what it mints");
            Assert.That(function.LocalVariables, Does.Contain(temp), "DeclareTemp declares what it mints");
            Assert.That(function.IsMintedTempName(temp.Name), Is.True, "DeclareTemp mints through the recording minter");
            Assert.That(function.IsReserved(temp.Name), Is.False, "a minted name is never a reserved one");
            Assert.That(function.IsReserved(spelled), Is.True, $"the program's '{spelled}' is reserved in {function.Name}");
            Assert.That(TempExec.Norm(TempExec.Run(backend, Emit(backend, module))), Is.EqualTo(position.Vb),
                $"{position.Id} with its variable spelled '{spelled}', on {backend}");
        });
    }
}

/// <summary>
/// The parts of ADR-0018 D2 / C1 that need no backend: the LINQ range variable at the IR level, and the one-door
/// guard. Not Integration, so the fast subset runs them.
/// </summary>
[TestFixture]
public class TempMintingDoorTests
{
    /// <summary>The LINQ range variable, at the IR level (see <see cref="Linq"/>).</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void ALinqRangeVariable_IsNeverMinted(bool upper)
    {
        var witness = TempMintingFacilityTests.WitnessName(TempMintingFacilityTests.Linq, aggressive: false);
        var spelled = upper ? witness.ToUpperInvariant() : witness;
        var (module, pass) = TempMintingFacilityTests.BuildAndMint(TempMintingFacilityTests.Linq, spelled, aggressive: false);
        var function = IRTempNames.AllFunctions(module).Single(f => f.Name == "Run");
        Assert.Multiple(() =>
        {
            Assert.That(pass.Minted["Run"].Name, Is.Not.EqualTo(spelled).IgnoreCase);
            Assert.That(function.ReservedNames, Does.Contain(spelled), "the range variable is reserved at the push");
        });
    }

    // ============================================================================================
    // C1 as amended by E1: one door for a pass
    // ============================================================================================

    /// <summary>
    /// ⭐ Clarification C1: an optimizer pass mints only through <see cref="IRFunction.DeclareTemp"/>. Read off the IL of
    /// every method in the compiler assembly (lambdas, local functions and iterator state machines are methods of
    /// nested types, and are attributed to the type that declares them): the ONLY callers of
    /// <see cref="IRFunction.GetNextTempName"/> are IRBuilder, which names its values through it, and
    /// <see cref="IRFunction.DeclareTemp"/>. No <see cref="OptimizationPass"/> and nothing in the optimizer's namespace.
    /// </summary>
    [Test]
    public void GetNextTempName_IsCalledOnlyByIRBuilderAndDeclareTemp()
    {
        var target = typeof(IRFunction).GetMethod(nameof(IRFunction.GetNextTempName))!;
        var callers = IlCalls.CallersOf(target);
        var owners = callers.Select(c => IlCalls.Outermost(c.DeclaringType!)).Distinct().ToList();

        Assert.Multiple(() =>
        {
            // ⭐ Cannot pass vacuously: a scanner that read no IL (a changed IL layout, an assembly it could not load) would find no
            // caller and the "only" assertions below would hold over an empty set. The two callers that exist today must be FOUND.
            Assert.That(callers, Has.Count.GreaterThanOrEqualTo(2), "the scan found fewer callers than the two that exist: it is not reading the IL");
            Assert.That(callers.Any(c => c.DeclaringType == typeof(IRFunction) && c.Name == nameof(IRFunction.DeclareTemp)), Is.True,
                "the scan must see DeclareTemp's call, the one door");
            Assert.That(callers.Any(c => IlCalls.Outermost(c.DeclaringType!) == typeof(IRBuilder)), Is.True,
                "the scan must see IRBuilder's calls: it names its SSA values through the minter");
            Assert.That(owners, Does.Contain(typeof(IRBuilder)), "the scan must see IRBuilder's calls, or it sees nothing");
            Assert.That(callers.Where(c => c.DeclaringType == typeof(IRFunction)).Select(c => c.Name),
                Is.EquivalentTo(new[] { nameof(IRFunction.DeclareTemp) }), "inside IRFunction, only DeclareTemp mints");
            Assert.That(owners.Select(t => t.FullName), Is.SubsetOf(new[] { typeof(IRBuilder).FullName, typeof(IRFunction).FullName }),
                "a new caller of GetNextTempName: an optimizer pass must mint through DeclareTemp (ADR-0018 D2, C1)");
            Assert.That(owners.Where(t => typeof(OptimizationPass).IsAssignableFrom(t)
                                          || t.Namespace == typeof(OptimizationPass).Namespace).Select(t => t.FullName),
                Is.Empty, "an optimizer type calls GetNextTempName directly");
        });
    }

    /// <summary>
    /// The guard above is only as good as its scanner, so the scanner is shown a violation. <see cref="GetNextTempNameDecoy"/> lives in
    /// THIS assembly and calls the minter directly and from inside a lambda; the scanner must find both, and attribute the lambda's
    /// call to the type that declares it. A guard that could not see a caller outside the compiler assembly, or one inside a closure,
    /// would pass on the day a pass started calling the minter from a lambda.
    /// </summary>
    [Test]
    public void TheIlScanner_SeesADirectCall_AndACallFromInsideALambda()
    {
        var target = typeof(IRFunction).GetMethod(nameof(IRFunction.GetNextTempName))!;
        var callers = IlCalls.CallersOf(target, typeof(GetNextTempNameDecoy).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(callers.Any(c => c.DeclaringType == typeof(GetNextTempNameDecoy) && c.Name == nameof(GetNextTempNameDecoy.Direct)), Is.True,
                "a direct call in another assembly");
            Assert.That(callers.Any(c => c.DeclaringType != typeof(GetNextTempNameDecoy) && IlCalls.Outermost(c.DeclaringType!) == typeof(GetNextTempNameDecoy)), Is.True,
                "a call inside a lambda is a method of a nested type, found and attributed to the decoy");
        });
    }
}

/// <summary>
/// Callers of <see cref="IRFunction.GetNextTempName"/> that live in the TEST assembly, so the guard's scanner can be shown to see a
/// call it should see: directly, and from inside a lambda (a method of a compiler-generated nested type).
/// </summary>
internal static class GetNextTempNameDecoy
{
    internal static string Direct(IRFunction function) => function.GetNextTempName();

    internal static Func<string> FromALambda(IRFunction function) => () => function.GetNextTempName();
}

/// <summary>A small IL reader: which methods of an assembly call a given method.</summary>
internal static class IlCalls
{
    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

    static IlCalls()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode op) continue;
            var value = (ushort)op.Value;
            if (value < 0x100) OneByte[value] = op;
            else if ((value & 0xff00) == 0xfe00) TwoByte[value & 0xff] = op;
        }
    }

    internal static Type Outermost(Type type)
    {
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type;
    }

    internal static List<MethodBase> CallersOf(MethodInfo target) => CallersOf(target, target.Module.Assembly);

    /// <summary>The methods of <paramref name="assembly"/> whose IL calls <paramref name="target"/>, which may live in another assembly.</summary>
    internal static List<MethodBase> CallersOf(MethodInfo target, Assembly assembly)
    {
        var found = new List<MethodBase>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
        foreach (var type in types)
        {
            var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (var method in methods)
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch (Exception) { continue; }
                if (il != null && Calls(il, method.Module, target)) found.Add(method);
            }
        }
        return found;
    }

    private static bool Calls(byte[] il, Module module, MethodInfo target)
    {
        var i = 0;
        while (i < il.Length)
        {
            OpCode op;
            if (il[i] == 0xfe) { op = TwoByte[il[i + 1]]; i += 2; }
            else { op = OneByte[il[i]]; i += 1; }

            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: i += 1; break;
                case OperandType.InlineVar: i += 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: i += 8; break;
                case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                case OperandType.InlineMethod:
                {
                    var token = BitConverter.ToInt32(il, i);
                    i += 4;
                    // Inside the target's own module a call names the method's definition token; from another module
                    // it names a member reference (0x0A) or a method specification (0x2B) that resolves to it.
                    if (module == target.Module && token == target.MetadataToken) return true;
                    var table = token >> 24;
                    if (table == 0x0A || table == 0x2B || (module != target.Module && table == 0x06))
                    {
                        try
                        {
                            var resolved = module.ResolveMethod(token);
                            if (resolved.MetadataToken == target.MetadataToken && resolved.Module == target.Module) return true;
                        }
                        catch (Exception) { /* a generic context this reader does not supply */ }
                    }
                    break;
                }
                default: i += 4; break;
            }
        }
        return false;
    }
}
