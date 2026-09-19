using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.MSIL;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A <c>Module</c>'s variables and constants reached from OUTSIDE the module — qualified
/// (<c>Helpers.Value</c>) or by bare name from another module — on every backend.
///
/// <para>⛔ THIS WAS NOT A BACKEND BUG, and it was found while chasing one. The C++ symptom
/// ("'Helpers' was not declared in this scope") was real, but so was JavaScript's
/// <c>ReferenceError: Helpers is not defined</c> and MSIL's <c>MissingFieldException: Field not
/// found: 'System.Object.Value'</c>. Only C# ran, and only because it re-emits the text and lets
/// csc resolve the name. The cause is the FRONT END: a Module's <c>Dim</c>/<c>Const</c> live in
/// the Module's own scope, which a sibling Module's lexical chain never reaches, and pass 1
/// registered only procedure signatures. So <c>Helpers.Value</c> fell through every channel to
/// the permissive "any PascalCase identifier could be a .NET type" fallback and was typed
/// Object, and the IR builder lowered it to a field read on a phantom variable named
/// <c>Helpers</c>.</para>
///
/// <para>⛔ THE UNQUALIFIED FORM WAS WORKING BY TWO COINCIDENCES. A bare cross-module
/// <c>Value</c> took the same fallback: typed as a phantom class named <c>Value</c>, no error,
/// and the IR builder minted a fresh LOCAL of that name. It printed the right number on C++ and
/// JavaScript only because the emitted bare global shared the name — and <c>Value + 1</c> was
/// refused as "requires numeric operands", and C# failed with CS0103 whenever the using module
/// came first.</para>
///
/// <para>⛔ TWO MODULES WITH THE SAME VARIABLE NAME WERE A SILENT WRONG ANSWER ON MSIL.
/// <c>A.GetA()</c> printed B's 2: the by-name field table kept the last one. C++ said
/// "redefinition", JavaScript refused; only MSIL was quiet. Such a global now gets an IR NAME
/// qualified by its owner (<c>A_Value</c>, <c>B_Value</c>), decided from the AST before any
/// declaration is lowered, so no backend's by-name table can merge them and nothing needs a
/// per-backend collision case — <see cref="ModuleGlobalCollisionTests"/> pins the shape.</para>
///
/// <para>⚠ Every running case asserts all FOUR backends, normalised, so "C++ now agrees with
/// the rest" is the property. The read-back for a WRITE goes through a file-scope function
/// that cannot see <c>Main</c>'s locals: a write that had landed on a fresh local named
/// <c>Value</c> would still read 13 back inside <c>Main</c> and prove nothing.</para>
///
/// <para>⚠ A qualified module CALL (<c>Helpers.Peek()</c>) is a SEPARATE gap and is NOT fixed
/// here — pinned at the end. It is why the read-backs use file-scope functions rather than a
/// <c>Peek</c> inside the module.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // RunEmittedCSharp redirects Console.Out
public class ModuleMemberAccessTests
{
    private static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();

    private static void RunsOnEveryBackend(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(Norm(RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
    }

    /// <summary>
    /// The emitted C#, compiled AND RUN in process through Roslyn: a console assembly emitted
    /// to memory, loaded, its entry point invoked with <c>Console.Out</c> captured.
    ///
    /// <para>⚠ Not <c>CliTestHarness.CompileRunCSharp</c>, which spawns <c>BasicLang.exe</c> — a
    /// Windows apphost that is not deployed on Linux, which is why every <c>_CSharp</c> row that
    /// uses it (18 of them) sits in this machine's baseline failure set. This fixture's C# leg
    /// must run wherever the other three do, so it takes the same in-process route
    /// <see cref="ReturnCoercionTests"/> already uses to compile, one step further. Each program
    /// loads as a fresh assembly, so one test's module globals cannot leak into the next.</para>
    /// </summary>
    private static string RunEmittedCSharp(string program)
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(program);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "ModuleMemberProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        Assert.That(emitted.Success, Is.True,
            "the emitted C# does not compile:\n" + string.Join("\n",
                emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))
            + "\n--- emitted ---\n" + csharp);

        var assembly = Assembly.Load(ms.ToArray());
        var entry = assembly.EntryPoint;
        Assert.That(entry, Is.Not.Null, "no entry point in the emitted program");

        var captured = new StringWriter();
        var original = Console.Out;
        Console.SetOut(captured);
        try
        {
            var args = entry!.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() };
            entry.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail("the emitted C# threw: " + (ex.InnerException ?? ex) + "\n--- emitted ---\n" + csharp);
        }
        finally
        {
            Console.SetOut(original);
        }
        return captured.ToString();
    }

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

    // ------------------------------------------------------------------ qualified access

    /// <summary>⛔ The headline: three of four backends could not run this.</summary>
    [Test]
    public void AQualifiedModuleVariableRead_RunsOnEveryBackend() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 11
        End Module
        Module M
         Sub Main()
          PrintLine(CStr(Helpers.Value))
         End Sub
        End Module
        """, "11");

    /// <summary>
    /// The write, read back from a scope that cannot see <c>Main</c>'s locals. C++ read the
    /// old form as "cannot use arrow operator on a type".
    /// </summary>
    [Test]
    public void AQualifiedModuleVariableWrite_LandsOnTheGlobal_ReadFromAnUnshadowedScope() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 11
        End Module
        Function PeekValue() As Integer
         Return Helpers.Value
        End Function
        Sub Main()
         Helpers.Value = 13
         PrintLine(CStr(PeekValue()))
        End Sub
        """, "13");

    /// <summary>
    /// ⛔ The compound form was REFUSED by the front end ("Arithmetic operator '+' requires
    /// numeric operands") — the Object typing, seen from the type checker. And once it resolved,
    /// C# failed it ALONE with CS0103: the result is renamed after its target and carried only
    /// the NAME, so the cross-module qualification that an IRVariable gets never reached the
    /// destination of <c>Value = Value + 1</c>. Both halves are asserted by running it.
    /// </summary>
    [Test]
    public void ACompoundQualifiedWrite_RunsOnEveryBackend() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 11
        End Module
        Function PeekValue() As Integer
         Return Helpers.Value
        End Function
        Sub Main()
         Helpers.Value = Helpers.Value + 1
         PrintLine(CStr(PeekValue()))
        End Sub
        """, "12");

    [Test]
    public void AQualifiedModuleConstant_IsReadAndTyped() => RunsOnEveryBackend("""
        Module Helpers
         Public Const K As Integer = 7
        End Module
        Sub Main()
         PrintLine(CStr(Helpers.K * 2))
        End Sub
        """, "14");

    [Test]
    public void AModuleQualifyingItsOwnVariable_Works() => RunsOnEveryBackend("""
        Module M
         Public X As Integer = 5
         Sub Main()
          PrintLine(CStr(M.X))
         End Sub
        End Module
        """, "5");

    [Test]
    public void AModuleArrayElement_ReadsAndWritesThroughTheModule() => RunsOnEveryBackend("""
        Module Helpers
         Public Items(3) As Integer
        End Module
        Function PeekItem() As Integer
         Return Helpers.Items(1)
        End Function
        Sub Main()
         Helpers.Items(1) = 7
         PrintLine(CStr(PeekItem()))
        End Sub
        """, "7");

    // ------------------------------------------------------------------ bare cross-module access

    /// <summary>
    /// ⛔ Was refused as "requires numeric operands": the bare name resolved to a PHANTOM
    /// .NET type called <c>Value</c>, not to the variable. That it printed 11 without the
    /// <c>+ 1</c> was the emitted global sharing the name, not resolution.
    /// </summary>
    [Test]
    public void AnUnqualifiedCrossModuleReference_IsTyped_NotAPhantomNetType() => RunsOnEveryBackend("""
        Module Helpers
         Public Value As Integer = 11
        End Module
        Module M
         Sub Main()
          PrintLine(CStr(Value + 1))
         End Sub
        End Module
        """, "12");

    /// <summary>
    /// ⛔ With the USING module first, C# failed the bare form with CS0103: the IR builder
    /// reached the reference before the declaration, found no global, and minted a local.
    /// Pass-1 registration makes both orders one case.
    /// </summary>
    [TestCase("Helpers.Value", TestName = "Qualified")]
    [TestCase("Value", TestName = "Bare")]
    public void TheUsingModuleMayPrecedeTheDeclaringOne(string reference) => RunsOnEveryBackend($"""
        Module M
         Sub Main()
          PrintLine(CStr({reference}))
         End Sub
        End Module
        Module Helpers
         Public Value As Integer = 11
        End Module
        """, "11");

    /// <summary>
    /// A module variable typed from its INITIALIZER, not a declaration. Pass 1 cannot type it
    /// (there is no type to resolve yet), so its stand-in is Object until pass 2 reaches the
    /// declaration and swaps in the real, inferred symbol. With the declaring module FIRST the
    /// swap has happened by the time the use is analysed, and <c>Value + 1</c> is 6.
    /// </summary>
    [Test]
    public void AnAutoTypedModuleVariable_IsTypedFromItsInitializer_WhenDeclaredFirst() => RunsOnEveryBackend("""
        Module Helpers
         Public Value = 5
        End Module
        Module M
         Sub Main()
          PrintLine(CStr(Helpers.Value + 1))
         End Sub
        End Module
        """, "6");

    /// <summary>
    /// ⛔ PINNED LIMITATION: the same variable USED before its module is declared is still
    /// Object at the use, because the inferred type only exists once pass 2 visits the
    /// initializer. A declared type (<c>As Integer</c>) has no such dependence — see
    /// <see cref="TheUsingModuleMayPrecedeTheDeclaringOne"/>. Refused with a message rather than
    /// miscompiled, which is the pre-change behaviour for this shape too.
    /// </summary>
    [Test]
    public void AnAutoTypedModuleVariable_UsedBeforeItsModule_IsStillObject_PinnedLimitation()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              PrintLine(CStr(Helpers.Value + 1))
             End Sub
            End Module
            Module Helpers
             Public Value = 5
            End Module
            """, out var ok);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(Errors(analyzer), Does.Contain("requires numeric operands"),
                "PINNED: if this passes analysis, inferred module types now flow across " +
                "declaration order — promote this to a running case");
        });
    }

    // ------------------------------------------------------------------ two modules, one name

    /// <summary>
    /// ⛔ MSIL printed <c>2,2</c> for the bare-name form of this before: one field, the last
    /// declaration's, read by both modules. C++ was "redefinition of 'int32_t Value'" and
    /// JavaScript refused. Written to and read from OUTSIDE both modules, so the qualified
    /// references are what keep them apart.
    /// </summary>
    [Test]
    public void TwoModulesWithTheSameVariableName_StayDistinct_OnEveryBackend() => RunsOnEveryBackend("""
        Module A
         Public Value As Integer = 1
        End Module
        Module B
         Public Value As Integer = 2
        End Module
        Function PeekA() As Integer
         Return A.Value
        End Function
        Function PeekB() As Integer
         Return B.Value
        End Function
        Sub Main()
         A.Value = A.Value + 10
         B.Value = B.Value + 20
         PrintLine(CStr(PeekA()))
         PrintLine(CStr(PeekB()))
        End Sub
        """, "11\n22");

    /// <summary>
    /// Each module writing ITS OWN colliding variable by bare name: <c>Value = Value + 10</c>
    /// inside <c>A</c> is a value the IR builder renames after the target, so the qualified IR
    /// name has to survive that rename on every backend's declared-name table — the path a
    /// per-backend collision fix would have had to thread the owner through, or lose the write.
    ///
    /// <para>⚠ Two program texts, because of a PRE-EXISTING split in how a module procedure is
    /// called from outside: C# needs <c>A.BumpA()</c> (a bare cross-module call is CS0103 there)
    /// while C++, JavaScript and MSIL run only the bare <c>BumpA()</c> (the qualified call is the
    /// separate gap pinned below). Neither call form is this change's; the writes are.</para>
    /// </summary>
    [Test]
    public void AModuleWritingItsOwnCollidingVariable_ByBareName_Works()
    {
        const string body = """
            Module A
             Public Value As Integer = 1
             Public Sub BumpA()
              Value = Value + 10
             End Sub
            End Module
            Module B
             Public Value As Integer = 2
             Public Sub BumpB()
              Value = Value + 20
             End Sub
            End Module
            Sub Main()
             {0}
             {1}
             PrintLine(CStr(A.Value))
             PrintLine(CStr(B.Value))
            End Sub
            """;
        var bareCalls = string.Format(body, "BumpA()", "BumpB()");
        var qualifiedCalls = string.Format(body, "A.BumpA()", "B.BumpB()");

        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(bareCalls))), Is.EqualTo("11\n22"), "C++");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(bareCalls)), Is.EqualTo("11\n22"), "JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(bareCalls)), Is.EqualTo("11\n22"), "MSIL");
            Assert.That(Norm(RunEmittedCSharp(qualifiedCalls)), Is.EqualTo("11\n22"), "C#");
        });
    }

    /// <summary>
    /// A FILE-SCOPE global whose name a Module also declares, referenced by bare name from
    /// file scope. The analyzer stamps no owner at file scope, so this is the one reference
    /// shape that still reaches <c>GetOrCreateVariable</c>'s bare-keyed global table — which
    /// holds whichever declaration came LAST. With the Module declared second, that is the
    /// Module's copy: <c>Scale = Scale + 10</c> in <c>Main</c> would silently bump
    /// <c>Beta.Scale</c> and leave the file's own untouched (12 / 12 instead of 11 / 2). The
    /// current-module lookup has to win before that table is consulted. Found by a surviving
    /// mutation; every other case here resolves through the owner-stamped path and could not
    /// see it.
    /// </summary>
    [Test]
    public void AFileScopeGlobal_CollidingWithAModules_StaysTheFilesOwn() => RunsOnEveryBackend("""
        Dim Scale As Integer = 1
        Module Beta
         Public Scale As Integer = 2
        End Module
        Sub Main()
         Scale = Scale + 10
         PrintLine(CStr(Scale))
         PrintLine(CStr(Beta.Scale))
        End Sub
        """, "11\n2");

    /// <summary>
    /// The Const twin of the case above, and it pins something older. A file-scope Const was
    /// never registered where <c>GetOrCreateVariable</c> looks (only <c>AddGlobalVariable</c>
    /// saw it), so a later reference minted a fresh LOCAL sharing its name — which "worked" only
    /// because every backend spelled the two identically. Once a colliding Const is renamed
    /// <c>Beta_Scale</c>, that fresh local is a bare <c>Scale</c> that exists nowhere. Found by a
    /// surviving mutation, like its twin.
    /// </summary>
    [Test]
    public void AFileScopeConst_CollidingWithAModules_StaysTheFilesOwn() => RunsOnEveryBackend("""
        Const Scale As Integer = 1
        Module Beta
         Public Const Scale As Integer = 2
        End Module
        Sub Main()
         PrintLine(CStr(Scale))
         PrintLine(CStr(Beta.Scale))
        End Sub
        """, "1\n2");

    // ------------------------------------------------------------------ guards

    /// <summary>A local INSTANCE named after the module is the receiver, not the module.</summary>
    [Test]
    public void ALocalInstanceShadowingTheModuleName_Wins() => RunsOnEveryBackend("""
        Class Box
         Public Value As Integer = 5
        End Class
        Module Helpers
         Public Value As Integer = 11
        End Module
        Module M
         Sub Main()
          Dim Helpers As New Box()
          PrintLine(CStr(Helpers.Value))
         End Sub
        End Module
        """, "5");

    /// <summary>
    /// A module-level <c>Dim</c> is Private by default. Before, the qualified form was typed
    /// Object and the bare form was the phantom type — neither was an error, and on C++ and
    /// JavaScript the bare form quietly READ the private global. Both are refused now, naming
    /// the module.
    /// </summary>
    [TestCase("Helpers.Hidden", TestName = "Qualified")]
    [TestCase("Hidden", TestName = "Bare")]
    public void APrivateModuleVariable_IsRefused(string reference)
    {
        var analyzer = Analyze($"""
            Module Helpers
             Dim Hidden As Integer = 1
            End Module
            Module M
             Sub Main()
              PrintLine(CStr({reference}))
             End Sub
            End Module
            """, out var ok);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, "must be refused");
            Assert.That(Errors(analyzer), Does.Contain("'Hidden' is Private to module 'Helpers'"));
        });
    }

    /// <summary>
    /// A bare name two modules export is ambiguous and must be qualified. Before, it silently
    /// bound to whichever the backend happened to pick — the MSIL <c>2,2</c> shape.
    /// </summary>
    [Test]
    public void AnAmbiguousBareName_IsRefused()
    {
        var analyzer = Analyze("""
            Module A
             Public Value As Integer = 1
            End Module
            Module B
             Public Value As Integer = 2
            End Module
            Module M
             Sub Main()
              PrintLine(CStr(Value))
             End Sub
            End Module
            """, out var ok);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, "must be refused");
            Assert.That(Errors(analyzer), Does.Contain("'Value' is ambiguous between modules 'A', 'B'"));
        });
    }

    /// <summary>A module's own bare name still resolves lexically, ambiguity or not.</summary>
    [Test]
    public void AModulesOwnName_IsNotAmbiguousInsideIt()
    {
        Analyze("""
            Module A
             Public Value As Integer = 1
             Public Function Own() As Integer
              Return Value
             End Function
            End Module
            Module B
             Public Value As Integer = 2
            End Module
            Sub Main()
            End Sub
            """, out var ok);
        Assert.That(ok, Is.True);
    }

    // ------------------------------------------------------------------ multi-file

    private static string TempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "BasicLang_ModuleMember_" + Path.GetRandomFileName())).FullName;

    /// <summary>
    /// ⛔ The MULTI-FILE half had its own symptom: a clean diagnostic, and a wrong one —
    /// "Module 'Helpers' does not have a public member 'Value'. Did you mean 'Val'?" — while
    /// <c>Helpers.Twice()</c> resolved. <c>CollectExportedSymbols</c> walked only the global
    /// scope, which holds procedure signatures (pass 1 flattens them there) and not a Module's
    /// <c>Dim</c>/<c>Const</c>. Constants were unreachable even UNqualified ("Undefined
    /// identifier 'K'").
    /// </summary>
    [Test]
    public void AMultiFileProject_ExportsModuleVariablesAndConstants()
    {
        var dir = TempDir();
        try
        {
            var helpers = Path.Combine(dir, "Helpers.bas");
            var main = Path.Combine(dir, "Main.bas");
            File.WriteAllText(helpers, "Module Helpers\n Public Value As Integer = 11\n Public Const K As Integer = 7\nEnd Module\n");
            File.WriteAllText(main,
                "Import Helpers\nModule M\n Sub Main()\n  Helpers.Value = Helpers.Value + 1\n" +
                "  PrintLine(CStr(Helpers.Value))\n  PrintLine(CStr(Helpers.K))\n End Sub\nEnd Module\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { helpers, main });

            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
                Assert.That(result.CombinedIR, Is.Not.Null);

                // The C# backend is the one that runs a multi-file project here, and its output
                // must name the owner from M: `Helpers.Value`, not a bare `Value` that M does not
                // have. (Measured end to end through the CLI: 12 then 7.)
                var cs = new CSharpCodeGenerator().Generate(result.CombinedIR!);
                Assert.That(cs, Does.Contain("Helpers.Value"));
                Assert.That(cs, Does.Contain("Helpers.K"));
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// Two FILES each declaring the name meet only in <c>CombineIRModules</c>, after each
    /// unit's IR is built, so the single-unit qualification above cannot see them and their IR
    /// names stay bare. MSIL used to keep the LAST one silently; it refuses now, naming both
    /// modules. (C# still groups by module and is fine; C++ and JavaScript were already loud.)
    /// </summary>
    [Test]
    public void ACrossFileCollision_IsRefusedByMsil_NotSilentlyMerged()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "Alpha.bas");
            var b = Path.Combine(dir, "Beta.bas");
            File.WriteAllText(a, "Module Alpha\n Public Scale As Integer = 1\nEnd Module\nSub Main()\nEnd Sub\n");
            File.WriteAllText(b, "Module Beta\n Public Scale As Integer = 2\nEnd Module\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { a, b });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR!.GlobalVariables.Values.Count(v => v.Name == "Scale"), Is.EqualTo(2),
                "both survive in the IR (the key is qualified on collision, the names are not)");

            var ex = Assert.Throws<ForeignFeatureException>(() => new MSILCodeGenerator().Generate(result.CombinedIR!));
            Assert.That(ex!.Message, Does.Contain("'Scale'").And.Contain("Alpha").And.Contain("Beta"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------ pin on what is STILL broken

    /// <summary>
    /// ⛔ PINNED AS BROKEN, and NOT this change's: a QUALIFIED MODULE CALL in a single file.
    /// <c>Helpers.Twice(4)</c> still lowers to an instance call on the phantom receiver —
    /// <c>t0 = Helpers.Twice(4);</c> on C++ ("'Helpers' was not declared"), ReferenceError on
    /// JavaScript, MissingMethodException on MSIL — while C# runs it (8). Same family, separate
    /// mechanism (the call visitor, not member access), left for its own change. If this
    /// stops matching, that change has landed: promote it to a compile-and-run case.
    /// </summary>
    [Test]
    public void AQualifiedModuleCall_IsStillAPhantomReceiverCall_PinnedDivergence()
    {
        const string program = """
            Module Helpers
             Public Function Twice(n As Integer) As Integer
              Return n * 2
             End Function
            End Module
            Sub Main()
             PrintLine(CStr(Helpers.Twice(4)))
            End Sub
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileToCppOptimized(program), Does.Match(@"=\s*Helpers\.Twice\(4\);"),
                "PINNED: the call is still emitted on the module name as if it were an object");
            Assert.That(Norm(RunEmittedCSharp(program)), Is.EqualTo("8"),
                "C# runs it — by re-emitting the text, which is not a lowering");
        });
    }
}
