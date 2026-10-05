using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #181 — AscW/Asc, ChrW/Chr and CByte/CShort/CSByte/CUShort/CUInt/CULng are TYPED and LOWERED as VB does. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. `total + AscW(ch)` and `Return CByte(x) * 2` were refused on EVERY backend ("requires numeric operands"): none of the ten names was registered in
//  `SemanticAnalyzer.RegisterStdLibFunctions`, so each call typed Object. Where the front end let one through (`CInt(AscW(s)) + 1`) no backend lowered it either: CS0103 on C#, a C++
//  compile error, a ReferenceError on JavaScript, a refusal on MSIL. They are registered now (AscW/Asc -> Integer, ChrW/Chr -> Char, CByte -> Byte, CShort -> Short, CSByte -> SByte,
//  CUShort -> UShort, CUInt -> UInteger, CULng -> ULong, parameter Object as CInt's is) and each backend has an arm beside its CInt arm.
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module with Microsoft.VisualBasic imported (S/t181/probes + S/t181/tw/probes, the
//  `.exp` beside each `.bas`) — never what a backend printed. A GROUP is one test case: it runs each of its probes on every backend where that probe now RUNS, each through the spawned CLI,
//  the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive — what a Release .blproj build and the IDE call), and reports every failing cell by probe id. A backend whose
//  tool is missing is SKIPPED (`TempExec.RequireTool`: g++/clang++, Node, ilasm), never failed; the case is ignored only when no cell could run. Every C# cell is `HangSafe`: it runs in a
//  child process with a time limit (`CSharpProcessRunner`), never in the in-process runner that has no timeout (#256).
//
//  ⭐ WHICH CELLS EXIST. A probe's `Agrees` flags are the backends where it now runs; the others are the KNOWN GAPS below, each a pre-existing rule that is not #181's.
//
//  ⛔ KNOWN GAPS — each a defect or a decision that is NOT #181's, listed with NO test (asserting one would pin the defect):
//    FRONT  `Char < Char` is refused ("requires numeric operands"). (`Char & Char` was refused too until #190, which made `&` convert both operands to String: `ChrW(72) & ChrW(105)` compiles now.)
//    JS     bans Char locals and literals (BL7004), so every probe that holds one is not a JavaScript row (`AscW("A"c)`, `Dim c = Chr(66)`, `Dim c As Char = Chr(67)`).
//    FRONT  `s.Chars(i)` passes the front end and fails on EVERY backend (CS1061 / TypeError / MissingMethod / a C++ error). The probes read a character with `Mid(s, i, 1)` instead.
//    FRONT  a Char is not assignable to a String (`Dim s As String = Chr(65)`, `Return Chr(65)` from a String Function, a Char argument for a String parameter): refused, exactly as it was on master
//           when Chr was typed Object ("Cannot assign value of type 'Char' to variable of type 'String'"); vbc widens. Measured on master and #181 alike, so not a #181 regression.
//    C#     a STRING argument (`CByte(s)`) is emitted as `Microsoft.VisualBasic.CompilerServices.Conversions.ToByte(s)`, exactly as CInt's is, and the test harness's Roslyn compile does not reference
//           Microsoft.VisualBasic (CS0234) — so that cell cannot RUN here and `p16_String_argument` is not a C# row. `VbConversionIntrinsicCompileTests` pins the emitted text instead.
//    C#     `Len("ab" & G())` prints "ab1": `EmitLen` does not parenthesize its receiver — task #275. The probes use `Len(t)` of a variable.
//    ALL    integer overflow is UNCHECKED, as CInt's is (`CByte(300)` wraps): task #272, the owner's decision. No row converts an out-of-range value.
//    C++    has no Object, so a probe that declares one (`Dim o As Object = Chr(67)`) is not a C++ row.
//
//  ⭐ MUTANTS (S/t181/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped):
//    m1 `AscW` not registered in the front end:  `p01_AscW_ForEachChar` and `p08_AscW_hash_loop` (compile refused on every backend), and every other row that calls AscW.
//    m2 the C++ narrow arm truncates instead of `std::nearbyint`:  `p05_...`, `p06_...` and `p14_...` — C++ cells only (CByte(3.5) prints 3, CUInt(5.5) prints 5).
//    m3 JavaScript's `IsCIntCall` is CInt-only:  `p05_...` and `p09_...` — JavaScript cells only (ReferenceError: __blCInt is not defined).
//
//  ⚠ Named "…ExecutionTests" on purpose: its rows RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>A named set of probes that is ONE test case, so the table stays small and each probe keeps its own id in a failure.</summary>
public sealed record ProbeGroup(string Id, TempProbe[] Probes)
{
    public override string ToString() => Id;
}

/// <summary>
/// #181 RUN: AscW/Asc of a Char and of a String, ChrW/Chr leaving a Char, CByte..CULng at the half-way points and in arithmetic, a user function named like an intrinsic, and #171's S6 —
/// each prints vbc's answer on every backend where it runs, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class VbConversionIntrinsicExecutionTests
{
    private const Bk Four = Bk.All;
    private const Bk NotJs = Bk.CSharp | Bk.Cpp | Bk.Msil;     // JS bans Char locals and literals (BL7004), and ULong (BL7003)
    private const Bk NotCpp = Bk.CSharp | Bk.JavaScript | Bk.Msil; // C++ has no Object
    private const Bk NotCSharp = Bk.Cpp | Bk.JavaScript | Bk.Msil;  // the C# test harness cannot compile a String argument (see the header)

    // ================================================================================================
    // Group 1 — AscW / Asc read a Char AND a String. Kills m1 (p01, p08).
    // ================================================================================================

    /// <summary>`total + AscW(ch)` over a `For Each ch As Char` — the original refusal ("requires numeric operands"). Kills m1.</summary>
    internal static readonly TempProbe p01_AscW_ForEachChar = new("p01_AscW_ForEachChar", """
        Sub Main()
            Dim s As String = "ABC"
            Dim total As Integer = 0
            For Each ch As Char In s
                total = total + AscW(ch)
            Next
            Console.WriteLine(total)
        End Sub
        """, "198", Four, HangSafe: true);

    /// <summary>`Asc` of a Char in a loop, and `Asc("z") * 2` — the result is an Integer you can multiply.</summary>
    internal static readonly TempProbe p02_Asc_ForEachChar_and_literal = new("p02_Asc_ForEachChar_and_literal", """
        Sub Main()
            Dim s As String = "abc"
            Dim total As Integer = 0
            For Each ch As Char In s
                total = total + Asc(ch)
            Next
            Console.WriteLine(total)
            Dim t = Asc("z") * 2
            Console.WriteLine(t)
        End Sub
        """, """
        294
        244
        """, Four, HangSafe: true);

    /// <summary>AscW of a STRING (the first character, `AscW(s)`) beside AscW of a one-character `Mid` — a String receiver is not a Char receiver on C++ and MSIL.</summary>
    internal static readonly TempProbe p03_AscW_of_String_vs_Mid = new("p03_AscW_of_String_vs_Mid", """
        Sub Main()
            Dim s As String = "Hello"
            Dim b = AscW(Mid(s, 1, 1))
            Dim c = AscW(s)
            Console.WriteLine(b - 1)
            Console.WriteLine(c * 2)
            Console.WriteLine(AscW(Mid(s, 5, 1)) + Asc(Mid(s, 2, 1)))
        End Sub
        """, """
        71
        144
        212
        """, Four, HangSafe: true);

    /// <summary>AscW / Asc of a Char LITERAL (`"A"c`) — a Char, not a String, so no `get_Chars` / `.at(0)`. Not JavaScript: BL7004.</summary>
    internal static readonly TempProbe p03b_AscW_of_a_Char_literal = new("p03b_AscW_of_a_Char_literal", """
        Sub Main()
            Dim a = AscW("A"c)
            Console.WriteLine(a + 1)
            Console.WriteLine(Asc("z"c) - 1)
        End Sub
        """, """
        66
        121
        """, NotJs, HangSafe: true);

    /// <summary>`h * 31 + AscW(Mid(s, i + 1, 1))` in a counted loop, `Mod`ed — a hash. Kills m1.</summary>
    internal static readonly TempProbe p08_AscW_hash_loop = new("p08_AscW_hash_loop", """
        Function Hash(s As String) As Integer
            Dim h As Integer = 0
            For i As Integer = 0 To s.Length - 1
                h = (h * 31 + AscW(Mid(s, i + 1, 1))) Mod 1000003
            Next
            Return h
        End Function
        Sub Main()
            Console.WriteLine(Hash("hello"))
        End Sub
        """, "162025", Four, HangSafe: true);

    // ================================================================================================
    // Group 2 — ChrW / Chr leave a CHAR (not a one-character String).
    // ================================================================================================

    /// <summary>`ChrW(65) & "x"`, a Chr in a bracketed concatenation, a constant arithmetic code, `Len` of a String built with Chr.</summary>
    internal static readonly TempProbe p04_ChrW_Chr_concat = new("p04_ChrW_Chr_concat", """
        Sub Main()
            Console.WriteLine(ChrW(65) & "x")
            Console.WriteLine("[" & Chr(66) & "]")
            Dim s As String = "" & ChrW(72) & ChrW(105)
            Console.WriteLine(s)
            Console.WriteLine(ChrW(97 + 2))
            Dim t As String = "ab" & Chr(99)
            Console.WriteLine(Len(t))
        End Sub
        """, """
        Ax
        [B]
        Hi
        c
        3
        """, Four, HangSafe: true);

    /// <summary>`Dim c = Chr(66)` infers a Char, so `c = "B"c` compares Char to Char. Not JavaScript: BL7004.</summary>
    internal static readonly TempProbe p04b_Chr_equals_a_Char_literal = new("p04b_Chr_equals_a_Char_literal", """
        Sub Main()
            Dim c = Chr(66)
            If c = "B"c Then
                Console.WriteLine("eq")
            Else
                Console.WriteLine("ne")
            End If
            If ChrW(67) <> "C"c Then Console.WriteLine("bad") Else Console.WriteLine("ok")
        End Sub
        """, """
        eq
        ok
        """, NotJs, HangSafe: true);

    /// <summary>`s & ChrW(AscW("a"c) + i)` in a loop (a non-constant code), and a `Dim c As Char = Chr(67)`. Not JavaScript: BL7004.</summary>
    internal static readonly TempProbe p10_ChrW_of_AscW_in_a_loop = new("p10_ChrW_of_AscW_in_a_loop", """
        Sub Main()
            Dim s As String = ""
            For i As Integer = 0 To 4
                s = s & ChrW(AscW("a"c) + i)
            Next
            Console.WriteLine(s)
            Dim c As Char = Chr(67)
            Console.WriteLine(c)
        End Sub
        """, """
        abcde
        C
        """, NotJs, HangSafe: true);

    /// <summary>The Chr/Asc shapes that already ran right before #181 (a boxed Chr, `CStr(Chr(..))`, `Asc(Chr(..))`) — the regression control. Not C++: no Object.</summary>
    internal static readonly TempProbe p11_Chr_Asc_older_shapes = new("p11_Chr_Asc_older_shapes", """
        Sub Main()
            Dim n = Asc("A")
            Console.WriteLine(n)
            Dim o As Object = Chr(67)
            Console.WriteLine(o)
            Console.WriteLine(Chr(68))
            Console.WriteLine(CStr(Chr(69)))
            Console.WriteLine(Asc(Chr(70)))
        End Sub
        """, """
        65
        C
        D
        E
        70
        """, NotCpp, HangSafe: true);

    // ================================================================================================
    // Group 3 — CByte..CUInt round HALF-TO-EVEN and type their arithmetic. Kills m2 (C++), m3 (JavaScript).
    // ================================================================================================

    /// <summary>`CByte(x) * 2` in a function, `CShort(x) + 1`, and the .5 cases `CByte(2.5)` = 2, `CByte(3.5)` = 4, `CShort(-2.5)` = -2. Kills m2 (C++: 3) and m3 (JavaScript: ReferenceError).</summary>
    internal static readonly TempProbe p05_CByte_CShort_in_arithmetic_half_even = new("p05_CByte_CShort_in_arithmetic_half_even", """
        Function Twice(x As Integer) As Integer
            Return CByte(x) * 2
        End Function
        Sub Main()
            Console.WriteLine(Twice(7))
            Dim x As Integer = 20
            Console.WriteLine(CShort(x) + 1)
            Console.WriteLine(CByte(2.5) + 0)
            Console.WriteLine(CByte(3.5) + 0)
            Console.WriteLine(CShort(-2.5) + 0)
        End Sub
        """, """
        14
        21
        2
        4
        -2
        """, Four, HangSafe: true);

    /// <summary>CSByte, CUShort and CUInt in arithmetic, with a Double operand (`CUInt(5.5)` = 6, `CSByte(4.5)` = 4, `CUShort(d) + CSByte(-d)` over 6.5). Kills m2 (C++: CUInt(5.5) prints 5).</summary>
    internal static readonly TempProbe p06_CSByte_CUShort_CUInt = new("p06_CSByte_CUShort_CUInt", """
        Sub Main()
            Dim x As Integer = 9
            Console.WriteLine(CSByte(x) - 10)
            Console.WriteLine(CUShort(x) * 3)
            Console.WriteLine(CUInt(x) + 1)
            Console.WriteLine(CSByte(4.5) + 0)
            Console.WriteLine(CUInt(5.5) + 0)
            Dim d As Double = 6.5
            Console.WriteLine(CUShort(d) + CSByte(-d))
        End Sub
        """, """
        -1
        27
        10
        4
        6
        0
        """, Four, HangSafe: true);

    /// <summary>EVERY half-way point from either side of zero: 0.5 -> 0, 1.5 -> 2, 2.5 -> 2, -0.5 -> 0, -1.5 -> -2 — half-to-even, never half-away-from-zero, on a variable and on a literal.</summary>
    internal static readonly TempProbe p14_round_at_point_five = new("p14_round_at_point_five", """
        Sub Main()
            Dim d As Double = 0.5
            Dim e As Double = 1.5
            Console.WriteLine(CByte(d) + 0)
            Console.WriteLine(CByte(e) + 0)
            Console.WriteLine(CUShort(2.5) + 0)
            Console.WriteLine(CShort(-0.5) + 0)
            Console.WriteLine(CShort(-1.5) + 0)
            Console.WriteLine(CUInt(0.5) + 0)
        End Sub
        """, """
        0
        2
        2
        0
        -2
        0
        """, Four, HangSafe: true);

    /// <summary>`Dim b As Byte = CByte(200)`, a Short and a UInteger local, and `k + 5` over `Dim k = CByte(10)` — the converted value keeps its narrow type through a local. Kills m3 (JavaScript).</summary>
    internal static readonly TempProbe p09_typed_locals = new("p09_typed_locals", """
        Sub Main()
            Dim b As Byte = CByte(200)
            Dim sh As Short = CShort(1000)
            Dim u As UInteger = CUInt(7)
            Console.WriteLine(b)
            Console.WriteLine(sh)
            Console.WriteLine(u)
            Dim k = CByte(10)
            Dim m = k + 5
            Console.WriteLine(m)
        End Sub
        """, """
        200
        1000
        7
        15
        """, Four, HangSafe: true);

    // ================================================================================================
    // Group 4 — Boolean, String and ULong arguments.
    // ================================================================================================

    /// <summary>`CByte(True)` is 255 — a Boolean is ALL BITS SET at the target's width (CShort/CSByte -1, CUShort 65535, CUInt 4294967295) — and `CByte(False) + 1` is 1; `CByte(True) * 2` is 510.</summary>
    internal static readonly TempProbe p15_Boolean_is_all_bits_set = new("p15_Boolean_is_all_bits_set", """
        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Console.WriteLine(CByte(t))
            Console.WriteLine(CShort(t))
            Console.WriteLine(CSByte(t))
            Console.WriteLine(CUShort(t))
            Console.WriteLine(CUInt(t))
            Console.WriteLine(CByte(f) + 1)
            Console.WriteLine(CByte(True) * 2)
        End Sub
        """, """
        255
        -1
        -1
        65535
        4294967295
        1
        510
        """, Four, HangSafe: true);

    /// <summary>A STRING argument is parsed (`CByte("7") + 1` = 8) — a variable and a literal. Not C#: the emitted call is VB's `Conversions.ToByte(s)`, which the test harness cannot compile (the header).</summary>
    internal static readonly TempProbe p16_String_argument = new("p16_String_argument", """
        Sub Main()
            Dim s As String = "7"
            Console.WriteLine(CByte(s) + 1)
            Console.WriteLine(CShort("12") * 2)
            Console.WriteLine(CUInt("40") - 1)
        End Sub
        """, """
        8
        24
        39
        """, NotCSharp, HangSafe: true);

    /// <summary>`CULng` on an Integer and a rounded Double. Not JavaScript: ULong is not representable (the refusal below).</summary>
    internal static readonly TempProbe p06b_CULng = new("p06b_CULng", """
        Sub Main()
            Dim x As Integer = 9
            Console.WriteLine(CULng(x) * 4)
            Dim d As Double = 4.5
            Console.WriteLine(CULng(d) + 0)
        End Sub
        """, """
        36
        4
        """, NotJs, HangSafe: true);

    // ================================================================================================
    // Group 5 — a user function named like an intrinsic WINS.
    // ================================================================================================

    /// <summary>The program's OWN `AscW(String)` and `ChrW(Integer)` (before #181 these names were no builtins, so a user's was the only one there was): `AscW("abc") + 1` is 1004, `ChrW(65)` is `<65>`.</summary>
    internal static readonly TempProbe p13_user_AscW_ChrW_wins = new("p13_user_AscW_ChrW_wins", """
        Function AscW(s As String) As Integer
            Return 1000 + s.Length
        End Function
        Function ChrW(n As Integer) As String
            Return "<" & n & ">"
        End Function
        Sub Main()
            Console.WriteLine(AscW("abc") + 1)
            Console.WriteLine(ChrW(65) & "!")
        End Sub
        """, """
        1004
        <65>!
        """, Four, HangSafe: true);

    /// <summary>The program's OWN `CByte(Integer) As Integer` (BasicLang accepts a VB keyword as a name; the expectation 701 is by construction): the user's wins, not the Byte conversion.</summary>
    internal static readonly TempProbe p13b_user_CByte_wins = new("p13b_user_CByte_wins", """
        Function CByte(x As Integer) As Integer
            Return x * 100
        End Function
        Sub Main()
            Console.WriteLine(CByte(7) + 1)
        End Sub
        """, "701", Four, HangSafe: true);

    // ================================================================================================
    // Group 6 — #171's S6, and CInt as the control.
    // ================================================================================================

    /// <summary>⭐ #171's S6, the original repro: `total + AscW(ch)` over a String with an INFERRED `For Each ch In "AB"` — 65 + 66 = 131 on all four backends (it was refused on all four).</summary>
    internal static readonly TempProbe s6_total_plus_AscW_over_a_String = new("s6_total_plus_AscW_over_a_String", """
        Sub Main()
            Dim total As Integer = 0
            For Each ch In "AB"
                total = total + AscW(ch)
            Next
            Console.WriteLine(total)
        End Sub
        """, "131", Four, HangSafe: true);

    /// <summary>The CInt CONTROL (its half-to-even, `CInt(AscW(s)) + 1` over a String) — the arm the new ones sit beside must not have moved.</summary>
    internal static readonly TempProbe p07_CInt_control = new("p07_CInt_control", """
        Sub Main()
            Dim x As Double = 2.5
            Console.WriteLine(CInt(x) * 2)
            Console.WriteLine(CInt(3.5) + 0)
            Dim s As String = "A"
            Console.WriteLine(CInt(AscW(s)) + 1)
        End Sub
        """, """
        4
        4
        66
        """, Four, HangSafe: true);

    internal static readonly IReadOnlyList<ProbeGroup> Groups = new[]
    {
        new ProbeGroup("AscW_and_Asc_read_a_Char_and_a_String",
            new[] { p01_AscW_ForEachChar, p02_Asc_ForEachChar_and_literal, p03_AscW_of_String_vs_Mid, p03b_AscW_of_a_Char_literal, p08_AscW_hash_loop }),
        new ProbeGroup("ChrW_and_Chr_leave_a_Char",
            new[] { p04_ChrW_Chr_concat, p04b_Chr_equals_a_Char_literal, p10_ChrW_of_AscW_in_a_loop, p11_Chr_Asc_older_shapes }),
        new ProbeGroup("CByte_to_CUInt_round_half_to_even_and_type_their_arithmetic",
            new[] { p05_CByte_CShort_in_arithmetic_half_even, p06_CSByte_CUShort_CUInt, p14_round_at_point_five, p09_typed_locals }),
        new ProbeGroup("a_Boolean_a_String_and_a_ULong_argument",
            new[] { p15_Boolean_is_all_bits_set, p16_String_argument, p06b_CULng }),
        new ProbeGroup("a_user_function_named_like_an_intrinsic_wins",
            new[] { p13_user_AscW_ChrW_wins, p13b_user_CByte_wins }),
        new ProbeGroup("S6_total_plus_AscW_over_a_String_and_the_CInt_control",
            new[] { s6_total_plus_AscW_over_a_String, p07_CInt_control }),
    };

    private static IEnumerable<TestCaseData> GroupCells() => Groups.Select(g => new TestCaseData(g).SetName(g.Id));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a probe cannot vanish, and a backend cannot be dropped from one, without this test saying so. It is also the fixture's one plain [Test]
    /// besides its case source: <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        var probes = Groups.SelectMany(g => g.Probes).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(probes.Select(p => p.Id), Is.EqualTo(new[]
            {
                "p01_AscW_ForEachChar", "p02_Asc_ForEachChar_and_literal", "p03_AscW_of_String_vs_Mid", "p03b_AscW_of_a_Char_literal", "p08_AscW_hash_loop",
                "p04_ChrW_Chr_concat", "p04b_Chr_equals_a_Char_literal", "p10_ChrW_of_AscW_in_a_loop", "p11_Chr_Asc_older_shapes",
                "p05_CByte_CShort_in_arithmetic_half_even", "p06_CSByte_CUShort_CUInt", "p14_round_at_point_five", "p09_typed_locals",
                "p15_Boolean_is_all_bits_set", "p16_String_argument", "p06b_CULng",
                "p13_user_AscW_ChrW_wins", "p13b_user_CByte_wins",
                "s6_total_plus_AscW_over_a_String", "p07_CInt_control",
            }));
            Assert.That(probes.Select(p => p.Id).Distinct().Count(), Is.EqualTo(probes.Count));
            Assert.That(probes.Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# cell runs in a child process with a time limit (#256)");
            // The known gaps are DROPPED backends, and each is named: Char literals and locals and ULong are not JavaScript (BL7004 / BL7003), Object is not C++.
            Assert.That(probes.Where(p => p.Agrees == NotJs).Select(p => p.Id),
                Is.EqualTo(new[] { "p03b_AscW_of_a_Char_literal", "p04b_Chr_equals_a_Char_literal", "p10_ChrW_of_AscW_in_a_loop", "p06b_CULng" }));
            Assert.That(probes.Where(p => p.Agrees == NotCpp).Select(p => p.Id), Is.EqualTo(new[] { "p11_Chr_Asc_older_shapes" }));
            Assert.That(probes.Where(p => p.Agrees == NotCSharp).Select(p => p.Id), Is.EqualTo(new[] { "p16_String_argument" }));
            Assert.That(probes.Where(p => p.Agrees == Four).Count(), Is.EqualTo(14));
            Assert.That(probes.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(14 * 4 + 4 * 3 + 1 * 3 + 1 * 3), "20 probes, each on every backend it runs on, each through three entry points");
        });
    }

    // ============================================================================================
    // RUN — vbc's answer, every backend the probe runs on, three entry points
    // ============================================================================================

    /// <summary>
    /// Each probe of the group on every backend it runs on, through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A call
    /// that is not typed is a refusal (m1); a narrow conversion that is not lowered is a ReferenceError on JavaScript (m3); one that truncates where VB rounds is a wrong number on C++ (m2).
    /// A backend whose tool is missing is skipped; the test is ignored only when none could run.
    /// </summary>
    [TestCaseSource(nameof(GroupCells))]
    public void AGroupOfProbes_PrintsVbcsAnswer_OnEveryBackendItRuns(ProbeGroup group)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in group.Probes)
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
        if (ran == 0) Assert.Ignore($"{group.Id}: no execution tool on this machine ({skipped} cells skipped).");
    }

    // ============================================================================================
    // REFUSED — the CLI and the project build say no, and write nothing
    // ============================================================================================

    /// <summary>
    /// The three deliberate refusals, through the spawned CLI (standard and `--optimize`) and through CompileProjectFiles + the backend (what the IDE build calls): a constant `Chr` code above 127 on
    /// C++ (a silent single-byte truncation otherwise), `CULng` on JavaScript (ULong is not representable) and p12 — `Dim s = Chr(65): s = s & "b"` — which is a String assigned to an inferred
    /// CHAR (vbc's Option Strict On answer, BC30512; it used to compile and print a wrong "Ab" on C#/JavaScript/MSIL). The in-process legs live in <c>VbConversionIntrinsicCompileTests</c>.
    /// </summary>
    [Test]
    public void TheRefusals_AreRefused_ThroughTheCliAndTheProjectBuild()
    {
        var cases = new (string Name, Bk Backend, string Source, string Expect)[]
        {
            ("Chr(200) on C++", Bk.Cpp, "Sub Main()\n    Console.WriteLine(\"[\" & Chr(200) & \"]\")\nEnd Sub\n", "names a character outside U+0000..U+007F"),
            ("CULng on JavaScript", Bk.JavaScript, "Sub Main()\n    Dim x As Integer = 9\n    Console.WriteLine(CULng(x) * 4)\nEnd Sub\n", "no lowering for 'CULng'"),
            ("p12 (String to Char) on C#", Bk.CSharp, "Sub Main()\n    Dim s = Chr(65)\n    s = s & \"b\"\n    Console.WriteLine(s)\nEnd Sub\n", "Cannot assign value of type 'String' to 'Char'"),
            ("p12 (String to Char) on MSIL", Bk.Msil, "Sub Main()\n    Dim s = Chr(65)\n    s = s & \"b\"\n    Console.WriteLine(s)\nEnd Sub\n", "Cannot assign value of type 'String' to 'Char'"),
        };

        var failures = new List<string>();
        foreach (var (name, backend, source, expect) in cases)
        {
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                try
                {
                    var said = entry == EntryPoint.ProjectRelease ? RefusalViaProject(backend, source) : RefusalViaCli(backend, entry, source);
                    if (!said.Contains(expect, StringComparison.Ordinal)) failures.Add($"{name}, {entry}: refused, but not with [{expect}]: {said.Replace("\n", " | ")}");
                }
                catch (AssertionException ex)
                {
                    failures.Add($"{name}, {entry}: {ex.Message.Split('\n')[0]}");
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>The spawned CLI: a non-zero exit, the diagnostic on its output, and NO emitted file. Returns the output.</summary>
    private static string RefusalViaCli(Bk backend, EntryPoint entry, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t181-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + TempExec.TargetName(backend) };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            Assert.That(exit, Is.Not.EqualTo(0), $"the CLI accepted the program ({TempExec.TargetName(backend)}, {entry}):\n{stdout}{stderr}");
            Assert.That(File.Exists(Path.Combine(dir, "Prog" + TempExec.Extension(backend))), Is.False, "a refused program left an output file behind");
            return stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>CompileProjectFiles (aggressive), then the backend: either the project's front end reports the error, or the backend throws. Returns the message.</summary>
    private static string RefusalViaProject(Bk backend, string source)
    {
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
        var dir = Path.Combine(Path.GetTempPath(), "bl-t181-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var result = compiler.CompileProjectFiles(new List<string> { path });
            if (result.HasErrors) return string.Join(" | ", result.AllErrors.Select(e => e.Message));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            var ir = result.CombinedIR;
            try
            {
                _ = backend switch
                {
                    Bk.Cpp => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir),
                    Bk.JavaScript => new JavaScriptCodeGenerator().Generate(ir),
                    Bk.Msil => new BasicLang.Compiler.CodeGen.MSIL.MSILCodeGenerator().Generate(ir),
                    _ => new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(ir),
                };
            }
            catch (CppCapabilityException ex)
            {
                return string.Join(" | ", ex.Diagnostics);
            }
            catch (NotSupportedException ex)
            {
                return ex.Message;
            }
            catch (ForeignFeatureException ex)
            {
                return ex.Message;
            }

            Assert.Fail($"the project build accepted the program on {backend}");
            return "";
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }
}

/// <summary>
/// #181 COMPILE, in process — no spawned CLI, no native compile, so it runs in the fast subset. Each refusal is a rule that already existed or a deliberate one, now reached through the new
/// typing: a constant `Chr`/`ChrW` code above U+007F on C++, `CULng` on JavaScript, and a String assigned to an inferred Char (p12). Plus the one C# shape the run harness cannot execute.
/// </summary>
[TestFixture]
public class VbConversionIntrinsicCompileTests
{
    private static string Wrap(string expr) => $"Sub Main()\n    Console.WriteLine(\"[\" & {expr} & \"]\")\nEnd Sub";

    /// <summary>
    /// C++ represents a Char as an 8-bit `char`, so a CONSTANT code above 127 would be truncated to a single byte — a silent wrong character — and is refused, as a non-ASCII Char literal is.
    /// 127 is the last code that is accepted. Through the standard AND the aggressive passes: the constant must survive the optimizer for the arm to see it.
    /// </summary>
    [TestCase("Chr(128)", true)]
    [TestCase("ChrW(955)", true)]
    [TestCase("Chr(127)", false)]
    public void Cpp_AConstantChrCodeAbove127_IsRefused(string call, bool refused)
    {
        var source = Wrap(call);
        foreach (var compile in new (string Name, Func<string, string> Run)[] { ("standard", src => BclE2E.CompileToCppOptimized(src)), ("aggressive", src => BclE2E.CompileToCppAggressive(src)) })
        {
            if (refused)
            {
                var ex = Assert.Throws<CppCapabilityException>(() => compile.Run(source), compile.Name);
                Assert.That(string.Join(" | ", ex!.Diagnostics), Does.Contain("names a character outside U+0000..U+007F"), compile.Name);
            }
            else
            {
                var cpp = compile.Run(source);
                Assert.That(cpp, Does.Contain("static_cast<char>(127)"), compile.Name);
            }
        }
    }

    /// <summary>
    /// C#'s String-argument arm is VB's `Conversions.ToByte(s)` (as CInt's is): the run harness cannot compile a reference to Microsoft.VisualBasic, so `p16_String_argument` is not a C# row
    /// and the emitted TEXT is pinned here (a String variable).
    /// </summary>
    [TestCase("Dim s As String = \"7\"\n    Console.WriteLine(CByte(s) + 1)", "Conversions.ToByte(s)")]
    public void CSharp_AConversionOfAString_GoesThroughVbConversions(string body, string expected)
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest($"Sub Main()\n    {body}\nEnd Sub");
        Assert.That(cs, Does.Contain("Microsoft.VisualBasic.CompilerServices." + expected));
    }

    /// <summary>`CULng` has no JavaScript lowering (ULong is not representable there): refused by name, through the standard and the aggressive passes — never a ReferenceError at run time.</summary>
    [Test]
    public void JavaScript_CULng_IsRefused()
    {
        const string source = "Sub Main()\n    Dim x As Integer = 9\n    Console.WriteLine(CULng(x) * 4)\nEnd Sub";
        var standard = Assert.Throws<NotSupportedException>(() => JsTestSupport.Compile(source));
        var aggressive = Assert.Throws<NotSupportedException>(() => JsTestSupport.CompileAggressive(source));
        Assert.Multiple(() =>
        {
            Assert.That(standard!.Message, Does.Contain("no lowering for 'CULng'"));
            Assert.That(aggressive!.Message, Does.Contain("no lowering for 'CULng'"));
        });
    }

    /// <summary>
    /// p12: `Dim s = Chr(65)` infers a CHAR now (Chr is typed), so `s = s &amp; "b"` assigns a String to a Char — vbc's Option Strict On answer (BC30512). It used to compile and print a wrong
    /// "Ab" on C#, JavaScript and MSIL (vbc with Option Strict Off prints "A"). A front-end rule, so no backend is reached.
    /// </summary>
    [Test]
    public void AStringAssignedToAnInferredChar_IsRefused_ByTheFrontEnd()
    {
        const string source = "Sub Main()\n    Dim s = Chr(65)\n    s = s & \"b\"\n    Console.WriteLine(s)\nEnd Sub";
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();

        Assert.That(analyzer.Analyze(ast), Is.False, "a String cannot be assigned to the Char that Chr infers");
        Assert.That(string.Join(" | ", analyzer.Errors.Select(e => e.Message)), Does.Contain("Cannot assign value of type 'String' to 'Char'"));
    }
}
