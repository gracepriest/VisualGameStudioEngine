using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0015 amendment, D2a: a hierarchy whose root is a <c>#CppInclude</c>d foreign C++ class.
///
/// <para>⛔ These shapes need the REAL Preprocessor: the in-process
/// <c>BclE2E.CompileToCppOptimized</c> helper builds its IR straight from
/// <c>Lexer</c>/<c>Parser</c>/<c>SemanticAnalyzer</c>/<c>IRBuilder</c> with no preprocessing pass
/// at all, so a <c>#CppInclude "gfoo.h"</c> line never reaches <c>module.CppIncludes</c> and the
/// generated program never <c>#include</c>s the header its base class needs — measured: "use of
/// undeclared identifier 'GFoo'", not a #200 defect. Every test that must actually COMPILE the
/// foreign base therefore goes through the real CLI binary (<c>BasicLang.exe Prog.bas
/// --target=cpp</c>, which runs the Preprocessor) with the header file written alongside the
/// source — this is also CLAUDE.md's "test through the CLI" entry point, for free. Only the two
/// REFUSAL tests (<see cref="E14g_AComputedCallIntoAForeignRootedClass_IsRefusedByName"/>, whose
/// capability check needs no header at all — a foreign base is detected purely from the ABSENCE
/// of a matching BasicLang class) stay on the in-process helper.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CppMeAsValueForeignBaseTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    private const string GFoo = "#pragma once\nclass GFoo { public: int x = 3; GFoo() {} int Get() { return x; } };\n";
    private const string GFoo2 = "#pragma once\nclass GFoo2 { public: int x; GFoo2(int a) : x(a) {} int Get() { return x; } };\n";
    private const string GFoo3Esft =
        "#pragma once\n#include <memory>\n" +
        "class GFoo3 : public std::enable_shared_from_this<GFoo3> { public: int x = 3; GFoo3() {} };\n";

    /// <summary>
    /// Compiles <paramref name="basSource"/> through the REAL CLI binary with
    /// <paramref name="headers"/> written alongside it, then compiles and runs the emitted C++
    /// (also with those headers present). Ignored when no C++ compiler is on this machine.
    /// </summary>
    private static string CliCompileRunCpp(string basSource, IReadOnlyDictionary<string, string> headers)
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var dir = Path.Combine(Path.GetTempPath(), "bl-me200-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), basSource);
            foreach (var kv in headers) File.WriteAllText(Path.Combine(dir, kv.Key), kv.Value);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "Prog.bas", "--target=cpp" }, dir, timeoutMs: 120_000);
            Assert.That(exit, Is.EqualTo(0),
                $"CLI `BasicLang.exe Prog.bas --target=cpp` failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            var cppPath = Path.Combine(dir, "Prog.cpp");
            Assert.That(File.Exists(cppPath), Is.True, $"CLI reported success but wrote no Prog.cpp.\nSTDOUT:\n{stdout}");

            return Norm(CppCompile.CompileAndRun(File.ReadAllText(cppPath), compiler.Value, headers));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    /// <summary>
    /// Like <see cref="CliCompileRunCpp"/>, but for a program whose contract is that the C++
    /// COMPILER refuses it (the <c>static_assert</c>) — compiles the BasicLang source through
    /// the CLI (which must succeed; the refusal is clang's, not BasicLang's), then tries the
    /// C++ compile WITHOUT asserting success, returning whether it compiled plus the combined
    /// compiler output.
    /// </summary>
    private static (bool Compiled, string Output) CliTryCompileCpp(string basSource, IReadOnlyDictionary<string, string> headers)
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var dir = Path.Combine(Path.GetTempPath(), "bl-me200-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), basSource);
            foreach (var kv in headers) File.WriteAllText(Path.Combine(dir, kv.Key), kv.Value);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "Prog.bas", "--target=cpp" }, dir, timeoutMs: 120_000);
            Assert.That(exit, Is.EqualTo(0),
                $"CLI `BasicLang.exe Prog.bas --target=cpp` failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            var cppPath = Path.Combine(dir, "Prog.cpp");
            return CppCompile.TryCompile(File.ReadAllText(cppPath), compiler.Value, headers);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    /// <summary>F6: a hierarchy rooted directly in a foreign class with a default constructor.</summary>
    [Test]
    public void F6_AHierarchyRootedInAForeignClass_BuildsAndRuns()
        => Assert.That(CliCompileRunCpp("""
            #CppInclude "gfoo.h"
            Using System
            Class D
                Inherits GFoo
                Public V As Integer = 5
            End Class
            Sub Main()
                Dim d As New D()
                Console.WriteLine(d.V)
            End Sub
            """, new Dictionary<string, string> { ["gfoo.h"] = GFoo }), Is.EqualTo("5"));

    /// <summary>F10: a BasicLang class two levels below the foreign root.</summary>
    [Test]
    public void F10_ABasicLangClassTwoLevelsBelowTheForeignRoot_BuildsAndRuns()
        => Assert.That(CliCompileRunCpp("""
            #CppInclude "gfoo.h"
            Using System
            Class D
                Inherits GFoo
                Public V As Integer = 5
                Public Function Twice() As Integer
                    Return V * 2
                End Function
            End Class
            Class E
                Inherits D
                Public W As Integer = 1
            End Class
            Sub Main()
                Dim e As New E()
                Console.WriteLine(e.Twice() + e.W)
            End Sub
            """, new Dictionary<string, string> { ["gfoo.h"] = GFoo }), Is.EqualTo("11"));

    /// <summary>
    /// F11: the direct-foreign child's own constructor takes parameters and passes one straight
    /// through to the foreign base via <c>MyBase.New(a)</c> — a pure argument (a bare parameter).
    /// </summary>
    [Test]
    public void F11_TheDirectForeignChildsConstructor_PassesAParameterToTheForeignBase()
        => Assert.That(CliCompileRunCpp("""
            #CppInclude "gfoo2.h"
            Using System
            Class D
                Inherits GFoo2
                Public V As Integer = 5
                Public Sub New(a As Integer)
                    MyBase.New(a)
                    V = V + a
                End Sub
            End Class
            Sub Main()
                Dim d As New D(4)
                Console.WriteLine(d.V)
            End Sub
            """, new Dictionary<string, string> { ["gfoo2.h"] = GFoo2 }), Is.EqualTo("9"));

    /// <summary>
    /// E14d: a module-level GLOBAL and a Const, each as a <c>MyBase.New</c> argument into a
    /// foreign base, across TWO constructor overloads of the same class. Purity admits a
    /// module-level variable beyond the ADR's literal list (implementation note): between the
    /// tag constructor's evaluation and <c>ctor_</c> step 1's, no BasicLang code runs, so it
    /// reads the same value both times.
    /// </summary>
    [Test]
    public void E14d_AGlobalAndAConst_AsMyBaseNewArgumentsIntoAForeignBase()
        => Assert.That(CliCompileRunCpp("""
            #CppInclude "gfoo2.h"
            Using System
            Dim G As Integer = 6
            Const K As Integer = 2
            Class D
                Inherits GFoo2
                Public V As Integer = 5
                Public Sub New(a As Integer)
                    MyBase.New(G)
                    V = V + a
                End Sub
                Public Sub New()
                    MyBase.New(K)
                End Sub
            End Class
            Sub Main()
                Dim d As New D(4)
                Dim e As New D()
                Console.WriteLine(d.V & " " & e.V)
            End Sub
            """, new Dictionary<string, string> { ["gfoo2.h"] = GFoo2 }), Is.EqualTo("9 5"));

    /// <summary>
    /// E14e: an OPERATOR expression (<c>a * 2</c>) over a parameter, as a <c>MyBase.New</c>
    /// argument into a foreign base — pure (an operator over pure operands), so admitted even
    /// though it is evaluated twice (the tag constructor's initializer list AND <c>ctor_</c>
    /// step 1, at depth &gt;= 2) — purity is exactly what makes the double evaluation
    /// unobservable.
    /// </summary>
    [Test]
    public void E14e_AComputedOperatorExpression_AsAMyBaseNewArgumentIntoAForeignBase()
        => Assert.That(CliCompileRunCpp("""
            #CppInclude "gfoo2.h"
            Using System
            Class D
                Inherits GFoo2
                Public V As Integer = 5
                Public Sub New(a As Integer)
                    MyBase.New(a * 2)
                    V = V + a
                End Sub
            End Class
            Sub Main()
                Dim d As New D(4)
                Console.WriteLine(d.V)
            End Sub
            """, new Dictionary<string, string> { ["gfoo2.h"] = GFoo2 }), Is.EqualTo("9"));

    /// <summary>
    /// E14f: the <c>static_assert</c> beside a foreign-rooted class head FIRES when the foreign
    /// base itself derives from <c>std::enable_shared_from_this</c> — a second such subobject,
    /// which would otherwise leave <c>weak_this</c> silently unset and fail with
    /// <c>bad_weak_ptr</c> at RUN time instead of at compile time (mutant
    /// <c>mx_no_static_assert</c>: WIP=COMPILE-FAIL, MUTANT=RAN — this program never even USES
    /// <c>Me</c> as a value, so nothing else would ever have caught a missing guard).
    /// </summary>
    [Test]
    public void E14f_AForeignBaseThatDerivesFromEnableSharedFromThis_FailsTheStaticAssert()
    {
        var (compiled, output) = CliTryCompileCpp("""
            #CppInclude "gfoo3.h"
            Using System
            Class D
                Inherits GFoo3
                Public V As Integer = 5
            End Class
            Sub Main()
                Dim d As New D()
                Console.WriteLine(d.V)
            End Sub
            """, new Dictionary<string, string> { ["gfoo3.h"] = GFoo3Esft });

        Assert.That(compiled, Is.False, "expected the static_assert to fail the C++ compile:\n" + output);
        Assert.That(output, Does.Contain("static assert").IgnoreCase
            .And.Contain("GFoo3").And.Contain("enable_shared_from_this"),
            "the refusal must still name the static_assert, GFoo3 and enable_shared_from_this " +
            "(ADR-0015 D2a) — a different failure here means the guard moved.\n" + output);
    }

    /// <summary>
    /// E14g: a CALL (<c>NextSeed()</c>) as a <c>MyBase.New</c> argument into a foreign-rooted
    /// class — refused BY NAME, purely from the capability check (no header needed: a foreign
    /// base is detected from <c>GFoo2</c> not being a BasicLang class of this module, regardless
    /// of whether <c>#CppInclude</c> was even preprocessed). Mutant <c>mx_no_purity_check</c>:
    /// WIP=BL-FAIL here, but MUTANT=RAN printing 2 — the call is genuinely evaluated TWICE
    /// (the tag constructor's initializer list and <c>ctor_</c> step 1) without the guard, so
    /// <c>Calls</c> silently drifts from VB's single-evaluation answer.
    /// </summary>
    [Test]
    public void E14g_AComputedCallIntoAForeignRootedClass_IsRefusedByName()
    {
        const string program = """
            #CppInclude "gfoo2.h"
            Using System
            Dim Calls As Integer = 0
            Function NextSeed() As Integer
                Calls = Calls + 1
                Return Calls * 10
            End Function
            Class D
                Inherits GFoo2
                Public Sub New(a As Integer)
                    MyBase.New(a)
                End Sub
            End Class
            Class E
                Inherits D
                Public Sub New()
                    MyBase.New(NextSeed())
                End Sub
            End Class
            Sub Main()
                Dim e As New E()
                Console.WriteLine(Calls)
            End Sub
            """;
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.CPlusPlus.CppCapabilityException>(
            () => BclE2E.CompileToCppOptimized(program));
        Assert.That(ex!.Message, Does.Contain("computed value").And.Contain("ADR-0015 D2a"),
            "the refusal must still name a computed MyBase.New value and ADR-0015 D2a — a " +
            "different message here means the refusal moved.\n" + ex.Message);
    }
}
