using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using VisualGameStudio.Tests.Msil;
using AccessModifier = BasicLang.Compiler.AST.AccessModifier;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A procedure's ACCESS: what a <c>Function</c> or <c>Sub</c> with no modifier gets, and what
/// an explicit <c>Private</c> means outside its Module — on every backend, from the front end.
///
/// <para>⛔ THE PARSER DEFAULTED A NO-MODIFIER PROCEDURE TO <c>Private</c>, THE OPPOSITE OF THE
/// LANGUAGE. VB's table: a Module's (or a file's) Functions and Subs are Public, its variables
/// and constants Private. Only C# ever noticed, because csc is the one backend that enforces the
/// <c>private static</c> the C# backend emits per Module class: a plain <c>Function Twice</c>
/// ran from any other module on C++, JavaScript and MSIL and was CS0122 on C#. And the two AST
/// constructors disagreed with each other — a bare file-scope <c>Function</c> was Private, a
/// bare <c>Sub</c> Public.</para>
///
/// <para>⛔ AND BECAUSE OF THAT DEFAULT, THE FRONT END ENFORCED NOTHING FOR PROCEDURES: doing
/// so would have refused every plain <c>Function</c> called across modules. So an explicit
/// <c>Private Function</c> — single-file, multi-file in either compile order, <c>.mod</c> — ran
/// from anywhere on three backends and was refused by csc alone; a Private <c>F</c> beside a
/// Public <c>F</c> made a bare call from a third module "ambiguous" on all four. Now the parser
/// says Public, the analyzer treats a procedure's access exactly as a variable's (bare,
/// qualified, cross-unit through every channel, and in the IDE's symbol table), and all four
/// backends agree from the front end, with one message: "'F' is Private to module 'A' and cannot
/// be accessed from here".</para>
///
/// <para>⚠ Every running case asserts all FOUR backends; every refusal is asserted at the
/// analyzer, which is where all four now agree. The multi-file refusals are asserted in BOTH
/// compile orders, because a pending sibling's signatures used to be registered WITHOUT their
/// declared access — a Private procedure was callable whenever its file was listed after the
/// caller's.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ModuleProcedureAccessTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);

    private static ProgramNode Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        return ast;
    }

    private static SemanticAnalyzer Analyze(string source, out bool ok)
    {
        var analyzer = new SemanticAnalyzer();
        ok = analyzer.Analyze(Parse(source));
        return analyzer;
    }

    private static string Errors(SemanticAnalyzer a) => string.Join(" | ", a.Errors.Select(e => e.Message));

    /// <summary>The declared access of every procedure, variable and constant in the AST, by name.</summary>
    private static string AccessOf(ProgramNode ast, string name)
    {
        AccessModifier? found = null;
        void Walk(ASTNode n)
        {
            switch (n)
            {
                case ModuleNode m: foreach (var c in m.Members) Walk(c); break;
                case ClassNode c: foreach (var x in c.Members) Walk(x); break;
                case InterfaceNode i: foreach (var x in i.Methods) Walk(x); break;
                case FunctionNode f when f.Name == name: found = f.Access; break;
                case SubroutineNode s when s.Name == name: found = s.Access; break;
                case VariableDeclarationNode v when v.Name == name: found = v.Access; break;
                case ConstantDeclarationNode k when k.Name == name: found = k.Access; break;
                case ExtensionMethodNode x when x.Method?.Name == name: found = x.Method.Access; break;
            }
        }
        foreach (var d in ast.Declarations) Walk(d);
        Assert.That(found, Is.Not.Null, $"no declaration named '{name}'");
        return found.ToString();
    }

    // ------------------------------------------------------------------ the parser's default

    /// <summary>⛔ The headline: every one of these parsed Private.</summary>
    [TestCase("Module M\n Function F() As Integer\n  Return 1\n End Function\nEnd Module\n", "F")]
    [TestCase("Module M\n Sub F()\n End Sub\nEnd Module\n", "F")]
    [TestCase("Module M\n Async Function F() As Integer\n  Return 1\n End Function\nEnd Module\n", "F")]
    [TestCase("Module M\n Iterator Function F() As IEnumerable(Of Integer)\n  Yield 1\n End Function\nEnd Module\n", "F")]
    [TestCase("Function F() As Integer\n Return 1\nEnd Function\n", "F")]
    [TestCase("Iterator Function F() As IEnumerable(Of Integer)\n Yield 1\nEnd Function\n", "F")]
    [TestCase("Shared Function F() As Integer\n Return 1\nEnd Function\n", "F")]
    [TestCase("Async Sub F()\nEnd Sub\n", "F")]
    [TestCase("Interface I\n Function F() As Integer\nEnd Interface\n", "F")]
    [TestCase("Extension Function F(n As Integer) As Integer\n Return n\nEnd Function\n", "F")]
    public void AProcedure_WithNoModifier_ParsesPublic(string source, string name)
        => Assert.That(AccessOf(Parse(source), name), Is.EqualTo("Public"));

    /// <summary>A bare file-scope <c>Sub</c> was already Public — the Function now matches it.</summary>
    [Test]
    public void ABareFileScopeFunctionAndSub_AgreeOnPublic()
    {
        var ast = Parse("Function F() As Integer\n Return 1\nEnd Function\nSub S()\nEnd Sub\n");
        Assert.That(AccessOf(ast, "F"), Is.EqualTo(AccessOf(ast, "S")).And.EqualTo("Public"));
    }

    /// <summary>VB's other half: a variable or constant with no modifier stays Private.</summary>
    [TestCase("Module M\n Dim V As Integer\nEnd Module\n", "V")]
    [TestCase("Module M\n Const K As Integer = 1\nEnd Module\n", "K")]
    [TestCase("Module M\n V As Integer\nEnd Module\n", "V")]
    [TestCase("Dim V As Integer\n", "V")]
    [TestCase("Const K As Integer = 1\n", "K")]
    [TestCase("Shared Dim V As Integer\n", "V")]
    public void AVariableOrConstant_WithNoModifier_StaysPrivate(string source, string name)
        => Assert.That(AccessOf(Parse(source), name), Is.EqualTo("Private"));

    [TestCase("Module M\n Private Function F() As Integer\n  Return 1\n End Function\nEnd Module\n", "F", "Private")]
    [TestCase("Module M\n Friend Sub F()\n End Sub\nEnd Module\n", "F", "Friend")]
    [TestCase("Module M\n Public Dim V As Integer\nEnd Module\n", "V", "Public")]
    [TestCase("Module M\n Private Iterator Function F() As IEnumerable(Of Integer)\n  Yield 1\n End Function\nEnd Module\n", "F", "Private")]
    [TestCase("Private Function F() As Integer\n Return 1\nEnd Function\n", "F", "Private")]
    [TestCase("Private Async Function F() As Integer\n Return 1\nEnd Function\n", "F", "Private")]
    [TestCase("Friend Shared Function F() As Integer\n Return 1\nEnd Function\n", "F", "Friend")]
    public void AnExplicitModifier_IsKept(string source, string name, string expected)
        => Assert.That(AccessOf(Parse(source), name), Is.EqualTo(expected));

    // ------------------------------------------------------------------ no modifier runs everywhere

    /// <summary>⛔ 8 on three backends and CS0122 on C#, before. Qualified and bare.</summary>
    [Test]
    public void ANoModifierModuleFunction_RunsOnEveryBackend_QualifiedAndBare()
    {
        const string program = """
            Module Helpers
             Function Twice(n As Integer) As Integer
              Return n * 2
             End Function
            End Module
            Module M
             Sub Main()
              PrintLine(CStr(Helpers.Twice(4)))
              PrintLine(CStr(Twice(5)))
             End Sub
            End Module
            """;
        RunsOnEveryBackend(program, "8\n10");
        Assert.That(ReturnCoercionTests.EmitCSharpForTest(program), Does.Contain("public static int Twice"),
            "C# spells the language's default");
    }

    [Test]
    public void ANoModifierModuleSub_RunsOnEveryBackend_QualifiedAndBare()
    {
        const string program = """
            Module Helpers
             Sub Say(s As String)
              PrintLine(s)
             End Sub
            End Module
            Module M
             Sub Main()
              Helpers.Say("q")
              Say("b")
             End Sub
            End Module
            """;
        RunsOnEveryBackend(program, "q\nb");
        Assert.That(ReturnCoercionTests.EmitCSharpForTest(program), Does.Contain("public static void Say"));
    }

    [Test]
    public void AFriendModuleFunction_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module A
             Friend Function F() As Integer
              Return 3
             End Function
            End Module
            Module M
             Sub Main()
              PrintLine(CStr(A.F() + F()))
             End Sub
            End Module
            """, "6");

    /// <summary>
    /// A Private procedure is reachable from its OWN module whatever the order — bare and
    /// qualified, above and below the declaration — so the refusals below cannot be "Private
    /// is now refused everywhere".
    /// </summary>
    [Test]
    public void APrivateProcedure_CalledFromItsOwnModule_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module A
             Public Function Above() As Integer
              Return F() + A.F()
             End Function
             Private Function F() As Integer
              Return 1
             End Function
             Public Function Below() As Integer
              Return F() + A.F()
             End Function
            End Module
            Module M
             Sub Main()
              PrintLine(CStr(A.Above()))
              PrintLine(CStr(A.Below()))
             End Sub
            End Module
            """, "2\n2");

    /// <summary>A file's own Private procedure, from the same file: the implicit module is one module.</summary>
    [Test]
    public void AFileScopePrivateFunction_CalledFromTheSameFile_RunsOnEveryBackend()
        => RunsOnEveryBackend("Private Function F() As Integer\n Return 7\nEnd Function\nSub Main()\n PrintLine(CStr(F()))\nEnd Sub\n", "7");

    /// <summary>
    /// ⛔ A Private <c>F</c> beside a Public <c>F</c>: the bare call from a third module was
    /// "'F' is ambiguous between modules 'A', 'B'" on all four backends, because the Private one
    /// counted as a candidate. It is not one; the call binds to the Public one — in either
    /// declaration order, so this is not "whichever pass 1 registered first".
    /// </summary>
    [TestCase("Private", "Public", "2")]
    [TestCase("Public", "Private", "1")]
    public void APrivateAndAPublicProcedure_OfOneName_BindToThePublicOne_FromAThirdModule(string aAccess, string bAccess, string expected)
        => RunsOnEveryBackend($"""
            Module A
             {aAccess} Function F() As Integer
              Return 1
             End Function
            End Module
            Module B
             {bAccess} Function F() As Integer
              Return 2
             End Function
            End Module
            Module M
             Sub Main()
              PrintLine(CStr(F()))
             End Sub
            End Module
            """, expected);

    // ------------------------------------------------------------------ Private is refused, from the front end

    /// <summary>
    /// ⛔ Every one of these ran on C++, JavaScript and MSIL and was CS0122 on C#. One message,
    /// from the analyzer, so the four agree before any backend runs.
    /// </summary>
    [TestCase("PrintLine(CStr(A.F()))", TestName = "{m}(qualified Function)")]
    [TestCase("PrintLine(CStr(F()))", TestName = "{m}(bare Function)")]
    [TestCase("A.Hidden()", TestName = "{m}(qualified Sub statement)")]
    [TestCase("Hidden()", TestName = "{m}(bare Sub statement)")]
    [TestCase("Dim x As Integer = A.F()", TestName = "{m}(qualified, in an initializer)")]
    public void APrivateModuleProcedure_CalledFromAnotherModule_IsRefused(string use)
    {
        var analyzer = Analyze($"""
            Module A
             Private Function F() As Integer
              Return 1
             End Function
             Private Sub Hidden()
              PrintLine("h")
             End Sub
            End Module
            Module M
             Sub Main()
              {use}
             End Sub
            End Module
            """, out var ok);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, Errors(analyzer));
            Assert.That(Errors(analyzer), Does.Contain("is Private to module 'A' and cannot be accessed from here"));
        });
    }

    /// <summary>A class is not inside the Module, so its methods are "outside" too.</summary>
    [Test]
    public void APrivateModuleProcedure_CalledFromAClassMethod_IsRefused()
    {
        var analyzer = Analyze("""
            Module A
             Private Function F() As Integer
              Return 1
             End Function
            End Module
            Class P
             Public Function Read() As Integer
              Return A.F()
             End Function
            End Class
            Module M
             Sub Main()
              Dim p As New P()
              PrintLine(CStr(p.Read()))
             End Sub
            End Module
            """, out var ok);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(Errors(analyzer), Does.Contain("'F' is Private to module 'A' and cannot be accessed from here"));
        });
    }

    /// <summary>
    /// Two Public and one Private declaration of a name: the bare call is ambiguous between the
    /// two VISIBLE owners, and the message names those two only.
    /// </summary>
    [Test]
    public void AnAmbiguousBareCall_ListsOnlyTheVisibleOwners()
    {
        var analyzer = Analyze("""
            Module A
             Public Function F() As Integer
              Return 1
             End Function
            End Module
            Module B
             Public Function F() As Integer
              Return 2
             End Function
            End Module
            Module C
             Private Function F() As Integer
              Return 3
             End Function
            End Module
            Module M
             Sub Main()
              PrintLine(CStr(F()))
             End Sub
            End Module
            """, out var ok);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(Errors(analyzer), Does.Contain("'F' is ambiguous between modules 'A', 'B'. Qualify it with the module name"));
            Assert.That(Errors(analyzer), Does.Not.Contain("'C'"));
        });
    }

    // ------------------------------------------------------------------ multi-file

    private static string TempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "BasicLang_ProcAccess_" + Path.GetRandomFileName())).FullName;

    private static CompilationResult CompileFiles(string dir, params (string name, string content)[] files)
    {
        var paths = files.Select(f =>
        {
            var p = Path.Combine(dir, f.name);
            File.WriteAllText(p, f.content);
            return p;
        }).ToArray();
        return new BasicCompiler().CompileProjectFiles(paths);
    }

    private static void CombinedRunsOnEveryBackend(IRModule ir, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharpText(new CSharpCodeGenerator().Generate(ir))), Is.EqualTo(expected), "C#");
            Assert.That(Norm(BclE2E.CompileRun(new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(ir))), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(MsilHarness.RunIlExpectingSuccess(new MSILCodeGenerator().Generate(ir))), Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>⛔ Three backends and CS0122, before — the multi-file no-modifier shape.</summary>
    [Test]
    public void AMultiFileNoModifierModuleProcedure_RunsOnEveryBackend()
    {
        var dir = TempDir();
        try
        {
            var result = CompileFiles(dir,
                ("Helpers.bas", "Module Helpers\n Function Twice(n As Integer) As Integer\n  Return n * 2\n End Function\nEnd Module\n"),
                ("Main.bas", "Import Helpers\nModule M\n Sub Main()\n  PrintLine(CStr(Helpers.Twice(4)))\n  PrintLine(CStr(Twice(5)))\n End Sub\nEnd Module\n"));
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            CombinedRunsOnEveryBackend(result.CombinedIR!, "8\n10");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>A bare file-scope Function in another file — the FunctionNode default, cross-file.</summary>
    [Test]
    public void AMultiFileNoModifierFileScopeFunction_RunsOnEveryBackend()
    {
        var dir = TempDir();
        try
        {
            var result = CompileFiles(dir,
                ("Util.bas", "Function F() As Integer\n Return 7\nEnd Function\n"),
                ("Main.bas", "Import Util\nSub Main()\n PrintLine(CStr(F()))\nEnd Sub\n"));
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            CombinedRunsOnEveryBackend(result.CombinedIR!, "7");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// ⛔ In EITHER compile order. A completed sibling reaches the caller through its exported
    /// symbols; a pending one through signatures read from its AST — and those used to drop
    /// the declared access, so with the caller's file listed first the Private procedure was
    /// callable (9 on three backends), and refused with it listed second.
    /// </summary>
    [TestCase(false, "PrintLine(CStr(Hidden()))", TestName = "{m}(callee first, bare)")]
    [TestCase(false, "PrintLine(CStr(Helpers.Hidden()))", TestName = "{m}(callee first, qualified)")]
    [TestCase(true, "PrintLine(CStr(Hidden()))", TestName = "{m}(caller first, bare)")]
    [TestCase(true, "PrintLine(CStr(Helpers.Hidden()))", TestName = "{m}(caller first, qualified)")]
    public void AMultiFilePrivateProcedure_IsRefused_InEitherCompileOrder(bool callerFirst, string use)
    {
        var dir = TempDir();
        try
        {
            var helpers = ("Helpers.bas", "Module Helpers\n Private Function Hidden() As Integer\n  Return 9\n End Function\nEnd Module\n");
            var main = ("Main.bas", $"Module M\n Sub Main()\n  {use}\n End Sub\nEnd Module\n");
            var result = callerFirst ? CompileFiles(dir, main, helpers) : CompileFiles(dir, helpers, main);
            var messages = string.Join(" | ", result.AllErrors.Select(e => e.Message));
            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True, "must be refused");
                Assert.That(messages, Does.Contain("'Hidden' is Private to module 'Helpers' and cannot be accessed from here"));
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// A <c>.mod</c> file is an implicit Module: its no-modifier Sub is Public and runs on every
    /// backend; its <c>Private Sub</c> is Private to it, whether the caller Imports it or not.
    /// </summary>
    [Test]
    public void AModFileNoModifierSub_RunsOnEveryBackend()
    {
        var dir = TempDir();
        try
        {
            var result = CompileFiles(dir,
                ("Helpers.mod", "Sub Shown()\n PrintLine(\"shown\")\nEnd Sub\n"),
                ("Main.bas", "Sub Main()\n Shown()\nEnd Sub\n"));
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            CombinedRunsOnEveryBackend(result.CombinedIR!, "shown");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestCase("Sub Main()\n Hidden()\nEnd Sub\n", TestName = "{m}(bare, no Import)")]
    [TestCase("Import Helpers\nSub Main()\n Hidden()\nEnd Sub\n", TestName = "{m}(bare, Import)")]
    [TestCase("Import Helpers\nSub Main()\n Helpers.Hidden()\nEnd Sub\n", TestName = "{m}(qualified)")]
    public void AModFilePrivateSub_IsRefused(string main)
    {
        var dir = TempDir();
        try
        {
            var result = CompileFiles(dir,
                ("Helpers.mod", "Private Sub Hidden()\n PrintLine(\"hidden\")\nEnd Sub\n"),
                ("Main.bas", main));
            var messages = string.Join(" | ", result.AllErrors.Select(e => e.Message));
            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True, "must be refused");
                Assert.That(messages, Does.Contain("'Hidden' is Private to module 'Helpers' and cannot be accessed from here"));
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------ the IDE's channel

    private static SemanticAnalyzer AnalyzeWithIdeSymbols(string siblingFile, string siblingSource, string mainSource, out bool ok)
    {
        var table = new ProjectSymbolTable();
        LspModuleSymbolCollector.Collect(Parse(siblingSource), Path.Combine(Path.GetTempPath(), siblingFile), table);
        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureProjectSymbols(table, "Main");
        ok = analyzer.Analyze(Parse(mainSource));
        return analyzer;
    }

    /// <summary>
    /// The IDE resolves siblings through <see cref="ProjectSymbolTable"/>, filled by the LSP's
    /// collector — which used to promote every <c>.mod</c> procedure to Public, a workaround
    /// for the parser's default that would now hide the user's own <c>Private</c>. Same rule,
    /// same message, in the editor.
    /// </summary>
    [TestCase("Helpers.mod", "Module Helpers\nPrivate Sub Hidden()\nEnd Sub\nEnd Module\n", "Import Helpers\nSub Main()\n Hidden()\nEnd Sub\n", TestName = "{m}(.mod, bare Sub)")]
    [TestCase("Helpers.mod", "Module Helpers\nPrivate Sub Hidden()\nEnd Sub\nEnd Module\n", "Sub Main()\n Helpers.Hidden()\nEnd Sub\n", TestName = "{m}(.mod, qualified Sub)")]
    [TestCase("Helpers.mod", "Module Helpers\nPrivate Function Hidden() As Integer\n Return 1\nEnd Function\nEnd Module\n", "Sub Main()\n PrintLine(CStr(Helpers.Hidden()))\nEnd Sub\n", TestName = "{m}(.mod, qualified Function)")]
    [TestCase("Helpers.bas", "Module Helpers\nPrivate Function Hidden() As Integer\n Return 1\nEnd Function\nEnd Module\n", "Sub Main()\n PrintLine(CStr(Helpers.Hidden()))\nEnd Sub\n", TestName = "{m}(.bas, qualified)")]
    public void TheIdeSymbolTable_RefusesAPrivateProcedure(string siblingFile, string sibling, string main)
    {
        var analyzer = AnalyzeWithIdeSymbols(siblingFile, sibling, main, out var ok);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(Errors(analyzer), Does.Contain("'Hidden' is Private to module 'Helpers' and cannot be accessed from here"));
        });
    }

    [TestCase("Helpers.mod", "Module Helpers\nSub Shown()\nEnd Sub\nEnd Module\n", "Import Helpers\nSub Main()\n Shown()\nEnd Sub\n", TestName = "{m}(.mod, bare)")]
    [TestCase("Helpers.bas", "Module Helpers\nFunction Twice(n As Integer) As Integer\n Return n * 2\nEnd Function\nEnd Module\n", "Sub Main()\n PrintLine(CStr(Helpers.Twice(4)))\nEnd Sub\n", TestName = "{m}(.bas, qualified)")]
    public void TheIdeSymbolTable_AcceptsANoModifierProcedure(string siblingFile, string sibling, string main)
    {
        var analyzer = AnalyzeWithIdeSymbols(siblingFile, sibling, main, out var ok);
        Assert.That(ok, Is.True, Errors(analyzer));
    }
}
