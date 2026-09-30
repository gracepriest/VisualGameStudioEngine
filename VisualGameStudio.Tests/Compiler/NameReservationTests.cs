using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>The programs the reservation tests read: one that declares every kind of name the builder reserves, and two
/// whose lambdas make ClosureLowering add names of its own.</summary>
internal static class ReservationPrograms
{
    /// <summary>
    /// Every declaration kind and every function kind in one program (ADR-0018 D1's writer table): a constructor, two operators,
    /// a property setter with a declared alias (`nv`), a method whose lambda captures a parameter, and in `Run` a hidden For Each
    /// element (`__foreach_0`, from reusing `x`), a For Each variable captured through TWO nested lambdas, a LINQ range variable,
    /// an Or pattern with two bindings, two Catch clauses (one spelled `T7`), an `AndAlso` carrier (`__sc0`), a `With` carrier
    /// (`__with`), a `Const`, a counted `For` and a lambda parameter.
    /// </summary>
    internal const string AllKinds = """
        Class Box
            Public V As Integer
            Public Sub New(v0 As Integer)
                Me.V = v0
            End Sub
            Public Shared Operator =(a As Box, b As Box) As Boolean
                Return a.V = b.V
            End Operator
            Public Shared Operator <>(a As Box, b As Box) As Boolean
                Return a.V <> b.V
            End Operator
            Public Property W As Integer
                Get
                    Return V
                End Get
                Set(nv As Integer)
                    V = nv
                End Set
            End Property
            Public Function Twice(k As Integer) As Integer
                Dim g As Func(Of Integer) = Function() k * V
                Return g() * 2
            End Function
        End Class

        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer), o As Object, a As Integer, b As Integer)
            Dim x As Integer
            For Each x In lst
                Show(x)
            Next
            For Each fe As Integer In lst
                Dim outer As Func(Of Integer) = Function()
                    Dim innerF As Func(Of Integer) = Function() fe
                    Return innerF() + 1
                End Function
                Show(outer())
            Next
            Dim q = From t5 In lst Select CInt(t5) * 2
            Select Case o
                Case n As Integer Or s As String
                    Show(1)
                Case Else
                    Show(0)
            End Select
            Try
                Throw New Exception("x")
            Catch e1 As ArgumentException
                Show(1)
            Catch T7 As Exception
                Show(2)
            End Try
            If a > 0 AndAlso b > 0 Then
                Show(3)
            End If
            Dim bx As New Box(3)
            With bx
                .V = 4
            End With
            Const K As Integer = 3
            For i As Integer = 0 To 2
                Dim h As Func(Of Integer, Integer) = Function(lp As Integer) i + K + lp
                Show(h(1))
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            Run(lst, 4, 1, 2)
        End Sub
        """;

    /// <summary>A For Each element, a body local captured by a lambda (so ClosureLowering adds a per-iteration CARRIER,
    /// `__carry_body`, and a loop-level environment local, `__closure_env1`) and the function-level environment local
    /// (`__closure_env0`): the three names ClosureLowering itself adds to `Run`.</summary>
    internal const string Carrier = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer))
            For Each e As Integer In lst
                Dim body As Integer = e * 2
                Dim f As Func(Of Integer) = Function() body + e
                Show(f())
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            Run(lst)
        End Sub
        """;

    /// <summary>A lambda inside a lambda, both reading a For Each `t0` of their creator `Run`.</summary>
    internal const string NestedLambdas = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer))
            For Each t0 As Integer In lst
                Dim outer As Func(Of Integer) = Function()
                    Dim inner As Func(Of Integer) = Function() t0
                    Return inner()
                End Function
                Show(outer())
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            Run(lst)
        End Sub
        """;
}

/// <summary>
/// ⭐ ADR-0018 D1 (the reservation set), E1 (published after the walk), E2 (the flag and the exemption) and E3 (module-level names),
/// on real IR and on hand-built IR. Fast tier: no backend, no process.
///
/// <para>The tests in this file exist because the implementer's own witnesses cannot reach four of the sixteen mutants ADR-0018
/// records: "IRBuilder does not set <c>TracksReservedNames</c>", "ClosureLowering does not copy it", "ClosureLowering does not seed
/// the hoisted lambda" and "the D4 refusal dropped" all leave every program printing VB's answer. A reservation is a property of the
/// IR, so the proof is a direct assertion on the IR.</para>
/// </summary>
[TestFixture]
public class NameReservationTests
{
    private static readonly TypeInfo Int = new("Integer", TypeKind.Primitive);
    private static readonly TypeInfo ExType = new("Exception", TypeKind.Class);

    private static IRFunction Fn(IRModule module, string name)
        => IRTempNames.AllFunctions(module).Single(f => f.Name == name);

    /// <summary>The lambda function (before ClosureLowering) or hoisted lambda (after it) that declares a parameter `name`.</summary>
    private static IRFunction LambdaWithParameter(IRModule module, string name)
        => IRTempNames.AllFunctions(module).Single(f => f.Name.StartsWith("__lambda_", StringComparison.Ordinal)
                                                        && f.Parameters.Any(p => p.Name == name));

    private static IEnumerable<IRFunction> Lambdas(IRModule module)
        => IRTempNames.AllFunctions(module).Where(f => f.Name.StartsWith("__lambda_", StringComparison.Ordinal));

    // ============================================================================================
    // E2.2 — every function carries the flag, so Invariant R applies to it
    // ============================================================================================

    private static IEnumerable<TestCaseData> FlagPrograms() => new (string Id, string Source)[]
    {
        ("AllKinds", ReservationPrograms.AllKinds),
        ("LC_t0", TempProbes.LcT0.Source),
        ("Carrier", ReservationPrograms.Carrier),
        ("NestedLambdas", ReservationPrograms.NestedLambdas),
    }.Select(p => new TestCaseData(p.Source).SetName(p.Id));

    /// <summary>
    /// ⭐ P1, first half. <see cref="IRFunction.TracksReservedNames"/> is set by IRBuilder on every function it builds — module
    /// functions, class methods and constructors, property accessors, operators, lambdas — because the verifier's Invariant R is
    /// skipped for a function without it: a function that quietly opts out is a function nobody checks. <b>Kills "IRBuilder does not
    /// set the flag".</b> The assertion names the kinds the program builds, so it cannot pass over an IR that lost half its functions.
    /// </summary>
    [TestCaseSource(nameof(FlagPrograms))]
    public void EveryFunctionIRBuilderBuilds_TracksReservedNames(string source)
    {
        var module = TempIr.Build(source);
        var functions = IRTempNames.AllFunctions(module);

        Assert.Multiple(() =>
        {
            Assert.That(functions.Count, Is.GreaterThanOrEqualTo(4), "the program builds several functions");
            Assert.That(functions.Any(f => f.IsLambda), Is.True, "it builds a lambda: a second creation site for IRFunction");
            Assert.That(functions.Where(f => !f.TracksReservedNames).Select(f => f.Name), Is.Empty,
                "a function IRBuilder built that does not track reservations is exempt from Invariant R (ADR-0018 E2.2)");
        });
    }

    [Test]
    public void EveryKindOfFunction_TracksReservedNames()
    {
        var names = IRTempNames.AllFunctions(TempIr.Build(ReservationPrograms.AllKinds)).ToDictionary(f => f.Name, f => f.TracksReservedNames);

        Assert.That(names.Keys, Is.SupersetOf(new[]
            {
                "Run", "Show", "Main", "Box__ctor", "Box.op_Equality", "Box.op_Inequality", "Box.get_W", "Box.set_W", "Twice",
            }), "a module Sub, a constructor, two operators, both accessors and a method are all built");
        Assert.That(names.Where(kv => !kv.Value).Select(kv => kv.Key), Is.Empty);
    }

    /// <summary>
    /// ⭐ P1, second half. ClosureLowering (which MSIL runs at its own entry) builds a CLONE of every function it lowers, and the
    /// clone carries the flag: the clone is a second creation site for IRFunction. <b>Kills "ClosureLowering does not copy the flag".</b>
    /// The lowering must actually have run (a different module, no function reference-equal to an original), or this asserts over
    /// the original functions.
    /// </summary>
    [TestCaseSource(nameof(FlagPrograms))]
    public void EveryFunctionClosureLoweringProduces_TracksReservedNames(string source)
    {
        var module = TempIr.Build(source);
        var originals = IRTempNames.AllFunctions(module);
        var lowered = ClosureLowering.Run(module);
        var clones = IRTempNames.AllFunctions(lowered);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.Not.SameAs(module), "these programs hold lambdas: ClosureLowering lowers a clone");
            Assert.That(clones.Any(c => originals.Any(o => ReferenceEquals(o, c))), Is.False, "every function of the lowered module is a clone");
            Assert.That(clones.Where(c => !c.TracksReservedNames).Select(c => c.Name), Is.Empty,
                "a clone that lost the flag is exempt from Invariant R (ADR-0018 E2.2)");
        });
    }

    // ---- E2.2 on REAL compiles, through the CLI's two entry points ---------------------------------

    public enum Route { CompileFile, CompileProjectFiles, CompileProjectFilesAggressive }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The five shipped samples. All five compile since #123 (an untyped `Const` and VB's `If(c, a, b)` parse and type, and
    /// Samples/Pong and Samples/SpaceShooter were made valid code). A sample that stops compiling is a failure here, not a skip. This
    /// is the FAST guard that a front-end verdict is never ignored for a sample: the CSE corpus fixture builds IR past a rejected
    /// front end on purpose, and its own verdict pins are the only other thing that notices.</summary>
    private static readonly (string Path, bool Compiles)[] Samples =
    {
        ("SampleGames/Pong/Main.bas", true),
        ("SampleGames/SpaceShooter/Main.bl", true),
        ("Samples/Platformer/Main.bas", true),
        ("Samples/Pong/Main.bas", true),
        ("Samples/SpaceShooter/Main.bas", true),
    };

    /// <summary>Every program a probe fixture holds: the #163 probes, the D2 positions, the witness families, and the two
    /// reservation programs.</summary>
    private static IEnumerable<(string Id, string Source)> RealPrograms()
    {
        foreach (var field in typeof(TempProbes).GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                     .Where(f => f.FieldType == typeof(TempProbe)))
        {
            var probe = (TempProbe)field.GetValue(null);
            yield return (probe.Id, probe.Source);
        }
        foreach (var p in TempMintingFacilityTests.Positions) yield return ("D2_" + p.Id, p.Source("x"));
        yield return ("D2_" + TempMintingFacilityTests.Linq.Id, TempMintingFacilityTests.Linq.Source("x"));
        yield return ("AllKinds", ReservationPrograms.AllKinds);
        yield return ("Carrier", ReservationPrograms.Carrier);
        yield return ("NestedLambdas", ReservationPrograms.NestedLambdas);
    }

    private static IRModule CompileToIr(string path, Route route, out string errors)
    {
        var options = new CompilerOptions { OptimizeAggressive = route == Route.CompileProjectFilesAggressive };
        var compiler = new BasicCompiler(options);
        var result = route == Route.CompileFile ? compiler.CompileFile(path) : compiler.CompileProjectFiles(new List<string> { path });
        errors = string.Join(" | ", result.AllErrors.Select(e => e.Message.Split('\n')[0]));
        return result.HasErrors ? null : result.CombinedIR;
    }

    /// <summary>
    /// ⭐ E2.2's obligation, on real compiles. Whatever route a program takes to a backend — the CLI's single-file route
    /// (<c>BasicCompiler.CompileFile</c>), the project route the CLI's <c>build</c> and the IDE use
    /// (<c>CompileProjectFiles</c>), with the standard or the aggressive passes — every function of the IR it ends with tracks
    /// reservations, and so does every function of the module MSIL lowers it to (MSIL runs <c>ClosureLowering.Run</c> at its own
    /// entry, so C#, C++, JavaScript and LLVM read the first module and MSIL reads the second). The verifier's Invariant R skips a
    /// function without the flag, so a function that silently opts out must be visible here. The shipped samples are included. The same
    /// corpus is the ruling's falsifier 2: Invariants R and T fire zero times on the IR of every route and on MSIL's lowered module.
    /// </summary>
    [TestCase(Route.CompileFile)]
    [TestCase(Route.CompileProjectFiles)]
    [TestCase(Route.CompileProjectFilesAggressive)]
    public void EveryFunctionThatReachesABackend_TracksReservedNames(Route route)
    {
        var failures = new List<string>();
        var dir = Path.Combine(Path.GetTempPath(), "bl-t121-flag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        int programs = 0, functions = 0, lowerings = 0;
        try
        {
            var inputs = new List<(string Id, string Path, bool Compiles)>();
            foreach (var (id, source) in RealPrograms())
            {
                var path = Path.Combine(dir, inputs.Count + ".bas");
                File.WriteAllText(path, source);
                inputs.Add((id, path, true));
            }
            foreach (var (relative, compiles) in Samples)
                inputs.Add(("sample " + relative, Path.Combine(RepoRoot(), relative), compiles));

            foreach (var (id, path, compiles) in inputs)
            {
                var ir = CompileToIr(path, route, out var errors);
                if (ir == null)
                {
                    if (compiles) failures.Add($"{id}: did not compile: {errors}");
                    continue;
                }
                if (!compiles) { failures.Add($"{id}: compiles now — mark it in {nameof(Samples)}"); continue; }

                programs++;
                var reaching = IRTempNames.AllFunctions(ir);
                functions += reaching.Count;
                failures.AddRange(reaching.Where(f => !f.TracksReservedNames).Select(f => $"{id}: '{f.Name}' reaches C#/C++/JavaScript/LLVM without the flag"));

                var lowered = ClosureLowering.Run(ir);
                if (!ReferenceEquals(lowered, ir)) lowerings++;
                failures.AddRange(IRTempNames.AllFunctions(lowered).Where(f => !f.TracksReservedNames).Select(f => $"{id}: '{f.Name}' reaches MSIL without the flag"));

                // ADR-0018's falsifier 2, over this corpus: the reservation invariants fire zero times, on either module.
                foreach (var (label, module) in new[] { ("IR", ir), ("lowered IR", lowered) })
                    failures.AddRange(IRVerifier.CheckInvariantR(module).Concat(IRVerifier.CheckInvariantT(module)).Select(v => $"{id}, {label}: {v}"));
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            Assert.That(programs, Is.GreaterThanOrEqualTo(40), "the probe corpus, the D2 positions, the reservation programs and three samples compile");
            Assert.That(functions, Is.GreaterThanOrEqualTo(150), "…and hold enough functions for the check to mean something");
            Assert.That(lowerings, Is.GreaterThanOrEqualTo(5), "…and several of them hold lambdas, so MSIL's clone is checked, not only the original");
        });
    }

    // ============================================================================================
    // D1 — IRBuilder reserves every kind of declaration (the writer table), in the right function
    // ============================================================================================

    private static TestCaseData Row(string kind, string holder, params string[] names)
        => new TestCaseData(holder, names).SetName(kind);

    private static IEnumerable<TestCaseData> ReservedByTheBuilder()
    {
        yield return Row("Dim_local", "Run", "x", "bx", "q", "h");
        yield return Row("Parameter", "Run", "lst", "o", "a", "b");
        yield return Row("Const", "Run", "K");
        yield return Row("CountedFor_variable", "Run", "i");
        yield return Row("ForEach_variable", "Run", "fe");
        yield return Row("ForEach_hidden_element_of_a_reused_variable", "Run", "__foreach_0");
        yield return Row("Catch_variable_bothClauses_anyCase", "Run", "e1", "T7");
        yield return Row("Pattern_bindings_ofAnOrPattern", "Run", "n", "s");
        // ⭐ E2.3: a LINQ range variable has no declaring IR node, so Invariant R cannot see it: this row is the only thing that
        // pins it. The mutant that leaves it out of the push is killed by this row and by nothing else at the IR level.
        yield return Row("LinqRange_variable", "Run", "t5");
        yield return Row("AndAlso_carrier", "Run", "__sc0");
        yield return Row("With_carrier", "Run", "__with");
        yield return Row("Unbound_name_GetOrCreateVariable", "Show", "Console");
        yield return Row("Constructor_parameter", "Box__ctor", "v0");
        yield return Row("Operator_parameters_a", "Box.op_Equality", "a", "b");
        yield return Row("Operator_parameters_b", "Box.op_Inequality", "a", "b");
        yield return Row("PropertySetter_value_and_alias", "Box.set_W", "value", "nv");
        yield return Row("Method_parameter_with_lambda", "Twice", "k", "g");
        yield return Row("LambdaParameter_lives_in_the_lambda", "lambda:lp", "lp");
        yield return Row("CapturedName_of_the_creator_lives_in_the_lambda", "lambda:lp", "fe", "K", "i", "x", "bx");
    }

    private static IRFunction Holder(IRModule module, string holder)
        => holder.StartsWith("lambda:", StringComparison.Ordinal) ? LambdaWithParameter(module, holder.Substring("lambda:".Length)) : Fn(module, holder);

    /// <summary>
    /// ⭐ D1's writer table, one row per kind. The builder reserves each name in the function that declares it, at the one version
    /// push (ADR-0018), and ignoring case: `T7` reserves `t7` too. The rows are the kinds of the table, so a new declaration kind
    /// that skips the push is a new row.
    /// </summary>
    [TestCaseSource(nameof(ReservedByTheBuilder))]
    public void TheBuilder_Reserves_EveryKindOfDeclaration(string holder, string[] names)
    {
        var module = TempIr.Build(ReservationPrograms.AllKinds);
        var function = Holder(module, holder);

        Assert.Multiple(() =>
        {
            foreach (var name in names)
            {
                // The set's own Contains (OrdinalIgnoreCase), not NUnit's item-by-item Does.Contain, which compares case-sensitively.
                Assert.That(function.ReservedNames.Contains(name), Is.True, $"'{name}' is reserved in {function.Name}");
                Assert.That(function.ReservedNames.Contains(name.ToUpperInvariant()), Is.True, $"'{name}' is reserved ignoring case (upper)");
                Assert.That(function.ReservedNames.Contains(name.ToLowerInvariant()), Is.True, $"'{name}' is reserved ignoring case (lower)");
            }
        });
    }

    /// <summary>The fix the operator visitor needed: it pushed its parameters while the ENCLOSING function was current, so the
    /// names were reserved there and not in the operator. Each operator holds its own.</summary>
    [Test]
    public void AnOperatorsParameters_AreReservedInTheOperator_NotInTheConstructorBeside()
    {
        var module = TempIr.Build(ReservationPrograms.AllKinds);

        Assert.Multiple(() =>
        {
            foreach (var op in new[] { "Box.op_Equality", "Box.op_Inequality" })
                Assert.That(Fn(module, op).ReservedNames, Is.SupersetOf(new[] { "a", "b" }), op);
            Assert.That(Fn(module, "Box__ctor").ReservedNames, Does.Not.Contain("a").And.Not.Contain("b"),
                "the constructor declares neither: the operator's parameters do not leak into a sibling");
            Assert.That(IRVerifier.CheckInvariantR(module), Is.Empty, "Invariant R is what named this defect the first time");
        });
    }

    // ---- ADR-0018 E1: the walk publishes nothing; the reservation is published once, after it -----------------

    /// <summary>
    /// ⭐ E1. IRBuilder RECORDS a reservation at the push and PUBLISHES the record after the walk (`CompleteReservations`, before the
    /// renamer). Published AT the push, `Dim t1` would make the minter skip `t1` from that moment on, and every temp minted after it would
    /// take the next number: U8's loop (`Dim t1` and `t1 = t1 + i`) minted `t0` and `t2`, and would mint `t0` and `t3`, renumbering temps of
    /// a program whose only temp-shaped name was ALREADY reserved. The ruling makes any output change outside the ruled set a regression,
    /// and this is the row that sees it: the two compiler temps of U8's `Sum` keep the numbers they always had (the `t1` in between is the
    /// user's own storage, `t1 = t1 + i`).
    /// </summary>
    [Test]
    public void TheWalkPublishesNothing_SoATempInAProgramWithAReservedT1_KeepsItsNumber()
    {
        var module = TempIr.Build(TempProbes.U8.Source);
        var sum = Fn(module, "Sum");
        var temps = TempIr.Reachable(sum).Where(v => v.IsCompilerTemp).Select(v => v.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sum.ReservedNames.Contains("t1"), Is.True, "precondition: the user's `t1` is reserved");
            Assert.That(temps, Is.EqualTo(new[] { "t0", "t2" }), "the compare and the loop increment: numbered as they were before #121");
        });
    }

    // ---- ADR-0018 D1: a lambda reserves every name of its creator, transitively -----------------------

    /// <summary>
    /// A lambda READS its creator's names (a captured For Each `t0` is an IR variable `t0` inside it) and mints from its OWN
    /// counter, so its set must hold them: the creator's set is a subset of the lambda's, and the lambda's of the lambda inside it.
    /// <b>Kills "IRBuilder does not seed lambdas".</b>
    /// </summary>
    [Test]
    public void ALambda_ReservesEveryNameOfItsCreator_Transitively()
    {
        var module = TempIr.Build(ReservationPrograms.NestedLambdas);
        var run = Fn(module, "Run");
        var lambdas = Lambdas(module).ToList();
        var outer = lambdas.Single(l => l.LocalVariables.Any(v => v.Name == "inner"));
        var innermost = lambdas.Single(l => !ReferenceEquals(l, outer));

        Assert.Multiple(() =>
        {
            Assert.That(lambdas, Has.Count.EqualTo(2));
            Assert.That(run.ReservedNames, Does.Contain("t0"));
            Assert.That(run.ReservedNames.Where(n => !outer.ReservedNames.Contains(n)), Is.Empty, "Run's names are in the outer lambda's set");
            Assert.That(outer.ReservedNames.Where(n => !innermost.ReservedNames.Contains(n)), Is.Empty, "…and the outer lambda's are in the innermost's");
            Assert.That(innermost.ReservedNames, Is.SupersetOf(new[] { "t0", "outer", "inner", "lst" }), "so the innermost lambda holds the captured `t0` and every name above it");
        });
    }

    // ============================================================================================
    // Falsifier 6 and P2 — ClosureLowering: the function it creates holds the names it added
    // ============================================================================================

    /// <summary>
    /// ⭐ Falsifier 6, `LC_t0`'s shape: a For Each `t0` captured by a lambda. The function ClosureLowering CREATES — the hoisted lambda,
    /// and the clone of the creator — has a populated <see cref="IRFunction.ReservedNames"/> holding `t0`. MSIL's own temp counter
    /// reads the lowered module, and an empty set there was `LC_t0`'s crash.
    /// </summary>
    [Test]
    public void LC_t0_TheFunctionsClosureLoweringCreates_HoldTheReservedNames()
    {
        var lowered = ClosureLowering.Run(TempIr.Build(TempProbes.LcT0.Source));
        var run = Fn(lowered, "Run");
        var hoisted = Lambdas(lowered).Single();

        Assert.Multiple(() =>
        {
            Assert.That(hoisted.IsLambda, Is.False, "it is a plain function now: ClosureLowering created it");
            Assert.That(hoisted.ReservedNames, Is.Not.Empty, "the function ClosureLowering creates has a POPULATED reservation");
            Assert.That(hoisted.ReservedNames, Does.Contain("t0"), "the hoisted lambda reads `t0`");
            Assert.That(run.ReservedNames, Does.Contain("t0"), "the clone of the creator still owns it");
            Assert.That(IRTempNames.UserOwned(lowered), Does.Contain("t0"), "the lowered module's UserOwned — what MSIL's counter skips — holds `t0`");
        });
    }

    private static IEnumerable<TestCaseData> AddedByClosureLowering() => new[]
    {
        new TestCaseData("__closure_env0").SetName("FunctionLevelEnvironmentLocal"),
        new TestCaseData("__closure_env1").SetName("LoopLevelEnvironmentLocal"),
        new TestCaseData("__carry_body").SetName("PerIterationCarrier"),
    };

    /// <summary>
    /// ⭐ P2. A name ClosureLowering ADDS to the creator (an environment local or a carrier) is in the hoisted lambda's
    /// <see cref="IRFunction.ReservedNames"/> and in the creator's own: the lambda is seeded from the creator's set AS IT STANDS after
    /// this pass wrote to it, and each of the pass's three <c>LocalVariables</c> writes reserves. IRBuilder already seeded the lambda from
    /// the names it knew, so the mutant that drops ClosureLowering's seeding is masked everywhere except here. Each row is one of the
    /// pass's three write sites.
    /// </summary>
    [TestCaseSource(nameof(AddedByClosureLowering))]
    public void ANameClosureLoweringAddsToTheCreator_IsReservedInTheCreatorAndTheHoistedLambda(string added)
    {
        var module = TempIr.Build(ReservationPrograms.Carrier);
        var before = Fn(module, "Run").LocalVariables.Select(l => l.Name).ToList();
        var lowered = ClosureLowering.Run(module);
        var run = Fn(lowered, "Run");
        var lambda = Lambdas(lowered).Single();

        Assert.Multiple(() =>
        {
            Assert.That(before, Does.Not.Contain(added), "precondition: the name is one ClosureLowering added, not one the builder declared");
            Assert.That(run.LocalVariables.Select(l => l.Name), Does.Contain(added), "ClosureLowering declared it in the creator");
            Assert.That(run.ReservedNames, Does.Contain(added), "…and reserved it there (ClosureLowering's write site)");
            Assert.That(lambda.ReservedNames, Does.Contain(added), "…and the hoisted lambda, seeded from the creator as it stands, holds it");
        });
    }

    [Test]
    public void EveryNameClosureLoweringDeclares_IsReservedWhereItIsDeclared()
    {
        var lowered = ClosureLowering.Run(TempIr.Build(ReservationPrograms.AllKinds));
        var added = IRTempNames.AllFunctions(lowered)
            .SelectMany(f => f.LocalVariables.Select(l => l.Name))
            .Where(n => n.StartsWith("__closure_env", StringComparison.Ordinal) || n.StartsWith("__carry_", StringComparison.Ordinal))
            .ToList();
        var missing = IRTempNames.AllFunctions(lowered)
            .SelectMany(f => f.LocalVariables.Where(l => l.Name.StartsWith("__closure_env", StringComparison.Ordinal)
                                                        || l.Name.StartsWith("__carry_", StringComparison.Ordinal))
                .Where(l => !f.ReservedNames.Contains(l.Name)).Select(l => $"{f.Name}: {l.Name}"))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.Not.Empty);
            Assert.That(missing, Is.Empty, "every environment local and carrier ClosureLowering declared is reserved where it is declared");
            Assert.That(IRVerifier.CheckInvariantR(lowered), Is.Empty);
        });
    }

    // ============================================================================================
    // Invariant R — the reservation is complete
    // ============================================================================================

    private static (IRModule Module, IRFunction Function, BasicBlock Entry) Hand(bool tracks = true)
    {
        var module = new IRModule("M");
        var function = new IRFunction("Run", Int) { TracksReservedNames = tracks };
        module.Functions.Add(function);
        return (module, function, function.CreateBlock("entry"));
    }

    private static TestCaseData Construct(string id, string kind, Func<IRFunction, BasicBlock, string[]> build)
        => new TestCaseData(kind, build).SetName(id);

    private static void Switch(IRFunction f, BasicBlock e, params IRPatternCase[] cases)
    {
        var sw = new IRSwitch(new IRVariable("o", Int), f.CreateBlock("default"));
        sw.PatternCases.AddRange(cases);
        e.AddInstruction(sw);
    }

    private static IRTypePatternCase Bind(IRFunction f, string binding)
        => new("Integer", f.CreateBlock("case_" + binding)) { BindingVariable = binding };

    private static IEnumerable<TestCaseData> DeclaringConstructs()
    {
        yield return Construct("Parameter", "a parameter", (f, e) =>
        {
            f.Parameters.Add(new IRVariable("hp", Int) { IsParameter = true });
            return new[] { "hp" };
        });
        yield return Construct("Local", "a local (LocalVariables)", (f, e) =>
        {
            f.LocalVariables.Add(new IRVariable("hl", Int));
            return new[] { "hl" };
        });
        yield return Construct("ForEach", "a For Each control variable", (f, e) =>
        {
            e.AddInstruction(new IRForEach("hfe", Int, new IRVariable("lst", Int), f.CreateBlock("body"), f.CreateBlock("end")));
            return new[] { "hfe" };
        });
        yield return Construct("Catch", "a Catch variable", (f, e) =>
        {
            var clauses = new List<IRCatchClause> { new(ExType, "hc1", f.CreateBlock("catch1")), new(ExType, "hc2", f.CreateBlock("catch2")) };
            e.AddInstruction(new IRTryCatch(f.CreateBlock("try"), clauses, null, f.CreateBlock("end")));
            return new[] { "hc1", "hc2" };
        });
        yield return Construct("Pattern", "a pattern binding", (f, e) =>
        {
            Switch(f, e, Bind(f, "hpb"));
            return new[] { "hpb" };
        });
        yield return Construct("Pattern_inAnOr", "a pattern binding", (f, e) =>
        {
            var or = new IROrPatternCase(f.CreateBlock("or"));
            or.Alternatives.Add(Bind(f, "ha1"));
            or.Alternatives.Add(Bind(f, "ha2"));
            Switch(f, e, or);
            return new[] { "ha1", "ha2" };
        });
        yield return Construct("Pattern_inATuple", "a pattern binding", (f, e) =>
        {
            var tuple = new IRTuplePatternCase(f.CreateBlock("tuple"));
            tuple.Elements.Add(Bind(f, "ht1"));
            tuple.Elements.Add(Bind(f, "ht2"));
            Switch(f, e, tuple);
            return new[] { "ht1", "ht2" };
        });
        yield return Construct("Pattern_inAnOrInsideATuple", "a pattern binding", (f, e) =>
        {
            var or = new IROrPatternCase(f.CreateBlock("or"));
            or.Alternatives.Add(Bind(f, "hn1"));
            or.Alternatives.Add(Bind(f, "hn2"));
            var tuple = new IRTuplePatternCase(f.CreateBlock("tuple"));
            tuple.Elements.Add(or);
            tuple.Elements.Add(Bind(f, "hn3"));
            Switch(f, e, Bind(f, "hn0"), tuple);
            return new[] { "hn0", "hn1", "hn2", "hn3" };
        });
    }

    /// <summary>
    /// ⭐ Invariant R, the NEGATIVE for each kind it checks: a declaration that bypassed the reservation is named — its function, its
    /// spelling and the kind of construct that declares it — and nothing else is. A parameter, a local, a For Each variable, a Catch
    /// variable, a pattern binding (plain, in an Or, in a tuple, and an Or inside a tuple).
    /// </summary>
    [TestCaseSource(nameof(DeclaringConstructs))]
    public void InvariantR_NamesAnUnreservedDeclaration(string kind, Func<IRFunction, BasicBlock, string[]> build)
    {
        var (module, function, entry) = Hand();
        var names = build(function, entry);
        entry.AddInstruction(new IRReturn());

        var violations = IRVerifier.CheckInvariantR(module);

        Assert.Multiple(() =>
        {
            Assert.That(violations.Select(v => v.Variable), Is.EquivalentTo(names), string.Join(" | ", violations));
            Assert.That(violations.Select(v => v.Invariant).Distinct(), Is.EqualTo(new[] { "R" }));
            Assert.That(violations.Select(v => v.Function).Distinct(), Is.EqualTo(new[] { "Run" }));
            Assert.That(violations.Select(v => v.UseBlock).Distinct(), Is.EqualTo(new[] { kind }), "the message names the construct");
        });
    }

    /// <summary>The POSITIVE: the same construct with its names reserved is clean. A negative that fires for every function would
    /// pass the row above alone.</summary>
    [TestCaseSource(nameof(DeclaringConstructs))]
    public void InvariantR_IsSilent_WhenTheDeclarationIsReserved(string kind, Func<IRFunction, BasicBlock, string[]> build)
    {
        var (module, function, entry) = Hand();
        foreach (var name in build(function, entry)) function.Reserve(name);
        entry.AddInstruction(new IRReturn());

        Assert.That(IRVerifier.CheckInvariantR(module), Is.Empty, kind);
    }

    /// <summary>The scope of R (E2.2): hand-built IR tracks nothing and is not checked. This is exactly why the flag on every
    /// function IRBuilder and ClosureLowering create is pinned above.</summary>
    [TestCaseSource(nameof(DeclaringConstructs))]
    public void InvariantR_DoesNotApplyToAFunctionThatDoesNotTrackReservations(string kind, Func<IRFunction, BasicBlock, string[]> build)
    {
        var (module, function, entry) = Hand(tracks: false);
        build(function, entry);
        entry.AddInstruction(new IRReturn());

        Assert.That(IRVerifier.CheckInvariantR(module), Is.Empty, kind);
    }

    private static IEnumerable<TestCaseData> RealKinds() => new[]
    {
        new TestCaseData("Run", new[] { "lst" }, "a parameter").SetName("Parameter"),
        new TestCaseData("Run", new[] { "bx" }, "a local (LocalVariables)").SetName("Local"),
        new TestCaseData("Run", new[] { "fe" }, "a For Each control variable").SetName("ForEach"),
        new TestCaseData("Run", new[] { "__foreach_0" }, "a For Each control variable").SetName("ForEach_hidden"),
        new TestCaseData("Run", new[] { "e1" }, "a Catch variable").SetName("Catch"),
        new TestCaseData("Run", new[] { "n", "s" }, "a pattern binding").SetName("Pattern_OrAlternatives"),
        new TestCaseData("lambda:lp", new[] { "lp" }, "a parameter").SetName("LambdaParameter"),
    };

    /// <summary>The verifier sees every kind in REAL IR too: un-reserve one name of a real program and Invariant R names it, and only
    /// it. (The positive, the whole program clean, is <see cref="TheAllKindsProgram_IsCleanUnderBothInvariants"/>.)</summary>
    [TestCaseSource(nameof(RealKinds))]
    public void InvariantR_NamesARealDeclaration_WhoseReservationIsRemoved(string holder, string[] names, string kind)
    {
        var module = TempIr.Build(ReservationPrograms.AllKinds);
        var function = Holder(module, holder);
        foreach (var name in names) Assert.That(function.ReservedNames.Remove(name), Is.True, $"precondition: '{name}' was reserved");

        var violations = IRVerifier.CheckInvariantR(module);

        Assert.Multiple(() =>
        {
            Assert.That(violations.Select(v => v.Variable), Is.EquivalentTo(names), string.Join(" | ", violations));
            Assert.That(violations.Select(v => v.Function).Distinct(), Is.EqualTo(new[] { function.Name }));
            Assert.That(violations.Select(v => v.UseBlock).Distinct(), Is.EqualTo(new[] { kind }));
        });
    }

    [Test]
    public void TheAllKindsProgram_IsCleanUnderBothInvariants_BeforeAndAfterClosureLowering()
    {
        var module = TempIr.Build(ReservationPrograms.AllKinds);
        var lowered = ClosureLowering.Run(module);

        Assert.Multiple(() =>
        {
            Assert.That(IRVerifier.CheckInvariantR(module), Is.Empty, "R on the IRBuilder's module");
            Assert.That(IRVerifier.CheckInvariantT(module), Is.Empty, "T on the IRBuilder's module");
            Assert.That(IRVerifier.CheckInvariantR(lowered), Is.Empty, "R on ClosureLowering's");
            Assert.That(IRVerifier.CheckInvariantT(lowered), Is.Empty, "T on ClosureLowering's");
        });
    }

    // ---- the exemption is EXACT: a minted, flagged temp, and nothing broader -----------------------

    [Test]
    public void AMintedFlaggedTemp_InLocalVariables_IsExemptFromR_AndStaysOutOfTheReservation()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        var temp = function.DeclareTemp(Int);

        Assert.Multiple(() =>
        {
            Assert.That(function.LocalVariables, Does.Contain(temp));
            Assert.That(function.ReservedNames, Does.Not.Contain(temp.Name), "minted and reserved names stay disjoint (D2)");
            Assert.That(IRVerifier.CheckInvariantR(module), Is.Empty, "the one exemption");
        });
    }

    [Test]
    public void AFlaggedLocal_ThatTheFunctionNeverMinted_IsNotExempt()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        function.LocalVariables.Add(new IRVariable("t99", Int) { IsCompilerTemp = true });

        var violations = IRVerifier.CheckInvariantR(module);

        Assert.That(violations.Select(v => v.Variable), Is.EqualTo(new[] { "t99" }), "flagged alone is not the exemption: the name must be one this function minted");
    }

    [Test]
    public void AMintedName_ThatIsNotFlagged_IsNotExempt()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        var name = function.GetNextTempName();
        function.LocalVariables.Add(new IRVariable(name, Int));

        var violations = IRVerifier.CheckInvariantR(module);

        Assert.That(violations.Select(v => v.Variable), Is.EqualTo(new[] { name }), "a minted name alone is not the exemption: the variable must be flagged");
    }

    [Test]
    public void AnUnflaggedLocalSpelledLikeAMintedName_IsNotExempt()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        function.LocalVariables.Add(new IRVariable("t0", Int));   // a user's `Dim t0`, never minted, never flagged

        var violations = IRVerifier.CheckInvariantR(module);

        Assert.That(violations.Select(v => v.Variable), Is.EqualTo(new[] { "t0" }),
            "the exemption is by provenance, never by the shape `t<digits>`: this is user storage that bypassed the reservation");
    }

    [Test]
    public void ATempMintedByAnotherFunction_IsNotExemptHere()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        var other = new IRFunction("Other", Int);
        var stranger = other.DeclareTemp(Int);
        function.LocalVariables.Add(stranger);   // flagged, and a name `other` minted — not this function

        Assert.That(IRVerifier.CheckInvariantR(module).Select(v => v.Variable), Is.EqualTo(new[] { stranger.Name }),
            "the minted record is per function: a temp another function minted is not this function's to exempt");
    }

    // ============================================================================================
    // Invariant T — a compiler temp never carries a name the program owns
    // ============================================================================================

    /// <summary>A For Each variable spelled `T9`, and a binary op (`a + 1`) in its body: the op is the value the leak tests flag.</summary>
    private const string LeakProgram = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer), a As Integer)
            For Each T9 As Integer In lst
                Show(a + 1)
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            Run(lst, 7)
        End Sub
        """;

    /// <summary>
    /// ⭐ P4, and the mutant "the D4 refusal dropped" (which every program still printed VB's answer under: with the reservation total
    /// there is no witness left). A compiler-flagged value under the name of a variable the function reserves (a For Each `T9`;
    /// `t9` and `T9` are the same name) is refused, through the shipped verifier entry point, naming Invariant T.
    /// </summary>
    [Test]
    [NonParallelizable] // IRVerifier.Mode is process-wide
    public void ACompilerTempUnderAReservedName_IsRefused_ByVerifyAfterOptimization()
    {
        var module = TempIr.Build(LeakProgram);
        var run = Fn(module, "Run");
        var leak = run.Blocks.SelectMany(b => b.Instructions).OfType<IRBinaryOp>().First();   // `a + 1`, a minted temp
        Assert.That(run.ReservedNames, Does.Contain("T9"), "precondition: the For Each variable is reserved");
        leak.IsCompilerTemp = true;
        leak.Name = "t9";

        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            var ex = Assert.Throws<IRVerificationException>(() => IRVerifier.VerifyAfterOptimization(module));
            Assert.Multiple(() =>
            {
                Assert.That(ex.Violations.Select(v => v.Invariant), Does.Contain("T"));
                Assert.That(ex.Violations.Where(v => v.Invariant == "T").Select(v => v.Variable), Is.EqualTo(new[] { "t9" }));
                Assert.That(ex.Message, Does.Contain("Invariant T"));
            });
        }
        finally { IRVerifier.Mode = previous; }
    }

    [Test]
    [NonParallelizable]
    public void TheSameProgram_WithNoLeak_PassesTheShippedVerifier()
    {
        var module = TempIr.Build(LeakProgram);
        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try { Assert.DoesNotThrow(() => IRVerifier.VerifyAfterOptimization(module)); }
        finally { IRVerifier.Mode = previous; }
    }

    [Test]
    public void InvariantT_ChecksAModuleLevelName_Too_E3()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        function.ModuleReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "t3" };   // a module function `t3()`
        function.LocalVariables.Add(new IRVariable("T3", Int) { IsCompilerTemp = true });

        var violations = IRVerifier.CheckInvariantT(module);

        Assert.That(violations.Select(v => (v.Invariant, v.Variable, v.WriterBlock)), Is.EqualTo(new[] { ("T", "T3", "LocalVariables") }),
            "a name only the MODULE reserves is refused too (the function's own set is empty): E3");
    }

    [Test]
    public void InvariantT_ReachesAValueInsideAnOperandTree()
    {
        var (module, function, entry) = Hand();
        var inner = new IRBinaryOp("t5", BinaryOpKind.Add, new IRVariable("a", Int), new IRConstant(1, Int), Int) { IsCompilerTemp = true };
        entry.AddInstruction(new IRReturn(new IRBinaryOp("s", BinaryOpKind.Add, inner, new IRConstant(2, Int), Int)));
        function.Reserve("t5");

        Assert.That(IRVerifier.CheckInvariantT(module).Select(v => v.Variable), Is.EqualTo(new[] { "t5" }),
            "the walk descends operand trees: a value that lives in no block is still reachable");
    }

    [Test]
    public void InvariantT_IsAboutProvenance_NotSpelling_AUserDimNamedT5IsFine()
    {
        var module = TempIr.Build("""
            Function F(a As Integer, b As Integer) As Integer
                Dim t5 As Integer = a + b
                Return t5
            End Function

            Sub Main()
                Console.WriteLine(CStr(F(1, 2)))
            End Sub
            """);
        var f = Fn(module, "F");

        Assert.Multiple(() =>
        {
            Assert.That(f.ReservedNames, Does.Contain("t5"), "precondition: the user's `t5` is reserved");
            Assert.That(f.LocalVariables.Any(l => l.Name == "t5" && !l.IsCompilerTemp), Is.True, "and it is unflagged user storage");
            Assert.That(TempIr.Reachable(f).Where(v => v.Name == "t5").All(v => !v.IsCompilerTemp), Is.True, "no value under it carries the marker");
            Assert.That(IRVerifier.CheckInvariantT(module), Is.Empty);
        });
    }

    // ============================================================================================
    // The minter skips what the program owns, ignoring case (D2, D3)
    // ============================================================================================

    [TestCase("T0", TestName = "AUserT0_BlocksTheMintedt0")]
    [TestCase("t0", TestName = "AUserLowerCaseT0_BlocksTheMintedt0")]
    [TestCase("T3", TestName = "AUserT3_BlocksTheMintedt3")]
    public void AReservedName_IsNeverMinted_WhateverItsCase(string reserved)
    {
        var function = new IRFunction("Run", Int);
        function.Reserve(reserved);

        var minted = Enumerable.Range(0, 6).Select(_ => function.GetNextTempName()).ToList();
        var declared = Enumerable.Range(0, 6).Select(_ => function.DeclareTemp(Int).Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(function.IsReserved(reserved.ToLowerInvariant()), Is.True);
            Assert.That(function.IsReserved(reserved.ToUpperInvariant()), Is.True);
            Assert.That(minted.Concat(declared).Where(n => string.Equals(n, reserved, StringComparison.OrdinalIgnoreCase)), Is.Empty,
                "neither GetNextTempName nor DeclareTemp hands out a reserved name in either case");
            Assert.That(minted.Concat(declared).Distinct().Count(), Is.EqualTo(12), "and every minted name is fresh");
        });
    }

    [Test]
    public void AModuleLevelName_IsNeverMinted_WhateverItsCase()
    {
        var function = new IRFunction("Run", Int);
        function.ModuleReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T1", "t2" };

        var declared = Enumerable.Range(0, 6).Select(_ => function.DeclareTemp(Int).Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(function.ReservedNames, Is.Empty, "the function itself reserves nothing");
            Assert.That(declared, Is.EqualTo(new[] { "t0", "t3", "t4", "t5", "t6", "t7" }), "t1 and t2 belong to the module");
            Assert.That(declared.All(n => !function.IsReserved(n)), Is.True, "D2's post-condition");
        });
    }

    [Test]
    public void DeclareTemp_Postcondition_HoldsForEveryTemp()
    {
        var function = new IRFunction("Run", Int);
        function.Reserve("t1");
        var temps = Enumerable.Range(0, 4).Select(_ => function.DeclareTemp(Int)).ToList();

        Assert.Multiple(() =>
        {
            foreach (var v in temps)
            {
                Assert.That(v.IsCompilerTemp, Is.True, v.Name);
                Assert.That(function.LocalVariables, Does.Contain(v), v.Name);
                Assert.That(function.IsMintedTempName(v.Name), Is.True, v.Name);
                Assert.That(function.IsReserved(v.Name), Is.False, v.Name);
            }
        });
    }

    /// <summary>A clone made by ClosureLowering keeps the record of what its original minted, so a temp a pass declared before the
    /// lowering is still a minted temp afterwards (MEASURED on MSIL: Invariant R fired without it). And the clone's counter does not
    /// restart at 0, or a temp minted after lowering would reuse a name.</summary>
    [Test]
    public void AClone_KeepsTheMintedRecord_AndTheCounter()
    {
        var module = TempIr.Build(TempProbes.LcT0.Source);
        var lambda = Lambdas(module).Single();
        var temp = lambda.DeclareTemp(Int);
        var lowered = ClosureLowering.Run(module);
        var hoisted = Lambdas(lowered).Single();
        var next = hoisted.DeclareTemp(Int);

        Assert.Multiple(() =>
        {
            Assert.That(hoisted.IsMintedTempName(temp.Name), Is.True, "the clone knows what its original minted");
            Assert.That(next.Name, Is.Not.EqualTo(temp.Name), "and never mints it again");
            Assert.That(IRVerifier.CheckInvariantR(lowered), Is.Empty, "so Invariant R exempts the declared temp on the lowered module");
        });
    }

    // ============================================================================================
    // UserOwned reads the UNION, so a declaration that bypassed reservation still cannot collide
    // ============================================================================================

    private static IEnumerable<TestCaseData> WhereAnOwnedNameCanLive()
    {
        yield return new TestCaseData((Action<IRModule, IRFunction>)((m, f) => f.Parameters.Add(new IRVariable("T3", Int) { IsParameter = true }))).SetName("Parameter");
        yield return new TestCaseData((Action<IRModule, IRFunction>)((m, f) => f.LocalVariables.Add(new IRVariable("T3", Int)))).SetName("Local");
        yield return new TestCaseData((Action<IRModule, IRFunction>)((m, f) => f.Reserve("T3"))).SetName("ReservedNamesAlone");
        yield return new TestCaseData((Action<IRModule, IRFunction>)((m, f) => m.GlobalVariables["T3"] = new IRVariable("T3", Int))).SetName("Global");
        yield return new TestCaseData((Action<IRModule, IRFunction>)((m, f) => m.Functions.Add(new IRFunction("T3", Int)))).SetName("FunctionName");
    }

    [TestCaseSource(nameof(WhereAnOwnedNameCanLive))]
    public void UserOwned_ReadsTheUnion_OfEveryPlaceAProgramOwnsAName(Action<IRModule, IRFunction> declare)
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        declare(module, function);

        var owned = IRTempNames.UserOwned(module);
        Assert.That(owned.Contains("t3") && owned.Contains("T3"), Is.True, "found by any spelling: a reservation-less declaration cannot collide");
    }

    [Test]
    public void UserOwned_FiltersByShape_ItIsTheOnlyReaderThatDoes()
    {
        var (module, function, entry) = Hand();
        entry.AddInstruction(new IRReturn());
        function.Reserve("x");          // reserved, not temp-shaped
        function.Reserve("t7");         // reserved and temp-shaped

        Assert.Multiple(() =>
        {
            Assert.That(IRTempNames.UserOwned(module), Is.EquivalentTo(new[] { "t7" }), "shape is the reader's concern");
            Assert.That(function.ReservedNames, Is.EquivalentTo(new[] { "x", "t7" }), "ReservedNames holds everything the program owns, whatever its shape");
        });
    }

    // ============================================================================================
    // E3 — module-level names reach every function's skip set
    // ============================================================================================

    private const string ModuleNames = """
        Dim T6 As Integer = 1

        Class C
            Public t7 As Integer
            Public Property T8 As Integer
            Public Sub t9()
                Me.t7 = 1
            End Sub
        End Class

        Function t4() As Integer
            Return 4
        End Function

        Sub Run()
            Dim c As New C()
            c.t9()
            Console.WriteLine(CStr(t4() + T6 + c.t7))
        End Sub

        Sub Main()
            Run()
        End Sub
        """;

    /// <summary>
    /// ⭐ E3. A global, a class field, a property, a method and a function, each spelled like a temp, are module-level names every
    /// function's minter skips. `Run` does not declare any of them: it CALLS `t4()` and reads `T6`, and a local `t4` would hide the
    /// function (C#: "method name expected"; C++: the local shadows it). <b>Kills "module names not published".</b>
    /// </summary>
    [TestCase("t4"), TestCase("T6"), TestCase("t7"), TestCase("T8"), TestCase("t9")]
    public void AModuleLevelName_IsSkippedByEveryFunctionsMinter(string moduleName)
    {
        var module = TempIr.Build(ModuleNames);
        var run = Fn(module, "Run");
        var minted = Enumerable.Range(0, 12).Select(_ => run.DeclareTemp(Int).Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(run.ReservedNames, Does.Not.Contain(moduleName), "Run does not declare it: only the module reserves it");
            Assert.That(run.IsReserved(moduleName), Is.True);
            Assert.That(run.IsReserved(moduleName.ToUpperInvariant()) && run.IsReserved(moduleName.ToLowerInvariant()), Is.True);
            Assert.That(minted.Where(n => string.Equals(n, moduleName, StringComparison.OrdinalIgnoreCase)), Is.Empty,
                $"twelve temps in a row, and none is the module's `{moduleName}`");
            Assert.That(IRTempNames.UserOwned(module).Contains(moduleName), Is.True);
        });
    }

    [Test]
    public void EveryFunction_SeesTheSameModuleLevelNames()
    {
        var module = TempIr.Build(ModuleNames);
        var everyone = IRTempNames.AllFunctions(module);

        Assert.Multiple(() =>
        {
            Assert.That(everyone.Count, Is.GreaterThanOrEqualTo(4));
            foreach (var f in everyone)
                Assert.That(f.ModuleReservedNames, Is.SupersetOf(new[] { "t4", "T6", "t7", "T8", "t9", "Run", "Main" }), f.Name);
        });
    }

    // ---- multi-file: CombineIRModules -------------------------------------------------------------

    /// <summary>
    /// ⭐ E3 across files. Each unit is built alone and can publish only ITS module-level names, so a function of file B has never
    /// heard of file A's `t3()`; the optimizer runs on the COMBINED module, and <c>CombineIRModules</c> publishes again there. A pass
    /// minting in B's `Main` must therefore skip A's `t3`, in the CLI's project route (and, because the standard and aggressive
    /// pipelines both run on the combined module, in both). <b>Kills "CombineIRModules does not publish".</b> The counter is driven
    /// past 3 with twelve temps, so the skip is proven, not assumed.
    /// </summary>
    [TestCase(false), TestCase(true)]
    public void AModuleFunctionInAnotherFile_IsSkipped_ByAFunctionInThisOne(bool aggressive)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t121-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "A.bas");
            var b = Path.Combine(dir, "B.bas");
            File.WriteAllText(a, "Public Function t3() As Integer\n    Return 42\nEnd Function\n");
            File.WriteAllText(b, "Sub Main()\n    Dim r As Integer = t3()\n    Console.WriteLine(CStr(r))\nEnd Sub\n");

            var result = new BasicCompiler(new CompilerOptions { OptimizeAggressive = aggressive }).CompileProjectFiles(new List<string> { a, b });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            var combined = result.CombinedIR;
            var main = Fn(combined, "Main");
            var minted = Enumerable.Range(0, 12).Select(_ => main.DeclareTemp(Int).Name).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(IRTempNames.AllFunctions(combined).Select(f => f.Name), Is.SupersetOf(new[] { "t3", "Main" }), "both units are in the combined module");
                Assert.That(main.ReservedNames, Does.Not.Contain("t3"), "B's Main declares no `t3`: only the OTHER unit's function is called");
                foreach (var f in IRTempNames.AllFunctions(combined))
                    Assert.That(f.ModuleReservedNames, Is.SupersetOf(new[] { "t3", "Main" }), $"{f.Name} sees both units' names");
                Assert.That(minted, Does.Not.Contain("t3"), "twelve temps in a row, and none is A's `t3`");
            });
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }
}
