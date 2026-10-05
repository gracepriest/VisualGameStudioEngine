using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #186 — `Nothing` converted to a VALUE type is that type's DEFAULT, the VB way, on every backend. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. VB converts Nothing to every type: a reference type gets a null reference and a value type gets its default (`Dim n As Integer = Nothing` is 0, a Boolean False, a Char ChrW(0), a Structure
//  every field at its default, a type parameter default(T)). BasicLang REFUSED every value type with advice ("Nothing has no value of type 'Integer'; write 0") at all nine #173 sites, in a typed array literal
//  and in If(). Where a shape slipped past the front end the backends broke on it: `If n = Nothing` was False for n = 0 on C#, JavaScript and MSIL and did not build on C++; `Case Nothing` on an Integer was CS0037
//  on C#, did not build on C++, never matched on JavaScript and on a Double was refused on MSIL. The owner decided "fix #186": adopt VB's rule on every backend. Now `TypeInfo.NothingIsDefaultValue` is the ONE list
//  of value types (the front end, the IR and two backends read it); `IRBuilder.NothingAs` lowers a primitive to its zero LITERAL and any other value type to a null constant typed with it, which MEANS "default of T"
//  (C# `default(T)`, C++ `T{}`; JavaScript and MSIL emit it as the default of an uninitialized `Dim x As T`); `NothingAsValueType` does the sites that skip CoerceToDeclaredType (a comparison operand, a Case value, an
//  Optional default, a ByRef argument). What stays refused is IDENTITY on a value type: `n Is Nothing` and `Case Is Nothing` (BC30020, below).
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t186/probes, the `.exp` beside each `.bas`) — never what a backend printed. A GROUP is one test case: it
//  runs each of its probes on every backend where that probe now RUNS, each through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive — what a Release .blproj build and the
//  IDE call), and reports every failing cell by probe id. A backend whose tool is missing is SKIPPED (`TempExec.RequireTool`: g++/clang++, Node, ilasm), never failed; the case is ignored only when no cell could run.
//  Every C# cell is `HangSafe`: a child process with a time limit (`CSharpProcessRunner`), never the in-process runner that has no timeout (#256).
//
//  ⭐ WHICH CELLS EXIST. Groups 1-5 (an Integer, Boolean and Double; a field, a module variable and a member; a Return, an argument, a `New` / `MyBase.New` argument, an Optional and a delegate argument; an array
//  element, a typed array literal and `If(c, 1, Nothing)`; `n = Nothing` and `Case Nothing`) run on ALL FOUR backends. Group 6 (Long, Single, Byte..ULong, Char, Decimal) runs on C#, C++ and MSIL: JavaScript has no
//  such type and refuses it by name, which `NothingIntoValueTypeCompileTests` pins. Groups 7 and 8 are the composite value types where they run at all (below).
//
//  ⛔ KNOWN GAPS — each a defect or a decision that is NOT #186's, each measured with a Nothing-free CONTROL that fails the same way (S/t186/probes/*c.bas), listed with NO test (asserting one would pin the defect):
//    STRUCTURE  JavaScript refuses it (BL7005, a capability decision) and MSIL spells a Structure as a CLASS (#192: a NullReferenceException on first use), so group 7 is C# and C++ only.
//    GENERIC T  C++ and MSIL build no generic function or class ("unknown type name 'T'", "undefined class 'T'") and on JavaScript an uninitialized T is `null`: n15 and n23 are C# rows only.
//    ENUM       C# only: C++ ("'Color' does not refer to a value"), MSIL (an .il syntax error) and JavaScript ("Color is not defined") fail the Nothing-free control n19c alike.
//    TUPLE      C# only: C++ ("use of undeclared identifier 'Integer'"), MSIL ("undefined class") and JavaScript (BL7007) refuse `Dim t As (Integer, String)` with or without a Nothing (control n21c).
//    UNION      runs on NO backend, Nothing or not: the front end admits `Dim u As U = Nothing` exactly as it admits `Dim u As U` (pinned in NothingConversionTests), then csc, clang, ilasm and JavaScript all refuse
//               the declaration (control n28c).
//    DATETIME   (and TimeSpan, Guid) C# and C++ only: JavaScript (`DateTime is not defined`, a null) and MSIL ("undefined class 'DateTime'") fail the Nothing-free `New DateTime(1, 1, 1)` control alike
//               (n06c). `Date` is not a BasicLang type at all.
//    ALL    `Function() Nothing` into a `Func(Of Integer)` is REFUSED (BL3001 "Cannot assign value of type 'Func' to variable of type 'Func'") — and so is the same lambda into a `Func(Of String)` (control n27c):
//           the lambda's own return type is never reconciled with its target, which is no value-type rule.
//    ALL    `Const K As Integer = Nothing` is REFUSED (BL3001 "Constant value type 'Object' is not compatible with declared type 'Integer'") — and so is `Const S As String = Nothing` (control n31c): Const was never
//           one of #173's nine conversion sites, so this is not a value-type rule either.
//    ALL    a literal passed ByRef (`S(0)`, so `S(Nothing)` into a ByRef Integer): C# CS1510, MSIL and JavaScript refuse it, C++ prints 1 (control n29c fails alike). The IR gives it VB's value, 0, as it gives `S(0)`.
//
//  ⭐ MUTANTS (S/t186: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    m1 `NothingAs` keeps the typed null for EVERY value type (no zero literal for a primitive):  group 1, JavaScript cells only — `n01_...` and `n12_...` print `null` where vbc prints 0 (C#, C++ and MSIL survive:
//       `default(int)`, `int32_t{}` and `ldnull`-as-0 all happen to be zero), plus the fast `ValueType_AdmitsNothing_AsItsDefault` rows (the constant is a null, not 0 / False / NUL).
//    m2 C# spells a typed null of a value type `null`, not `default(T)`:  group 7 and 8, C# cells — `n07_...` and `n22_...` are CS0037, `n15_...` is CS0403.
//    m3 a comparison operand keeps the untyped Nothing (`n = Nothing` is not `n = 0`):  group 5 — `n13_...` and `n13b_...` print `ne` for n = 0 on C#, JavaScript and MSIL and do not build on C++.
//
//  ⚠ Named "…ExecutionTests" on purpose: its groups RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #186 RUN: `Nothing` into an Integer, Boolean, Double, Long, Char, Decimal, a narrow integer, a Structure, a type parameter, an Enum and a DateTime — at a declaration, an assignment, a field, a member, a Return, an
/// argument, a `New` and `MyBase.New` argument, an Optional default, a delegate call, an array element, a typed array literal, an `If()` operand, a comparison and a `Case` — each prints vbc's answer on every backend
/// where it runs, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class NothingIntoValueTypeExecutionTests
{
    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;   // JavaScript refuses Long, Char, Decimal and ULong (BL7003/7004/7007)
    private const Bk NotJsNotMsil = Bk.CSharp | Bk.Cpp;      // a Structure / DateTime: JavaScript BL7005 or no DateTime, and MSIL spells a Structure as a class (#192)
    private const Bk CSharpOnly = Bk.CSharp;                 // a type parameter, an Enum, a tuple: C++ and MSIL have none of them, JavaScript's default is null

    // ================================================================================================
    // Group 1 — an Integer, a Boolean and a Double, declared and assigned. Kills m1 (n01, n12, on JavaScript).
    // ================================================================================================

    /// <summary>`Dim n As Integer = Nothing` — 0, and 0 + 5. Kills m1: on JavaScript a typed null prints `null`.</summary>
    internal static readonly TempProbe n01_Dim_Integer_local = new("n01_Dim_Integer_local", """
        Sub Main()
            Dim n As Integer = Nothing
            Console.WriteLine(n)
            Console.WriteLine(n + 5)
        End Sub
        """, """
        0
        5
        """, Bk.All, HangSafe: true);

    /// <summary>`Dim b As Boolean = Nothing` — False, so the If takes its Else and `Not b` is True.</summary>
    internal static readonly TempProbe n03_Dim_Boolean_local = new("n03_Dim_Boolean_local", """
        Sub Main()
            Dim b As Boolean = Nothing
            If b Then
                Console.WriteLine("T")
            Else
                Console.WriteLine("F")
            End If
            Console.WriteLine(Not b)
        End Sub
        """, """
        F
        True
        """, Bk.All, HangSafe: true);

    /// <summary>`x = Nothing` and `d = Nothing` onto an Integer and a Double that already hold values — an assignment, not a declaration. Kills m1 (JavaScript prints `null`).</summary>
    internal static readonly TempProbe n12_assignment_x_eq_Nothing = new("n12_assignment_x_eq_Nothing", """
        Sub Main()
            Dim x As Integer = 7
            Dim d As Double = 1.5
            x = Nothing
            d = Nothing
            Console.WriteLine(x)
            Console.WriteLine(d + 1)
        End Sub
        """, """
        0
        1
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // Group 2 — a field, a module variable and a member assignment.
    // ================================================================================================

    /// <summary>A module variable and two field INITIALISERS (an Integer and a Double): each must stay a constant, which a zero literal is and a call would not be.</summary>
    internal static readonly TempProbe n08_module_variable_and_field_initialisers = new("n08_module_variable_and_field_initialisers", """
        Dim G As Integer = Nothing
        Class Holder
            Public F As Integer = Nothing
            Public D As Double = Nothing
        End Class
        Sub Main()
            Dim h As New Holder()
            Console.WriteLine(G + 1)
            Console.WriteLine(h.F + h.D + 2)
        End Sub
        """, """
        1
        2
        """, Bk.All, HangSafe: true);

    /// <summary>`h.F = Nothing` onto a field already holding 3 and `h.P = Nothing` onto a Double auto-property already holding 2.5 — a member assignment.</summary>
    internal static readonly TempProbe n30_member_assignment = new("n30_member_assignment", """
        Class Holder
            Public F As Integer = 3
            Private _p As Double = 2.5
            Public Property P As Double
                Get
                    Return _p
                End Get
                Set(value As Double)
                    _p = value
                End Set
            End Property
        End Class
        Sub Main()
            Dim h As New Holder()
            h.F = Nothing
            h.P = Nothing
            Console.WriteLine(h.F)
            Console.WriteLine(h.P + 1)
        End Sub
        """, """
        0
        1
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // Group 3 — a Return, an argument, a `New` and `MyBase.New` argument, an Optional default and a delegate call.
    // ================================================================================================

    /// <summary>`Return Nothing` from an `As Integer` function, on the path that falls through.</summary>
    internal static readonly TempProbe n09_Return_Nothing = new("n09_Return_Nothing", """
        Function F(k As Integer) As Integer
            If k > 0 Then Return k
            Return Nothing
        End Function
        Sub Main()
            Console.WriteLine(F(3))
            Console.WriteLine(F(0))
        End Sub
        """, """
        3
        0
        """, Bk.All, HangSafe: true);

    /// <summary>`S(Nothing)` — an argument for an Integer parameter, beside the written `S(4)`.</summary>
    internal static readonly TempProbe n10_ByVal_argument = new("n10_ByVal_argument", """
        Sub S(n As Integer)
            Console.WriteLine(n + 1)
        End Sub
        Sub Main()
            S(Nothing)
            S(4)
        End Sub
        """, """
        1
        5
        """, Bk.All, HangSafe: true);

    /// <summary>`New A(Nothing)` and `MyBase.New(Nothing)` (an `IRBaseConstructorCall`, ADR-0016) into an Integer constructor parameter.</summary>
    internal static readonly TempProbe n10b_New_and_MyBase_New_argument = new("n10b_New_and_MyBase_New_argument", """
        Class A
            Public K As Integer
            Public Sub New(k As Integer)
                Me.K = k + 1
            End Sub
        End Class
        Class B
            Inherits A
            Public Sub New()
                MyBase.New(Nothing)
            End Sub
        End Class
        Sub Main()
            Dim a As New A(Nothing)
            Dim b As New B()
            Console.WriteLine(a.K)
            Console.WriteLine(b.K)
        End Sub
        """, """
        1
        1
        """, Bk.All, HangSafe: true);

    /// <summary>`Optional n As Integer = Nothing` — the default is written into the signature (C#) or the call site (the rest), and must be 0 where it is omitted.</summary>
    internal static readonly TempProbe n11_Optional_default = new("n11_Optional_default", """
        Sub S(Optional n As Integer = Nothing)
            Console.WriteLine(n + 1)
        End Sub
        Sub Main()
            S()
            S(4)
        End Sub
        """, """
        1
        5
        """, Bk.All, HangSafe: true);

    /// <summary>`f(Nothing)` where `f` is an `Action(Of Integer)` — the delegate-invocation argument path (typed from the delegate's own generic arguments), not the named-procedure one.</summary>
    internal static readonly TempProbe n27_delegate_argument = new("n27_delegate_argument", """
        Sub Main()
            Dim f As Action(Of Integer) = Sub(k As Integer) Console.WriteLine(k + 1)
            f(Nothing)
            f(4)
        End Sub
        """, """
        1
        5
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // Group 4 — an array element, a typed array literal and an If() operand.
    // ================================================================================================

    /// <summary>`a(0) = Nothing` into an Integer array element that held 5.</summary>
    internal static readonly TempProbe n14_array_element = new("n14_array_element", """
        Sub Main()
            Dim a(2) As Integer
            a(0) = 5
            a(1) = 6
            a(0) = Nothing
            Console.WriteLine(a(0) + a(1))
        End Sub
        """, "6", Bk.All, HangSafe: true);

    /// <summary>`New Integer() {1, Nothing, 3}` — the typed array literal's OWN element rule (not the Dim path's).</summary>
    internal static readonly TempProbe n18_typed_array_literal = new("n18_typed_array_literal", """
        Sub Main()
            Dim a() As Integer = New Integer() {1, Nothing, 3}
            Console.WriteLine(a(0) + a(1) + a(2))
        End Sub
        """, "4", Bk.All, HangSafe: true);

    /// <summary>`If(t, 1, Nothing)` and `If(Not t, Nothing, 2)` — Nothing takes the OTHER operand's type, so each is that type's default.</summary>
    internal static readonly TempProbe n20_If_operand = new("n20_If_operand", """
        Sub Main()
            Dim t As Boolean = False
            Dim x As Integer = If(t, 1, Nothing)
            Console.WriteLine(x)
            Console.WriteLine(If(Not t, Nothing, 2) + 7)
        End Sub
        """, """
        0
        7
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // Group 5 — a comparison and a Case. Kills m3 (n13, n13b).
    // ================================================================================================

    /// <summary>⭐ `n = Nothing`, `n &lt;&gt; Nothing` — VB converts the Nothing to Integer and compares, so for n = 0 it is TRUE. Kills m3: the untyped null compared False on C#, JavaScript and MSIL and did not build on C++.</summary>
    internal static readonly TempProbe n13_n_eq_Nothing_compares_with_the_default = new("n13_n_eq_Nothing_compares_with_the_default", """
        Sub Main()
            Dim n As Integer = 0
            If n = Nothing Then
                Console.WriteLine("eq")
            Else
                Console.WriteLine("ne")
            End If
            n = 5
            If n = Nothing Then
                Console.WriteLine("eq2")
            Else
                Console.WriteLine("ne2")
            End If
            If n <> Nothing Then Console.WriteLine("ne3")
        End Sub
        """, """
        eq
        ne2
        ne3
        """, Bk.All, HangSafe: true);

    /// <summary>`Nothing = n` (Nothing on the LEFT), `b = Nothing` on a Boolean, `d &lt;&gt; Nothing` and `d &gt; Nothing` on a Double. Kills m3 on both operand sides.</summary>
    internal static readonly TempProbe n13b_comparison_either_side_and_other_types = new("n13b_comparison_either_side_and_other_types", """
        Sub Main()
            Dim b As Boolean = False
            Dim d As Double = 0.5
            Dim n As Integer = 0
            If Nothing = n Then Console.WriteLine("a")
            If b = Nothing Then Console.WriteLine("b")
            If d <> Nothing Then Console.WriteLine("c")
            If d > Nothing Then Console.WriteLine("d")
        End Sub
        """, """
        a
        b
        c
        d
        """, Bk.All, HangSafe: true);

    /// <summary>`Case Nothing` (no `Is`) on an Integer and on a Double subject, and `Case Is &lt; Nothing` / `Case Is &gt; Nothing` — the value Case, which compares with the default. (`Case Is Nothing` is the refused identity form.)</summary>
    internal static readonly TempProbe n24_Case_Nothing = new("n24_Case_Nothing", """
        Sub Main()
            Dim n As Integer = 0
            Select Case n
                Case Nothing
                    Console.WriteLine("zero")
                Case Else
                    Console.WriteLine("other")
            End Select
            Dim m As Integer = 5
            Select Case m
                Case Is < Nothing
                    Console.WriteLine("neg")
                Case Is > Nothing
                    Console.WriteLine("pos")
            End Select
            Dim d As Double = 0
            Select Case d
                Case Nothing
                    Console.WriteLine("dzero")
            End Select
        End Sub
        """, """
        zero
        pos
        dzero
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // Group 6 — Long, Single, the narrow integers, Char and Decimal. C#, C++ and MSIL (JavaScript refuses them by name).
    // ================================================================================================

    /// <summary>A Long (`l + 1`) and a Double (`d + 2`).</summary>
    internal static readonly TempProbe n02_Long_and_Double = new("n02_Long_and_Double", """
        Sub Main()
            Dim l As Long = Nothing
            Dim d As Double = Nothing
            Console.WriteLine(l + 1)
            Console.WriteLine(d + 2)
        End Sub
        """, """
        1
        2
        """, Three, HangSafe: true);

    /// <summary>`Dim c As Char = Nothing` — ChrW(0). C# writes `'\0'` and C++ `'\0'`, where a bare NUL byte in the source would end the literal.</summary>
    internal static readonly TempProbe n04_Char = new("n04_Char", """
        Sub Main()
            Dim c As Char = Nothing
            Console.WriteLine(AscW(c))
        End Sub
        """, "0", Three, HangSafe: true);

    /// <summary>`Dim m As Decimal = Nothing` — 0, and 0 + 1.</summary>
    internal static readonly TempProbe n05_Decimal = new("n05_Decimal", """
        Sub Main()
            Dim m As Decimal = Nothing
            Console.WriteLine(m)
            Console.WriteLine(m + 1)
        End Sub
        """, """
        0
        1
        """, Three, HangSafe: true);

    /// <summary>Short, Byte, Single, SByte, UShort, UInteger and ULong (also re-assigned) — the narrow types, which a written `0` leaves untouched too.</summary>
    internal static readonly TempProbe n25_narrow_and_unsigned_integers = new("n25_narrow_and_unsigned_integers", """
        Sub Main()
            Dim s As Short = Nothing
            Dim y As Byte = Nothing
            Dim f As Single = Nothing
            Dim sb As SByte = Nothing
            Dim us As UShort = Nothing
            Dim ui As UInteger = Nothing
            Dim ul As ULong = Nothing
            Console.WriteLine(s)
            Console.WriteLine(y)
            Console.WriteLine(f)
            Console.WriteLine(sb)
            Console.WriteLine(us)
            Console.WriteLine(ui)
            Console.WriteLine(ul)
            ul = 5
            ul = Nothing
            Console.WriteLine(ul)
        End Sub
        """, """
        0
        0
        0
        0
        0
        0
        0
        0
        """, Three, HangSafe: true);

    // ================================================================================================
    // Group 7 — a Structure and a DateTime, on C# and C++. Kills m2 (n07, n22 on C#).
    // ================================================================================================

    /// <summary>⭐ `Dim p As Pt = Nothing` and `p = Nothing` onto a Structure holding 3 — every field back at its default (an Integer 0, a String null, a Boolean False). Kills m2: C# `null` is CS0037.</summary>
    internal static readonly TempProbe n07_Structure_local_and_assignment = new("n07_Structure_local_and_assignment", """
        Structure Pt
            Public X As Integer
            Public Name As String
            Public Ok As Boolean
        End Structure
        Sub Main()
            Dim p As Pt = Nothing
            Console.WriteLine(p.X)
            Console.WriteLine(p.Name Is Nothing)
            p.X = 3
            Console.WriteLine(p.X)
            p = Nothing
            Console.WriteLine(p.X)
        End Sub
        """, """
        0
        True
        3
        0
        """, NotJsNotMsil, HangSafe: true);

    /// <summary>⭐ A Structure through `Return Nothing`, a `Show(Nothing)` argument and a field INITIALISER. Kills m2 (C# `null`: CS0037 at the Return).</summary>
    internal static readonly TempProbe n22_Structure_Return_argument_and_field = new("n22_Structure_Return_argument_and_field", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Function Make(k As Integer) As Pt
            If k = 0 Then Return Nothing
            Dim p As Pt
            p.X = k
            p.Y = k
            Return p
        End Function
        Sub Show(p As Pt)
            Console.WriteLine(p.X + p.Y)
        End Sub
        Class Holder
            Public P As Pt = Nothing
        End Class
        Sub Main()
            Show(Make(0))
            Show(Make(2))
            Show(Nothing)
            Dim h As New Holder()
            Show(h.P)
        End Sub
        """, """
        0
        4
        0
        0
        """, NotJsNotMsil, HangSafe: true);

    /// <summary>`Optional p As Pt = Nothing` — a Structure default, written where the call omits it.</summary>
    internal static readonly TempProbe n11b_Structure_Optional_default = new("n11b_Structure_Optional_default", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Sub Show(Optional p As Pt = Nothing)
            Console.WriteLine(p.X + p.Y)
        End Sub
        Sub Main()
            Show()
            Dim q As Pt
            q.X = 2
            q.Y = 3
            Show(q)
        End Sub
        """, """
        0
        5
        """, NotJsNotMsil, HangSafe: true);

    /// <summary>`Dim d As DateTime = Nothing` — `0001-01-01`, so Year, Month and Day are all 1. (A P1 native struct: typed as a synthetic class, found by the NativeOwned arm of the list.)</summary>
    internal static readonly TempProbe n06b_DateTime = new("n06b_DateTime", """
        Sub Main()
            Dim d As DateTime = Nothing
            Console.WriteLine(d.Year)
            Console.WriteLine(d.Month)
            Console.WriteLine(d.Day)
        End Sub
        """, """
        1
        1
        1
        """, NotJsNotMsil, HangSafe: true);

    // ================================================================================================
    // Group 8 — a type parameter, an Enum and a tuple, on C# alone. Kills m2 (n15).
    // ================================================================================================

    /// <summary>⭐ `Dim v As T = Nothing` and `Return Nothing` in a generic function, instantiated with Integer, Double and Boolean: `default(T)`. Kills m2: C# `null` is CS0403.</summary>
    internal static readonly TempProbe n15_type_parameter_default = new("n15_type_parameter_default", """
        Function Def(Of T)() As T
            Dim v As T = Nothing
            Return v
        End Function
        Function Def2(Of T)() As T
            Return Nothing
        End Function
        Sub Main()
            Console.WriteLine(Def(Of Integer)())
            Console.WriteLine(Def2(Of Double)())
            Console.WriteLine(Def(Of Boolean)())
        End Sub
        """, """
        0
        0
        False
        """, CSharpOnly, HangSafe: true);

    /// <summary>A generic CLASS: a `T` field initialiser, `V = Nothing` and `Return Nothing` — each is `default(T)` of the instantiation (here Integer).</summary>
    internal static readonly TempProbe n23_generic_class_field_and_member = new("n23_generic_class_field_and_member", """
        Class Box(Of T)
            Public V As T = Nothing
            Public Function Reset() As T
                V = Nothing
                Return Nothing
            End Function
        End Class
        Sub Main()
            Dim b As New Box(Of Integer)()
            Console.WriteLine(b.V)
            b.V = 5
            Console.WriteLine(b.Reset())
            Console.WriteLine(b.V)
        End Sub
        """, """
        0
        0
        0
        """, CSharpOnly, HangSafe: true);

    /// <summary>`Dim c As Color = Nothing` and `c = Nothing` — an Enum's default is 0, which matches no member here (`Red = 1`).</summary>
    internal static readonly TempProbe n19_Enum = new("n19_Enum", """
        Enum Color
            Red = 1
            Green = 2
        End Enum
        Sub Main()
            Dim c As Color = Nothing
            Console.WriteLine(CInt(c))
            If c = Color.Red Then
                Console.WriteLine("red")
            Else
                Console.WriteLine("notred")
            End If
            c = Nothing
            Console.WriteLine(CInt(c))
        End Sub
        """, """
        0
        notred
        0
        """, CSharpOnly, HangSafe: true);

    /// <summary>`Dim t As (Integer, String) = Nothing` and `t = Nothing` — a tuple's default (`default(ValueTuple<int, string>)`).</summary>
    internal static readonly TempProbe n21_tuple = new("n21_tuple", """
        Sub Main()
            Dim t As (Integer, String) = Nothing
            t = Nothing
            Console.WriteLine("ok")
        End Sub
        """, "ok", CSharpOnly, HangSafe: true);

    // ================================================================================================
    // The tables
    // ================================================================================================

    internal static readonly IReadOnlyList<ProbeGroup> Groups = new[]
    {
        new ProbeGroup("Nothing_is_0_False_and_0_in_a_Dim_and_an_assignment",
            new[] { n01_Dim_Integer_local, n03_Dim_Boolean_local, n12_assignment_x_eq_Nothing }),
        new ProbeGroup("Nothing_is_the_default_in_a_field_a_module_variable_and_a_member_assignment",
            new[] { n08_module_variable_and_field_initialisers, n30_member_assignment }),
        new ProbeGroup("Nothing_is_the_default_in_a_Return_an_argument_a_New_and_MyBase_New_argument_an_Optional_and_a_delegate_call",
            new[] { n09_Return_Nothing, n10_ByVal_argument, n10b_New_and_MyBase_New_argument, n11_Optional_default, n27_delegate_argument }),
        new ProbeGroup("Nothing_is_the_default_in_an_array_element_a_typed_array_literal_and_an_If_operand",
            new[] { n14_array_element, n18_typed_array_literal, n20_If_operand }),
        new ProbeGroup("Nothing_compared_and_in_a_Case_is_the_default",
            new[] { n13_n_eq_Nothing_compares_with_the_default, n13b_comparison_either_side_and_other_types, n24_Case_Nothing }),
        new ProbeGroup("Nothing_is_the_default_of_a_Long_Single_narrow_integer_Char_and_Decimal_on_CSharp_Cpp_and_Msil",
            new[] { n02_Long_and_Double, n04_Char, n05_Decimal, n25_narrow_and_unsigned_integers }),
        new ProbeGroup("Nothing_is_the_default_of_a_Structure_and_a_DateTime_on_CSharp_and_Cpp",
            new[] { n07_Structure_local_and_assignment, n22_Structure_Return_argument_and_field, n11b_Structure_Optional_default, n06b_DateTime }),
        new ProbeGroup("Nothing_is_the_default_of_a_type_parameter_an_Enum_and_a_tuple_on_CSharp",
            new[] { n15_type_parameter_default, n23_generic_class_field_and_member, n19_Enum, n21_tuple }),
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
            Assert.That(Groups.Select(g => g.Probes.Length), Is.EqualTo(new[] { 3, 2, 5, 3, 3, 4, 4, 4 }));
            Assert.That(probes.Select(p => p.Id), Is.EqualTo(new[]
            {
                "n01_Dim_Integer_local", "n03_Dim_Boolean_local", "n12_assignment_x_eq_Nothing",
                "n08_module_variable_and_field_initialisers", "n30_member_assignment",
                "n09_Return_Nothing", "n10_ByVal_argument", "n10b_New_and_MyBase_New_argument", "n11_Optional_default", "n27_delegate_argument",
                "n14_array_element", "n18_typed_array_literal", "n20_If_operand",
                "n13_n_eq_Nothing_compares_with_the_default", "n13b_comparison_either_side_and_other_types", "n24_Case_Nothing",
                "n02_Long_and_Double", "n04_Char", "n05_Decimal", "n25_narrow_and_unsigned_integers",
                "n07_Structure_local_and_assignment", "n22_Structure_Return_argument_and_field", "n11b_Structure_Optional_default", "n06b_DateTime",
                "n15_type_parameter_default", "n23_generic_class_field_and_member", "n19_Enum", "n21_tuple",
            }));
            Assert.That(probes.Select(p => p.Id).Distinct().Count(), Is.EqualTo(probes.Count));
            Assert.That(probes.Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# cell runs in a child process with a time limit (#256)");
            // The known gaps are DROPPED backends, and each is named in the header.
            Assert.That(probes.Where(p => p.Agrees == Bk.All).Count(), Is.EqualTo(16), "groups 1-5 run on all four backends, JavaScript included (m1's killers are the JavaScript cells)");
            Assert.That(probes.Where(p => p.Agrees == Three).Select(p => p.Id), Is.EqualTo(new[] { "n02_Long_and_Double", "n04_Char", "n05_Decimal", "n25_narrow_and_unsigned_integers" }));
            Assert.That(probes.Where(p => p.Agrees == NotJsNotMsil).Count(), Is.EqualTo(4));
            Assert.That(probes.Where(p => p.Agrees == CSharpOnly).Count(), Is.EqualTo(4));
            Assert.That(probes.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(16 * 4 + 4 * 3 + 4 * 2 + 4 * 1), "28 probes, each on every backend it runs on, each through three entry points");
        });
    }

    // ============================================================================================
    // RUN — vbc's answer, every backend the probe runs on, three entry points
    // ============================================================================================

    /// <summary>
    /// Each probe of the group on every backend it runs on, through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A Nothing that is not its
    /// type's default is a wrong printout or a build error: `null` on JavaScript (m1), CS0037 / CS0403 on C# (m2), `ne` or no build at all on a comparison (m3). A backend whose tool is missing is skipped;
    /// the test is ignored only when none could run.
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
/// #186 COMPILE, in process — no spawned CLI, no native compile, so it runs in the fast subset. What the default rule must NOT have opened: IDENTITY on a value type (`n Is Nothing`, `Case Is Nothing`: vbc's
/// BC30020), and the one JavaScript refusal that remains, by name.
/// </summary>
[TestFixture]
public class NothingIntoValueTypeCompileTests
{
    /// <summary>Parse (asserting no parse error: a typo in a probe must not pass as a refusal), analyze, and return every ERROR message.</summary>
    private static List<string> Errors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "the probe does not parse: " + string.Join("; ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();
    }

    /// <summary>
    /// vbc refuses `n Is Nothing` on an Integer (BC30020: `Is` needs a reference type) and so does a `Case Is Nothing`; the value form — `n = Nothing`, `Case Nothing` — is what #186 now runs. ONE error each, naming the
    /// operand and the type, with the fix in the message, and the value forms in the SAME program accepted (a front end that refused the whole program, or admitted the identity form, fails).
    /// </summary>
    [Test]
    public void IsNothing_AndCaseIsNothing_OnAValueType_StayRefused_ButTheValueFormsAreAccepted()
    {
        var isNothing = Errors("""
            Sub Main()
                Dim n As Integer = 3
                If n = Nothing Then Console.WriteLine("v")
                If n Is Nothing Then Console.WriteLine("x")
            End Sub
            """);
        var caseIsNothing = Errors("""
            Sub Main()
                Dim n As Integer = 3
                Select Case n
                    Case Nothing
                        Console.WriteLine("v")
                    Case Is Nothing
                        Console.WriteLine("x")
                End Select
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(isNothing, Has.Count.EqualTo(1), string.Join(" | ", isNothing));
            Assert.That(isNothing[0], Does.Contain("'Is' requires operands of a reference or nullable type, but 'n' is 'Integer', a value type"));
            Assert.That(caseIsNothing, Has.Count.EqualTo(1), string.Join(" | ", caseIsNothing));
            Assert.That(caseIsNothing[0], Does.Contain("'Case Is Nothing' requires a Select Case value of a reference or nullable type, but it is 'Integer', a value type"));
        });
    }

    /// <summary>
    /// JavaScript has no Long, Char, Decimal or ULong, so it refuses each BY NAME — with a Nothing exactly as with a written `0` — through the standard and the aggressive passes (a capability check, not an optimizer
    /// rule). The other groups run on it: an Integer, a Boolean and a Double hold no such type. (Group 6 of the execution fixture is C#, C++ and MSIL for the same reason.)
    /// </summary>
    [Test]
    public void JavaScript_StillRefuses_Long_Char_Decimal_AndULong_ByName_WithANothing()
    {
        var rows = new[]
        {
            ("Long", "Dim v As Long = Nothing", "BL7003"),
            ("Char", "Dim v As Char = Nothing", "BL7004"),
            ("Decimal", "Dim v As Decimal = Nothing", "BL7007"),
            ("ULong", "Dim v As ULong = Nothing", "BL7003"),
        };

        Assert.Multiple(() =>
        {
            foreach (var (type, declaration, code) in rows)
            {
                var source = $"Sub Main()\n    {declaration}\n    Console.WriteLine(v)\nEnd Sub";
                var standard = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(source), $"{type}, standard passes");
                var aggressive = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.CompileAggressive(source), $"{type}, aggressive passes");
                Assert.That(standard!.Message, Does.StartWith(code), $"{type}, standard passes");
                Assert.That(aggressive!.Message, Does.StartWith(code), $"{type}, aggressive passes");
            }
        });
    }
}
