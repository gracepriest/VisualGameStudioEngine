using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #124 — ADR-0013 D3: every remaining name reference is bound through the front end's
//  resolution. This file is the FAST half (no process spawned): it asks the IR itself, per
//  consumed site, whether it carries the DECLARED spelling when the reference is written in
//  another case. The execution half is `NameBindingResolutionExecutionTests`.
//
//  ⭐ THE ORACLE. A program that spells every reference as its declaration does is the control:
//  before #124 it was already right, and #124 leaves its IR byte-identical. So each row here is
//  a PAIR — the case-differing program and its same-case control — and the assertion is that
//  their IR is the SAME TEXT, plus explicit facts (the declared spelling is there; the written
//  one is not) so a row can never pass by both being equally wrong. The two programs differ
//  ONLY in the case of the references; anything else that moved is the defect.
//
//  ⛔ Every row below was run against ALL of the #124 mutants (M01–M16); the mutant table is in
//  docs/HANDOFF.md's #124 section, and each mutant is killed by a test in this file or in the
//  execution fixture — see the `Kills` remarks.
// ================================================================================================

/// <summary>
/// Shared plumbing for the site tests: build IR from real source, flatten it to comparable text
/// and to a set of names, and — for the member-access sites — build it WITHOUT the final
/// <c>CanonicaliseMemberNames</c> pass, which is the only way to see a member site's own output.
/// </summary>
internal static class BindingSiteIr
{
    private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Parse, analyze (asserting a clean front end) and build the IR the way
    /// <c>IRBuilder.Build</c> does: the production path, every post-pass included.</summary>
    internal static IRModule Build(string source)
    {
        var (ast, analyzer) = NameBindingProbe.Analyze(source);
        return new IRBuilder(analyzer).Build(ast, "T");
    }

    /// <summary>
    /// ⭐ The IR exactly as the walk over the AST leaves it — BEFORE
    /// <c>IRBuilder.CanonicaliseMemberNames</c> rewrites every member reference to the spelling
    /// the receiver's class declares (ADR-0013's follow-up #250 asks the architect whether that
    /// post-pass stays). Each member site (<c>DeclaredMemberSpelling</c>, <c>AccessorMemberOf</c>)
    /// now takes its spelling from the ANALYZER'S resolved symbol; the post-pass makes the final IR
    /// the same either way, which is why four of the twenty #124 mutants (M06, M11a, M11b, M11c)
    /// survived every end-to-end probe. A test that reads the IR after <c>Build</c> cannot tell the
    /// site from the post-pass; this one can.
    ///
    /// <para>Mirrors <c>Build</c>'s first two steps (<c>_module</c>, <c>CollectSharedModuleGlobalNames</c>)
    /// and its <c>program.Accept</c>, and stops there: <c>CanonicaliseMemberNames</c>,
    /// <c>CompleteReservations</c>, <c>SeparateTempsFromUserNames</c>, <c>AssignBodyLocals</c> and
    /// <c>MarkCompilerTemps</c> are skipped, so temp names and local lists are NOT final — only
    /// member and callee spellings may be asserted on this IR. Reflection on two private members is
    /// deliberate and fails LOUDLY (the two asserts) if either moves.</para>
    /// </summary>
    internal static IRModule BuildBeforeCanonicalisation(string source)
    {
        var (ast, analyzer) = NameBindingProbe.Analyze(source);
        var builder = new IRBuilder(analyzer);

        var moduleField = typeof(IRBuilder).GetField("_module", NonPublic);
        var collect = typeof(IRBuilder).GetMethod("CollectSharedModuleGlobalNames", NonPublic);
        Assert.That(moduleField, Is.Not.Null,
            "IRBuilder no longer has a private `_module` field: update BindingSiteIr.BuildBeforeCanonicalisation.");
        Assert.That(collect, Is.Not.Null,
            "IRBuilder no longer has a private `CollectSharedModuleGlobalNames`: update BindingSiteIr.BuildBeforeCanonicalisation.");

        var module = new IRModule("T");
        moduleField!.SetValue(builder, module);
        collect!.Invoke(builder, new object[] { ast });
        ast.Accept(builder);
        return module;
    }

    /// <summary>Every instruction of <paramref name="function"/>, nested try / catch / finally and
    /// for-each blocks included (each block once).</summary>
    internal static IEnumerable<IRInstruction> Instructions(IRFunction function)
    {
        var seen = new HashSet<BasicBlock>(ReferenceEqualityComparer.Instance);
        var found = new List<IRInstruction>();

        void Walk(BasicBlock block)
        {
            if (block?.Instructions == null || !seen.Add(block)) return;
            foreach (var instruction in block.Instructions)
            {
                found.Add(instruction);
                switch (instruction)
                {
                    case IRTryCatch tryCatch:
                        Walk(tryCatch.TryBlock);
                        if (tryCatch.CatchClauses != null)
                            foreach (var clause in tryCatch.CatchClauses) Walk(clause?.Block);
                        Walk(tryCatch.FinallyBlock);
                        Walk(tryCatch.EndBlock);
                        break;
                    case IRForEach forEach:
                        Walk(forEach.BodyBlock);
                        Walk(forEach.EndBlock);
                        break;
                }
            }
        }

        if (function?.Blocks != null)
            foreach (var block in function.Blocks) Walk(block);
        return found;
    }

    internal static IEnumerable<IRInstruction> Instructions(IRModule module) =>
        module.Functions.SelectMany(Instructions);

    /// <summary>The function (class members flatten into <c>Functions</c> under their bare name)
    /// called <paramref name="name"/>, asserting there is exactly one.</summary>
    internal static IRFunction Function(IRModule module, string name)
    {
        var matches = module.Functions.Where(f => f.Name == name && !f.IsExternal).ToList();
        Assert.That(matches, Has.Count.EqualTo(1), $"expected exactly one function '{name}'");
        return matches[0];
    }

    /// <summary>The module's whole IR as comparable text: each function's locals, then each block's
    /// instructions, as the IR prints them.</summary>
    internal static string Text(IRModule module)
    {
        var lines = new List<string>();
        foreach (var function in module.Functions)
        {
            lines.Add($"== {function.Name} locals=[{string.Join(",", function.LocalVariables.Select(v => v.Name))}]");
            foreach (var block in function.Blocks)
            {
                lines.Add($"  {block.Name}:");
                foreach (var instruction in block.Instructions) lines.Add($"    {instruction}");
            }
        }
        lines.Add("globals=[" + string.Join(",", module.GlobalVariables
            .Select(kv => $"{kv.Key}=>{kv.Value.Name}({kv.Value.ModuleName})")) + "]");
        return string.Join("\n", lines);
    }

    private static readonly Regex VariableToken =
        new(@"[%@]([A-Za-z_][A-Za-z0-9_]*)(?:\.\d+)?|^\s*([A-Za-z_][A-Za-z0-9_]*) = ", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Every variable name an instruction prints — an operand (<c>%n</c>, <c>@G</c>, a
    /// version suffix stripped) or a named result (<c>Total = add …</c>) — plus every function's
    /// declared locals: what the IR CALLS its storage, compared as exact strings.</summary>
    internal static HashSet<string> Names(IRModule module)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
            foreach (var local in function.LocalVariables) names.Add(local.Name);
        foreach (Match m in VariableToken.Matches(Text(module)))
            names.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        return names;
    }

    /// <summary>
    /// The pair assertion every row uses: <paramref name="variant"/> and
    /// <paramref name="control"/> build to the SAME IR text, the IR names <paramref name="declared"/>,
    /// and it names none of <paramref name="written"/> (the wrong-case spellings the variant uses).
    /// </summary>
    internal static void AssertBindsAsDeclared(
        string variant, string control, string[] declared, string[] written,
        Func<string, IRModule>? build = null)
    {
        build ??= Build;
        var variantIr = build(variant);
        var controlIr = build(control);
        var variantText = Text(variantIr);
        var names = Names(variantIr);

        Assert.Multiple(() =>
        {
            Assert.That(variantText, Is.EqualTo(Text(controlIr)),
                "the case-differing program must build to the SAME IR as its same-case control");
            foreach (var name in declared)
                Assert.That(names, Does.Contain(name), $"the IR must name '{name}' (as declared):\n{variantText}");
            foreach (var name in written)
                Assert.That(names, Does.Not.Contain(name), $"the IR must never name '{name}' (as written):\n{variantText}");
        });
    }

    /// <summary>Every node of type <typeparamref name="T"/> reachable from <paramref name="root"/>
    /// through the AST's own properties. A synthesized reference (<c>ControlReference</c>,
    /// <c>EventReference</c>) and a <c>Binding</c> are additive metadata, not children, and are not
    /// walked into.</summary>
    internal static List<T> FindAll<T>(object root) where T : class
    {
        var found = new List<T>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object o)
        {
            if (o == null || !seen.Add(o)) return;
            if (o is T match) found.Add(match);
            var type = o.GetType();
            if (type.Namespace != "BasicLang.Compiler.AST") return;
            foreach (var p in type.GetProperties())
            {
                if (p.GetIndexParameters().Length > 0
                    || p.Name is "Binding" or "ControlReference" or "EventReference") continue;
                object v;
                try { v = p.GetValue(o); } catch { continue; }
                if (v is string) continue;
                if (v is IEnumerable e) { foreach (var item in e) Walk(item); }
                else Walk(v);
            }
        }
        Walk(root);
        return found;
    }

    /// <summary>The one node of type <typeparamref name="T"/> in the tree — fails if there is not
    /// exactly one.</summary>
    internal static T Sole<T>(object root) where T : class
    {
        var all = FindAll<T>(root);
        Assert.That(all, Has.Count.EqualTo(1), $"expected exactly one {typeof(T).Name}");
        return all[0];
    }

    /// <summary>Project files in a fresh temp directory, compiled through
    /// <c>BasicCompiler.CompileProjectFiles</c> — the entry point the CLI and the IDE both reach.
    /// The result's <c>Units</c> hold each file's own IR.</summary>
    internal static CompilationResult CompileProject(params (string FileName, string Source)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t124-sites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            foreach (var (name, source) in files)
            {
                var path = Path.Combine(dir, name);
                File.WriteAllText(path, source);
                paths.Add(path);
            }
            var result = new BasicCompiler().CompileProjectFiles(paths);
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            return result;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }
}

// ================================================================================================
//  BoundVariable: a bare reference by Kind — Field, Property, ModuleGlobal (owning module,
//  imported, file scope), and the name-not-storage kinds (Method, Type, Event).
// ================================================================================================

/// <summary>
/// <c>IRBuilder.BoundVariable</c> (via <c>ReferencedVariable</c>): a reference spelled unlike its
/// declaration reaches the IR under the DECLARED spelling, for every kind the analyzer records.
///
/// <para>Kills: M01 (Field/Property arm reads the written name) — <see cref="AFieldReadWriteAndCompoundWrite_BindAsDeclared"/>,
/// <see cref="AnInheritedField_BindsAsDeclared"/>, <see cref="ASharedField_BindsAsDeclared"/>,
/// <see cref="AFieldInsideALambda_BindsAsDeclared"/>, <see cref="AnAutoPropertyBareName_BindsAsDeclared"/>;
/// M02 (ModuleGlobal arm reads the written name) — <see cref="AFileScopeGlobal_BindsAsDeclared"/>,
/// <see cref="AModuleGlobalFromInsideItsModule_BindsAsDeclared"/>; M03 (Method/Type/Event arm reads
/// the written name) — <see cref="AMethodNameAsAValue_BindsAsDeclared"/>,
/// <see cref="AnEventNameAsAValue_BindsAsDeclared"/>, <see cref="ATypeReceiver_BindsAsDeclared"/>;
/// M04 (imported global read/write by written name) — <see cref="AnImportedGlobal_BindsAsDeclared_ReadAndWrite"/>;
/// M05 (module-member global by written name) — <see cref="AModuleGlobalDeclaredLaterInTheFile_BindsAsDeclared"/>.</para>
/// </summary>
[TestFixture]
public class NameBindingBoundVariableSiteTests
{
    [Test]
    public void AFieldReadWriteAndCompoundWrite_BindAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Class C
                Private Total As Integer = 5
                Public Sub Bump()
                    total = total + 7
                    TOTAL += 1
                End Sub
                Public Function Read() As Integer
                    Return total * 2
                End Function
            End Class
            Sub Main()
                Dim o As New C()
                o.Bump()
                Console.WriteLine(o.Read())
            End Sub
            """, """
            Class C
                Private Total As Integer = 5
                Public Sub Bump()
                    Total = Total + 7
                    Total += 1
                End Sub
                Public Function Read() As Integer
                    Return Total * 2
                End Function
            End Class
            Sub Main()
                Dim o As New C()
                o.Bump()
                Console.WriteLine(o.Read())
            End Sub
            """, declared: new[] { "Total" }, written: new[] { "total", "TOTAL" });

    [Test]
    public void AnInheritedField_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Class B
                Protected Level As Integer = 3
            End Class
            Class D
                Inherits B
                Public Function Run() As Integer
                    level = level * 5
                    Return LEVEL + 1
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New D().Run())
            End Sub
            """, """
            Class B
                Protected Level As Integer = 3
            End Class
            Class D
                Inherits B
                Public Function Run() As Integer
                    Level = Level * 5
                    Return Level + 1
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New D().Run())
            End Sub
            """, declared: new[] { "Level" }, written: new[] { "level", "LEVEL" });

    [Test]
    public void ASharedField_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Class C
                Private Shared Count As Integer = 0
                Public Shared Function Tick() As Integer
                    count += 1
                    COUNT = count + 10
                    Return Count
                End Function
            End Class
            Sub Main()
                Console.WriteLine(C.Tick())
            End Sub
            """, """
            Class C
                Private Shared Count As Integer = 0
                Public Shared Function Tick() As Integer
                    Count += 1
                    Count = Count + 10
                    Return Count
                End Function
            End Class
            Sub Main()
                Console.WriteLine(C.Tick())
            End Sub
            """, declared: new[] { "Count" }, written: new[] { "count", "COUNT" });

    /// <summary>A field read and written from two lambdas: each lambda is its own IR function, and
    /// each reaches the field by its declared spelling.</summary>
    [Test]
    public void AFieldInsideALambda_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Class C
                Private Total As Integer = 5
                Public Function Run() As Integer
                    Dim f = Function(k As Integer) k + total
                    Dim s = Sub(k As Integer) TOTAL = k
                    s(40)
                    Return f(2)
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New C().Run())
            End Sub
            """, """
            Class C
                Private Total As Integer = 5
                Public Function Run() As Integer
                    Dim f = Function(k As Integer) k + Total
                    Dim s = Sub(k As Integer) Total = k
                    s(40)
                    Return f(2)
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New C().Run())
            End Sub
            """, declared: new[] { "Total" }, written: new[] { "total", "TOTAL" });

    /// <summary>A plain auto-property named bare inside its own class is a Property-kind binding:
    /// the same arm as a field, and it keeps the property's declared spelling.</summary>
    [Test]
    public void AnAutoPropertyBareName_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Class C
                Public Property Count As Integer
                Public Sub Bump()
                    count = count + 1
                    COUNT += 2
                End Sub
            End Class
            Sub Main()
                Dim o As New C()
                o.Bump()
                Console.WriteLine(o.Count)
            End Sub
            """, """
            Class C
                Public Property Count As Integer
                Public Sub Bump()
                    Count = Count + 1
                    Count += 2
                End Sub
            End Class
            Sub Main()
                Dim o As New C()
                o.Bump()
                Console.WriteLine(o.Count)
            End Sub
            """, declared: new[] { "Count" }, written: new[] { "count", "COUNT" });

    /// <summary>A Module's own variable named bare from inside the Module: the global carries the
    /// declared spelling and the owning module (<c>GlobalReference</c>).</summary>
    [Test]
    public void AModuleGlobalFromInsideItsModule_BindsAsDeclared()
    {
        const string variant = """
            Module Data
                Public G As Integer = 1
                Public Sub Bump()
                    g = G + 1
                End Sub
            End Module
            Sub Main()
                Data.Bump()
                Console.WriteLine(g)
            End Sub
            """;
        const string control = """
            Module Data
                Public G As Integer = 1
                Public Sub Bump()
                    G = G + 1
                End Sub
            End Module
            Sub Main()
                Data.Bump()
                Console.WriteLine(G)
            End Sub
            """;
        BindingSiteIr.AssertBindsAsDeclared(variant, control, declared: new[] { "G" }, written: new[] { "g" });

        var global = BindingSiteIr.Build(variant).GlobalVariables.Values.Single();
        Assert.Multiple(() =>
        {
            Assert.That(global.Name, Is.EqualTo("G"));
            Assert.That(global.IsGlobal, Is.True);
            Assert.That(global.ModuleName, Is.EqualTo("Data"));
        });
    }

    /// <summary>A Module variable used BEFORE the Module is declared in the file: its declaration
    /// has not been visited, so the reference is a FORWARD reference (<c>GlobalReference</c>'s
    /// fallback) — and that fallback is spelled by the name it is handed, which must be the
    /// declared one. (MGw.)</summary>
    [Test]
    public void AModuleGlobalDeclaredLaterInTheFile_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Sub Main()
                counter = COUNTER + 5
                Console.WriteLine(counter)
            End Sub
            Module Data
                Public Counter As Integer = 7
            End Module
            """, """
            Sub Main()
                Counter = Counter + 5
                Console.WriteLine(Counter)
            End Sub
            Module Data
                Public Counter As Integer = 7
            End Module
            """, declared: new[] { "Counter" }, written: new[] { "counter", "COUNTER" });

    /// <summary>A file-scope <c>Dim</c>, read and written from a Sub: the ModuleGlobal arm's third
    /// lookup (the maps its declaration wrote).</summary>
    [Test]
    public void AFileScopeGlobal_BindsAsDeclared()
    {
        const string variant = """
            Dim G As Integer = 1
            Sub Bump()
                g = G + 1
            End Sub
            Sub Main()
                Bump()
                Console.WriteLine(g)
            End Sub
            """;
        const string control = """
            Dim G As Integer = 1
            Sub Bump()
                G = G + 1
            End Sub
            Sub Main()
                Bump()
                Console.WriteLine(G)
            End Sub
            """;
        BindingSiteIr.AssertBindsAsDeclared(variant, control, declared: new[] { "G" }, written: new[] { "g" });
        Assert.That(BindingSiteIr.Build(variant).GlobalVariables.Values.Select(v => v.Name), Is.EquivalentTo(new[] { "G" }));
    }

    /// <summary>A Module member reached through the Module's own name, in another case, on both
    /// sides of an assignment — <c>ModuleMemberGlobal</c>, read AND write.</summary>
    [Test]
    public void AQualifiedModuleMember_BindsAsDeclared_ReadAndWrite()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Module Data
                Public G As Integer = 1
            End Module
            Sub Main()
                data.g = Data.G + 1
                Console.WriteLine(DATA.g)
            End Sub
            """, """
            Module Data
                Public G As Integer = 1
            End Module
            Sub Main()
                Data.G = Data.G + 1
                Console.WriteLine(Data.G)
            End Sub
            """, declared: new[] { "G" }, written: new[] { "g" });

    /// <summary>A Module member reached through the Module's own name, in another case, BEFORE the Module is declared in the file:
    /// the declaration has not been visited, so the qualified reference is a forward one (<c>GlobalReference</c>'s fallback) —
    /// spelled by the name it is handed, which must be the symbol's (declared) one, not the member access's written one.</summary>
    [Test]
    public void AQualifiedModuleMemberBeforeItsModule_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Sub Main()
                data.counter = DATA.COUNTER + 5
                Console.WriteLine(Data.counter)
            End Sub
            Module Data
                Public Counter As Integer = 7
            End Module
            """, """
            Sub Main()
                Data.Counter = Data.Counter + 5
                Console.WriteLine(Data.Counter)
            End Sub
            Module Data
                Public Counter As Integer = 7
            End Module
            """, declared: new[] { "Counter" }, written: new[] { "counter", "COUNTER" });

    /// <summary>
    /// A Module variable declared in ANOTHER FILE, named bare in another case (the "imported"
    /// global) and through its Module (<c>ModuleMemberGlobal</c>): each is a global of the declared
    /// spelling owned by <c>Data</c>. Read and write. (MF1.)
    /// </summary>
    [Test]
    public void AnImportedGlobal_BindsAsDeclared_ReadAndWrite()
    {
        const string data = """
            Module Data
                Public Counter As Integer = 7
            End Module
            """;
        var variant = BindingSiteIr.CompileProject(("Data.bas", data), ("Main.bas", """
            Sub Main()
                counter = COUNTER + 1
                Data.counter = Data.COUNTER * 2
                Console.WriteLine(Counter)
            End Sub
            """));
        var control = BindingSiteIr.CompileProject(("Data.bas", data), ("Main.bas", """
            Sub Main()
                Counter = Counter + 1
                Data.Counter = Data.Counter * 2
                Console.WriteLine(Counter)
            End Sub
            """));

        var main = variant.Units.Single(u => Path.GetFileName(u.FilePath) == "Main.bas").IR;
        var mainControl = control.Units.Single(u => Path.GetFileName(u.FilePath) == "Main.bas").IR;
        var names = BindingSiteIr.Names(main);
        Assert.Multiple(() =>
        {
            Assert.That(BindingSiteIr.Text(main), Is.EqualTo(BindingSiteIr.Text(mainControl)),
                "same IR as the same-case control:\n" + BindingSiteIr.Text(main));
            Assert.That(names, Does.Contain("Counter"));
            Assert.That(names, Does.Not.Contain("counter").And.Not.Contain("COUNTER"),
                BindingSiteIr.Text(main));

            // Every global the unit references is Data's, by the declared spelling.
            var globals = BindingSiteIr.Instructions(main)
                .SelectMany(i => Regex.Matches(i.ToString(), @"@([A-Za-z_]\w*)").Select(m => m.Groups[1].Value))
                .Distinct().ToList();
            Assert.That(globals, Is.EquivalentTo(new[] { "Counter" }));
        });
    }

    /// <summary>A Sub named without call syntax as a value (<c>AddressOf show</c>): the Method
    /// binding is a NAME, not storage, and it is spelled as declared.</summary>
    [Test]
    public void AMethodNameAsAValue_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Sub Show(v As Integer)
                Console.WriteLine(v * 3)
            End Sub
            Sub Main()
                Dim f As Action(Of Integer) = AddressOf show
                f(5)
            End Sub
            """, """
            Sub Show(v As Integer)
                Console.WriteLine(v * 3)
            End Sub
            Sub Main()
                Dim f As Action(Of Integer) = AddressOf Show
                f(5)
            End Sub
            """, declared: new[] { "Show" }, written: new[] { "show" });

    /// <summary>An event named as an <c>AddHandler</c> operand: the Event binding is a name, and it
    /// is spelled as declared. (EVb.)</summary>
    [Test]
    public void AnEventNameAsAValue_BindsAsDeclared()
        => BindingSiteIr.AssertBindsAsDeclared("""
            Class Button
                Public Event Clicked(count As Integer)
                Public Sub Wire()
                    AddHandler clicked, AddressOf Me.OnClicked
                End Sub
                Public Sub OnClicked(n As Integer)
                    Console.WriteLine(n + 100)
                End Sub
                Public Sub Press()
                    RaiseEvent Clicked(5)
                End Sub
            End Class
            Sub Main()
                Dim b As New Button()
                b.Wire()
                b.Press()
            End Sub
            """, """
            Class Button
                Public Event Clicked(count As Integer)
                Public Sub Wire()
                    AddHandler Clicked, AddressOf Me.OnClicked
                End Sub
                Public Sub OnClicked(n As Integer)
                    Console.WriteLine(n + 100)
                End Sub
                Public Sub Press()
                    RaiseEvent Clicked(5)
                End Sub
            End Class
            Sub Main()
                Dim b As New Button()
                b.Wire()
                b.Press()
            End Sub
            """, declared: new[] { "Clicked" }, written: new[] { "clicked" });

    /// <summary>A class named as the receiver of a Shared call (<c>util.Make(1)</c>): the Type
    /// binding is the receiver's name, and the fused static call is spelled by it. (MEt.)</summary>
    [Test]
    public void ATypeReceiver_BindsAsDeclared()
    {
        const string variant = """
            Class Util
                Public Shared Function Make(x As Integer) As Integer
                    Return x + 100
                End Function
            End Class
            Sub Main()
                Console.WriteLine(util.Make(1))
                Console.WriteLine(UTIL.make(2))
            End Sub
            """;
        const string control = """
            Class Util
                Public Shared Function Make(x As Integer) As Integer
                    Return x + 100
                End Function
            End Class
            Sub Main()
                Console.WriteLine(Util.Make(1))
                Console.WriteLine(Util.Make(2))
            End Sub
            """;
        BindingSiteIr.AssertBindsAsDeclared(variant, control, declared: Array.Empty<string>(), written: Array.Empty<string>());
        var calls = BindingSiteIr.Instructions(BindingSiteIr.Build(variant)).OfType<IRCall>()
            .Select(c => c.FunctionName).Where(n => n.Contains("Make", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.That(calls, Is.EqualTo(new[] { "Util.Make", "Util.Make" }));
    }
}

// ================================================================================================
//  AccessorMemberOf, the Await callee, RaiseEvent, New
// ================================================================================================

/// <summary>
/// The name-a-thing sites: a bare accessor-backed property (<c>AccessorMemberOf</c>), the callee of
/// an <c>Await</c>, the event of a <c>RaiseEvent</c>, and the class of a <c>New</c>.
///
/// <para>Kills: M06 (accessor member spelled as written) — the two <c>…BeforeCanonicalisation</c>
/// rows; M09 (Await callee as written) — <see cref="AnAwaitedUserFunction_IsCalledByItsDeclaredName"/>;
/// M10 (RaiseEvent as written) — <see cref="RaiseEvent_CallsTheEventByItsDeclaredName"/>; M12 (New
/// class as written) — <see cref="New_NamesTheClassAsDeclared"/>; M15 (Event never recorded) —
/// <see cref="RaiseEvent_CallsTheEventByItsDeclaredName"/> and
/// <see cref="AnEventReference_IsBoundAsKindEvent"/>.</para>
/// </summary>
[TestFixture]
public class NameBindingNamedThingSiteTests
{
    private const string AccessorVariant = """
        Class C
            Private _v As Integer
            Public Property Value As Integer
                Get
                    Return _v
                End Get
                Set(nv As Integer)
                    _v = nv
                End Set
            End Property
            Public Sub Bump()
                value = VALUE + 1
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.Bump()
            Console.WriteLine(o.Value)
        End Sub
        """;

    private const string AccessorControl = """
        Class C
            Private _v As Integer
            Public Property Value As Integer
                Get
                    Return _v
                End Get
                Set(nv As Integer)
                    _v = nv
                End Set
            End Property
            Public Sub Bump()
                Value = Value + 1
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.Bump()
            Console.WriteLine(o.Value)
        End Sub
        """;

    private const string SharedAccessorVariant = """
        Class C
            Private Shared _v As Integer
            Public Shared Property Total As Integer
                Get
                    Return _v
                End Get
                Set(nv As Integer)
                    _v = nv
                End Set
            End Property
            Public Shared Sub Bump()
                total = TOTAL + 1
            End Sub
        End Class
        Sub Main()
            C.Bump()
            Console.WriteLine(C.Total)
        End Sub
        """;

    private const string SharedAccessorControl = """
        Class C
            Private Shared _v As Integer
            Public Shared Property Total As Integer
                Get
                    Return _v
                End Get
                Set(nv As Integer)
                    _v = nv
                End Set
            End Property
            Public Shared Sub Bump()
                Total = Total + 1
            End Sub
        End Class
        Sub Main()
            C.Bump()
            Console.WriteLine(C.Total)
        End Sub
        """;

    /// <summary>A bare Get/Set property, written in another case, lowers to the accessor node the
    /// QUALIFIED form produces — <c>IRFieldAccess(Me, Value)</c> / <c>IRFieldStore(Me, Value)</c> —
    /// naming the property as declared. Final IR.</summary>
    [Test]
    public void ABareAccessorProperty_LowersToTheDeclaredMember()
    {
        BindingSiteIr.AssertBindsAsDeclared(AccessorVariant, AccessorControl, declared: Array.Empty<string>(), written: Array.Empty<string>());
        var bump = BindingSiteIr.Function(BindingSiteIr.Build(AccessorVariant), "Bump");
        var instructions = BindingSiteIr.Instructions(bump).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(instructions.OfType<IRFieldAccess>().Select(a => a.FieldName), Is.EqualTo(new[] { "Value" }));
            Assert.That(instructions.OfType<IRFieldStore>().Select(s => s.FieldName), Is.EqualTo(new[] { "Value" }));
        });
    }

    /// <summary>
    /// ⭐ The SAME rows, read BEFORE <c>CanonicaliseMemberNames</c>: the accessor site's own answer.
    /// M06 (<c>AccessorMemberOf</c> returns the WRITTEN spelling) is invisible to every end-to-end
    /// probe, because the post-pass rewrites it back; here the site is the only thing that ran.
    /// </summary>
    [Test]
    public void ABareAccessorProperty_NamesTheDeclaredMember_BeforeCanonicalisation()
    {
        var bump = BindingSiteIr.Function(BindingSiteIr.BuildBeforeCanonicalisation(AccessorVariant), "Bump");
        var instructions = BindingSiteIr.Instructions(bump).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(instructions.OfType<IRFieldAccess>().Select(a => a.FieldName), Is.EqualTo(new[] { "Value" }),
                "the bare read, before the post-pass");
            Assert.That(instructions.OfType<IRFieldStore>().Select(s => s.FieldName), Is.EqualTo(new[] { "Value" }),
                "the bare write, before the post-pass");
        });
    }

    /// <summary>A Shared accessor property: the receiver is the DECLARING class and the member is
    /// spelled as declared — final IR and before the post-pass.</summary>
    [Test]
    public void ASharedBareAccessorProperty_NamesTheDeclaredMember_FinalAndBeforeCanonicalisation()
    {
        BindingSiteIr.AssertBindsAsDeclared(SharedAccessorVariant, SharedAccessorControl,
            declared: Array.Empty<string>(), written: Array.Empty<string>());

        foreach (var (label, module) in new[]
        {
            ("final", BindingSiteIr.Build(SharedAccessorVariant)),
            ("before the post-pass", BindingSiteIr.BuildBeforeCanonicalisation(SharedAccessorVariant)),
        })
        {
            var bump = BindingSiteIr.Instructions(BindingSiteIr.Function(module, "Bump")).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(bump.OfType<IRFieldAccess>().Select(a => $"{a.Object.Name}.{a.FieldName}"),
                    Is.EqualTo(new[] { "C.Total" }), label + ": read");
                Assert.That(bump.OfType<IRFieldStore>().Select(s => $"{s.Object.Name}.{s.FieldName}"),
                    Is.EqualTo(new[] { "C.Total" }), label + ": write");
            });
        }
    }

    private const string AwaitVariant = """
        Class Worker
            Public Shared Async Function Make() As Task(Of Integer)
                Return 7
            End Function
        End Class
        Async Function GetNumber() As Task(Of Integer)
            Return 42
        End Function
        Async Function Run() As Task(Of Integer)
            Dim a As Integer = Await getnumber()
            Dim b As Integer = Await worker.MAKE()
            Await Task.Delay(1)
            Return a + b
        End Function
        Sub Main()
            Console.WriteLine(Run().Result)
        End Sub
        """;

    private const string AwaitControl = """
        Class Worker
            Public Shared Async Function Make() As Task(Of Integer)
                Return 7
            End Function
        End Class
        Async Function GetNumber() As Task(Of Integer)
            Return 42
        End Function
        Async Function Run() As Task(Of Integer)
            Dim a As Integer = Await GetNumber()
            Dim b As Integer = Await Worker.Make()
            Await Task.Delay(1)
            Return a + b
        End Function
        Sub Main()
            Console.WriteLine(Run().Result)
        End Sub
        """;

    /// <summary>
    /// An awaited user Function, and an awaited Shared member of a user class, are called by their
    /// declared spelling — and the awaited .NET member (<c>Task.Delay</c>), which carries no
    /// BasicLang symbol, keeps the spelling it was written in.
    /// </summary>
    [Test]
    public void AnAwaitedUserFunction_IsCalledByItsDeclaredName()
    {
        BindingSiteIr.AssertBindsAsDeclared(AwaitVariant, AwaitControl, declared: Array.Empty<string>(), written: Array.Empty<string>());

        var awaited = BindingSiteIr.Instructions(BindingSiteIr.Function(BindingSiteIr.Build(AwaitVariant), "Run"))
            .OfType<IRAwait>().Select(a => (a.Expression as IRCall)?.FunctionName).ToList();
        Assert.That(awaited, Is.EqualTo(new[] { "GetNumber", "Worker.Make", "Task.Delay" }));
    }

    private const string RaiseVariant = """
        Class Button
            Public Event Clicked(count As Integer)
            Private clicks As Integer
            Public Sub Press()
                clicks = clicks + 1
                RaiseEvent clicked(clicks)
                RaiseEvent CLICKED(clicks * 10)
            End Sub
        End Class
        Sub OnClicked(n As Integer)
            Console.WriteLine(n)
        End Sub
        Sub Main()
            Dim b As New Button()
            AddHandler b.Clicked, AddressOf OnClicked
            b.Press()
        End Sub
        """;

    /// <summary>
    /// <c>RaiseEvent clicked(…)</c> for an event declared <c>Clicked</c> raises <c>raise_Clicked</c>,
    /// for every spelling. (Kills M10 and M15. M10 survived every end-to-end probe because the C# and
    /// JavaScript backends look the event up again by their own case-insensitive name — MEASURED: the
    /// M10 build still emits <c>Clicked?.Invoke(…)</c> — so only the IR shows the written spelling.)
    /// </summary>
    [Test]
    public void RaiseEvent_CallsTheEventByItsDeclaredName()
    {
        var press = BindingSiteIr.Instructions(BindingSiteIr.Function(BindingSiteIr.Build(RaiseVariant), "Press"))
            .OfType<IRCall>().Select(c => c.FunctionName).ToList();
        var control = BindingSiteIr.Instructions(BindingSiteIr.Function(
                BindingSiteIr.Build(RaiseVariant.Replace("clicked(clicks)", "Clicked(clicks)").Replace("CLICKED(", "Clicked(")), "Press"))
            .OfType<IRCall>().Select(c => c.FunctionName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(press, Is.EqualTo(new[] { "raise_Clicked", "raise_Clicked" }));
            Assert.That(control, Is.EqualTo(press), "same as the same-case control");
        });
    }

    /// <summary>The analyzer records the event a <c>RaiseEvent</c> names: a synthesized reference,
    /// not a child of the statement, whose binding is Kind Event and spelled as declared.</summary>
    [Test]
    public void AnEventReference_IsBoundAsKindEvent()
    {
        var (ast, _) = NameBindingProbe.Analyze(RaiseVariant);
        var raises = BindingSiteIr.FindAll<RaiseEventStatementNode>(ast);
        Assert.That(raises, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            foreach (var raise in raises)
            {
                Assert.That(raise.EventReference, Is.Not.Null, $"line {raise.Line}: the event is recorded");
                Assert.That(raise.EventReference!.Binding, Is.Not.Null, $"line {raise.Line}");
                Assert.That(raise.EventReference.Binding!.Kind, Is.EqualTo(NameBindingKind.Event));
                Assert.That(raise.EventReference.Binding.DeclaredName, Is.EqualTo("Clicked"));
                Assert.That(raise.EventReference.Binding.Declaration.Kind, Is.EqualTo(SymbolKind.Event));
                Assert.That(raise.EventReference.Name, Is.EqualTo(raise.EventName), "Name stays as written");
            }
        });
    }

    /// <summary>The event reference is overwritten on every analysis pass over the same AST (a
    /// SECOND analyzer here — one analyzer cannot re-register a class), never left over.</summary>
    [Test]
    public void AnEventReference_IsOverwrittenOnEveryAnalysisPass()
    {
        var ast = new Parser(new Lexer(RaiseVariant).Tokenize()).Parse();
        Assert.That(new SemanticAnalyzer().Analyze(ast), Is.True);
        var raise = BindingSiteIr.FindAll<RaiseEventStatementNode>(ast).First();
        var first = raise.EventReference;
        Assert.That(first, Is.Not.Null);

        Assert.That(new SemanticAnalyzer().Analyze(ast), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(raise.EventReference, Is.Not.Null);
            Assert.That(raise.EventReference, Is.Not.SameAs(first), "re-recorded, not left over");
            Assert.That(raise.EventReference!.Binding!.DeclaredName, Is.EqualTo("Clicked"));
        });
    }

    /// <summary>
    /// <c>New BOX()</c> for a class declared <c>Box</c> constructs <c>Box</c>: the class the
    /// ANALYZER resolved, not the written spelling (a `New` node carries no NameBinding). A type
    /// the analyzer does not resolve to a user class keeps its written name. (TYa; kills M12.)
    /// </summary>
    [Test]
    public void New_NamesTheClassAsDeclared()
    {
        const string variant = """
            Class Box
                Public V As Integer = 3
            End Class
            Sub Main()
                Dim b As box = New BOX()
                Dim l As New List(Of box)()
                l.Add(b)
                Console.WriteLine(l(0).V + l.Count)
            End Sub
            """;
        const string control = """
            Class Box
                Public V As Integer = 3
            End Class
            Sub Main()
                Dim b As Box = New Box()
                Dim l As New List(Of Box)()
                l.Add(b)
                Console.WriteLine(l(0).V + l.Count)
            End Sub
            """;
        BindingSiteIr.AssertBindsAsDeclared(variant, control, declared: Array.Empty<string>(), written: Array.Empty<string>());
        var created = BindingSiteIr.Instructions(BindingSiteIr.Build(variant)).OfType<IRNewObject>()
            .Select(n => n.ClassName).ToList();
        Assert.That(created, Is.EqualTo(new[] { "Box", "List" }),
            "the user class as declared; the .NET collection as written (it is not a user class)");
    }
}

// ================================================================================================
//  DeclaredMemberSpelling: a member ACCESS names its member as the analyzer's symbol does
// ================================================================================================

/// <summary>
/// <c>IRBuilder.DeclaredMemberSpelling</c>: <c>obj.m</c> read, store, instance call, Shared call
/// and <c>MyBase.m</c>, each spelled as the member is DECLARED — observed twice: on the final IR,
/// and on the IR BEFORE <c>CanonicaliseMemberNames</c>, because a member of another file's class or
/// module, or a fused static call, is reached only by the site (the post-pass never sees it), while
/// a same-file instance member is repaired by the post-pass either way. Reading the IR before it is
/// the only way a test can tell those two apart.
///
/// <para>Kills: M11a (member read), M11b (member store), M11c (instance call) — the
/// <c>…BeforeCanonicalisation</c> rows, which are the only tests that can (each is masked by the
/// post-pass in the final IR: see the header of <see cref="BindingSiteIr.BuildBeforeCanonicalisation"/>);
/// M11d (Shared call) and M11e (<c>MyBase</c> call) — both rows, the post-pass does not touch them.</para>
/// </summary>
[TestFixture]
public class NameBindingMemberSpellingSiteTests
{
    private const string Variant = """
        Class Base
            Public Overridable Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
        End Class
        Class C
            Inherits Base
            Public Total As Integer = 5
            Public Shared Function Make(x As Integer) As Integer
                Return x + 100
            End Function
            Public Sub Bump()
                Total += 1
            End Sub
            Public Overrides Function Twice(x As Integer) As Integer
                Return MyBase.twice(x) + 1
            End Function
        End Class
        Sub Main()
            Dim o As C = New c()
            o.total = 3
            o.bump()
            Console.WriteLine(o.TOTAL + c.MAKE(1) + o.twice(1))
        End Sub
        """;

    private const string Control = """
        Class Base
            Public Overridable Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
        End Class
        Class C
            Inherits Base
            Public Total As Integer = 5
            Public Shared Function Make(x As Integer) As Integer
                Return x + 100
            End Function
            Public Sub Bump()
                Total += 1
            End Sub
            Public Overrides Function Twice(x As Integer) As Integer
                Return MyBase.Twice(x) + 1
            End Function
        End Class
        Sub Main()
            Dim o As C = New C()
            o.Total = 3
            o.Bump()
            Console.WriteLine(o.Total + C.Make(1) + o.Twice(1))
        End Sub
        """;

    private static IEnumerable<TestCaseData> Builds()
    {
        yield return new TestCaseData((Func<string, IRModule>)BindingSiteIr.Build).SetName("{m}_Final");
        yield return new TestCaseData((Func<string, IRModule>)BindingSiteIr.BuildBeforeCanonicalisation).SetName("{m}_BeforeCanonicalisation");
    }

    [TestCaseSource(nameof(Builds))]
    public void AMemberRead_IsSpelledAsDeclared(Func<string, IRModule> build)
        => Assert.That(BindingSiteIr.Instructions(BindingSiteIr.Function(build(Variant), "Main")).OfType<IRFieldAccess>()
            .Select(a => a.FieldName), Is.EqualTo(new[] { "Total" }));

    [TestCaseSource(nameof(Builds))]
    public void AMemberStore_IsSpelledAsDeclared(Func<string, IRModule> build)
        => Assert.That(BindingSiteIr.Instructions(BindingSiteIr.Function(build(Variant), "Main")).OfType<IRFieldStore>()
            .Select(s => s.FieldName), Is.EqualTo(new[] { "Total" }));

    [TestCaseSource(nameof(Builds))]
    public void AnInstanceCall_IsSpelledAsDeclared(Func<string, IRModule> build)
        => Assert.That(BindingSiteIr.Instructions(BindingSiteIr.Function(build(Variant), "Main")).OfType<IRInstanceMethodCall>()
            .Select(c => c.MethodName), Is.EqualTo(new[] { "Bump", "Twice" }));

    [TestCaseSource(nameof(Builds))]
    public void AStaticCall_IsSpelledAsDeclared(Func<string, IRModule> build)
        => Assert.That(BindingSiteIr.Instructions(BindingSiteIr.Function(build(Variant), "Main")).OfType<IRCall>()
            .Select(c => c.FunctionName).Where(n => n.EndsWith("Make", StringComparison.OrdinalIgnoreCase)),
            Is.EqualTo(new[] { "C.Make" }));

    [TestCaseSource(nameof(Builds))]
    public void AMyBaseCall_IsSpelledAsDeclared(Func<string, IRModule> build)
    {
        var module = build(Variant);
        var twice = module.Functions.Where(f => f.Name == "Twice")
            .SelectMany(BindingSiteIr.Instructions).OfType<IRBaseMethodCall>().Select(c => c.MethodName).ToList();
        Assert.That(twice, Is.EqualTo(new[] { "Twice" }));
    }

    [Test]
    public void TheWholeProgram_BuildsToTheSameIRAsItsSameCaseControl()
        => Assert.That(BindingSiteIr.Text(BindingSiteIr.Build(Variant)), Is.EqualTo(BindingSiteIr.Text(BindingSiteIr.Build(Control))));

    /// <summary>
    /// A member of a class declared in ANOTHER FILE — the shape the post-pass could not see before
    /// (it looked a member up in THIS unit's classes only): the site itself, through the analyzer's
    /// symbol, spells the read, the write and the call as declared. (MF2.)
    /// </summary>
    [Test]
    public void AMemberOfAClassInAnotherFile_IsSpelledAsDeclared()
    {
        var result = BindingSiteIr.CompileProject(("Box.bas", """
            Class Box
                Public Value As Integer
                Public Sub Bump()
                    Value += 1
                End Sub
            End Class
            """), ("Main.bas", """
            Sub Main()
                Dim b As box = New BOX()
                b.value = 3
                b.bump()
                Console.WriteLine(b.VALUE)
            End Sub
            """));
        var main = result.Units.Single(u => Path.GetFileName(u.FilePath) == "Main.bas").IR;
        var instructions = BindingSiteIr.Instructions(main).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(instructions.OfType<IRNewObject>().Select(n => n.ClassName), Is.EqualTo(new[] { "Box" }));
            Assert.That(instructions.OfType<IRFieldStore>().Select(s => s.FieldName), Is.EqualTo(new[] { "Value" }));
            Assert.That(instructions.OfType<IRFieldAccess>().Select(a => a.FieldName), Is.EqualTo(new[] { "Value" }));
            Assert.That(instructions.OfType<IRInstanceMethodCall>().Select(c => c.MethodName), Is.EqualTo(new[] { "Bump" }));
        });
    }

    /// <summary>A .NET member has no BasicLang symbol: it keeps the spelling it was written in. The
    /// site never REWRITES what it cannot resolve.</summary>
    [Test]
    public void ADotNetMember_KeepsItsWrittenSpelling()
    {
        var module = BindingSiteIr.Build("""
            Sub Main()
                Dim l As New List(Of Integer)()
                l.Add(1)
                Console.WriteLine(l.Count)
            End Sub
            """);
        var calls = BindingSiteIr.Instructions(module).OfType<IRInstanceMethodCall>().Select(c => c.MethodName).ToList();
        Assert.That(calls, Is.EqualTo(new[] { "Add" }));
    }
}

// ================================================================================================
//  The counted For: which storage it drives
// ================================================================================================

/// <summary>
/// ADR-0013 D3/D6 for a counted <c>For</c> with NO <c>As</c>: it drives whatever its control name
/// already denotes — the ANALYZER's answer (<c>ForStatementNode.ControlReference</c>), reached by
/// the declared spelling — in any case; the <c>As</c> form declares a new variable; and the
/// Ordinal <c>ResolvesToExistingStorage</c> is asked only when the analyzer bound no storage.
///
/// <para>Kills: M07/M14 (the loop does not drive bound storage / the analyzer never records the
/// control) — the four <c>…DrivesTheDeclared…</c> rows; M08 (the increment writes back under the
/// written spelling) — the same rows, which assert every name in the loop; M16 (the Ordinal
/// fallback dropped) — <see cref="AnEarlierClosedBlockLocal_IsNotDeclaredTwice"/>; M13 (For Each
/// reuse decided Ordinal) — <see cref="AForEachOverAField_ReusesItAsDeclared"/>.</para>
/// </summary>
[TestFixture]
public class NameBindingForDecisionSiteTests
{
    private static ForLoopNode Loop(string source, out SemanticAnalyzer analyzer, out ProgramNode ast)
    {
        (ast, analyzer) = NameBindingProbe.Analyze(source);
        return BindingSiteIr.Sole<ForLoopNode>(ast);
    }

    // ---- the four kinds of storage, each in another case ---------------------------------------

    private const string OverLocal = """
        Sub Main()
            Dim Total As Integer = 100
            For total = 1 To 3
            Next
            Console.WriteLine(Total)
        End Sub
        """;

    private const string OverParameter = """
        Sub Run(N As Integer)
            For n = 1 To 2
            Next
            Console.WriteLine(N)
        End Sub
        Sub Main()
            Run(50)
        End Sub
        """;

    private const string OverField = """
        Class C
            Private Total As Integer = 50
            Public Function Run() As Integer
                For total = 1 To 3
                    Console.WriteLine(TOTAL)
                Next
                Return Total
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """;

    private const string OverFileScopeGlobal = """
        Dim G As Integer = 0
        Sub Bump()
            For g = 1 To 3
            Next
        End Sub
        Sub Main()
            Bump()
            Console.WriteLine(G)
        End Sub
        """;

    private const string OverModuleGlobal = """
        Module Data
            Public G As Integer = 0
        End Module
        Sub Bump()
            For g = 1 To 3
            Next
        End Sub
        Sub Main()
            Bump()
            Console.WriteLine(Data.G)
        End Sub
        """;

    private static string SameCase(string source, params string[] pairs)
    {
        for (var i = 0; i < pairs.Length; i += 2) source = source.Replace(pairs[i], pairs[i + 1]);
        return source;
    }

    [Test]
    public void ALoopOverALocal_DrivesTheDeclaredLocal_NotASecondOne()
    {
        var control = SameCase(OverLocal, "For total", "For Total");
        BindingSiteIr.AssertBindsAsDeclared(OverLocal, control, declared: new[] { "Total" }, written: new[] { "total" });

        var main = BindingSiteIr.Function(BindingSiteIr.Build(OverLocal), "Main");
        Assert.That(main.LocalVariables.Select(v => v.Name), Is.EqualTo(new[] { "Total" }),
            "one local — the loop declared none of its own");
    }

    [Test]
    public void ALoopOverAParameter_DrivesTheDeclaredParameter_NotALocal()
    {
        var control = SameCase(OverParameter, "For n", "For N");
        BindingSiteIr.AssertBindsAsDeclared(OverParameter, control, declared: new[] { "N" }, written: new[] { "n" });

        var run = BindingSiteIr.Function(BindingSiteIr.Build(OverParameter), "Run");
        Assert.That(run.LocalVariables, Is.Empty, "the parameter is the loop's storage: no local");
    }

    [Test]
    public void ALoopOverAField_DrivesTheDeclaredField()
    {
        var control = SameCase(OverField, "For total", "For Total", "(TOTAL)", "(Total)");
        BindingSiteIr.AssertBindsAsDeclared(OverField, control, declared: new[] { "Total" }, written: new[] { "total", "TOTAL" });

        var run = BindingSiteIr.Function(BindingSiteIr.Build(OverField), "Run");
        Assert.That(run.LocalVariables, Is.Empty, "a field drives the loop: the loop declares no local");
    }

    [Test]
    public void ALoopOverAFileScopeGlobal_DrivesTheDeclaredGlobal()
    {
        var control = SameCase(OverFileScopeGlobal, "For g", "For G");
        BindingSiteIr.AssertBindsAsDeclared(OverFileScopeGlobal, control, declared: new[] { "G" }, written: new[] { "g" });

        var module = BindingSiteIr.Build(OverFileScopeGlobal);
        Assert.Multiple(() =>
        {
            Assert.That(BindingSiteIr.Function(module, "Bump").LocalVariables, Is.Empty);
            Assert.That(module.GlobalVariables.Values.Select(v => v.Name), Is.EquivalentTo(new[] { "G" }));
        });
    }

    [Test]
    public void ALoopOverAModuleGlobal_DrivesTheDeclaredGlobal()
    {
        var control = SameCase(OverModuleGlobal, "For g", "For G");
        BindingSiteIr.AssertBindsAsDeclared(OverModuleGlobal, control, declared: new[] { "G" }, written: new[] { "g" });

        var module = BindingSiteIr.Build(OverModuleGlobal);
        var bump = BindingSiteIr.Function(module, "Bump");
        Assert.Multiple(() =>
        {
            Assert.That(bump.LocalVariables, Is.Empty, "the module's variable drives the loop");
            Assert.That(BindingSiteIr.Instructions(bump).OfType<IRAssignment>().Select(a => a.Target.Name),
                Has.All.EqualTo("G"), "the start value and the increment both write G, as declared");
        });
    }

    /// <summary>
    /// A counted For and a For Each over a Module variable of ANOTHER FILE, bare, in another case: the loops drive the imported
    /// global by its DECLARED spelling (<c>GlobalVariable</c>'s imported arm, reached only through <c>BoundVariable</c> — the For
    /// control asks it directly). (MF5. ⚠ The increment's plain <c>%Total.1</c> is #246, unchanged here: only spellings are asserted.)
    /// </summary>
    [Test]
    public void ALoopOverAnotherFilesGlobal_DrivesTheDeclaredGlobal()
    {
        const string data = """
            Module Data
                Public Item As Integer = 0
                Public Total As Integer = 0
            End Module
            """;
        var variant = BindingSiteIr.CompileProject(("Data.bas", data), ("Main.bas", """
            Sub Main()
                For Each item In New Integer() {4, 6}
                Next
                For total = 1 To 3
                Next
                Console.WriteLine(Data.Item + Data.Total)
            End Sub
            """));
        var control = BindingSiteIr.CompileProject(("Data.bas", data), ("Main.bas", """
            Sub Main()
                For Each Item In New Integer() {4, 6}
                Next
                For Total = 1 To 3
                Next
                Console.WriteLine(Data.Item + Data.Total)
            End Sub
            """));
        var main = variant.Units.Single(u => Path.GetFileName(u.FilePath) == "Main.bas").IR;
        var mainControl = control.Units.Single(u => Path.GetFileName(u.FilePath) == "Main.bas").IR;
        var names = BindingSiteIr.Names(main);
        Assert.Multiple(() =>
        {
            Assert.That(BindingSiteIr.Text(main), Is.EqualTo(BindingSiteIr.Text(mainControl)), BindingSiteIr.Text(main));
            Assert.That(names, Does.Contain("Item").And.Contain("Total"));
            Assert.That(names, Does.Not.Contain("item").And.Not.Contain("total"), BindingSiteIr.Text(main));
        });
    }

    /// <summary>Every write the loop makes — its start value and its increment — targets the
    /// DECLARED spelling. (Kills M08: an increment written back under the loop's own spelling.)</summary>
    [Test]
    public void TheIncrementWritesBackUnderTheDeclaredSpelling()
    {
        var run = BindingSiteIr.Function(BindingSiteIr.Build(OverField), "Run");
        var writes = BindingSiteIr.Instructions(run).OfType<IRAssignment>().Select(a => a.Target.Name).ToList();
        Assert.That(writes, Is.EqualTo(new[] { "Total", "Total" }), "start value, then increment");
    }

    // ---- ControlReference: the analyzer records the decision ----------------------------------

    [TestCase(OverLocal, NameBindingKind.Local, "Total", TestName = "ControlReference_Local")]
    [TestCase(OverParameter, NameBindingKind.Parameter, "N", TestName = "ControlReference_Parameter")]
    [TestCase(OverField, NameBindingKind.Field, "Total", TestName = "ControlReference_Field")]
    [TestCase(OverFileScopeGlobal, NameBindingKind.ModuleGlobal, "G", TestName = "ControlReference_FileScopeGlobal")]
    [TestCase(OverModuleGlobal, NameBindingKind.ModuleGlobal, "G", TestName = "ControlReference_ModuleGlobal")]
    public void TheAnalyzerRecordsWhatTheControlNameDenotes(string source, NameBindingKind kind, string declared)
    {
        var loop = Loop(source, out _, out _);
        Assert.That(loop.ControlReference, Is.Not.Null, "the loop's control name denotes existing storage");
        Assert.Multiple(() =>
        {
            Assert.That(loop.ControlReference!.Name, Is.EqualTo(loop.Variable), "written as the loop spells it");
            Assert.That(loop.ControlReference.Binding, Is.Not.Null);
            Assert.That(loop.ControlReference.Binding!.Kind, Is.EqualTo(kind));
            Assert.That(loop.ControlReference.Binding.DeclaredName, Is.EqualTo(declared));
            Assert.That(loop.Variable, Is.Not.EqualTo(declared), "test is broken if the loop spells it as declared");
        });
    }

    /// <summary>The reference is METADATA, not a child: it is overwritten on every analysis pass
    /// over the same AST (a SECOND analyzer here — one analyzer cannot re-register a class), and no
    /// visitor walks into it.</summary>
    [Test]
    public void TheControlReference_IsOverwrittenOnEveryPass()
    {
        var ast = new Parser(new Lexer(OverField).Tokenize()).Parse();
        Assert.That(new SemanticAnalyzer().Analyze(ast), Is.True);
        var loop = BindingSiteIr.Sole<ForLoopNode>(ast);
        var first = loop.ControlReference;
        Assert.That(first, Is.Not.Null);

        Assert.That(new SemanticAnalyzer().Analyze(ast), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(loop.ControlReference, Is.Not.Null);
            Assert.That(loop.ControlReference, Is.Not.SameAs(first), "re-recorded, not left over");
            Assert.That(loop.ControlReference!.Binding!.DeclaredName, Is.EqualTo("Total"));
        });
    }

    // ---- the As form and the fresh-variable forms ---------------------------------------------

    /// <summary>The <c>As</c> form declares a NEW variable, shadowing the field: it records no
    /// control reference, declares a local of the loop's own spelling, and writes THAT — not the
    /// field. (Unchanged by #124; this is the guard that it stays so. NB the read of the field
    /// AFTER such a loop is #247's, deliberately not part of this program.)</summary>
    [Test]
    public void TheAsForm_DeclaresANewVariable_AndNeverDrivesTheField()
    {
        const string source = """
            Class C
                Private Total As Integer = 50
                Public Sub Run()
                    For total As Integer = 1 To 3
                    Next
                End Sub
            End Class
            Sub Main()
                Dim o As New C()
                o.Run()
            End Sub
            """;
        var loop = Loop(source, out _, out _);
        var run = BindingSiteIr.Function(BindingSiteIr.Build(source), "Run");
        var writes = BindingSiteIr.Instructions(run).OfType<IRAssignment>().Select(a => a.Target.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(loop.ControlReference, Is.Null, "an As clause declares: nothing existing is denoted");
            Assert.That(run.LocalVariables.Select(v => v.Name), Is.EqualTo(new[] { "total" }), "the loop's own local");
            Assert.That(writes, Is.EqualTo(new[] { "total", "total" }), "start and increment write the new local");
        });
    }

    /// <summary>A name that denotes nothing yet is declared by the loop, spelled as the loop spells
    /// it — the analyzer records no control reference.</summary>
    [Test]
    public void ANameThatDenotesNothing_IsDeclaredByTheLoop()
    {
        const string source = """
            Sub Main()
                For j = 1 To 3
                    Console.WriteLine(J)
                Next
            End Sub
            """;
        var loop = Loop(source, out _, out _);
        var main = BindingSiteIr.Function(BindingSiteIr.Build(source), "Main");
        Assert.Multiple(() =>
        {
            Assert.That(loop.ControlReference, Is.Null);
            Assert.That(main.LocalVariables.Select(v => v.Name), Is.EqualTo(new[] { "j" }));
        });
    }

    /// <summary>
    /// ⭐ THE FALLBACK: <c>ResolvesToExistingStorage</c> is consulted ONLY when the analyzer bound no
    /// storage. A <c>Dim i</c> in an earlier block that has closed is invisible to the analyzer (the
    /// loop's <c>i</c> denotes nothing, so no control reference) but is already one of the function's
    /// locals — and the loop must not declare it a SECOND time. (FOsb; kills M16, which drops the
    /// Ordinal test and re-declares it: a duplicate local, CS0128 on C#.)
    /// </summary>
    [Test]
    public void AnEarlierClosedBlockLocal_IsNotDeclaredTwice()
    {
        const string source = """
            Sub Main()
                If True Then
                    Dim i As Integer = 5
                    Console.WriteLine(i)
                End If
                For i = 1 To 2
                    Console.WriteLine(i)
                Next
            End Sub
            """;
        var loop = Loop(source, out _, out _);
        var main = BindingSiteIr.Function(BindingSiteIr.Build(source), "Main");
        Assert.Multiple(() =>
        {
            Assert.That(loop.ControlReference, Is.Null, "the analyzer sees no storage: the earlier Dim's scope has closed");
            Assert.That(main.LocalVariables.Select(v => v.Name), Is.EqualTo(new[] { "i" }), "declared once");
        });
    }

    /// <summary>
    /// A Method or Type name is not storage: <c>IsStorageBinding</c> refuses it, and the loop
    /// declares its own variable, spelled as written, as it always did. ⚠ VB REFUSES this program
    /// (BC30068/BC30311), so this row pins the documented decision in <c>IsStorageBinding</c>, not
    /// a VB result.
    /// </summary>
    [Test]
    public void AControlNamedLikeAMethod_DeclaresItsOwnVariable()
    {
        const string source = """
            Sub Total()
            End Sub
            Sub Main()
                For total = 1 To 3
                Next
            End Sub
            """;
        var loop = Loop(source, out _, out _);
        var main = BindingSiteIr.Function(BindingSiteIr.Build(source), "Main");
        Assert.Multiple(() =>
        {
            Assert.That(loop.ControlReference?.Binding?.Kind, Is.EqualTo(NameBindingKind.Method), "recorded — as a name");
            Assert.That(main.LocalVariables.Select(v => v.Name), Is.EqualTo(new[] { "total" }), "the loop's own local");
        });
    }

    // ---- For Each (the analyzer's own reuse decision) ----------------------------------------

    /// <summary>
    /// A bare <c>For Each item</c> over a field <c>Item</c> REUSES it (ADR-0009), in any case: the
    /// element lands in the field, spelled as declared. The reuse decision is the analyzer's and
    /// case-insensitive; #124 leaves it alone. (FEf; kills M13, an Ordinal reuse test.)
    /// </summary>
    [Test]
    public void AForEachOverAField_ReusesItAsDeclared()
    {
        const string variant = """
            Class C
                Private Item As Integer
                Public Function Run() As Integer
                    For Each item In New Integer() {4, 5, 6}
                        Console.WriteLine(ITEM)
                    Next
                    Return Item
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New C().Run())
            End Sub
            """;
        var control = variant.Replace("For Each item", "For Each Item").Replace("(ITEM)", "(Item)");
        BindingSiteIr.AssertBindsAsDeclared(variant, control, declared: new[] { "Item" }, written: new[] { "item", "ITEM" });

        var (ast, analyzer) = NameBindingProbe.Analyze(variant);
        Assert.That(analyzer.ForEachControlBindings.TryGetValue(BindingSiteIr.Sole<ForEachLoopNode>(ast), out _), Is.True,
            "the analyzer decided the loop reuses existing storage");
    }
}

// ================================================================================================
//  The internal compiler error, and the deliberate ABSENCE of one
// ================================================================================================

/// <summary>
/// ADR-0013 D1/D3: a bound Local/Parameter/LambdaParameter or FILE-SCOPE ModuleGlobal reference
/// whose declaration is not registered is an internal compiler error — never a silent create. A
/// bound Field or Property whose declaration the IR builder never registered is DELIBERATELY not
/// (the implementer's documented deviation from the orchestrator's Q3): the analyzer's own
/// declaration is the evidence the member exists.
///
/// <para>⚠ A file-scope global's miss cannot be reached from source — the analyzer refuses a
/// file-scope variable used before its declaration, and the declaration site always registers it —
/// so the positive case tampers the recorded binding, the idiom
/// <c>NameBindingConsumptionTests.BoundMiss_TamperedDeclaredName…</c> uses for a Local. The
/// front end is real; only the binding is hand-built.</para>
/// </summary>
[TestFixture]
public class NameBindingMissTests
{
    private const string FileScope = """
        Dim G As Integer = 1
        Sub Main()
            Console.WriteLine(g)
        End Sub
        """;

    private static IdentifierExpressionNode Reference(ProgramNode ast, string name, int line)
        => NameBindingProbe.SoleReferenceOnLine(ast, name, line);

    [Test]
    public void AFileScopeGlobalMiss_IsAnInternalCompilerError()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze(FileScope);
        var reference = Reference(ast, "g", 3);
        Assert.That(reference.Binding!.Kind, Is.EqualTo(NameBindingKind.ModuleGlobal));
        Assert.That(reference.Binding.Declaration.OwningModule, Is.Null.Or.Empty, "test is broken: a Module's, not a file-scope one");
        Assert.That(reference.Binding.Declaration.IsImported, Is.False);
        reference.Binding = reference.Binding with { DeclaredName = "zzz_unregistered" };

        var ex = Assert.Throws<InvalidOperationException>(() => new IRBuilder(analyzer).Build(ast, "T"));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("Internal compiler error"));
            Assert.That(ex.Message, Does.Contain("zzz_unregistered"));
            Assert.That(ex.Message, Does.Contain("#124"));
            Assert.That(ex.Message, Does.Contain("module-level"));
        });
    }

    /// <summary>The negative: the same program, untampered, builds — the reference resolves to the
    /// global its declaration registered.</summary>
    [Test]
    public void AFileScopeGlobal_ThatIsRegistered_IsNeverAnError()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze(FileScope);
        IRModule module = null!;
        Assert.DoesNotThrow(() => module = new IRBuilder(analyzer).Build(ast, "T"));
        Assert.That(BindingSiteIr.Names(module), Does.Contain("G"));
    }

    /// <summary>
    /// The deviation, pinned: tamper a FIELD reference's declared name so nothing in the IR
    /// registers it. NO error — the variable is made, spelled as recorded — because a member has no
    /// IR-side registration that is complete at the reference (see
    /// <see cref="TheUnregisteredMemberShapes_StillCompile"/>).
    /// </summary>
    [Test]
    public void AFieldWhoseDeclarationTheIrNeverRegistered_IsNotAnInternalCompilerError()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Class C
                Private Total As Integer = 5
                Public Function Run() As Integer
                    Return Total
                End Function
            End Class
            """);
        var reference = Reference(ast, "Total", 4);
        Assert.That(reference.Binding!.Kind, Is.EqualTo(NameBindingKind.Field));
        reference.Binding = reference.Binding with { DeclaredName = "Elsewhere" };

        IRModule module = null!;
        Assert.DoesNotThrow(() => module = new IRBuilder(analyzer).Build(ast, "T"));
        Assert.That(BindingSiteIr.Names(module), Does.Contain("Elsewhere"),
            "the reference is honoured under the spelling the analyzer recorded");
    }

    [Test]
    public void APropertyWhoseDeclarationTheIrNeverRegistered_IsNotAnInternalCompilerError()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Class C
                Public Property Count As Integer
                Public Function Run() As Integer
                    Return Count
                End Function
            End Class
            """);
        var reference = Reference(ast, "Count", 4);
        Assert.That(reference.Binding!.Kind, Is.EqualTo(NameBindingKind.Property));
        reference.Binding = reference.Binding with { DeclaredName = "Elsewhere" };

        Assert.DoesNotThrow(() => new IRBuilder(analyzer).Build(ast, "T"));
    }

    /// <summary>A Module's variable that the IR has not registered is a FORWARD reference, not an
    /// error: a Module may be declared after its use (MGw), so an absent declaration is normal.</summary>
    [Test]
    public void AModuleGlobalNotYetDeclared_IsAForwardReference_NotAnInternalCompilerError()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Sub Main()
                Console.WriteLine(Counter)
            End Sub
            Module Data
                Public Counter As Integer = 7
            End Module
            """);
        var reference = Reference(ast, "Counter", 2);
        Assert.That(reference.Binding!.Declaration.OwningModule, Is.EqualTo("Data"));
        reference.Binding = reference.Binding with { DeclaredName = "Elsewhere" };
        Assert.DoesNotThrow(() => new IRBuilder(analyzer).Build(ast, "T"));
    }

    /// <summary>
    /// The shapes the deviation exists for — a bound member the IR builder has no registration for
    /// at the reference — that a program reaches from SOURCE: an enclosing class's Shared field read
    /// from a nested class (the nested class is a separate IR class that never declares it), and a
    /// base declared AFTER its derived class (the IR class list is filled in declaration order, so
    /// the base is not there yet). Each still builds, and each reference lowers to the declared name.
    ///
    /// <para>⚠ Not reachable from source: a Structure's member (a Structure had fields only — no
    /// method body could name one bare; STALE since #230, a Structure declares methods now, and
    /// <c>StructureMembersExecutionTests</c> runs a bare field write and a bare self-call inside
    /// one; whether either takes this IR-miss path was not measured here), and a base class in ANOTHER FILE (refused today:
    /// <c>InheritedMemberTests.ACrossFileBaseClass_IsNotFound_Pinned</c>). Both are covered by the
    /// hand-built rows above.</para>
    /// </summary>
    [Test]
    public void TheUnregisteredMemberShapes_StillCompile()
    {
        var nested = BindingSiteIr.Build("""
            Class Outer
                Private Shared Count As Integer = 5
                Public Class Inner
                    Public Function Twice() As Integer
                        Return COUNT * 2
                    End Function
                End Class
            End Class
            Sub Main()
                Dim i As New Inner()
                Console.WriteLine(i.Twice())
            End Sub
            """);
        var baseLater = BindingSiteIr.Build("""
            Class Derived
                Inherits Base
                Public Function Run() As Integer
                    total = total * 2
                    Return TOTAL
                End Function
            End Class
            Class Base
                Protected Total As Integer = 4
            End Class
            Sub Main()
                Console.WriteLine(New Derived().Run())
            End Sub
            """);
        Assert.Multiple(() =>
        {
            Assert.That(BindingSiteIr.Names(nested), Does.Contain("Count").And.Not.Contain("COUNT"), "nested class, enclosing Shared field");
            Assert.That(BindingSiteIr.Names(baseLater), Does.Contain("Total").And.Not.Contain("total").And.Not.Contain("TOTAL"),
                "base declared after its derived class");
        });
    }
}
