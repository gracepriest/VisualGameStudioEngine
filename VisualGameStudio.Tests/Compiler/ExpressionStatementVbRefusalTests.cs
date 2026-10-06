using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #267 — VB accepts an expression as a statement only when it is an INVOCATION. The analyzer now refuses the rest with vbc's own numbers.
//
//    BC30035 "Syntax error."  a statement that BEGINS with `New` or `(`:  New C()   New C(F())   New C().M()   (F())
//    BC30545 "Property access must assign to the property or use its value."  a property read:  b.P   Me.P   MyBase.P   a bare P   b.P()   l.Item(0)   Box.S
//    BC30057 "Too many arguments to 'X'."  arguments handed to a property that takes none:  b.Items(0)
//    BC30454 "Expression is not a method."  any other value: a field / local / parameter / constant read (b.K  Make().K  x), an array element or indexer (a(5)  l(F())  d("k")  MakeList()(0)),
//            a cast (CType  DirectCast  TryCast  CInt(x) and its eleven siblings)
//
//  Before, only a literal, a tuple or an operator expression on a line of its own was refused (`SemanticAnalyzer.IsValueOnlyExpression`), so every shape above compiled and the backends disagreed about what
//  it meant: C# DROPPED the statement and any call inside it (`New Box(Tag())` never called Tag), JavaScript dropped more, C++ and MSIL ran them. `SemanticAnalyzer.NonInvocationStatement` decides by what the
//  expression was ANALYZED as, never by its spelling: its node kind, the symbol it bound, and whether `Visit(CallExpressionNode)` typed it as an element read (`_elementReadCalls`). It is permissive wherever the
//  analyzer does not know (a .NET member binds no symbol; an unresolved name binds none), and for a delegate-typed value, which VB INVOKES.
//
//  ⭐ THE ORACLE IS vbc. Every row below is a program the test-writer compiled with the SDK's vbc (the program wrapped in a VB Module, `S/t267/tw/rows-matrix.txt`: one line per row, the vbc code beside
//  BasicLang's code and line). Where a row says BCnnnnn, vbc said BCnnnnn for that statement and for nothing else; where a row is ACCEPTED, vbc ran it. The probe ids are the implementer's (w1-w15 from
//  #139's sweep, c*, r*), so S/t267/allprobes finds the original; ids beginning x_, d_ or n_ are the test-writer's. ⚠ Four rows have NO vbc oracle: `a[5]`, `l[0]` and `fs[0]` are BasicLang's own bracket
//  spelling of an element read (vbc: BC30203, not VB syntax), refused by the same rule as `a(5)`; and UserCInt (below). ⚠ vbc reports no binding error next to a SYNTAX error (BC30035, BC30203): a probe
//  that holds one says nothing about its other statements, so the delegate rows (`fs(0)`, `fl(0)`, `dd("k")`, `b.CbP`, `fl.Item(0)` refused; `f`, `b.Cb`, `Make().Cb` accepted) were measured one statement per program. The refused rows are NOT whole programs: they are ONE shared program (the `Prelude` below) with the statement under test written into a
//  marked line, so the expected line is the marker's line counted off the template text, never read back from the analyzer.
//
//  This fixture is the fast, front-end half: parse + analyze, `CompileProjectFiles` (which stops at the combined IR) and the LSP. `ExpressionStatementVbRefusalExecutionTests` (Integration, below) RUNS accepted
//  shapes on C#, JavaScript and C++ and drives the real CLI for the refusals, so the refusal is shown not to over-reach and not to depend on the target.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Every one was measured against vbc on the fixed build.
//    1. `b.CbP()` — a delegate-typed PROPERTY called with parentheses: vbc says BC30545, BasicLang ACCEPTS it, because the call goes through `_delegateMemberInvocations` (the delegate-member call path)
//       before the property check. The same property written WITHOUT parentheses (`b.CbP`, and `CbP` inside the class) IS a row; `b.Cb()` (a delegate FIELD) is accepted by both.
//    2. .NET members and .NET-typed values bind no symbol, so these stay ACCEPTED and vbc refuses them: `l.Count` / `s.Length` / `l.Count()` (BC30545), a local of a .NET class or of DateTime standing
//       alone (BC30454). The analyzer has no .NET property facts (#222 family).
//    3. `CDec(x)`, `CChar(s)`, `CObj(x)`, `CDate(s)` (vbc BC30454) stay accepted: nothing registers them as intrinsics, so no symbol says they are casts. The other twelve are rows.
//    4. `Color.Red` (an enum member binds no symbol). `TypeOf o Is Animal` keeps its generic refusal ("Expression is not a statement"), not vbc's BC30035.
//    5. Side findings, NOT this fix and NOT pinned by any row: C# drops a parameterless call written without parentheses (`Tag`, `b.Bump`); a delegate `f` standing alone prints 0 on all four backends
//       (vbc invokes it); `Call F()` is miscompiled (`Call(F())`); the `Property Cell(i As Integer)` form does not parse, so BC30545 on a property WITH parameters has no row.
//
//  ⭐ MUTANTS (each the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    M1  the BC30035 check removed (the whole `if`) ................................ w1, w2, c5, r1 (+ x_new_field, x_single_line_if, x_inside_try), and the LSP, project and CLI rows
//    M1b only a DIRECT `New` refused (no walk down the receiver chain) ............. c5 (`New Box().Bump()`) and x_new_field (`New Box().K`)
//    M1c the parenthesis flag not honoured (`BeginsWithParenthesis` ignored) ........ r1 (`(Tag())`)
//    M2  the delegate exemption removed (`MayBeDelegateValue` is `type == null`) ..... the accepted rows `f` (d_local_action), `h` (d_user_delegate_type), `fn` (d_local_func), `b.Cb` (d_delegate_field),
//                                                                                    `Make().Cb` (d_field_of_a_call), `f()` (d_call_with_parentheses), `fi(3)`, the delegate PARAMETER `act`, the
//                                                                                    implicit-Me field `Cb`, the controls w12 and c1, and the execution row (a delegate called as a statement)
//    M3  the element-read record removed (`_elementReadCalls`) ....................... r7 `l.Item(0)`, `fl.Item(0)`, r13 `MakeList().Item(0)` and `MakeList()(0)`
//    M4  a WriteOnly property read no longer exempt .................................. r15 (BC30545 reported beside BC30524)
//    M5  conversion operators recognised by NAME instead of by symbol ................. UserCInt (a user's own `Function CInt` becomes BC30454)
//    M6  a Constant is no longer a value symbol ....................................... r3_constant
//    M7  the bracket element read (`ArrayAccessExpressionNode`) exempt ................ x_bracket_array_element, x_bracket_list_indexer, x_bracket_delegate_element
//    M8  a delegate-typed PROPERTY exempt again (the delegate test before the property test) .. n_delegate_typed_property (`b.CbP`), n_delegate_typed_property_implicit_me (`CbP` in the class)
//    M9  the bracket element read exempt when its type may be a delegate ................ x_bracket_delegate_element (`fs[0]`)
//    M10 an element read exempt when its type may be a delegate ...................... n_delegate_array_element (`fs(0)`), n_list_of_delegates_indexer (`fl(0)`), n_dictionary_of_delegates_indexer
//                                                                                    (`dd("k")`), n_list_of_delegates_item (`fl.Item(0)`)
//  M1-M3 are the three the task named; the rest are one change each in the other branches of `NonInvocationStatement`. M8-M10 are the delegate exemption of the first #267 commit, which accepted `fs(0)`,
//  `fl(0)`, `dd("k")`, `b.CbP` and `fl.Item(0)`: vbc refuses all five, and the amended commit does too.
// ================================================================================================

/// <summary>One probe: a whole program, and the 1-based line the diagnostic belongs on (0 when none is expected).</summary>
public sealed record StatementProbe(string Id, string Source, int Line)
{
    public override string ToString() => Id;
}

/// <summary>A test case's probes and the ONE diagnostic code each must report (null: each must report nothing at all).</summary>
public sealed record StatementGroup(string Id, string? Code, params StatementProbe[] Probes)
{
    public override string ToString() => Id;
}

[TestFixture]
public class ExpressionStatementVbRefusalTests
{
    // ============================================================================================
    // The shared program. Each refused row is this text with ONE statement written over a marker line.
    // ============================================================================================

    /// <summary>
    /// Everything a row can name: a class with a field, an array, a List, a property (Get only, Set only, Shared), a delegate field, a delegate-typed property and a Shared field; a class that inherits it; a Structure, a user Delegate,
    /// a module variable and constant, a Sub with a parameter, a generic Sub and a Sub with a delegate parameter; and a Main holding a local of every kind a bare name can have (an Action, a user delegate, a Func, an array, a List and a Dictionary of delegates). The markers are the only places a row writes.
    /// </summary>
    internal const string Prelude = """
        Delegate Sub Handler()

        Structure Pt
            Public X As Integer
        End Structure

        Class Box
            Public K As Integer
            Public Arr(3) As Integer
            Public L As List(Of Integer)
            Public Hits As Integer
            Public Cb As Action
            Public Shared Made As Integer
            Public Sub New()
                L = New List(Of Integer)()
                Cb = Sub() Console.WriteLine("cb")
            End Sub
            Public Sub New(v As Integer)
                K = v
                L = New List(Of Integer)()
                Cb = Sub() Console.WriteLine("cb")
            End Sub
            Public Sub Bump()
                Hits = Hits + 1
            End Sub
            Public ReadOnly Property P As Integer
                Get
                    Hits = Hits + 1
                    Return 5
                End Get
            End Property
            Public ReadOnly Property Items As List(Of Integer)
                Get
                    Return L
                End Get
            End Property
            Public ReadOnly Property ArrP As Integer()
                Get
                    Return Arr
                End Get
            End Property
            Public ReadOnly Property CbP As Action
                Get
                    Return Cb
                End Get
            End Property
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                    K = value
                End Set
            End Property
            Public Shared ReadOnly Property S As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Sub Work()
                ' @MEMBER
            End Sub
        End Class

        Class Derived
            Inherits Box
            Public Sub Probe()
                ' @DERIVED
            End Sub
        End Class

        Function Tag() As Integer
            Console.WriteLine("tag")
            Return 1
        End Function

        Function Make() As Box
            Return New Box()
        End Function

        Function MakeList() As List(Of Integer)
            Dim r As New List(Of Integer)()
            r.Add(1)
            Return r
        End Function

        Dim G As Integer = 3
        Const Limit As Integer = 4

        Sub Use(p As Integer)
            ' @PARAM
        End Sub

        Sub Gen(Of T)(v As T)
            ' @GENERIC
        End Sub

        Sub Run(act As Action)
            ' @ACTPARAM
        End Sub

        Sub Main()
            Dim b As New Box()
            Dim x As Integer = 5
            Dim o As Object = 5
            Dim s As String = "a"
            Dim a(2) As Integer
            Dim l As New List(Of Integer)()
            l.Add(7)
            Dim d As New Dictionary(Of String, Integer)()
            Dim pt As Pt
            Dim f As Action = Sub() Console.WriteLine("f")
            Dim h As Handler = Sub() Console.WriteLine("h")
            Dim fn As Func(Of Integer) = Function() 7
            Dim fs(1) As Action
            fs(0) = Sub() Console.WriteLine("fs0")
            Dim fl As New List(Of Action)()
            fl.Add(Sub() Console.WriteLine("fl0"))
            Dim dd As New Dictionary(Of String, Action)()
            dd("k") = Sub() Console.WriteLine("dd")
            Dim fi As Action(Of Integer) = Sub(n) Console.WriteLine("fi" & n)
            ' @MAIN
            Console.WriteLine("end")
        End Sub
        """;

    /// <summary>The program with <paramref name="statement"/> written over the marker of <paramref name="place"/> (MAIN, MEMBER, DERIVED, PARAM, GENERIC or ACTPARAM), and the 1-based line of <paramref name="target"/> (default: the statement's last line).</summary>
    internal static StatementProbe At(string id, string place, string statement, string? target = null)
    {
        var lines = Prelude.Replace("\r\n", "\n").Split('\n').ToList();
        var marker = lines.FindIndex(l => l.Trim() == "' @" + place);
        if (marker < 0) throw new ArgumentException($"{id}: no marker {place}");
        var indent = lines[marker][..(lines[marker].Length - lines[marker].TrimStart().Length)];
        var body = statement.Split('\n').Select(l => l.StartsWith(' ') ? l : indent + l).ToList();
        var which = target == null ? body.Count - 1 : body.FindIndex(l => l.Trim() == target);
        if (which < 0) throw new ArgumentException($"{id}: target `{target}` is not a line of the statement");
        lines.RemoveAt(marker);
        lines.InsertRange(marker, body);
        return new StatementProbe(id, string.Join("\n", lines), marker + 1 + which);
    }

    /// <summary>A probe that is a whole program of its own (written verbatim from the implementer's probe).</summary>
    private static StatementProbe Whole(string id, string source) => new(id, source, 0);

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a refusal) and analyze; every diagnostic, in the analyzer's order.</summary>
    internal static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    private static string Said(IEnumerable<SemanticError> errors)
        => errors.Any() ? string.Join(" | ", errors.Select(e => $"{e.Line}:{e.Column} {e.ErrorCode} {e.Message}")) : "no diagnostic";

    // ============================================================================================
    // Verbatim programs (accepted by vbc, run by it: the `.exp` beside each probe in S/t267/allprobes)
    // ============================================================================================

    /// <summary>c1_calls: every call shape VB accepts as a statement; run by vbc: a|tag|tag|4 31|7|1||1y.</summary>
    private const string C1_Calls = """
        ' CONTROL: every call shape VB accepts as a statement (Sub, discarded Function, obj.M(), parameterless without parens, .NET calls)
        Class Box
            Public Hits As Integer
            Public Sub Bump()
                Hits = Hits + 1
            End Sub
            Public Function Twice() As Integer
                Hits = Hits + 1
                Return Hits
            End Function
        End Class

        Class Base
            Public N As Integer
            Public Overridable Sub Work()
                N = N + 1
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Work()
                MyBase.Work()
                Me.Helper()
                Helper()
                Helper
            End Sub
            Public Sub Helper()
                N = N + 10
            End Sub
        End Class

        Sub Say(s As String)
            Console.WriteLine(s)
        End Sub

        Function Tag() As Integer
            Console.WriteLine("tag")
            Return 1
        End Function

        Sub Main()
            Say("a")
            Tag()
            Tag
            Dim b As New Box()
            b.Bump()
            b.Bump
            b.Twice()
            b.Twice
            Dim d As New Derived()
            d.Work()
            Console.WriteLine(b.Hits & " " & d.N)
            Dim n As Integer = 0
            Dim f As Action(Of Integer) = Sub(x) n = n + x
            f(3)
            f.Invoke(4)
            Dim g As Func(Of Integer, Integer) = Function(x) x + 1
            g(10)
            Console.WriteLine(n)
            Console.WriteLine(1)
            Console.WriteLine
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Clear
            l.Add(2)
            l.Clear()
            l.Add(3)
            Dim sb As New System.Text.StringBuilder()
            sb.Append("x")
            sb.Clear
            sb.Append("y")
            Console.WriteLine(l.Count & sb.ToString())
        End Sub
        """;

    /// <summary>w12_delegate: delegate invocations f(3), f.Invoke(4), g(10), g.Invoke(100); vbc: 117.</summary>
    private const string W12_Delegate = """
        ' Delegate invokes at statement level: f(3), f.Invoke(3), and a Func result discarded
        Sub Main()
            Dim n As Integer = 0
            Dim f As Action(Of Integer) = Sub(x) n = n + x
            f(3)
            f.Invoke(4)
            Dim g As Func(Of Integer, Integer) = Function(x)
                                                     n = n + x
                                                     Return n
                                                 End Function
            g(10)
            g.Invoke(100)
            Console.WriteLine(n)
        End Sub
        """;

    /// <summary>w13_event: RaiseEvent and AddHandler; vbc: 10.</summary>
    private const string W13_Event = """
        ' RaiseEvent, AddHandler, and a method call through a field at statement level
        Class Src
            Public Event Ping(n As Integer)
            Public Sub Fire()
                RaiseEvent Ping(5)
            End Sub
        End Class

        Class Sink
            Public Total As Integer
            Public Sub OnPing(n As Integer)
                Total = Total + n
            End Sub
        End Class

        Sub Main()
            Dim s As New Src()
            Dim k As New Sink()
            AddHandler s.Ping, AddressOf k.OnPing
            s.Fire()
            s.Fire()
            Console.WriteLine(k.Total)
        End Sub
        """;

    /// <summary>c3_await: Await at statement level and a Task discarded through .Wait(); vbc: work 1, work 2, end.</summary>
    private const string C3_Await = """
        ' CONTROL: Await at statement level, and an Async Function's Task discarded through .Wait()
        Async Function Work(n As Integer) As Task(Of Integer)
            Console.WriteLine("work " & n)
            Await Task.Delay(1)
            Return n
        End Function

        Async Function Run() As Task
            Await Work(1)
            Work(2).Wait()
        End Function

        Sub Main()
            Run().Wait()
            Console.WriteLine("end")
        End Sub
        """;

    /// <summary>r2_tostring: x.ToString with and without parentheses; vbc: end.</summary>
    private const string R2_ToString = """
        ' x.ToString with and without parentheses at statement level
        Sub Main()
            Dim x As Integer = 5
            x.ToString
            x.ToString()
            Console.WriteLine("end")
        End Sub
        """;

    /// <summary>w14_linq: a LINQ chain whose terminal call has a side effect; the probe's own `l.Count()` line is NOT here (vbc refuses it, BasicLang accepts it: known gap 2).</summary>
    private const string W14_Linq = """
        ' A LINQ query / method chain at statement level whose terminal call has a side effect
        Function Tag(x As Integer) As Integer
            Console.WriteLine("tag " & x)
            Return x
        End Function

        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            l.Select(Function(x As Integer) Tag(x)).ToList()
            Console.WriteLine("end")
        End Sub
        """;

    /// <summary>w15_await: Await of a Task and an Async Function's result discarded; vbc: work 1, work 2, end.</summary>
    private const string W15_Await = """
        ' Await of a Task at statement level, and an Async Function result discarded
        Using System.Threading.Tasks

        Async Function Work(n As Integer) As Task(Of Integer)
            Console.WriteLine("work " & n)
            Await Task.Delay(1)
            Return n
        End Function

        Async Function Run() As Task
            Await Work(1)
            Work(2).Wait()
        End Function

        Sub Main()
            Run().Wait()
            Console.WriteLine("end")
        End Sub
        """;

    /// <summary>
    /// A user's own Function named like a conversion intrinsic. NOT a vbc probe: `CInt` is a reserved word in VB and cannot name a Function there. It pins the design point the fix states — the
    /// conversion operators are recorded by SYMBOL, so a user's Function that shadows one is a CALL — against the mutant that recorded them by spelling.
    /// </summary>
    private const string UserCInt = """
        Function CInt(n As Integer) As Integer
            Console.WriteLine("mine")
            Return n
        End Function

        Sub Main()
            CInt(3)
            Console.WriteLine("end")
        End Sub
        """;

    // ============================================================================================
    // The rows — one test case each; a case holding several probes names each in its failure message.
    // ============================================================================================

    private static IEnumerable<TestCaseData> RefusedRows()
    {
        yield return new TestCaseData(new StatementGroup("A_statement_that_begins_with_New_or_a_parenthesis__w1_w2_c5_r1", "BC30035",
            At("w1_newobj", "MAIN", "New Box()"),
            At("w2_newobjarg", "MAIN", "New Box(Tag())"),
            At("c5_newcall", "MAIN", "New Box().Bump()"),
            At("r1_paren", "MAIN", "(Tag())"),
            At("x_new_field", "MAIN", "New Box().K"),
            At("x_single_line_if", "MAIN", "If x > 0 Then New Box()"),
            At("x_inside_try", "MAIN", "Try\n        New Box()\n    Catch ex As Exception\n    End Try", "New Box()")))
            .SetName("Refused_BC30035_A_statement_that_begins_with_New_or_a_parenthesis__M1_w1_w2_c5_r1");
        yield return new TestCaseData(new StatementGroup("A_property_read__w3_r4_w11_r7_r13_r15", "BC30545",
            At("w3_propget", "MAIN", "b.P"),
            At("r4_implicit_me_property", "MEMBER", "P"),
            At("r4_property_with_parentheses", "MAIN", "b.P()"),
            At("w11_me_property", "MEMBER", "Me.P"),
            At("w11_mybase_property", "DERIVED", "MyBase.P"),
            At("r7_list_item", "MAIN", "l.Item(0)"),
            At("r13_call_receiver_item", "MAIN", "MakeList().Item(0)"),
            At("r15_shared_property", "MAIN", "Box.S"),
            At("x_property_returning_a_list", "MAIN", "b.Items"),
            At("x_property_of_a_call", "MAIN", "Make().P"),
            At("n_delegate_typed_property", "MAIN", "b.CbP"),
            At("n_delegate_typed_property_implicit_me", "MEMBER", "CbP"),
            At("n_list_of_delegates_item", "MAIN", "fl.Item(0)"),
            At("x_single_line_if", "MAIN", "If x > 0 Then b.P"),
            At("x_single_line_sub_lambda", "MAIN", "Dim act As Action = Sub() b.P"),
            At("x_inside_for", "MAIN", "For i As Integer = 1 To 2\n        b.P\n    Next", "b.P")))
            .SetName("Refused_BC30545_A_property_read_a_delegate_typed_one_too_and_the_explicit_Item__M3_M8_w3_r4_w11_r7_r13_r15_n");
        yield return new TestCaseData(new StatementGroup("Arguments_to_a_property_that_takes_none__r13", "BC30057",
            At("r13_property_returning_a_list", "MAIN", "b.Items(0)"),
            At("r13_property_returning_an_array", "MAIN", "b.ArrP(0)"),
            At("x_property_of_a_call", "MAIN", "Make().Items(0)")))
            .SetName("Refused_BC30057_Arguments_to_a_property_that_takes_none__r13");
        yield return new TestCaseData(new StatementGroup("A_field_local_parameter_or_constant_read__w10_w7_r3_r4_r11_r12", "BC30454",
            At("w10_field", "MAIN", "b.K"),
            At("w7_field_of_a_call", "MAIN", "Make().K"),
            At("r3_local", "MAIN", "x"),
            At("r3_constant", "MAIN", "Limit"),
            At("r3_module_variable", "MAIN", "G"),
            At("r3_parameter", "PARAM", "p"),
            At("r11_shared_field", "MAIN", "Box.Made"),
            At("r4_implicit_me_field", "MEMBER", "Hits"),
            At("r4_me_field", "MEMBER", "Me.Hits"),
            At("r12_object", "MAIN", "o"),
            At("r12_string", "MAIN", "s"),
            At("r12_array", "MAIN", "a"),
            At("r12_list", "MAIN", "l"),
            At("r12_structure", "MAIN", "pt"),
            At("r12_class", "MAIN", "b"),
            At("r12_type_parameter", "GENERIC", "v")))
            .SetName("Refused_BC30454_A_field_local_parameter_or_constant_read__w10_w7_r3_r4_r11_r12");
        yield return new TestCaseData(new StatementGroup("An_array_element_or_indexer_read__w6_w5_w4_r13", "BC30454",
            At("w6_array_element", "MAIN", "a(5)"),
            At("w5_list_indexer_with_a_call", "MAIN", "l(Tag())"),
            At("w4_dictionary_indexer", "MAIN", "d(\"k\")"),
            At("r13_indexer_of_a_call", "MAIN", "MakeList()(0)"),
            At("r13_array_field_element", "MAIN", "b.Arr(0)"),
            At("r13_list_field_indexer", "MAIN", "b.L(0)"),
            At("r13_call_on_a_field", "MAIN", "b.K()"),
            At("n_delegate_array_element", "MAIN", "fs(0)"),
            At("n_list_of_delegates_indexer", "MAIN", "fl(0)"),
            At("n_dictionary_of_delegates_indexer", "MAIN", "dd(\"k\")"),
            At("x_bracket_delegate_element", "MAIN", "fs[0]"),
            At("x_bracket_array_element", "MAIN", "a[5]"),
            At("x_bracket_list_indexer", "MAIN", "l[0]"),
            At("x_inside_select_case", "MAIN", "Select Case x\n        Case 5\n            a(1)\n    End Select", "a(1)")))
            .SetName("Refused_BC30454_An_array_element_or_indexer_read_a_delegate_element_too__M3_M7_M10_w6_w5_w4_r13_n");
        yield return new TestCaseData(new StatementGroup("A_cast__w9_r6", "BC30454",
            At("w9_ctype", "MAIN", "CType(Tag(), Object)"),
            At("r6_directcast", "MAIN", "DirectCast(o, String)"),
            At("r6_trycast", "MAIN", "TryCast(o, String)"),
            At("r6_cbool", "MAIN", "CBool(x)"),
            At("r6_cbyte", "MAIN", "CByte(x)"),
            At("r6_cdbl", "MAIN", "CDbl(x)"),
            At("r6_cint", "MAIN", "CInt(x)"),
            At("r6_clng", "MAIN", "CLng(x)"),
            At("r6_csbyte", "MAIN", "CSByte(x)"),
            At("r6_cshort", "MAIN", "CShort(x)"),
            At("r6_csng", "MAIN", "CSng(x)"),
            At("r6_cstr", "MAIN", "CStr(x)"),
            At("r6_cuint", "MAIN", "CUInt(x)"),
            At("r6_culng", "MAIN", "CULng(x)"),
            At("r6_cushort", "MAIN", "CUShort(x)")))
            .SetName("Refused_BC30454_A_cast_CType_DirectCast_TryCast_and_the_twelve_conversion_operators__w9_r6");
        yield return new TestCaseData(new StatementGroup("A_WriteOnly_property_read__r15", "BC30524",
            At("r15_writeonly", "MAIN", "b.W")))
            .SetName("Refused_BC30524_A_WriteOnly_property_read_is_left_to_BC30524_and_never_BC30545__r15");
    }

    private static IEnumerable<TestCaseData> AcceptedRows()
    {
        yield return new TestCaseData(new StatementGroup("A_delegate_VARIABLE_PARAMETER_or_FIELD_is_invoked__d_n_w12", null,
            At("d_local_action", "MAIN", "f"),
            At("d_user_delegate_type", "MAIN", "h"),
            At("d_local_func", "MAIN", "fn"),
            At("d_delegate_field", "MAIN", "b.Cb"),
            At("d_field_of_a_call", "MAIN", "Make().Cb"),
            At("d_invoke_member", "MAIN", "f.Invoke"),
            At("d_call_with_parentheses", "MAIN", "f()"),
            At("d_array_element_invoked", "MAIN", "fs(0)()"),
            At("n_list_element_invoked", "MAIN", "fl(0)()"),
            At("n_delegate_called_with_an_argument", "MAIN", "fi(3)"),
            At("n_invoke_with_an_argument", "MAIN", "fi.Invoke(4)"),
            At("n_field_called_with_parentheses", "MAIN", "b.Cb()"),
            At("n_implicit_me_delegate_field", "MEMBER", "Cb"),
            At("n_delegate_parameter", "ACTPARAM", "act"),
            Whole("w12_delegate", W12_Delegate)))
            .SetName("Accepted_A_delegate_VARIABLE_PARAMETER_or_FIELD_is_invoked__M2_d_n_w12");
        yield return new TestCaseData(new StatementGroup("Sub_and_Function_calls_with_and_without_parentheses__c1", null,
            Whole("c1_calls", C1_Calls)))
            .SetName("Accepted_Sub_and_Function_calls_with_and_without_parentheses_Me_MyBase_and_dotNET__c1");
        yield return new TestCaseData(new StatementGroup("RaiseEvent_AddHandler_Await_ToString_LINQ__w13_c3_r2_w14_w15", null,
            Whole("w13_event", W13_Event),
            Whole("c3_await", C3_Await),
            Whole("r2_tostring", R2_ToString),
            Whole("w14_linq", W14_Linq),
            Whole("w15_await", W15_Await)))
            .SetName("Accepted_RaiseEvent_AddHandler_Await_x_ToString_and_a_LINQ_chain__w13_c3_r2_w14_w15");
        yield return new TestCaseData(new StatementGroup("A_users_own_Function_named_like_a_conversion_operator__UserCInt", null,
            Whole("UserCInt", UserCInt)))
            .SetName("Accepted_A_users_own_Function_named_like_a_conversion_operator_is_a_call__UserCInt");
    }

    /// <summary>
    /// Each probe is refused with vbc's number, with NOTHING else, exactly once, at the line of the statement, and as an Error whose message starts with that number. The line is counted off the template, not
    /// read from the analyzer.
    /// </summary>
    [TestCaseSource(nameof(RefusedRows))]
    public void AnExpressionStatementVbRefuses_IsRefused_WithVbcsNumber_AtItsLine(StatementGroup group)
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in group.Probes)
            {
                var errors = Analyze(probe.Source);
                Assert.That(errors.Select(e => (e.ErrorCode, e.Line)).ToArray(), Is.EqualTo(new[] { (group.Code, probe.Line) }),
                    $"{probe.Id}: exactly {group.Code} at line {probe.Line}; got {Said(errors)}");
                Assert.That(errors.Select(e => e.Message), Has.All.StartWith(group.Code + ":"), $"{probe.Id}: the message names its number");
                Assert.That(errors.Select(e => e.Severity), Has.All.EqualTo(ErrorSeverity.Error), $"{probe.Id}: an Error");
            }
        });
    }

    /// <summary>
    /// Each probe is ACCEPTED: no diagnostic of any kind. Every one is a statement vbc runs (the user's-own-CInt row is the one exception: vbc cannot name a Function CInt, so it has no oracle).
    /// </summary>
    [TestCaseSource(nameof(AcceptedRows))]
    public void AStatementVbAccepts_IsStillAccepted(StatementGroup group)
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in group.Probes)
            {
                var errors = Analyze(probe.Source);
                Assert.That(errors, Is.Empty, $"{probe.Id}: vbc accepts it; got {Said(errors)}");
            }
        });
    }

    // ============================================================================================
    // Both entry points, and the LSP
    // ============================================================================================

    private const string TypesFile = """
        Class Crate
            Public K As Integer
            Public Hits As Integer
            Public L As List(Of Integer)
            Public Sub New()
                L = New List(Of Integer)()
            End Sub
            Public Sub Bump()
                Hits = Hits + 1
            End Sub
            Public ReadOnly Property P As Integer
                Get
                    Return 5
                End Get
            End Property
            Public ReadOnly Property Items As List(Of Integer)
                Get
                    Return L
                End Get
            End Property
        End Class
        """;

    private const string ProgramFile = """
        Sub Main()
            Dim c As New Crate()
            Dim a(2) As Integer
            c.Bump()
            New Crate()
            c.P
            c.Items(0)
            c.K
            a(5)
            c.Bump
            Console.WriteLine("end")
        End Sub
        """;

    private static int LineOf(string source, string text) => source.Replace("\r\n", "\n").Split('\n').ToList().FindIndex(l => l.Trim() == text) + 1;

    /// <summary>
    /// <c>BasicCompiler.CompileProjectFiles</c> (what a .blproj build and the IDE call; it stops at the combined IR, so no process) refuses one shape of EACH code family — and accepts the call written
    /// beside them — when the class lives in ANOTHER file of the project, in either compile order. The single-file rows above never go through it: a project compile analyzes a file inside a unit, with the
    /// sibling file's symbols imported, a different path from the bare Parser + SemanticAnalyzer pair.
    /// </summary>
    [Test]
    public void TheProjectEntryPoint_RefusesOneShapeOfEveryFamily_ForAClassInAnotherFile()
    {
        var expected = new[]
        {
            ("BC30035", LineOf(ProgramFile, "New Crate()")),
            ("BC30545", LineOf(ProgramFile, "c.P")),
            ("BC30057", LineOf(ProgramFile, "c.Items(0)")),
            ("BC30454", LineOf(ProgramFile, "c.K")),
            ("BC30454", LineOf(ProgramFile, "a(5)")),
        };

        List<SemanticError> ViaProject(bool typesFirst)
        {
            var dir = Path.Combine(Path.GetTempPath(), "bl-t267-proj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var program = Path.Combine(dir, "Program.bas");
                var types = Path.Combine(dir, "Types.bas");
                File.WriteAllText(program, ProgramFile);
                File.WriteAllText(types, TypesFile);
                var files = typesFirst ? new List<string> { types, program } : new List<string> { program, types };
                return new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(files).AllErrors.ToList();
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
        }

        Assert.Multiple(() =>
        {
            foreach (var typesFirst in new[] { false, true })
            {
                var errors = ViaProject(typesFirst);
                Assert.That(errors.OrderBy(e => e.Line).Select(e => (e.ErrorCode, e.Line)).ToArray(), Is.EqualTo(expected),
                    $"through CompileProjectFiles (Types.bas {(typesFirst ? "first" : "second")}): vbc's number at each statement and nothing for `c.Bump()` / `c.Bump`; got {Said(errors)}");
            }
        });
    }

    /// <summary>
    /// The editor sees it too: <c>DocumentManager</c> (which copies <c>analyzer.Errors</c> into its OWN diagnostic list) surfaces each refusal as an ERROR carrying vbc's number, at the statement's line and at the
    /// column of the expression (the first character of each of these), and says nothing about the call written beside them.
    /// </summary>
    [Test]
    public void Lsp_SurfacesTheRefusal_AsAnError_WithVbcsNumber_AtTheExpression()
    {
        var program = At("lsp", "MAIN", "b.Bump()\n    New Box(Tag())\n    b.P\n    b.Items(0)\n    a(5)\n    b.Bump");
        var lines = program.Source.Split('\n').ToList();
        var first = lines.FindIndex(l => l.Trim() == "New Box(Tag())");
        var state = new DocumentManager().UpdateDocument(DocumentUri.From("untitled:ExpressionStatementProbe.bas"), program.Source);
        var said = string.Join(" | ", state.Diagnostics.Select(d => $"{d.Line}:{d.Column} {d.Severity} {d.Message}"));
        var errors = state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();   // the editor also adds Hints (an unused variable), which are not under test

        var expected = new[] { ("BC30035", first + 1), ("BC30545", first + 2), ("BC30057", first + 3), ("BC30454", first + 4) };
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(expected.Length), "one Error per refused statement and none for `b.Bump()` / `b.Bump`; got: " + said);
            foreach (var (code, line) in expected)
            {
                var d = errors.FirstOrDefault(x => x.Message.Contains(code + ":"));
                Assert.That(d, Is.Not.Null, $"{code} over LSP as an Error; got: {said}");
                if (d == null) continue;
                Assert.That(d.Line, Is.EqualTo(line), $"{code}: the statement's line");
                Assert.That(d.Column, Is.EqualTo(5), $"{code}: the expression's column (four spaces of indent)");
            }
        });
    }
}

// ================================================================================================
//  The Integration half. Spawns the CLI and runs the C#, JavaScript and C++ the compiler writes.
// ================================================================================================

/// <summary>
/// #267, RUN: the refusal does not over-reach, and does not depend on the target.
///
/// <para><b>Accepted shapes still run</b> on C#, JavaScript and C++ (events: C# and JavaScript), through the spawned CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>, printing what vbc prints: the statement forms
/// the backends once disagreed about — a delegate array element INVOKED (<c>fs(0)()</c>), <c>Me.M()</c>, <c>MyBase.M()</c>, a bare <c>Helper()</c>, a discarded Function — and <c>RaiseEvent</c> /
/// <c>AddHandler</c> (C# and JavaScript only: C++ does not compile an <c>Event</c> — `use of undeclared identifier 'Action'` — before and after #267, so it is a side finding with no row). Each is a shape that ALREADY printed vbc's answer on every backend named; the programs are chosen that way (S/t267/tw/ex, vbc's answers beside them).</para>
///
/// <para>⚠ NOT here, because each is a side finding that is not #267's and asserting it would pin a defect: a parameterless call written WITHOUT parentheses (`Tag`, `b.Bump`: C# drops it), a delegate
/// `f` standing alone (prints 0 on every backend; vbc invokes it), `Call F()`, `f.Invoke(x)` and `StringBuilder` (a refusal or a compile error on C++ / JavaScript).</para>
///
/// <para><b>Refused on every target</b>: the real CLI is given one program holding a shape of each code family and must exit non-zero, name all four numbers and write no output file, on the C#, C++,
/// JavaScript, MSIL and LLVM targets, with and without <c>--optimize</c>. No tool beyond the CLI is needed: the refusal comes before any backend.</para>
///
/// A tool that is missing is skipped, never failed; the accepted-shape test is ignored only when no cell could run.
/// ⚠ Named "…ExecutionTests" and runs JavaScript under Node: it is in <c>JsExecutionTierRosterTests</c>' roster.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node and C# child runs share the machine with the spawned CLI
public class ExpressionStatementVbRefusalExecutionTests
{
    private const Bk ThreeBackends = Bk.CSharp | Bk.Cpp | Bk.JavaScript;

    private static readonly TempProbe[] AcceptedProbes =
    {
        // x1_members: Me.M(), MyBase.M(), a bare Helper(), a discarded Function, through a Derived class. vbc: a | tag | 121 | 342
        new TempProbe("x1_members", """
            Class Base
                Public N As Integer
                Public Overridable Sub Work()
                    N = N + 1
                End Sub
            End Class

            Class Derived
                Inherits Base
                Public Overrides Sub Work()
                    MyBase.Work()
                    Me.Helper()
                    Helper()
                End Sub
                Public Sub Helper()
                    N = N + 10
                End Sub
                Public Function Twice() As Integer
                    N = N + 100
                    Return N
                End Function
                Public Sub Both()
                    Me.Twice()
                    Twice()
                    Me.Work()
                End Sub
            End Class

            Sub Say(s As String)
                Console.WriteLine(s)
            End Sub

            Function Tag() As Integer
                Console.WriteLine("tag")
                Return 1
            End Function

            Sub Main()
                Say("a")
                Tag()
                Dim d As New Derived()
                d.Work()
                d.Twice()
                Console.WriteLine(d.N)
                d.Both()
                Console.WriteLine(d.N)
            End Sub
            """, "a\ntag\n121\n342", ThreeBackends, HangSafe: true),
        // x2_delegates: a delegate array element INVOKED (fs(0)()), Sub and Function delegates called as statements. vbc: 12 | 1112
        new TempProbe("x2_delegates", """
            Sub Main()
                Dim n As Integer = 0
                Dim fs(1) As Action
                fs(0) = Sub() n = n + 1
                fs(1) = Sub() n = n + 10
                fs(0)()
                fs(1)()
                fs(0)()
                Console.WriteLine(n)
                Dim g As Action(Of Integer) = Sub(x) n = n + x
                g(100)
                Dim h As Func(Of Integer, Integer) = Function(x)
                                                         n = n + x
                                                         Return n
                                                     End Function
                h(1000)
                Console.WriteLine(n)
            End Sub
            """, "12\n1112", ThreeBackends, HangSafe: true),
        // x3_events: RaiseEvent and AddHandler (C# and JavaScript only: C++ does not compile an Event, `use of undeclared identifier Action`, before and after #267). vbc: 10
        new TempProbe("x3_events", """
            ' RaiseEvent, AddHandler, and a method call through a field at statement level
            Class Src
                Public Event Ping(n As Integer)
                Public Sub Fire()
                    RaiseEvent Ping(5)
                End Sub
            End Class

            Class Sink
                Public Total As Integer
                Public Sub OnPing(n As Integer)
                    Total = Total + n
                End Sub
            End Class

            Sub Main()
                Dim s As New Src()
                Dim k As New Sink()
                AddHandler s.Ping, AddressOf k.OnPing
                s.Fire()
                s.Fire()
                Console.WriteLine(k.Total)
            End Sub
            """, "10", Bk.CSharp | Bk.JavaScript, HangSafe: true),
    };

    [Test]
    public void TheAcceptedStatementShapes_StillRun_AndPrintVbcsAnswer()
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in AcceptedProbes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>One program, one shape of each family; every target, standard and aggressive.</summary>
    [Test]
    public void TheCli_RefusesOneShapeOfEveryFamily_OnEveryTarget_AndWritesNothing()
    {
        var program = ExpressionStatementVbRefusalTests.At("cli", "MAIN", "b.Bump()\n    New Box(Tag())\n    b.P\n    b.Items(0)\n    a(5)\n    b.Bump").Source;
        var failures = new List<string>();
        foreach (var target in new[] { "csharp", "cpp", "javascript", "msil", "llvm" })
        {
            foreach (var optimize in new[] { false, true })
            {
                var dir = Path.Combine(Path.GetTempPath(), "bl-t267-cli-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Prog.bas"), program);
                    var args = new List<string> { "Prog.bas", "--target=" + target };
                    if (optimize) args.Add("--optimize");
                    var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
                    var all = stdout + stderr;
                    var cell = $"--target={target}{(optimize ? " --optimize" : "")}";
                    if (exit == 0) failures.Add($"{cell}: exit 0, the program was accepted");
                    foreach (var code in new[] { "BC30035", "BC30545", "BC30057", "BC30454" })
                        if (!all.Contains(code + ":")) failures.Add($"{cell}: {code} is not reported:\n{all}");
                    var written = Directory.GetFiles(dir).Select(Path.GetFileName).Where(f => f != "Prog.bas").ToList();
                    if (written.Count > 0) failures.Add($"{cell}: wrote {string.Join(", ", written)} for a refused program");
                }
                finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
