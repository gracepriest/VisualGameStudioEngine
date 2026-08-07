using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class JavaScriptInteropTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_JsInterop_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Test]
    public void JsImport_IsCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(module.JsImports.Select(i => i.Specifier), Is.EqualTo(new[] { "./chart.js" }));
        Assert.That(module.JsImports.Single().IsSideEffectOnly, Is.True,
            "a bare #JsImport binds no names — correct ES, not a shortfall");
    }

    /// <summary>The directive name is matched case-insensitively, like every other one.</summary>
    [Test]
    public void JsImport_MixedCaseDirective_IsCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#jsimport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(module.JsImports.Select(i => i.Specifier), Is.EqualTo(new[] { "./a.js" }));
    }

    /// <summary>
    /// An import gated behind an inactive conditional must not be collected.
    ///
    /// ⛔ #IfDef, NOT #If. The Preprocessor implements #IfDef/#IfNDef/#Else/#EndIf; `#If` is a
    /// LEXER/parser construct and never reaches the directive collector, so a test written
    /// with #If fails through JsTestSupport's parse guard no matter how correct the
    /// implementation is.
    /// </summary>
    [Test]
    public void JsImport_InsideInactiveConditional_IsNotCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#IfDef NEVER_DEFINED\n#JsImport \"./nope.js\"\n#EndIf\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        Assert.That(module.JsImports, Is.Empty);
    }

    /// <summary>
    /// The directive must be COMMENTED OUT, not removed. A source map built from
    /// IRInstruction.SourceLine is off by one for the whole file otherwise — exactly the
    /// .mod/.cls off-by-one class of bug this backend already had to fix once.
    /// </summary>
    [Test]
    public void JsImport_PreservesLineNumbers()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
            runPreprocessor: true, sourceFilePath: "prog.bas");

        var lines = module.Functions.Single(f => f.Name == "Main")
            .Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.SourceLine > 0).Select(i => i.SourceLine).ToList();

        Assert.That(lines, Does.Contain(3), "Console.WriteLine is on source line 3");
    }

    // ------------------------------------------------------------------
    // Rejections. All of these surface through JsTestSupport's preprocess
    // guard, which THROWS rather than returning the diagnostics.
    // ------------------------------------------------------------------

    /// <summary>
    /// Assert the preprocessor rejected a directive, by MESSAGE and not merely by "something
    /// threw".
    ///
    /// <para>⛔ <c>Throws.Exception</c> is a false green here. BuildModule throws from four
    /// separate places (preprocess, the parse guard, semantic analysis, and its own broken-test
    /// guard), so a bare Throws.Exception is satisfied by the exact OPPOSITE of the behaviour
    /// under test: drop the comment-out step and the raw <c>#JsImport ...</c> line reaches the
    /// lexer, the parse guard throws, and the test stays green while both the syntax rejection
    /// AND line preservation are broken. Matching the message pins it to the preprocessor.
    /// Same reasoning as CppPassthroughTests' <c>Does.Contain("Invalid #CppInclude")</c>.</para>
    /// </summary>
    private static void AssertRejected(string source, string expectedMessageFragment) =>
        Assert.That(() => JsTestSupport.BuildModule(source, runPreprocessor: true),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains(expectedMessageFragment));

    /// <summary>A bare path is not a module specifier — JavaScript specifiers are quoted.</summary>
    [Test]
    public void JsImport_WithoutQuotes_IsAnError()
        => AssertRejected("#JsImport ./a.js\nSub Main()\nEnd Sub", "Invalid #JsImport syntax");

    /// <summary>
    /// There are no angle-bracket module specifiers in JavaScript, so <c>&lt;./a.js&gt;</c> is a
    /// mistake rather than the alternate spelling it is for #CppInclude — which DOES accept both
    /// forms, because &lt;x&gt; and "x" mean different things to a C++ compiler.
    /// </summary>
    [Test]
    public void JsImport_AngleBrackets_IsAnError()
        => AssertRejected("#JsImport <./a.js>\nSub Main()\nEnd Sub", "Invalid #JsImport syntax");

    /// <summary>
    /// An empty specifier is rejected, not collected as "". The capture group is <c>[^"]+</c>
    /// rather than <c>[^"]*</c> deliberately; a <c>*</c> would emit <c>import "";</c>.
    /// </summary>
    [Test]
    public void JsImport_EmptySpecifier_IsAnError()
        => AssertRejected("#JsImport \"\"\nSub Main()\nEnd Sub", "Invalid #JsImport syntax");

    /// <summary>
    /// A Windows-style path is the one bad specifier that would otherwise produce no diagnostic
    /// at all: it collects, it escapes cleanly at emission, and it 404s at run time.
    /// </summary>
    [Test]
    public void JsImport_BackslashSpecifier_IsAnError()
        => AssertRejected("#JsImport \"..\\lib\\chart.js\"\nSub Main()\nEnd Sub",
            "forward slashes, not backslashes");

    // ------------------------------------------------------------------
    // END-TO-END through the REAL compiler.
    //
    // ⛔ Every test above routes through JsTestSupport.BuildModule, which HAND-COPIES
    // pre.JsImports onto the module it builds. Delete BOTH
    // `result.CombinedIR.JsImports.AddRange(_preprocessor.JsImports)` lines in Compiler.cs
    // and every one of them still passes — they read as end-to-end and are not. The two
    // tests below are the only thing holding those joins down, one per route, because the
    // single-file and project routes each have their OWN copy of the wiring and nothing but
    // a comment keeps them in step. This is the gap #CppInclude closed on purpose; see
    // CppPassthroughTests.Cpp_CppInclude_EndToEndThroughRealCompiler_EmitsInclude.
    // ------------------------------------------------------------------

    [Test]
    public void JsImport_EndToEndThroughRealCompiler_LandsOnCombinedIR()
    {
        var path = Path.Combine(_tempDir, "Program.bas");
        File.WriteAllText(path, "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub");

        // Fresh compiler per compile — the module registry is stateful.
        var result = new BasicCompiler().CompileFile(path);

        Assert.That(result.AllErrors.Select(e => e.Message), Is.Empty);
        Assert.That(result.CombinedIR, Is.Not.Null);
        Assert.That(result.CombinedIR.JsImports.Select(i => i.Specifier),
            Is.EqualTo(new[] { "./chart.js" }),
            "#JsImport must survive CompileFile's CombineIRModules -> CombinedIR.JsImports join");
    }

    /// <summary>
    /// The project route (<c>build x.blproj</c> and the IDE both land here) has its own copy of
    /// the join. Specifiers from EVERY unit accumulate, because the Preprocessor instance is
    /// shared across the whole compilation and never cleared.
    /// </summary>
    [Test]
    public void JsImport_EndToEndThroughProjectRoute_LandsOnCombinedIR()
    {
        var main = Path.Combine(_tempDir, "Program.bas");
        var helper = Path.Combine(_tempDir, "Helpers.bas");
        File.WriteAllText(main, "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub");
        File.WriteAllText(helper, "#JsImport \"./util.js\"\nSub Helper()\nEnd Sub");

        var result = new BasicCompiler().CompileProjectFiles(new[] { main, helper });

        Assert.That(result.AllErrors.Select(e => e.Message), Is.Empty);
        Assert.That(result.CombinedIR, Is.Not.Null);
        Assert.That(result.CombinedIR.JsImports.Select(i => i.Specifier),
            Is.EquivalentTo(new[] { "./chart.js", "./util.js" }),
            "#JsImport must survive CompileProjectFiles' CombineIRModules -> CombinedIR.JsImports join");
    }

    // ------------------------------------------------------------------
    // EMISSION. Collecting a specifier is worth nothing until it reaches
    // the generated JavaScript as a real ES import.
    // ------------------------------------------------------------------

    [Test]
    public void JsImport_EmitsAnImportStatement()
        => Assert.That(
            JsTestSupport.Compile("#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true),
            Does.Contain("import \"./chart.js\";"));

    /// <summary>
    /// Imports go first. ESM hoists them, so this is convention and readability rather than a
    /// correctness requirement — but the output is meant to be READ in devtools, and an import
    /// buried under function declarations reads as generated sludge.
    /// </summary>
    [Test]
    public void JsImport_ImportsPrecedeDeclarations()
    {
        var lines = JsTestSupport
            .Compile("#JsImport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true)
            .Replace("\r\n", "\n").Split('\n')
            .Where(l => l.Trim().Length > 0).ToList();

        var firstImport = lines.FindIndex(l => l.TrimStart().StartsWith("import "));
        var firstFunction = lines.FindIndex(l => l.TrimStart().StartsWith("function "));

        Assert.That(firstImport, Is.GreaterThanOrEqualTo(0), "no import emitted");
        // Guarded too: FindIndex returns -1 when absent, so without this `firstImport < -1`
        // fails with a message about ORDERING when the real cause is "nothing was emitted".
        Assert.That(firstFunction, Is.GreaterThanOrEqualTo(0), "no function emitted");
        Assert.That(firstImport, Is.LessThan(firstFunction));
    }

    /// <summary>Two files in one project may import the same module.</summary>
    [Test]
    public void JsImport_DeduplicatesRepeatedSpecifiers()
    {
        var js = JsTestSupport.Compile(
            "#JsImport \"./a.js\"\n#JsImport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(System.Text.RegularExpressions.Regex.Matches(js, @"import ""\./a\.js""").Count,
            Is.EqualTo(1));
    }

    /// <summary>
    /// A source map must still point at the right .bas lines with imports above the code —
    /// which it does only because Line() maintains _generatedLine.
    /// </summary>
    [Test]
    public void JsImport_DoesNotShiftSourceMapPositions()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
            runPreprocessor: true, sourceFilePath: "prog.bas");
        var generator = new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator();
        var js = generator.Generate(module).Replace("\r\n", "\n").Split('\n');

        var generatedLine = System.Array.FindIndex(js, l => l.Contains("console.log(1)"));
        Assert.That(generatedLine, Is.GreaterThanOrEqualTo(0), "no console.log emitted");

        // Reuse the existing decoder rather than asserting on the raw mappings string —
        // Does.Contain("mappings") passes on ANY source map and proves nothing.
        var pairs = JavaScriptGeneratorSourceMapTests.Decode(generator.SourceMap.ToJson("app.js"));
        var mapped = pairs.Where(p => p.generated == generatedLine).Select(p => p.source).ToList();

        Assert.That(mapped, Does.Contain(2),
            "Console.WriteLine is on source line 3 (0-based 2); imports above it must not shift it");
    }

    // ------------------------------------------------------------------
    // `::` — the raw-JavaScript escape hatch, in EXPRESSION (call) position ONLY.
    //
    // ⛔ THE EXPRESSION/TYPE SPLIT IS THE DESIGN. `::` predates this backend: it was built
    // for C++ passthrough and means two different things by POSITION.
    //   * EXPRESSION — `::console.log(x)` — a raw JS identifier. It LOWERS: emitted verbatim.
    //   * TYPE — `Dim m As std::mutex` — an opaque C++ type. It does NOT lower; emitting
    //     `stdmutex` would be a silent miscompile, so it stays REJECTED (see below).
    //
    // ⚠ MEASURED IR SHAPES, because they are not what the syntax suggests:
    //   `::console.log("hi")`  -> IRInstanceMethodCall{ MethodName="log",
    //                             Object=IRVariable("::console", Foreign) }  — the `::` is on
    //                             the RECEIVER, rendered by VariableRef, NOT by CallTarget.
    //   `::alert("hi")`        -> IRCall{ FunctionName="::alert" }           — CallTarget.
    // Both spellings are covered below so a fix to one cannot be mistaken for a fix to both.
    // ------------------------------------------------------------------

    [Test]
    public void ForeignIdentifier_EmitsVerbatim()
        => Assert.That(JsTestSupport.Compile("Sub Main()\n::console.log(\"hi\")\nEnd Sub"),
            Does.Contain("console.log(\"hi\")"));

    /// <summary>
    /// ⚠ SanitizeName DROPS non-alphanumerics — it does not substitute underscores. The mangled
    /// form is `windowalert`, so that is what must be absent. Asserting Not.Contain("window_alert")
    /// would be false-green: it passes before and after the fix.
    /// </summary>
    [Test]
    public void ForeignIdentifier_IsNotMangled()
    {
        var js = JsTestSupport.Compile("Sub Main()\n::window.alert(\"hi\")\nEnd Sub");

        Assert.That(js, Does.Contain("window.alert"));
        Assert.That(js, Does.Not.Contain("windowalert"));
    }

    /// <summary>
    /// The FREE-FUNCTION spelling — a leading `::` with no member access — reaches a different
    /// renderer (IRCall/CallTarget) than the dotted form above (IRInstanceMethodCall/VariableRef).
    /// Without this, a fix applied at only one of the two sites reads as complete.
    /// </summary>
    [Test]
    public void ForeignIdentifier_BareFreeFunction_EmitsVerbatim()
        => Assert.That(JsTestSupport.Compile("Sub Main()\n::alert(\"hi\")\nEnd Sub"),
            Does.Contain("alert(\"hi\")"));

    /// <summary>A `::` TYPE is still a C++ passthrough type and still does not lower.</summary>
    [Test]
    public void ForeignType_IsStillRejected()
        => Assert.That(() => JsTestSupport.Compile("Sub Main()\nDim m As std::mutex\nEnd Sub"),
            Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());

    /// <summary>
    /// ⚠ KNOWN LIMITATION, pinned so it is known rather than discovered. Assignment to a `::`
    /// member is a SEMANTIC ANALYZER error, raised before any backend, so relaxing
    /// ForeignFeatureChecker cannot reach it. Use javascript{ } for stateful DOM work.
    /// </summary>
    [Test]
    public void ForeignMemberAssignment_IsStillRejected_KNOWN()
        => Assert.That(() => JsTestSupport.Compile("Sub Main()\n::document.title = \"hi\"\nEnd Sub"),
            Throws.Exception.With.Message.Contains("Cannot assign"));

    /// <summary>
    /// ⚠ KNOWN LIMITATION. An inferred local from a `::` expression gets a Foreign type, which
    /// CheckType rejects — so a `::` value cannot be stored and reused.
    /// </summary>
    [Test]
    public void ForeignValueInALocal_IsStillRejected_KNOWN()
        => Assert.That(() => JsTestSupport.Compile(
                "Sub Main()\nDim el = ::document.getElementById(\"out\")\nEnd Sub"),
            Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());

    /// <summary>
    /// ⚠ KNOWN LIMITATION, and <b>the first wall a user actually hits</b> — so it is pinned
    /// separately from the inferred-local form above rather than assumed to be the same case.
    ///
    /// <para>Declaring the type does NOT rescue the value. `Dim v As Integer = ::getValue()`
    /// dies EARLIER and in a different component: the SemanticAnalyzer types the initializer as
    /// `::getValue` and refuses the assignment outright — <i>"Cannot assign value of type
    /// '::getValue' to variable of type 'Integer'"</i> — so no backend flag can reach it and
    /// relaxing ForeignFeatureChecker further would change nothing.</para>
    ///
    /// <para>Together with the two pins above, the honest statement of the hatch's scope is:
    /// <b>`::` works in CALL and ARGUMENT position only. A `::` value cannot be STORED at all</b>,
    /// by inference or by declaration. Reach for <c>javascript{ }</c> when you need to keep one.</para>
    /// </summary>
    [Test]
    public void ForeignValueInATypedLocal_IsStillRejected_KNOWN()
        => Assert.That(() => JsTestSupport.Compile(
                "Sub Main()\nDim v As Integer = ::getValue()\nEnd Sub"),
            Throws.Exception.With.Message.Contains(
                "Cannot assign value of type '::getValue' to variable of type 'Integer'"));

    /// <summary>
    /// ⛔ THE MEASURED MISCOMPILE. `.Length` on a FOREIGN receiver must reach the output exactly
    /// as written — the hatch's entire promise is verbatim emission.
    ///
    /// <para>Before the fix <c>Console.WriteLine(::s.Length)</c> emitted
    /// <c>console.log(s.length)</c>: <c>Expr</c>'s <c>IsLengthAccess</c> arm short-circuits ahead
    /// of <c>FieldAccess</c>, and <c>IsLengthAccess</c> matched the FIELD NAME alone,
    /// case-insensitively, with no receiver gate at all. Usually right by accident, because
    /// JavaScript does spell it <c>length</c> — and a silent miscompile for any foreign object
    /// carrying a capital-<c>Length</c> property, from a build that reported success.</para>
    ///
    /// <para>Asserting the ABSENCE of <c>.length</c> as well is what makes this test load-bearing:
    /// <c>Does.Contain(".Length")</c> alone would pass on a case-insensitive substring engine and,
    /// more importantly, says nothing about which of the two spellings was emitted.</para>
    /// </summary>
    [Test]
    public void ForeignIdentifier_LengthMember_IsNotCaseFolded()
    {
        var js = JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(::s.Length)\nEnd Sub");

        Assert.That(js, Does.Contain("s.Length"));
        Assert.That(js, Does.Not.Contain("s.length"));
    }

    /// <summary>
    /// The same rule one hop further out. The receiver of the `.Length` in `::a.b.Length` is NOT
    /// an IRVariable named `::a.b` — MEASURED, it is an IRFieldAccess bound to a temp
    /// (<c>const t7 = a.b;</c>) whose own name carries no `::` at all. So a "does the receiver's
    /// name start with `::`" gate fixes the single-hop shape and leaves this one folded, which is
    /// why the backend's foreignness test is TRANSITIVE.
    /// </summary>
    [Test]
    public void ForeignIdentifier_LengthOnAChainedMember_IsNotCaseFolded()
    {
        var js = JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(::a.b.Length)\nEnd Sub");

        Assert.That(js, Does.Contain(".Length"));
        Assert.That(js, Does.Not.Contain(".length"));
    }

    /// <summary>
    /// The same gate through the OTHER member-rewriting channel. `Me` is the one BasicLang name
    /// <c>SanitizeName</c> actively REWRITES (to `this`), so a foreign property spelled `Me`
    /// measured as <c>s.this</c> — valid JavaScript reading an entirely different property.
    /// </summary>
    [Test]
    public void ForeignIdentifier_MemberNamedMe_IsNotRewrittenToThis()
    {
        var js = JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(::s.Me)\nEnd Sub");

        Assert.That(js, Does.Contain("s.Me"));
        Assert.That(js, Does.Not.Contain("s.this"));
    }

    /// <summary>An ORDINARY `.Length` still lowers — the gate must not disarm the rename itself.</summary>
    [Test]
    public void OrdinaryLength_StillLowersToLowercase()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\nDim s As String = \"abc\"\nConsole.WriteLine(s.Length)\nEnd Sub");

        Assert.That(js, Does.Contain("s.length"));
        Assert.That(js, Does.Not.Contain("s.Length"));
    }

    /// <summary>
    /// ⛔ Only a LEADING `::` is a JS passthrough. An INTERIOR one (`mathlib::freeAdd`) is a C++
    /// namespace qualification with no JavaScript meaning: stripping it yields `mathlibfreeAdd`,
    /// an undefined identifier reaching the browser from a green build.
    /// </summary>
    [Test]
    public void ForeignIdentifier_WithInteriorNamespace_IsRejected()
        => Assert.That(() => JsTestSupport.Compile("Sub Main()\n::mathlib::freeAdd(1, 2)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

    /// <summary>
    /// ⛔ The interior-`::` form with NO leading `::` — `mathlib::freeAdd(1, 2)`, which is how
    /// every pre-existing foreign-passthrough test in this repo is written. It must be refused
    /// for the same reason as the leading form, and it is the case a "does the name START with
    /// `::`" guard silently lets through: nothing throws, `mathlibfreeAdd` is emitted, and the
    /// ReferenceError surfaces in the browser instead of in the build.
    /// </summary>
    [Test]
    public void ForeignIdentifier_InteriorNamespaceWithoutLeadingColons_IsRejected()
        => Assert.That(() => JsTestSupport.Compile("Sub Main()\nmathlib::freeAdd(1, 2)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

    /// <summary>
    /// A `Select Case` pattern operand reaches the output through ExprInline and NOTHING else —
    /// pattern operands are never entries in any block, so no other renderer sees them. That made
    /// it the easiest site to miss when the checker's operand scan stepped aside.
    /// </summary>
    [Test]
    public void ForeignIdentifier_InACaseValue_IsRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "Sub Main()\nDim y As Integer = 42\nSelect Case y\nCase mathlib::kAnswer\n" +
                "Console.WriteLine(\"m\")\nEnd Select\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

    /// <summary>
    /// The same interior-`::` refusal through the OTHER channel: a `New` of a `::`-qualified
    /// class. `New std::mutex()` used to be refused by ForeignFeatureChecker's IRNewObject arm;
    /// now that the arm is relaxed for this backend, NewObject itself must refuse it — otherwise
    /// it emits `new stdmutex()`, a ReferenceError from a build that reported success.
    /// </summary>
    [Test]
    public void ForeignNewObject_WithInteriorNamespace_IsRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "Sub Main()\nConsole.WriteLine(New std::mutex())\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

    // ------------------------------------------------------------------
    // THE SAME SHAPES, ON THE IR THAT ACTUALLY SHIPS.
    //
    // ⛔⛔ Every `::` test above runs through JsTestSupport.Compile, which is BuildModule +
    // Generate with NOTHING between — while all three shipping routes (CLI single-file, CLI
    // project, the IDE's BuildService) run OptimizationPipeline.AddStandardPasses()
    // UNCONDITIONALLY, with no switch that turns it off. So the guard for this feature was
    // pinned entirely to a path no user reaches. That is the documented hazard in CLAUDE.md and
    // it is not hypothetical here: it once hid six live defects on this backend behind 351 green
    // tests.
    //
    // These need no Node. The question is a CODEGEN one — does the name still come out verbatim,
    // does the interior form still get refused — and text answers it. (Behaviour questions need
    // stdout as the oracle, which is what JavaScriptOptimizedExecutionTests exists for.)
    //
    // Passes that could plausibly disturb these: copy propagation and CSE re-point operands, so
    // a foreign receiver can arrive at the renderer as a DIFFERENT node than IRBuilder produced;
    // dead-code elimination can drop the block a rejection would have been raised from, turning
    // a refusal into a silent accept.
    // ------------------------------------------------------------------

    [Test]
    public void Optimized_ForeignIdentifier_EmitsVerbatim()
        => Assert.That(
            JsTestSupport.CompileOptimized("Sub Main()\n::console.log(\"hi\")\nEnd Sub"),
            Does.Contain("console.log(\"hi\")"));

    /// <summary>The free-function spelling reaches CallTarget rather than VariableRef.</summary>
    [Test]
    public void Optimized_ForeignIdentifier_BareFreeFunction_EmitsVerbatim()
    {
        var js = JsTestSupport.CompileOptimized("Sub Main()\n::window.alert(\"hi\")\n::alert(\"bye\")\nEnd Sub");

        Assert.That(js, Does.Contain("window.alert(\"hi\")"));
        Assert.That(js, Does.Contain("alert(\"bye\")"));
        Assert.That(js, Does.Not.Contain("windowalert"));
    }

    /// <summary>
    /// The measured miscompile, re-pinned on the shipping IR. Copy propagation is exactly the
    /// kind of pass that could hand <c>IsLengthAccess</c> a receiver node the transitive
    /// foreignness test has to walk further to recognise.
    /// </summary>
    [Test]
    public void Optimized_ForeignIdentifier_LengthMember_IsNotCaseFolded()
    {
        var js = JsTestSupport.CompileOptimized("Sub Main()\nConsole.WriteLine(::s.Length)\nEnd Sub");

        Assert.That(js, Does.Contain("s.Length"));
        Assert.That(js, Does.Not.Contain("s.length"));
    }

    /// <summary>
    /// A refusal is worth nothing if a pass can delete the code that raises it. Both spellings,
    /// because the leading and non-leading forms take different branches of the guard.
    /// </summary>
    [Test]
    public void Optimized_ForeignIdentifier_WithInteriorNamespace_IsStillRejected()
    {
        Assert.That(() => JsTestSupport.CompileOptimized("Sub Main()\n::mathlib::freeAdd(1, 2)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

        Assert.That(() => JsTestSupport.CompileOptimized("Sub Main()\nmathlib::freeAdd(1, 2)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));
    }

    /// <summary>
    /// ⚠ The pattern-operand channel is the one the optimizer is most likely to reshape:
    /// ConstantFoldingPass rewrites case constants, and dead-code elimination prunes switch arms.
    /// If a `Case mathlib::kAnswer` were folded away, the BL7009 would vanish with it.
    /// </summary>
    [Test]
    public void Optimized_ForeignIdentifier_InACaseValue_IsStillRejected()
        => Assert.That(() => JsTestSupport.CompileOptimized(
                "Sub Main()\nDim y As Integer = 42\nSelect Case y\nCase mathlib::kAnswer\n" +
                "Console.WriteLine(\"m\")\nEnd Select\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

    [Test]
    public void Optimized_ForeignNewObject_WithInteriorNamespace_IsStillRejected()
        => Assert.That(() => JsTestSupport.CompileOptimized(
                "Sub Main()\nConsole.WriteLine(New std::mutex())\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7009"));

    // ------------------------------------------------------------------
    // Task 4 — javascript{ … } inline blocks: THE UNIVERSAL ESCAPE HATCH.
    //
    // `::` is call-only (see the _KNOWN tests above), so every stateful DOM idiom — assigning
    // textContent, storing an element, attaching a handler — has to come through a block. That
    // makes this, not `::`, the feature the "BasicLang produces web sites" milestone rests on.
    // ------------------------------------------------------------------

    [Test]
    public void InlineJavaScriptBlock_EmitsVerbatim()
        => Assert.That(
            JsTestSupport.Compile("Sub Main()\njavascript{ console.log(\"inline\"); }\nEnd Sub"),
            Does.Contain("console.log(\"inline\");"));

    /// <summary>
    /// The milestone program itself, guarded by a test rather than only by a checklist item:
    /// a block reaching into the DOM. Node cannot run it (no DOM), so codegen is the only
    /// automatable oracle — the human step is Task 6.
    /// </summary>
    [Test]
    public void InlineJavaScriptBlock_TheMilestoneProgram_EmitsVerbatim()
    {
        var js = JsTestSupport.Compile(
            "#JsImport \"./greet.js\"\nSub Main()\n" +
            "javascript{ document.getElementById(\"out\").textContent = greet(\"BasicLang\"); }\n" +
            "End Sub", runPreprocessor: true);

        Assert.That(js, Does.Contain("import \"./greet.js\";"));
        Assert.That(js, Does.Contain("document.getElementById(\"out\").textContent = greet(\"BasicLang\");"));
    }

    /// <summary>
    /// A multi-line block must not shift the source map for anything BELOW it. This is the
    /// whole reason Visit(IRInlineCode) emits through Line() rather than appending: Line()
    /// maintains _generatedLine, and every later RecordMapping reads it. Append the block
    /// directly and a breakpoint on the last line of this program binds three lines too high.
    /// </summary>
    [Test]
    public void InlineJavaScriptBlock_DoesNotShiftSourceMapPositionsBelowIt()
    {
        var module = JsTestSupport.BuildModule(
            "Sub Main()\njavascript{\nconst a = 1;\nconst b = 2;\n}\nConsole.WriteLine(7)\nEnd Sub",
            sourceFilePath: "prog.bas");
        var generator = new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator();
        var js = generator.Generate(module).Replace("\r\n", "\n").Split('\n');

        var generatedLine = Array.FindIndex(js, l => l.Contains("console.log(7)"));
        Assert.That(generatedLine, Is.GreaterThanOrEqualTo(0), "no console.log emitted");

        var pairs = JavaScriptGeneratorSourceMapTests.Decode(generator.SourceMap.ToJson("app.js"));
        var mapped = pairs.Where(p => p.generated == generatedLine).Select(p => p.source).ToList();

        Assert.That(mapped, Does.Contain(5),
            "Console.WriteLine is on source line 6 (0-based 5); the block above it must not shift it");
    }

    /// <summary>A block tagged for another backend must still be refused here.</summary>
    [Test]
    public void InlineCppBlock_IsStillRejectedOnJavaScript()
        => Assert.That(() => JsTestSupport.Compile("Sub Main()\ncpp{ int x = 1; }\nEnd Sub"),
            Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());

    /// <summary>
    /// ⛔ THE MIRROR, and the one that would have been missed: a `javascript{ }` block must be
    /// refused on the backends that cannot emit it. The checker arm is symmetric
    /// (ownInlineLanguage), so this passes for free — but "for free" is exactly what a
    /// regression silently takes away, and the C# backend dropping a block would produce a
    /// do-nothing program from a green build.
    /// </summary>
    [TestCase("csharp")]
    [TestCase("cpp")]
    public void InlineJavaScriptBlock_IsRejectedOnOtherBackends(string backend)
    {
        var module = JsTestSupport.BuildModule("Sub Main()\njavascript{ console.log(1); }\nEnd Sub");

        Assert.That(() => BasicLang.Compiler.Driver.Program.GenerateCode(module, backend),
            Throws.Exception, $"the {backend} backend silently dropped a javascript{{ }} block");
    }

    /// <summary>
    /// ⛔ Adding a language tag to the lexer's keyword table would otherwise STEAL that word as
    /// an identifier — and `javascript` is a plausible variable name on this backend
    /// specifically. The scan arm falls back to Identifier when no `{` follows, which makes all
    /// five tags contextual rather than reserved.
    /// </summary>
    [TestCase("javascript")]
    [TestCase("csharp")]
    [TestCase("cpp")]
    public void InlineLanguageTag_WithoutABrace_IsStillAnOrdinaryIdentifier(string name)
        => Assert.That(
            JsTestSupport.Compile($"Sub Main()\nDim {name} As Integer = 3\nConsole.WriteLine({name})\nEnd Sub"),
            Does.Contain($"console.log({name})"));

    /// <summary>
    /// The optimizer sees an opaque instruction with no operands and no result. Confirm it is
    /// not pruned as dead — a dropped escape hatch is a silent do-nothing program, and the
    /// shipping routes all optimize while JsTestSupport.Compile does not.
    /// </summary>
    [Test]
    public void Optimized_InlineJavaScriptBlock_SurvivesVerbatim()
        => Assert.That(
            JsTestSupport.CompileOptimized("Sub Main()\njavascript{ console.log(\"inline\"); }\nEnd Sub"),
            Does.Contain("console.log(\"inline\");"));

    // ==================================================================
    // #JsImport BINDING FORMS.
    //
    // The bare form runs a module for its side effects and binds nothing — correct ES, but it
    // left the ordinary `export function greet()` module unusable: import ✅, copy ✅, build ✅,
    // then `greet is not defined` in the browser. These forms mirror ES exactly, because the
    // person writing one is reading MDN, not a BasicLang manual.
    // ==================================================================

    private static BasicLang.Compiler.IR.JsImportDirective OneImport(string directive)
        => JsTestSupport.BuildModule($"{directive}\nSub Main()\nEnd Sub", runPreprocessor: true)
            .JsImports.Single();

    /// <summary>
    /// The bare form keeps binding nothing. That is CORRECT ES — a side-effect import — and the
    /// reason the binding forms had to be added rather than the bare form changed.
    /// </summary>
    [Test]
    public void JsImport_Bare_StillBindsNothing()
    {
        var import = OneImport("#JsImport \"./m.js\"");

        Assert.That(import.Clause, Is.Null);
        Assert.That(import.IsSideEffectOnly, Is.True);
    }

    [TestCase("#JsImport { greet } From \"./m.js\"", "{ greet }",
        TestName = "JsImport_Named_Single")]
    [TestCase("#JsImport { greet, other } From \"./m.js\"", "{ greet, other }",
        TestName = "JsImport_Named_Several")]
    [TestCase("#JsImport { greet As hi } From \"./m.js\"", "{ greet as hi }",
        TestName = "JsImport_Named_Aliased")]
    [TestCase("#JsImport { greet, other As o } From \"./m.js\"", "{ greet, other as o }",
        TestName = "JsImport_Named_Mixed")]
    [TestCase("#JsImport lib From \"./m.js\"", "lib", TestName = "JsImport_Default")]
    [TestCase("#JsImport * As lib From \"./m.js\"", "* as lib", TestName = "JsImport_Namespace")]
    public void JsImport_BindingClause_IsParsedAndNormalisedToJavaScript(string directive, string clause)
    {
        var import = OneImport(directive);

        Assert.That(import.Specifier, Is.EqualTo("./m.js"));
        Assert.That(import.Clause, Is.EqualTo(clause));
    }

    /// <summary>
    /// ⛔ <c>As</c> and <c>From</c> are normalised to LOWERCASE JavaScript. BasicLang is
    /// case-insensitive, so a user may reasonably write <c>as</c>, <c>AS</c> or <c>As</c> — but
    /// <c>import { a AS b }</c> is a SyntaxError, and a build that emits one produces a blank
    /// page. The keyword casing is ours to fix; the NAMES are not (see the next test).
    /// </summary>
    [TestCase("#JsImport { greet as hi } From \"./m.js\"")]
    [TestCase("#JsImport { greet AS hi } from \"./m.js\"")]
    [TestCase("#jsimport { greet As hi } FROM \"./m.js\"")]
    public void JsImport_KeywordCasing_IsNormalised(string directive)
        => Assert.That(OneImport(directive).Clause, Is.EqualTo("{ greet as hi }"));

    /// <summary>
    /// ⛔ THE MIRROR of the keyword rule, and the one that matters more: an imported NAME is a
    /// JavaScript name and its case is load-bearing. <c>{ Greet }</c> must stay <c>Greet</c> —
    /// helpfully "correcting" it to the BasicLang spelling would import something the module
    /// does not export, and ES named imports fail at LINK time, so the page renders nothing.
    /// </summary>
    [Test]
    public void JsImport_ImportedNameCasing_IsPreservedExactly()
        => Assert.That(OneImport("#JsImport { getElementById As Grab } From \"./m.js\"").Clause,
            Is.EqualTo("{ getElementById as Grab }"));

    [Test]
    public void JsImport_BoundNames_AreTheLocalNames()
    {
        Assert.That(OneImport("#JsImport { a, b As c } From \"./m.js\"").BoundNames,
            Is.EqualTo(new[] { "a", "c" }), "an alias replaces the imported name locally");
        Assert.That(OneImport("#JsImport * As lib From \"./m.js\"").BoundNames,
            Is.EqualTo(new[] { "lib" }));
        Assert.That(OneImport("#JsImport \"./m.js\"").BoundNames, Is.Empty);
    }

    // ---------------------------------------------------------------- emission

    [TestCase("#JsImport { greet } From \"./m.js\"", "import { greet } from \"./m.js\";")]
    [TestCase("#JsImport lib From \"./m.js\"", "import lib from \"./m.js\";")]
    [TestCase("#JsImport * As lib From \"./m.js\"", "import * as lib from \"./m.js\";")]
    [TestCase("#JsImport \"./m.js\"", "import \"./m.js\";")]
    public void JsImport_EmitsTheMatchingEsStatement(string directive, string expected)
        => Assert.That(JsTestSupport.Compile($"{directive}\nSub Main()\nEnd Sub", runPreprocessor: true),
            Does.Contain(expected));

    /// <summary>
    /// ⛔ De-duplication keys on the CLAUSE AND the specifier. Two clauses naming different
    /// exports of one module are two imports; collapsing them on the specifier alone drops the
    /// second binding, and the failure surfaces as `other is not defined` at run time.
    /// </summary>
    [Test]
    public void JsImport_SameModuleDifferentClauses_BothSurvive()
    {
        var js = JsTestSupport.Compile(
            "#JsImport { greet } From \"./m.js\"\n#JsImport { other } From \"./m.js\"\n" +
            "Sub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(js, Does.Contain("import { greet } from \"./m.js\";"));
        Assert.That(js, Does.Contain("import { other } from \"./m.js\";"));
    }

    /// <summary>The identical directive twice is still one import.</summary>
    [Test]
    public void JsImport_IdenticalClauseTwice_IsEmittedOnce()
    {
        var js = JsTestSupport.Compile(
            "#JsImport { greet } From \"./m.js\"\n#JsImport { greet } From \"./m.js\"\n" +
            "Sub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(Regex.Matches(js, @"import \{ greet \} from").Count, Is.EqualTo(1));
    }

    // ---------------------------------------------------------------- rejections

    [TestCase("#JsImport { } From \"./m.js\"", "imports no names",
        TestName = "JsImport_EmptyBraces_IsRejected")]
    [TestCase("#JsImport { 9bad } From \"./m.js\"", "Invalid #JsImport binding",
        TestName = "JsImport_NonIdentifierBinding_IsRejected")]
    [TestCase("#JsImport { a b } From \"./m.js\"", "Invalid #JsImport binding",
        TestName = "JsImport_TwoNamesNoComma_IsRejected")]
    [TestCase("#JsImport greet From ./m.js", "Invalid #JsImport syntax",
        TestName = "JsImport_UnquotedSpecifierWithClause_IsRejected")]
    [TestCase("#JsImport { greet } \"./m.js\"", "Invalid #JsImport syntax",
        TestName = "JsImport_ClauseWithoutFrom_IsRejected")]
    [TestCase("#JsImport * From \"./m.js\"", "Invalid #JsImport syntax",
        TestName = "JsImport_NamespaceWithoutAlias_IsRejected")]
    public void JsImport_MalformedClause_IsRejectedByMessage(string directive, string expected)
        => Assert.That(() => JsTestSupport.BuildModule($"{directive}\nSub Main()\nEnd Sub",
                runPreprocessor: true),
            Throws.Exception.With.Message.Contains(expected));

    /// <summary>A trailing comma is legal in ES and must not be an error here either.</summary>
    [Test]
    public void JsImport_TrailingComma_IsAccepted()
        => Assert.That(OneImport("#JsImport { greet, } From \"./m.js\"").Clause,
            Is.EqualTo("{ greet }"));

    // ---------------------------------------------------------------- BL7010 collisions

    /// <summary>
    /// ⛔ An import binding and a generated declaration share ONE JavaScript module scope, and
    /// redeclaring an import is a SyntaxError — not a runtime error in one corner of the page,
    /// but a parse failure that renders NOTHING. From a build that reported success.
    /// </summary>
    [Test]
    public void JsImport_BindingThatCollidesWithAFunction_IsRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "#JsImport { greet } From \"./m.js\"\nSub greet()\nEnd Sub\nSub Main()\nEnd Sub",
                runPreprocessor: true),
            Throws.Exception.With.Message.Contains("BL7010"));

    [Test]
    public void JsImport_BindingThatCollidesWithAClass_IsRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "#JsImport * As Widget From \"./m.js\"\nClass Widget\nEnd Class\nSub Main()\nEnd Sub",
                runPreprocessor: true),
            Throws.Exception.With.Message.Contains("BL7010"));

    /// <summary>Two modules colliding with each other is even harder to read in a browser —
    /// neither is obviously at fault — so it is refused at build time too.</summary>
    [Test]
    public void JsImport_TwoModulesBindingTheSameName_IsRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "#JsImport { greet } From \"./a.js\"\n#JsImport { greet } From \"./b.js\"\n" +
                "Sub Main()\nEnd Sub", runPreprocessor: true),
            Throws.Exception.With.Message.Contains("BL7010"));

    /// <summary>The message must name the escape hatch it is asking for.</summary>
    [Test]
    public void JsImport_CollisionMessage_SuggestsAnAlias()
        => Assert.That(() => JsTestSupport.Compile(
                "#JsImport { greet } From \"./m.js\"\nSub greet()\nEnd Sub\nSub Main()\nEnd Sub",
                runPreprocessor: true),
            Throws.Exception.With.Message.Contains("As js"));

    /// <summary>And the alias must actually clear it.</summary>
    [Test]
    public void JsImport_AliasedBinding_ClearsTheCollision()
        => Assert.That(JsTestSupport.Compile(
                "#JsImport { greet As jsGreet } From \"./m.js\"\nSub greet()\nEnd Sub\n" +
                "Sub Main()\nEnd Sub", runPreprocessor: true),
            Does.Contain("import { greet as jsGreet } from \"./m.js\";"));

    /// <summary>
    /// ⛔ CASE-SENSITIVE, deliberately. BasicLang is case-insensitive but JavaScript is not, so
    /// `greet` and `Greet` genuinely coexist in the output — refusing that pair would be a false
    /// positive that blocks a legal program.
    /// </summary>
    [Test]
    public void JsImport_BindingDifferingOnlyInCase_IsNotACollision()
        => Assert.That(JsTestSupport.Compile(
                "#JsImport { greet } From \"./m.js\"\nSub Greet()\nEnd Sub\nSub Main()\nEnd Sub",
                runPreprocessor: true),
            Does.Contain("import { greet } from \"./m.js\";"));

    /// <summary>
    /// A CLASS METHOD is a property of a class object, not a module-scope name, so it cannot
    /// collide. Refusing it would block a legal program for no reason.
    /// </summary>
    [Test]
    public void JsImport_BindingMatchingAClassMethodName_IsNotACollision()
        => Assert.That(JsTestSupport.Compile(
                "#JsImport { render } From \"./m.js\"\nClass Widget\nPublic Sub render()\nEnd Sub\n" +
                "End Class\nSub Main()\nEnd Sub", runPreprocessor: true),
            Does.Contain("import { render } from \"./m.js\";"));

    /// <summary>
    /// ⛔ The collision is compared on the EMITTED name, not the BasicLang one. SanitizeName
    /// drops non-alphanumerics, so a BasicLang identifier can reach the output as a different
    /// string — and the clash happens there.
    /// </summary>
    [Test]
    public void JsImport_CollisionIsComparedOnTheEmittedName()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport { greet } From \"./m.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);
        module.Functions.Add(new BasicLang.Compiler.IR.IRFunction("gr-eet", null));

        Assert.That(() => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator()
                .Generate(module),
            Throws.Exception.With.Message.Contains("BL7010"),
            "'gr-eet' emits as 'greet' and collides there, even though the BasicLang names differ");
    }

    /// <summary>Still refused on the IR that ships — the checker runs before any pass could
    /// rename or drop a declaration.</summary>
    [Test]
    public void Optimized_JsImport_CollisionIsStillRejected()
        => Assert.That(() => JsTestSupport.CompileOptimized(
                "#JsImport { greet } From \"./m.js\"\nSub greet()\nEnd Sub\nSub Main()\nEnd Sub",
                runPreprocessor: true),
            Throws.Exception.With.Message.Contains("BL7010"));
}
