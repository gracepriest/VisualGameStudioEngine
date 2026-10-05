using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #184 — a Char WIDENS to a String, the VB way (legal under Option Strict On), on every backend. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. `Dim s As String = c`, `= Chr(65)`, `Return c` from an As String function, a Char argument for a String parameter, `For Each s As String In "ab"`, `s = c`, a field, a module
//  variable, a Const, an Optional default and an array-literal element were all refused ("Cannot assign value of type 'Char' to variable of type 'String'"). Where the front end let a shape through,
//  the backends broke on it: `Case "a"c` in a String Select was CS0029 on C#, did not build on C++ and threw InvalidProgramException on MSIL; `c = "a"` was CS0019 on C# and did not build on C++.
//  Now `TypeInfo.IsAssignableFrom` admits scalar Char -> String (ONE table, `TypeInfo.IsCharToStringWidening`, which the IR shares) and `IRBuilder.WidenCharToString` converts: a literal is
//  re-typed in place, anything else becomes `CStr(value)`. String -> Char stays refused (BC30512), and so do the three sites vbc also refuses (below).
//
//  ⭐ THE ORACLE IS vbc (Option Strict On). Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module with Microsoft.VisualBasic imported (S/t184/probes, the `.exp` beside
//  each `.bas`) — never what a backend printed. A GROUP is one test case: it runs each of its probes on every backend where that probe now RUNS, each through the spawned CLI, the CLI with
//  `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive — what a Release .blproj build and the IDE call), and reports every failing cell by probe id. A backend whose tool is missing is
//  SKIPPED (`TempExec.RequireTool`: g++/clang++, Node, ilasm), never failed; the case is ignored only when no cell could run. Every C# cell is `HangSafe`: a child process with a time limit
//  (`CSharpProcessRunner`), never the in-process runner that has no timeout (#256).
//
//  ⭐ WHICH CELLS EXIST. C#, C++ and MSIL run the shapes in the five groups; JavaScript has its own group, because it refuses a Char local, parameter or field (BL7004) and so runs only the shapes that
//  hold none (a `Chr(65)` initialiser, a Const, an Optional default, a lambda returning a Char literal, a Char literal in a Case, a String For Each variable over a String). A probe's `Agrees` flags
//  are the backends where it now runs; the others are the KNOWN GAPS below.
//
//  ⛔ KNOWN GAPS — each a defect or a decision that is NOT #184's, each measured with a Char-free control on the commit before it, listed with NO test (asserting one would pin the defect):
//    MSIL   String `=` is a REFERENCE compare (ceq): `"x" & 5 = "x5"` is False there. So `c = "a"` (probe c12) is still wrong on MSIL and is not an MSIL row (#205).
//    C#     rejects a NON-CONSTANT String `Case` value (CS9135: `Case t`, `Case c`) and a RELATIONAL String pattern (CS8781: `Case Is >= "n"`, `Case "a" To "m"`): c11v is not a C# row, c11r is C++-only here.
//    MSIL   has no `Case Is >=` lowering for a String, and fails on a `Char()` array ("undefined class Char"): c11r is not an MSIL row and c20 (For Each over a Char array) is C# and C++ only.
//    ALL    arguments to BCL collection members are never typed, so `l.Add(c)` into a `List(Of String)` is CS1503 on C#, a C++ compile error and InvalidProgramException on MSIL (#278).
//    ALL    a .NET member's arguments get no conversion at all: `"abc".StartsWith(c)`, `Path.Combine("dir", c)`, `New Exception(c)`, `String.Concat(c, "x")` (#278).
//    FRONT  overloading a method by PARAMETER TYPE is refused ("already defined in this scope"), so `F(String)` beside `F(Object)` cannot say which one a Char picks (#279).
//    FRONT  a user Operator with a Char operand stays refused ("Arithmetic operator '+' requires numeric operands"): the binding carries no parameter type for the IR to convert to (#279).
//    JS     refuses a Char local, parameter or field (BL7004) however it is used: one refusal row below, nothing else.
//
//  ⭐ MUTANTS (S/t184: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    m1 `IsAssignableFrom`'s Char -> String arm dropped:  the widening is refused everywhere — groups 1-4 and the JavaScript group (c01 and c16, c05 and c18 by name, on every backend), the
//       moved `ForEachOverStringTests.ExplicitString_IsAccepted_AndPrintsVbcsAnswer`, and every fast row here.
//    m2 `WidenCharToString` skips the `CStr(...)` (a non-literal passes through as a Char):  groups 1-5 (C#: CS0029 / CS1503 / CS0019, C++: no matching function, MSIL: InvalidProgramException /
//       NullReferenceException; c04 fails on all three), the moved E9 test, and the fast `ANonLiteralChar_BecomesACStrCall_AndALiteralIsRetypedInPlace`. The JavaScript group passes: a Char never reaches it.
//    m3 the ByRef refusal dropped (a Char into a ByRef String admitted):  ONLY the fast `AKeptRefusal_IsStillRefused` row "a Char into a ByRef String" fails. The ByVal c04 is not affected, which is the point.
//
//  ⚠ Named "…ExecutionTests" on purpose: its JavaScript group RUNS under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #184 RUN: a Char widens to a String in a declaration, an assignment, a Const, a Return, an argument, a delegate call, a field, a module variable, an array literal, a constructor and MyBase
/// argument, an Optional default, a For Each variable and a String Select — each prints vbc's answer on every backend where it runs, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class CharWidensToStringExecutionTests
{
    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;   // JavaScript refuses a Char local, parameter or field (BL7004): its shapes are the JavaScript group
    private const Bk NotMsil = Bk.CSharp | Bk.Cpp;           // MSIL: a Char() array is "undefined class Char", and String `=` is a reference compare (#205)
    private const Bk NotCSharp = Bk.Cpp | Bk.Msil;           // C#: a non-constant String Case value is CS9135
    private const Bk CppOnly = Bk.Cpp;                       // C#: CS8781, MSIL: no `Case Is >=` lowering for a String (JavaScript has its own row)

    // ================================================================================================
    // Group 1 — a declaration, Chr(65), an assignment and a Const. Kills m1 (c01, c16) and m2.
    // ================================================================================================

    /// <summary>`Dim s As String = c` (a Char VARIABLE: `CStr(c)`) beside `Dim t As String = "z"c` (a literal: re-typed in place). Kills m1 and m2.</summary>
    internal static readonly TempProbe c01_Dim_from_a_Char_variable_and_a_Char_literal = new("c01_Dim_from_a_Char_variable_and_a_Char_literal", """
        Sub Main()
            Dim c As Char = "A"c
            Dim s As String = c
            Console.WriteLine(s & "!")
            Dim t As String = "z"c
            Console.WriteLine(t.Length)
        End Sub
        """, """
        A!
        1
        """, Three, HangSafe: true);

    /// <summary>`Dim s As String = Chr(65)` and `ChrW(66)`: Chr is typed Char (#181), so this is the same widening with no Char variable in the source.</summary>
    internal static readonly TempProbe c02_Dim_from_Chr_and_ChrW = new("c02_Dim_from_Chr_and_ChrW", """
        Sub Main()
            Dim s As String = Chr(65)
            Dim t As String = ChrW(66)
            Console.WriteLine(s & t)
        End Sub
        """, "AB", Three, HangSafe: true);

    /// <summary>`s = c` and `s = "y"c` onto a String that already holds a value — an assignment, not a declaration.</summary>
    internal static readonly TempProbe c06_assignment_s_eq_c = new("c06_assignment_s_eq_c", """
        Sub Main()
            Dim s As String = "start"
            Dim c As Char = "x"c
            s = c
            Console.WriteLine(s)
            s = "y"c
            Console.WriteLine(s & s)
        End Sub
        """, """
        x
        yy
        """, Three, HangSafe: true);

    /// <summary>`Const K As String = "k"c` at module level and inside a Sub (C# writes a Const's value into the declaration). Kills m1 ("Constant value type 'Char' is not compatible").</summary>
    internal static readonly TempProbe c16_Const_from_a_Char_literal = new("c16_Const_from_a_Char_literal", """
        Const MK As String = "m"c
        Sub Main()
            Const K As String = "k"c
            Console.WriteLine(K & MK & K.Length)
        End Sub
        """, "km1", Three, HangSafe: true);

    // ================================================================================================
    // Group 2 — a Return, an argument, a delegate call and a lambda return.
    // ================================================================================================

    /// <summary>`Return c` and `Return "q"c` from an `As String` function.</summary>
    internal static readonly TempProbe c03_Return_a_Char_from_a_String_function = new("c03_Return_a_Char_from_a_String_function", """
        Function Wrap(c As Char) As String
            Return c
        End Function
        Function Lit() As String
            Return "q"c
        End Function
        Sub Main()
            Console.WriteLine(Wrap("h"c) & Lit())
        End Sub
        """, "hq", Three, HangSafe: true);

    /// <summary>A Char VARIABLE and a Char literal passed ByVal to a String parameter — `Show(c)` and `Show("m"c)`. Kills m2 (the by-value argument: CS1503 / no matching `Show` / InvalidProgram).</summary>
    internal static readonly TempProbe c04_ByVal_Char_argument = new("c04_ByVal_Char_argument", """
        Sub Show(s As String)
            Console.WriteLine("[" & s & "]" & s.Length)
        End Sub
        Sub Main()
            Dim c As Char = "k"c
            Show(c)
            Show("m"c)
        End Sub
        """, """
        [k]1
        [m]1
        """, Three, HangSafe: true);

    /// <summary>A delegate called with a Char: an `Action(Of String)` and a `Func(Of String, Integer)` (`f(c)`, `g(c)`) — the delegate-call argument path, not the named-procedure one.</summary>
    internal static readonly TempProbe c22_delegate_call_with_a_Char = new("c22_delegate_call_with_a_Char", """
        Sub Main()
            Dim f As Action(Of String) = Sub(x As String) Console.WriteLine("<" & x & ">")
            Dim c As Char = "h"c
            f(c)
            Dim g As Func(Of String, Integer) = Function(x As String) x.Length
            Console.WriteLine(g(c))
        End Sub
        """, """
        <h>
        1
        """, Three, HangSafe: true);

    /// <summary>A lambda that returns a Char literal on one path and a String on the other (the dominant return type is String), read through a String function.</summary>
    internal static readonly TempProbe c26_lambda_returns_a_Char_literal_and_a_String = new("c26_lambda_returns_a_Char_literal_and_a_String", """
        Function Pick(b As Boolean) As String
            Dim f = Function(x As Boolean)
                        If x Then
                            Return "c"c
                        End If
                        Return "str"
                    End Function
            Return f(b)
        End Function
        Sub Main()
            Console.WriteLine(Pick(True) & Pick(False))
        End Sub
        """, "cstr", Three, HangSafe: true);

    // ================================================================================================
    // Group 3 — a field and property, a module variable, an array literal, a constructor and MyBase argument.
    // ================================================================================================

    /// <summary>`b.Name = c` onto a Public String field and `b.Label = "L"c` onto a String auto-property.</summary>
    internal static readonly TempProbe c14_field_and_property_assignment = new("c14_field_and_property_assignment", """
        Class Box
            Public Name As String
            Public Property Label As String
        End Class
        Sub Main()
            Dim c As Char = "n"c
            Dim b As New Box()
            b.Name = c
            b.Label = "L"c
            Console.WriteLine(b.Name & b.Label)
        End Sub
        """, "nL", Three, HangSafe: true);

    /// <summary>A module variable and a field INITIALISER from a Char literal, and a String-typed getter that returns a Private Char field.</summary>
    internal static readonly TempProbe c19_module_variable_field_initialiser_and_getter = new("c19_module_variable_field_initialiser_and_getter", """
        Dim GS As String = "g"c
        Class Holder
            Public F As String = "f"c
            Private _c As Char = "p"c
            Public ReadOnly Property P As String
                Get
                    Return _c
                End Get
            End Property
        End Class
        Sub Main()
            Dim h As New Holder()
            Console.WriteLine(GS & h.F & h.P)
        End Sub
        """, "gfp", Three, HangSafe: true);

    /// <summary>`New String() {c, "v"}` — a Char variable as an array-literal ELEMENT (the literal's own typing rule: a non-literal must be the element type or widen to it).</summary>
    internal static readonly TempProbe c13_array_literal_element = new("c13_array_literal_element", """
        Sub Main()
            Dim c As Char = "u"c
            Dim a() As String = New String() {c, "v"}
            Console.WriteLine(a(0) & a(1) & a.Length)
        End Sub
        """, "uv2", Three, HangSafe: true);

    /// <summary>`New Tag(c)` and `MyBase.New(c)` (an `IRBaseConstructorCall`, ADR-0016) with a Char for a String constructor parameter.</summary>
    internal static readonly TempProbe c25_constructor_and_MyBase_argument = new("c25_constructor_and_MyBase_argument", """
        Class Tag
            Public Name As String
            Public Sub New(n As String)
                Name = n
            End Sub
        End Class
        Class Sub2
            Inherits Tag
            Public Sub New(c As Char)
                MyBase.New(c)
            End Sub
        End Class
        Sub Main()
            Dim c As Char = "z"c
            Dim t As New Tag(c)
            Dim w As New Sub2("y"c)
            Console.WriteLine(t.Name & w.Name)
        End Sub
        """, "zy", Three, HangSafe: true);

    // ================================================================================================
    // Group 4 — an Optional default and a For Each variable. Kills m1 (c18, c05).
    // ================================================================================================

    /// <summary>`Optional s As String = "d"c` (C# writes the default into the signature, where `'d'` for a string is CS1750). Kills m1 ("Default value type 'Char' is not compatible").</summary>
    internal static readonly TempProbe c18_Optional_default = new("c18_Optional_default", """
        Sub G(Optional s As String = "d"c)
            Console.WriteLine("[" & s & "]")
        End Sub
        Sub Main()
            G()
            G("e")
        End Sub
        """, """
        [d]
        [e]
        """, Three, HangSafe: true);

    /// <summary>⭐ `For Each s As String In "ab"` — a String loop variable over a String (it was refused "Cannot assign String element type 'Char' to loop variable of type 'String'"). Kills m1 and m2 (C++: no match).</summary>
    internal static readonly TempProbe c05_ForEach_String_variable_over_a_String = new("c05_ForEach_String_variable_over_a_String", """
        Sub Main()
            For Each s As String In "ab"
                Console.WriteLine(s & "-" & s.Length)
            Next
        End Sub
        """, """
        a-1
        b-1
        """, Three, HangSafe: true);

    /// <summary>`For Each s As String In cs` over a `Char()` array. Not MSIL: a Char() array is "undefined class Char" there (the `For Each ch As Char In cs` control fails the same way).</summary>
    internal static readonly TempProbe c20_ForEach_String_variable_over_a_Char_array = new("c20_ForEach_String_variable_over_a_Char_array", """
        Sub Main()
            Dim cs() As Char = New Char() {"x"c, "y"c}
            For Each s As String In cs
                Console.WriteLine(s & s.Length)
            Next
        End Sub
        """, """
        x1
        y1
        """, NotMsil, HangSafe: true);

    // ================================================================================================
    // Group 5 — a Char in a String Select, and a comparison.
    // ================================================================================================

    /// <summary>`Select Case s` over a String with `Case "a"c` / `Case "b"c` — the constant Char cases are re-typed to String.</summary>
    internal static readonly TempProbe c11_Case_Char_literals_in_a_String_Select = new("c11_Case_Char_literals_in_a_String_Select", """
        Sub Main()
            Dim s As String = "b"
            Select Case s
                Case "a"c
                    Console.WriteLine("is a")
                Case "b"c
                    Console.WriteLine("is b")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub
        """, "is b", Three, HangSafe: true);

    /// <summary>`Case "a"c To "m"c` and `Case Is >= "n"c` over a String — the range and relational Case values. C++ only (a C# relational String pattern is CS8781, MSIL has no String `Is >=`); JavaScript has its row.</summary>
    internal static readonly TempProbe c11r_Case_Char_range_and_Is = new("c11r_Case_Char_range_and_Is", """
        Sub Main()
            For Each w As String In New String() {"c", "q"}
                Select Case w
                    Case "a"c To "m"c
                        Console.WriteLine(w & " low")
                    Case Is >= "n"c
                        Console.WriteLine(w & " high")
                End Select
            Next
        End Sub
        """, """
        c low
        q high
        """, CppOnly, HangSafe: true);

    /// <summary>`Case c` with a Char VARIABLE (a non-constant Case value). Not C#: CS9135, with a String variable too.</summary>
    internal static readonly TempProbe c11v_Case_a_Char_variable = new("c11v_Case_a_Char_variable", """
        Sub Main()
            Dim c As Char = "b"c
            Dim s As String = "b"
            Select Case s
                Case "a"
                    Console.WriteLine("is a")
                Case c
                    Console.WriteLine("is c")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub
        """, "is c", NotCSharp, HangSafe: true);

    /// <summary>`c = "a"` and `"b" &lt;&gt; c` — a Char compared with a String widens the Char (C# was CS0019, C++ did not build). Not MSIL: String `=` is a reference compare there (#205).</summary>
    internal static readonly TempProbe c12_Char_equals_a_String = new("c12_Char_equals_a_String", """
        Sub Main()
            Dim c As Char = "a"c
            If c = "a" Then
                Console.WriteLine("eq")
            Else
                Console.WriteLine("ne")
            End If
            If "b" <> c Then Console.WriteLine("ne2")
        End Sub
        """, """
        eq
        ne2
        """, NotMsil, HangSafe: true);

    // ================================================================================================
    // The tables
    // ================================================================================================

    internal static readonly IReadOnlyList<ProbeGroup> Groups = new[]
    {
        new ProbeGroup("Char_widens_in_a_declaration_Chr_an_assignment_and_a_Const",
            new[] { c01_Dim_from_a_Char_variable_and_a_Char_literal, c02_Dim_from_Chr_and_ChrW, c06_assignment_s_eq_c, c16_Const_from_a_Char_literal }),
        new ProbeGroup("Char_widens_in_a_Return_an_argument_a_delegate_call_and_a_lambda_return",
            new[] { c03_Return_a_Char_from_a_String_function, c04_ByVal_Char_argument, c22_delegate_call_with_a_Char, c26_lambda_returns_a_Char_literal_and_a_String }),
        new ProbeGroup("Char_widens_in_a_field_a_module_variable_an_array_literal_and_a_constructor_and_MyBase_argument",
            new[] { c14_field_and_property_assignment, c19_module_variable_field_initialiser_and_getter, c13_array_literal_element, c25_constructor_and_MyBase_argument }),
        new ProbeGroup("Char_widens_as_an_Optional_default_and_into_a_For_Each_variable",
            new[] { c18_Optional_default, c05_ForEach_String_variable_over_a_String, c20_ForEach_String_variable_over_a_Char_array }),
        new ProbeGroup("Char_widens_in_a_String_Select_and_a_comparison",
            new[] { c11_Case_Char_literals_in_a_String_Select, c11r_Case_Char_range_and_Is, c11v_Case_a_Char_variable, c12_Char_equals_a_String }),
        // JavaScript: only the shapes that hold no Char local, parameter or field. Each is the SAME probe as above, re-targeted at JavaScript alone.
        new ProbeGroup("JavaScript_runs_the_shapes_that_hold_no_Char_local_parameter_or_field",
            new[]
            {
                c02_Dim_from_Chr_and_ChrW, c16_Const_from_a_Char_literal, c26_lambda_returns_a_Char_literal_and_a_String, c18_Optional_default,
                c05_ForEach_String_variable_over_a_String, c11_Case_Char_literals_in_a_String_Select, c11r_Case_Char_range_and_Is,
            }.Select(p => p with { Id = p.Id + "_js", Agrees = Bk.JavaScript }).ToArray()),
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
            Assert.That(Groups.Select(g => g.Probes.Length), Is.EqualTo(new[] { 4, 4, 4, 3, 4, 7 }));
            Assert.That(probes.Select(p => p.Id), Is.EqualTo(new[]
            {
                "c01_Dim_from_a_Char_variable_and_a_Char_literal", "c02_Dim_from_Chr_and_ChrW", "c06_assignment_s_eq_c", "c16_Const_from_a_Char_literal",
                "c03_Return_a_Char_from_a_String_function", "c04_ByVal_Char_argument", "c22_delegate_call_with_a_Char", "c26_lambda_returns_a_Char_literal_and_a_String",
                "c14_field_and_property_assignment", "c19_module_variable_field_initialiser_and_getter", "c13_array_literal_element", "c25_constructor_and_MyBase_argument",
                "c18_Optional_default", "c05_ForEach_String_variable_over_a_String", "c20_ForEach_String_variable_over_a_Char_array",
                "c11_Case_Char_literals_in_a_String_Select", "c11r_Case_Char_range_and_Is", "c11v_Case_a_Char_variable", "c12_Char_equals_a_String",
                "c02_Dim_from_Chr_and_ChrW_js", "c16_Const_from_a_Char_literal_js", "c26_lambda_returns_a_Char_literal_and_a_String_js", "c18_Optional_default_js",
                "c05_ForEach_String_variable_over_a_String_js", "c11_Case_Char_literals_in_a_String_Select_js", "c11r_Case_Char_range_and_Is_js",
            }));
            Assert.That(probes.Select(p => p.Id).Distinct().Count(), Is.EqualTo(probes.Count));
            Assert.That(probes.Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# cell runs in a child process with a time limit (#256)");
            // The known gaps are DROPPED backends, and each is named in the header.
            Assert.That(probes.Where(p => p.Agrees == Three).Count(), Is.EqualTo(15));
            Assert.That(probes.Where(p => p.Agrees == NotMsil).Select(p => p.Id), Is.EqualTo(new[] { "c20_ForEach_String_variable_over_a_Char_array", "c12_Char_equals_a_String" }));
            Assert.That(probes.Where(p => p.Agrees == NotCSharp).Select(p => p.Id), Is.EqualTo(new[] { "c11v_Case_a_Char_variable" }));
            Assert.That(probes.Where(p => p.Agrees == CppOnly).Select(p => p.Id), Is.EqualTo(new[] { "c11r_Case_Char_range_and_Is" }));
            Assert.That(probes.Where(p => p.Agrees == Bk.JavaScript).Count(), Is.EqualTo(7));
            Assert.That(probes.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(15 * 3 + 2 * 2 + 1 * 2 + 1 + 7), "19 probes + 7 JavaScript copies, each on every backend it runs on, each through three entry points");
        });
    }

    // ============================================================================================
    // RUN — vbc's answer, every backend the probe runs on, three entry points
    // ============================================================================================

    /// <summary>
    /// Each probe of the group on every backend it runs on, through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A widening that is
    /// refused is a compile error on every backend (m1); one that is not converted is CS0029 / CS1503 / CS0019 on C#, no matching function on C++ and InvalidProgramException on MSIL (m2). A backend
    /// whose tool is missing is skipped; the test is ignored only when none could run.
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
}

/// <summary>
/// #184 COMPILE, in process — no spawned CLI, no native compile, so it runs in the fast subset. The refusals vbc ALSO gives, which the widening must not have opened (String -> Char; a Char into a ByRef
/// String; a Char `Set(value)` on a String property; `Is` on a Char), the one JavaScript refusal, and the IR shape of the widening itself.
/// </summary>
[TestFixture]
public class CharWidensToStringCompileTests
{
    /// <summary>Parse (asserting no parse error: a typo in a probe must not pass as a refusal), analyze, and return every ERROR with its line.</summary>
    private static (bool Ok, List<(int Line, string Message)> Errors) Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "the probe does not parse: " + string.Join("; ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        return (ok, analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => (e.Line, e.Message)).ToList());
    }

    /// <summary>
    /// The four refusals, each ONE error on the line that names the offence, and nothing else in the program refused — every other line is a widening that must be ACCEPTED (so a front end that
    /// refused the whole program, or admitted the offence, fails). Each is one vbc gives too, under Option Strict On:
    /// String -> Char is BC30512 (the narrowing; `Dim t As String = c` on the line above is the widening, accepted);
    /// a Char into a ByRef String is BC32029 (the value would be copied back String -> Char; the ByVal `V(c)` beside it is accepted) — kills m3;
    /// a Char `Set(value)` on a String property is BC31064; `Is` on a Char is BC30020.
    /// </summary>
    [TestCase("a String into a Char", """
        Sub Main()
            Dim c As Char = "a"c
            Dim s As String = "hi"
            Dim t As String = c
            Dim d As Char = s
        End Sub
        """, 5, "Cannot assign value of type 'String' to variable of type 'Char'")]
    [TestCase("a Char into a ByRef String", """
        Sub V(s As String)
            Console.WriteLine(s)
        End Sub
        Sub M(ByRef s As String)
            s = s & "!"
        End Sub
        Sub Main()
            Dim c As Char = "r"c
            V(c)
            M(c)
        End Sub
        """, 10, "Argument 1: cannot convert from 'Char' to 'String'")]
    [TestCase("a Char Set(value) on a String property", """
        Class Box
            Private _v As String
            Public Property V As String
                Get
                    Return _v
                End Get
                Set(value As Char)
                    _v = value
                End Set
            End Property
        End Class
        Sub Main()
            Dim b As New Box()
            b.V = "q"c
        End Sub
        """, 7, "Setter parameter type 'Char' does not match property type 'String'")]
    [TestCase("Is on a Char", """
        Sub Main()
            Dim c As Char = "a"c
            Dim s As String = "a"
            Dim t As String = c
            If s Is c Then Console.WriteLine("same")
        End Sub
        """, 5, "'Is' requires operands of a reference or nullable type, but 'c' is 'Char'")]
    public void AKeptRefusal_IsStillRefused(string name, string source, int line, string message)
    {
        var (ok, errors) = Analyze(source);
        Assert.That(ok, Is.False, $"{name}: the front end accepted it");
        Assert.That(errors, Has.Count.EqualTo(1), $"{name}: " + string.Join(" | ", errors.Select(e => $"line {e.Line}: {e.Message}")));
        Assert.That(errors[0].Line, Is.EqualTo(line), $"{name}: {errors[0].Message}");
        Assert.That(errors[0].Message, Does.Contain(message), name);
    }

    /// <summary>
    /// JavaScript has no character type, so a Char LOCAL stays BL7004 however it is used — `Dim s As String = c` included: widening a Char variable does not make it a JavaScript value. Through the
    /// standard and the aggressive passes (the refusal is a capability check, not an optimizer rule). The shapes with no Char local run: the JavaScript group of the execution fixture.
    /// </summary>
    [Test]
    public void JavaScript_AStringInitialisedFromACharLocal_IsStillRefused_BL7004()
    {
        const string source = "Sub Main()\n    Dim c As Char = \"A\"c\n    Dim s As String = c\n    Console.WriteLine(s)\nEnd Sub";
        var standard = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(source));
        var aggressive = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.CompileAggressive(source));
        Assert.Multiple(() =>
        {
            Assert.That(standard!.Message, Does.StartWith("BL7004: 'Char' cannot be lowered to JavaScript"));
            Assert.That(standard.Message, Does.Contain("local variable 'c'"));
            Assert.That(aggressive!.Message, Does.StartWith("BL7004: 'Char' cannot be lowered to JavaScript"));
        });
    }

    /// <summary>
    /// The IR shape of the widening: a Char VARIABLE stored into a String becomes exactly one `CStr(c)` call typed String (the conversion an interpolation hole uses — measured right on C#, C++ and MSIL),
    /// while a Char LITERAL is re-typed in place and gets no call. Without the call (m2) the Char reaches the backend as a Char (CS0029 / InvalidProgramException); wrapping the literal too would put
    /// a second call here. Fast — the execution fixture proves the same thing by RUNNING it, slowly.
    /// </summary>
    [Test]
    public void ANonLiteralChar_BecomesACStrCall_AndALiteralIsRetypedInPlace()
    {
        const string source = "Sub Main()\n    Dim c As Char = \"A\"c\n    Dim s As String = c\n    Dim t As String = \"z\"c\n    Console.WriteLine(s & t)\nEnd Sub";
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var module = new IRBuilder(analyzer).Build(ast, "TestModule");

        var main = module.Functions.Single(f => string.Equals(f.Name, "Main", StringComparison.OrdinalIgnoreCase));
        var conversions = main.Blocks.SelectMany(b => b.Instructions).OfType<IRCall>().Where(c => c.FunctionName == "CStr").ToList();

        Assert.That(conversions, Has.Count.EqualTo(1), "one CStr: for `Dim s As String = c`, none for the literal `\"z\"c`");
        Assert.Multiple(() =>
        {
            Assert.That(conversions[0].Type.Name, Is.EqualTo("String"));
            Assert.That(conversions[0].Arguments.Single().Type.Name, Is.EqualTo("Char"));
            Assert.That(conversions[0].Arguments.Single(), Is.Not.InstanceOf<IRConstant>(), "the argument is the variable c, not a literal");
        });
    }
}
