using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.ProjectSystem;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0015 (<c>docs/superpowers/decisions/0015-cpp-me-as-value-two-phase-construction.md</c>),
/// task #200: <c>Me</c> as a VALUE on the C++ backend, and the two-phase construction
/// (<c>BasicLang::New</c> / tag constructor / <c>ctor_</c>) that makes it sound inside a
/// constructor too.
///
/// <para>Every test here COMPILES AND RUNS the generated C++ (never a text-only assertion for
/// behaviour) and asserts against VB's own answer — the exact contract ADR-0015 states under
/// "Byte identity" and "Obligations". The M-numbered probes are the ADR's own repro corpus
/// (<c>S/t200/probes/M1.bas</c>…<c>M7.bas</c>); the E-numbered ones are its edge corpus
/// (<c>S/t200/edge/E*.bas</c>). Both pipelines are exercised — <see cref="Cpp"/> (the CLI's
/// default <c>AddStandardPasses</c> pipeline, where <c>StrengthReductionPass</c> lives — the
/// exact pass that made <c>IRConstructor.BaseConstructorArgs</c> go stale, see
/// <see cref="E11_AComputedMyBaseNewArgument_SurvivesTheOptimizerRewritingItsOwnOperand"/>) and
/// <see cref="CppAgg"/> (<c>-O</c>, the CLI's <c>--optimize</c> / a Release <c>.blproj</c>
/// pipeline).</para>
///
/// <para>A missing C++ compiler SKIPS (never fails) via <c>BclE2E.CompileRun</c>'s own
/// <c>Assert.Ignore</c>, per CLAUDE.md's native-build gates.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg (FourBackends) redirects Console.Out
public class CppMeAsValueTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static string Cpp(string program) => Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)));
    private static string CppAgg(string program) => Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(program)));

    /// <summary>Both C++ pipelines must print VB's answer; C#/JS/MSIL (already correct pre-#200) must too.</summary>
    private static void AssertMPattern(string program, string expected)
    {
        FourBackends.RunsOnEveryBackend(program, expected);
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    // ================================================================================
    // M1-M7 — ADR-0015's own repro corpus. Every one of these failed to COMPILE before
    // #200 ("no viable conversion from 'Node *' to 'std::shared_ptr<Node>'"); C#, JS and
    // MSIL already printed VB's answer.
    // ================================================================================

    /// <summary>M1: <c>Me</c> passed as an ordinary argument to another object's method.</summary>
    [Test]
    public void M1_MeAsAnArgument_ToAnotherObjectsMethod()
        => AssertMPattern("""
            Class Registry
                Public Count As Integer
                Public Last As Node
                Public Sub Add(n As Node)
                    Count = Count + 1
                    Last = n
                End Sub
            End Class
            Class Node
                Public V As Integer
                Public Sub Register(r As Registry)
                    r.Add(Me)
                End Sub
            End Class
            Sub Main()
                Dim r As New Registry()
                Dim a As New Node()
                a.V = 7
                a.Register(r)
                Console.WriteLine(r.Count)
                Console.WriteLine(r.Last.V)
                Console.WriteLine(r.Last Is a)
            End Sub
            """, "1\n7\nTrue");

    /// <summary>M2: <c>Return Me</c> and <c>Dim n As Node = Me</c> — <c>Me</c> as a return value and a local.</summary>
    [Test]
    public void M2_MeReturned_AndAliasedThroughALocal()
        => AssertMPattern("""
            Class Node
                Public V As Integer
                Public Function Self() As Node
                    Return Me
                End Function
                Public Function Aliased() As Node
                    Dim n As Node = Me
                    Return n
                End Function
            End Class
            Sub Main()
                Dim a As New Node()
                a.V = 3
                Console.WriteLine(a.Self().V)
                Console.WriteLine(a.Aliased().V)
                Console.WriteLine(a.Self() Is a)
            End Sub
            """, "3\n3\nTrue");

    /// <summary>M3: <c>Me</c> added to a generic <c>List(Of Node)</c>.</summary>
    [Test]
    public void M3_MeAddedToAGenericList()
        => AssertMPattern("""
            Class Node
                Public V As Integer
                Public Sub AddTo(list As List(Of Node))
                    list.Add(Me)
                End Sub
            End Class
            Sub Main()
                Dim lst As New List(Of Node)()
                Dim a As New Node()
                a.V = 4
                a.AddTo(lst)
                a.AddTo(lst)
                Console.WriteLine(lst.Count)
                Console.WriteLine(lst(1).V)
            End Sub
            """, "2\n4");

    /// <summary>
    /// M4: <c>Me</c> handed out of <c>Sub New</c> itself — the registration pattern D2 exists
    /// to make sound: ownership must already exist while the constructor is still running.
    /// </summary>
    [Test]
    public void M4_MePassedOutOfSubNew_TheRegistrationPattern()
        => AssertMPattern("""
            Class Registry
                Public Count As Integer
                Public Last As Node
                Public Sub Add(n As Node)
                    Count = Count + 1
                    Last = n
                End Sub
            End Class
            Class Node
                Public V As Integer
                Public Sub New(r As Registry, v0 As Integer)
                    V = v0
                    r.Add(Me)
                End Sub
            End Class
            Sub Main()
                Dim r As New Registry()
                Dim a As New Node(r, 5)
                Dim b As New Node(r, 6)
                Console.WriteLine(r.Count)
                Console.WriteLine(r.Last.V)
                Console.WriteLine(r.Last Is b)
            End Sub
            """, "2\n6\nTrue");

    /// <summary>
    /// M5: <c>Me</c> from a BASE method and a DERIVED method, into base- and derived-typed
    /// parameters. Also the flagship kill of the "enable_shared_from_this on every class" mutant
    /// (m1): with <c>Dog</c> ALSO carrying its own <c>enable_shared_from_this</c>, the object has
    /// two such subobjects and <c>shared_from_this()</c> is AMBIGUOUS — a C++ compile error, not a
    /// wrong answer.
    /// </summary>
    [Test]
    public void M5_MeFromBaseAndDerivedMethods_IntoBaseAndDerivedTypedParameters()
        => AssertMPattern("""
            Class Zoo
                Public Count As Integer
                Public Sub Admit(a As Animal)
                    Count = Count + 1
                End Sub
                Public Sub AdmitDog(d As Dog)
                    Count = Count + 10
                End Sub
            End Class
            Class Animal
                Public Name As String
                Public Sub Announce(z As Zoo)
                    z.Admit(Me)
                End Sub
            End Class
            Class Dog
                Inherits Animal
                Public Sub Bark(z As Zoo)
                    z.Admit(Me)
                    z.AdmitDog(Me)
                End Sub
            End Class
            Sub Main()
                Dim z As New Zoo()
                Dim d As New Dog()
                d.Announce(z)
                d.Bark(z)
                Console.WriteLine(z.Count)
            End Sub
            """, "12");

    /// <summary>M6: <c>Me</c> as an <c>Is</c> operand, and passed into a Shared method from an instance one.</summary>
    [Test]
    public void M6_MeAsAnIsOperand_AndPassedIntoASharedMethod()
        => AssertMPattern("""
            Class Node
                Public V As Integer
                Public Function Same(other As Node) As Boolean
                    Return other Is Me
                End Function
            End Class
            Class Caller
                Public V As Integer = 9
                Public Sub Go()
                    Show2(Me)
                End Sub
                Public Shared Sub Show2(c As Caller)
                    Console.WriteLine(c.V)
                End Sub
            End Class
            Sub Main()
                Dim a As New Node()
                Dim b As New Node()
                Console.WriteLine(a.Same(a))
                Console.WriteLine(a.Same(b))
                Dim c As New Caller()
                c.Go()
            End Sub
            """, "True\nFalse\n9");

    /// <summary>M7: <c>Me</c> stored into another object's field.</summary>
    [Test]
    public void M7_MeStoredIntoAField()
        => AssertMPattern("""
            Class Node
                Public V As Integer
                Public NextNode As Node
                Public Sub LinkTo(other As Node)
                    other.NextNode = Me
                End Sub
            End Class
            Sub Main()
                Dim a As New Node()
                Dim b As New Node()
                a.V = 1
                b.V = 2
                a.LinkTo(b)
                Console.WriteLine(b.NextNode.V)
            End Sub
            """, "1");

    // ================================================================================
    // D2: two-phase construction order (S/t200/edge/E01-E17). Only the C++ leg is
    // asserted here — E01 is a KNOWN divergence on C#/JS (task #234, its own pin below);
    // the rest are new-to-#200 shapes this suite had never run before.
    // ================================================================================

    /// <summary>
    /// D2's headline behaviour change (V1): a VIRTUAL CALL FROM A BASE CONSTRUCTOR now
    /// dispatches to the DERIVED override, which sees its fields still at their .NET
    /// DEFAULT (<c>0</c>/<c>""</c>) — exactly .NET's own answer, because ownership (and so
    /// <c>Me</c>) exists before <c>ctor_</c>'s body runs, but field initializers are still
    /// PENDING at that point (they are step 2, run inside <c>ctor_</c>, never the tag
    /// constructor). Before #200 the one-phase constructor produced
    /// "Derived.Describe 42 [d]" here — the DERIVED fields, wrongly, already initialized.
    /// Kills mutants m4 (one-phase), m5 (field inits after body), m6 (base call after field
    /// inits) and mx_tag_ctor_no_field_defaults — all four give a DIFFERENT first line.
    /// </summary>
    [Test]
    public void E01_AVirtualCallFromABaseConstructor_DispatchesToTheDerivedOverride_SeeingItsDefaults()
    {
        const string program = """
            Class Base
                Public Sub New()
                    Console.WriteLine("Base.New")
                    Describe()
                End Sub
                Public Overridable Sub Describe()
                    Console.WriteLine("Base.Describe")
                End Sub
            End Class
            Class Derived
                Inherits Base
                Public Tag As Integer = 42
                Public Name As String = "d"
                Public Sub New()
                    MyBase.New()
                    Console.WriteLine("Derived.New " & Tag)
                End Sub
                Public Overrides Sub Describe()
                    Console.WriteLine("Derived.Describe " & Tag & " [" & Name & "]")
                End Sub
            End Class
            Sub Main()
                Dim d As New Derived()
                d.Describe()
            End Sub
            """;
        const string expected = "Base.New\nDerived.Describe 0 []\nDerived.New 42\nDerived.Describe 42 [d]";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// The SAME program's C# and JavaScript legs are a KNOWN, PRE-EXISTING, UNRELATED gap
    /// (#234): field initializers run too EARLY on both — measured. C# prints
    /// "Derived.Describe 42 [d]" (Tag/Name already at their initializer values during the
    /// base-constructor virtual call); JavaScript prints "Derived.Describe undefined []"
    /// (an uninitialized JS class field reads <c>undefined</c>, not .NET's <c>0</c>). Neither
    /// backend changed here — #200 has no IR change (ADR-0015's Obligations) — so a DIFFERENT
    /// wrong answer on either leg means #234 moved, not that #200 regressed it.
    /// </summary>
    [Test]
    public void E01_OnCSharpAndJavaScript_IsAPreExistingGap_PinnedAgainst234()
    {
        const string program = """
            Class Base
                Public Sub New()
                    Console.WriteLine("Base.New")
                    Describe()
                End Sub
                Public Overridable Sub Describe()
                    Console.WriteLine("Base.Describe")
                End Sub
            End Class
            Class Derived
                Inherits Base
                Public Tag As Integer = 42
                Public Name As String = "d"
                Public Sub New()
                    MyBase.New()
                    Console.WriteLine("Derived.New " & Tag)
                End Sub
                Public Overrides Sub Describe()
                    Console.WriteLine("Derived.Describe " & Tag & " [" & Name & "]")
                End Sub
            End Class
            Sub Main()
                Dim d As New Derived()
                d.Describe()
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)),
                Is.EqualTo("Base.New\nDerived.Describe 42 [d]\nDerived.New 42\nDerived.Describe 42 [d]"),
                "PINNED (#234): if this changed, C#'s field-initializer/base-call ordering moved — " +
                "re-measure before touching this pin.\nC#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)),
                Is.EqualTo("Base.New\nDerived.Describe undefined []\nDerived.New 42\nDerived.Describe 42 [d]"),
                "PINNED (#234): if this changed, JavaScript's field-initializer/base-call ordering " +
                "moved — re-measure before touching this pin.\nJavaScript");
        });
    }

    /// <summary>D2 step 2: field initializers, in declaration order, across THREE generations.</summary>
    [Test]
    public void E04_FieldInitializers_RunAfterTheBaseCtor_AndBeforeTheBody_AcrossThreeGenerations()
    {
        const string program = """
            Class Base
                Public BaseField As Integer = 10
                Public Sub New(seed As Integer)
                    Console.WriteLine("Base.New seed=" & seed & " BaseField=" & BaseField)
                    BaseField = BaseField + seed
                End Sub
            End Class
            Class Derived
                Inherits Base
                Public DerivedField As Integer = 20
                Public Sub New(seed As Integer)
                    MyBase.New(seed)
                    Console.WriteLine("Derived.New BaseField=" & BaseField & " DerivedField=" & DerivedField)
                    DerivedField = DerivedField + BaseField
                End Sub
            End Class
            Class Leaf
                Inherits Derived
                Public LeafField As Integer = 30
                Public Sub New()
                    MyBase.New(5)
                    Console.WriteLine("Leaf.New LeafField=" & LeafField & " DerivedField=" & DerivedField)
                End Sub
            End Class
            Sub Main()
                Dim x As New Leaf()
                Console.WriteLine(x.BaseField & " " & x.DerivedField & " " & x.LeafField)
            End Sub
            """;
        const string expected =
            "Base.New seed=5 BaseField=10\nDerived.New BaseField=15 DerivedField=20\n" +
            "Leaf.New LeafField=30 DerivedField=35\n15 35 30";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>Overloaded AND Optional constructors (E03) all four resolve and construct correctly.</summary>
    [Test]
    public void E03_OverloadedAndOptionalConstructors_AllFourShapesConstructCorrectly()
    {
        const string program = """
            Class Box
                Public W As Integer = 1
                Public H As Integer = 1
                Public Label As String = "box"
                Public Sub New()
                End Sub
                Public Sub New(w0 As Integer)
                    W = w0
                End Sub
                Public Sub New(w0 As Integer, h0 As Integer, Optional lbl As String = "opt")
                    W = w0
                    H = h0
                    Label = lbl
                End Sub
                Public Function Show() As String
                    Return Label & ":" & W & "x" & H
                End Function
            End Class
            Sub Main()
                Dim a As New Box()
                Dim b As New Box(3)
                Dim c As New Box(4, 5)
                Dim d As New Box(6, 7, "named")
                Console.WriteLine(a.Show())
                Console.WriteLine(b.Show())
                Console.WriteLine(c.Show())
                Console.WriteLine(d.Show())
            End Sub
            """;
        const string expected = "box:1x1\nbox:3x1\nopt:4x5\nnamed:6x7";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// E12 — a DIVERGENCE FIX, recorded as a deliberate behaviour change (ADR-0015 amendment,
    /// "corrected premises" #3 / E12). The old one-phase constructor's initializer list carried
    /// an <c>X(x)</c> heuristic: any parameter sharing a field's name silently initialized that
    /// field, so this program used to print "3 4" — VB itself prints "0 4" (<c>X</c> is never
    /// assigned; only <c>Me.Y = y</c> is). <c>ctor_</c> never synthesizes a field store from a
    /// parameter's name; only an explicit statement does.
    /// </summary>
    [Test]
    public void E12_AParameterNamedLikeAField_IsNeverSilentlyStored_TheOldHeuristicIsGone()
    {
        const string program = """
            Class P
                Public X As Integer
                Public Y As Integer
                Public Sub New(x As Integer, y As Integer)
                    Me.Y = y
                End Sub
            End Class
            Sub Main()
                Dim p As New P(3, 4)
                Console.WriteLine(p.X & " " & p.Y)
            End Sub
            """;
        const string expected = "0 4";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// E11 — THE staleness test. <c>a * 2</c> is exactly the shape
    /// <c>StrengthReductionPass</c> (in <c>AddStandardPasses</c> — the CLI's DEFAULT pipeline,
    /// no <c>-O</c> needed) rewrites to <c>a &lt;&lt; 1</c>, replacing the operand node
    /// <c>IRConstructor.BaseConstructorArgs</c> still points at. <c>CppObjectModel.PlanBaseArguments</c>
    /// must recognise the now-stale node as an OPERATOR and render it inline from its own
    /// operands (a parameter and a constant — needing no prefix), rather than either crashing,
    /// looking up a temp that no longer holds it, or refusing a shape that used to compile-error
    /// before #200 ("use of undeclared identifier") and must not regress. Kills mutant m4
    /// (COMPILE-FAIL) and is the direct evidence for the ADR's own "BaseConstructorArgs can be
    /// STALE" implementation note.
    /// </summary>
    [Test]
    public void E11_AComputedMyBaseNewArgument_SurvivesTheOptimizerRewritingItsOwnOperand()
    {
        const string program = """
            Class Base
                Public BV As Integer
                Public Sub New(v As Integer)
                    BV = v
                End Sub
            End Class
            Class Derived
                Inherits Base
                Public Sub New(a As Integer)
                    MyBase.New(a * 2)
                End Sub
            End Class
            Sub Main()
                Dim d As New Derived(21)
                Console.WriteLine(d.BV)
            End Sub
            """;
        const string expected = "42";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++ (default pipeline — StrengthReductionPass runs here)");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// A CALL as a <c>MyBase.New</c> argument, into an ordinary (non-foreign) hierarchy —
    /// admitted, unlike D2a's foreign-rooted purity rule, because nothing here evaluates the
    /// argument twice. Written for #200 (not part of the ADR's own probe set) specifically to
    /// KILL the "base call always at the top" mutant (<c>mx_prologue_at_top</c>): the measured
    /// matrix shows only <see cref="E16_AnArrayLiteralMyBaseNewArgument_PlacesItsElementStoresBeforeTheBaseCall"/>
    /// caught that mutant, and a mutant a single test happens to catch is one accidental removal
    /// away from an invisible regression. If the prologue (the base <c>ctor_</c> call) is written
    /// before <c>Twice(a)</c> actually runs, <c>BV</c> reads the temp's un-set default (0) instead
    /// of 42.
    /// </summary>
    [Test]
    public void ACallAsAMyBaseNewArgument_RunsBeforeTheBaseConstructor_KillsThePrologueAtTopMutant()
    {
        const string program = """
            Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
            Class Base
                Public BV As Integer
                Public Sub New(v As Integer)
                    BV = v
                End Sub
            End Class
            Class Derived
                Inherits Base
                Public Sub New(a As Integer)
                    MyBase.New(Twice(a))
                End Sub
            End Class
            Sub Main()
                Dim d As New Derived(21)
                Console.WriteLine(d.BV)
            End Sub
            """;
        const string expected = "42";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// An ARRAY LITERAL <c>MyBase.New</c> argument: the allocation plus its per-element stores
    /// are part of evaluating the argument even though no operand edge leads to the stores
    /// (<c>CppObjectModel.PlanBaseArguments</c>'s "array literal" extension). The ADR's own
    /// mutation matrix names this as the ONE existing probe that caught <c>mx_prologue_at_top</c>
    /// before #200's own test above was added.
    /// </summary>
    [Test]
    public void E16_AnArrayLiteralMyBaseNewArgument_PlacesItsElementStoresBeforeTheBaseCall()
    {
        const string program = """
            Class Base
                Public Total As Integer
                Public Sub New(xs As Integer())
                    For Each x As Integer In xs
                        Total = Total + x
                    Next
                End Sub
            End Class
            Class Derived
                Inherits Base
                Public Sub New(a As Integer)
                    MyBase.New(New Integer() {a, 2, 3})
                End Sub
            End Class
            Sub Main()
                Dim d As New Derived(1)
                Console.WriteLine(d.Total)
            End Sub
            """;
        const string expected = "6";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// <c>AndAlso</c> builds its value across several statements (a local set in TWO blocks, one
    /// per branch) — refused BY NAME, not guessed at, because <c>Base::ctor_</c> is placed
    /// immediately after the straight-line prefix that computes the argument (E11's placement
    /// rule) and a branchy value has no such prefix. Never compiled before #200 either ("use of
    /// undeclared identifier"), so refusing it regresses nothing.
    /// </summary>
    [Test]
    public void E15_AndAlsoInAMyBaseNewArgument_IsRefusedByName()
    {
        const string program = """
            Class Base
                Public BV As Boolean
                Public N As Integer
                Public Sub New(v As Boolean, n0 As Integer)
                    BV = v
                    N = n0
                End Sub
            End Class
            Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
            Class Derived
                Inherits Base
                Public W As Integer = 3
                Public Sub New(a As Integer, b As Integer)
                    MyBase.New(a > 0 AndAlso b > 0, Twice(a) + 1)
                    W = W + 1
                End Sub
            End Class
            Sub Main()
                Dim d As New Derived(2, 5)
                Console.WriteLine(d.BV & " " & d.N & " " & d.W)
            End Sub
            """;
        var ex = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(program));
        Assert.That(ex!.Message, Does.Contain("AndAlso").And.Contain("ADR-0015"),
            "the refusal must still name AndAlso/OrElse and ADR-0015 — a different message here " +
            "means the refusal moved.\n" + ex.Message);
    }

    /// <summary>
    /// The registration-loop pattern (E10): TWO classes, each handing <c>Me</c> out of its own
    /// constructor into a caller-owned list, five and three times respectively, across a `For`
    /// loop. Kills mutant m4 (RUN-FAIL: bad_weak_ptr under one-phase construction).
    /// </summary>
    [Test]
    public void E10_TheRegistrationLoopPattern_MeHandedOutOfEveryConstructorCall()
    {
        const string program = """
            Class Item
                Public Shared All As List(Of Item)
                Public Id As Integer
                Public Sub New(id0 As Integer)
                    Id = id0
                    All.Add(Me)
                End Sub
            End Class
            Class Group
                Public Members As List(Of Group)
                Public Tag As Integer
                Public Sub New(owner As List(Of Group), t As Integer)
                    Tag = t
                    owner.Add(Me)
                End Sub
            End Class
            Sub Main()
                Item.All = New List(Of Item)()
                For i As Integer = 1 To 5
                    Dim it As New Item(i)
                Next
                Dim s As Integer = 0
                For Each x As Item In Item.All
                    s = s + x.Id
                Next
                Console.WriteLine(Item.All.Count & " " & s)
                Dim gs As New List(Of Group)()
                For j As Integer = 1 To 3
                    Dim g As New Group(gs, j * 10)
                Next
                Console.WriteLine(gs.Count & " " & gs(2).Tag)
            End Sub
            """;
        const string expected = "5 15\n3 30";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// E17 — a base-declared method hands <c>Me</c> out from inside a DERIVED instance that adds
    /// nothing to <c>Announce</c>. <c>Animal</c> is the hierarchy ROOT (D1); <c>Dog</c> adds no
    /// <c>enable_shared_from_this</c> of its own, so <c>Self(this)</c> called with <c>this</c>
    /// pointing at the <c>Animal</c> subobject of a <c>Dog</c> instance still names the WHOLE
    /// object, and the identity check against the original <c>Dog</c> handle holds.
    /// </summary>
    [Test]
    public void E17_AMethodDeclaredOnTheBase_HandsOutTheWholeDerivedInstance()
    {
        const string program = """
            Class Zoo
                Public Count As Integer
                Public Last As Animal
                Public Sub Admit(a As Animal)
                    Count = Count + 1
                    Last = a
                End Sub
            End Class
            Class Animal
                Public Name As String = "animal"
                Public Sub Announce(z As Zoo)
                    z.Admit(Me)
                End Sub
            End Class
            Class Dog
                Inherits Animal
                Public Sub New()
                    Name = "dog"
                End Sub
            End Class
            Sub Main()
                Dim z As New Zoo()
                Dim d As New Dog()
                d.Announce(z)
                Console.WriteLine(z.Count & " " & z.Last.Name)
                Console.WriteLine(z.Last Is d)
            End Sub
            """;
        const string expected = "1 dog\nTrue";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    // ================================================================================
    // Generic, interface, lambda (C++ leg only — the other backends' known gaps on this
    // exact shape are separate, filed tasks, pinned below).
    // ================================================================================

    /// <summary>E06: a GENERIC class (<c>Box(Of T)</c>) implementing an interface passes <c>Me</c> to a sink.</summary>
    [Test]
    public void E06_AGenericClass_PassesMeToASink()
    {
        const string program = """
            Interface IItem
                Function Describe() As String
            End Interface
            Class Sink
                Public Count As Integer
                Public Last As IItem
                Public Sub Take(i As IItem)
                    Count = Count + 1
                    Last = i
                End Sub
            End Class
            Class Box(Of T)
                Implements IItem
                Public Value As T
                Public Function Describe() As String
                    Return "box"
                End Function
                Public Sub Give(k As Sink)
                    k.Take(Me)
                End Sub
            End Class
            Sub Main()
                Dim a As New Box(Of Integer)()
                a.Value = 7
                Dim k As New Sink()
                a.Give(k)
                a.Give(k)
                Console.WriteLine(k.Count & " " & k.Last.Describe())
            End Sub
            """;
        const string expected = "2 box";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// E07: an INTERFACE-implementing class passes <c>Me</c> to a sink, upcasts it with
    /// <c>TryCast(Me, IShape)</c> both as a return value and into a local, and tests the result
    /// with <c>IsNot Nothing</c>.
    /// </summary>
    [Test]
    public void E07_AnInterfaceImplementingClass_PassesMe_AndTryCastsMeToTheInterface()
    {
        const string program = """
            Interface IShape
                Function Area() As Integer
            End Interface
            Class Sink
                Public Total As Integer
                Public Sub Take(s As IShape)
                    Total = Total + s.Area()
                End Sub
            End Class
            Class Sq
                Implements IShape
                Public Side As Integer
                Public Function Area() As Integer
                    Return Side * Side
                End Function
                Public Sub Give(k As Sink)
                    k.Take(Me)
                End Sub
                Public Function AsShape() As IShape
                    Return TryCast(Me, IShape)
                End Function
                Public Function Probe() As Boolean
                    Dim s As IShape = TryCast(Me, IShape)
                    Return s IsNot Nothing
                End Function
            End Class
            Sub Main()
                Dim q As New Sq()
                q.Side = 3
                Dim k As New Sink()
                q.Give(k)
                Console.WriteLine(k.Total)
                Console.WriteLine(q.AsShape().Area())
                Console.WriteLine(q.Probe())
            End Sub
            """;
        const string expected = "9\n9\nTrue";
        Assert.That(Cpp(program), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(program), Is.EqualTo(expected), "C++ (-O)");
    }

    private const string E09a = """
        Class Sink
            Public Last As Counter
            Public Sub Take(c As Counter)
                Last = c
            End Sub
        End Class
        Class Counter
            Public N As Integer
            Public Sub Run(k As Sink)
                Dim f As Action = Sub() k.Take(Me)
                N = 5
                f()
            End Sub
        End Class
        Sub Main()
            Dim c As New Counter()
            Dim k As New Sink()
            c.Run(k)
            Console.WriteLine(k.Last.N)
            Console.WriteLine(k.Last Is c)
        End Sub
        """;

    private const string E09b = """
        Class Sink
            Public Last As Counter
            Public Sub Take(c As Counter)
                Last = c
            End Sub
        End Class
        Class Counter
            Public N As Integer = 2
            Public Sub New(k As Sink)
                Dim f As Action = Sub() k.Take(Me)
                f()
            End Sub
        End Class
        Sub Main()
            Dim k As New Sink()
            Dim c As New Counter(k)
            Console.WriteLine(k.Last.N)
            Console.WriteLine(k.Last Is c)
        End Sub
        """;

    /// <summary>
    /// E09a: a lambda declared in an ORDINARY method captures <c>Me</c> and calls it out through
    /// a captured object (<c>k.Take(Me)</c>), invoked after the lambda is created. Works on C++
    /// only because of D2's two-phase construction — <c>Me</c> is already the owning
    /// <c>shared_ptr</c> by the time the lambda captures it, no special case (ADR-0015's own
    /// "#140 inherits" contract).
    /// </summary>
    [Test]
    public void E09a_ALambdaInAnOrdinaryMethod_CapturingMe_Cpp()
    {
        const string expected = "5\nTrue";
        Assert.That(Cpp(E09a), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(E09a), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// E09b: the SAME shape, but the lambda is declared inside <c>Sub New</c> itself — the case
    /// D2 exists to make sound (<c>Me</c> must already be owned while the constructor body,
    /// which creates the lambda, is still running).
    /// </summary>
    [Test]
    public void E09b_ALambdaInsideSubNew_CapturingMe_Cpp()
    {
        const string expected = "2\nTrue";
        Assert.That(Cpp(E09b), Is.EqualTo(expected), "C++");
        Assert.That(CppAgg(E09b), Is.EqualTo(expected), "C++ (-O)");
    }

    /// <summary>
    /// PINNED (#237): both E09a and E09b are a PRE-EXISTING, UNRELATED C# backend gap —
    /// measured: the emitted lambda body renders EMPTY (<c>() => { }</c>), dropping
    /// <c>k.Take(Me)</c> entirely, so <c>k.Last</c> is never set and
    /// <c>Console.WriteLine(k.Last.N)</c> throws <see cref="NullReferenceException"/>. Nothing
    /// about #200 touches C# lambda lowering; a DIFFERENT failure here means #237 moved.
    /// </summary>
    [Test]
    public void E09_OnCSharp_ThrowsNullReferenceException_PinnedAgainst237()
    {
        Assert.Multiple(() =>
        {
            var exA = Assert.Throws<AssertionException>(() => FourBackends.RunEmittedCSharp(E09a));
            Assert.That(exA!.Message, Does.Contain("NullReferenceException"), "E09a C#:\n" + exA.Message);

            var exB = Assert.Throws<AssertionException>(() => FourBackends.RunEmittedCSharp(E09b));
            Assert.That(exB!.Message, Does.Contain("NullReferenceException"), "E09b C#:\n" + exB.Message);
        });
    }

    // ================================================================================
    // Both entry points (CLAUDE.md: "test through BOTH entry points" — a fix verified
    // only through the in-process helper can still break via the CLI or the IDE build).
    // ================================================================================

    /// <summary>
    /// M4 through the REAL CLI binary (<c>BasicLang.exe Prog.bas --target=cpp</c>) — the exact
    /// registration pattern D2 exists for, run through <c>BasicCompiler.CompileFile</c> (module
    /// registry, preprocessing, <c>CombineIRModules</c>) rather than the in-process
    /// single-unit helper.
    /// </summary>
    [Test]
    public void M4_ThroughTheRealCliBinary()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var dir = Path.Combine(Path.GetTempPath(), "bl-me200-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), """
                Class Registry
                    Public Count As Integer
                    Public Last As Node
                    Public Sub Add(n As Node)
                        Count = Count + 1
                        Last = n
                    End Sub
                End Class
                Class Node
                    Public V As Integer
                    Public Sub New(r As Registry, v0 As Integer)
                        V = v0
                        r.Add(Me)
                    End Sub
                End Class
                Sub Main()
                    Dim r As New Registry()
                    Dim a As New Node(r, 5)
                    Dim b As New Node(r, 6)
                    Console.WriteLine(r.Count)
                    Console.WriteLine(r.Last.V)
                    Console.WriteLine(r.Last Is b)
                End Sub
                """);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "Prog.bas", "--target=cpp" }, dir, timeoutMs: 120_000);
            Assert.That(exit, Is.EqualTo(0),
                $"CLI `BasicLang.exe Prog.bas --target=cpp` failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            var cppPath = Path.Combine(dir, "Prog.cpp");
            Assert.That(File.Exists(cppPath), Is.True, $"CLI reported success but wrote no Prog.cpp.\nSTDOUT:\n{stdout}");

            var run = CppCompile.CompileAndRun(File.ReadAllText(cppPath), compiler.Value);
            Assert.That(Norm(run), Is.EqualTo("2\n6\nTrue"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    /// <summary>
    /// E01 through the Release <c>.blproj</c> path the IDE uses
    /// (<c>CppProjectBuilder.Build</c> → <c>CompileProjectFiles</c>, CLAUDE.md's "IDE build
    /// delegates to the CLI engine"), proving the SAME construction-order divergence fix reaches
    /// a real project build, not only the CLI's single-file compile. A BasicLang native project
    /// ALWAYS builds with MSVC (<see cref="NativeBuildSkip.RequireMsvcForBasicLangNative"/>),
    /// so this SKIPS off Windows — measured here, must be re-run on a Windows box per CLAUDE.md.
    /// </summary>
    [Test]
    public void E01_ThroughTheReleaseBlprojPath_TheIdeUses()
    {
        NativeBuildSkip.RequireMsvcForBasicLangNative();

        var dir = Path.Combine(Path.GetTempPath(), "bl-me200-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "App.bas"), """
                Class Base
                    Public Sub New()
                        Console.WriteLine("Base.New")
                        Describe()
                    End Sub
                    Public Overridable Sub Describe()
                        Console.WriteLine("Base.Describe")
                    End Sub
                End Class
                Class Derived
                    Inherits Base
                    Public Tag As Integer = 42
                    Public Name As String = "d"
                    Public Sub New()
                        MyBase.New()
                        Console.WriteLine("Derived.New " & Tag)
                    End Sub
                    Public Overrides Sub Describe()
                        Console.WriteLine("Derived.Describe " & Tag & " [" & Name & "]")
                    End Sub
                End Class
                Sub Main()
                    Dim d As New Derived()
                    d.Describe()
                End Sub
                """);
            var projPath = Path.Combine(dir, "App.blproj");
            File.WriteAllText(projPath, """
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>Cpp</TargetBackend>
                  </PropertyGroup>
                </BasicLangProject>
                """);

            var project = ProjectFile.Load(projPath);
            var result = CppProjectBuilder.Build(project, "Release");
            Assert.That(result.Success, Is.True,
                "Release build failed:\n" + result.RawToolchainOutput + "\n" +
                string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

            var psi = new System.Diagnostics.ProcessStartInfo(result.ExecutablePath!)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            Assert.That(proc.WaitForExit(30_000), Is.True, "produced exe did not exit within 30s");
            Assert.That(proc.ExitCode, Is.EqualTo(0), $"exe exited {proc.ExitCode}");
            Assert.That(Norm(stdout), Is.EqualTo("Base.New\nDerived.Describe 0 []\nDerived.New 42\nDerived.Describe 42 [d]"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }
}
