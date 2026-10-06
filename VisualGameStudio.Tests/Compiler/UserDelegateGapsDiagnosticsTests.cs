using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;
using AccessModifier = BasicLang.Compiler.AST.AccessModifier;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #202 — three gaps in user Delegates, found by #187 (user-delegate conversion), each fixed in ONE place (fix commit 8ee40f8c).
//
//   (1) PARSER.     `Public Delegate Sub D(...)` did not parse ("Expected Function, Sub, Class, ... after modifiers, got 'Delegate'"); only a bare `Delegate` did. The top-level modifier arm now takes the access
//                   VB allows at file level, Public and Friend, and records it on `DelegateDeclarationNode.Access`. The rest stay REFUSED, now with vbc's own diagnostic at the declaration (the first modifier):
//                   Private BC31089, Protected / Protected Friend BC31047, Shared / Async / Iterator BC30385.
//   (2) CROSS-FILE. A Delegate declared in a SIBLING file was visible only when its file compiled FIRST; in the other order its name fell to the .NET fallback ("Cannot assign value of type 'Func' to
//                   variable of type 'Joiner'"). `SemanticAnalyzer.RegisterSiblingDelegateSignatures` (pass 1b) registers a pending sibling's delegates as a sibling CLASS is, and the LSP's project collector
//                   (`LspModuleSymbolCollector`) got the same case.
//   (3) .INVOKE.    `v.Invoke(args)` is `v(args)` for every delegate the analyzer models (`SemanticAnalyzer.IsModeledDelegate`, read by the analyzer AND the IR builder): a user Delegate, a Func / Action, and a
//                   bare Action. On a Func / Action it typed Object and was lowered as a METHOD call only C# has (clang "no member named 'Invoke'", node "f.Invoke is not a function", MSIL
//                   MissingMethodException). A BasicLang `Class Action` / `Class Func` with its own Invoke is excluded: its Invoke is a method.
//
//  ⭐ THE ORACLE IS vbc. The sources are the implementer's probes (S/t202/probes S01..S15, mprobes M1..M3, bc/ P_* and Q_*), the expected output of each is what vbc prints for the program wrapped in a VB
//  Module, and each refusal's code is what vbc reports for the declaration. Nothing is re-derived from what a backend prints.
//
//  This fixture is the fast half: parse + analyze, `CompileProjectFiles` (stops at the combined IR), the LSP project collector, and generated TEXT; no process. `UserDelegateGapsExecutionTests` (Integration)
//  RUNS the same programs on C#, C++, JavaScript and MSIL. The moved pin is `UserDelegateConversionTests.DotInvoke_OnFuncAction_IsTypedByItsTypeArguments_Task202` (was `..._PinnedAgainst202`).
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Each is outside #202's three gaps:
//    * A Delegate NESTED in a Module or a Class does not parse at all, bare or modified (S05, S06). Whether it takes the access of a nested type, and what a nested type's name is to a sibling file, is a
//      ruling, not a measurement (#290).
//    * A file-level `Private Class` is accepted (bc/P_class.bas; vbc: BC31089). The Delegate arm refuses it; the Class arm was not touched (#290).
//    * S08 on MSIL: `InvalidProgramException` for a `Func(Of Double, Double)` lambda. Pre-existing, identical with no `.Invoke` in the program (ctl/C08e).
//    * S13 on JavaScript: `F.Invoke(2)` on a bare FIELD inside its OWN class is `ReferenceError: F is not defined`. Pre-existing #187 named-receiver path, identical for a user Delegate field (ctl/C13u).
//    * S15 on C++: a user `Class Action` / `Class Func` named like the std type does not compile. Pre-existing and identical before the fix, so no C++ cell here (the front-end row below holds the C# claim).
//
//  ⭐ MUTANTS (each the fix plus ONE change, built from a plain copy of the source and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    MA  the file-level Delegate arm in `Parser.ParseTopLevelDeclaration` is removed .................... `AFileLevelPublicOrFriendDelegate_Parses...`, the three refusal rows (a refused modifier then parses as a
//                                                                                                         Delegate or fails differently), M1 and M3 (`Public` / `Friend`), and the execution fixture's S01-S03
//    MB  `RegisterSiblingDelegateSignatures(pendingUnits)` is removed ................................... `ASiblingFilesDelegate_IsVisibleInEveryCompileOrder` (M1, M2, M3 reversed), and the execution fixture
//    MC  the LSP collector's `DelegateDeclarationNode` case is disabled ................................. `TheLspProjectCollector_SeesASiblingFilesDelegate_InEitherDocumentOrder` (only this one)
//    MD  the analyzer's `.Invoke` gate reads `IsUserDelegate` again ................................... the moved pin, `ADotInvokeOnAFunc_IsTypedByItsTypeArguments`, `ADotInvokeOnAnAction_...`,
//                                                                                                         `ADotInvokeOnAFuncOrAction_IsLoweredAsTheCallItIs` (the front end refuses the program)
//    ME  the IR builder's `.Invoke` gate reads `DelegateSignature != null` again ........................ the moved pin (C# reads `f.Invoke(5)`), `ADotInvokeOnAFuncOrAction_IsLoweredAsTheCallItIs` (C++ and
//                                                                                                         JavaScript text), and the execution fixture's S07-S14 on C++, JavaScript and MSIL (C# still runs)
//    MF  the bare-Action arm of `IsModeledDelegate` drops `DeclaredMemberNames == null` ................. `AUserClassNamedActionOrFunc_KeepsItsOwnInvokeMethod` (only this one)
//    MG  the analyzer's `invokesThroughDotInvoke` flag is dropped from the delegate-invocation arm ...... `ADotInvokeOnAnAction_IsTypedVoid_AndLeavesNoDeadTemp`: the bare Action's `.Invoke()` is typed Action
//                                                                                                         (not Void) AND leaves a dead Action temp on C++ and MSIL. It SURVIVES at run time: every program
//                                                                                                         still prints vbc's answer, so no execution cell can see it
//    MH  the BC31089 check is removed ................................................................... `APrivateDelegate_IsRefused_BC31089` (only this one: a Private Delegate runs again)
//    MI  pass 1b runs AFTER pass 2 (the class members) ................................................. `ASiblingFilesDelegate_IsVisibleInEveryCompileOrder` (M4, Main's file first)
//    MJ  the bare-Action arm of `IsModeledDelegate` is removed (only Kind=Delegate is modeled) .......... `ADotInvokeOnAFuncOrAction_IsLoweredAsTheCallItIs`, `ADotInvokeOnAnAction_...` and the execution fixture
// ================================================================================================

/// <summary>The probes #202's two fixtures share. The sources are the implementer's (S/t202); every expected output is vbc's own.</summary>
internal static class UserDelegateGapProbes
{
    private static TempProbe Probe(string id, string source, string vb, Bk agrees = Bk.All)
        => new(id, source, vb, agrees, HangSafe: true);

    // ---- (1) a file-level Public / Friend Delegate ----

    internal static readonly TempProbe S01 = Probe("S01_pubsub", """
        Public Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Sub(m As String) Console.WriteLine("got " & m)
            d("x")
            d.Invoke("y")
        End Sub
        """, "got x\ngot y");

    internal static readonly TempProbe S02 = Probe("S02_pubfunc", """
        Public Delegate Function Twice(n As Integer) As Integer

        Function Apply(t As Twice, v As Integer) As Integer
            Return t(v) + 1
        End Function

        Sub Main()
            Dim t As Twice = Function(n As Integer) n * 2
            Console.WriteLine(Apply(t, 5))
            Dim r As Integer = t.Invoke(4)
            Console.WriteLine(r)
        End Sub
        """, "11\n8");

    internal static readonly TempProbe S03 = Probe("S03_friend", """
        Friend Delegate Function Combine(a As Integer, b As Integer) As Integer
        Friend Delegate Sub Show(s As String)

        Sub Main()
            Dim c As Combine = Function(a As Integer, b As Integer) a + b
            Dim s As Show = Sub(x As String) Console.WriteLine("show " & x)
            s(CStr(c(3, 4)))
        End Sub
        """, "show 7");

    // ---- (3) `.Invoke` on a Func / Action value ----

    internal static readonly TempProbe S07 = Probe("S07_funcinv_local", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
            Dim r As Integer = f.Invoke(5)
            Console.WriteLine(r)
            Dim g As Func(Of Integer, Integer, Integer) = Function(a As Integer, b As Integer) a - b
            Dim q As Integer = g.Invoke(9, 4)
            Console.WriteLine(q)
        End Sub
        """, "10\n5");

    /// <summary>⚠ Not on MSIL: <c>Func(Of Double, Double)</c> is an InvalidProgramException there, with or without `.Invoke` (ctl/C08e).</summary>
    internal static readonly TempProbe S08 = Probe("S08_funcinv_arith", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
            Console.WriteLine(f.Invoke(5) + 1)
            Dim r As Integer = f.Invoke(2) * f.Invoke(3)
            Console.WriteLine(r)
            Dim d As Func(Of Double, Double) = Function(x As Double) x / 4
            Dim e As Double = d.Invoke(10) + 0.5
            Console.WriteLine(e)
        End Sub
        """, "11\n24\n3", Bk.CSharp | Bk.Cpp | Bk.JavaScript);

    internal static readonly TempProbe S09 = Probe("S09_funcinv_param", """
        Sub Show(n As Integer)
            Console.WriteLine("n=" & n)
        End Sub

        Function Twice(n As Integer) As Integer
            Return n * 2
        End Function

        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(x As Integer) x + 1
            Show(f.Invoke(5))
            Console.WriteLine(Twice(f.Invoke(1)))
            Show(f.Invoke(f.Invoke(0)))
        End Sub
        """, "n=6\n4\nn=2");

    internal static readonly TempProbe S10 = Probe("S10_actioninv", """
        Sub Main()
            Dim a As Action = Sub() Console.WriteLine("hi")
            a.Invoke()
            Dim b As Action(Of String) = Sub(s As String) Console.WriteLine("b " & s)
            b.Invoke("x")
            Dim c As Action(Of Integer, Integer) = Sub(x As Integer, y As Integer) Console.WriteLine(x + y)
            c.Invoke(2, 3)
        End Sub
        """, "hi\nb x\n5");

    internal static readonly TempProbe S11 = Probe("S11_funcstr", """
        Sub Main()
            Dim g As Func(Of String) = Function() "ab"
            Console.WriteLine(g.Invoke() & "cd")
            Dim s As String = "x" & g.Invoke()
            Console.WriteLine(s)
            Dim h As Func(Of Integer, String) = Function(n As Integer) "n" & n
            Dim t As String = h.Invoke(7) & "!"
            Console.WriteLine(t)
        End Sub
        """, "abcd\nxab\nn7!");

    internal static readonly TempProbe S12 = Probe("S12_funcinv_field", """
        Class Holder
            Public Op As Func(Of Integer, Integer)
            Public Done As Action(Of String)
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Op = Function(x As Integer) x * 10
            h.Done = Sub(s As String) Console.WriteLine("done " & s)
            Dim r As Integer = h.Op.Invoke(4)
            Console.WriteLine(r)
            h.Done.Invoke("ok")
        End Sub
        """, "40\ndone ok");

    /// <summary>⚠ Not on JavaScript: `F.Invoke(2)` on a bare FIELD inside its own class is a ReferenceError there (#187's named-receiver path; ctl/C13u).</summary>
    internal static readonly TempProbe S13 = Probe("S13_invoke_inclass", """
        Delegate Function Op(n As Integer) As Integer

        Class Holder
            Public F As Func(Of Integer, Integer)
            Public U As Op
            Public Done As Action

            Public Sub Run()
                Console.WriteLine(F.Invoke(2))
                Console.WriteLine(Me.F.Invoke(3))
                Console.WriteLine(U.Invoke(4))
                Done.Invoke()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.F = Function(x As Integer) x * 10
            h.U = Function(x As Integer) x + 1
            h.Done = Sub() Console.WriteLine("done")
            h.Run()
        End Sub
        """, "20\n30\n5\ndone", Bk.CSharp | Bk.Cpp | Bk.Msil);

    internal static readonly TempProbe S14 = Probe("S14_invoke_param", """
        Function Apply(f As Func(Of Integer, Integer), v As Integer) As Integer
            Return f.Invoke(v) + f.Invoke(1)
        End Function

        Function Make(k As Integer) As Func(Of Integer, Integer)
            Return Function(x As Integer) x + k
        End Function

        Sub Main()
            Console.WriteLine(Apply(Function(x As Integer) x * x, 3))
            Dim r As Integer = Make(5).Invoke(1)
            Console.WriteLine(r)
            Dim p As Func(Of Integer, Boolean) = Function(x As Integer) x > 2
            If p.Invoke(5) Then Console.WriteLine("big")
        End Sub
        """, "10\n6\nbig");

    /// <summary>A user class named like the std delegates, with its own `Invoke`: a METHOD. vbc prints 42 and f3. ⚠ Front end only (C++ never compiled a class named `Action`).</summary>
    internal const string S15 = """
        Class Action
            Public Function Invoke() As Integer
                Return 42
            End Function
        End Class

        Class Func
            Public Function Invoke(n As Integer) As String
                Return "f" & n
            End Function
        End Class

        Sub Main()
            Dim a As New Action()
            Dim r As Integer = a.Invoke()
            Console.WriteLine(r)
            Dim f As New Func()
            Console.WriteLine(f.Invoke(3))
        End Sub
        """;

    // ---- (2) a Delegate in a sibling file: S/t202/mprobes M1..M3, each as (A.bas, B.bas) in the compile order AB ----

    /// <summary>A Public Delegate Function in A; B uses it as a field type, a parameter type and a local type.</summary>
    internal static readonly ProjectProbe M1 = new("M1_pub", new[]
    {
        ("A.bas", """
            Public Delegate Function Op(n As Integer) As Integer
            """),
        ("B.bas", """
            Class Box
                Public F As Op
            End Class

            Function Apply(o As Op, v As Integer) As Integer
                Return o(v)
            End Function

            Sub Main()
                Dim local As Op = Function(n As Integer) n * 3
                Console.WriteLine(Apply(local, 2))
                Dim b As New Box()
                b.F = Function(n As Integer) n + 100
                Console.WriteLine(b.F(1))
                Console.WriteLine(Apply(b.F, 5))
            End Sub
            """),
    }, "6\n101\n105");

    /// <summary>Two BARE Delegates in A (a Sub and a Function); B takes them as a field, a parameter and a local, and calls `.Invoke` on both.</summary>
    internal static readonly ProjectProbe M2 = new("M2_default", new[]
    {
        ("A.bas", """
            Delegate Sub Notify(msg As String)
            Delegate Function Joiner(a As String, b As String) As String
            """),
        ("B.bas", """
            Class Sink
                Public OnMsg As Notify
            End Class

            Sub Fire(n As Notify, s As String)
                n(s)
            End Sub

            Sub Main()
                Dim j As Joiner = Function(a As String, b As String) a & "+" & b
                Dim n As Notify = Sub(m As String) Console.WriteLine("msg " & m)
                Fire(n, j("x", "y"))
                Dim k As New Sink()
                k.OnMsg = n
                k.OnMsg("field")
                n.Invoke(j.Invoke("p", "q"))
            End Sub
            """),
    }, "msg x+y\nmsg field\nmsg p+q");

    /// <summary>A Friend Delegate and a Friend Class that holds it, both in A; B builds a delegate for it in a Function and reads it back.</summary>
    internal static readonly ProjectProbe M3 = new("M3_friend", new[]
    {
        ("A.bas", """
            Friend Delegate Function Score(n As Integer) As Integer

            Friend Class Game
                Public Rule As Score
                Public Function Play(v As Integer) As Integer
                    Return Rule(v)
                End Function
            End Class
            """),
        ("B.bas", """
            Function Make(bonus As Integer) As Score
                Return Function(n As Integer) n + bonus
            End Function

            Sub Main()
                Dim g As New Game()
                g.Rule = Make(7)
                Console.WriteLine(g.Play(1))
                Dim s As Score = g.Rule
                Console.WriteLine(s(10))
            End Sub
            """),
    }, "8\n17");

    /// <summary>
    /// THREE files: the Delegates in one, a CLASS in another whose field, parameter and return are typed by them, and Main in the third. The class's members read the delegates from a pending SIBLING, so
    /// pass 1b (the delegates) must run before pass 2 (the class members) — and, with Main compiled first, both are pending.
    /// </summary>
    internal static readonly ProjectProbe M4 = new("M4_threefiles", new[]
    {
        ("Delegates.bas", """
            Public Delegate Sub Notify(msg As String)
            Public Delegate Function Joiner(a As String, b As String) As String
            """),
        ("Sinks.bas", """
            Public Class Sink
                Public OnMsg As Notify
                Public Pick As Joiner

                Public Sub Hook(n As Notify)
                    OnMsg = n
                End Sub

                Public Function Maker() As Joiner
                    Return Pick
                End Function
            End Class
            """),
        ("Prog.bas", """
            Sub Main()
                Dim k As New Sink()
                k.Hook(Sub(m As String) Console.WriteLine("hook " & m))
                k.OnMsg("x")
                k.Pick = Function(a As String, b As String) a & b
                Dim j As Joiner = k.Maker()
                Console.WriteLine(j("p", "q"))
            End Sub
            """),
    }, "hook x\npq");

    internal static readonly ProjectProbe[] Shapes = { M1, M2, M3 };

    /// <summary>The same project with its files in the REVERSE compile order.</summary>
    internal static ProjectProbe Reversed(ProjectProbe probe) => probe with { Id = probe.Id + "_reversed", Files = probe.Files.Reverse().ToArray() };

    internal static string Normalised(string text) => text.Replace("\r\n", "\n");
}

[TestFixture]
public class UserDelegateGapsDiagnosticsTests
{
    // ============================================================================================
    // Helpers
    // ============================================================================================

    private const string Tail = """


        Sub Main()
            Dim d As Notify = Sub(m As String) Console.WriteLine("got " & m)
            d("x")
        End Sub
        """;

    /// <summary>Lex and parse. Returns the parser, so a test reads its <c>Errors</c> as well as the tree.</summary>
    private static (ProgramNode Ast, Parser Parser) Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        return (ast, parser);
    }

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a diagnostic) and analyze; every diagnostic, in the analyzer's order.</summary>
    private static List<SemanticError> Analyze(string source)
    {
        var (ast, parser) = Parse(source);
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    private static string Said(IEnumerable<SemanticError> errors) => string.Join(" | ", errors.Select(e => $"{e.Line}:{e.Column} {e.Message}"));

    /// <summary><c>CompileProjectFiles</c> on the given files, written in the given order to a fresh directory.</summary>
    private static CompilationResult CompileProject(IEnumerable<(string Name, string Source)> files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t202-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f =>
            {
                var path = Path.Combine(dir, f.Name);
                File.WriteAllText(path, f.Source);
                return path;
            }).ToList();
            return new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(paths);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static IEnumerable<IEnumerable<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1) { yield return items; yield break; }
        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest)) yield return new[] { items[i] }.Concat(tail);
        }
    }

    // ============================================================================================
    // (1) The parser: a file-level Delegate takes Public and Friend, and refuses the rest with vbc's code
    // ============================================================================================

    /// <summary>
    /// A Delegate written with <c>Public</c>, <c>Friend</c> or no modifier parses, records the access it was given, and is USABLE: the analyzer accepts the program that declares a variable of its type and
    /// calls it. Both kinds (Sub, Function). <c>Public Delegate Sub D(...)</c> was a parse error ("Expected Function, Sub, Class, ... after modifiers, got 'Delegate'"). MA removes the arm.
    /// </summary>
    [Test]
    public void AFileLevelPublicOrFriendDelegate_Parses_RecordsItsAccess_AndIsUsable()
    {
        var rows = new (string Declaration, string Use, AccessModifier? Access)[]
        {
            ("Public Delegate Sub Notify(msg As String)", "Dim d As Notify = Sub(m As String) Console.WriteLine(m)\n    d(\"x\")", AccessModifier.Public),
            ("Friend Delegate Sub Notify(msg As String)", "Dim d As Notify = Sub(m As String) Console.WriteLine(m)\n    d(\"x\")", AccessModifier.Friend),
            ("Delegate Sub Notify(msg As String)", "Dim d As Notify = Sub(m As String) Console.WriteLine(m)\n    d(\"x\")", null),
            ("Public Delegate Function Twice(n As Integer) As Integer", "Dim t As Twice = Function(n As Integer) n * 2\n    Dim r As Integer = t(4)", AccessModifier.Public),
            ("Friend Delegate Function Twice(n As Integer) As Integer", "Dim t As Twice = Function(n As Integer) n * 2\n    Dim r As Integer = t.Invoke(4)", AccessModifier.Friend),
        };

        Assert.Multiple(() =>
        {
            foreach (var (declaration, use, access) in rows)
            {
                var source = $"{declaration}\n\nSub Main()\n    {use}\nEnd Sub\n";
                var (ast, parser) = Parse(source);
                Assert.That(parser.Errors, Is.Empty, $"[{declaration}] parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
                var node = ast.Declarations.OfType<DelegateDeclarationNode>().SingleOrDefault();
                Assert.That(node, Is.Not.Null, $"[{declaration}] the program declares one Delegate");
                if (node == null) continue;
                if (access != null) Assert.That(node.Access, Is.EqualTo(access), $"[{declaration}] the access it was written with is recorded");

                var analyzer = new SemanticAnalyzer();
                analyzer.Analyze(ast);
                Assert.That(analyzer.Errors, Is.Empty, $"[{declaration}] the analyzer accepts it: " + Said(analyzer.Errors));
            }
        });
    }

    /// <summary>One refused declaration: the modifiers as written, and the exact diagnostic vbc gives it.</summary>
    public sealed record RefusedDelegate(string Modifiers, string Code, string Message)
    {
        public override string ToString() => Modifiers + " Delegate -> " + Code;
    }

    private const string BC31089 = "BC31089: Types declared 'Private' must be inside another type.";
    private const string BC31047 = "BC31047: Protected types can only be declared inside of a class.";

    private static IEnumerable<TestCaseData> RefusedRows()
    {
        // vbc's answers, measured by the implementer on S/t202/bc: P_Private, P_Protected, P_ProtectedFriend, Q_Shared, Q_PublicShared, Q_Async, Q_Iterator (one error each, at the declaration).
        yield return new TestCaseData((object)new[] { new RefusedDelegate("Private", "BC31089", BC31089) })
            .SetName("APrivateDelegate_IsRefused_BC31089");
        yield return new TestCaseData((object)new[]
            {
                new RefusedDelegate("Protected", "BC31047", BC31047),
                new RefusedDelegate("Protected Friend", "BC31047", BC31047),
            })
            .SetName("AProtectedDelegate_IsRefused_BC31047");
        yield return new TestCaseData((object)new[]
            {
                new RefusedDelegate("Shared", "BC30385", "BC30385: 'Shared' is not valid on a Delegate declaration."),
                new RefusedDelegate("Public Shared", "BC30385", "BC30385: 'Shared' is not valid on a Delegate declaration."),
                new RefusedDelegate("Async", "BC30385", "BC30385: 'Async' is not valid on a Delegate declaration."),
                new RefusedDelegate("Iterator", "BC30385", "BC30385: 'Iterator' is not valid on a Delegate declaration."),
            })
            .SetName("ASharedAsyncOrIteratorDelegate_IsRefused_BC30385");
    }

    /// <summary>
    /// Each declaration is refused with vbc's own code and text, ONCE, at the declaration: the line and column of its FIRST modifier. Through the parser, and through <c>CompileProjectFiles</c> (what a build
    /// and the IDE call), where the same message reaches the build's error list at the same position. Private (MH removes its check; the program then RUNS on every backend), Protected and Protected Friend are
    /// for a type inside another type; Shared, Async and Iterator never apply to a Delegate. A declaration refused this way leaves no half-parsed Delegate behind: the rest of the file still parses.
    /// </summary>
    [TestCaseSource(nameof(RefusedRows))]
    public void AFileLevelDelegateModifierVbcRefuses_IsRefusedWithVbcsCode_AtTheDeclaration(RefusedDelegate[] rows)
    {
        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                var source = $"{row.Modifiers} Delegate Sub Notify(msg As String){Tail}";
                var (ast, parser) = Parse(source);
                var said = string.Join(" | ", parser.Errors.Select(e => $"{e.Token.Line}:{e.Token.Column} {e.Message}"));
                Assert.That(parser.Errors, Has.Count.EqualTo(1), $"[{row}] exactly one parse error; got {said}");
                if (parser.Errors.Count != 1) continue;
                Assert.That(parser.Errors[0].Message, Is.EqualTo(row.Message), $"[{row}] vbc's {row.Code}");
                Assert.That((parser.Errors[0].Token.Line, parser.Errors[0].Token.Column), Is.EqualTo((1, 1)), $"[{row}] at the declaration: its first modifier");
                Assert.That(ast.Declarations.OfType<DelegateDeclarationNode>(), Is.Empty, $"[{row}] no Delegate is declared by a refused declaration");
                Assert.That(ast.Declarations.OfType<SubroutineNode>().Select(s => s.Name), Does.Contain("Main"), $"[{row}] the rest of the file still parses");

                var result = CompileProject(new[] { ("Main.bas", source) });
                var project = result.AllErrors.Where(e => e.Message.Contains(row.Code)).ToList();
                Assert.That(project, Has.Count.EqualTo(1), $"[{row}] CompileProjectFiles reports {row.Code} once; got " + Said(result.AllErrors));
                if (project.Count == 1)
                {
                    Assert.That(project[0].Message, Does.Contain(row.Message), $"[{row}] CompileProjectFiles: the same text");
                    Assert.That((project[0].Line, project[0].Column), Is.EqualTo((1, 1)), $"[{row}] CompileProjectFiles: the same position");
                }
            }
        });
    }

    // ============================================================================================
    // (3) `.Invoke` on a Func / Action value is typed by its type arguments, and is the call
    // ============================================================================================

    /// <summary>
    /// <c>f.Invoke(args)</c> on a <c>Func(Of ..., R)</c> is typed R — by the Func's LAST type argument, and not Object — wherever the result goes: a typed local (the moved pin), arithmetic, a String concatenation
    /// (<c>Func(Of String).Invoke() &amp; "x"</c>), a typed parameter, another call's argument, a field, a Return. The arguments are checked against the type arguments too. Untouched by MD, which gives the
    /// result back to Object.
    /// </summary>
    [Test]
    public void ADotInvokeOnAFunc_IsTypedByItsTypeArguments()
    {
        var accepted = new[]
        {
            // a typed local, arithmetic, a second Func, a typed parameter, and nesting
            ("S07/S08/S09", """
                Sub Show(n As Integer)
                    Console.WriteLine(n)
                End Sub

                Sub Main()
                    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
                    Dim r As Integer = f.Invoke(5)
                    Dim s As Integer = f.Invoke(5) + 1
                    Dim t As Integer = f.Invoke(2) * f.Invoke(3)
                    Dim g As Func(Of Integer, Integer, Integer) = Function(a As Integer, b As Integer) a - b
                    Dim q As Integer = g.Invoke(9, 4)
                    Show(f.Invoke(f.Invoke(0)))
                End Sub
                """),
            // `Func(Of String).Invoke() & "x"`, both operand orders, and into a String
            ("S11", """
                Sub Main()
                    Dim g As Func(Of String) = Function() "ab"
                    Console.WriteLine(g.Invoke() & "x")
                    Dim s As String = "x" & g.Invoke()
                    Dim h As Func(Of Integer, String) = Function(n As Integer) "n" & n
                    Dim t As String = h.Invoke(7) & "!"
                End Sub
                """),
            // a Func held in a PARAMETER, a FIELD, and returned by a call
            ("S12/S14", """
                Class Holder
                    Public Op As Func(Of Integer, Integer)
                End Class

                Function Apply(f As Func(Of Integer, Integer), v As Integer) As Integer
                    Return f.Invoke(v) + f.Invoke(1)
                End Function

                Function Make(k As Integer) As Func(Of Integer, Integer)
                    Return Function(x As Integer) x + k
                End Function

                Sub Main()
                    Dim h As New Holder()
                    Dim r As Integer = h.Op.Invoke(4)
                    Dim m As Integer = Make(5).Invoke(1)
                    Dim p As Func(Of Integer, Boolean) = Function(x As Integer) x > 2
                    If p.Invoke(5) Then Console.WriteLine("big")
                End Sub
                """),
        };

        // each refused for the TYPE the call has: the Func's own result, not Object — and an argument that does not fit is checked against its type argument
        var refused = new[]
        {
            ("an Integer result into a String", """
                Sub Main()
                    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
                    Dim s As String = f.Invoke(5)
                End Sub
                """, "Cannot assign value of type 'Integer' to variable of type 'String'"),
            ("a String result into an Integer", """
                Sub Main()
                    Dim g As Func(Of String) = Function() "ab"
                    Dim n As Integer = g.Invoke()
                End Sub
                """, "Cannot assign value of type 'String' to variable of type 'Integer'"),
            ("a String argument for an Integer parameter", """
                Sub Main()
                    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
                    Dim r As Integer = f.Invoke("no")
                End Sub
                """, "Argument 1: cannot convert from 'String' to 'Integer'. Expected: Func(Of Integer, Integer)"),
            ("two arguments for one parameter", """
                Sub Main()
                    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
                    Dim r As Integer = f.Invoke(1, 2)
                End Sub
                """, "Delegate 'Func' expects 1 argument(s), got 2. Expected: Func(Of Integer, Integer)"),
        };

        Assert.Multiple(() =>
        {
            foreach (var (id, source) in accepted)
                Assert.That(Analyze(source), Is.Empty, $"{id}: vbc accepts it; got " + Said(Analyze(source)));
            foreach (var (id, source, message) in refused)
            {
                var errors = Analyze(source);
                Assert.That(errors.Select(e => e.Message).ToList(), Has.Some.Contain(message), $"{id}: refused for its real type; got " + Said(errors));
                Assert.That(errors, Has.Count.EqualTo(1), $"{id}: one diagnostic; got " + Said(errors));
            }
        });
    }

    /// <summary>
    /// <c>a.Invoke()</c> on an <c>Action</c> — the bare one (a CLASS to the type table), <c>Action(Of String)</c> and <c>Action(Of Integer, Integer)</c> — is a statement, typed VOID: it is accepted as one, and
    /// refused where a value is wanted ("Cannot assign value of type 'Void'"), as vbc does (BC30491). The bare Action is the one a Kind check misses, so it is entered by the analyzer's
    /// <c>invokesThroughDotInvoke</c> flag; MG drops the flag and the call is typed Object, so the `Void` message is gone.
    /// </summary>
    [Test]
    public void ADotInvokeOnAnAction_IsTypedVoid_AndLeavesNoDeadTemp()
    {
        const string statements = """
            Sub Main()
                Dim a As Action = Sub() Console.WriteLine("hi")
                a.Invoke()
                Dim b As Action(Of String) = Sub(s As String) Console.WriteLine("b " & s)
                b.Invoke("x")
                Dim c As Action(Of Integer, Integer) = Sub(x As Integer, y As Integer) Console.WriteLine(x + y)
                c.Invoke(2, 3)
            End Sub
            """;
        var wantsAValue = new[]
        {
            ("a bare Action", "Dim a As Action = Sub() Console.WriteLine(\"hi\")", "a.Invoke()"),
            ("an Action(Of String)", "Dim a As Action(Of String) = Sub(s As String) Console.WriteLine(s)", "a.Invoke(\"x\")"),
        };

        // The statement leaves no result temp typed Action behind. A call to a Sub produces no value, so a temp that holds one is dead: declared, never read. MG leaves one, `std::function<void()> t2` in C++ and
        // `class [mscorlib]System.Action V_4` in MSIL. (⚠ Not the typed Action(Of String): that kind keeps a dead temp already, in the `d(...)` spelling as well — it is pre-existing, and harmless.)
        // ⚠ Generated BEFORE the assertions, and every assertion shares ONE Assert.Multiple: a first block that fails ends the test, and a second one after it would never run.
        const string bare = """
            Sub Main()
                Dim a As Action = Sub() Console.WriteLine("hi")
                a.Invoke()
            End Sub
            """;
        var texts = new[] { false, true }.Select(optimize => (Optimize: optimize, Cpp: GeneratedText(bare, "cpp", optimize), Il: GeneratedText(bare, "msil", optimize))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Analyze(statements), Is.Empty, "S10: a statement; got " + Said(Analyze(statements)));
            foreach (var (id, declaration, call) in wantsAValue)
            {
                var errors = Analyze($"Sub Main()\n    {declaration}\n    Dim r As Integer = {call}\nEnd Sub\n");
                Assert.That(errors.Select(e => e.Message).ToList(), Has.Some.Contain("Cannot assign value of type 'Void' to variable of type 'Integer'"), $"{id}: typed Void; got " + Said(errors));
                Assert.That(errors, Has.Count.EqualTo(1), $"{id}: one diagnostic; got " + Said(errors));
            }

            foreach (var (optimize, cpp, il) in texts)
            {
                var body = Regex.Match(cpp, @"^void Main\(\)\r?\n\{.*?^\}", RegexOptions.Singleline | RegexOptions.Multiline).Value;
                Assert.That(body, Is.Not.Empty, "the C++ Main was found");
                foreach (Match declared in Regex.Matches(body, @"std::function<void\(\)> (\w+) = \{\};"))
                    Assert.That(Regex.Matches(body, $@"\b{declared.Groups[1].Value}\b").Count, Is.GreaterThan(1),
                        $"C++ (optimize={optimize}): `{declared.Groups[1].Value}` is declared and never read — a dead Action temp\n{body}");

                foreach (Match local in Regex.Matches(il, @"\[(\d+)\] class \[mscorlib\]System\.Action V_\d+"))
                {
                    var n = local.Groups[1].Value;
                    Assert.That(Regex.IsMatch(il, $@"\b(?:ld|st)loc(?:a)?(?:\.s)?\s+{n}\b|\b(?:ld|st)loc\.{n}\b"), Is.True,
                        $"MSIL (optimize={optimize}): local [{n}] (System.Action) is declared and never used — a dead Action temp\n{il}");
                }
            }
        });
    }

    /// <summary>
    /// The text the compiler writes for <c>.Invoke</c> on a Func and on an Action is the CALL: <c>f(5)</c> and <c>a()</c> on C++ and JavaScript, which have no <c>Invoke</c> member on a function value
    /// ("no member named 'Invoke'" from clang, "f.Invoke is not a function" from node), through the standard passes and the optimizing ones. The IR builder reads the same gate the analyzer does (ME puts its
    /// old gate back, so the front end types the call and the lowering emits a method call). MSIL's own `callvirt ...::Invoke` IS the call there and is run by the execution fixture.
    /// </summary>
    [Test]
    public void ADotInvokeOnAFuncOrAction_IsLoweredAsTheCallItIs()
    {
        const string source = """
            Sub Main()
                Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
                Dim r As Integer = f.Invoke(5)
                Dim g As Func(Of String) = Function() "ab"
                Dim s As String = g.Invoke() & "x"
                Dim a As Action = Sub() Console.WriteLine("hi")
                a.Invoke()
                Dim b As Action(Of String) = Sub(t As String) Console.WriteLine(t)
                b.Invoke("y")
                Console.WriteLine(r)
                Console.WriteLine(s)
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var backend in new[] { "cpp", "javascript" })
                foreach (var optimize in new[] { false, true })
                {
                    var text = GeneratedText(source, backend, optimize);
                    Assert.That(text, Does.Not.Contain(".Invoke("), $"{backend} (optimize={optimize}): .Invoke is lowered as the call");
                    Assert.That(text, Does.Contain("f(5)"), $"{backend} (optimize={optimize}): `f.Invoke(5)` is `f(5)`");
                    Assert.That(text, Does.Contain("a()"), $"{backend} (optimize={optimize}): `a.Invoke()` is `a()`");
                    Assert.That(text, Does.Contain("b("), $"{backend} (optimize={optimize}): `b.Invoke(\"y\")` is `b(...)`");
                }
        });
    }

    /// <summary>The program through parse, analysis, the IR builder and, when asked, the STANDARD optimizer (what the CLI runs), as the text of one backend. A refusal fails the test.</summary>
    private static string GeneratedText(string source, string backend, bool optimize)
    {
        var (ast, parser) = Parse(source);
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, Said(analyzer.Errors));
        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        if (optimize)
        {
            var passes = new OptimizationPipeline();
            passes.AddStandardPasses();
            passes.Run(module);
        }

        return UserDelegateGapProbes.Normalised(backend switch
        {
            "cpp" => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module),
            "javascript" => new JavaScriptCodeGenerator().Generate(module),
            "msil" => new MSILCodeGenerator().Generate(module),
            "csharp" => new ImprovedCSharpCodeGenerator().Generate(module),
            _ => throw new ArgumentException(backend),
        });
    }

    /// <summary>
    /// ⛔ A BasicLang CLASS named <c>Action</c> or <c>Func</c> with its own <c>Invoke</c> method keeps it: <c>a.Invoke()</c> is a METHOD call, typed by the method (Integer, here) and not by the delegate
    /// redirect. The first draft of the gate asked only the NAME, and this program went from printing 42 to "Cannot assign value of type 'Void' to variable of type 'Integer'". A declared class carries
    /// <c>DeclaredMemberNames</c>, which the bare .NET <c>Action</c> (a member-less phantom) does not; MF drops that test. On C# the call is still written as a method call. (C++ never compiled a class
    /// named <c>Action</c>, before or after: no C++ cell.)
    /// </summary>
    [Test]
    public void AUserClassNamedActionOrFunc_KeepsItsOwnInvokeMethod()
    {
        var errors = Analyze(UserDelegateGapProbes.S15);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "S15: vbc prints 42 and f3; got " + Said(errors));
            if (errors.Count > 0) return;
            var (ast, _) = Parse(UserDelegateGapProbes.S15);
            var analyzer = new SemanticAnalyzer();
            analyzer.Analyze(ast);
            var csharp = UserDelegateGapProbes.Normalised(new ImprovedCSharpCodeGenerator().Generate(new IRBuilder(analyzer).Build(ast, "TestModule")));
            Assert.That(csharp, Does.Contain("a.Invoke()"), "a method call on the class, not the delegate call a()");
            Assert.That(csharp, Does.Contain("f.Invoke(3)"), "a method call on the class, not the delegate call f(3)");
        });
    }

    // ============================================================================================
    // (2) A Delegate in a sibling file
    // ============================================================================================

    /// <summary>
    /// A Delegate declared in a SIBLING file is visible in every compile order — the field, parameter and local shapes M1, M2 and M3 each way round (a `Dim j As Joiner = Function(...)` was "Cannot assign value
    /// of type 'Func' to variable of type 'Joiner'" when its file came second), and the THREE-file project M4 in all six: a sibling CLASS whose field, parameter and return are typed by a sibling Delegate, with Main
    /// in the third file. Each through <c>CompileProjectFiles</c> (which stops at the combined IR, so no process). MB removes pass 1b.
    /// </summary>
    [Test]
    public void ASiblingFilesDelegate_IsVisibleInEveryCompileOrder()
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in UserDelegateGapProbes.Shapes)
                foreach (var shape in new[] { probe, UserDelegateGapProbes.Reversed(probe) })
                {
                    var result = CompileProject(shape.Files);
                    Assert.That(result.HasErrors, Is.False, $"{shape.Id} ({string.Join(", ", shape.Files.Select(f => f.Name))}): " + Said(result.AllErrors));
                }

            foreach (var order in Permutations(UserDelegateGapProbes.M4.Files))
            {
                var files = order.ToArray();
                var result = CompileProject(files);
                Assert.That(result.HasErrors, Is.False, $"M4 ({string.Join(", ", files.Select(f => f.Name))}): " + Said(result.AllErrors));
            }
        });
    }

    /// <summary>
    /// The editor sees it too: the LSP's project collector (<c>LspModuleSymbolCollector</c>) exports a Delegate as the compiler does, so a project of M1, M2 and M3 produces NO diagnostic in either document, with
    /// the project's files listed either way round and either document opened first. Without the collector's case the editor said "Cannot assign value of type 'Func' to variable of type 'Joiner'" while the build
    /// ran the program (MC disables it). A fresh <c>DocumentManager</c> for each open, so no cached analysis carries over.
    /// </summary>
    [Test]
    public void TheLspProjectCollector_SeesASiblingFilesDelegate_InEitherDocumentOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "bl-t202-lsp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Multiple(() =>
            {
                foreach (var probe in UserDelegateGapProbes.Shapes)
                    foreach (var listed in new[] { probe, UserDelegateGapProbes.Reversed(probe) })
                        foreach (var opened in probe.Files)
                        {
                            var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(dir);
                            foreach (var (name, source) in listed.Files) File.WriteAllText(Path.Combine(dir, name), source);
                            File.WriteAllText(Path.Combine(dir, "P.blproj"),
                                "<BasicLangProject>\n  <PropertyGroup><ProjectName>P</ProjectName><OutputType>Exe</OutputType><TargetBackend>CSharp</TargetBackend></PropertyGroup>\n  <ItemGroup>"
                                + string.Concat(listed.Files.Select(f => $"<Compile Include=\"{f.Name}\" />")) + "</ItemGroup>\n</BasicLangProject>\n");

                            var path = Path.Combine(dir, opened.Name);
                            var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), opened.Source);
                            var label = $"{probe.Id}, listed {string.Join(",", listed.Files.Select(f => f.Name))}, opened {opened.Name}";
                            Assert.That(state.ProjectContext?.ProjectFilePath, Does.EndWith("P.blproj"), $"{label}: the document belongs to the project");
                            Assert.That(state.Diagnostics, Is.Empty, $"{label}: " + string.Join(" | ", state.Diagnostics.Select(d => $"{d.Severity}({d.Line},{d.Column}): {d.Message}")));
                        }
            });
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* temp */ } }
    }
}
