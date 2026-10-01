using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Msil;

// =====================================================================================
//  #140 / ADR-0010 D1 as amended by ADR-0019 — ClosureLowering's contract with its backends:
//  one options record, one result record, a per-ROOT skip that is atomic, and patch 01's
//  "a function is lowered once". Every test here is IR-level and in-process: no ilasm, no clang,
//  no node, so the fixture runs in the fast subset (the MSIL EXECUTION of #241's shapes is in
//  ClosureLoweringNestedCreatorExecutionTests below).
// =====================================================================================

internal static class ClosureLoweringContractPrograms
{
    /// <summary>ADR-0016 D3's AT1: ONE root (<c>Derived.Tag</c>) with TWO lambdas — the first can be
    /// lowered, the second reads <c>MyBase.Tag()</c>, a D9 refusal. vbc prints 16 (10 + 6).</summary>
    internal const string AT1 = """
        Class Base
            Public Overridable Function Tag() As Integer
                Return 1
            End Function
        End Class
        Class Derived
            Inherits Base
            Public Overrides Function Tag() As Integer
                Dim k As Integer = 5
                Dim f1 As Func(Of Integer) = Function() k * 2
                Dim f2 As Func(Of Integer) = Function() MyBase.Tag() + k
                Return f1() + f2()
            End Function
        End Class
        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.Tag())
        End Sub
        """;

    /// <summary>D07: a Select Case 'When' guard that reads a captured variable the lambda WRITES.
    /// Refused by ClosureLowering (D9) and by the by-copy rule (the lambda writes <c>lim</c>).</summary>
    internal const string D07 = """
        Sub Main()
            Dim lim As Integer = 5
            Dim f = Sub() lim = lim + 1
            f()
            Dim v As Integer = 7
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
        End Sub
        """;

    /// <summary>The exact text the lowering refuses D07 with, spelled by a backend named "MSIL". It is
    /// byte-identical to what the pass said before #140 moved the prefix into
    /// <c>ClosureLoweringOptions.BackendName</c> (measured against the pre-#140 compiler).</summary>
    internal const string D07Reason =
        "a Select Case 'When' guard in 'Main' reads a variable that a lambda captures (or a lambda itself). " +
        "A guard is rendered inline, where no closure-environment load can be placed; the first cut refuses the shape. " +
        "Compute the guard's value into a local before the Select Case.";

    /// <summary>Two roots: <c>Lowerable</c> has nothing the lowering refuses; <c>Refused</c> has a
    /// read-only guard (a D9 refusal the by-copy rule admits). The roots must not affect each other.</summary>
    internal const string TwoRoots = """
        Function Lowerable(k As Integer) As Integer
            Dim f As Func(Of Integer) = Function() k + 1
            Return f()
        End Function
        Function Refused(k As Integer) As Integer
            Dim g As Func(Of Integer) = Function() k * 2
            Select Case k
                Case Is > 0 When k > g()
                    Return 1
                Case Else
                    Return 0
            End Select
        End Function
        Sub Main()
            Console.WriteLine(Lowerable(1) + Refused(2))
        End Sub
        """;

    /// <summary>D10: a <c>Func</c> with 10 type arguments (9 parameters and the result) — one more than
    /// the .NET facade MSIL reaches. VB prints 18.</summary>
    internal const string D10 = """
        Sub Main()
            Dim g2 As Func(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Function(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer, j As Integer) a * j
            Console.WriteLine(g2(2, 2, 3, 4, 5, 6, 7, 8, 9))
        End Sub
        """;

    /// <summary>D11: an <c>Action</c> with 9 type arguments, one more than the facade reaches. VB prints 10.</summary>
    internal const string D11 = """
        Sub Main()
            Dim f As Action(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Sub(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer, j As Integer) Console.WriteLine(a + j)
            f(1, 2, 3, 4, 5, 6, 7, 8, 9)
        End Sub
        """;

    /// <summary>A <c>Func</c> with exactly 9 type arguments and an <c>Action</c> with exactly 8: the
    /// largest the MSIL caps (<see cref="ClosureLowering.MaxFuncTypeArguments"/> /
    /// <see cref="ClosureLowering.MaxActionTypeArguments"/>) admit.</summary>
    internal const string AtTheCaps = """
        Sub Main()
            Dim g As Func(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Function(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, f As Integer, h As Integer, i As Integer) a + i
            Dim s As Action(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Sub(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, f As Integer, h As Integer, i As Integer) Console.WriteLine(a + i)
            Console.WriteLine(g(1, 2, 3, 4, 5, 6, 7, 8))
            s(1, 2, 3, 4, 5, 6, 7, 8)
        End Sub
        """;

    // ---- #241's shapes: a creator lambda that sits AFTER its creator, in a class member ----------

    /// <summary>N3: nested lambdas in an INSTANCE method. vbc prints 403.</summary>
    internal const string N3 = """
        Class D
            Public K As Integer = 2
            Public Function Run(p As Integer) As Integer
                Dim n As Integer = p
                Dim outer As Action = Sub()
                                          Dim inner As Action = Sub() n = n + K * 100
                                          inner()
                                      End Sub
                outer()
                outer()
                Return n
            End Function
        End Class
        Sub Main()
            Dim d As New D()
            Console.WriteLine(d.Run(3))
        End Sub
        """;

    /// <summary>N4: nested lambdas in a CONSTRUCTOR. vbc prints 12.</summary>
    internal const string N4 = """
        Class D
            Public Total As Integer
            Public Sub New(p As Integer)
                Dim acc As Integer = p
                Dim outer As Func(Of Integer) = Function()
                                                    Dim inner As Func(Of Integer) = Function() acc + 1
                                                    acc = inner()
                                                    Return acc
                                                End Function
                outer()
                Total = outer()
            End Sub
        End Class
        Sub Main()
            Dim d As New D(10)
            Console.WriteLine(d.Total)
        End Sub
        """;

    /// <summary>N5: nested lambdas in a SHARED method. vbc prints 203.</summary>
    internal const string N5 = """
        Class D
            Public Shared Function Run(p As Integer) As Integer
                Dim n As Integer = p
                Dim outer As Action = Sub()
                                          Dim inner As Action = Sub() n = n + 100
                                          inner()
                                      End Sub
                outer()
                outer()
                Return n
            End Function
        End Class
        Sub Main()
            Console.WriteLine(D.Run(3))
        End Sub
        """;

    /// <summary>N6: nested lambdas in a property GETTER. vbc prints 8.</summary>
    internal const string N6 = """
        Class D
            Private _k As Integer = 4
            Public ReadOnly Property Twice As Integer
                Get
                    Dim f As Func(Of Integer) = Function()
                                                    Dim g As Func(Of Integer) = Function() _k * 2
                                                    Return g()
                                                End Function
                    Return f()
                End Get
            End Property
        End Class
        Sub Main()
            Dim d As New D()
            Console.WriteLine(d.Twice)
        End Sub
        """;

    /// <summary>R20: nested lambdas at MODULE level (the shape that always worked). vbc prints 3,103.</summary>
    internal const string R20 = """
        Sub Main()
            Dim n As Integer = 1
            Dim q As Integer = 2
            Dim outer = Sub()
                            Dim inner = Sub() n = n + 100
                            inner()
                        End Sub
            Dim a As Integer = n + q
            outer()
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;

    /// <summary>R21: a lambda nested two deep that ESCAPES its creator (module level). vbc prints 11, 21, 31.</summary>
    internal const string R21 = """
        Function MakeCounter(start As Integer) As Func(Of Func(Of Integer))
            Dim count As Integer = start
            Return Function()
                       Dim step1 As Integer = 10
                       Return Function()
                                  count = count + step1
                                  Return count
                              End Function
                   End Function
        End Function
        Sub Main()
            Dim mk As Func(Of Func(Of Integer)) = MakeCounter(1)
            Dim c1 As Func(Of Integer) = mk()
            Dim c2 As Func(Of Integer) = mk()
            Console.WriteLine(c1())
            Console.WriteLine(c2())
            Console.WriteLine(c1())
        End Sub
        """;
}

[TestFixture]
public class ClosureLoweringOptionsContractTests
{
    private static readonly ClosureLoweringOptions Cpp = new("C++", MaxActionArity: null, MaxFuncArity: null, UnloweredRootPolicy.Skip);

    private static IRModule Module(string source) => CppClosures.Optimized(source, CppEntry.Standard);

    private static string Print(IRFunction f)
    {
        var printer = new IRPrettyPrinter();
        f.Accept(printer);
        return printer.GetOutput();
    }

    private static List<IRClass> Environments(IRModule m) =>
        m.Classes.Values.Where(ClosureLowering.IsEnvironmentClass).ToList();

    private static IRFunction Function(IRModule m, string name) =>
        IRTempNames.AllFunctions(m).First(f => f.Name == name);

    // ---- ClosureLoweringOptions / ClosureLoweringResult / UnloweredRootPolicy -------------------------

    [Test]
    public void MsilOptions_AreTheMeasuredFacadeCaps_AndTheThrowPolicy()
    {
        var msil = ClosureLoweringOptions.Msil;
        Assert.Multiple(() =>
        {
            Assert.That(msil.BackendName, Is.EqualTo("MSIL"));
            Assert.That(msil.MaxActionArity, Is.EqualTo(8), "Action`9 is a TypeLoadException on .NET 8 (ADR-0010 D7, measured)");
            Assert.That(msil.MaxFuncArity, Is.EqualTo(9), "Func`10 is a TypeLoadException on .NET 8 (ADR-0010 D7, measured)");
            Assert.That(msil.Policy, Is.EqualTo(UnloweredRootPolicy.Throw));
            Assert.That(ClosureLowering.MaxActionTypeArguments, Is.EqualTo(8));
            Assert.That(ClosureLowering.MaxFuncTypeArguments, Is.EqualTo(9));
        });
    }

    /// <summary>The one-argument <c>Run</c> IS the MSIL options: the same lowered IR (and so the same IL)
    /// for every contract program, and the same refusal text for the ones MSIL refuses. The pre-#140 MSIL
    /// output is byte-identical to this by construction.</summary>
    [Test]
    public void RunWithoutOptions_IsRunWithTheMsilOptions_ForEveryContractProgram()
    {
        var programs = ClosureLoweringProbes.AllContractSources()
            .Concat(new[]
            {
                ("AT1", ClosureLoweringContractPrograms.AT1), ("D07", ClosureLoweringContractPrograms.D07),
                ("D10", ClosureLoweringContractPrograms.D10), ("D11", ClosureLoweringContractPrograms.D11),
                ("N3", ClosureLoweringContractPrograms.N3), ("N4", ClosureLoweringContractPrograms.N4),
                ("R21", ClosureLoweringContractPrograms.R21), ("TwoRoots", ClosureLoweringContractPrograms.TwoRoots),
            });
        Assert.Multiple(() =>
        {
            foreach (var (name, source) in programs)
            {
                static string Outcome(Func<IRModule> run)
                {
                    try { return new IRPrettyPrinter().Print(run()); }
                    catch (ForeignFeatureException ex) { return "REFUSED: " + ex.Message; }
                }
                var plain = Outcome(() => ClosureLowering.Run(Module(source)));
                var withOptions = Outcome(() =>
                {
                    var result = ClosureLowering.Run(Module(source), ClosureLoweringOptions.Msil);
                    Assert.That(result.SkippedRoots, Is.Empty, $"{name}: the Throw policy never skips a root");
                    return result.Module;
                });
                Assert.That(withOptions, Is.EqualTo(plain), $"{name}: Run(module) must equal Run(module, ClosureLoweringOptions.Msil)");
            }
        });
    }

    [Test]
    public void ANoLambdaModule_IsReturnedItself_UnderEitherPolicy_WithNothingSkipped()
    {
        var module = Module("Sub Main()\n    Console.WriteLine(1)\nEnd Sub\n");
        foreach (var options in new[] { ClosureLoweringOptions.Msil, Cpp })
        {
            var result = ClosureLowering.Run(module, options);
            Assert.That(result.Module, Is.SameAs(module), options.BackendName);
            Assert.That(result.SkippedRoots, Is.Empty, options.BackendName);
        }
    }

    // ---- The Throw policy (MSIL) ---------------------------------------------------------------------

    /// <summary>Throw-policy falsifier: MSIL on D07 still throws, and the text is the pre-#140 text — the
    /// prefix now comes from <c>BackendName</c> instead of a literal in the pass.</summary>
    [Test]
    public void Throw_OnD07_StillThrows_WithTheUnchangedText()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => ClosureLowering.Run(Module(ClosureLoweringContractPrograms.D07)));
        Assert.That(ex!.Message, Is.EqualTo("MSIL: " + ClosureLoweringContractPrograms.D07Reason));

        var viaOptions = Assert.Throws<ForeignFeatureException>(
            () => ClosureLowering.Run(Module(ClosureLoweringContractPrograms.D07), ClosureLoweringOptions.Msil));
        Assert.That(viaOptions!.Message, Is.EqualTo(ex.Message));

        // ... and through the real MSIL entry point, which is what every MSIL build reaches.
        var viaBackend = Assert.Throws<ForeignFeatureException>(() => MsilHarness.CompileToIl(ClosureLoweringContractPrograms.D07));
        Assert.That(viaBackend!.Message, Is.EqualTo(ex.Message));
    }

    /// <summary>The prefix is the OPTION, not a constant: "MSIL:" for MSIL, "C++:" for C++, and any other
    /// name a backend gives is what the text starts with.</summary>
    [Test]
    public void BackendName_PrefixesEveryRefusal()
    {
        var source = ClosureLoweringContractPrograms.D07;
        Assert.Multiple(() =>
        {
            var msil = Assert.Throws<ForeignFeatureException>(() => ClosureLowering.Run(Module(source), ClosureLoweringOptions.Msil));
            Assert.That(msil!.Message, Does.StartWith("MSIL: "));

            var cpp = Assert.Throws<ForeignFeatureException>(
                () => ClosureLowering.Run(Module(source), Cpp with { Policy = UnloweredRootPolicy.Throw }));
            Assert.That(cpp!.Message, Is.EqualTo("C++: " + ClosureLoweringContractPrograms.D07Reason));

            var other = Assert.Throws<ForeignFeatureException>(
                () => ClosureLowering.Run(Module(source), new ClosureLoweringOptions("Zed", null, null, UnloweredRootPolicy.Throw)));
            Assert.That(other!.Message, Is.EqualTo("Zed: " + ClosureLoweringContractPrograms.D07Reason));

            // Under Skip the same reason is carried in SkippedRoots, spelled by the same option.
            var skipped = ClosureLowering.Run(Module(source), new ClosureLoweringOptions("Zed", null, null, UnloweredRootPolicy.Skip));
            Assert.That(skipped.SkippedRoots.Single().Reason.Message, Is.EqualTo("Zed: " + ClosureLoweringContractPrograms.D07Reason));
        });
    }

    // ---- The Skip policy (C++) ------------------------------------------------------------------------

    [Test]
    public void Skip_OnD07_LeavesTheRootUnlowered_AndNamesItWithTheReason()
    {
        var input = Module(ClosureLoweringContractPrograms.D07);
        var before = new IRPrettyPrinter().Print(input);

        var result = ClosureLowering.Run(input, Cpp);

        Assert.Multiple(() =>
        {
            var skipped = result.SkippedRoots.Single();
            Assert.That(skipped.Root.Name, Is.EqualTo("Main"));
            Assert.That(skipped.Root, Is.Not.SameAs(Function(input, "Main")), "a root of the CLONE, not of the input");
            Assert.That(skipped.Reason.Message, Is.EqualTo("C++: " + ClosureLoweringContractPrograms.D07Reason));
            Assert.That(result.Module, Is.Not.SameAs(input), "a program with a lambda gets a clone");
            Assert.That(Environments(result.Module), Is.Empty, "nothing was lowered");
            Assert.That(new IRPrettyPrinter().Print(input), Is.EqualTo(before), "the input is never written");
            Assert.That(new IRPrettyPrinter().Print(result.Module), Is.EqualTo(before),
                "invariant (iii): a skipped root's IR in the clone is exactly the input's (here every root is skipped)");
        });
    }

    /// <summary>
    /// ⭐ ATOMICITY FALSIFIER (ruling D2 invariant (i), D5's AT1). ONE root with TWO lambdas: the first can
    /// be lowered, the second hits a D9 refusal (<c>MyBase.Tag()</c> in a lambda). A root is lowered
    /// ENTIRELY or NOT AT ALL, so under Skip the clone's root has ZERO environment classes, both lambdas are
    /// still lambdas, the root's IR prints exactly as the input's, and the root is named. A pass that lowered
    /// the first lambda before it met the second's refusal would leave an environment behind and a lambda
    /// reading a field that nothing creates. (vbc prints 16; the program runs on C++ — see
    /// <c>CppClosureRunTests</c>.)
    /// </summary>
    [Test]
    public void Skip_AT1_ARootWithOneLowerableAndOneRefusedLambda_IsLeftWhole()
    {
        var input = Module(ClosureLoweringContractPrograms.AT1);
        var lambdas = IRTempNames.AllFunctions(input).Where(f => f.IsLambda).Select(f => f.Name).OrderBy(n => n).ToList();
        Assert.That(lambdas, Has.Count.EqualTo(2), "AT1 has two lambdas");
        var printed = IRTempNames.AllFunctions(input).Select(Print).ToList();

        var result = ClosureLowering.Run(input, Cpp);

        Assert.Multiple(() =>
        {
            var root = result.SkippedRoots.Single().Root;
            Assert.That(root.Name, Is.EqualTo("Tag"), "the root is named");
            Assert.That(ClosureLowering.OwnerClassOf(result.Module, root, out _)?.Name, Is.EqualTo("Derived"), "Derived.Tag, not Base.Tag");
            Assert.That(Environments(result.Module), Is.Empty, "the clone's root has ZERO environment classes");
            var cloneLambdas = IRTempNames.AllFunctions(result.Module).Where(f => f.IsLambda).ToList();
            Assert.That(cloneLambdas.Select(f => f.Name).OrderBy(n => n), Is.EqualTo(lambdas), "both lambdas still IsLambda");
            Assert.That(IRTempNames.AllFunctions(result.Module).Select(Print).ToList(), Is.EqualTo(printed),
                "every function prints exactly as the input's: nothing of the root was rewritten");
            Assert.That(result.SkippedRoots.Single().Reason.Message, Does.StartWith("C++: ").And.Contain("MyBase"),
                "the reason is the second lambda's refusal");
        });
    }

    /// <summary>Roots are independent: one that cannot be lowered is skipped; the other is lowered. And the
    /// post-condition (ruling D2 invariant (ii)): after Run, no IsLambda function remains outside a skipped
    /// root.</summary>
    [Test]
    public void Skip_TwoRoots_LowersOneAndSkipsTheOther_AndNoLambdaRemainsOutsideASkippedRoot()
    {
        var input = Module(ClosureLoweringContractPrograms.TwoRoots);
        var result = ClosureLowering.Run(input, Cpp);

        Assert.Multiple(() =>
        {
            Assert.That(result.SkippedRoots.Select(s => s.Root.Name), Is.EqualTo(new[] { "Refused" }));
            Assert.That(Environments(result.Module), Has.Count.EqualTo(1), "Lowerable got its environment; Refused none");

            var creators = ClosureLowering.CreatorsOf(result.Module);
            var skippedRoots = result.SkippedRoots.Select(s => s.Root).ToList();
            foreach (var lambda in IRTempNames.AllFunctions(result.Module).Where(f => f.IsLambda))
                Assert.That(skippedRoots, Does.Contain(ClosureLowering.RootOf(lambda, creators)),
                    $"lambda '{lambda.Name}' remains, so its root must be a skipped one");

            Assert.That(result.SkippedRoots.Single().Root, Is.SameAs(Function(result.Module, "Refused")),
                "a skipped root is a function of the result's module");
        });
    }

    // ---- Invariant (iv): a lambda that creates lambdas is lowered ONCE (patch 01, #241) ---------------------

    private static IEnumerable<TestCaseData> NestedCreatorShapes()
    {
        yield return new TestCaseData(ClosureLoweringContractPrograms.N3).SetName("N3_instanceMethod");
        yield return new TestCaseData(ClosureLoweringContractPrograms.N4).SetName("N4_constructor");
        yield return new TestCaseData(ClosureLoweringContractPrograms.N5).SetName("N5_sharedMethod");
        yield return new TestCaseData(ClosureLoweringContractPrograms.N6).SetName("N6_propertyGetter");
        yield return new TestCaseData(ClosureLoweringContractPrograms.R20).SetName("R20_moduleLevel");
        yield return new TestCaseData(ClosureLoweringContractPrograms.R21).SetName("R21_moduleLevelEscaping");
    }

    /// <summary>
    /// A lambda that creates a lambda is itself a creator, placed AFTER its own creator in the module; the
    /// pass used to lower it a second time as a root, which emitted a dead DUPLICATE method on a SECOND,
    /// nested environment (<c>&lt;&gt;c__Env2</c> inside <c>&lt;&gt;c__Env0</c>) — ilasm failed on it
    /// ("undefined class ...", #241) and clang would reject the ill-typed copy. Two creators means exactly
    /// two environments, every lambda's method is on exactly ONE of them, and no environment is nested in
    /// another. Under both policies, because C++ inherited the bug.
    /// </summary>
    [TestCaseSource(nameof(NestedCreatorShapes))]
    public void ACreatorLambdaPlacedAfterItsCreator_IsLoweredOnce(string source)
    {
        foreach (var options in new[] { ClosureLoweringOptions.Msil, Cpp })
        {
            var lowered = ClosureLowering.Run(Module(source), options);
            var environments = Environments(lowered.Module);
            Assert.Multiple(() =>
            {
                Assert.That(lowered.SkippedRoots, Is.Empty, options.BackendName);
                Assert.That(environments, Has.Count.EqualTo(2),
                    $"{options.BackendName}: the root and its outer lambda are the two creators: {string.Join(", ", environments.Select(e => e.Name))}");
                var methods = environments.SelectMany(e => e.Methods).Select(m => m.Name).ToList();
                Assert.That(methods, Is.Unique, $"{options.BackendName}: a lambda's method is on exactly one environment");
                Assert.That(methods, Has.Count.EqualTo(2), $"{options.BackendName}: one method per lambda");
                foreach (var env in environments)
                    Assert.That(environments.Select(e => e.Name), Does.Not.Contain(env.EnclosingClass),
                        $"{options.BackendName}: environment '{env.Name}' must not be nested in another environment");
                Assert.That(lowered.Module.Functions.Any(f => f.IsLambda), Is.False, options.BackendName);
            });
        }
    }

    // ---- The arity options ---------------------------------------------------------------------------------

    /// <summary>D10/D11 on MSIL's caps: refused with the facade text. The caps are a .NET delegate-facade
    /// fact, so they are MSIL's OPTIONS, not the pass's.</summary>
    [TestCase(nameof(ClosureLoweringContractPrograms.D10), "'Func' with 10 type arguments is above the supported arity. Func`1..`9 are the ones")]
    [TestCase(nameof(ClosureLoweringContractPrograms.D11), "'Action' with 9 type arguments is above the supported arity. Action`1..`8 are the ones")]
    public void MsilCaps_RefuseTheArityJustAboveThem(string which, string text)
    {
        var source = which == nameof(ClosureLoweringContractPrograms.D10) ? ClosureLoweringContractPrograms.D10 : ClosureLoweringContractPrograms.D11;
        var ex = Assert.Throws<ForeignFeatureException>(() => ClosureLowering.Run(Module(source), ClosureLoweringOptions.Msil));
        Assert.That(ex!.Message, Does.StartWith("MSIL: " + text), ex.Message);

        var skipped = ClosureLowering.Run(Module(source), ClosureLoweringOptions.Msil with { Policy = UnloweredRootPolicy.Skip });
        Assert.That(skipped.SkippedRoots.Single().Root.Name, Is.EqualTo("Main"));
        Assert.That(skipped.SkippedRoots.Single().Reason.Message, Does.StartWith("MSIL: " + text));
    }

    /// <summary>The same two programs through the real MSIL entry point (the one every MSIL build reaches): refused with the
    /// facade text. On C++ they are lowered and run (<c>CppClosurePathTests</c>, <c>CppClosureRunTests</c>).</summary>
    [TestCase(nameof(ClosureLoweringContractPrograms.D10), "MSIL: 'Func' with 10 type arguments is above the supported arity.")]
    [TestCase(nameof(ClosureLoweringContractPrograms.D11), "MSIL: 'Action' with 9 type arguments is above the supported arity.")]
    public void TheMsilBackend_StillRefusesTheArityJustAboveItsCaps(string which, string text)
    {
        var source = which == nameof(ClosureLoweringContractPrograms.D10) ? ClosureLoweringContractPrograms.D10 : ClosureLoweringContractPrograms.D11;
        var ex = Assert.Throws<ForeignFeatureException>(() => MsilHarness.CompileToIl(source));
        Assert.That(ex!.Message, Does.StartWith(text), ex.Message);
    }

    /// <summary>A null cap is UNBOUNDED: D10 and D11 are lowered, and nothing is skipped. This is what makes C++
    /// lower them (ruling D1: H′ over H).</summary>
    [TestCase(nameof(ClosureLoweringContractPrograms.D10))]
    [TestCase(nameof(ClosureLoweringContractPrograms.D11))]
    public void NullCaps_AreUnbounded_SoTheArityJustAboveMsilsCapsIsLowered(string which)
    {
        var source = which == nameof(ClosureLoweringContractPrograms.D10) ? ClosureLoweringContractPrograms.D10 : ClosureLoweringContractPrograms.D11;
        foreach (var policy in new[] { UnloweredRootPolicy.Skip, UnloweredRootPolicy.Throw })
        {
            var result = ClosureLowering.Run(Module(source), Cpp with { Policy = policy });
            Assert.That(result.SkippedRoots, Is.Empty, policy.ToString());
            Assert.That(Environments(result.Module), Has.Count.EqualTo(1), policy.ToString());
            Assert.That(result.Module.Functions.Any(f => f.IsLambda), Is.False, policy.ToString());
        }
    }

    /// <summary>The two caps are independent and exact: MSIL's 9 / 8 admit a Func with 9 type arguments and an
    /// Action with 8; custom caps move the line.</summary>
    [Test]
    public void TheCaps_AreIndependentAndExact()
    {
        var atTheCaps = Module(ClosureLoweringContractPrograms.AtTheCaps);
        Assert.Multiple(() =>
        {
            Assert.That(ClosureLowering.Run(atTheCaps, ClosureLoweringOptions.Msil).SkippedRoots, Is.Empty);
            Assert.That(Environments(ClosureLowering.Run(atTheCaps, ClosureLoweringOptions.Msil).Module), Has.Count.EqualTo(1));

            // Func cap 8: the 9-type-argument Func is refused, the Action (8 <= 8) is not the reason.
            var funcCap = ClosureLowering.Run(atTheCaps, new ClosureLoweringOptions("T", 8, 8, UnloweredRootPolicy.Skip));
            Assert.That(funcCap.SkippedRoots.Single().Reason.Message, Does.StartWith("T: 'Func' with 9 type arguments"));

            // Action cap 7: the 8-type-argument Action is refused.
            var actionCap = ClosureLowering.Run(atTheCaps, new ClosureLoweringOptions("T", 7, 9, UnloweredRootPolicy.Skip));
            Assert.That(actionCap.SkippedRoots.Single().Reason.Message, Does.StartWith("T: 'Action' with 8 type arguments"));

            // One null cap does not unbound the other.
            var onlyActionUnbounded = ClosureLowering.Run(atTheCaps, new ClosureLoweringOptions("T", null, 8, UnloweredRootPolicy.Skip));
            Assert.That(onlyActionUnbounded.SkippedRoots.Single().Reason.Message, Does.StartWith("T: 'Func' with 9 type arguments"));
        });
    }
}

// =====================================================================================
//  #241 on the MSIL EXECUTION tier: the shapes that failed ilasm before patch 01 run with vbc's
//  own answer. [Category("Integration")]: assembles and runs IL.
// =====================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class ClosureLoweringNestedCreatorExecutionTests
{
    /// <summary>(name, program, vbc's output — S/t140/oracle).</summary>
    private static IEnumerable<TestCaseData> Shapes()
    {
        yield return new TestCaseData(ClosureLoweringContractPrograms.N3, "403").SetName("N3_instanceMethod");
        yield return new TestCaseData(ClosureLoweringContractPrograms.N4, "12").SetName("N4_constructor");
        yield return new TestCaseData(ClosureLoweringContractPrograms.N5, "203").SetName("N5_sharedMethod");
        yield return new TestCaseData(ClosureLoweringContractPrograms.N6, "8").SetName("N6_propertyGetter");
        yield return new TestCaseData(ClosureLoweringContractPrograms.R20, "3,103").SetName("R20_moduleLevel_alwaysWorked");
        yield return new TestCaseData(ClosureLoweringContractPrograms.R21, "11\n21\n31").SetName("R21_moduleLevelEscaping_alwaysWorked");
    }

    /// <summary>N3-N6 failed <c>ilasm</c> ("undefined class 'D/&lt;&gt;c__Env0/&lt;&gt;c__Env2'") before the
    /// first commit of #140 — the dead duplicate method on the doubly lowered nested lambda — and print vbc's
    /// answer now, standard and aggressive pipelines. X22, X25 and E01 (the three base-constructor shapes the
    /// same fault produced) are in <c>BaseConstructorCallDiagnosticsTests</c> and
    /// <c>BaseConstructorCallLoweringExecutionTests</c>.</summary>
    [TestCaseSource(nameof(Shapes))]
    public void NestedCreatorLambdas_RunOnMsil_WithVbcsAnswer(string source, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, aggressive");
        });
    }
}
