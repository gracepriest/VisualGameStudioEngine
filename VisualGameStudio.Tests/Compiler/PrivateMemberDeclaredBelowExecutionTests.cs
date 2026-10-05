using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  #152 — a Private member declared BELOW the method that names it bare resolves inside its own class.
//
//  WHAT WAS WRONG. Pass 1 (`SemanticAnalyzer.PopulateClassMemberSignatures`) left Private members out of
//  `TypeInfo.Members`; ADR-0007's P17. Once `ResolveClassMember` read `Members` for a bare name, a Private
//  property, auto-property or field named bare ABOVE its declaration was "Undefined identifier" on all four
//  backends, a Private Const typed Object ("Arithmetic operator '+' requires numeric operands"), and a Private
//  Sub/Function compiled with its call typed Object (C++ did not compile; MSIL threw MissingMethodException).
//  The same shapes declared Public ran. The fix records Private members too (with their Access); visibility
//  is still `TypeInfo.ResolveMember`'s, which returns a Private member only for its OWN class.
//
//  THE ORACLE IS vbc. Every expected value below was checked by compiling the program, wrapped in a VB
//  Module, with vbc and running it (S/t136/tools/vbv2.py). No backend is the oracle.
//
//  HOW A ROW RUNS. Each execution row is one program run on all four backends (C#, C++, JavaScript, MSIL),
//  each through the real CLI, the CLI with --optimize and BasicCompiler.CompileProjectFiles with
//  OptimizeAggressive (`TempExec.AssertMatchesInEveryEntryPoint`, C# through CSharpProcessRunner). A backend
//  whose tool is missing (no C++ compiler, no Node, no ilasm) is SKIPPED, never failed: the row still runs the
//  others, a failure on any of them fails the row, and a row that ran only part of its legs ends Ignored with
//  the legs named — so it cannot read as a green run of a leg that never happened.
//
//  MUTANTS (rebuilt from the fix and swapped in; each is killed by the row named):
//    M1  a Private PROPERTY's Access recorded as Public       -> the R2u refusal row (the derived class resolves
//                                                                 the base's Private P and the program compiles)
//    M2  pass 1 skips Private FIELDS and CONSTS               -> P17f_P17c on every backend ("Undefined identifier
//                                                                 'F'" / "Arithmetic operator '+'"), P17sh on MSIL
//    M3  pass 1 skips Private SUBS and FUNCTIONS              -> P17s on C++ (the call types Object: C2440) and on
//                                                                 MSIL (MissingMethodException); C# and JavaScript
//                                                                 still ran under M3, so those two legs alone kill nothing
//
//  KNOWN GAPS — listed here, deliberately NOT tested (none is this defect; each is identical in the all-Public
//  twin or the declared-above twin of the same program):
//    - The front end does not refuse a Private member used from OUTSIDE its class, in either declaration order
//      (`c.F` from Main compiles; C# says CS0122, MSIL FieldAccessException, JavaScript has no private).
//      Pre-existing; task #273.
//    - `Me.<Const>` is CS0176 on C# and a wrong value on JavaScript; a Shared-PROPERTY increment is wrong on C++;
//      and a member declared below its bare use that SHADOWS a file-scope global binds the global on C# and
//      JavaScript. All pre-existing; task #274. (The rows below use a bare Const, a Shared FIELD and a Shared
//      read-only property, and no global of the same name, for exactly that reason.)
//    - A derived class naming its base's Private FUNCTION bare is not refused (R2uf's `Twice(1)`): pass 1
//      flattens every procedure signature into the global scope by bare name, so it resolves there. Only the
//      field in that program is refused, and only the field is pinned.
//    - Observed while writing these rows, not filed: C++ drops `R = Q * 10` after `Q = P + 1` on plain
//      auto-properties, in a single all-Public class on master too. T6 uses `R = 9` to stay clear of it.
//    - A derived class in ANOTHER file is "Unknown base class" (open in CLAUDE.md), so there is no cross-file
//      derived row; the cross-file row is a class with Private members used by its own methods.
//
//  ⚠ Named "…ExecutionTests" on purpose: it RUNS under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #152 RUN: a Private property, auto-property, field, Const, Sub, Function and Shared member named bare in a
/// method ABOVE its declaration compiles and prints vbc's answer on C#, C++, JavaScript and MSIL, through every
/// entry point; so does the same member reached through a lambda, another instance, a derived class (declared
/// above or below its base) and a class in another file.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class PrivateMemberDeclaredBelowExecutionTests
{
    // ---- the programs (each line of vbc's answer beside it) ---------------------------------------

    /// <summary>P17 — a Private property with Get/Set, named bare and as Me.P above its declaration, over a Public field.</summary>
    private const string P17 = """
        Class Box
            Public K As Integer

            Sub Work()
                P = 5
                Console.WriteLine(CStr(K) & "," & CStr(P))
                Me.P = 7
                Console.WriteLine(CStr(K) & "," & CStr(Me.P))
            End Sub

            Private Property P As Integer
                Get
                    Return K * 2
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    private const string P17Expected = "5,10\n7,14";

    /// <summary>P17a + P17o — a Private AUTO-property (bare and Me.), and another instance's Private property read in the class's own method.</summary>
    private const string P17aP17o = """
        Class AutoBox
            Sub Work()
                P = 5
                Console.WriteLine(CStr(P + 1))
                Me.P = Me.P + 10
                Console.WriteLine(CStr(P) & "," & CStr(Me.P))
            End Sub

            Private Property P As Integer
        End Class

        Class PairBox
            Function Same(other As PairBox) As Boolean
                Return other.Q = Q
            End Function

            Sub Init(v As Integer)
                Q = v
            End Sub

            Private Property Q As Integer
        End Class

        Sub Main()
            Dim a As New AutoBox()
            a.Work()
            Dim x As New PairBox()
            Dim y As New PairBox()
            x.Init(3)
            y.Init(3)
            Console.WriteLine(CStr(x.Same(y)))
            y.Init(4)
            Console.WriteLine(CStr(x.Same(y)))
        End Sub
        """;

    private const string P17aP17oExpected = "6\n15,15\nTrue\nFalse";

    /// <summary>P17f + P17c — a Private FIELD (bare and Me.) and a Private CONST (bare; Me.Const is #274) above their declarations. ⛔ Kills M2.</summary>
    private const string P17fP17c = """
        Class FieldBox
            Sub Work()
                F = 3
                Console.WriteLine(CStr(F * 2))
                Me.F = Me.F + 1
                Console.WriteLine(CStr(F) & "," & CStr(Me.F))
            End Sub

            Private F As Integer
        End Class

        Class ConstBox
            Function Make() As Integer
                Return Limit + Limit + 2
            End Function

            Private Const Limit As Integer = 20
        End Class

        Sub Main()
            Dim a As New FieldBox()
            a.Work()
            Dim b As New ConstBox()
            Console.WriteLine(CStr(b.Make()))
        End Sub
        """;

    private const string P17fP17cExpected = "6\n4,4\n42";

    /// <summary>P17s — a Private SUB and a Private FUNCTION named bare and as Me.X above their declarations. ⛔ Kills M3 (C++ and MSIL).</summary>
    private const string P17s = """
        Class Box
            Public N As Integer

            Sub Work()
                Bump(4)
                Me.Bump(5)
                Console.WriteLine(CStr(N) & "," & CStr(Twice(N)) & "," & CStr(Me.Twice(1)))
            End Sub

            Private Sub Bump(by As Integer)
                N = N + by
            End Sub

            Private Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    private const string P17sExpected = "9,18,2";

    /// <summary>P17sh — Private SHARED members: a field, a read-only property and a function, named bare and through the class, shared across instances. ⛔ Also kills M2 on MSIL.</summary>
    private const string P17sh = """
        Class Tally
            Sub Work()
                Hits = Hits + 1
                Console.WriteLine(CStr(Hits) & "," & CStr(Half(Hits)) & "," & CStr(Margin) & "," & CStr(Tally.Hits))
            End Sub

            Private Shared Hits As Integer = 7
            Private Shared ReadOnly Property Margin As Integer
                Get
                    Return 3
                End Get
            End Property

            Private Shared Function Half(x As Integer) As Integer
                Return x \ 2
            End Function
        End Class

        Sub Main()
            Dim a As New Tally()
            Dim b As New Tally()
            a.Work()
            b.Work()
        End Sub
        """;

    private const string P17shExpected = "8,4,3,8\n9,4,3,9";

    /// <summary>
    /// T6 — a derived class declared ABOVE its base and one declared BELOW it, each naming the base's Protected and Public
    /// members bare (declared below the base's own bare use of its Private P). The Private P must not get in the way.
    /// </summary>
    private const string Derived = """
        Class DerivedAbove
            Inherits Base

            Function Show() As Integer
                Return Q + R
            End Function
        End Class

        Class Base
            Sub Work()
                P = 5
                Q = P + 1
                R = 9
            End Sub

            Private Property P As Integer
            Protected Property Q As Integer
            Public Property R As Integer
        End Class

        Class DerivedBelow
            Inherits Base

            Function Show() As Integer
                Return Q + R + 1
            End Function
        End Class

        Sub Main()
            Dim a As New DerivedAbove()
            a.Work()
            Console.WriteLine(CStr(a.Show()))
            Dim b As New DerivedBelow()
            b.Work()
            Console.WriteLine(CStr(b.Show()))
        End Sub
        """;

    private const string DerivedExpected = "15\n16";

    /// <summary>
    /// T7 — the base's Private P and a derived class's OWN P, one name: the base's methods bind the base's P, the derived class's
    /// (declared BELOW its bare use) binds its own. Pass 1 now records both, so neither may take the other's.
    /// </summary>
    private const string SameNameInDerived = """
        Class Base
            Public Sub Work()
                P = 5
                Console.WriteLine("base " & CStr(P))
            End Sub

            Private Property P As Integer
        End Class

        Class Derived
            Inherits Base

            Public Sub Own()
                P = 7
                Console.WriteLine("derived " & CStr(P))
            End Sub

            Public Property P As Integer
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
            d.Own()
        End Sub
        """;

    private const string SameNameInDerivedExpected = "base 5\nderived 7";

    /// <summary>T9 — a Private property named bare INSIDE A LAMBDA that sits above the property's declaration (the closure captures Me).</summary>
    private const string InLambda = """
        Class Box
            Function Run() As Integer
                P = 4
                Dim add As Func(Of Integer, Integer) = Function(x As Integer) x + P
                P = 10
                Return add(1)
            End Function

            Private Property P As Integer
        End Class

        Sub Main()
            Dim b As New Box()
            Console.WriteLine(CStr(b.Run()))
        End Sub
        """;

    private const string InLambdaExpected = "11";

    /// <summary>T8 — the class lives in ANOTHER FILE (the cross-file sibling axis of the same helper) and Main only calls it.</summary>
    private static readonly (string Name, string Source)[] TwoFiles =
    {
        ("Counter.bas", """
            Class Counter
                Public Sub Bump(by As Integer)
                    Delta = by
                    Total = Total + Scaled(Delta)
                End Sub

                Public Function Report() As String
                    Return CStr(Total) & "/" & CStr(Delta)
                End Function

                Private Property Delta As Integer
                Private Total As Integer
                Private Function Scaled(x As Integer) As Integer
                    Return x * 2
                End Function
            End Class
            """),
        ("Main.bas", """
            Sub Main()
                Dim c As New Counter()
                c.Bump(3)
                c.Bump(4)
                Console.WriteLine(c.Report())
            End Sub
            """),
    };

    private const string TwoFilesExpected = "14/4";

    // ---- the rows ---------------------------------------------------------------------------------

    /// <summary>
    /// Every single-file program, on every backend, through every entry point. Before #152 each of these was refused by the front
    /// end ("Undefined identifier", "Arithmetic operator '+' requires numeric operands") or, for the Sub/Function, compiled to a call
    /// typed Object that C++ rejected and MSIL could not bind.
    /// </summary>
    [TestCase(P17, P17Expected, TestName = "P17_PrivatePropertyWithGetSet_NamedBareAboveItsDeclaration")]
    [TestCase(P17aP17o, P17aP17oExpected, TestName = "P17a_P17o_PrivateAutoPropertyAndAnotherInstancesProperty")]
    [TestCase(P17fP17c, P17fP17cExpected, TestName = "P17f_P17c_PrivateFieldAndConst")]
    [TestCase(P17s, P17sExpected, TestName = "P17s_PrivateSubAndFunction")]
    [TestCase(P17sh, P17shExpected, TestName = "P17sh_PrivateSharedFieldPropertyAndFunction")]
    [TestCase(Derived, DerivedExpected, TestName = "P17u_DerivedClassDeclaredAboveAndBelowItsBase_NamesProtectedAndPublicMembers")]
    [TestCase(SameNameInDerived, SameNameInDerivedExpected, TestName = "P17d_BasePrivatePAndDerivedOwnP_EachBindsItsOwn")]
    [TestCase(InLambda, InLambdaExpected, TestName = "P17l_PrivatePropertyNamedBareInALambda")]
    public void APrivateMemberDeclaredBelowItsBareUse_PrintsVbcsAnswer_OnEveryBackend(string source, string expected)
        => OnEveryBackend(TestContext.CurrentContext.Test.Name, (backend, label) => TempExec.AssertMatchesInEveryEntryPoint(backend, source, expected, label, hangSafe: true));

    /// <summary>
    /// The same, for a class in another file: the project entry points (<c>BasicLang build</c> Debug and Release, and
    /// <c>CompileProjectFiles</c> standard and aggressive) take the sibling axis of the pass-1 helper as well as the in-file one.
    /// </summary>
    [Test]
    public void P17m_PrivateMembersOfAClassInAnotherFile_PrintVbcsAnswer_OnEveryBackend()
        => OnEveryBackend(TestContext.CurrentContext.Test.Name, (backend, label) =>
        {
            TempExec.RequireTool(backend);
            BindingProjectExec.RequireDotnetForCliCSharp(backend);
            var failures = new List<string>();
            foreach (var entry in BindingProjectExec.Entries(backend))
            {
                try
                {
                    // ⛔ the C# leg goes through the child-process runner, never the in-process one (no timeout there)
                    var got = TempExec.Norm(TempExec.Run(backend, BindingProjectExec.Emit(backend, entry, TwoFiles), hangSafe: true));
                    if (got != TempExec.Norm(TwoFilesExpected))
                        failures.Add($"{entry}: printed [{got.Replace("\n", " | ")}] where VB prints [{TempExec.Norm(TwoFilesExpected).Replace("\n", " | ")}]");
                }
                catch (AssertionException ex)
                {
                    failures.Add($"{entry}: {ex.Message.Split('\n')[0]}");
                }
            }
            Assert.That(failures, Is.Empty, $"{label} on {backend}, through {string.Join(", ", BindingProjectExec.Entries(backend))}:\n" + string.Join("\n", failures));
        });

    /// <summary>
    /// Runs <paramref name="leg"/> once per backend. A failure on any backend fails the row (after every backend has been tried);
    /// a backend whose tool is missing is skipped by the existing gates and named; a row that skipped any leg ends Ignored, after
    /// its failures have been thrown, so a skip can never hide one.
    /// </summary>
    private static void OnEveryBackend(string label, Action<Bk, string> leg)
    {
        var failures = new List<string>();
        var skipped = new List<string>();
        var ran = new List<string>();
        foreach (var backend in TempExec.Backends(Bk.All))
        {
            try
            {
                leg(backend, label);
                ran.Add(backend.ToString());
            }
            catch (IgnoreException ex)
            {
                skipped.Add($"{backend} ({ex.Message})");
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (skipped.Count > 0)
            Assert.Ignore($"ran and passed on [{string.Join(", ", ran)}]; skipped: {string.Join("; ", skipped)}");
    }
}

/// <summary>
/// #152, REFUSALS (front end only, no process spawned — in the fast subset): a derived class naming its base's Private member
/// bare is STILL refused, with the base declared above or below it. This is the other half of the fix: pass 1 now records the
/// base's Private members, and only <c>TypeInfo.ResolveMember</c>'s own-class rule keeps them out of a derived class's reach.
/// ⛔ Kills M1 (a Private property's Access recorded as Public: the derived class then resolves it and the program compiles).
///
/// <para>Each program is checked twice — the analyzer directly, and <c>BasicCompiler.CompileProjectFiles</c> (what the IDE's build
/// service calls) — and its Protected twin (every <c>Private</c> spelled <c>Protected</c>) must be ACCEPTED, so the refusal is
/// the access level and not the program.</para>
/// </summary>
[TestFixture]
public class PrivateMemberDeclaredBelowRefusalTests
{
    /// <summary>R2 — the base declared ABOVE the derived class; the derived <c>Show</c> names the base's Private P bare on line 13.</summary>
    private const string R2 = """
        Class Base
            Sub Work()
                P = 5
            End Sub

            Private Property P As Integer
        End Class

        Class Derived
            Inherits Base

            Sub Show()
                Console.WriteLine(CStr(P))
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
            d.Show()
        End Sub
        """;

    /// <summary>R2u — the base declared BELOW the derived class; the derived <c>Show</c> names the base's Private P bare on line 5.</summary>
    private const string R2u = """
        Class Derived
            Inherits Base

            Sub Show()
                Console.WriteLine(CStr(P))
            End Sub
        End Class

        Class Base
            Sub Work()
                P = 5
            End Sub

            Private Property P As Integer
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
            d.Show()
        End Sub
        """;

    /// <summary>R2uf — the base declared BELOW, a Private FIELD (refused) and a Private Function (not refused: see the known gaps).</summary>
    private const string R2uf = """
        Class Derived
            Inherits Base

            Function Show() As Integer
                Return F + Twice(1)
            End Function
        End Class

        Class Base
            Private F As Integer = 3

            Private Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(CStr(d.Show()))
        End Sub
        """;

    [TestCase(R2, 13, "P", TestName = "R2_BaseDeclaredAbove_DerivedNamesItsPrivatePropertyBare_IsRefused")]
    [TestCase(R2u, 5, "P", TestName = "R2u_BaseDeclaredBelow_DerivedNamesItsPrivatePropertyBare_IsRefused")]
    [TestCase(R2uf, 5, "F", TestName = "R2uf_BaseDeclaredBelow_DerivedNamesItsPrivateFieldBare_IsRefused")]
    public void ADerivedClassNamingItsBasesPrivateMemberBare_IsRefused(string source, int line, string name)
    {
        var expectedText = $"Undefined identifier '{name}'";

        var refused = Analyze(source);
        var protectedTwin = Analyze(source.Replace("Private ", "Protected "));
        var viaProject = CompileProject(source);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Accepted, Is.False, "the analyzer must refuse a derived class naming its base's Private member");
            Assert.That(refused.Errors.Any(e => e.Line == line && e.Message.Contains(expectedText)), Is.True,
                $"expected \"{expectedText}\" on line {line}; got: " + Describe(refused.Errors));

            Assert.That(viaProject.HasErrors, Is.True, "CompileProjectFiles (the IDE's build) must refuse it too");
            Assert.That(viaProject.AllErrors.Any(e => e.Line == line && e.Message.Contains(expectedText)), Is.True,
                $"CompileProjectFiles: expected \"{expectedText}\" on line {line}; got: " + Describe(viaProject.AllErrors));

            Assert.That(protectedTwin.Accepted, Is.True,
                "the same program with the member Protected must be accepted, or the refusal is not about access: " + Describe(protectedTwin.Errors));
        });
    }

    private static (bool Accepted, List<SemanticError> Errors) Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        var accepted = analyzer.Analyze(ast);
        return (accepted, analyzer.Errors.ToList());
    }

    private static CompilationResult CompileProject(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t152-refuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            return new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path });
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static string Describe(IEnumerable<SemanticError> errors)
        => string.Join(" | ", errors.Select(e => $"line {e.Line}: {e.Message}"));
}
