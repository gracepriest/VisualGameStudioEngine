using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #144 — the SHAPE of the C# a ByRef constructor parameter is emitted as. Nothing runs here and nothing spawns: the emitted text is read and handed to Roslyn to COMPILE (never to run).
//  This is the fast subset's half of the fix (the RUN half is ConstructorByRefExecutionTests, Integration) and the cheap kill of all three mutants:
//
//    F1  a copy-in temp is an EXPRESSION, `ref (new T[] { v })[0]`, in a `New` argument list and in `: base(...)`, in source order     kills M3 (the temp as a statement) and M2
//    F2  a variable argument is passed `ref`, and the constructor's own parameter is declared `ref`                                    kills M1 (constructor parameters lose IsByRef: CS1615)
//    F3  a literal / expression / Const argument gets a carrier; a by-value constructor gets no `ref` at all                          kills M2 (no copy-in: CS1510) and M3
//
//  Each is read through the standard pipeline, the aggressive pipeline (what `--optimize` and a Release build run) and `CompileProjectFiles` (the IDE's build); the spawned CLI is the run fixture's.
// ================================================================================================

[TestFixture]
public class ConstructorByRefShapeTests
{
    private static string Row(string id) => ConstructorByRefExecutionTests.VariableRows.Concat(ConstructorByRefExecutionTests.CopyInRows).Single(p => p.Id == id).Source;

    private static string EmitPipeline(string source, bool aggressive)
    {
        var module = CSharpTestSupport.BuildModule(source, "Prog.bas");
        if (aggressive)
        {
            AggressivePipeline.Apply(module);
        }
        else
        {
            var passes = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            passes.AddStandardPasses();
            passes.Run(module);
        }

        return new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(module).Replace("\r\n", "\n");
    }

    private static IEnumerable<(string Name, string Text)> Emits(string source)
    {
        yield return ("standard", EmitPipeline(source, aggressive: false));
        yield return ("aggressive", EmitPipeline(source, aggressive: true));
        yield return ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n"));
    }

    private static void AssertCompiles(string name, string id, string text)
    {
        var errors = MyBaseMethodCallStatementShapeTests.RoslynErrors(text);
        Assert.That(errors, Is.Empty, $"{id} {name}: Roslyn finds {string.Join(" | ", errors)}\n{text}");
    }

    /// <summary>
    /// F1 — the copy-in temp is written IN PLACE as an expression. `MyBase.New(7)` is `: base(ref (new int[] { 7 })[0])` (C# admits no statement before `: base(...)`), and `New Pair(Seed(1), Seed(2))`
    /// is `new Pair(Seed(1), ref (new int[] { Seed(2) })[0])` — Seed(1) before Seed(2), because the carrier sits where the argument sits. M3 writes the temp as a statement: the base call is refused
    /// ("needs a statement before the base call") and the other runs Seed(2) first.
    /// </summary>
    [Test]
    public void ACopyInTemp_IsAnExpression_InPlace()
    {
        foreach (var (name, text) in Emits(Row("n10_mybase_literal")))
        {
            Assert.That(text, Does.Contain(": base(ref (new int[] { 7 })[0])"), $"n10 {name}:\n{text}");
            AssertCompiles(name, "n10", text);
        }

        foreach (var (name, text) in Emits(Row("n13_eval_order")))
        {
            Assert.That(text, Does.Contain("new Pair(Seed(1), ref (new int[] { Seed(2) })[0])"), $"n13 {name}:\n{text}");
            Assert.That(text, Does.Contain("new Pair(Seed(3), ref (new int[] { Seed(4) * 10 })[0])"), $"n13 {name}:\n{text}");
            AssertCompiles(name, "n13", text);
        }
    }

    /// <summary>
    /// F2 — a variable passed to a ByRef constructor parameter is passed `ref`, and the parameter is DECLARED `ref`: `public Box(ref int n)` / `new Box(ref p)`, and through two constructors
    /// `public Derived(ref int m)` / `: base(ref m)`. M1 (the constructor's parameters lose IsByRef) leaves the call's `ref` beside a by-value declaration: CS1615.
    /// </summary>
    [Test]
    public void AVariableArgument_IsPassedRef_ToADeclaredRefConstructorParameter()
    {
        foreach (var (name, text) in Emits(Row("n1_local")))
        {
            Assert.That(text, Does.Contain("public Box(ref int n)"), $"n1 {name}:\n{text}");
            Assert.That(text, Does.Contain("new Box(ref p)"), $"n1 {name}:\n{text}");
            AssertCompiles(name, "n1", text);
        }

        foreach (var (name, text) in Emits(Row("n6_mybase_new")))
        {
            Assert.That(text, Does.Contain("public BaseBox(ref int n)"), $"n6 {name}:\n{text}");
            Assert.That(text, Does.Contain("public Derived(ref int m)"), $"n6 {name}:\n{text}");
            Assert.That(text, Does.Contain(": base(ref m)"), $"n6 {name}:\n{text}");
            Assert.That(text, Does.Contain("new Derived(ref p)"), $"n6 {name}:\n{text}");
            AssertCompiles(name, "n6", text);
        }
    }

    /// <summary>
    /// F3 — a literal, an expression, a Const and a call each get a copy-in carrier (every `new Box(` in `n9` and `n12` opens with `ref (new int[] {`), so Roslyn accepts them. M2 (no copy-in) passes the
    /// argument bare behind `ref`: CS1510. And the control: a by-value constructor (`Point(x0)`, `Box(n, Optional s)`) is written with no `ref` anywhere — the flags are not over-applied.
    /// </summary>
    [Test]
    public void AValueArgument_GetsACarrier_AndAByValueConstructorGetsNoRef()
    {
        var carrier = new Regex(@"new Box\((?!ref \(new int\[\] \{ [^}]+ \}\)\[0\]\))");
        foreach (var (id, expected) in new[] { ("n9_literal", 2), ("n12_const_call_loop", 3) })
            foreach (var (name, text) in Emits(Row(id)))
            {
                Assert.That(Regex.Matches(text, @"new Box\(ref \(new int\[\] \{ [^}]+ \}\)\[0\]\)").Count, Is.EqualTo(expected), $"{id} {name}: one carrier per value argument:\n{text}");
                Assert.That(carrier.Matches(text).Select(m => m.Value), Is.Empty, $"{id} {name}: every `new Box(` carries one");
                AssertCompiles(name, id, text);
            }

        const string byValue = """
            Class Point
                Public X As Integer
                Sub New(x0 As Integer)
                    X = x0
                End Sub
            End Class

            Class Box
                Public Tag As String
                Sub New(n As Integer, Optional s As String = "d")
                    Tag = s & CStr(n)
                End Sub
            End Class

            Sub Main()
                Dim p As Integer = 3
                Dim pt As New Point(p)
                Dim b As New Box(p)
                Dim c As New Box(p, "e")
                p = 9
                Console.WriteLine(CStr(pt.X) & "," & b.Tag & "," & c.Tag & "," & CStr(p))
            End Sub
            """;
        foreach (var (name, text) in Emits(byValue))
        {
            Assert.That(text, Does.Not.Contain("ref "), $"a by-value constructor is written with no `ref` {name}:\n{text}");
            Assert.That(text, Does.Contain("new Point(p)"), $"{name}:\n{text}");
            AssertCompiles(name, "byValue", text);
        }
    }
}
