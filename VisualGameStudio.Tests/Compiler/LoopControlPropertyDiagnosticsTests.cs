using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #221 — VB's BC30039, "Loop control variable cannot be a property or a late-bound indexed array.": a counted `For` with no `As` whose control name binds to a PROPERTY is refused
//  (`SemanticAnalyzer.RefuseLoopControlProperty`). Before, a ReadWrite property was DRIVEN by the loop (R1 / R2 / R5 printed 1..4 on every backend), a ReadOnly one was BC30526 and a WriteOnly
//  one BC30524; vbc reports BC30039 ALONE for all of them. The `For Each` refusal of a property (ADR-0009) carries the same code and message now:
//  `ForEachControlVariableDiagnosticsTests.G10_PropertyControlVariable_IsRefused`.
//
//  Fast: no process. Each program is parsed and analyzed (`Analyze`) AND taken through `BasicCompiler.CompileProjectFiles` (`ViaProject`, what a .blproj build and the IDE call; it stops at the IR).
//  No codegen changed (the byte compare over the t124 corpora is identical wherever a program still compiles), so there is no execution fixture: every accepted shape runs as it ran before.
//
//  ORACLE: vbc, the SDK's, on the program wrapped in a VB Module (`S/t136/tools/vbv2.py`) — the implementer's `S/t221/probes`: R1 R2 R5 R7 R8 are BC30039 at the `For` line; A1 A2 A7 A8 run and
//  print 4, 4, "4 | 0" and 4. Decided by the symbol the name BINDS to, never its spelling: `For F = …` in `Function F` (#219) and `For P = …` in P's own `Get` bind to the implicit return variable,
//  a Local; a field or a local spelled like a property is found first by lookup.
//
//  MOVED HERE: `PropertyAccessExecutionTests.BareForLoop_OverAReadWriteProperty_FrontEndAccepts_PinsPreExistingGap_Against221` (the R1 row) and
//  `PropertyAccessDiagnosticsTests.Write_BareForLoop_OverAReadOnlyProperty_IsRefused` (the R7 row, BC30526 before).
//
//  MUTANTS (each killed by a row below): M1 the check off (R rows); M2 the check also fires on a FIELD (`Field`); M3a it also fires when the name binds to a Function's return variable
//  (`FunctionReturnVariable`); M3b it decides by what lookup FOUND, so it fires in a property's own Get (`GetReturnVariable`).
//
//  NOT pinned (BasicLang's parser takes a bare name after `For`): `For Me.P = …` / `For o.P = …` (vbc BC30039) and `For Me.x = …` / `For o.x = …` over a field (vbc runs, 4) are all parse errors,
//  before and after. `For Each P In xs` in P's own Get is refused (BC30039 now; vbc binds the return variable and prints the last element), and `For Each F In xs` in `Function F` declares a new
//  `F` and returns 0 (vbc 8) — the `For Each` site never asks `AsReturnVariable`, a #219 gap.
// ================================================================================================

[TestFixture]
public class LoopControlPropertyDiagnosticsTests
{
    private const string BC30039 = "BC30039";
    private const string VbcMessage = "BC30039: Loop control variable cannot be a property or a late-bound indexed array.";

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a refusal) and analyze; every diagnostic, in the analyzer's order.</summary>
    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    /// <summary><c>BasicCompiler.CompileProjectFiles</c> (aggressive), in process: what a Release .blproj build and the IDE call.</summary>
    private static List<SemanticError> ViaProject(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t221-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            return new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path }).AllErrors.ToList();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static string Said(List<SemanticError> errors) => string.Join(" | ", errors.Select(e => $"{e.Line}: {e.ErrorCode} {e.Message}"));

    // ---- refused: the source and the 1-based line vbc reports BC30039 on (the `For`) ------------------------------------------------------------------------------------------------------

    /// <summary>R1 — an auto-property, driven bare from its own class (moved from PropertyAccessExecutionTests' #221 pin: it printed 1 2 3 4).</summary>
    private const string AutoProperty = """
        Class C
            Public Property P As Integer
            Public Sub Run()
                For P = 1 To 3
                    Console.WriteLine(P)
                Next
                Console.WriteLine(P)
            End Sub
        End Class
        Sub Main()
            Dim c As C = New C()
            c.Run()
        End Sub
        """;

    /// <summary>R2 — a Get/Set property: the loop called the setter (set 1 .. set 4) on C#, JavaScript and MSIL, and did not compile on C++.</summary>
    private const string GetSetProperty = """
        Class C
            Private _p As Integer
            Public Property P As Integer
                Get
                    Return _p
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _p = value
                End Set
            End Property
            Public Sub Run()
                For P = 1 To 3
                Next
                Console.WriteLine(P)
            End Sub
        End Class
        Sub Main()
            Dim c As C = New C()
            c.Run()
        End Sub
        """;

    /// <summary>R5 — a Shared property, from a Shared Sub of its class.</summary>
    private const string SharedProperty = """
        Class C
            Public Shared Property SP As Integer
            Public Shared Sub Run()
                For SP = 1 To 3
                Next
                Console.WriteLine(SP)
            End Sub
        End Class
        Sub Main()
            C.Run()
        End Sub
        """;

    /// <summary>R7 — a ReadOnly property: BC30526 before (moved from PropertyAccessDiagnosticsTests), BC30039 alone in vbc.</summary>
    private const string ReadOnlyProperty = """
        Class C
            Private _p As Integer
            Public ReadOnly Property P As Integer
                Get
                    Return _p
                End Get
            End Property
            Public Sub Run()
                For P = 1 To 3
                Next
                Console.WriteLine(P)
            End Sub
        End Class
        Sub Main()
            Dim c As C = New C()
            c.Run()
        End Sub
        """;

    /// <summary>R8 — a WriteOnly property: BC30524 before (the loop reads its control variable), BC30039 alone in vbc.</summary>
    private const string WriteOnlyProperty = """
        Class C
            Private _p As Integer
            Public WriteOnly Property P As Integer
                Set(value As Integer)
                    _p = value
                End Set
            End Property
            Public Sub Run()
                For P = 1 To 3
                Next
                Console.WriteLine(_p)
            End Sub
        End Class
        Sub Main()
            Dim c As C = New C()
            c.Run()
        End Sub
        """;

    [TestCase(AutoProperty, 4, TestName = "ACountedFor_OverAnAutoProperty_IsBC30039")]
    [TestCase(GetSetProperty, 13, TestName = "ACountedFor_OverAGetSetProperty_IsBC30039")]
    [TestCase(SharedProperty, 4, TestName = "ACountedFor_OverASharedProperty_IsBC30039")]
    [TestCase(ReadOnlyProperty, 9, TestName = "ACountedFor_OverAReadOnlyProperty_IsBC30039_NotBC30526")]
    [TestCase(WriteOnlyProperty, 9, TestName = "ACountedFor_OverAWriteOnlyProperty_IsBC30039_NotBC30524")]
    public void ACountedFor_OverAProperty_IsVbsBC30039Alone(string source, int line)
    {
        var analyzed = Analyze(source);
        var project = ViaProject(source);
        Assert.Multiple(() =>
        {
            Assert.That(analyzed.Select(e => (e.ErrorCode, e.Line)), Is.EqualTo(new[] { (BC30039, line) }), "analyzer, exactly one diagnostic: " + Said(analyzed));
            Assert.That(analyzed.Select(e => e.Message), Has.All.Contains(VbcMessage), "vbc's message: " + Said(analyzed));
            Assert.That(project.Where(e => e.ErrorCode == BC30039).Select(e => e.Line), Is.EqualTo(new[] { line }), "CompileProjectFiles: " + Said(project));
            Assert.That(project.Where(e => e.ErrorCode is "BC30526" or "BC30524"), Is.Empty, "CompileProjectFiles, no BC30526 / BC30524: " + Said(project));
        });
    }

    // ---- accepted: vbc compiles and runs every one ----------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A1 — #219: `F` in `Function F` is the implicit return variable, a Local; the loop drives it (vbc 4).</summary>
    private const string FunctionReturnVariable = """
        Function F() As Integer
            For F = 1 To 3
            Next
        End Function
        Sub Main()
            Console.WriteLine(F())
        End Sub
        """;

    /// <summary>A8 — #219: `P` in P's own Get is the Get's implicit return variable, though lookup finds the PROPERTY first (vbc 4).</summary>
    private const string GetReturnVariable = """
        Class C
            Public ReadOnly Property P As Integer
                Get
                    For P = 1 To 3
                    Next
                End Get
            End Property
        End Class
        Sub Main()
            Dim c As C = New C()
            Console.WriteLine(c.P)
        End Sub
        """;

    /// <summary>A2 — a field of the class, driven bare (vbc 4).</summary>
    private const string Field = """
        Class C
            Public x As Integer
            Public Sub Run()
                For x = 1 To 3
                Next
                Console.WriteLine(x)
            End Sub
        End Class
        Sub Main()
            Dim c As C = New C()
            c.Run()
        End Sub
        """;

    /// <summary>A7 — a LOCAL spelled exactly like the class's property: lookup finds the local, so the loop drives it and the property stays 0 (vbc "4 | 0").</summary>
    private const string LocalSpelledLikeTheProperty = """
        Class C
            Public Property P As Integer
            Public Sub Run()
                Dim P As Integer
                For P = 1 To 3
                Next
                Console.WriteLine(P)
                Console.WriteLine(Me.P)
            End Sub
        End Class
        Sub Main()
            Dim c As C = New C()
            c.Run()
        End Sub
        """;

    [TestCase(FunctionReturnVariable, TestName = "ACountedFor_OverAFunctionsReturnVariable_CompilesClean")]
    [TestCase(GetReturnVariable, TestName = "ACountedFor_OverAGetsReturnVariable_CompilesClean")]
    [TestCase(Field, TestName = "ACountedFor_OverAField_CompilesClean")]
    [TestCase(LocalSpelledLikeTheProperty, TestName = "ACountedFor_OverALocalSpelledLikeAProperty_CompilesClean")]
    public void ACountedFor_OverStorage_CompilesClean(string source)
    {
        var analyzed = Analyze(source);
        var project = ViaProject(source);
        Assert.Multiple(() =>
        {
            Assert.That(analyzed, Is.Empty, "analyzer: " + Said(analyzed));
            Assert.That(project, Is.Empty, "CompileProjectFiles: " + Said(project));
        });
    }
}
