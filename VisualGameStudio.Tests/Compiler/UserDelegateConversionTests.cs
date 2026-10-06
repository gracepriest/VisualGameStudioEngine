using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #187 (fix commit <c>37faed14</c>) — front-end tests, fast subset. A lambda or
/// <c>AddressOf</c> now target-types to a user <c>Delegate Sub</c>/<c>Delegate Function</c> at
/// every site Func/Action already converts at (<c>SemanticAnalyzer.DelegateShapeOf</c> /
/// <c>ConvertToUserDelegate</c>), and invoking a user <c>Delegate Function</c> is typed its own
/// return type instead of Void. Data-driven over the CONVERSION SITES per the implementer's
/// measured matrix (<c>S/t187/matrix-after.txt</c>): Dim, field initializer, assignment, property
/// set, Return, user-procedure argument, class-method argument, constructor argument,
/// <c>MyBase.New</c> argument, delegate-invocation argument. Exact refusal messages (R1-R12) are
/// taken from the implementer's <c>S/t187/probes/*.exp</c> and build logs, never re-derived.
///
/// <para>Execution (all four backends, both pipelines) is
/// <see cref="UserDelegateConversionExecutionTests"/> — this fixture never runs g++/node/ilasm
/// and stays out of <c>[Category("Integration")]</c>, including its C++-codegen-TEXT assertions
/// (no C++ is ever compiled here, only generated as a string, so per CLAUDE.md's rule these are
/// not Integration tests).</para>
/// </summary>
[TestFixture]
public class UserDelegateConversionTests
{
    // ========================================================================
    // Helpers
    // ========================================================================

    private static ProgramNode Parse(string source, out Parser parser)
    {
        var lexer = new Lexer(source);
        parser = new Parser(lexer.Tokenize());
        return parser.Parse();
    }

    /// <summary>Runs the front end only (lex/parse/semantic-analyze) with no IR/codegen step, for
    /// sites where codegen or IR lowering has its OWN, unrelated gap (a non-constant field
    /// initializer refusal at IR level, #170's MyBase.New closure gap) and the assertion under
    /// test is purely "did the ANALYZER accept/type this the way #187 says it should".</summary>
    private static (bool Success, SemanticAnalyzer Analyzer) Analyze(string source)
    {
        var ast = Parse(source, out _);
        var analyzer = new SemanticAnalyzer();
        var success = analyzer.Analyze(ast);
        return (success, analyzer);
    }

    private static string AllErrors(SemanticAnalyzer analyzer) =>
        string.Join("; ", analyzer.Errors.Select(e => e.Message));

    /// <summary>Compile BasicLang source to C# output string (mirrors LambdaTests.CompileToCSharp).
    /// A real C# <c>delegate</c> is emitted for a user <c>Delegate</c> declaration
    /// (<c>CSharpBackend.GenerateDelegate</c>: <c>public delegate {ReturnType} {Name}(...)</c>),
    /// so a converted lambda/AddressOf reads back with the DELEGATE'S OWN NAME as its declared
    /// type (e.g. <c>Notify</c>), never <c>Action&lt;string&gt;</c> — this is how "the
    /// expression's type is D, not Action/Func" is asserted below.</summary>
    private static string CompileToCSharp(string source, out List<string> errors)
    {
        errors = new List<string>();

        var ast = Parse(source, out _);

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var options = new CodeGenOptions
        {
            Namespace = "TestOutput",
            GenerateMainMethod = false,
            GenerateComments = false
        };
        var csharpGen = new ImprovedCSharpCodeGenerator(options);
        return csharpGen.Generate(irModule);
    }

    /// <summary>C++ codegen as TEXT ONLY — never compiled, never run, so this stays out of
    /// <c>[Category("Integration")]</c> per CLAUDE.md ("mark anything that compiles/runs native
    /// code... Integration"). Mirrors <c>CppCollectionTests.CompileToCpp</c>.</summary>
    private static string CompileToCppText(string source, out List<string> errors)
    {
        errors = new List<string>();

        var ast = Parse(source, out var parser);
        foreach (var error in parser.Errors) errors.Add($"Parse error: {error.Message}");
        if (errors.Count > 0) return null;

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    /// <summary>The optimizer-running C++ TEXT sibling of <see cref="CompileToCppText"/> — same
    /// "never compiled/run" scope, but through the SAME standard passes the CLI's optimizing
    /// build runs, per CLAUDE.md's "validate codegen through the CLI and the IR optimizer, not
    /// only the non-optimizing helper".</summary>
    private static string CompileToCppOptimizedText(string source, out List<string> errors)
    {
        errors = new List<string>();

        var ast = Parse(source, out var parser);
        foreach (var error in parser.Errors) errors.Add($"Parse error: {error.Message}");
        if (errors.Count > 0) return null;

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    // ========================================================================
    // 1. Conversion sites — a lambda or AddressOf converts to a user Delegate, typed AS the
    //    delegate (never Action/Func). Probe numbers refer to S/t187/probes and S/t187/edge*.
    // ========================================================================

    [Test]
    public void Dim_TypedLambda_ConvertsToUserDelegateSub()
    {
        // D1
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify = Sub(m As String) Console.WriteLine("got " & m)
                d("x")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("Notify"), "the delegate declaration must be emitted");
        Assert.That(output, Does.Not.Contain("Action<string>"),
            "the Dim'd value must be typed as Notify, not its structural Action<string>");
    }

    [Test]
    public void Dim_UntypedLambda_ParametersInferredFromUserDelegate()
    {
        // D6 — an untyped lambda parameter is inferred from the target delegate's OWN signature.
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify = Sub(m) Console.WriteLine("untyped " & m)
                d("q")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("(string m) =>"),
            "'m' must be inferred as String from Notify's own parameter, not Object");
        Assert.That(output, Does.Not.Contain("(object m)"));
    }

    [Test]
    public void Dim_MultiLineFunctionLambda_ConvertsToUserDelegateFunction_TypedAsD()
    {
        // D3's second half — a multi-line Function lambda into a Delegate Function.
        var source = """
            Delegate Function Combine(a As Integer, b As Integer) As Integer

            Sub Main()
                Dim g As Combine = Function(a As Integer, b As Integer)
                                       Return a * b
                                   End Function
                Console.WriteLine(g(4, 5))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("Combine"));
        Assert.That(output, Does.Not.Contain("Func<int, int, int>"),
            "the Dim'd value must be typed as Combine, not its structural Func<int, int, int>");
    }

    [Test]
    public void Dim_AddressOf_ConvertsToUserDelegateSub()
    {
        // D2
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Handler(m As String)
                Console.WriteLine("h " & m)
            End Sub

            Sub Main()
                Dim d As Notify = AddressOf Handler
                d("y")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Action<string>"),
            "AddressOf converted to Notify must not read back as its structural Action<string>");
    }

    [Test]
    public void FieldInitializer_LambdaTargetTypedToUserDelegate_AnalyzerAccepts()
    {
        // E9b — the ANALYZER admits a lambda into a Delegate-typed FIELD initializer (#187); the
        // program as a whole still fails, but at the IR stage, for the pre-existing, unrelated,
        // by-design reason that a field initializer must be a constant expression
        // (IRBuilder.cs:1837) — never the old "Cannot assign 'Action' to 'Notify'" refusal.
        var source = """
            Delegate Sub Notify(msg As String)

            Class Holder
                Public Field As Notify = Sub(m) Console.WriteLine("field " & m)
            End Class
            """;

        var (success, analyzer) = Analyze(source);

        Assert.That(success, Is.True,
            "the ANALYZER must accept a lambda target-typed to a Delegate field initializer: " + AllErrors(analyzer));

        // Confirm the unrelated IR-level refusal is the ONLY thing that still fails the full
        // pipeline, and that it is not the pre-#187 message.
        var irBuilder = new IRBuilder(analyzer);
        var ex = Assert.Throws<Exception>(() => irBuilder.Build(Parse(source, out _), "TestModule"));
        Assert.That(ex!.Message, Does.Contain("cannot be computed at compile time"));
        Assert.That(ex.Message, Does.Not.Contain("Cannot assign"));
    }

    [Test]
    public void Assignment_TypedLambda_ConvertsToUserDelegate_IncludingCapturingLambda()
    {
        // D4 — reassignment, second lambda captures an outer local.
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify
                d = Sub(m As String) Console.WriteLine("assigned " & m)
                d("z")
                Dim prefix As String = ">"
                d = Sub(m As String) Console.WriteLine(prefix & m)
                d("w")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Action<string>"));
    }

    [Test]
    public void Assignment_AddressOf_ConvertsToUserDelegate()
    {
        // Dedicated AddressOf-at-assignment probe (distinct from the Dim-site AddressOf case):
        // kills a mutant that reverts ONLY Visit(AssignmentStatementNode)'s target-typing check.
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Shout(m As String)
                Console.WriteLine("shout " & m)
            End Sub

            Sub Main()
                Dim a As Notify
                a = AddressOf Shout
                a("v")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Action<string>"));
    }

    [Test]
    public void PropertySet_Lambda_ConvertsToUserDelegate()
    {
        // E9_sites' h.Prop = Sub(m) ... — a PROPERTY, not a plain variable, on the assignment
        // target side.
        var source = """
            Delegate Sub Notify(msg As String)

            Class Holder
                Public Property Prop As Notify
            End Class

            Sub Main()
                Dim h As New Holder()
                h.Prop = Sub(m) Console.WriteLine("prop " & m)
                Dim p As Notify = h.Prop
                p("y")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Action<string>"));
    }

    [Test]
    public void Return_TypedLambda_ConvertsToUserDelegateFunction()
    {
        // D5 — Return infers the CURRIED lambda's own parameters/return from the target delegate.
        var source = """
            Delegate Function Transform(n As Integer) As Integer

            Function MakeAdder(k As Integer) As Transform
                Return Function(n As Integer) n + k
            End Function

            Sub Main()
                Dim t As Transform = MakeAdder(10)
                Console.WriteLine(t(5))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Func<int, int>"),
            "MakeAdder must return Transform, not its structural Func<int, int>");
    }

    [Test]
    public void Return_AddressOf_ConvertsToUserDelegateFunction()
    {
        // PickUp() from E9_sites — dedicated AddressOf-at-Return probe: kills a mutant that
        // reverts ONLY the Return-statement target-typing check.
        var source = """
            Delegate Function Transform(n As Integer) As Integer

            Function PickUp() As Transform
                Return AddressOf Inc
            End Function

            Function Inc(n As Integer) As Integer
                Return n + 1
            End Function

            Sub Main()
                Dim up As Transform = PickUp()
                Console.WriteLine(up(10))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Func<int, int>"));
    }

    [Test]
    public void UserProcedureArgument_Lambda_ConvertsToUserDelegate()
    {
        // D3's first line — a lambda argument to a free FUNCTION whose parameter is a user
        // Delegate.
        var source = """
            Delegate Function Combine(a As Integer, b As Integer) As Integer

            Function Apply(f As Combine, x As Integer, y As Integer) As Integer
                Return f(x, y)
            End Function

            Sub Main()
                Console.WriteLine(Apply(Function(a As Integer, b As Integer) a + b, 2, 3))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void ClassMethodArgument_Lambda_ConvertsToUserDelegate_InstanceAndShared()
    {
        // E4 — an instance method AND a Shared method, each with a Delegate-typed parameter.
        var source = """
            Delegate Function Transform(n As Integer) As Integer

            Class Runner
                Public Function Run(ByVal f As Transform, x As Integer) As Integer
                    Return f(x)
                End Function
                Public Shared Function SRun(ByVal f As Transform, x As Integer) As Integer
                    Return f.Invoke(x)
                End Function
            End Class

            Sub Main()
                Dim r As New Runner()
                Console.WriteLine(r.Run(Function(n) n + 1, 10))
                Console.WriteLine(Runner.SRun(Function(n As Integer) n * 2, 10))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void ConstructorArgument_UntypedLambda_ConvertsToUserDelegate()
    {
        // E9_sites' Holder(Function(n) n * 7) — a NEW site as of #187: a constructor parameter
        // typed as a user Delegate now target-types its lambda argument (including inferring an
        // untyped parameter), which previously had NO target typing applied at all.
        var source = """
            Delegate Function Transform(n As Integer) As Integer

            Class Holder
                Public Held As Transform
                Public Sub New(t As Transform)
                    Held = t
                End Sub
            End Class

            Sub Main()
                Dim h As New Holder(Function(n) n * 7)
                Console.WriteLine(h.Held(3))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("(int n) => n * 7"),
            "the untyped constructor-argument lambda parameter must be inferred as Integer");
    }

    [Test]
    public void ConstructorArgument_FuncTarget_UntypedLambdaNowInferred()
    {
        // H1_func_ctor — the SAME new site, but for a Func(Of ...) constructor parameter: #187's
        // fix is shared infrastructure (VisitWithDelegateTarget), so a Func/Action constructor
        // argument is NEWLY admitted here too, not just a user Delegate one.
        var source = """
            Class Box
                Private _f As Func(Of Integer, Integer)
                Public Sub New(f As Func(Of Integer, Integer))
                    _f = f
                End Sub
                Public Function Go(x As Integer) As Integer
                    Return _f(x)
                End Function
            End Class

            Sub Main()
                Dim b As New Box(Function(n) n * 10)
                Console.WriteLine(b.Go(2))
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("(int n) => n * 10"),
            "the untyped constructor-argument lambda parameter must be inferred as Integer");
    }

    [Test]
    public void MyBaseNewArgument_Lambda_AnalyzerAcceptsAndTypesAsUserDelegate()
    {
        // E13_mybase — front-end-only: the ANALYZER now target-types a lambda argument to
        // MyBase.New's Delegate-typed parameter (a NEW #187 site). The full pipeline still fails
        // to COMPILE on C#/C++/MSIL for the pre-existing, unrelated reason that a lambda passed
        // to MyBase.New has no IL/closure lowering at all (#170) — measured in
        // S/t187/edge3/o-final/E13_mybase.txt; JavaScript alone runs it end-to-end. This test
        // only pins the ANALYZER'S half of the fix, which #170 does not touch.
        var source = """
            Delegate Function Transform(n As Integer) As Integer

            Class BaseBox
                Public Held As Transform
                Public Sub New(t As Transform)
                    Held = t
                End Sub
            End Class

            Class Derived
                Inherits BaseBox
                Public Sub New()
                    MyBase.New(Function(n) n + 100)
                End Sub
            End Class
            """;

        var (success, analyzer) = Analyze(source);

        Assert.That(success, Is.True,
            "the analyzer must accept a lambda target-typed to MyBase.New's Delegate parameter: "
            + AllErrors(analyzer));
    }

    [Test]
    public void MyBaseNewArgument_FuncTarget_UntypedLambdaNowInferred()
    {
        // The Func(Of ...) sibling of the probe above, analyzer-only for the same reason (#170
        // has no lowering for a lambda passed to MyBase.New on any backend, Func/Action included
        // — this shape was ALREADY refused pre-#187 for lack of target-typing, independent of
        // #170's later IR gap).
        var source = """
            Class BaseBox
                Public Held As Func(Of Integer, Integer)
                Public Sub New(t As Func(Of Integer, Integer))
                    Held = t
                End Sub
            End Class

            Class Derived
                Inherits BaseBox
                Public Sub New()
                    MyBase.New(Function(n) n + 100)
                End Sub
            End Class
            """;

        var (success, analyzer) = Analyze(source);

        Assert.That(success, Is.True,
            "the analyzer must infer the untyped lambda parameter for MyBase.New's Func parameter: "
            + AllErrors(analyzer));
    }

    [Test]
    public void DelegateInvocationArgument_Lambda_ConvertsToUserDelegate()
    {
        // E14 — Relay's OWN first parameter is itself a user Delegate (Notify); calling a Relay
        // VALUE with a lambda argument target-types that argument to Notify.
        var source = """
            Delegate Sub Notify(msg As String)
            Delegate Sub Relay(n As Notify, s As String)

            Sub Main()
                Dim r As Relay = Sub(n, s) n(s & "!")
                r(Sub(m) Console.WriteLine("relayed " & m), "hi")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    // ========================================================================
    // 2. Invocation typing — d(args) / d.Invoke(args) are typed R (Void for a Delegate Sub);
    //    wrong argument count/type is refused; .Invoke on Func/Action is typed by its type arguments (#202).
    // ========================================================================

    [Test]
    public void Invocation_UserDelegateFunction_TypedAsItsOwnReturnType()
    {
        // D3 — invoking a Delegate Function used to type Void, refusing `Return f(x, y)` from an
        // Integer function ("Cannot return type 'Void' from function expecting 'Integer'").
        var source = """
            Delegate Function Combine(a As Integer, b As Integer) As Integer

            Function Apply(f As Combine, x As Integer, y As Integer) As Integer
                Return f(x, y)
            End Function
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void Invocation_UserDelegateSub_TypedAsVoid()
    {
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Take(d As Notify, s As String)
                d(s)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void DotInvoke_UserDelegateFunction_TypedAsItsOwnReturnType()
    {
        // E6 — d.Invoke(args) on a user Delegate takes the SAME path as d(args): typed R.
        var source = """
            Delegate Function Transform(n As Integer) As Integer

            Sub Main()
                Dim t As Transform = Function(n As Integer) n * 3
                Dim r As Integer = t.Invoke(4)
                Console.WriteLine(r)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void DotInvoke_UserDelegateSub_Works()
    {
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify = Sub(m) Console.WriteLine("inv " & m)
                d.Invoke("a")
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void Invocation_WrongArgumentCount_Refused()
    {
        // R10
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify = Sub(m As String) Console.WriteLine(m)
                d(1, 2)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null);
        var allErrors = string.Join("; ", errors);
        Assert.That(allErrors, Does.Contain("Delegate 'd' expects 1 argument(s), got 2. Expected: Notify(msg As String)"));
    }

    [Test]
    public void Invocation_WrongArgumentType_Refused()
    {
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify = Sub(m As String) Console.WriteLine(m)
                d(5)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null);
        var allErrors = string.Join("; ", errors);
        Assert.That(allErrors, Does.Contain("cannot convert from 'Integer' to 'String'"));
    }

    [Test]
    public void DotInvoke_OnFuncAction_IsTypedByItsTypeArguments_Task202()
    {
        // #202 (was pinned UNFIXED here as DotInvoke_OnFuncAction_StaysTypedObject_PinnedAgainst202): `f.Invoke(args)` is
        // `f(args)` for every delegate the analyzer models, not only a user Delegate (SemanticAnalyzer.IsModeledDelegate, the
        // gate the analyzer and the IR builder both read). So `.Invoke` on a Func is typed by the Func's LAST type argument —
        // here Integer — and `Dim r As Integer = f.Invoke(5)` is accepted (it was refused with "Cannot assign value of type
        // 'Object' to variable of type 'Integer'"). On C# it reads back as the plain call `f(5)`. vbc accepts the program and
        // prints 10. UserDelegateGapsDiagnosticsTests holds the rest of the shapes (Action, Func(Of String), a user
        // Class named Action); UserDelegateGapsExecutionTests runs them on four backends.
        var source = """
            Sub Main()
                Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
                Dim r As Integer = f.Invoke(5)
                Console.WriteLine(r)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("r = f(5);"), ".Invoke on a Func is lowered as the call f(5)");
    }

    // ========================================================================
    // 3. Mismatches (R1-R12) — an EXACT message naming the delegate, both signatures and the
    //    first difference. Never the generic "Cannot assign 'Action'". Messages are taken
    //    verbatim from S/t187/probes/*.exp and the implementer's captured build logs.
    // ========================================================================

    [TestCase("""
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Sub(m As Integer) Console.WriteLine(m)
        End Sub
        """,
        "Cannot convert lambda 'Sub(m As Integer)' to delegate 'Notify(msg As String)': parameter 1 is Integer, the delegate takes String",
        "R1 (wrong parameter type)")]
    [TestCase("""
        Delegate Function Transform(n As Integer) As Integer

        Sub Main()
            Dim t As Transform = Function(n As Integer, m As Integer) n + m
        End Sub
        """,
        "Cannot convert lambda 'Function(n As Integer, m As Integer) As Integer' to delegate 'Transform(n As Integer) As Integer': it takes 2 parameter(s), the delegate takes 1",
        "R2 (arity)")]
    [TestCase("""
        Delegate Function Transform(n As Integer) As Integer

        Sub Main()
            Dim t As Transform = Sub(n As Integer) Console.WriteLine(n)
        End Sub
        """,
        "Cannot convert lambda 'Sub(n As Integer)' to delegate 'Transform(n As Integer) As Integer': it is a Sub, which returns no value; the delegate is a Function returning Integer",
        "R3 (Sub into a Function delegate)")]
    [TestCase("""
        Delegate Function Transform(n As Integer) As Integer

        Sub Main()
            Dim t As Transform = Function(n As Integer) CLng(n)
        End Sub
        """,
        "Cannot convert lambda 'Function(n As Integer) As Long' to delegate 'Transform(n As Integer) As Integer': it returns Long, the delegate returns Integer",
        "R4 (wrong return type)")]
    [TestCase("""
        Delegate Sub Notify(msg As String)

        Sub Handler(m As Integer)
            Console.WriteLine(m)
        End Sub

        Sub Main()
            Dim d As Notify = AddressOf Handler
        End Sub
        """,
        "Cannot convert 'AddressOf Handler' (Sub Handler(m As Integer)) to delegate 'Notify(msg As String)': parameter 1 is Integer, the delegate takes String",
        "R5 (AddressOf wrong parameter type)")]
    [TestCase("""
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Function(m As String) m.Length
        End Sub
        """,
        "Cannot convert lambda 'Function(m As String) As Integer' to delegate 'Notify(msg As String)': it is a Function returning Integer; the delegate is a Sub",
        "R6 (Function into a Sub delegate)")]
    [TestCase("""
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim a As Action(Of String) = Sub(m As String) Console.WriteLine(m)
            Dim d As Notify = a
        End Sub
        """,
        "A delegate value does not convert to a different delegate type ('Action(Of String)' to 'Notify'), as in C#. Assign a lambda or AddressOf instead",
        "R7 (delegate VALUE of a different type)")]
    [TestCase("""
        Delegate Function Transform(n As Integer) As Integer

        Function Twice(n As Integer) As Long
            Return n * 2
        End Function

        Sub Main()
            Dim t As Transform = AddressOf Twice
        End Sub
        """,
        "Cannot convert 'AddressOf Twice' (Function Twice(n As Integer) As Long) to delegate 'Transform(n As Integer) As Integer': it returns Long, the delegate returns Integer",
        "R8 (AddressOf wrong return type)")]
    [TestCase("""
        Delegate Function Transform(n As Integer) As Integer

        Function Apply(f As Transform, x As Integer) As Integer
            Return f(x)
        End Function

        Sub Main()
            Console.WriteLine(Apply(Function(n As String) 1, 2))
        End Sub
        """,
        "Cannot convert lambda 'Function(n As String) As Integer' to delegate 'Transform(n As Integer) As Integer': parameter 1 is String, the delegate takes Integer",
        "R9 (call-argument site mismatch)")]
    [TestCase("""
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Sub() Console.WriteLine("no args")
        End Sub
        """,
        "Cannot convert lambda 'Sub()' to delegate 'Notify(msg As String)': it takes 0 parameter(s), the delegate takes 1",
        "R12 (0-arity lambda into a 1-parameter delegate)")]
    public void Mismatch_RefusedWithExactMessage(string source, string expectedMessage, string label)
    {
        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, $"[{label}] must be refused");
        var allErrors = string.Join("; ", errors);
        // The specific message is asserted verbatim below — that alone rules out the pre-#187
        // generic fallback for every case EXCEPT R7, where "Cannot assign value of type 'Action'
        // to variable of type 'Notify'" is legitimately kept as the message's PREFIX (#187's
        // stated boundary: a delegate VALUE of a different type stays refused as in C#), with the
        // hint appended — so R7 is not a regression to the generic message, it is the by-design
        // exception, and expectedMessage below (the appended hint) still pins it precisely.
        Assert.That(allErrors, Does.Contain(expectedMessage), $"[{label}] exact message mismatch:\n{allErrors}");
    }

    [Test]
    public void Mismatch_R10_WrongArgumentCount_ExactMessage()
    {
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim d As Notify = Sub(m As String) Console.WriteLine(m)
                d(1, 2)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null);
        Assert.That(string.Join("; ", errors),
            Does.Contain("Delegate 'd' expects 1 argument(s), got 2. Expected: Notify(msg As String)"));
    }

    [Test]
    public void Mismatch_R11_DelegateValueOfADifferentType_ReturnSite_ExactMessage()
    {
        // R11 — the SAME "delegate value of a different type" rule, at the Return site AND the
        // call-argument site, in one program: two DIFFERENT refusals, both mentioning the hint.
        var source = """
            Delegate Sub Notify(msg As String)
            Delegate Sub Other(msg As String)

            Function Make(a As Action(Of String)) As Notify
                Return a
            End Function

            Sub Take(n As Notify)
                n("x")
            End Sub

            Sub Main()
                Dim o As Other = Sub(m) Console.WriteLine(m)
                Take(o)
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null);
        var allErrors = string.Join("; ", errors);
        Assert.That(allErrors, Does.Contain(
            "Cannot return type 'Action<String>' from function expecting 'Notify'. A delegate value does not convert to a different delegate type ('Action(Of String)' to 'Notify'), as in C#"));
        Assert.That(allErrors, Does.Contain(
            "Argument 1: cannot convert from 'Other' to 'Notify'. A delegate value does not convert to a different delegate type ('Other' to 'Notify'), as in C#"));
    }

    // ========================================================================
    // 4. Scope — delegate parameters get their OWN scope (E12); a delegate parameter name does
    //    not leak as a global.
    // ========================================================================

    [Test]
    public void TwoDelegates_SharingAParameterName_AreBothAccepted()
    {
        // E12 — pre-#187, both delegate declarations' parameters were defined in the GLOBAL
        // scope, so the second "msg" was "already defined".
        var source = """
            Delegate Sub Notify(msg As String)
            Delegate Sub Notify2(msg As String)

            Sub Main()
                Dim a As Notify = Sub(msg) Console.WriteLine("one " & msg)
                Dim b As Notify2 = Sub(msg) Console.WriteLine("two " & msg)
                a("p")
                b("q")
            End Sub
            """;

        var (success, analyzer) = Analyze(source);

        Assert.That(success, Is.True, AllErrors(analyzer));
    }

    [Test]
    public void DelegateParameterName_DoesNotCollideWithATopLevelProcedureOfTheSameName()
    {
        // A delegate parameter's scope is the DECLARATION's own — never the enclosing (global)
        // scope — so a top-level Sub sharing its name is unaffected.
        var source = """
            Delegate Sub Notify(msg As String)

            Sub msg()
                Console.WriteLine("proc")
            End Sub

            Sub Main()
                msg()
            End Sub
            """;

        var (success, analyzer) = Analyze(source);

        Assert.That(success, Is.True, AllErrors(analyzer));
    }

    [Test]
    public void DelegateParameterName_DoesNotCollideWithAnUnrelatedLocalOfTheSameName()
    {
        var source = """
            Delegate Sub Notify(msg As String)

            Sub Main()
                Dim msg As String = "hi"
                Console.WriteLine(msg)
            End Sub
            """;

        var (success, analyzer) = Analyze(source);

        Assert.That(success, Is.True, AllErrors(analyzer));
    }

    // ========================================================================
    // 5. Unchanged — the Func/Action exactness rule is REUSED verbatim, not loosened, for a
    //    user Delegate target: a single-line lambda's body must be exactly R, no widening.
    // ========================================================================

    [Test]
    public void SingleLineLambda_BodyNotExactlyR_StillRefused_OnFuncItself()
    {
        // The pre-existing Func(Of...) control, measured via the CLI — Integer does not widen to
        // Long even though Dim/assignment elsewhere would allow the widening; this is the RULE
        // #187 reuses unchanged for a user Delegate. (The generic "Cannot assign value of type
        // 'Func' to variable of type 'Func'" — both sides just say "Func", no generic args — is
        // ITSELF the pre-existing, UNCHANGED Func message; #187 only adds the precise message on
        // the user-Delegate side, tested below.)
        var source = """
            Sub Main()
                Dim f As Func(Of Integer, Long) = Function(n As Integer) n
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Func(Of Integer, Long) must still refuse a body typed Integer");
        Assert.That(string.Join("; ", errors),
            Does.Contain("Cannot assign value of type 'Func' to variable of type 'Func'"));
    }

    [Test]
    public void SingleLineLambda_BodyNotExactlyR_StillRefused_OnEquivalentUserDelegate()
    {
        // The SAME shape as above, D being a user Delegate whose structural shape is
        // Func(Of Integer, Long): still refused, by the SAME exactness rule — pinning that #187
        // did not invent a separate, looser rule for user delegates. Measured via the CLI.
        var source = """
            Delegate Function LongMaker(n As Integer) As Long

            Sub Main()
                Dim f As LongMaker = Function(n As Integer) n
            End Sub
            """;

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "LongMaker must still refuse a body typed Integer, same as Func(Of Integer, Long)");
        Assert.That(string.Join("; ", errors),
            Does.Contain("Cannot convert lambda 'Function(n As Integer) As Integer' to delegate " +
                "'LongMaker(n As Integer) As Long': it returns Integer, the delegate returns Long"));
    }

    // ========================================================================
    // 6. IR/codegen: the lambda's R must come from the delegate's shape, and a user Delegate's
    //    parameter/return TYPE (not just its name) must reach the C++ backend. Both are
    //    C++-codegen-TEXT-only checks (nothing is compiled or run), so they stay in the fast
    //    subset. Kill: IRBuilder's `lambdaShape` reverted to `lambdaType`; IRDelegate types (or
    //    CppCodeGenerator's `p.Type` read) reverted.
    // ========================================================================

    [Test]
    public void MultiLineFunctionLambda_IntoUserDelegate_CppReturnsResolvedType_NotObject()
    {
        // D3's multi-line Function lambda into Combine (Delegate Function ... As Integer).
        // IRBuilder.Visit(LambdaExpressionNode) reads its R off DelegateShapeOf(lambdaType), not
        // lambdaType itself — lambdaType is "Combine" (a class-kind TypeInfo with NO
        // GenericArguments once converted), so IsFuncWithReturnType(lambdaType) is false and the
        // return type would silently fall back to Object without the shape mapping. Measured via
        // the CLI (--show-generated): `g = [=](int32_t a, int32_t b) -> int32_t { ... }`.
        var source = """
            Delegate Function Combine(a As Integer, b As Integer) As Integer

            Sub Main()
                Dim g As Combine = Function(a As Integer, b As Integer)
                                       Return a * b
                                   End Function
                Console.WriteLine(g(4, 5))
            End Sub
            """;

        var cpp = CompileToCppOptimizedText(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(cpp, Does.Contain("-> int32_t"),
            "the multi-line lambda's own C++ function must return int32_t (Combine's R), not Object:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("-> void*"));
    }

    [Test]
    public void UserDelegate_ClassReturnType_CppAliasUsesSharedPtr_NotBareClass()
    {
        // E3 — CppCodeGenerator.GenerateDelegate must read the RESOLVED TypeInfo
        // (IRParameter.Type / IRDelegate.ReturnType) for a class-typed delegate return, not a
        // bare-name Primitive stub: BasicLang classes are std::shared_ptr<T> (reference
        // semantics), so a std::function<Widget(...)> (a VALUE Widget) could never hold a lambda
        // returning `New Widget(...)` or an AddressOf a Widget-returning method at all. Measured
        // via the CLI (--show-generated): `using MakeWidget = std::function<std::shared_ptr<Widget>(std::string)>;`.
        var source = """
            Class Widget
                Public Name As String
                Public Sub New(n As String)
                    Name = n
                End Sub
            End Class

            Delegate Function MakeWidget(n As String) As Widget

            Sub Main()
                Dim f As MakeWidget = Function(n As String) New Widget(n)
                Console.WriteLine(f("a").Name)
            End Sub
            """;

        var cpp = CompileToCppOptimizedText(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(cpp, Does.Contain("using MakeWidget = std::function<std::shared_ptr<Widget>(std::string)>;"),
            "MakeWidget's C++ alias must use the resolved, shared_ptr-wrapped Widget return type:\n" + cpp);
    }
}
