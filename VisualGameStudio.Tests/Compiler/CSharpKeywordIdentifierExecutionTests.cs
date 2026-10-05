using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen.CSharp;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #182 — the C# backend escapes EVERY reserved keyword used as a name (`@out`, `@lock`, `@checked`). RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. VB lets a program name a local, parameter, field, property, method, class or type parameter `out`, `ref`, `params`, `lock`, `checked`, `base`, `int`, ... The C# backend escaped a
//  33-word SUBSET of C#'s reserved keywords, so such a program was emitted bare and csc refused it (CS1001 / CS1519) — a green BasicLang build, a red C# one. The fix is one rule
//  (`CSharpBackend.EscapeKeyword`) over the FULL reserved list, applied by `SanitizeName` AND by the places that print a name without it: `MapType`'s fallback for a user type, the generic parameter lists of a
//  class / method / function, and the `where` clause. Without the second half a class named `checked` was declared `class @checked` and used as `checked c = new checked()`. A type position makes NO exception for
//  C#'s built-in spellings: the front end spells a built-in `Integer`/`Single`/..., so a type that reaches `MapType`'s fallback as `float` or `int` is a USER class and bare `float` there names the built-in.
//  Contextual keywords (`var`, `value`, `nameof`, `dynamic`, `record`, ...) are left alone — they compile as names, and `value` must stay the setter's implicit parameter.
//
//  ⭐ THE ORACLE IS vbc, not a backend. Every program below is the implementer's probe (S/t182/probes K01..K14, each with a vbc `.exp`) and its expected text is what the SDK's vbc prints for it
//  (the program wrapped in a VB Module) — measured, never taken from a backend.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): a single-file probe goes through the real BasicLang CLI (standard passes), the real CLI
//  with `--optimize` (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj build and the IDE call) — the three `EntryPoint`s. The two-file probe (K08) goes
//  through `BasicLang build P.blproj` (Debug, and Release = aggressive) and `CompileProjectFiles` (standard and aggressive) — the four `ProjectEntry`s. C# ONLY: this is a C#-only fix.
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (`CSharpProcessRunner`): a probe holds loops, and an in-process runner has no time limit.
//  The FAST half is `CSharpKeywordIdentifierShapeTests` (no process): the emitted C# of every probe, and of every Roslyn reserved keyword, handed to Roslyn to COMPILE.
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from the fixed source and run against a copy of the test output with its BasicLang.dll swapped):
//    M1  `SanitizeName` no longer escapes (identifiers print bare)                                   KILLED by all of K01, K02, K03_K04_K05, K06, K07_K09, K13, K14, K08 (CS1001 / CS1519; K10 alone stays green) and both fast tests
//    M2  `MapType`'s fallback prints a type name raw again (a class named like a keyword)           KILLED by `K06_class` (CS1003), `K13_misc`, `K14_builtin_named_class`, `K08` (CS1514) and both fast tests
//    M3  a generic CLASS's parameter list prints raw                                                 KILLED by `K13_misc` (`class Box<params>`: CS1001) and the fast `TheEmittedCSharp_…`
//    M4  a type position leaves C#'s built-in spellings (`int`, `float`, ...) bare                   KILLED by `K14_builtin_named_class` (CS1061: `float` is the BUILT-IN) and both fast tests
//
//  ⛔ KNOWN GAPS — NOT #182's, measured on the fixed build, and with NO test (asserting one would pin the defect). #182 is the C# backend alone; the same programs fail on the other backends, each of which
//  has its OWN keyword mangler (task #276, "backend keyword mangling"), and a few shapes fail in the front end whatever the backend (task #277):
//    - C++ fails the same way on `this`, `int`, `void`, `delete`, `export`, `unsigned`, `typename`, `nullptr`, ... (`cannot combine with previous 'type-name' declaration specifier`).        — #276
//    - JavaScript fails the same way on `this`, `var`, ... (`SyntaxError: Unexpected token 'this'`).                                                                                    — #276
//    - MSIL: K06 (a class named `lock`, a Structure named `struct`) throws a NullReferenceException at run time, and K13 fails on a generic parameter named `params`.                     — #276
//    - Front end: an Enum member assigned to a typed local types as Object; a lowercase Enum name fails; a generic constructor's arguments and a generic member's return type are not substituted.  — #277
//
//  ⚠ Named "…ExecutionTests" but it spawns no Node: it is C# only, so it is listed in JsExecutionTierRosterTests.NotJavaScriptExecution, like CSharpFieldAssignmentExecutionTests, and NOT in the roster.
// ================================================================================================

/// <summary>The #182 probe programs (S/t182/probes) and vbc's answer for each. C# only.</summary>
internal static class KeywordNameProbes
{
    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.CSharp, HangSafe: true);

    /// <summary>K01 — thirty locals named like a keyword (`out`, `ref`, `params`, `base`, `int`, `lock`, `checked`, `this`, `fixed`, `void`, `struct`, `float`, `bool`, `null`, ...), read, assigned and compounded; a string that says "out".</summary>
    internal static readonly TempProbe K01 = P("K01_locals", """
        Sub Main()
            Dim out As Integer = 1
            Dim ref As Integer = 2
            Dim params As Integer = 3
            Dim base As Integer = 4
            Dim int As Integer = 5
            Dim lock As Integer = 6
            Dim checked As Integer = 7
            Dim this As Integer = 8
            Dim fixed As Integer = 9
            Dim void As Integer = 10
            Dim struct As Integer = 11
            Dim float As Integer = 12
            Dim bool As Integer = 13
            Dim null As Integer = 14
            Dim foreach As Integer = 15
            Dim switch As Integer = 16
            Dim break As Integer = 17
            Dim internal As Integer = 18
            Dim virtual As Integer = 19
            Dim sealed As Integer = 20
            Dim abstract As Integer = 21
            Dim override As Integer = 22
            Dim volatile As Integer = 23
            Dim unsafe As Integer = 24
            Dim uint As Integer = 25
            Dim explicit As Integer = 26
            Dim implicit As Integer = 27
            Dim stackalloc As Integer = 28
            Dim unchecked As Integer = 29
            Dim goto_ As Integer = 0
            out = out + ref + params
            lock += checked
            Console.WriteLine(out & " " & ref & " " & params & " " & base & " " & int & " " & lock & " " & checked & " " & this)
            Console.WriteLine(fixed + void + struct + float + bool + null + foreach + switch + break + internal)
            Console.WriteLine(virtual + sealed + abstract + override + volatile + unsafe + uint + explicit + implicit + stackalloc + unchecked + goto_)
            Dim s As String = "out"
            Console.WriteLine(s & " lock")
        End Sub
        """, "6 2 3 4 5 13 7 8\n135\n264\nout lock");

    /// <summary>K02 — parameters named like a keyword: plain, ByRef, Optional, passed on to a call.</summary>
    internal static readonly TempProbe K02 = P("K02_params", """
        Function Add3(out As Integer, ref As Integer, params As Integer) As Integer
            Return out + ref * 10 + params * 100
        End Function

        Sub Bump(ByRef lock As Integer, ByVal checked As Integer)
            lock = lock + checked
        End Sub

        Function Pick(int As String, Optional base As Integer = 7) As String
            Return int & base
        End Function

        Sub Main()
            Console.WriteLine(Add3(1, 2, 3))
            Dim this As Integer = 5
            Bump(this, 4)
            Console.WriteLine(this)
            Dim out As Integer = 1
            Bump(out, out)
            Console.WriteLine(out)
            Console.WriteLine(Pick("a"))
            Console.WriteLine(Pick("b", 3))
        End Sub
        """, "321\n9\n2\na7\nb3");

    /// <summary>K03 — fields named like a keyword (public, private, Shared), read bare, through `Me.` and through an instance and the class.</summary>
    internal static readonly TempProbe K03 = P("K03_fields", """
        Class Box
            Public out As Integer
            Public lock As String
            Private base As Integer
            Public Shared checked As Integer = 3

            Public Sub New(params As Integer)
                Me.base = params
                out = params * 2
                Me.lock = "L" & params
            End Sub

            Public Function Total() As Integer
                Return out + base + checked
            End Function
        End Class

        Sub Main()
            Dim b As New Box(5)
            b.out = b.out + 1
            Console.WriteLine(b.out)
            Console.WriteLine(b.lock)
            Console.WriteLine(b.Total())
            Box.checked = 10
            Console.WriteLine(b.Total())
        End Sub
        """, "11\nL5\n19\n26");

    /// <summary>K04 — properties named like a keyword; the setter's implicit `value` must still be the setter's parameter.</summary>
    internal static readonly TempProbe K04 = P("K04_property", """
        Class Gate
            Private _lock As Integer
            Public Property lock As Integer
                Get
                    Return _lock
                End Get
                Set(value As Integer)
                    _lock = value * 2
                End Set
            End Property
            Public Property checked As String
            Public Property int As Integer
        End Class

        Sub Main()
            Dim g As New Gate()
            g.lock = 4
            Console.WriteLine(g.lock)
            g.checked = "yes"
            Console.WriteLine(g.checked)
            g.int = 9
            g.int += 1
            Console.WriteLine(g.int)
        End Sub
        """, "8\nyes\n10");

    /// <summary>K05 — methods named like a keyword: an instance Function and Sub, a Shared one, and module-level `Function int` / `Sub out`; called bare, through `Me.` and through an instance.</summary>
    internal static readonly TempProbe K05 = P("K05_method", """
        Class Worker
            Public Function lock(x As Integer) As Integer
                Return x + 1
            End Function
            Public Sub checked()
                Console.WriteLine("checked called")
            End Sub
            Public Shared Function params(a As Integer, b As Integer) As Integer
                Return a * b
            End Function
            Public Function Run() As Integer
                checked()
                Return lock(10) + Me.lock(20)
            End Function
        End Class

        Function int(x As Double) As Integer
            Return CInt(x * 2)
        End Function

        Sub out(msg As String)
            Console.WriteLine("out: " & msg)
        End Sub

        Sub Main()
            Dim w As New Worker()
            Console.WriteLine(w.lock(1))
            w.checked()
            Console.WriteLine(Worker.params(6, 7))
            Console.WriteLine(w.Run())
            Console.WriteLine(int(2.5))
            out("hi")
        End Sub
        """, "2\nchecked called\n42\nchecked called\n32\n5\nout: hi");

    /// <summary>K06 — a CLASS named `lock`, one that `Inherits` it named `checked`, and a Structure named `struct`: a declaration, `New`, a `Dim … As` type, an `Inherits` clause and a `MyBase.New`.</summary>
    internal static readonly TempProbe K06 = P("K06_class", """
        Class lock
            Public Value As Integer
            Public Sub New(v As Integer)
                Value = v
            End Sub
            Public Overrides Function ToString() As String
                Return "lock(" & Value & ")"
            End Function
        End Class

        Class checked
            Inherits lock
            Public Sub New()
                MyBase.New(42)
            End Sub
        End Class

        Structure struct
            Public a As Integer
            Public b As Integer
        End Structure

        Sub Main()
            Dim a As New lock(3)
            Console.WriteLine(a.ToString())
            Dim c As lock = New checked()
            Console.WriteLine(c.Value)
            Dim s As struct
            s.a = 1
            s.b = 2
            Console.WriteLine(s.a + s.b)
        End Sub
        """, "lock(3)\n42\n3");

    /// <summary>K07 — lambda parameters named like a keyword (one, two, a capture beside them), and a Sub lambda handed to `List.ForEach`.</summary>
    internal static readonly TempProbe K07 = P("K07_lambda", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(out As Integer) out * 2
            Console.WriteLine(f(21))
            Dim g As Func(Of Integer, Integer, Integer) = Function(ref As Integer, int As Integer) ref - int
            Console.WriteLine(g(10, 3))
            Dim lock As Integer = 100
            Dim h As Func(Of Integer, Integer) = Function(checked As Integer) checked + lock
            Console.WriteLine(h(5))
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            xs.Add(4)
            xs.ForEach(Sub(base As Integer) Console.WriteLine(base * lock))
        End Sub
        """, "42\n7\n105\n300\n400");

    /// <summary>K09 — a `For` control (declared `int`, and a local `out`), a `For Each` variable `lock` and a `Catch ref As Exception`.</summary>
    internal static readonly TempProbe K09 = P("K09_for", """
        Sub Main()
            Dim total As Integer = 0
            For int As Integer = 1 To 4
                total += int
            Next
            Console.WriteLine(total)
            Dim out As Integer
            For out = 10 To 12
                Console.WriteLine(out)
            Next
            Dim xs As New List(Of String)()
            xs.Add("a")
            xs.Add("b")
            For Each lock As String In xs
                Console.WriteLine(lock)
            Next
            Try
                Throw New Exception("boom")
            Catch ref As Exception
                Console.WriteLine(ref.Message)
            End Try
        End Sub
        """, "10\n10\n11\n12\na\nb\nboom");

    /// <summary>K10 — the CONTEXTUAL keywords (`var`, `value`, `dynamic`, `record`, `add`, `remove`, `init`, `required`, `file`, `args`, ...) are plain names, and `value` is still a setter's implicit parameter.</summary>
    internal static readonly TempProbe K10 = P("K10_contextual", """
        Class Holder
            Private _v As Integer
            Public Property Data As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value + 1
                End Set
            End Property
        End Class

        Sub Main()
            Dim var As Integer = 1
            Dim value As Integer = 2
            Dim dynamic As Integer = 3
            Dim record As Integer = 6
            Dim add As Integer = 7
            Dim remove As Integer = 8
            Dim unmanaged As Integer = 10
            Dim init As Integer = 11
            Dim required As Integer = 12
            Dim scoped As Integer = 13
            Dim file As Integer = 14
            Dim nint As Integer = 15
            Dim orderby As Integer = 16
            Dim group As Integer = 17
            Dim by As Integer = 18
            Dim notnull As Integer = 19
            Dim managed As Integer = 20
            Dim args As Integer = 21
            Console.WriteLine(var + value + dynamic + record + add + remove + unmanaged)
            Console.WriteLine(init + required + scoped + file + nint + orderby + group + by + notnull + managed + args)
            Dim h As New Holder()
            h.Data = value
            Console.WriteLine(h.Data)
        End Sub
        """, "37\n176\n3");

    /// <summary>K13 — a Module variable `out` and Const `lock`; an Interface with a method `checked` and property `params`; a generic class `Box(Of params)` holding a `base` field of that type; an array `int()`; a `Select Case`, an
    /// interpolated string, `While` and `Do … Loop Until` over locals named `struct` / `float` / `bool` / `sealed` / `void` / `this` / `ref`.</summary>
    internal static readonly TempProbe K13 = P("K13_misc", """
        Dim out As Integer = 3
        Const lock As Integer = 40

        Interface IShape
            Function checked() As Integer
            Property params As String
        End Interface

        Class Box(Of params)
            Public base As params
            Public Function Get_() As params
                Return base
            End Function
        End Class

        Class Sq
            Implements IShape
            Private _p As String = "p"
            Public Function checked() As Integer
                Return 9
            End Function
            Public Property params As String
                Get
                    Return _p
                End Get
                Set(value As String)
                    _p = value & "!"
                End Set
            End Property
        End Class

        Function MakeArr() As Integer()
            Dim int() As Integer = {1, 2, 3}
            Return int
        End Function

        Sub Main()
            Dim ref As New Box(Of String)()
            ref.base = "bx"
            Console.WriteLine(ref.Get_())
            Dim txt As String = "bx"
            Dim this As Integer = txt.Length
            Console.WriteLine(this)
            Dim bi As New Box(Of Integer)()
            bi.base = 5
            Console.WriteLine(bi.base)
            Dim void() As Integer = MakeArr()
            Console.WriteLine(void(2))
            Dim s As IShape = New Sq()
            s.params = "q"
            Console.WriteLine(s.params & s.checked())
            out = out + lock
            Console.WriteLine(out)
            Select Case out
                Case 43
                    Console.WriteLine("forty-three")
                Case Else
                    Console.WriteLine("other")
            End Select
            Dim sealed As String = $"v={out}"
            Console.WriteLine(sealed)
            Dim bool As Boolean = out > 10
            If bool Then Console.WriteLine("big")
            Dim struct As Integer = 0
            While struct < 3
                struct += 1
            End While
            Console.WriteLine(struct)
            Dim float As Integer = 0
            Do
                float += 2
            Loop Until float >= 6
            Console.WriteLine(float)
        End Sub
        """, "bx\n2\n5\n3\nq!9\n43\nforty-three\nv=43\nbig\n3\n6");

    /// <summary>K14 — a class named like a C# BUILT-IN TYPE (`float`, `int`): used as a `Function` return type, a `Dim … As`, `New`, and a `List(Of float)` argument. The front end spells a built-in `Single`, so `float` here is a USER class.</summary>
    internal static readonly TempProbe K14 = P("K14_aliasclass", """
        Class float
            Public v As Integer
            Public Function Twice() As Integer
                Return v * 2
            End Function
        End Class

        Class int
            Public name As String = "int-class"
        End Class

        Function Make(n As Integer) As float
            Dim r As New float()
            r.v = n
            Return r
        End Function

        Sub Main()
            Dim a As float = Make(21)
            Console.WriteLine(a.Twice())
            Dim b As int = New int()
            Console.WriteLine(b.name)
            Dim xs As New List(Of float)()
            xs.Add(a)
            Console.WriteLine(xs.Count)
        End Sub
        """, "42\nint-class\n1");

    /// <summary>K08 — two files: a Module `Tools` (a variable `out`, a Function `lock(int)`) and a class `checked` (a field `ref`, a method `params(base)`) in `Lib.bas`, used from `Main.bas` bare and qualified.</summary>
    internal static readonly ProjectProbe K08 = new("K08_crossfile", new[]
        {
            ("Lib.bas", """
                Public Module Tools
                    Public out As Integer = 1
                    Public Function lock(int As Integer) As Integer
                        Return int * 100 + out
                    End Function
                End Module

                Public Class checked
                    Public ref As Integer
                    Public Function params(base As Integer) As Integer
                        Return ref + base
                    End Function
                End Class
                """),
            ("Main.bas", """
                Sub Main()
                    Dim c As New checked()
                    c.ref = 5
                    Console.WriteLine(c.ref + Tools.lock(3))
                    Tools.out = 7
                    Console.WriteLine(Tools.out)
                    Console.WriteLine(lock(1))
                    Console.WriteLine(c.params(10))
                End Sub
                """),
        }, "306\n7\n107\n15", Bk.CSharp);

    /// <summary>Every single-file probe — what the fast fixture hands to Roslyn.</summary>
    internal static readonly IReadOnlyList<TempProbe> SingleFile = new[] { K01, K02, K03, K04, K05, K06, K07, K09, K10, K13, K14 };

    /// <summary>The execution groups: one NUnit case each, looping its probes. A row that fails is NAMED in the message with its id and entry point.</summary>
    internal static readonly IReadOnlyDictionary<string, TempProbe[]> Groups = new Dictionary<string, TempProbe[]>
    {
        ["K01_locals"] = new[] { K01 },
        ["K02_params"] = new[] { K02 },
        ["K03_K04_K05_members"] = new[] { K03, K04, K05 },
        ["K06_class"] = new[] { K06 },
        ["K07_K09_lambda_and_loop_variables"] = new[] { K07, K09 },
        ["K13_misc"] = new[] { K13 },
        ["K14_builtin_named_class"] = new[] { K14 },
        ["K10_contextual"] = new[] { K10 },
    };
}

/// <summary>
/// #182 RUN: a name that is a reserved C# keyword — a local, parameter, field, property, method, class, structure, type parameter, lambda / loop / catch variable — compiles and prints vbc's answer through the CLI, the CLI
/// with <c>--optimize</c> and <c>CompileProjectFiles</c>; a two-file project does the same through <c>BasicLang build</c> (Debug and Release) and <c>CompileProjectFiles</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the CLI legs spawn dotnet and the runners spawn children; keep the machine to this fixture
public class CSharpKeywordIdentifierExecutionTests
{
    private static IEnumerable<TestCaseData> GroupCases()
        => KeywordNameProbes.Groups.Keys.Select(id => new TestCaseData(id).SetName(id));

    /// <summary>The message of a failed assertion without the emitted program (which can be long): the first lines, up to the "--- emitted ---" marker.</summary>
    private static string Brief(string message)
        => string.Join(" // ", message.Replace("\r\n", "\n").Split('\n').TakeWhile(l => !l.StartsWith("--- emitted")).Take(4));

    /// <summary>
    /// One group of probes on C#, through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>: each must print vbc's answer. A failure in one probe or entry point is collected, not thrown, so every
    /// other one still reports (M1 fails them ALL with CS1001 / CS1519; M2 / M3 / M4 only the rows with a type position).
    /// </summary>
    [TestCaseSource(nameof(GroupCases))]
    public void AKeywordUsedAsAName_PrintsVbcsAnswer_InEveryEntryPoint(string group)
    {
        var failures = new List<string>();
        foreach (var probe in KeywordNameProbes.Groups[group])
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                try
                {
                    var got = TempExec.Norm(TempExec.Run(Bk.CSharp, entry, probe.Source, hangSafe: true));
                    if (got != TempExec.Norm(probe.Vb))
                        failures.Add($"{probe.Id} {entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
                }
                catch (AssertionException ex)
                {
                    failures.Add($"{probe.Id} {entry}: {Brief(ex.Message)}");
                }
            }

        Assert.That(failures, Is.Empty, $"{group} on C#, through {string.Join(", ", Enum.GetValues<EntryPoint>())}:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// K08 — the same keywords across two FILES: a Module's variable and Function, and a class with a field and a method, declared in <c>Lib.bas</c> and used from <c>Main.bas</c> bare and qualified. Through
    /// <c>BasicLang build P.blproj</c> (Debug; Release = aggressive) and <c>CompileProjectFiles</c> (standard; aggressive). M2 (a class named `checked` printed raw) is CS1514 here, from the project route.
    /// </summary>
    [Test]
    public void AKeywordUsedAcrossTwoFiles_PrintsVbcsAnswer_InEveryEntryPoint()
    {
        BindingProjectExec.RequireDotnetForCliCSharp(Bk.CSharp); // the CLI's C# project build runs `dotnet build`; the check is outside any multiple-assertion block
        var probe = KeywordNameProbes.K08;
        var failures = new List<string>();
        foreach (var entry in BindingProjectExec.Entries(Bk.CSharp))
        {
            try
            {
                var emitted = BindingProjectExec.Emit(Bk.CSharp, entry, probe.Files);
                var got = TempExec.Norm(CSharpProcessRunner.RunExpectingSuccess(emitted));
                if (got != TempExec.Norm(probe.Vb))
                    failures.Add($"{entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {Brief(ex.Message)}");
            }
        }

        Assert.That(failures, Is.Empty, $"{probe.Id} on C#, through {string.Join(", ", BindingProjectExec.Entries(Bk.CSharp))}:\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// #182 SHAPE — the fast half. Nothing runs and nothing spawns: the C# the compiler writes is handed to Roslyn to COMPILE (never to run). It is the cheap kill of all four mutants (see
/// <see cref="CSharpKeywordIdentifierExecutionTests"/>' header) and, with no category, the part of #182 that the fast subset covers.
/// </summary>
[TestFixture]
public class CSharpKeywordIdentifierShapeTests
{
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

        return new ImprovedCSharpCodeGenerator().Generate(module).Replace("\r\n", "\n");
    }

    private static IEnumerable<(string Name, string Text)> Emits(TempProbe probe)
    {
        yield return ("standard", EmitPipeline(probe.Source, aggressive: false));
        yield return ("aggressive", EmitPipeline(probe.Source, aggressive: true));
        yield return ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, probe.Source).Replace("\r\n", "\n"));
    }

    /// <summary>
    /// The C# of every probe — K01..K07, K09, K10, K13, K14 through the standard pipeline, the aggressive one and <c>CompileProjectFiles</c>, and K08's two files through the project entry point (standard and aggressive) — has
    /// NOTHING wrong for Roslyn. M1 (identifiers print bare) is CS1001 / CS1519 in the first row; M2 (a type name printed raw) and M3 (a generic CLASS's parameter list raw) are the K06 / K13 / K08 rows; M4 (a type position leaves
    /// <c>float</c> / <c>int</c> bare) is CS1061 on K14, where the bare <c>float</c> is the BUILT-IN. And an escape is only ever an IDENTIFIER's: K01 prints <c>"out lock"</c> as a string, and no string literal in any
    /// probe's C# carries an <c>@</c>.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_OfEveryKeywordProbe_CompilesInRoslyn_AndEscapesOnlyIdentifiers()
    {
        var failures = new List<string>();
        void Check(string id, string name, string text)
        {
            var errors = MyBaseMethodCallStatementShapeTests.RoslynErrors(text);
            if (errors.Length > 0) failures.Add($"{id} {name}: Roslyn finds {errors[0]} ({errors.Length} errors)");

            var strings = CSharpSyntaxTree.ParseText(text).GetRoot().DescendantTokens()
                .Where(t => t.Kind() == SyntaxKind.StringLiteralToken)
                .Select(t => t.ValueText);
            foreach (var literal in strings.Where(s => s.Contains('@')))
                failures.Add($"{id} {name}: a string literal carries an '@' escape: \"{literal}\"");
        }

        foreach (var probe in KeywordNameProbes.SingleFile)
            foreach (var (name, text) in Emits(probe))
            {
                Check(probe.Id, name, text);
                if (probe.Id == "K01_locals")
                {
                    if (!text.Contains("@out") || !text.Contains("@lock") || !text.Contains("@checked")) failures.Add($"K01 {name}: no @out / @lock / @checked in\n{text}");
                    if (!text.Contains("\"out lock\"")) failures.Add($"K01 {name}: the string \"out lock\" was not printed as written");
                }
            }

        foreach (var entry in new[] { ProjectEntry.ProjectStandard, ProjectEntry.ProjectAggressive })
            Check(KeywordNameProbes.K08.Id, entry.ToString(), BindingProjectExec.Emit(Bk.CSharp, entry, KeywordNameProbes.K08.Files).Replace("\r\n", "\n"));

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// The reserved list is COMPLETE: every keyword Roslyn itself calls reserved (<c>SyntaxFacts.GetReservedKeywordKinds</c>, the oracle — no list is kept here) that BasicLang accepts as a name, as a LOCAL and as a CLASS, is
    /// escaped. The 33-word subset the backend used to carry fails this on <c>out</c>, <c>ref</c>, <c>params</c>, <c>lock</c>, <c>checked</c>, <c>fixed</c>, ... A keyword that is also a BasicLang keyword (<c>If</c>, <c>Do</c>, <c>New</c>, ...) is
    /// refused by the front end and is skipped, NAMED in the failure message; the floor keeps the sweep from going hollow if that refusal ever widens.
    /// </summary>
    [Test]
    public void EveryReservedCSharpKeyword_ThatBasicLangAcceptsAsAName_IsEscaped()
    {
        var keywords = SyntaxFacts.GetReservedKeywordKinds().Select(k => SyntaxFacts.GetText(k)).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        var refused = new List<string>();
        var exercised = 0;

        foreach (var kw in keywords)
        {
            var asLocal = $"Sub Main()\n    Dim {kw} As Integer = 7\n    Console.WriteLine({kw} + 1)\nEnd Sub\n";
            var asClass = $"Class {kw}\n    Public v As Integer\nEnd Class\n\nSub Main()\n    Dim c As New {kw}()\n    c.v = 7\n    Console.WriteLine(c.v + 1)\nEnd Sub\n";
            foreach (var (form, source) in new[] { ("local", asLocal), ("class", asClass) })
            {
                // `Decimal` and `SByte` are BasicLang's OWN type names: `Class decimal` / `New decimal()` binds the BUILT-IN in the front end, whatever the backend writes (measured: CS1061 on the
                // built-in) — the same layer as the Enum / generic gaps of task #277, not #182's. A LOCAL named `decimal` is fine and stays in the sweep.
                if (form == "class" && BasicLangTypeNames.Contains(kw)) continue;
                string text;
                try
                {
                    text = new ImprovedCSharpCodeGenerator().Generate(CSharpTestSupport.BuildModule(source));
                }
                catch (InvalidOperationException)
                {
                    refused.Add($"{kw}({form})");
                    continue;
                }

                exercised++;
                var errors = MyBaseMethodCallStatementShapeTests.RoslynErrors(text);
                if (errors.Length > 0) failures.Add($"{kw} as a {form}: {errors[0]}");
            }
        }

        TestContext.Out.WriteLine($"swept {keywords.Count} reserved keywords: {exercised} programs accepted by the front end; refused ({refused.Count}): {string.Join(", ", refused)}");
        Assert.That(failures, Is.Empty, $"{failures.Count} keyword(s) are not escaped:\n" + string.Join("\n", failures));
        Assert.That(exercised, Is.GreaterThanOrEqualTo(MinimumExercised),
            $"only {exercised} of {keywords.Count * 2} keyword programs were accepted by the front end — the sweep has gone hollow. Refused: {string.Join(", ", refused)}");
    }

    /// <summary>The floor, set below the measurement: 76 of the 160 keyword programs (81 keywords x local / class, less Decimal and SByte as classes) are accepted by the front end — the rest are BasicLang's own keywords and type names.</summary>
    private const int MinimumExercised = 60;

    private static readonly HashSet<string> BasicLangTypeNames = new(StringComparer.Ordinal) { "decimal", "sbyte" };
}
