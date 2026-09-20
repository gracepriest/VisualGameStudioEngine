using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// What a DERIVED class can see of its base: a field, a Const, a property, a Shared member, a
/// method — by bare name, through an instance, through <c>Me</c> and through <c>MyBase</c>.
///
/// <para>⛔ ESSENTIALLY NONE OF IT WORKED, on all four backends, and the language's own
/// inheritance was unusable because of it. Measured, compiled and run, before the change: a
/// derived method naming an inherited field, Protected field, Const, auto-Property,
/// Get/Set-Property or Shared field was REFUSED; so was reading or writing one through an
/// instance (<c>b.Total + 1</c> was "Arithmetic operator '+' requires numeric operands");
/// <c>Me.Field</c> was refused; <c>MyBase.Field</c> emitted a reference to an undeclared
/// <c>__base</c> on every backend; a three-level chain was refused; and an inherited member
/// sharing a name with a module global already diverged silently — C++ read the field, the other
/// three read the global.</para>
///
/// <para>⚠ Only METHODS appeared to work, and NOT by inheritance: pass 1 flattens every procedure
/// signature into the GLOBAL scope by bare name, so a bare inherited call found it there. That is
/// also why the failure had three faces by SPELLING — a one-character or lowercase name reached
/// "Undefined identifier", while a normal PascalCase name was swallowed by the deliberately
/// permissive "any PascalCase identifier could be a .NET type" arm into a phantom type, with no
/// diagnostic at all. Both name shapes are covered here; a fixture with only one pins only one
/// code path.</para>
///
/// <para>⚠ The fix is one walk in four places, plus what the backends needed to emit the result:
/// <c>TypeInfo.ResolveMember</c> walks the base chain (skipping a base's Private members,
/// re-resolving each base by name, depth-guarded); the analyzer calls it for a bare name, a
/// member access, a <c>With</c> member and the assign-to-constant guard; the C++ backend
/// registers inherited fields and properties as real names so a COMPUTED write is not decayed to
/// a temp and dropped; MSIL's instance field and property tables walk the chain and every
/// <c>ldfld</c>/<c>stfld</c>/<c>callvirt</c> names the DECLARING class; and <c>MyBase</c> lowers
/// to the object itself carrying the base type.</para>
///
/// <para>⛔ A PRE-EXISTING MSIL DEFECT, found by reading the emitted IL and fixed here because the
/// walk reaches it: a MODULE-level function was never given a fresh class-member context, so it
/// kept the last emitted class's field tables. A module function naming something that class also
/// declares took the bare-FIELD path and emitted <c>ldarg.0</c> in a STATIC method — not a program
/// the CLR will load. It needs only a name collision, no inheritance at all
/// (<see cref="AModuleGlobalAndAClassFieldOfOneName_StayDistinct_WithoutInheritance"/>).</para>
///
/// <para>⚠ Every gap left below is PINNED WITH A CONTROL proving it is not inheritance's: the same
/// shape written against the class's OWN member fails identically. An inherited member is never
/// worse off than an own one.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class InheritedMemberTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);
    private static string Cpp(string program) => Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)));
    private static string Js(string program) => Norm(JavaScriptExecutionTests.RunJs(program));
    private static string Msil(string program) => Norm(MsilHarness.RunExpectingSuccess(program));
    private static string Cs(string program) => Norm(FourBackends.RunEmittedCSharp(program));

    /// <summary>Base + derived + a Main that prints <c>b.Run()</c>. The derived class always exposes Run().</summary>
    private static string Prog(string baseBody, string derivedBody, bool baseFirst = true, string main = null) =>
        (baseFirst
            ? "Class Base\n" + baseBody + "End Class\nClass Box\n Inherits Base\n" + derivedBody + "End Class\n"
            : "Class Box\n Inherits Base\n" + derivedBody + "End Class\nClass Base\n" + baseBody + "End Class\n")
        + (main ?? "Sub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n");

    /// <summary>Runs the front end and returns its diagnostics, whether or not it accepted.</summary>
    private static string Diagnostics(string program)
    {
        var parser = new Parser(new Lexer(program).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join(" | ", analyzer.Errors.Select(e => e.Message));
    }

    /// <summary>The analysis errors for a program the front end must REFUSE.</summary>
    private static string Refusal(string program)
    {
        var parser = new Parser(new Lexer(program).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        Assert.That(ok, Is.False, "the program must be refused, but analysis succeeded");
        return string.Join(" | ", analyzer.Errors.Select(e => e.Message));
    }

    // ------------------------------------------------------------------ a bare inherited field

    /// <summary>
    /// ⛔ The two NAME FACES, which take different paths on a miss. A multi-character PascalCase
    /// name was swallowed into a phantom type with no diagnostic; a one-character name reached
    /// "Undefined identifier". A fixture with one and not the other pins half the defect.
    /// </summary>
    [TestCase("Total", TestName = "{m}(multi-character PascalCase)")]
    [TestCase("V", TestName = "{m}(one character)")]
    [TestCase("total", TestName = "{m}(lowercase)")]
    public void ABareInheritedField_IsRead_OnEveryBackend(string field)
        => RunsOnEveryBackend(Prog($" Public {field} As Integer = 7\n",
            $" Public Function Run() As Integer\n  Return {field}\n End Function\n"), "7");

    /// <summary>An untyped context: no downstream type check runs here, so nothing would report a miss.</summary>
    [Test]
    public void ABareInheritedField_IsRead_InAnUntypedContext_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 7\n",
            " Public Sub Show()\n  PrintLine(CStr(Total))\n End Sub\n",
            true, "Sub Main()\n Dim b As New Box()\n b.Show()\nEnd Sub\n"), "7");

    [Test]
    public void ABareInheritedProtectedField_IsRead_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Protected Total As Integer = 7\n",
            " Public Function Run() As Integer\n  Return Total\n End Function\n"), "7");

    /// <summary>A simple right-hand side: the destination is an IRVariable and never decays.</summary>
    [Test]
    public void ABareInheritedField_IsWritten_WithASimpleValue_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 1\n",
            " Public Sub Bump()\n  Total = 5\n End Sub\n",
            true, "Sub Main()\n Dim b As New Box()\n b.Bump()\n PrintLine(CStr(b.Total))\nEnd Sub\n"), "5");

    /// <summary>
    /// ⛔ The COMPUTED right-hand side, read back from OUTSIDE the class. This is the shape the
    /// C++ registration exists for: a computed value is renamed after its destination, and a
    /// destination the emitter does not recognise as a real name decays to a temporary — the
    /// program compiles clean and THROWS THE WRITE AWAY. Reading back through the same bare name
    /// would agree with itself whichever location it picked.
    /// </summary>
    [Test]
    public void ABareInheritedField_IsWritten_WithAComputedValue_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 1\n",
            " Public Sub Bump()\n  Total = Total + 10\n End Sub\n",
            true, "Sub Main()\n Dim b As New Box()\n b.Bump()\n PrintLine(CStr(b.Total))\nEnd Sub\n"), "11");

    // ------------------------------------------------------------------ the other member kinds

    [Test]
    public void ABareInheritedConst_IsRead_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Const Limit As Integer = 9\n",
            " Public Function Run() As Integer\n  Return Limit\n End Function\n"), "9");

    [Test]
    public void ABareInheritedAutoProperty_IsReadAndWritten_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Property P As Integer\n",
            " Public Function Run() As Integer\n  P = 8\n  Return P\n End Function\n"), "8");

    [Test]
    public void ABareInheritedSharedField_IsRead_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Shared Count As Integer = 6\n",
            " Public Function Run() As Integer\n  Return Count\n End Function\n"), "6");

    [Test]
    public void ABareInheritedSharedMethod_IsCalled_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Shared Function Helper() As Integer\n  Return 3\n End Function\n",
            " Public Function Run() As Integer\n  Return Helper()\n End Function\n"), "3");

    /// <summary>
    /// ⛔ A bare inherited METHOD resolved before this change — by pass 1's global flattening, not
    /// by inheritance — but MSIL then emitted the call against the MODULE class:
    /// <c>call int32 MsilProbe::Helper()</c>, MissingMethodException from a green build. The
    /// receiver token now names the class that DECLARES the method.
    /// </summary>
    [Test]
    public void ABareInheritedMethod_IsCalled_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Function Helper() As Integer\n  Return 3\n End Function\n",
            " Public Function Run() As Integer\n  Return Helper()\n End Function\n"), "3");

    /// <summary>The statement form takes a different emission path from the expression form.</summary>
    [Test]
    public void ABareInheritedSub_IsCalled_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Sub Say()\n  PrintLine(\"hi\")\n End Sub\n",
            " Public Sub Run2()\n  Say()\n End Sub\n",
            true, "Sub Main()\n Dim b As New Box()\n b.Run2()\nEnd Sub\n"), "hi");

    // ------------------------------------------------------------------ the receiver forms

    /// <summary>
    /// ⛔ In an ARITHMETIC context, so the type is actually checked. The member access typed every
    /// inherited member Object without a diagnostic, and "Arithmetic operator '+' requires numeric
    /// operands" is what the user saw. A print-only assertion would not hold this: C# and
    /// JavaScript re-resolve the emitted text themselves.
    /// </summary>
    [Test]
    public void AnInheritedField_IsReadThroughAnInstance_InATypedContext_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 7\n",
            " Public Function Run() As Integer\n  Return 0\n End Function\n",
            true, "Sub Main()\n Dim b As New Box()\n Dim n As Integer = b.Total + 1\n PrintLine(CStr(n))\nEnd Sub\n"), "8");

    [Test]
    public void AnInheritedField_IsWrittenThroughAnInstance_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 1\n",
            " Public Function Run() As Integer\n  Return Total\n End Function\n",
            true, "Sub Main()\n Dim b As New Box()\n b.Total = 7\n PrintLine(CStr(b.Run()))\nEnd Sub\n"), "7");

    [Test]
    public void AnInheritedField_IsReadThroughMe_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 7\n",
            " Public Function Run() As Integer\n  Return Me.Total\n End Function\n"), "7");

    /// <summary>
    /// ⛔ <c>MyBase.Field</c> lowered to a variable literally named <c>__base</c>, which nothing
    /// declares: "use of undeclared identifier" on C++, "__base is not defined" on JavaScript,
    /// CS0103 on C#, InvalidProgramException on MSIL. Broken on all four, measured. Only
    /// <c>MyBase.Method()</c> escaped it, through the arm that intercepts the call before the
    /// receiver is ever visited — which is why the method twin below is the control for it.
    /// </summary>
    [Test]
    public void AnInheritedField_IsReadThroughMyBase_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Total As Integer = 7\n",
            " Public Function Run() As Integer\n  Return MyBase.Total\n End Function\n"), "7");

    [Test]
    public void AnInheritedMethod_IsCalledThroughMyBase_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Function Helper() As Integer\n  Return 3\n End Function\n",
            " Public Function Run() As Integer\n  Return MyBase.Helper()\n End Function\n"), "3");

    // ------------------------------------------------------------------ structural

    /// <summary>The walk is a loop, not one hop.</summary>
    [Test]
    public void AThreeLevelChain_ReadsTheTopmostField_OnEveryBackend()
        => RunsOnEveryBackend("""
            Class A
             Public Total As Integer = 7
            End Class
            Class B
             Inherits A
            End Class
            Class Box
             Inherits B
             Public Function Run() As Integer
              Return Total
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """, "7");

    /// <summary>
    /// ⚠ The walk starts at the class ITSELF, not at its base, and this is what that buys: an OWN
    /// member declared BELOW the method that names it. Pass 2 defines members into the class scope
    /// in source order while analyzing each body inline, so the later declaration does not exist
    /// yet; the type's member table is complete from pass 1 whatever the order.
    /// </summary>
    [Test]
    public void AnOwnMemberDeclaredBelowTheMethodThatNamesIt_IsRead_OnEveryBackend()
        => RunsOnEveryBackend("""
            Class Box
             Public Function Run() As Integer
              Return Total
             End Function
             Public Total As Integer = 7
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """, "7");

    /// <summary>
    /// ⛔ CLASS SCOPE IS NEARER THAN MODULE SCOPE, and both halves of the compiler have to agree
    /// about it. Before the change the four backends disagreed: C++ read the inherited field (7)
    /// and JavaScript, MSIL and C# read the module global (99), from one program. Read back
    /// through BOTH names, so a wrong location cannot agree with itself.
    /// </summary>
    [Test]
    public void AnInheritedFieldShadowingAModuleGlobal_WinsInsideTheClass_OnEveryBackend()
        => RunsOnEveryBackend("Module G\n Public Total As Integer = 99\nEnd Module\n" +
            Prog(" Public Total As Integer = 7\n",
                " Public Function Run() As Integer\n  Return Total\n End Function\n",
                true, "Sub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\n PrintLine(CStr(G.Total))\nEnd Sub\n"),
            "7\n99");

    /// <summary>
    /// ⛔ THE CONTROL, and a PRE-EXISTING MSIL DEFECT this change fixes: no inheritance at all,
    /// just a class field and a module global of one name. A module-level function was never given
    /// a fresh class-member context, so it kept the last emitted class's field tables and emitted
    /// <c>ldarg.0</c> — in a STATIC method — for the module global: InvalidProgramException, from a
    /// build that reported success. Found by reading the emitted IL.
    /// </summary>
    [Test]
    public void AModuleGlobalAndAClassFieldOfOneName_StayDistinct_WithoutInheritance()
        => RunsOnEveryBackend("""
            Module G
             Public Total As Integer = 99
            End Module
            Class Box
             Public Total As Integer = 7
             Public Function Run() As Integer
              Return Total
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
             PrintLine(CStr(G.Total))
            End Sub
            """, "7\n99");

    // ------------------------------------------------------------------ what must STAY refused

    /// <summary>
    /// ⚠ PRIVATE IS PRIVATE TO THE DECLARING CLASS. The walk skips a base's Private members, and
    /// it must: pass 1 filters them out of the member table while pass 2 does not, so without the
    /// explicit skip whether one resolved would depend on declaration order — and admitting one
    /// turns a front-end acceptance into a CS0122 or a clang private-access failure in the
    /// emitted code, which is a worse error further from the user.
    /// </summary>
    [Test]
    public void ABaseClassesPrivateField_IsNotVisibleToTheDerivedClass()
        => Assert.That(Refusal(Prog(" Private Total As Integer = 7\n",
            " Public Function Run() As Integer\n  Return Total\n End Function\n")),
            Does.Contain("Total"));

    /// <summary>The write half of the walk: without it an inherited Const is silently writable.</summary>
    [Test]
    public void AnInheritedConst_CannotBeAssigned()
        => Assert.That(Refusal(Prog(" Public Const Limit As Integer = 9\n",
            " Public Function Run() As Integer\n  Limit = 3\n  Return Limit\n End Function\n")),
            Does.Contain("Cannot assign to constant 'Limit'"));

    /// <summary>
    /// ⛔ THE WALK MUST TERMINATE ON A CYCLE. A base is a NAME in the source and nothing validates
    /// that the chain is acyclic, so <c>A Inherits B</c> against <c>B Inherits A</c> would spin in
    /// the walker forever. Failing to resolve a member is a diagnostic; HANGING THE COMPILER is
    /// not, and a hang is the one failure a test suite cannot report on its own — this one runs
    /// the analysis on a worker and fails if it does not come back.
    /// </summary>
    [Test]
    public void ABaseClassCycle_Terminates_RatherThanHangingTheCompiler()
    {
        // ⚠ THE LOOKUP HAS TO HAPPEN AFTER THE CYCLE CLOSES. Pass 2 binds each class's BaseType
        // as it visits the class, so inside A's own body B.BaseType is still unset and the walk
        // ends after one step — a miss there does not exercise the guard at all. Main is visited
        // last, when A→B→A is a real ring.
        const string program = """
            Class A
             Inherits B
             Public Function Run() As Integer
              Return 1
             End Function
            End Class
            Class B
             Inherits A
            End Class
            Sub Main()
             Dim a As New A()
             PrintLine(CStr(a.Missing))
            End Sub
            """;

        var analysis = System.Threading.Tasks.Task.Run(() => Diagnostics(program));
        Assert.That(analysis.Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the base-chain walk did not terminate on a cycle — the depth guard is gone");
        Assert.That(analysis.Result, Does.Contain("Missing"),
            "the walk came back but reported nothing about the member it could not find");
    }

    /// <summary>
    /// ⚠ THE DEPTH-ZERO HALF of the Private rule, which no other shape can observe: a BARE name
    /// finds a class's own Private member lexically, without the walker ever running. Only a
    /// qualified <c>Me.Secret</c> goes through the walk at depth 0 — and rejecting it there does
    /// not refuse the program, because the .NET arm below the lookup claims any PascalCase
    /// receiver and types the read Object. The failure surfaces one step later, as an arithmetic
    /// operand error, which is exactly why this shape has to be COMPILED AND RUN to be pinned.
    /// </summary>
    [Test]
    public void AClassesOwnPrivateField_ResolvesThroughMe_OnEveryBackend()
        => RunsOnEveryBackend(Prog(" Public Other As Integer = 1\n",
            " Private Secret As Integer = 5\n"
            + " Public Function Run() As Integer\n  Return Me.Secret + 1\n End Function\n"),
            "6");

    /// <summary>
    /// ⚠ The <c>With</c> site's walk, which nothing else can observe: a <c>With</c> block over a
    /// class instance is unimplemented on every backend (pinned below), so the member cannot be
    /// RUN. What is observable is that the FRONT END now resolves it — a flat lookup there
    /// refuses the program outright with "does not have a member".
    /// </summary>
    [Test]
    public void AWithBlockNamingAnInheritedMember_ResolvesInTheFrontEnd()
        => Assert.That(Diagnostics(Prog(" Public Total As Integer = 7\n",
                " Public Function Run() As Integer\n  Return 0\n End Function\n",
                true, "Sub Main()\n Dim b As New Box()\n With b\n  PrintLine(CStr(.Total))\n End With\nEnd Sub\n")),
            Does.Not.Contain("does not have a member"),
            "the With site walks the base chain; the backends still cannot emit a With block");

    // ------------------------------------------------------------------ pinned, each with a control

    /// <summary>
    /// ⛔ PINNED, NOT INHERITANCE'S: C++ and JavaScript emit classes in DECLARATION ORDER, so a
    /// base declared BELOW its derived class is an incomplete type / a temporal-dead-zone
    /// reference. The control proves it: the same order with NO member access at all fails
    /// identically. MSIL and C# run both orders. If C++ and JavaScript start ordering their
    /// classes, promote both to four-backend runs.
    /// </summary>
    [Test]
    public void ABaseDeclaredBelowItsDerivedClass_IsAnEmissionOrderGap_Pinned()
    {
        var withAccess = Prog(" Public Total As Integer = 7\n",
            " Public Function Run() As Integer\n  Return Total\n End Function\n", baseFirst: false);
        const string control = """
            Class Box
             Inherits Base
             Public Function Run() As Integer
              Return 3
             End Function
            End Class
            Class Base
             Public V As Integer
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Msil(withAccess), Is.EqualTo("7"), "MSIL runs the inherited read in either order");
            Assert.That(Cs(withAccess), Is.EqualTo("7"), "C# runs the inherited read in either order");
            Assert.That(() => Cpp(control), Throws.Exception,
                "CONTROL: C++ refuses a base declared below its derived class with NO member access");
            Assert.That(() => Js(control), Throws.Exception,
                "CONTROL: JavaScript refuses the same");
        });
    }

    /// <summary>
    /// ⛔ PINNED, NOT INHERITANCE'S: C++ cannot read a Get/Set PROPERTY by bare name inside a
    /// method — an AUTO-property works on all four. The control is the same property declared on
    /// the class itself.
    /// </summary>
    [Test]
    public void AGetSetPropertyByBareName_IsACppGap_Pinned()
    {
        const string body = " Private _p As Integer\n Public Property P As Integer\n  Get\n   Return _p\n  End Get\n  Set(value As Integer)\n   _p = value\n  End Set\n End Property\n";
        var inherited = Prog(body, " Public Function Run() As Integer\n  P = 8\n  Return P\n End Function\n");
        var own = "Class Box\n" + body + " Public Function Run() As Integer\n  P = 8\n  Return P\n End Function\nEnd Class\n" +
                  "Sub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n";
        Assert.Multiple(() =>
        {
            Assert.That(Js(inherited), Is.EqualTo("8"), "JavaScript");
            Assert.That(Msil(inherited), Is.EqualTo("8"), "MSIL");
            Assert.That(Cs(inherited), Is.EqualTo("8"), "C#");
            Assert.That(() => Cpp(own), Throws.Exception,
                "CONTROL: C++ cannot read the class's OWN Get/Set property by bare name either");
        });
    }

    /// <summary>
    /// ⛔ PINNED, NOT INHERITANCE'S: a <c>With</c> block over a class instance is unimplemented on
    /// every backend. The control is the class's own member; a <c>With</c> over a MODULE member
    /// runs on all four, which is why the feature looks implemented.
    /// </summary>
    [Test]
    public void AWithBlockOverAClassInstance_IsUnimplemented_OnEveryBackend_Pinned()
    {
        const string ownControl = """
            Class Box
             Public Total As Integer = 7
            End Class
            Sub Main()
             Dim b As New Box()
             With b
              PrintLine(CStr(.Total))
             End With
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(() => Cpp(ownControl), Throws.Exception, "CONTROL: C++ on the class's OWN member");
            Assert.That(() => Js(ownControl), Throws.Exception, "CONTROL: JavaScript on the class's OWN member");
            RunsOnEveryBackend("""
                Module M
                 Public Total As Integer = 7
                End Module
                Sub Main()
                 PrintLine(CStr(M.Total))
                End Sub
                """, "7");
        });
    }

    /// <summary>
    /// ⛔ PINNED, NOT INHERITANCE'S: a bare name spelled with different CASE from the declaration
    /// reaches the backends verbatim — the bare-name path is not canonicalised to the declared
    /// spelling. The control is the class's own field. On JavaScript it is a SILENT
    /// <c>undefined</c>, which is why this is recorded rather than left to be rediscovered.
    /// </summary>
    [Test]
    public void ACaseDifferentBareSpelling_IsACanonicalisationGap_Pinned()
    {
        const string own = """
            Class Box
             Public Total As Integer = 7
             Public Function Run() As Integer
              Return total
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(() => Cpp(own), Throws.Exception, "CONTROL: C++ on the class's OWN field");
            Assert.That(Js(own), Is.Not.EqualTo("7"), "CONTROL: JavaScript reads the wrong property, silently");
        });
    }

    /// <summary>
    /// ⛔ PINNED, NOT INHERITANCE'S: JavaScript DROPS a field write whose right-hand side is a
    /// call — the field keeps its old value, from a build that reported success. The control is
    /// the class's own field with its own method, and with a free function, both of which print 1
    /// instead of 10 on JavaScript while the other three print 10.
    /// </summary>
    [Test]
    public void AFieldWriteFromACallResult_IsAJavaScriptGap_Pinned()
    {
        const string own = """
            Class Box
             Public Total As Integer = 1
             Public Function Ten() As Integer
              Return 10
             End Function
             Public Sub Bump()
              Total = Ten()
             End Sub
            End Class
            Sub Main()
             Dim b As New Box()
             b.Bump()
             PrintLine(CStr(b.Total))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(own), Is.EqualTo("10"), "C++");
            Assert.That(Msil(own), Is.EqualTo("10"), "MSIL");
            Assert.That(Cs(own), Is.EqualTo("10"), "C#");
            Assert.That(Js(own), Is.EqualTo("1"),
                "PINNED CONTROL: JavaScript drops a field write whose RHS is a call, with no inheritance involved");
        });
    }

    /// <summary>
    /// ⛔ PINNED, pre-existing and untouched: a class declared in ANOTHER FILE cannot be a base —
    /// "Unknown base class". The whole cross-file inheritance surface is unreachable, so none of
    /// the shapes above could be measured across files.
    /// </summary>
    [Test]
    public void ACrossFileBaseClass_IsNotFound_Pinned()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "BasicLang_Inherited_" + Path.GetRandomFileName())).FullName;
        try
        {
            var basePath = Path.Combine(dir, "Base.bas");
            var mainPath = Path.Combine(dir, "Main.bas");
            File.WriteAllText(basePath, "Class Base\n Public Total As Integer = 7\nEnd Class\n");
            File.WriteAllText(mainPath, "Import Base\nClass Box\n Inherits Base\nEnd Class\n" +
                "Sub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Total))\nEnd Sub\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { basePath, mainPath });
            Assert.That(string.Join(" | ", result.AllErrors.Select(e => e.Message)),
                Does.Contain("Unknown base class 'Base'"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
