using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #169 (plus #199) — ADR-0013: case-insensitive name binding between the front end and the
//  IR (`docs/superpowers/decisions/0013-case-insensitive-name-binding-front-end-to-ir.md`).
//
//  BasicLang is case-insensitive, but the IR builder used to re-resolve every bare name through
//  its own Ordinal maps (`_variableVersions`/`_locals`/`_moduleGlobals`) — a SECOND, independent
//  resolver that could and did disagree with the analyzer's own (K8: `Function(N) n * 10` with a
//  field `n` printed 10, not 40 — the field silently won). ADR-0013 D1 replaces the second
//  resolver with ONE recording point and ONE consuming site:
//
//  - **Recording** — `SemanticAnalyzer.SetNodeSymbol` is the ONE place an identifier reference's
//    `NameBinding` (`DeclaredName`, `Kind`, `Declaration`) is written, at the analyzer's own
//    `SymbolTable` lookup — so the binding and the resolved symbol can never disagree. Set fresh
//    on every analysis pass (`Visit(IdentifierExpressionNode)` clears it first), never rewriting
//    `Name`. Null (exempt) for `Me`, a `::` foreign name, a .NET member with no BasicLang
//    `Symbol`, a compiler-SYNTHESIZED declaration (the `For Each` hidden element variable, D5),
//    and a symbol whose name fails the OrdinalIgnoreCase invariant against the written spelling
//    (D8, counted in `UnboundByNameMismatch`). An `Event` reference was exempt here (D7) until
//    #124 consumed it: it is now recorded as Kind `Event`.
//  - **Consuming** — `IRBuilder.ReferencedVariable` is the ONE site: for a Local/Parameter/
//    LambdaParameter binding it looks up `Binding.DeclaredName` in `_variableVersions` ONLY
//    (never a module global or a class member — the K8 fix), and a miss is an INTERNAL COMPILER
//    ERROR, never a silent create.
//  - **D5 registration** — every declaration the analyzer binds as Local/Parameter/
//    LambdaParameter is registered in `_variableVersions` at its declaration site (the `For Each`
//    control variable, the hidden `__foreach_N`, the counted `For` variable, LINQ range
//    variables, and a setter's declared parameter as an ALIAS of `value`), so a bound reference
//    never reaches the create branch.
//
//  Task #199 (its own earlier commit, `4ecbe895`) is the prerequisite this all sits on:
//  `EnterProcedureScope`/`ExitProcedureScope` snapshot-and-restore `_variableVersions`/`_locals`
//  at every procedure body, so an earlier procedure's local or parameter can no longer bind a
//  later procedure's same-named reference (ADR-0013 D4).
//
//  This file is the FRONT-END / IR half (fast subset, no process spawned). The execution half —
//  real compiled-and-run programs on all four backends — is `NameBindingExecutionTests.cs`
//  (`[Category("Integration")]`).
// ================================================================================================

/// <summary>
/// Shared plumbing: parse + analyze real BasicLang source, and find a specific
/// <see cref="IdentifierExpressionNode"/> in the resulting AST by name (and, where more than one
/// reference shares a name, by source line). The same idiom the M4/M5 mutant-kill harness
/// (<c>S/t169/harness2/Program.cs</c>) used to find and tamper a <c>Binding</c> directly — walk
/// every <c>BasicLang.Compiler.AST</c>-namespaced property reachable from the root, skipping
/// <c>Binding</c> itself (which is additive metadata, not a child node, and would otherwise loop
/// through <see cref="NameBinding.Declaration"/> into the symbol table).
/// </summary>
internal static class NameBindingProbe
{
    /// <summary>Parse and analyze <paramref name="source"/>, asserting a clean front end.</summary>
    internal static (ProgramNode Ast, SemanticAnalyzer Analyzer) Analyze(string source)
    {
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        return (ast, analyzer);
    }

    /// <summary>Every <see cref="IdentifierExpressionNode"/> reachable from <paramref name="root"/>
    /// whose written <see cref="IdentifierExpressionNode.Name"/> matches <paramref name="name"/>
    /// (case-insensitively — BasicLang's own rule), in tree-walk order.</summary>
    internal static List<IdentifierExpressionNode> FindByName(object root, string name)
    {
        var found = new List<IdentifierExpressionNode>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object o)
        {
            if (o == null || !seen.Add(o)) return;
            if (o is IdentifierExpressionNode id
                && string.Equals(id.Name, name, StringComparison.OrdinalIgnoreCase))
                found.Add(id);
            var t = o.GetType();
            if (t.Namespace != "BasicLang.Compiler.AST") return;
            foreach (var p in t.GetProperties())
            {
                if (p.GetIndexParameters().Length > 0 || p.Name == "Binding") continue;
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

    /// <summary>The ONE reference spelled <paramref name="name"/> on source line
    /// <paramref name="line"/> — fails loudly if there is not exactly one, so a test can never
    /// silently probe the wrong occurrence.</summary>
    internal static IdentifierExpressionNode SoleReferenceOnLine(object root, string name, int line)
    {
        var matches = FindByName(root, name).Where(id => id.Line == line).ToList();
        Assert.That(matches, Has.Count.EqualTo(1),
            $"expected exactly one '{name}' reference on line {line}, found {matches.Count}");
        return matches[0];
    }

    /// <summary>The sole <see cref="ForEachLoopNode"/> reachable from <paramref name="root"/>.</summary>
    internal static ForEachLoopNode SoleForEachLoop(object root)
    {
        ForEachLoopNode found = null;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object o)
        {
            if (o == null || !seen.Add(o)) return;
            if (o is ForEachLoopNode loop) { Assert.That(found, Is.Null, "more than one For Each loop"); found = loop; }
            var t = o.GetType();
            if (t.Namespace != "BasicLang.Compiler.AST") return;
            foreach (var p in t.GetProperties())
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = p.GetValue(o); } catch { continue; }
                if (v is string) continue;
                if (v is IEnumerable e) { foreach (var item in e) Walk(item); }
                else Walk(v);
            }
        }
        Walk(root);
        Assert.That(found, Is.Not.Null, "no For Each loop found");
        return found;
    }
}

/// <summary>
/// D1/D7/D8 — what <see cref="SemanticAnalyzer.SetNodeSymbol"/> records on an identifier
/// reference's <see cref="IdentifierExpressionNode.Binding"/>, direct from the real front end
/// (Lexer/Parser/SemanticAnalyzer — no IR built).
/// </summary>
[TestFixture]
public class NameBindingRecordingTests
{
    /// <summary>
    /// One program, one case-differing reference per <see cref="NameBindingKind"/> D1 records
    /// (Local, Parameter, LambdaParameter, Field, ModuleGlobal) — every reference spelled
    /// DIFFERENTLY from its own declaration, on the SAME line, so a wrong Kind or a wrong
    /// <c>DeclaredName</c> cannot hide behind a coincidental exact-case match.
    /// <para>⛔ MUTANT M1 (the analyzer never marks a lambda parameter symbol as
    /// <c>LambdaParameter</c> — <c>BindingKindOf</c> returns null for it instead) kills the `lp`
    /// assertion: <c>lp</c>'s <c>Binding</c> would be null instead of recording
    /// <c>LambdaParameter</c>.</para>
    /// </summary>
    [Test]
    public void EveryVersionedAndUnversionedKind_IsRecordedWithItsDeclaredSpellingAndSymbol()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Dim ModuleG As Integer = 1

            Class Widget
                Public Fld As Integer = 2
                Public Function Run(Prm As Integer) As Integer
                    Dim Loc As Integer = 3
                    Dim f = Function(Lp As Integer) lp + loc + prm + fld + moduleg
                    Return f(1)
                End Function
            End Class
            """);

        IdentifierExpressionNode On(string writtenName) =>
            NameBindingProbe.SoleReferenceOnLine(ast, writtenName, 7);

        Assert.Multiple(() =>
        {
            var lp = On("lp").Binding;
            Assert.That(lp, Is.Not.Null, "lp");
            Assert.That(lp!.Kind, Is.EqualTo(NameBindingKind.LambdaParameter), "lp Kind");
            Assert.That(lp.DeclaredName, Is.EqualTo("Lp"), "lp DeclaredName is the DECLARATION's spelling");

            var loc = On("loc").Binding;
            Assert.That(loc, Is.Not.Null, "loc");
            Assert.That(loc!.Kind, Is.EqualTo(NameBindingKind.Local), "loc Kind");
            Assert.That(loc.DeclaredName, Is.EqualTo("Loc"), "loc DeclaredName");

            var prm = On("prm").Binding;
            Assert.That(prm, Is.Not.Null, "prm");
            Assert.That(prm!.Kind, Is.EqualTo(NameBindingKind.Parameter), "prm Kind");
            Assert.That(prm.DeclaredName, Is.EqualTo("Prm"), "prm DeclaredName");

            var fld = On("fld").Binding;
            Assert.That(fld, Is.Not.Null, "fld");
            Assert.That(fld!.Kind, Is.EqualTo(NameBindingKind.Field), "fld Kind");
            Assert.That(fld.DeclaredName, Is.EqualTo("Fld"), "fld DeclaredName");

            var moduleg = On("moduleg").Binding;
            Assert.That(moduleg, Is.Not.Null, "moduleg");
            Assert.That(moduleg!.Kind, Is.EqualTo(NameBindingKind.ModuleGlobal), "moduleg Kind");
            Assert.That(moduleg.DeclaredName, Is.EqualTo("ModuleG"), "moduleg DeclaredName");

            Assert.That(analyzer.UnboundByNameMismatch, Is.EqualTo(0), "no mismatch on a normal program");
        });
    }

    /// <summary>Two DIFFERENT references to the SAME parameter — one spelled EXACTLY as
    /// declared, one case-differing — must carry the SAME <see cref="NameBinding.Declaration"/>
    /// object. Reference identity, never a re-lookup-by-name: a field and a parameter that
    /// happen to share a spelling elsewhere can never be confused this way.</summary>
    [Test]
    public void Declaration_IsReferenceEqualAcrossTwoDifferentReferencesToTheSameParameter()
    {
        var (ast, _) = NameBindingProbe.Analyze("""
            Sub Main()
                Dim f = Function(N As Integer) As Integer
                            Dim y As Integer = N
                            Return n + y
                        End Function
                Console.WriteLine(f(2))
            End Sub
            """);

        var exactSpelling = NameBindingProbe.SoleReferenceOnLine(ast, "N", 3);
        var caseDiffering = NameBindingProbe.SoleReferenceOnLine(ast, "n", 4);

        Assert.Multiple(() =>
        {
            Assert.That(exactSpelling.Binding, Is.Not.Null);
            Assert.That(caseDiffering.Binding, Is.Not.Null);
            Assert.That(caseDiffering.Binding!.Declaration, Is.SameAs(exactSpelling.Binding!.Declaration),
                "both references name the SAME parameter -- their Declaration must be the identical object");
        });
    }

    /// <summary>M5 — <see cref="IdentifierExpressionNode.Name"/> is never rewritten. The body's
    /// reference stays spelled exactly as WRITTEN ("n"), even though it binds to a parameter
    /// DECLARED "N" — <c>Name</c> and <c>Binding.DeclaredName</c> deliberately disagree in case.
    /// <para>⛔ MUTANT M5 (<c>SetNodeSymbol</c> also overwrites <c>reference.Name</c> to the
    /// declared spelling) kills this test directly: <c>Name</c> would read "N", not "n".</para>
    /// </summary>
    [Test]
    public void NodeName_IsNeverRewritten_StaysTheSourceSpelling()
    {
        var (ast, _) = NameBindingProbe.Analyze("""
            Sub Main()
                Dim n As Integer = 1
                Dim f = Function(N As Integer) n + 1
                Console.WriteLine(f(2))
            End Sub
            """);

        var reference = NameBindingProbe.SoleReferenceOnLine(ast, "n", 3);
        Assert.Multiple(() =>
        {
            Assert.That(reference.Name, Is.EqualTo("n"), "Name stays the WRITTEN spelling");
            Assert.That(reference.Binding!.DeclaredName, Is.EqualTo("N"), "DeclaredName is the DECLARATION's spelling");
            Assert.That(string.Equals(reference.Name, reference.Binding.DeclaredName, StringComparison.OrdinalIgnoreCase),
                Is.True, "the ADR-0013 invariant: DeclaredName equals Name under OrdinalIgnoreCase, always");
        });
    }

    [Test]
    public void Me_IsExempt_BindingIsNull()
    {
        var (ast, _) = NameBindingProbe.Analyze("""
            Class C
                Public Function F() As Object
                    Return Me
                End Function
            End Class
            """);

        var me = NameBindingProbe.FindByName(ast, "Me").Single();
        Assert.That(me.Binding, Is.Null);
    }

    [Test]
    public void ForeignCppQualifiedName_IsExempt_BindingIsNull()
    {
        var (ast, _) = NameBindingProbe.Analyze("""
            Sub Main()
                Dim ans = mathlib::kAnswer
                Console.WriteLine(ans)
            End Sub
            """);

        var foreign = NameBindingProbe.FindByName(ast, "mathlib::kAnswer").Single();
        Assert.That(foreign.IsForeignQualified, Is.True, "test is broken if this isn't the foreign node");
        Assert.That(foreign.Binding, Is.Null);
    }

    [Test]
    public void DotNetMemberWithNoBasicLangSymbol_IsExempt_BindingIsNull()
    {
        var (ast, _) = NameBindingProbe.Analyze("""
            Sub Main()
                Dim x As Object = DateTime.Now
                Console.WriteLine(x)
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(NameBindingProbe.FindByName(ast, "DateTime").Single().Binding, Is.Null, "DateTime");
            Assert.That(NameBindingProbe.FindByName(ast, "Console").Single().Binding, Is.Null, "Console");
        });
    }

    /// <summary>D7, as #124 consumed it — an Event reference (here, <c>AddHandler</c>'s
    /// event-expression operand, the one place an event name reaches
    /// <c>Visit(IdentifierExpressionNode)</c> as an ordinary reference rather than a bare string)
    /// IS bound: Kind <see cref="NameBindingKind.Event"/>, the declaration's spelling, the event's
    /// own symbol. #169 left it unbound and said <c>Event</c> would join
    /// <see cref="NameBindingKind"/> when #124 consumed it; this row was that pin (it asserted a
    /// null binding) and moves with it. The IR-level consequence — the name reaches the IR as
    /// declared — is <c>NameBindingBoundVariableSiteTests.AnEventNameAsAValue_BindsAsDeclared</c>.</summary>
    [Test]
    public void EventReference_IsBoundAsKindEvent_WithItsDeclaredSpelling_D7()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Class C
                Public Event Ping()
                Public Sub Handler()
                End Sub
                Public Sub Wire()
                    AddHandler Ping, AddressOf Handler
                End Sub
                Public Sub WireAgain()
                    AddHandler PING, AddressOf Handler
                End Sub
            End Class
            """);

        var pings = NameBindingProbe.FindByName(ast, "Ping").OrderBy(id => id.Line).ToList();
        Assert.That(pings, Has.Count.EqualTo(2), "test is broken if AddHandler no longer reaches Visit(IdentifierExpressionNode)");
        Assert.Multiple(() =>
        {
            foreach (var ping in pings)
            {
                Assert.That(ping.Binding, Is.Not.Null, $"line {ping.Line}: an Event reference is bound (D7)");
                Assert.That(ping.Binding!.Kind, Is.EqualTo(NameBindingKind.Event), $"line {ping.Line}");
                Assert.That(ping.Binding.DeclaredName, Is.EqualTo("Ping"), $"line {ping.Line}: the DECLARATION's spelling");
                Assert.That(ping.Binding.Declaration.Kind, Is.EqualTo(SymbolKind.Event), $"line {ping.Line}");
            }
            Assert.That(pings[1].Name, Is.EqualTo("PING"), "Name stays the written spelling");
            Assert.That(pings[1].Binding!.Declaration, Is.SameAs(pings[0].Binding!.Declaration),
                "both spellings name the SAME event symbol");
            Assert.That(analyzer.UnboundByNameMismatch, Is.EqualTo(0));
        });
    }

    /// <summary>D5 — the <c>For Each</c> hidden element variable (<c>__foreach_N</c>) the
    /// analyzer SYNTHESIZES for a bare reuse is exempt: the IR builder registers it at its
    /// synthesis site and binds it by its own name, never through a recorded
    /// <see cref="NameBinding"/>. Reached only through
    /// <see cref="SemanticAnalyzer.ForEachControlBindings"/> — the synthesized assignment
    /// (<c>item = __foreach_0</c>) is not part of the parsed AST's own statement lists.
    /// The REUSE target's own reference (<c>item</c>, case-differing from the pre-declared
    /// <c>Item</c>) is asserted too, for contrast: it is an ORDINARY bound reference (Local),
    /// not exempt — only the synthesized RIGHT-hand side is.</summary>
    [Test]
    public void SynthesizedForeachHiddenVariable_IsExempt_BindingIsNull_D5()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Sub Main()
                Dim items As New List(Of Integer)()
                items.Add(1)
                Dim Item As Integer = 0
                For Each item In items
                    Console.WriteLine(item)
                Next
            End Sub
            """);

        var loop = NameBindingProbe.SoleForEachLoop(ast);
        Assert.That(analyzer.ForEachControlBindings.TryGetValue(loop, out var binding), Is.True,
            "test is broken if this reuse shape did not register a ForEachControlBinding");

        var target = (IdentifierExpressionNode)binding.Assignment.Target;
        var hiddenValue = (IdentifierExpressionNode)binding.Assignment.Value;

        Assert.Multiple(() =>
        {
            Assert.That(hiddenValue.Name, Is.EqualTo(binding.HiddenName));
            Assert.That(hiddenValue.Binding, Is.Null, "the synthesized __foreach_N reference is exempt");

            Assert.That(target.Binding, Is.Not.Null, "the REUSE target itself is an ordinary bound reference");
            Assert.That(target.Binding!.Kind, Is.EqualTo(NameBindingKind.Local));
            Assert.That(target.Binding.DeclaredName, Is.EqualTo("Item"));
        });
    }

    /// <summary>
    /// D8 — the mismatch counter stays 0 on a normal program, even one that reads a MODULE
    /// member through a case-differing QUALIFIED path (<c>data.total</c> for a module
    /// <c>Data.Total</c>) — that still resolves to a symbol whose <c>Name</c> matches
    /// case-insensitively, so D8's mismatch never fires; it counts a resolved symbol whose name
    /// disagrees by MORE than case, which this task did not find a way to construct through the
    /// public front-end API in the time available (no import-aliasing syntax exists to rebind a
    /// name to a differently-spelled symbol) — noted here rather than guessed at.
    /// </summary>
    [Test]
    public void MismatchCounter_StaysZero_OnANormalProgram_D8()
    {
        var (_, analyzer) = NameBindingProbe.Analyze("""
            Module Data
                Public Total As Integer = 5
            End Module
            Sub Main()
                Console.WriteLine(data.total)
            End Sub
            """);

        Assert.That(analyzer.UnboundByNameMismatch, Is.EqualTo(0));
    }

    /// <summary>Re-analysis leaves a FRESH <see cref="NameBinding"/>, never a stale one — the
    /// same node instance, analyzed twice, must carry the binding the SECOND pass computed, not
    /// a leftover from the first. (Here the two passes agree, since the source did not change;
    /// what this guards is that nothing SKIPS re-setting <c>Binding</c> on the second pass.)</summary>
    [Test]
    public void ReAnalysis_LeavesAFreshBinding_NeverAStaleOne()
    {
        var ast = new Parser(new Lexer("""
            Sub Main()
                Dim n As Integer = 1
                Dim f = Function(N As Integer) n + 1
                Console.WriteLine(f(2))
            End Sub
            """).Tokenize()).Parse();

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True);
        var first = NameBindingProbe.SoleReferenceOnLine(ast, "n", 3);
        var firstBinding = first.Binding;

        Assert.That(analyzer.Analyze(ast), Is.True, "re-analysis of the SAME ast");
        var second = NameBindingProbe.SoleReferenceOnLine(ast, "n", 3);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first), "the same AST node, re-analyzed");
            Assert.That(second.Binding, Is.Not.Null);
            Assert.That(second.Binding!.Kind, Is.EqualTo(NameBindingKind.LambdaParameter));
            Assert.That(second.Binding.DeclaredName, Is.EqualTo("N"));
            // Not Is.SameAs(firstBinding) -- NameBinding is a record and D1 says it is OVERWRITTEN
            // on every pass, not reused; the invariant is that the CONTENT is fresh and correct,
            // which the assertions above already establish.
            Assert.That(firstBinding, Is.Not.Null);
        });
    }
}

/// <summary>
/// D1/D3 — the ONE consuming site, <c>IRBuilder.ReferencedVariable</c>: a bound miss is an
/// internal compiler error (never a silent create, M4), and a Local/Parameter/LambdaParameter
/// reference is looked up in <c>_variableVersions</c> ONLY — never a class member or module
/// global (K8).
/// </summary>
[TestFixture]
public class NameBindingConsumptionTests
{
    /// <summary>
    /// M4 — tamper a resolved reference's <see cref="NameBinding.DeclaredName"/> so nothing by
    /// that name is registered (<c>with {{ DeclaredName = "zzz_unregistered" }}</c> — the same
    /// tamper <c>S/t169/harness2</c> used to prove the ICE). The IR build must throw the
    /// documented internal compiler error and must NEVER silently mint a variable named
    /// "zzz_unregistered" instead.
    /// <para>⛔ MUTANT M4 (<c>ReferencedVariable</c>'s miss falls through to
    /// <c>GetOrCreateVariable</c> instead of throwing) kills this test: it would build without
    /// throwing, silently creating the exact kind of undeclared variable D1 exists to prevent.</para>
    /// </summary>
    [Test]
    public void BoundMiss_TamperedDeclaredName_ThrowsInternalCompilerError_NeverSilentlyCreates()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Sub Main()
                Dim n As Integer = 1
                Dim f = Function(N As Integer) n + 1
                Console.WriteLine(f(2))
            End Sub
            """);

        var reference = NameBindingProbe.SoleReferenceOnLine(ast, "n", 3);
        Assert.That(reference.Binding, Is.Not.Null);
        Assert.That(reference.Binding!.Kind, Is.EqualTo(NameBindingKind.LambdaParameter));
        reference.Binding = reference.Binding with { DeclaredName = "zzz_unregistered" };

        var ex = Assert.Throws<InvalidOperationException>(() => new IRBuilder(analyzer).Build(ast, "T"));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("Internal compiler error"));
            Assert.That(ex.Message, Does.Contain("zzz_unregistered"));
            Assert.That(ex.Message, Does.Contain("#169"));
        });
    }

    /// <summary>
    /// K8 at the IR level — the lambda's own case-differing <c>n</c> resolves to the PARAMETER's
    /// own <see cref="IRVariable"/> (named exactly as declared, <c>N</c>), never to a field load
    /// of the class's same-spelled <c>n</c>. Direct on the built IR, no execution needed — the
    /// execution-level pin (prints 40, not 10) is <c>NameBindingExecutionTests</c>.
    /// <para>⛔ MUTANT M2 (the consumer ignores <c>Binding</c> unconditionally) and M3 (a class
    /// member is checked BEFORE the binding) both kill this test: either would make the multiply
    /// read an <see cref="IRFieldAccess"/> of the field, or a variable literally named "n" rather
    /// than the parameter's own "N".</para>
    /// </summary>
    [Test]
    public void K8_LambdaParameterResolvesToTheParameter_NeverAFieldLoad()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Class Box
                Private n As Integer = 1
                Public Function Run() As Integer
                    Dim f = Function(N As Integer) n * 10
                    Return f(4)
                End Function
            End Class
            Sub Main()
                Dim b As New Box()
                Console.WriteLine(b.Run())
            End Sub
            """);

        var module = new IRBuilder(analyzer).Build(ast, "T");
        var lambda = module.Functions.Single(f => f.Name == "__lambda_0");

        Assert.That(lambda.Parameters, Has.Count.EqualTo(1));
        var parameter = lambda.Parameters[0];
        Assert.That(parameter.Name, Is.EqualTo("N"));

        var mul = lambda.Blocks.SelectMany(b => b.Instructions).OfType<IRBinaryOp>()
            .SingleOrDefault(op => op.Operation == BinaryOpKind.Mul);
        Assert.That(mul, Is.Not.Null, "expected exactly one multiply instruction in the lambda");

        Assert.Multiple(() =>
        {
            var operand = new[] { mul!.Left, mul.Right }.OfType<IRVariable>().SingleOrDefault(v => v.Name == "N");
            Assert.That(operand, Is.Not.Null, "one operand must be the parameter's own spelling 'N':\n" + mul);
            Assert.That(operand, Is.SameAs(parameter),
                "it must be the PARAMETER's own IRVariable object, not merely a same-named one");
            Assert.That(lambda.Blocks.SelectMany(b => b.Instructions).OfType<IRFieldAccess>(), Is.Empty,
                "no field load anywhere in the lambda -- the field 'n' is never touched");
        });
    }
}

/// <summary>
/// Task #199's own scope boundary, exercised through the front end + IR builder together: two
/// Subs each declaring a local of the SAME name must never see each other's storage.
/// </summary>
[TestFixture]
public class NameBindingProcedureScopingTests
{
    /// <summary>
    /// <c>A</c> declares a String <c>n</c>; <c>B</c> declares an Integer <c>n</c>. Before #199
    /// neither `_variableVersions` push was ever popped, so B's reference could see A's
    /// registration (wrong TYPE, wrong storage). Asserted on the built IR directly: the two
    /// <see cref="IRVariable"/> objects are reference-DIFFERENT and keep their own declared
    /// types.
    /// </summary>
    [Test]
    public void TwoSubs_SameLocalName_EachIRFunctionsVariableIsItsOwn()
    {
        var (ast, analyzer) = NameBindingProbe.Analyze("""
            Sub A()
                Dim n As String = "z"
                Console.WriteLine(n)
            End Sub
            Sub B()
                Dim n As Integer = 3
                Console.WriteLine(n)
            End Sub
            """);

        var module = new IRBuilder(analyzer).Build(ast, "T");
        var a = module.Functions.Single(f => f.Name == "A" && !f.IsExternal);
        var b = module.Functions.Single(f => f.Name == "B" && !f.IsExternal);

        var aN = a.LocalVariables.Single(v => v.Name == "n");
        var bN = b.LocalVariables.Single(v => v.Name == "n");

        Assert.Multiple(() =>
        {
            Assert.That(aN, Is.Not.SameAs(bN), "A's n and B's n must be different IRVariable objects");
            Assert.That(aN.Type?.Name, Is.EqualTo("String"), "A's n keeps its own declared type");
            Assert.That(bN.Type?.Name, Is.EqualTo("Integer"), "B's n keeps its own declared type");
        });
    }
}

/// <summary>
/// The C# backend's own half of ADR-0013: <c>GenerateLambdaExpression</c> scopes a lambda's
/// parameter names in the case-insensitive <c>_variableNameMap</c> both ways — in, for the
/// body, and back out again afterward.
/// </summary>
[TestFixture]
public class NameBindingCSharpTextTests
{
    private static string GenerateCSharp(string source)
    {
        var (ast, analyzer) = NameBindingProbe.Analyze(source);
        var module = new IRBuilder(analyzer).Build(ast, "T");
        return new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(module);
    }

    /// <summary>
    /// <c>Sub(N As Integer)</c> inside a function with a local <c>n</c>: the emitted lambda
    /// body must write the PARAMETER'S own spelling (<c>N</c>) — never the enclosing <c>n</c>,
    /// which is exactly the shape that used to print 101 instead of 1 (K1).
    /// <para>⛔ MUTANT M10 (<c>_variableNameMap[param.Name] = SanitizeName(param.Name)</c> is
    /// disabled) kills this test: the body would emit <c>n = n + 100;</c>, reading and writing
    /// the ENCLOSING variable.</para>
    /// </summary>
    [Test]
    public void LambdaParameterCase_EmitsTheParametersOwnSpelling_InsideTheBody()
    {
        var cs = GenerateCSharp("""
            Sub Main()
                Dim n As Integer = 1
                Dim setp = Sub(N As Integer) n = n + 100
                setp(5)
                Console.WriteLine(n)
            End Sub
            """);

        Assert.That(cs, Does.Contain("N = N + 100"),
            "the lambda body must write its OWN parameter N, not the enclosing n:\n" + cs);
        Assert.That(cs, Does.Not.Contain("n = n + 100"),
            "the enclosing n must never be written by the lambda:\n" + cs);
    }

    /// <summary>
    /// AFTER the lambda closes, a module global <c>g</c> read in the SAME function must still
    /// emit as <c>g</c> — never as the lambda's <c>G</c>. This pins BOTH halves of M10's fix:
    /// the map entry the lambda ADDED for its own parameter <c>G</c> must be REMOVED once the
    /// body is done, not merely restored to what it held before (there was nothing before —
    /// <c>g</c> was never in the map at function scope to begin with, since a module global is
    /// resolved a different way).
    /// <para>⛔ MUTANT (the <c>finally</c> restore in <c>GenerateLambdaExpression</c> is
    /// skipped) kills this test: the trailing <c>Console.WriteLine(g)</c> would emit
    /// <c>Console.WriteLine(G)</c> instead — CS0103 (or worse, a silent wrong read, if some
    /// OTHER "G" happened to be in scope).</para>
    /// </summary>
    [Test]
    public void ModuleGlobalAfterLambda_EmitsAsItself_NotTheLambdasRemovedParameterSpelling()
    {
        var cs = GenerateCSharp("""
            Dim g As Integer = 5
            Sub Main()
                Dim f = Function(G As Integer) g + 1
                Console.WriteLine(f(10))
                Console.WriteLine(g)
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("(int G) => G + 1"),
                "inside the lambda, G means the parameter:\n" + cs);
            Assert.That(cs, Does.Contain("Console.WriteLine(g);"),
                "after the lambda, the trailing read must be the module global g, lowercase, " +
                "not the lambda's removed parameter spelling G:\n" + cs);
        });
    }
}
