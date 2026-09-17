using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A CONSTRUCTOR called with a trailing <c>Optional</c> parameter left out — the shape
/// <see cref="OptionalParameterTests"/> had to pin as refused, because filling at the call site
/// could not reach it.
///
/// <para>⛔ It never reached the IR builder at all. Constructors are keyed by ARITY in
/// <c>TypeInfo.Members</c> — <c>.ctor1</c>, <c>.ctor2</c> — and all three construction sites looked
/// up an EXACT key, so the analyzer refused the program outright. Measured before, on all three:</para>
///
/// <list type="bullet">
/// <item><c>New Box(4)</c> against <c>Sub New(a As Integer, Optional b As Integer = 5)</c> —
/// "No constructor for 'Box' takes 1 argument(s). Available constructors take: 2 argument(s)".</item>
/// <item><c>New Box()</c> against an all-Optional constructor — "…takes 0 argument(s). Available
/// constructors take: 1 argument(s)".</item>
/// <item><c>MyBase.New(7)</c> against an Optional base constructor — "No constructor for base class
/// 'Base' takes 1 argument(s)".</item>
/// </list>
///
/// <para>⚠ One rule (<c>SemanticAnalyzer.ResolveConstructor</c>) serves all three, because three
/// copies disagreeing about which constructor a call binds to is the bug class the arity key
/// already produced. An EXACT arity always wins, so nothing that resolved before resolves
/// differently; only when no exact key exists does it look for the unique longer constructor whose
/// extra trailing parameters are all Optional.</para>
///
/// <para>⚠ The bound constructor is then RECORDED (<c>ConstructorBindings</c>) and the IR builder
/// reads it, replacing <c>UnambiguousConstructorParameters</c>, which re-derived the parameter list
/// from the IR class. That is not a tidy-up: the two could disagree, and
/// <see cref="AClassDeclaredAfterItsUse_IsStillNotValidated"/> records the order gap that is the
/// reason they could.</para>
/// </summary>
[TestFixture]
public class OptionalConstructorTests
{
    private const string NewProgram = """
        Class Box
         Public Sub New(a As Integer, Optional b As Integer = 5)
          PrintLine("ctor:" & CStr(a) & "," & CStr(b))
         End Sub
        End Class

        Module M
         Sub Main()
          Dim p As New Box(4)
          Dim q As New Box(4, 9)
         End Sub
        End Module
        """;

    private const string NewExpected = "ctor:4,5\nctor:4,9";

    /// <summary>
    /// ⚠ C# is NOT already right here, unlike the module-Sub case. The C# backend emits a
    /// constructor signature with no default at all (<c>public Box(int a, int b)</c>), so
    /// <c>new Box(4)</c> is CS1501 — every backend needed the fill.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_Compiles_WithAnOmittedConstructorOptional()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(NewProgram);

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(NewProgram), Is.Empty);
            Assert.That(csharp, Does.Contain("new Box(4, 5)"),
                "the default is produced by the compiler; the emitted signature carries none:\n"
                + csharp);
        });
    }

    [Test]
    [Category("Integration")]
    public void JavaScript_FillsAnOmittedConstructorOptional()
    {
        Assert.That(JavaScriptExecutionTests.RunJs(NewProgram), Is.EqualTo(NewExpected));
    }

    [Test]
    [Category("Integration")]
    public void Msil_FillsAnOmittedConstructorOptional()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess(NewProgram), Is.EqualTo(NewExpected + "\n"));
    }

    [Test]
    [Category("Integration")]
    public void Cpp_FillsAnOmittedConstructorOptional()
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(NewProgram)),
            Is.EqualTo(NewExpected + "\n"));
    }

    /// <summary>
    /// ⚠ The degenerate case, and its own key: <c>New Box()</c> asks for <c>.ctor0</c>, which an
    /// all-Optional constructor never creates. A class whose only constructor can be called with no
    /// arguments could not be constructed with no arguments.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnAllOptionalConstructor_TakesNoArgumentsAtAll()
    {
        const string program = """
            Class Box
             Public Sub New(Optional a As Integer = 1)
              PrintLine("ctor:" & CStr(a))
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New Box()
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("ctor:1"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("ctor:1\n"));
        });
    }

    /// <summary>
    /// ⚠ The THIRD construction site: <c>MyBase.New(…)</c>, which the IR builder collects into
    /// <c>IRConstructor.BaseConstructorArgs</c> rather than an <c>IRNewObject</c>, and which had
    /// its own exact-arity lookup and its own error message.
    ///
    /// <para>⚠ MSIL is NOT asserted — see
    /// <see cref="Msil_IgnoresBaseConstructorArgumentsEntirely_PreExisting"/>.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AMyBaseNewCall_FillsAnOmittedOptional()
    {
        const string program = """
            Class Base
             Public Sub New(a As Integer, Optional b As Integer = 5)
              PrintLine("base:" & CStr(a) & "," & CStr(b))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              MyBase.New(7)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(ReturnCoercionTests.EmitCSharpForTest(program), Does.Contain("base(7, 5)"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("base:7,5"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo("base:7,5\n"));
        });
    }

    // ====================================================================================
    // What must NOT change.
    // ====================================================================================

    /// <summary>
    /// ⛔ An EXACT arity still wins. Without that precedence <c>New Box(1)</c> could bind the
    /// three-parameter constructor by filling two defaults, silently calling a different
    /// constructor than it called before this change.
    ///
    /// <para>⚠ MSIL, not JavaScript: a class with TWO constructors cannot be lowered to JS at all
    /// ("SyntaxError: A class may only have one constructor"). Measured as pre-existing with a
    /// pair that has no Optional parameter anywhere — same failure — so it is the overload itself
    /// JS cannot express, not anything this change does.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnExactArityStillWins_OverAFillableLongerConstructor()
    {
        const string program = """
            Class Box
             Public Sub New(a As Integer)
              PrintLine("one:" & CStr(a))
             End Sub
             Public Sub New(a As Integer, b As Integer, Optional c As Integer = 9)
              PrintLine("three:" & CStr(a) & "," & CStr(b) & "," & CStr(c))
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New Box(1)
              Dim q As New Box(1, 2)
             End Sub
            End Module
            """;

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program),
            Is.EqualTo("one:1\nthree:1,2,9\n"),
            "one:1 means the exact .ctor1 won; three:1,2,9 means .ctor3 filled its one Optional");
    }

    /// <summary>
    /// ⚠ TWO fillable candidates answer null and leave the existing diagnostic in place, rather
    /// than picking one. Picking silently is worse than the error that exists today.
    /// </summary>
    [Test]
    public void AnAmbiguousFill_IsStillRefused()
    {
        var errors = Analyze("""
            Class Box
             Public Sub New(Optional a As Integer = 1)
              PrintLine("one")
             End Sub
             Public Sub New(Optional a As Integer = 1, Optional b As Integer = 2)
              PrintLine("two")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New Box()
             End Sub
            End Module
            """);

        Assert.That(errors, Has.Some.Contains(
            "No constructor for 'Box' takes 0 argument(s). Available constructors take: "
            + "1 argument(s), 2 argument(s)"),
            "actual: " + string.Join(" | ", errors));
    }

    /// <summary>
    /// ⚠ A REQUIRED parameter is never filled — only <c>Optional</c> trailing parameters are, so a
    /// genuinely wrong arity keeps its error.
    /// </summary>
    [Test]
    public void AMissingRequiredArgument_IsStillRefused()
    {
        var errors = Analyze("""
            Class Box
             Public Sub New(a As Integer)
              PrintLine("one")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New Box()
             End Sub
            End Module
            """);

        Assert.That(errors, Has.Some.Contains("No constructor for 'Box' takes 0 argument(s)"),
            "actual: " + string.Join(" | ", errors));
    }

    /// <summary>
    /// ⚠ The constructor ARGUMENT COERCION must survive moving its parameter source from the IR
    /// class to the analyzer's binding — <c>New Box(7 / 2)</c> is 3, not 3.5 and not CS1503.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AConstructorArgumentIsStillCoerced()
    {
        const string program = """
            Class Box
             Public Sub New(n As Integer)
              PrintLine("ctor:" & CStr(n))
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New Box(7 / 2)
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("ctor:3"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("ctor:3\n"));
        });
    }

    // ====================================================================================
    // Two gaps this does NOT close, pinned so they surface rather than drift.
    // ====================================================================================

    /// <summary>
    /// ⛔ <b>A class declared AFTER the code that uses it gets NO constructor checking at all</b>,
    /// and this change does not fix it. <c>RegisterDeclarations</c> (pass 1) recurses into a
    /// <c>ClassNode</c>'s members but has no case for a <c>ConstructorNode</c>, and the class's
    /// <c>TypeInfo</c> does not exist until pass 2's <c>Visit(ClassNode)</c> — so when
    /// <c>New Box(…)</c> is analyzed there is no <c>.ctor</c> key to find, the arity check is
    /// skipped (<c>hasAnyConstructor</c> is false, so not even an error), and nothing is recorded
    /// for the IR builder to coerce or fill against.
    ///
    /// <para>⛔ It is PRE-EXISTING and it is a live miscompile, not a theoretical one: measured on
    /// the previous commit, <c>New Box(7 / 2)</c> in this order emitted
    /// <c>new Box((double)(7) / (double)(2))</c> — <b>CS1503, does not build</b> — while the same
    /// program with the class declared first emitted the cast and ran. The argument coercion has
    /// been half-working since it shipped, and nothing noticed because every test and sample in the
    /// repo declares classes first.</para>
    ///
    /// <para>⚠ Closing it means pre-registering constructors in pass 1, which means creating class
    /// <c>TypeInfo</c>s in pass 1 — and <c>TypeManager.DefineType</c> returns NULL for a name it
    /// already holds, so duplicate-class detection and <c>Visit(ClassNode)</c> both have to change
    /// with it. That is a restructuring of type registration, deliberately not bundled here.</para>
    ///
    /// <para>This pins BOTH halves of the gap so the day someone does that work, this test goes red
    /// and both are picked up together.</para>
    /// </summary>
    [Test]
    public void AClassDeclaredAfterItsUse_IsStillNotValidated()
    {
        const string coercion = """
            Module M
             Sub Main()
              Dim p As New Box(7 / 2)
             End Sub
            End Module

            Class Box
             Public Sub New(n As Integer)
              PrintLine("ctor:" & CStr(n))
             End Sub
            End Class
            """;

        const string fill = """
            Module M
             Sub Main()
              Dim p As New Box(4)
             End Sub
            End Module

            Class Box
             Public Sub New(a As Integer, Optional b As Integer = 5)
              PrintLine("ctor:" & CStr(a) & "," & CStr(b))
             End Sub
            End Class
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.EmitCSharpForTest(coercion),
                Does.Contain("new Box((double)(7) / (double)(2))"),
                "when this becomes new Box((int)(...)), pass 1 has learned constructors — "
                + "delete this pin and assert the real behaviour");
            Assert.That(ReturnCoercionTests.EmitCSharpForTest(fill), Does.Contain("new Box(4)"),
                "and when this becomes new Box(4, 5), the fill reaches this order too");
        });
    }

    /// <summary>
    /// ⛔ MSIL ignores <c>BaseConstructorArgs</c> ENTIRELY — it emits
    /// <c>call instance void Base::.ctor()</c> whatever the arguments, and the program dies with
    /// <c>MissingMethodException: Void Base..ctor()</c>.
    ///
    /// <para>⚠ Proved independent of Optional parameters by supplying EVERY argument: the same
    /// failure, with a base constructor that has no Optional parameter at all. So it is a
    /// pre-existing MSIL gap and not something this change introduced or could fix — which is why
    /// <see cref="AMyBaseNewCall_FillsAnOmittedOptional"/> asserts the other three backends and not
    /// this one.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void Msil_IgnoresBaseConstructorArgumentsEntirely_PreExisting()
    {
        var run = Msil.MsilHarness.Run("""
            Class Base
             Public Sub New(a As Integer, b As Integer)
              PrintLine("base:" & CStr(a) & "," & CStr(b))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              MyBase.New(7, 9)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """);

        Assert.That(run.Outcome, Is.EqualTo(Msil.MsilHarness.MsilOutcome.RunFailed),
            "if this passes, MSIL has learned base constructor arguments — assert the real "
            + "behaviour here and add MSIL back to AMyBaseNewCall_FillsAnOmittedOptional");
        Assert.That(run.Output, Does.Contain("Void Base..ctor()"));
    }

    // ====================================================================================
    // Helpers.
    // ====================================================================================

    /// <summary>Parse + analyze only, returning the analyzer's errors as text.</summary>
    private static string[] Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new BasicLang.Compiler.SemanticAnalysis.SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.ToString()).ToArray();
    }
}
