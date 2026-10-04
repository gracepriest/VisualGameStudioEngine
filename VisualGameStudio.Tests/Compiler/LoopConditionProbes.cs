using System;
using System.Collections.Generic;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// One program of the #256 matrix: its source, the answer vbc printed for it, the backends that print that
/// answer TODAY (the cells that have an expectation), and whether JavaScript refuses it (#257).
/// </summary>
/// <param name="Agrees">The backends whose output is vbc's, measured after the #256 fix. A backend left out is a defect
/// that is not #256's and is named in the fixture's header — it has NO expectation here, because asserting a wrong
/// answer pins the defect.</param>
/// <param name="JsRefused">JavaScript refuses the loop header ("a loop header whose branch does not target the loop's
/// own .end block", #257), through every entry point. Pinned as a known refusal, not as an answer.</param>
public sealed record LoopProbe(string Id, string Source, string Vb, Bk Agrees, bool JsRefused)
{
    public override string ToString() => Id;
}

/// <summary>
/// ⭐ #256 — the probe matrix, shared by the execution fixture (<see cref="LoopConditionReevaluationExecutionTests"/>) and
/// the emission-shape fixture (<see cref="LoopConditionEmissionShapeTests"/>), which never runs anything.
///
/// <para><b>THE ORACLE IS vbc.</b> Every <see cref="LoopProbe.Vb"/> is what the SDK's <c>vbc</c> printed for the program
/// wrapped in a <c>Module</c> (the implementer's <c>S/t256/vb-oracle.txt</c>, every row <c>VB-RAN</c>), never a backend's output.</para>
///
/// <para><b>THE SIDE-EFFECT COUNTER.</b> Every program declares <c>seen</c> and <c>P(tag, v)</c>, which appends the tag and returns
/// <c>v</c>. A condition built from <c>P</c> calls therefore leaves a TRAIL of exactly which operands ran, how often, in which
/// order: <c>W_aa</c>'s <c>abababa</c> is "a, b, a, b, a, b, a" — every iteration ran both operands, the last one stopped at "a".
/// A loop that evaluated its condition once (#256) hangs or prints <c>seen=ab</c>; one that ran it twice prints
/// <c>aabaabaa…</c>; one that failed to short-circuit prints a <c>b</c> where the left operand was false. The result alone
/// (<c>i=3 body=3</c>) cannot tell any of those from the right answer.</para>
///
/// <para>5 loop forms x 7 condition kinds = 35 programs, generated below (one definition, so a kind cannot be added to
/// one form and forgotten in another); the extras are the programs the matrix needs around them.</para>
/// </summary>
internal static class LoopConditionProbes
{
    /// <summary>While … End While, Do While … Loop, Do Until … Loop, Do … Loop While, Do … Loop Until.</summary>
    internal static readonly string[] Forms = { "W", "DW", "DU", "LW", "LU" };

    /// <summary>
    /// ctl: a plain compare (no side effect — the control). se: ONE counting call (one block, so it keeps `while (cond)`).
    /// aa: AndAlso. oe: OrElse. iff: If(c, a, b). nt: Not (a AndAlso b). cx: (a AndAlso b) OrElse (c AndAlso d).
    /// </summary>
    internal static readonly string[] Kinds = { "ctl", "se", "aa", "oe", "iff", "nt", "cx" };

    /// <summary>
    /// ⭐ STRESS conditions (ids s1..s10), for the shapes the grid does not have: a chain of three, `Not` over an OrElse, an If with an AndAlso arm,
    /// an If as a call's argument, an If inside a compare, an If inside an AndAlso, `Not If(…)`, an AndAlso whose right operand is an OrElse, and an If
    /// whose CONDITION is an AndAlso. The execution fixture runs them in two forms (While and Do … Loop While) and the emission fixture hands every form
    /// to Roslyn. Until-sense is `Not (cond)`.
    /// </summary>
    internal static readonly string[] StressKinds = { "s1", "s2", "s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10" };

    /// <summary>The kinds whose condition is ONE block: no AndAlso, OrElse or If(). They keep `while (cond)`.</summary>
    internal static readonly string[] OneBlockKinds = { "ctl", "se" };

    /// <summary>
    /// ⭐ More one-block conditions, for the EMISSION-SHAPE fixture only (no vbc answer: they are never run). not: `Not (i >= 3)`, and: the
    /// non-short-circuit `And`, or: `Or` — each is ONE block, so the loop keeps `while (cond)`, byte for byte as before #256.
    /// </summary>
    internal static readonly string[] ShapeOnlyOneBlockKinds = { "not", "and", "or" };

    private const string Head = """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0

        """;

    private const string Tail = """
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """;

    private const string Body = "        i = i + 1\n        body = body + 1\n";

    // (the condition in While sense, the condition in Until sense)
    private static (string While, string Until) Condition(string kind) => kind switch
    {
        "ctl" => ("i < 3", "i >= 3"),
        "not" => ("Not (i >= 3)", "Not (i < 3)"),
        "and" => ("i < 3 And i < 9", "i >= 3 And i < 9"),
        "or" => ("i < 1 Or i < 3", "i >= 3 Or i < 0"),
        "se" => ("P(\"a\", i < 3)", "P(\"a\", i >= 3)"),
        "aa" => ("P(\"a\", i < 3) AndAlso P(\"b\", i < 9)", "P(\"a\", i >= 3) AndAlso P(\"b\", i < 9)"),
        "oe" => ("P(\"a\", i < 1) OrElse P(\"b\", i < 3)", "P(\"a\", i >= 3) OrElse P(\"b\", i < 0)"),
        "iff" => ("If(P(\"c\", i Mod 2 = 0), P(\"a\", i < 3), P(\"b\", i < 3))", "If(P(\"c\", i Mod 2 = 0), P(\"a\", i >= 3), P(\"b\", i >= 3))"),
        "nt" => ("Not (P(\"a\", i >= 3) AndAlso P(\"b\", i < 9))", "Not (P(\"a\", i < 3) AndAlso P(\"b\", i < 9))"),
        "cx" => ("(P(\"a\", i < 2) AndAlso P(\"b\", i < 9)) OrElse (P(\"c\", i = 2) AndAlso P(\"d\", i < 9))",
                 "(P(\"a\", i >= 3) AndAlso P(\"b\", i < 9)) OrElse (P(\"c\", i = 1) AndAlso P(\"d\", i > 9))"),
        "s1" => StressCondition("P(\"a\", i < 3) AndAlso P(\"b\", i < 9) AndAlso P(\"c\", i < 8)"),
        "s2" => StressCondition("P(\"a\", i < 1) OrElse P(\"b\", i < 2) OrElse P(\"c\", i < 3)"),
        "s3" => StressCondition("Not (P(\"a\", i >= 3) OrElse P(\"b\", i >= 9))"),
        "s4" => StressCondition("If(P(\"a\", i < 2), P(\"b\", i < 9), P(\"c\", i < 3) AndAlso P(\"d\", i < 9))"),
        "s5" => StressCondition("P(\"a\", If(i < 3, True, False))"),
        "s6" => StressCondition("If(i < 3, \"x\", \"y\") = \"x\""),
        "s7" => StressCondition("(i < 3) AndAlso (If(i Mod 2 = 0, True, True))"),
        "s8" => StressCondition("P(\"a\", i < 3) AndAlso (P(\"b\", i < 9) OrElse P(\"c\", False))"),
        "s9" => StressCondition("Not If(i < 3, True, False)"),
        "s10" => StressCondition("If(P(\"a\", i < 3) AndAlso P(\"b\", i < 9), P(\"c\", True), P(\"d\", False))"),
        _ => throw new ArgumentException(kind),
    };

    private static (string While, string Until) StressCondition(string whileSense) => (whileSense, $"Not ({whileSense})");

    private static string Loop(string form, string kind)
    {
        var (w, u) = Condition(kind);
        return form switch
        {
            "W" => $"    While {w}\n{Body}    End While\n",
            "DW" => $"    Do While {w}\n{Body}    Loop\n",
            "DU" => $"    Do Until {u}\n{Body}    Loop\n",
            "LW" => $"    Do\n{Body}    Loop While {w}\n",
            "LU" => $"    Do\n{Body}    Loop Until {u}\n",
            _ => throw new ArgumentException(form),
        };
    }

    /// <summary>The program for one cell of the 5 x 7 grid, e.g. <c>Program("LU", "oe")</c>.</summary>
    internal static string Program(string form, string kind)
        => Head.Replace("\r\n", "\n") + Loop(form, kind) + Tail.Replace("\r\n", "\n") + "\n";

    // ---- what vbc printed for each of the 35 (S/t256/vb-oracle.txt) ----
    private static readonly Dictionary<string, string> Oracle = new()
    {
        ["DU_aa"] = "i=3 body=3 seen=aaaab",
        ["DU_ctl"] = "i=3 body=3 seen=",
        ["DU_cx"] = "i=3 body=3 seen=acacdacab",
        ["DU_iff"] = "i=3 body=3 seen=cacbcacb",
        ["DU_nt"] = "i=3 body=3 seen=abababa",
        ["DU_oe"] = "i=3 body=3 seen=abababa",
        ["DU_se"] = "i=3 body=3 seen=aaaa",
        ["DW_aa"] = "i=3 body=3 seen=abababa",
        ["DW_ctl"] = "i=3 body=3 seen=",
        ["DW_cx"] = "i=3 body=3 seen=ababacdac",
        ["DW_iff"] = "i=3 body=3 seen=cacbcacb",
        ["DW_nt"] = "i=3 body=3 seen=aaaab",
        ["DW_oe"] = "i=3 body=3 seen=aababab",
        ["DW_se"] = "i=3 body=3 seen=aaaa",
        ["LU_aa"] = "i=3 body=3 seen=aaab",
        ["LU_ctl"] = "i=3 body=3 seen=",
        ["LU_cx"] = "i=3 body=3 seen=acdacab",
        ["LU_iff"] = "i=3 body=3 seen=cbcacb",
        ["LU_nt"] = "i=3 body=3 seen=ababa",
        ["LU_oe"] = "i=3 body=3 seen=ababa",
        ["LU_se"] = "i=3 body=3 seen=aaa",
        ["LW_aa"] = "i=3 body=3 seen=ababa",
        ["LW_ctl"] = "i=3 body=3 seen=",
        ["LW_cx"] = "i=3 body=3 seen=abacdac",
        ["LW_iff"] = "i=3 body=3 seen=cbcacb",
        ["LW_nt"] = "i=3 body=3 seen=aaab",
        ["LW_oe"] = "i=3 body=3 seen=ababab",
        ["LW_se"] = "i=3 body=3 seen=aaa",
        ["W_aa"] = "i=3 body=3 seen=abababa",
        ["W_ctl"] = "i=3 body=3 seen=",
        ["W_cx"] = "i=3 body=3 seen=ababacdac",
        ["W_iff"] = "i=3 body=3 seen=cacbcacb",
        ["W_nt"] = "i=3 body=3 seen=aaaab",
        ["W_oe"] = "i=3 body=3 seen=aababab",
        ["W_se"] = "i=3 body=3 seen=aaaa"
    };

    /// <summary>
    /// Who agrees with vbc on a cell of the grid, measured on the fixed build:
    /// C# and C++ on all 35; MSIL on all but <c>nt</c> (MSIL's <c>Not</c> is bitwise, #257: three of those cells hang and two print the
    /// wrong answer); JavaScript only where there is no control flow in the condition (ctl, se) — it refuses the rest (#257).
    /// </summary>
    private static Bk AgreesFor(string kind) => kind switch
    {
        "ctl" or "se" => Bk.All,
        "nt" => Bk.CSharp | Bk.Cpp,
        _ => Bk.CSharp | Bk.Cpp | Bk.Msil,
    };

    /// <summary>The 35 programs of the grid, in form-major order.</summary>
    internal static readonly IReadOnlyList<LoopProbe> Grid = Forms
        .SelectMany(f => Kinds.Select(k => new LoopProbe(
            $"{f}_{k}", Program(f, k), Oracle[$"{f}_{k}"], AgreesFor(k), !OneBlockKinds.Contains(k))))
        .ToList();

    // ---- what vbc printed for the stress programs (W and LW; S/t256/tw-tmp/stressvb, run through vbv2.py) ----
    private static readonly Dictionary<string, string> StressOracle = new()
    {
        ["LW_s1"] = "i=3 body=3 seen=abcabca",
        ["LW_s2"] = "i=3 body=3 seen=ababcabc",
        ["LW_s3"] = "i=3 body=3 seen=ababa",
        ["LW_s4"] = "i=3 body=3 seen=abacdac",
        ["LW_s5"] = "i=3 body=3 seen=aaa",
        ["LW_s6"] = "i=3 body=3 seen=",
        ["LW_s7"] = "i=3 body=3 seen=",
        ["LW_s8"] = "i=3 body=3 seen=ababa",
        ["LW_s9"] = "i=1 body=1 seen=",
        ["LW_s10"] = "i=3 body=3 seen=abcabcad",
        ["W_s1"] = "i=3 body=3 seen=abcabcabca",
        ["W_s2"] = "i=3 body=3 seen=aababcabc",
        ["W_s3"] = "i=3 body=3 seen=abababa",
        ["W_s4"] = "i=3 body=3 seen=ababacdac",
        ["W_s5"] = "i=3 body=3 seen=aaaa",
        ["W_s6"] = "i=3 body=3 seen=",
        ["W_s7"] = "i=3 body=3 seen=",
        ["W_s8"] = "i=3 body=3 seen=abababa",
        ["W_s9"] = "i=0 body=0 seen=",
        ["W_s10"] = "i=3 body=3 seen=abcabcabcad"
    };

    /// <summary>
    /// The stress conditions in two forms — While (top-tested) and Do … Loop While (bottom-tested, peeled) — for C# and C++ (MSIL's `Not` is bitwise,
    /// #257, and two of the ten have one; JavaScript refuses every one).
    /// </summary>
    internal static readonly IReadOnlyList<LoopProbe> Stress = new[] { "W", "LW" }
        .SelectMany(f => StressKinds.Select(k => new LoopProbe($"{f}_{k}", Program(f, k), StressOracle[$"{f}_{k}"], Bk.CSharp | Bk.Cpp, false)))
        .ToList();

    // ================================== the extras (S/t256/probes/m, m2) ==================================

    // a loop in a CLASS METHOD (the condition is OrElse over a field-reading call)
    internal static readonly LoopProbe c_meth = new("c_meth", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Class Counter
            Public Total As Integer
            Public Function Run(limit As Integer) As Integer
                Dim i As Integer = 0
                Do While P("a", i < limit) OrElse P("b", i < 0)
                    i = i + 1
                    Total = Total + i
                Loop
                Return i
            End Function
        End Class

        Sub Main()
            Dim c As New Counter()
            Dim r As Integer = c.Run(3)
            Console.WriteLine("r=" & r & " total=" & c.Total & " seen=" & seen)
        End Sub
        """,
        """
        r=3 total=6 seen=aaaab
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // ADR-0014: a per-iteration Dim captured by a lambda, While … AndAlso
    internal static readonly LoopProbe d_W = new("d_W", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim fs As New List(Of Func(Of Integer))()
            While P("a", i < 3) AndAlso P("b", i < 9)
                i = i + 1
                Dim x As Integer
                x = x + i
                fs.Add(Function() x)
            End While
            For k As Integer = 0 To fs.Count - 1
                Dim g As Func(Of Integer) = fs(k)
                Console.WriteLine(g())
            Next
            Console.WriteLine("i=" & i & " seen=" & seen)
        End Sub
        """,
        """
        1
        3
        6
        i=3 seen=abababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // ADR-0014: a per-iteration Dim captured by a lambda, Do Until … OrElse
    internal static readonly LoopProbe d_DU = new("d_DU", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim fs As New List(Of Func(Of Integer))()
            Do Until P("a", i >= 3) OrElse P("b", i < 0)
                i = i + 1
                Dim x As Integer
                x = x + i
                fs.Add(Function() x)
            Loop
            For k As Integer = 0 To fs.Count - 1
                Dim g As Func(Of Integer) = fs(k)
                Console.WriteLine(g())
            Next
            Console.WriteLine("i=" & i & " seen=" & seen)
        End Sub
        """,
        """
        1
        3
        6
        i=3 seen=abababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // ADR-0014: a per-iteration Dim captured by a lambda, Do … Loop While … AndAlso (the peeled shape)
    internal static readonly LoopProbe d_LW = new("d_LW", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim fs As New List(Of Func(Of Integer))()
            Do
                i = i + 1
                Dim x As Integer
                x = x + i
                fs.Add(Function() x)
            Loop While P("a", i < 3) AndAlso P("b", i < 9)
            For k As Integer = 0 To fs.Count - 1
                Dim g As Func(Of Integer) = fs(k)
                Console.WriteLine(g())
            Next
            Console.WriteLine("i=" & i & " seen=" & seen)
        End Sub
        """,
        """
        1
        3
        6
        i=3 seen=ababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // an If inside the body, While … AndAlso (the body's own control flow is not the condition's)
    internal static readonly LoopProbe f_W_aa = new("f_W_aa", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            While P("a", i < 3) AndAlso P("b", i < 9)
                i = i + 1
                If i Mod 2 = 0 Then
                    body = body + 100
                End If
                body = body + 1
            End While
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=103 seen=abababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // …its plain-condition control
    internal static readonly LoopProbe f_W_ctl = new("f_W_ctl", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            While i < 3
                i = i + 1
                If i Mod 2 = 0 Then
                    body = body + 100
                End If
                body = body + 1
            End While
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=103 seen=
        """,
        Bk.All, false);
    // an If inside a Do … Loop While body: ⛔ C# is #227 (the loop's copy of the body drops the If's continuation) — no C# expectation
    internal static readonly LoopProbe f_LW_aa = new("f_LW_aa", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            Do
                i = i + 1
                If i Mod 2 = 0 Then
                    body = body + 100
                End If
                body = body + 1
            Loop While P("a", i < 3) AndAlso P("b", i < 9)
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=103 seen=ababa
        """,
        Bk.Cpp | Bk.Msil, true);
    // …its plain-condition control: ⛔ C# is the same #227 — no C# expectation
    internal static readonly LoopProbe f_LW_ctl = new("f_LW_ctl", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            Do
                i = i + 1
                If i Mod 2 = 0 Then
                    body = body + 100
                End If
                body = body + 1
            Loop While i < 3
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=103 seen=
        """,
        Bk.Cpp | Bk.JavaScript | Bk.Msil, false);
    // a short-circuit loop inside a Function lambda: C# printed nothing right until #136 (CS1643: the lambda lost its return paths); it runs now
    internal static readonly LoopProbe l_fn = new("l_fn", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(n As Integer)
                                                     Dim k As Integer = 0
                                                     While P("a", k < n) AndAlso P("b", k < 9)
                                                         k = k + 1
                                                     End While
                                                     Return k
                                                 End Function
            Console.WriteLine("k=" & f(3) & " seen=" & seen)
        End Sub
        """,
        """
        k=3 seen=abababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // a short-circuit loop inside a Sub lambda: C# lost the lambda's writes until #136; it runs now
    internal static readonly LoopProbe l_sub = new("l_sub", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim total As Integer = 0
            Dim s As Action(Of Integer) = Sub(n As Integer)
                                              Dim k As Integer = 0
                                              Do Until P("a", k >= n) OrElse P("b", k < 0)
                                                  k = k + 1
                                                  total = total + k
                                              Loop
                                          End Sub
            s(3)
            Console.WriteLine("total=" & total & " seen=" & seen)
        End Sub
        """,
        """
        total=6 seen=abababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // two nested While loops, each with an AndAlso condition
    internal static readonly LoopProbe n_WW = new("n_WW", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim j As Integer = 0
            Dim body As Integer = 0
            While P("a", i < 3) AndAlso P("b", i < 9)
                j = 0
                While P("c", j < 2) AndAlso P("d", i < 9)
                    j = j + 1
                    body = body + 1
                End While
                i = i + 1
            End While
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=6 seen=abcdcdcabcdcdcabcdcdca
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // a Do … Loop Until around a Do While, both with AndAlso: ⛔ C# is #227 (the inner loop is dropped from the loop's copy) — no C# expectation
    internal static readonly LoopProbe n_LD = new("n_LD", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim j As Integer = 0
            Dim body As Integer = 0
            Do
                j = 0
                Do While P("c", j < 2) AndAlso P("d", i < 9)
                    j = j + 1
                    body = body + 1
                Loop
                i = i + 1
            Loop Until P("a", i >= 3) AndAlso P("b", i < 9)
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=6 seen=cdcdcacdcdcacdcdcab
        """,
        Bk.Cpp | Bk.Msil, true);
    // …the same nesting with PLAIN conditions: ⛔ C# HANGS before and after #256 (#227) — no C# expectation
    internal static readonly LoopProbe n_LDctl = new("n_LDctl", """
        Sub Main()
            Dim i As Integer = 0
            Dim j As Integer = 0
            Dim body As Integer = 0
            Do
                j = 0
                Do While j < 2
                    j = j + 1
                    body = body + 1
                Loop
                i = i + 1
            Loop Until i >= 3
            Console.WriteLine("i=" & i & " body=" & body)
        End Sub
        """,
        """
        i=3 body=6
        """,
        Bk.Cpp | Bk.JavaScript | Bk.Msil, false);
    // a counted For whose To bound is an If(): C# computes it ONCE (VB's rule); ⛔ C++ and MSIL re-evaluate it every iteration (#261) — C# only
    internal static readonly LoopProbe o_for = new("o_for", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim lim As Integer = 3
            Dim body As Integer = 0
            For j = 1 To If(P("c", lim > 0), lim, 0)
                body = body + 1
                lim = 1
            Next
            Console.WriteLine("body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        body=3 seen=c
        """,
        Bk.CSharp, true);
    // Exit Do from inside a Try/Finally in the body, Do Until … OrElse
    internal static readonly LoopProbe t_body = new("t_body", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            Do Until P("a", i >= 5) OrElse P("b", i < 0)
                Try
                    i = i + 1
                    If i = 3 Then Exit Do
                Finally
                    body = body + 1
                End Try
            Loop
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=3 seen=ababab
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // the loop is inside a Try … Catch … Finally
    internal static readonly LoopProbe t_in = new("t_in", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Try
                While P("a", i < 3) AndAlso P("b", i < 9)
                    i = i + 1
                End While
                Console.WriteLine("in try " & i)
            Catch ex As Exception
                Console.WriteLine("caught")
            Finally
                Console.WriteLine("finally")
            End Try
            Console.WriteLine("i=" & i & " seen=" & seen)
        End Sub
        """,
        """
        in try 3
        finally
        i=3 seen=abababa
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // Exit While
    internal static readonly LoopProbe x_W = new("x_W", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            While P("a", i < 5) AndAlso P("b", i < 9)
                i = i + 1
                If i = 3 Then Exit While
                body = body + 1
            End While
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=2 seen=ababab
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // Exit Do, Do Until … OrElse
    internal static readonly LoopProbe x_DU = new("x_DU", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            Do Until P("a", i >= 5) OrElse P("b", i < 0)
                i = i + 1
                If i = 3 Then Exit Do
                body = body + 1
            Loop
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=2 seen=ababab
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // Exit Do in a Do … Loop While: ⛔ C# is #227 — no C# expectation
    internal static readonly LoopProbe x_LW = new("x_LW", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            Do
                i = i + 1
                If i = 3 Then Exit Do
                body = body + 1
            Loop While P("a", i < 5) AndAlso P("b", i < 9)
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=2 seen=abab
        """,
        Bk.Cpp | Bk.Msil, true);
    // Exit Do from inside a Select Case (a C# switch's break would leave the switch, not the loop)
    internal static readonly LoopProbe x_sel = new("x_sel", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim body As Integer = 0
            Do While P("a", i < 9) AndAlso P("b", i < 9)
                i = i + 1
                Select Case i
                    Case 3
                        Exit Do
                    Case Else
                        body = body + 1
                End Select
            Loop
            Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
        End Sub
        """,
        """
        i=3 body=2 seen=ababab
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    // Exit Do from an inner Do While If(…) loop, inside an outer While … AndAlso
    internal static readonly LoopProbe x_Wlast = new("x_Wlast", """
        Dim seen As String = ""

        Function P(tag As String, v As Boolean) As Boolean
            seen = seen & tag
            Return v
        End Function

        Sub Main()
            Dim i As Integer = 0
            Dim r As Integer = 0
            While P("a", r < 3) AndAlso P("b", True)
                r = r + 1
                i = 0
                Do While If(P("c", i < 9), True, False)
                    i = i + 1
                    Exit Do
                Loop
            End While
            Console.WriteLine("i=" & i & " r=" & r & " seen=" & seen)
        End Sub
        """,
        """
        i=1 r=3 seen=abcabcabca
        """,
        Bk.CSharp | Bk.Cpp | Bk.Msil, true);
    /// <summary>The probes around the grid, by id.</summary>
    internal static readonly IReadOnlyList<LoopProbe> Extras = new[]
    {
        c_meth, d_W, d_DU, d_LW, f_W_aa, f_W_ctl, f_LW_aa, f_LW_ctl, l_fn, l_sub, n_WW, n_LD, n_LDctl, o_for, t_body, t_in, x_W, x_DU, x_LW, x_sel, x_Wlast,
    };

    /// <summary>Every program: the grid, then the extras.</summary>
    internal static readonly IReadOnlyList<LoopProbe> All = Grid.Concat(Extras).ToList();

    internal static LoopProbe ById(string id)
        => All.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException($"no loop probe '{id}'");
}
