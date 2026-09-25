using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Calls to a <c>Module</c>'s procedures from outside it — qualified (<c>Helpers.Twice(4)</c>)
/// and bare — on every backend.
///
/// <para>⛔ A QUALIFIED CALL LOWERED TO AN INSTANCE CALL ON A PHANTOM RECEIVER. The analyzer
/// resolved <c>Helpers</c> to its Module symbol (no type), typed the access Object, and the IR
/// builder's static-vs-instance heuristic — "is the receiver's name exactly a class?" — said
/// instance. So <c>t0 = Helpers.Twice(4);</c> on C++ ("'Helpers' was not declared"),
/// <c>ReferenceError</c> on JavaScript, <c>callvirt ... System.Object::'Twice'</c>
/// (MissingMethodException) on MSIL, and 8 on C# by re-emitting the text. Every shape — Function,
/// Sub, self-qualified, nested, from file scope, with ByRef, with Optional — and the multi-file
/// path identically.</para>
///
/// <para>⛔ THE BARE FORM HAD THE MIRROR DEFECT ON C#. This backend emits one static class per
/// Module and qualified cross-module VARIABLES but never CALLS, so <c>Twice(4)</c> from Module M
/// was emitted bare inside <c>static class M</c>: CS0103, on C# alone. No single call form ran on
/// all four backends.</para>
///
/// <para>⛔ TWO MODULES WITH THE SAME PROCEDURE NAME LOST ONE OF THEM, SILENTLY. Pass 1 flattens
/// procedure signatures into the global scope first-wins, so B's <c>F</c> had no symbol; a bare
/// <c>F()</c> from a third module bound to A's; and <c>CombineIRModules</c> — which the
/// single-file CLI path also goes through — deduplicated IR functions by bare name and dropped
/// B's <c>F</c> from the output, body and all, from a build that reported success. Three
/// backends printed A's value for <c>B.F()</c>'s caller; C# emitted no class B at all.</para>
///
/// <para>⚠ The fix mirrors the variables one: the analyzer records every Module's procedures as
/// its members (pass 1, always — not only when the global scope was free) and resolves
/// <c>Module.Proc</c> to them; a bare call prefers the enclosing Module's own procedure whatever
/// the declaration order and is refused as ambiguous when two OTHER modules declare it; the IR
/// builder lowers every module-procedure call — same unit, other module, other FILE — through one
/// path, under a bare or owner-qualified IR name with the owner carried on
/// <c>IRCall.CalleeModule</c>; contested names are spelled <c>A_F</c> / <c>B_F</c> up front; C#
/// qualifies from the stamp; and the cross-file combine step REFUSES a collision instead of
/// dropping it.</para>
///
/// <para>⚠ Every running case asserts all FOUR backends. A write-then-read-back is avoided
/// wherever it would prove nothing (see <c>ModuleMemberAccessTests</c>).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ModuleProcedureCallTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);

    private static SemanticAnalyzer Analyze(string source, out bool ok)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        ok = analyzer.Analyze(ast);
        return analyzer;
    }

    private static string Errors(SemanticAnalyzer a) => string.Join(" | ", a.Errors.Select(e => e.Message));

    private static IRModule BuildIr(string source)
    {
        // One AST for both passes: node symbols are keyed by node identity, so an IR built from
        // a second parse of the same text sees none of them (measured — every call degraded).
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, Errors(analyzer));
        return new IRBuilder(analyzer).Build(ast, "T");
    }

    // ------------------------------------------------------------------ qualified calls

    /// <summary>⛔ The headline: three of four backends could not run this.</summary>
    [Test]
    public void AQualifiedFunctionCall_RunsOnEveryBackend() => RunsOnEveryBackend("""
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        Module M
         Sub Main()
          PrintLine(CStr(Helpers.Twice(4)))
         End Sub
        End Module
        """, "8");

    /// <summary>A Sub, as a statement, observed through the module variable it bumps.</summary>
    [Test]
    public void AQualifiedSubCall_RunsAsAStatement() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 1
         Public Sub Bump()
          Value = Value + 5
         End Sub
        End Module
        Module M
         Sub Main()
          Helpers.Bump()
          PrintLine(CStr(Helpers.Value))
         End Sub
        End Module
        """, "6");

    [Test]
    public void AModuleQualifyingItsOwnProcedure_Works() => RunsOnEveryBackend("""
        Module M
         Function Own() As Integer
          Return 9
         End Function
         Sub Main()
          PrintLine(CStr(M.Own()))
         End Sub
        End Module
        """, "9");

    /// <summary>
    /// ⛔ With the declaring module SECOND, MSIL did not even assemble before: the phantom
    /// receiver had no type at all, so the call named an undefined class <c>'Helpers'</c>.
    /// </summary>
    [Test]
    public void TheUsingModuleMayPrecedeTheDeclaringOne() => RunsOnEveryBackend("""
        Module M
         Sub Main()
          PrintLine(CStr(Helpers.Twice(4)))
         End Sub
        End Module
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        """, "8");

    [Test]
    public void ANestedQualifiedCall_Works() => RunsOnEveryBackend("""
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        Sub Main()
         PrintLine(CStr(Helpers.Twice(Helpers.Twice(2))))
        End Sub
        """, "8");

    [Test]
    public void AQualifiedCallFromFileScope_Works() => RunsOnEveryBackend("""
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        Sub Main()
         PrintLine(CStr(Helpers.Twice(4)))
        End Sub
        """, "8");

    /// <summary>
    /// The shape <c>ModuleMemberAccessTests</c> could not use: a qualified WRITE read back
    /// through a procedure of the SAME module. Both halves now run.
    /// </summary>
    [Test]
    public void AQualifiedWrite_ReadBackThroughTheModulesOwnProcedure() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 11
         Public Function Peek() As Integer
          Return Value
         End Function
        End Module
        Sub Main()
         Helpers.Value = 13
         PrintLine(CStr(Helpers.Peek()))
        End Sub
        """, "13");

    /// <summary>
    /// An omitted Optional is filled from the declaration — the qualified call goes through the
    /// same emission as a bare one, so it cannot lose what a bare one has. (C# printed 6 before
    /// only because csc filled the default itself.)
    /// </summary>
    [Test]
    public void AnOmittedOptional_IsFilledThroughAQualifiedCall() => RunsOnEveryBackend("""
        Module Helpers
         Public Function Add(a As Integer, Optional b As Integer = 5) As Integer
          Return a + b
         End Function
        End Module
        Sub Main()
         PrintLine(CStr(Helpers.Add(1)))
        End Sub
        """, "6");

    /// <summary>
    /// ⛔ ByRef through a qualified call was lost on EVERY backend — the instance arm never reads
    /// the declaration's ByRef markers — and C# said so: CS1620, "must be passed with 'ref'".
    /// Runs on C++ and C# now. ⛔ PIN CLOSED: MSIL also now runs and agrees at 5 — it used to fail
    /// every ByRef call with <c>InvalidProgramException</c>, a pre-existing gap (see
    /// <c>MsilByRefTests</c> for the family that closed it), pinned here rather than normalised
    /// away. JavaScript still refuses ByRef by design (<c>BL7002</c>), asserted as it is.
    /// </summary>
    [Test]
    public void ByRef_ThroughAQualifiedCall_IsMarked()
    {
        const string program = """
            Module Helpers
             Public Sub Inc(ByRef n As Integer)
              n = n + 1
             End Sub
            End Module
            Sub Main()
             Dim x As Integer = 4
             Helpers.Inc(x)
             PrintLine(CStr(x))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo("5"), "C++");
            Assert.That(Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo("5"), "C#");
            Assert.That(() => JavaScriptExecutionTests.RunJs(program),
                Throws.Exception.With.Message.Contains("ByRef"), "JavaScript refuses ByRef by design");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("5"), "MSIL");
        });
    }

    // ------------------------------------------------------------------ bare cross-module calls

    /// <summary>⛔ The C# mirror: CS0103 there and there alone. Now all four.</summary>
    [Test]
    public void ABareCrossModuleFunctionCall_RunsOnEveryBackend_IncludingCSharp() => RunsOnEveryBackend("""
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        Module M
         Sub Main()
          PrintLine(CStr(Twice(4)))
         End Sub
        End Module
        """, "8");

    [Test]
    public void ABareCrossModuleSubCall_RunsOnEveryBackend() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 1
         Public Sub Bump()
          Value = Value + 5
         End Sub
        End Module
        Module M
         Sub Main()
          Bump()
          PrintLine(CStr(Helpers.Value))
         End Sub
        End Module
        """, "6");

    /// <summary>
    /// A Module literally named <c>Main</c> is emitted as <c>Program</c> on C#, and a call into it
    /// must be spelled that way too — the one module name the qualification cannot use verbatim.
    /// Calls in both directions.
    /// </summary>
    [Test]
    public void AModuleNamedMain_IsQualifiedAsProgram_InBothDirections() => RunsOnEveryBackend("""
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return Main.Base() * n
         End Function
        End Module
        Module Main
         Public Function Base() As Integer
          Return 3
         End Function
         Sub Main()
          PrintLine(CStr(Helpers.Twice(4)))
          PrintLine(CStr(Twice(5)))
         End Sub
        End Module
        """, "12\n15");

    // ------------------------------------------------------------------ two modules, one name

    /// <summary>
    /// ⛔ <c>B.F()</c> could not resolve, and B's <c>F</c> was not even in the output. Both now:
    /// spelled <c>A_F</c> / <c>B_F</c> in the IR, so no backend's by-name table can merge them.
    /// </summary>
    [Test]
    public void TwoModulesWithTheSameProcedureName_StayDistinct_OnEveryBackend() => RunsOnEveryBackend("""
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
        Sub Main()
         PrintLine(CStr(A.F()))
         PrintLine(CStr(B.F()))
        End Sub
        """, "1\n2");

    /// <summary>
    /// ⛔ Printed A's 1 on three backends before — first-wins, silently. Refused now.
    /// </summary>
    [Test]
    public void AnAmbiguousBareProcedureName_IsRefused()
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
            Sub Main()
             PrintLine(CStr(F()))
            End Sub
            """, out var ok);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, "must be refused");
            Assert.That(Errors(analyzer), Does.Contain("'F' is ambiguous between modules 'A', 'B'"));
        });
    }

    /// <summary>
    /// ⛔ Inside Module A, a bare call to A's OWN <c>F</c> written ABOVE its declaration bound to
    /// B's — pass 2 defines A's copy in A's scope only when it reaches the declaration, and the
    /// global scope held whichever pass 1 saw first. The enclosing module's own procedure wins
    /// now, whatever the order, and there is no ambiguity error for it.
    /// </summary>
    [Test]
    public void AModulesOwnProcedure_WinsFromAboveItsDeclaration() => RunsOnEveryBackend("""
        Module B
         Public Function F() As Integer
          Return 2
         End Function
        End Module
        Module A
         Public Sub Go()
          PrintLine(CStr(F()))
         End Sub
         Public Function F() As Integer
          Return 1
         End Function
        End Module
        Sub Main()
         A.Go()
        End Sub
        """, "1");

    [Test]
    public void ContestedProcedureNames_AreOwnerQualifiedInTheIr_AndUncontestedOnesStayBare()
    {
        var ir = BuildIr("""
            Module A
             Public Function F() As Integer
              Return 1
             End Function
             Public Function Only() As Integer
              Return 3
             End Function
            End Module
            Module B
             Public Function F() As Integer
              Return 2
             End Function
            End Module
            Sub Main()
             PrintLine(CStr(A.F() + B.F() + Only()))
            End Sub
            """);
        var names = ir.Functions.Select(f => f.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("A_F").And.Contain("B_F"), "contested: spelled by owner");
            Assert.That(names, Does.Contain("Only"), "uncontested: bare");
            Assert.That(names.Count(n => n == "F"), Is.EqualTo(0), "no bare F survives to be merged");
            var calls = ir.Functions.Single(f => f.Name == "Main").Blocks
                .SelectMany(b => b.Instructions).OfType<IRCall>().ToList();
            Assert.That(calls.Select(c => c.FunctionName), Is.SupersetOf(new[] { "A_F", "B_F", "Only" }));
            Assert.That(calls.Single(c => c.FunctionName == "A_F").CalleeModule, Is.EqualTo("A"),
                "the owner rides beside the bare/qualified IR name for the one backend that needs it");
        });
    }

    /// <summary>
    /// ⛔ A call a Module makes to its OWN procedure below the declaration must see the fully
    /// typed pass-2 symbol, not pass 1's stand-in: pass 1 types a RETURN type by bare name, so
    /// <c>Task(Of Integer)</c> is looked up as "Task" and lands on Object. Removing the pass-2
    /// re-record was measured "unobservable" on PARAMETER types alone — and the full suite then
    /// refused <c>Dim t As Task(Of Integer) = GetCount()</c> with "Cannot assign value of type
    /// 'Object' to variable of type 'Task'" (TaskResultTests). This is that shape, in the
    /// fixture that owns the mechanism. Analyzer-level on purpose: a module function RETURNING
    /// a collection is its own pre-existing gap (MSIL does not assemble it, C++ prints nothing),
    /// and a running case here would be measuring that, not this.
    /// </summary>
    [Test]
    public void AGenericReturnType_ReachesACallBelowTheDeclaration_InTheSameModule()
    {
        Analyze("""
            Module M
             Async Function GetCount() As Task(Of Integer)
              Return 5
             End Function
             Sub Run()
              Dim t As Task(Of Integer) = GetCount()
             End Sub
            End Module
            Sub Main()
            End Sub
            """, out var ok);
        Assert.That(ok, Is.True, "the pass-2 symbol's return type must reach the call");
    }

    // ------------------------------------------------------------------ guards and pins

    /// <summary>A local INSTANCE named after the module is the receiver, not the module.</summary>
    [Test]
    public void ALocalInstanceShadowingTheModuleName_Wins() => RunsOnEveryBackend("""
        Class Box
         Public Function Twice(n As Integer) As Integer
          Return n * 3
         End Function
        End Class
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        Sub Main()
         Dim Helpers As New Box()
         PrintLine(CStr(Helpers.Twice(4)))
        End Sub
        """, "12");

    /// <summary>
    /// A module procedure with NO modifier, called from another module. ⛔ This was a PINNED
    /// DIVERGENCE: the parser defaulted it to <c>Private</c> — the opposite of the language —
    /// and nothing but csc enforced that, so C++, JavaScript and MSIL printed 8 while C#
    /// emitted <c>private static</c> and refused the call (CS0122). The parser now says
    /// Public, as VB does, and this is the promoted four-backend case;
    /// <see cref="ModuleProcedureAccessTests"/> holds the rest of the access surface.
    /// </summary>
    [Test]
    public void ANoModifierModuleFunction_RunsOnEveryBackend()
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
             End Sub
            End Module
            """;
        FourBackends.RunsOnEveryBackend(program, "8");
        Assert.That(ReturnCoercionTests.EmitCSharpForTest(program), Does.Contain("public static int Twice"),
            "the language's default, spelled by C#");
    }

    /// <summary>
    /// A CLASS method calling a module procedure by bare name and qualified. ⛔ This was PINNED
    /// as a C++ ordering gap: the front end used to refuse the whole program ("Cannot return
    /// type 'Object'"), then resolved it — and C++ emitted the class body BEFORE the
    /// free-function prototypes, so <c>Twice</c> was "use of undeclared identifier" there
    /// alone. The prototypes now precede the classes; this is the promoted four-backend case,
    /// and <see cref="CppEmissionOrderTests"/> holds every other shape of it.
    /// </summary>
    [Test]
    public void AClassMethodCallingAModuleProcedure_RunsOnEveryBackend()
    {
        const string program = """
            Module Helpers
             Public Function Twice(n As Integer) As Integer
              Return n * 2
             End Function
            End Module
            Class Box
             Public Function Run() As Integer
              Return Twice(4)
             End Function
             Public Function RunQ() As Integer
              Return Helpers.Twice(5)
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
             PrintLine(CStr(b.RunQ()))
            End Sub
            """;
        FourBackends.RunsOnEveryBackend(program, "8\n10");
        Assert.That(BclE2E.CompileToCppOptimized(program), Does.Match(@"=\s*Twice\(4\);"),
            "the call lowers to the free function, which the prototype above the class declares");
    }

    // ------------------------------------------------------------------ multi-file

    private static string TempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "BasicLang_ModuleCall_" + Path.GetRandomFileName())).FullName;

    private static IRModule Optimized(IRModule ir)
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(ir);
        return ir;
    }

    /// <summary>
    /// ⛔ THE MULTI-FILE PATH WAS BROKEN ON THREE BACKENDS TOO, in two ways. The qualified call
    /// was the same phantom instance call. The BARE imported call was spelled as the dotted
    /// <c>"Helpers.Twice"</c> IR name, which C++ alone stripped back to the flattened function:
    /// JavaScript refused it ("no lowering for 'Helpers.Twice'") and MSIL sanitised the dot away
    /// into <c>Combined::HelpersTwice</c>, a method nothing defines. Both forms now go out bare
    /// with the owner beside them, and all four backends run the combined program.
    /// </summary>
    [Test]
    public void AMultiFileProject_CallsAnImportedProcedure_QualifiedAndBare_OnEveryBackend()
    {
        var dir = TempDir();
        try
        {
            var helpers = Path.Combine(dir, "Helpers.bas");
            var main = Path.Combine(dir, "Main.bas");
            File.WriteAllText(helpers, "Module Helpers\n Public Function Twice(n As Integer) As Integer\n  Return n * 2\n End Function\nEnd Module\n");
            File.WriteAllText(main, "Import Helpers\nModule M\n Sub Main()\n  PrintLine(CStr(Helpers.Twice(4)))\n  PrintLine(CStr(Twice(5)))\n End Sub\nEnd Module\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { helpers, main });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            var ir = Optimized(result.CombinedIR!);

            Assert.Multiple(() =>
            {
                var cs = new CSharpCodeGenerator().Generate(ir);
                Assert.That(cs, Does.Contain("Helpers.Twice(4)").And.Contain("Helpers.Twice(5)"), "C# qualifies both from CalleeModule");
                Assert.That(Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("8\n10"), "C#");

                var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir);
                Assert.That(Norm(BclE2E.CompileRun(cpp)), Is.EqualTo("8\n10"), "C++");

                var js = new JavaScriptCodeGenerator().Generate(ir);
                Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo("8\n10"), "JavaScript");

                var il = new MSILCodeGenerator().Generate(ir);
                Assert.That(il, Does.Not.Contain("HelpersTwice").And.Not.Contain("System.Object::'Twice'"),
                    "MSIL: neither the sanitised-dot name nor the phantom receiver");
                Assert.That(System.Text.RegularExpressions.Regex.Matches(il, @"call int32 'Combined'::'Twice'\(int32\)").Count, Is.EqualTo(2),
                    "MSIL: both calls land on the real method");
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// ⛔ Two FILES each declaring the same module procedure met only in <c>CombineIRModules</c>,
    /// where a first-wins name check dropped the second — body and all — from a build that
    /// reported success. Within one unit the IR builder names them apart; across files no backend
    /// can carry two functions of one bare name, so it is refused, naming both.
    /// </summary>
    [Test]
    public void ACrossFileProcedureCollision_IsRefused_NotSilentlyDropped()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "Alpha.bas");
            var b = Path.Combine(dir, "Beta.bas");
            File.WriteAllText(a, "Module A\n Public Function F() As Integer\n  Return 1\n End Function\nEnd Module\nSub Main()\n PrintLine(CStr(A.F()))\nEnd Sub\n");
            File.WriteAllText(b, "Module B\n Public Function F() As Integer\n  Return 2\n End Function\nEnd Module\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { a, b });
            var messages = string.Join(" | ", result.AllErrors.Select(e => e.Message));
            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True, "must be refused");
                Assert.That(messages, Does.Contain("Procedure 'F'").And.Contain("'A'").And.Contain("'B'"));
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// A class METHOD in the first file and a module FUNCTION of the same name in the second.
    /// The second unit's function looks for an earlier same-named function to collide with, and
    /// must skip the member body — that is what the exemption in the lookup is for. ⛔ Found by a
    /// surviving mutation: the method-vs-method case below never reaches that lookup at all,
    /// because a member body is added before it and cannot collide with anything.
    /// </summary>
    [Test]
    public void ACrossFileMethodThenModuleFunction_OfTheSameName_IsAllowed()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "Alpha.bas");
            var b = Path.Combine(dir, "Beta.bas");
            File.WriteAllText(a, "Class P\n Public Function Handle() As Integer\n  Return 1\n End Function\nEnd Class\nSub Main()\n Dim p As New P()\n PrintLine(CStr(p.Handle()))\nEnd Sub\n");
            File.WriteAllText(b, "Module B\n Public Function Handle() As Integer\n  Return 2\n End Function\nEnd Module\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { a, b });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Same-named METHODS of different classes across files stay exempt from that refusal.</summary>
    [Test]
    public void ACrossFileClassMethodCollision_IsStillAllowed()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "Alpha.bas");
            var b = Path.Combine(dir, "Beta.bas");
            File.WriteAllText(a, "Class P\n Public Function Handle() As Integer\n  Return 1\n End Function\nEnd Class\nSub Main()\n Dim p As New P()\n PrintLine(CStr(p.Handle()))\nEnd Sub\n");
            File.WriteAllText(b, "Class Q\n Public Function Handle() As Integer\n  Return 2\n End Function\nEnd Class\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { a, b });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
