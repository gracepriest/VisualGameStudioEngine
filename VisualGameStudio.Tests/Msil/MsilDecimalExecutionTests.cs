using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Task #129 — MSIL compiles and RUNS <c>Decimal</c>, and prints what <c>vbc</c> prints (fix commit 3799f3dd).
///
/// <para><b>The oracle is vbc, not MSIL.</b> Every expected string below is the answer
/// <c>vbc</c> printed for the same program (the implementer's <c>.exp</c> files,
/// <c>S/t129/probes/{m,m2}</c>) — never copied from the backend this task is about. Before the fix, 180
/// of the 228 MSIL cells in the probe matrix failed to ASSEMBLE ("Reference to undefined class
/// 'valuetypeSystemRuntimeSystemDecimal'"): a Decimal local, field, parameter, return, literal, operator,
/// comparison and <c>CInt</c> were all wrong, and an operator the assembler accepted
/// (<c>add</c> on a 16-byte struct, <c>ceq</c>, <c>conv.i4</c>) was a silent wrong answer.</para>
///
/// <para><b>Both entry points, plain and optimized.</b> Every program goes through the REAL CLI
/// (<c>BasicLang Prog.bas --target=msil</c> and the same with <c>--optimize</c>), and one test drives a
/// Release <c>.blproj</c> build — <c>CompileProjectFiles</c>, the route the IDE takes. The CLI only
/// writes the <c>.il</c>; <c>ilasm</c> and the run go through <see cref="MsilHarness.RunIl"/> (timeout,
/// out of process), so a machine without <c>ilasm</c> SKIPS these and Windows owes the run through its own
/// <c>ilasm</c>. The two IL-text tests at the bottom need no assembler and run in the fast subset.</para>
///
/// <para><b>Known gaps, deliberately NOT tested</b> (pinning "still refused" would make the fix for each
/// one edit this file; see the #129 commit message and <c>docs/HANDOFF.md</c>):</para>
/// <list type="bullet">
///   <item><c>d /= x</c> — IRBuilder types every <c>/=</c> Double (<c>IRBuilder.cs</c> ~5025), so MSIL
///   refuses a float64 stored into a Decimal slot (C# prints 1.5 for a literal and fails CS0019 for a
///   variable).</item>
///   <item>Literal scale — <c>Dim d As Decimal = 1.50</c> prints 1.50 on C#, C++ and MSIL where vbc reads
///   an unsuffixed 1.50 as a Double and prints 1.5 (spec 6.1: a Decimal-context literal keeps its source
///   scale; the lexer has no <c>D</c> suffix). Every probe here is chosen so its output does NOT depend on a
///   literal's scale (<c>d03_scale</c>, <c>e54_negzero</c>, <c>e65_methods</c> are left out).</item>
///   <item>Front-end refusals on every backend: no <c>CDec</c>, <c>^</c> does not parse, Decimal with
///   Double/Single, implicit Decimal to Integer/Double, a Double literal as a Decimal <c>Optional</c>
///   default.</item>
/// </list>
///
/// <para>⚠ <c>[NonParallelizable]</c>: every test spawns the CLI, <c>ilasm</c> and <c>dotnet</c>.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class MsilDecimalExecutionTests
{
    // ========================================================================================
    // The probes: name -> (source, vbc's answer).
    // ========================================================================================

    private static readonly Dictionary<string, (string Source, string Expected)> Probes = new();

    private static void Add(string name, string source, string expected) =>
        Probes[name] = (source, expected);

    static MsilDecimalExecutionTests()
    {
        Add("d01_decl",
            """
            Sub Main()
                Dim d As Decimal
                Console.WriteLine(d)
            End Sub
            """,
            """
            0
            """);

        Add("d02_lit",
            """
            Sub Main()
                Dim d As Decimal = 1.5
                Dim z As Decimal = 0
                Dim n As Decimal = -2.75
                Dim w As Decimal = 3
                Console.WriteLine(d)
                Console.WriteLine(z)
                Console.WriteLine(n)
                Console.WriteLine(w)
            End Sub
            """,
            """
            1.5
            0
            -2.75
            3
            """);

        Add("d06_arith",
            """
            Sub Main()
                Dim a As Decimal = 7.5
                Dim b As Decimal = 2
                Console.WriteLine(a + b)
                Console.WriteLine(a - b)
                Console.WriteLine(a * b)
                Console.WriteLine(a / b)
                Dim one As Decimal = 1
                Dim three As Decimal = 3
                Console.WriteLine(one / three)
            End Sub
            """,
            """
            9.5
            5.5
            15.0
            3.75
            0.3333333333333333333333333333
            """);

        Add("d07_intdiv",
            """
            Sub Main()
                Dim a As Decimal = 7.5
                Dim b As Decimal = 2
                Console.WriteLine(a \ b)
                Dim c As Decimal = 9.5
                Console.WriteLine(c \ b)
            End Sub
            """,
            """
            4
            5
            """);

        Add("d08_mod",
            """
            Sub Main()
                Dim a As Decimal = 7.5
                Dim b As Decimal = 2
                Dim c As Decimal = -7.5
                Console.WriteLine(a Mod b)
                Console.WriteLine(c Mod b)
            End Sub
            """,
            """
            1.5
            -1.5
            """);

        Add("d09_neg",
            """
            Sub Main()
                Dim a As Decimal = 7.5
                Dim z As Decimal = 0
                Console.WriteLine(-a)
                Console.WriteLine(-z)
                Dim b As Decimal = -a
                Console.WriteLine(b)
            End Sub
            """,
            """
            -7.5
            0
            -7.5
            """);

        Add("e60_intdivmix",
            """
            Sub Main()
                Dim d As Decimal = 9.5
                Dim i As Integer = 2
                Console.WriteLine(d \ i)
                Console.WriteLine(i \ d)
                Dim m As Decimal = 8.5
                Console.WriteLine(m \ i)
            End Sub
            """,
            """
            5
            0
            4
            """);

        Add("e61_modmix",
            """
            Sub Main()
                Dim d As Decimal = 7.5
                Dim i As Integer = 2
                Console.WriteLine(d Mod i)
                Console.WriteLine(i Mod d)
            End Sub
            """,
            """
            1.5
            2
            """);

        Add("d11_cmp",
            """
            Sub Main()
                Dim a As Decimal = 1.5
                Dim b As Decimal = 2.5
                Dim c As Decimal = 1.5
                Console.WriteLine(a = b)
                Console.WriteLine(a <> b)
                Console.WriteLine(a < b)
                Console.WriteLine(a > b)
                Console.WriteLine(a <= c)
                Console.WriteLine(a >= b)
                Console.WriteLine(a = c)
            End Sub
            """,
            """
            False
            True
            True
            False
            True
            False
            True
            """);

        Add("d12_if",
            """
            Sub Main()
                Dim a As Decimal = 1.5
                Dim b As Decimal = 2.5
                If a < b Then
                    Console.WriteLine("lt")
                Else
                    Console.WriteLine("ge")
                End If
                If a = b Then
                    Console.WriteLine("eq")
                Else
                    Console.WriteLine("ne")
                End If
                If b >= a Then Console.WriteLine("ge2")
            End Sub
            """,
            """
            lt
            ne
            ge2
            """);

        Add("e55_cmpconst",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                If d > 2 Then Console.WriteLine("gt2")
                If d < 3 Then Console.WriteLine("lt3")
                If d = 2.5 Then Console.WriteLine("eq")
                If d <> 1 Then Console.WriteLine("ne1")
                Dim i As Integer = 2
                If d > i Then Console.WriteLine("gti")
            End Sub
            """,
            """
            gt2
            lt3
            eq
            ne1
            gti
            """);

        Add("d39_for",
            """
            Sub Main()
                For d As Decimal = 0.5 To 2 Step 0.5
                    Console.WriteLine(d)
                Next
            End Sub
            """,
            """
            0.5
            1.0
            1.5
            2.0
            """);

        Add("d40_while",
            """
            Sub Main()
                Dim t As Decimal = 0
                Dim q As Decimal = 0.25
                Dim lim As Decimal = 1
                Dim n As Integer = 0
                While t < lim
                    t = t + q
                    n = n + 1
                End While
                Console.WriteLine(t)
                Console.WriteLine(n)
            End Sub
            """,
            """
            1.00
            4
            """);

        Add("d13_select",
            """
            Sub Show(d As Decimal)
                Select Case d
                    Case 1.5
                        Console.WriteLine("one-and-a-half")
                    Case Is > 2
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("other")
                End Select
            End Sub
            Sub Main()
                Dim a As Decimal = 1.5
                Dim b As Decimal = 2.5
                Dim c As Decimal = 1
                Show(a)
                Show(b)
                Show(c)
            End Sub
            """,
            """
            one-and-a-half
            big
            other
            """);

        Add("e46_selectint",
            """
            Sub Show(d As Decimal)
                Select Case d
                    Case 1
                        Console.WriteLine("one")
                    Case 2 To 3
                        Console.WriteLine("two-three")
                    Case Is > 4
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("other")
                End Select
            End Sub
            Sub Main()
                Dim a As Decimal = 1
                Dim b As Decimal = 2.5
                Dim c As Decimal = 4.5
                Dim e As Decimal = 3.5
                Show(a)
                Show(b)
                Show(c)
                Show(e)
            End Sub
            """,
            """
            one
            two-three
            big
            other
            """);

        Add("e64_guard",
            """
            Sub Show(d As Decimal)
                Dim lim As Decimal = 2
                Select Case d
                    Case Is > 1
                        Console.WriteLine("gt1")
                    Case Else
                        Console.WriteLine("other")
                End Select
            End Sub
            Sub Main()
                Dim a As Decimal = 1.5
                Dim b As Decimal = 0.5
                Show(a)
                Show(b)
            End Sub
            """,
            """
            gt1
            other
            """);

        Add("e66_casenothing",
            """
            Sub Main()
                Dim d As Decimal = 0
                Select Case d
                    Case Nothing
                        Console.WriteLine("nothing")
                    Case Else
                        Console.WriteLine("else")
                End Select
            End Sub
            """,
            """
            nothing
            """);

        Add("d20_cint",
            """
            Sub Main()
                Dim a As Decimal = 2.5
                Dim b As Decimal = 3.5
                Dim c As Decimal = -2.5
                Dim e As Decimal = 2.7
                Console.WriteLine(CInt(a))
                Console.WriteLine(CInt(b))
                Console.WriteLine(CInt(c))
                Console.WriteLine(CInt(e))
                Console.WriteLine(CLng(a))
                Console.WriteLine(CLng(b))
                Console.WriteLine(CLng(c))
            End Sub
            """,
            """
            2
            4
            -2
            3
            2
            4
            -2
            """);

        Add("e44_ctype",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim f As Decimal = 3.5
                Dim i As Integer = CType(d, Integer)
                Dim j As Integer = CType(f, Integer)
                Dim l As Long = CType(f, Long)
                Dim x As Double = CType(d, Double)
                Dim s As Single = CType(d, Single)
                Console.WriteLine(i)
                Console.WriteLine(j)
                Console.WriteLine(l)
                Console.WriteLine(x)
                Console.WriteLine(s)
                Dim n As Integer = 7
                Dim y As Double = 0.25
                Dim e As Decimal = CType(n, Decimal)
                Dim g As Decimal = CType(y, Decimal)
                Console.WriteLine(e)
                Console.WriteLine(g)
            End Sub
            """,
            """
            2
            4
            4
            2.5
            2.5
            7
            0.25
            """);

        Add("d14_mixint",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim i As Integer = 3
                Console.WriteLine(d + i)
                Console.WriteLine(i + d)
                Console.WriteLine(d - i)
                Console.WriteLine(i - d)
                Console.WriteLine(d * i)
                Console.WriteLine(i * d)
                Console.WriteLine(d / i)
                Console.WriteLine(i / d)
                Console.WriteLine(d > i)
                Console.WriteLine(i > d)
            End Sub
            """,
            """
            5.5
            5.5
            -0.5
            0.5
            7.5
            7.5
            0.8333333333333333333333333333
            1.2
            False
            True
            """);

        Add("d15_mixlong",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim l As Long = 4
                Console.WriteLine(d + l)
                Console.WriteLine(l + d)
                Console.WriteLine(d * l)
                Console.WriteLine(l - d)
                Console.WriteLine(l < d)
            End Sub
            """,
            """
            6.5
            6.5
            10.0
            1.5
            False
            """);

        Add("d16_mixbyte",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim b As Byte = 2
                Console.WriteLine(d + b)
                Console.WriteLine(b + d)
                Console.WriteLine(b * d)
            End Sub
            """,
            """
            4.5
            4.5
            5.0
            """);

        Add("e59_mixshort",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim s As Short = 3
                Console.WriteLine(d * s)
                Console.WriteLine(s - d)
                Console.WriteLine(s > d)
            End Sub
            """,
            """
            7.5
            0.5
            True
            """);

        Add("d22_widen",
            """
            Sub Main()
                Dim i As Integer = 7
                Dim d As Decimal = i
                Console.WriteLine(d)
                Dim l As Long = 9000000000
                Dim e As Decimal = l
                Console.WriteLine(e)
                Dim b As Byte = 200
                Dim f As Decimal = b
                Console.WriteLine(f)
                d = i * 2
                Console.WriteLine(d)
            End Sub
            """,
            """
            7
            9000000000
            200
            14
            """);

        Add("e56_argwiden",
            """
            Sub Show(x As Decimal)
                Console.WriteLine(x)
            End Sub
            Function FromInt(i As Integer) As Decimal
                Return i
            End Function
            Sub Main()
                Dim i As Integer = 7
                Show(i)
                Show(3)
                Console.WriteLine(FromInt(9))
                Dim l As Long = 12
                Show(l)
            End Sub
            """,
            """
            7
            3
            9
            12
            """);

        Add("e31b_optional_int",
            """
            Function Price(q As Integer, Optional r As Decimal = 2) As Decimal
                Return q * r
            End Function
            Sub Main()
                Console.WriteLine(Price(3))
                Dim r As Decimal = 1.5
                Console.WriteLine(Price(3, r))
            End Sub
            """,
            """
            6
            4.5
            """);

        Add("d26_param",
            """
            Sub Show(ByVal x As Decimal)
                Console.WriteLine(x)
                x = x + 1
            End Sub
            Sub Bump(ByRef x As Decimal)
                x = x + 1
            End Sub
            Sub Main()
                Dim d As Decimal = 1.5
                Show(d)
                Console.WriteLine(d)
                Bump(d)
                Console.WriteLine(d)
            End Sub
            """,
            """
            1.5
            1.5
            2.5
            """);

        Add("d27_ret",
            """
            Function Half(x As Decimal) As Decimal
                Return x / 2
            End Function
            Function Zero() As Decimal
                Return 0
            End Function
            Sub Main()
                Dim d As Decimal = 5
                Console.WriteLine(Half(d))
                Console.WriteLine(Zero())
                Dim h As Decimal = Half(Half(d))
                Console.WriteLine(h)
            End Sub
            """,
            """
            2.5
            0
            1.25
            """);

        Add("d28_field",
            """
            Class Acct
                Public Bal As Decimal
                Public Sub Deposit(a As Decimal)
                    Bal = Bal + a
                End Sub
            End Class
            Sub Main()
                Dim a As New Acct()
                Dim amt As Decimal = 1.25
                a.Deposit(amt)
                a.Deposit(amt)
                Console.WriteLine(a.Bal)
            End Sub
            """,
            """
            2.50
            """);

        Add("e50_fieldinit",
            """
            Class Item
                Public Price As Decimal = 1.25
                Public Qty As Integer = 3
                Public Function Total() As Decimal
                    Return Price * Qty
                End Function
            End Class
            Sub Main()
                Dim it As New Item()
                Console.WriteLine(it.Price)
                Console.WriteLine(it.Total())
            End Sub
            """,
            """
            1.25
            3.75
            """);

        Add("e51_property",
            """
            Class Acct
                Private _bal As Decimal
                Public Property Balance As Decimal
                    Get
                        Return _bal
                    End Get
                    Set(value As Decimal)
                        _bal = value
                    End Set
                End Property
            End Class
            Sub Main()
                Dim a As New Acct()
                Dim v As Decimal = 12.75
                a.Balance = v
                Console.WriteLine(a.Balance)
                a.Balance = a.Balance * 2
                Console.WriteLine(a.Balance)
            End Sub
            """,
            """
            12.75
            25.50
            """);

        Add("e48_byrefelem",
            """
            Sub Bump(ByRef x As Decimal, s As Decimal)
                x = x + s
            End Sub
            Sub Main()
                Dim a(1) As Decimal
                Dim h As Decimal = 1.5
                a(0) = 1
                Bump(a(0), h)
                Console.WriteLine(a(0))
                Dim g As Decimal = 2
                Bump(g, h)
                Bump(g, h)
                Console.WriteLine(g)
            End Sub
            """,
            """
            2.5
            5.0
            """);

        Add("e57_fieldbyref",
            """
            Class Acct
                Public Bal As Decimal
            End Class
            Sub AddTo(ByRef target As Decimal, amount As Decimal)
                target = target + amount
            End Sub
            Dim g As Decimal = 1
            Sub Main()
                Dim one As Decimal = 0.5
                AddTo(g, one)
                Console.WriteLine(g)
            End Sub
            """,
            """
            1.5
            """);

        Add("d29_global",
            """
            Dim g As Decimal = 1.25
            Sub Main()
                g = g * 2
                Console.WriteLine(g)
            End Sub
            """,
            """
            2.50
            """);

        Add("e63_globalfold",
            """
            Dim g As Decimal = 2 + 3
            Dim h As Decimal = 7
            Sub Main()
                Console.WriteLine(g)
                Console.WriteLine(h)
                Dim one As Decimal = 0.5
                Console.WriteLine(g + h + one)
            End Sub
            """,
            """
            5
            7
            12.5
            """);

        Add("d25_output",
            """
            Sub Main()
                Dim d As Decimal = 1.5
                Console.WriteLine(d)
                Console.WriteLine("x" & d)
                Console.WriteLine(d & "y")
                Console.WriteLine(d.ToString())
                Console.Write(d)
                Console.WriteLine()
            End Sub
            """,
            """
            1.5
            x1.5
            1.5y
            1.5
            1.5
            """);

        Add("e53_concat",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim s As String = "a" & d & "b"
                Console.WriteLine(s)
                Console.WriteLine($"v={d}")
                Console.WriteLine(CStr(d) & CStr(d))
            End Sub
            """,
            """
            a2.5b
            v=2.5
            2.52.5
            """);

        Add("d35_round",
            """
            Sub Main()
                Dim d As Decimal = 2.345
                Console.WriteLine(Math.Round(d, 2))
                Dim e As Decimal = 2.5
                Console.WriteLine(Math.Round(e))
            End Sub
            """,
            """
            2.34
            2
            """);

        Add("d36_abs",
            """
            Sub Main()
                Dim d As Decimal = -2.5
                Console.WriteLine(Math.Abs(d))
            End Sub
            """,
            """
            2.5
            """);

        Add("d37_parse",
            """
            Sub Main()
                Dim d As Decimal = Decimal.Parse("1.5")
                Console.WriteLine(d)
                Console.WriteLine(Decimal.Parse("1.50"))
            End Sub
            """,
            """
            1.5
            1.50
            """);

        Add("d38_max",
            """
            Sub Main()
                Console.WriteLine(Decimal.MaxValue)
                Dim m As Decimal = Decimal.MinValue
                Console.WriteLine(m)
            End Sub
            """,
            """
            79228162514264337593543950335
            -79228162514264337593543950335
            """);

        Add("d05b_bigint",
            """
            Sub Main()
                Dim d As Decimal = 9223372036854775807
                Dim ten As Decimal = 10
                Console.WriteLine(d)
                Console.WriteLine(d * ten)
            End Sub
            """,
            """
            9223372036854775807
            92233720368547758070
            """);

        Add("d32_array",
            """
            Sub Main()
                Dim a(2) As Decimal
                a(0) = 1.5
                a(1) = 2.25
                a(2) = a(0) + a(1)
                Console.WriteLine(a(0))
                Console.WriteLine(a(1))
                Console.WriteLine(a(2))
                Dim t As Decimal = 0
                For i As Integer = 0 To 2
                    t = t + a(i)
                Next
                Console.WriteLine(t)
            End Sub
            """,
            """
            1.5
            2.25
            3.75
            7.50
            """);

        Add("d33_list",
            """
            Sub Main()
                Dim l As New List(Of Decimal)
                Dim x As Decimal = 1.5
                Dim y As Decimal = 2.5
                l.Add(x)
                l.Add(y)
                Dim t As Decimal = 0
                For Each v As Decimal In l
                    t = t + v
                Next
                Console.WriteLine(l.Count)
                Console.WriteLine(t)
                Console.WriteLine(l(1))
            End Sub
            """,
            """
            2
            4.0
            2.5
            """);

        Add("e34b_boxonly",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim o As Object = d
                Console.WriteLine(o)
                Dim p As Object = d * 2
                Console.WriteLine(p)
            End Sub
            """,
            """
            2.5
            5.0
            """);

        Add("e34c_unbox",
            """
            Sub Main()
                Dim d As Decimal = 2.5
                Dim o As Object = d
                Dim e As Decimal = CType(o, Decimal)
                Dim one As Decimal = 1
                Console.WriteLine(e + one)
            End Sub
            """,
            """
            3.5
            """);

        Add("e68_lambda",
            """
            Sub Main()
                Dim rate As Decimal = 1.5
                Dim f As Func(Of Decimal, Decimal) = Function(x As Decimal) x * rate
                Dim v As Decimal = 2
                Console.WriteLine(f(v))
                rate = 3
                Console.WriteLine(f(v))
            End Sub
            """,
            """
            3.0
            6
            """);

        Add("e62_nothing",
            """
            Function Pick(flag As Boolean) As Decimal
                Dim r As Decimal
                If flag Then r = 4.25
                Return r
            End Function
            Sub Main()
                Console.WriteLine(Pick(True))
                Console.WriteLine(Pick(False))
            End Sub
            """,
            """
            4.25
            0
            """);

        Add("d42_overflow",
            """
            Sub Main()
                Dim m As Decimal = Decimal.MaxValue
                Dim one As Decimal = 1
                Try
                    Dim r As Decimal = m + one
                    Console.WriteLine(r)
                Catch ex As OverflowException
                    Console.WriteLine("overflow")
                End Try
            End Sub
            """,
            """
            overflow
            """);

        Add("d43_divzero",
            """
            Sub Main()
                Dim a As Decimal = 1
                Dim z As Decimal = 0
                Try
                    Console.WriteLine(a / z)
                Catch ex As DivideByZeroException
                    Console.WriteLine("divzero")
                End Try
            End Sub
            """,
            """
            divzero
            """);

        Add("e69_tryreturn",
            """
            Function SafeDiv(a As Decimal, b As Decimal) As Decimal
                Try
                    Return a / b
                Catch ex As DivideByZeroException
                    Return -1
                End Try
            End Function
            Sub Main()
                Dim a As Decimal = 7
                Dim b As Decimal = 2
                Dim z As Decimal = 0
                Console.WriteLine(SafeDiv(a, b))
                Console.WriteLine(SafeDiv(a, z))
            End Sub
            """,
            """
            3.5
            -1
            """);
    }

    // ========================================================================================
    // Runners.
    // ========================================================================================

    private static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();

    /// <summary>The real CLI to <c>.il</c> (plain or <c>--optimize</c>), then <c>ilasm</c> and a run.</summary>
    private static MsilRun RunViaCli(string source, bool optimize)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t129-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=msil" };
            if (optimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            var il = Path.Combine(dir, "Prog.il");
            if (exit != 0 || !File.Exists(il))
                return new MsilRun(MsilOutcome.GenerateFailed, "", "", $"CLI exited {exit}:\n{stdout}{stderr}");
            return RunIl(File.ReadAllText(il), "Prog");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>A Release <c>.blproj</c> build (<c>CompileProjectFiles</c>, aggressive pipeline), then <c>ilasm</c> and a run.</summary>
    private static MsilRun RunViaReleaseProject(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t129-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), source);
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
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "build", Path.Combine(dir, "App.blproj"), "-c", "Release" },
                dir, timeoutMs: 120_000);
            var il = Directory.GetFiles(dir, "App.il", SearchOption.AllDirectories).FirstOrDefault();
            if (exit != 0 || il == null)
                return new MsilRun(MsilOutcome.GenerateFailed, "", "", $"CLI build exited {exit}:\n{stdout}{stderr}");
            return RunIl(File.ReadAllText(il), "App");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static void AssertPrints(string probe, string entry, MsilRun run, string expected)
    {
        Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.Ran), $"{probe} [{entry}]: {run.Report}");
        if (run.Outcome == MsilOutcome.Ran)
            Assert.That(Norm(run.Output), Is.EqualTo(Norm(expected)), $"{probe} [{entry}] vs vbc");
    }

    // ========================================================================================
    // 1. The rows: each is one or more probes, every one run through the real CLI plain AND
    //    --optimize. Several probes share a row to keep the case count down; a failure names the
    //    probe and the entry point.
    // ========================================================================================

    [TestCase("declaration and literal", "d01_decl d02_lit")]
    [TestCase("arithmetic + - * /", "d06_arith")]
    [TestCase("backslash, Mod, unary minus", "d07_intdiv d08_mod d09_neg e60_intdivmix e61_modmix")]
    [TestCase("comparisons, If, constants", "d11_cmp d12_if e55_cmpconst")]
    [TestCase("comparison in For and While", "d39_for d40_while")]
    [TestCase("Select Case", "d13_select e46_selectint e64_guard e66_casenothing")]
    [TestCase("CInt, CLng, CType half-even", "d20_cint e44_ctype")]
    [TestCase("Integer/Long/Byte/Short widen to Decimal",
        "d14_mixint d15_mixlong d16_mixbyte e59_mixshort d22_widen e56_argwiden e31b_optional_int")]
    [TestCase("field, parameter, return, ByRef, global",
        "d26_param d27_ret d28_field e50_fieldinit e51_property e48_byrefelem e57_fieldbyref d29_global e63_globalfold")]
    [TestCase("WriteLine and &", "d25_output e53_concat")]
    [TestCase("Math.Round, Math.Abs, Decimal.Parse, MaxValue", "d35_round d36_abs d37_parse d38_max")]
    [TestCase("large literal", "d05b_bigint")]
    [TestCase("array, List, box/unbox, lambda, default",
        "d32_array d33_list e34b_boxonly e34c_unbox e68_lambda e62_nothing")]
    [TestCase("overflow, divide by zero, Return in Try", "d42_overflow d43_divzero e69_tryreturn")]
    public void Probes_PrintVbcsAnswer_ThroughTheRealCli_PlainAndOptimized(string row, string probes)
    {
        Assert.Multiple(() =>
        {
            foreach (var name in probes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.That(Probes.ContainsKey(name), Is.True, $"row '{row}': no probe named {name}");
                var (source, expected) = Probes[name];
                foreach (var optimize in new[] { false, true })
                    AssertPrints(name, optimize ? "CLI --optimize" : "CLI", RunViaCli(source, optimize), expected);
            }
        });
    }

    // ========================================================================================
    // 2. The IDE's route: a Release .blproj build (CompileProjectFiles, aggressive pipeline).
    //    Arithmetic, Select Case and CInt — the three that used to be a silent wrong answer.
    // ========================================================================================

    [Test]
    public void ReleaseBlprojBuild_ArithmeticSelectCaseAndCInt_PrintVbcsAnswer()
    {
        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "d06_arith", "d13_select", "d20_cint" })
                AssertPrints(name, "Release .blproj", RunViaReleaseProject(Probes[name].Source), Probes[name].Expected);
        });
    }
}

/// <summary>
/// Task #129 — the shape of the IL, with no assembler: the cheap, fast-subset witnesses for what the
/// execution rows above prove by running (and the only ones that still say something on a machine
/// without <c>ilasm</c>).
/// </summary>
[TestFixture]
public class MsilDecimalIlShapeTests
{
    private const string Program = """
        Sub Main()
            Dim d As Decimal = 1.5
            Dim w As Decimal = 3
            Dim big As Decimal = 9223372036854775807
            Dim neg As Decimal = -2.75
            Dim s As Decimal = d + w
            If d < w Then Console.WriteLine(CInt(s))
            Console.WriteLine(big)
            Console.WriteLine(neg)
        End Sub
        """;

    private static string Il(bool aggressive) =>
        MsilHarness.CompileToIl(Program, aggressive: aggressive);

    /// <summary>
    /// ONE spelling of the type, <c>valuetype [mscorlib]System.Decimal</c>, in the <c>.locals</c> and in the
    /// signatures; an operator is a CALL to <c>op_Addition</c>/<c>op_LessThan</c>, never <c>add</c>/<c>clt</c>;
    /// <c>CInt</c> is <c>Convert.ToInt32</c> (half-to-even), never <c>conv.i4</c>. The old spelling was
    /// <c>[System.Runtime]</c> and was sanitized into <c>class 'valuetypeSystemRuntimeSystemDecimal'</c>.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Decimal_IsOneValueType_AndEveryOperatorIsAMethodCall(bool aggressive)
    {
        var il = Il(aggressive);
        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("valuetype [mscorlib]System.Decimal 'd'"), ".locals spelling");
            Assert.That(il, Does.Contain("System.Decimal::op_Addition(valuetype [mscorlib]System.Decimal, valuetype [mscorlib]System.Decimal)"));
            Assert.That(il, Does.Contain("System.Decimal::op_LessThan("));
            Assert.That(il, Does.Contain("System.Convert::ToInt32(valuetype [mscorlib]System.Decimal)"));
            Assert.That(il, Does.Not.Contain("System.Runtime"), "the old [System.Runtime] spelling");
            Assert.That(il, Does.Not.Contain("valuetypeSystem"), "the sanitized class name ilasm refused");
            Assert.That(Regex.IsMatch(il, @"^\s*(add|sub|mul|div|rem|neg|ceq|clt|cgt|conv\.i4)\s*$", RegexOptions.Multiline),
                Is.False, "a raw arithmetic/compare/convert opcode on a Decimal");
        });
    }

    /// <summary>
    /// A literal is rebuilt from its EXACT bits, as csc writes <c>1.5m</c>: a whole value in int range uses
    /// <c>.ctor(int32)</c>, one in long range <c>.ctor(int64)</c>, and anything else the five-argument
    /// constructor with its scale — 1.5 is (15, 0, 0, positive, 1), -2.75 is (275, 0, 0, negative, 2). Never
    /// <c>ldc.r8</c> (a Double has no 28 digits) and never the old "WARNING" comment plus <c>ldc.i4.0</c>.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void DecimalLiteral_IsRebuiltFromItsExactBits(bool aggressive)
    {
        var il = Il(aggressive);
        const string Five = @"newobj instance void \[mscorlib\]System\.Decimal::\.ctor\(int32, int32, int32, bool, uint8\)";
        Assert.Multiple(() =>
        {
            Assert.That(Regex.IsMatch(il, @"ldc\.i4(\.s)? 15\s+ldc\.i4\.0\s+ldc\.i4\.0\s+ldc\.i4\.0\s+ldc\.i4\.1\s+" + Five),
                Is.True, "1.5m = (15, 0, 0, false, 1)");
            Assert.That(Regex.IsMatch(il, @"ldc\.i4(\.s)? 275\s+ldc\.i4\.0\s+ldc\.i4\.0\s+ldc\.i4\.1\s+ldc\.i4\.2\s+" + Five),
                Is.True, "-2.75m = (275, 0, 0, true, 2)");
            Assert.That(il, Does.Contain("System.Decimal::.ctor(int32)"), "3 is a whole int32");
            Assert.That(il, Does.Contain("ldc.i8 9223372036854775807"));
            Assert.That(il, Does.Contain("System.Decimal::.ctor(int64)"), "Long.MaxValue is a whole int64");
            Assert.That(il, Does.Not.Contain("ldc.r8"), "a Decimal literal is never a Double");
            Assert.That(il, Does.Not.Contain("WARNING"));
        });
    }
}
