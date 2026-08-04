using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// THE shared .NET-boundary test harness (P2a-2 Task-8 Step 0, I5): one resolver, one
/// "resolve this member the way production does" helper, and the STUB-RUNTIME machinery that
/// runs a lowered BasicLang program against a test-emitted fake <c>g_net</c>.
///
/// <para><b>Why it is shared rather than private to one fixture.</b> Two things were already
/// duplicated — <see cref="Winner"/> verbatim in two fixtures, and a per-fixture
/// <c>SharedResolver</c> in six, each paying its own ~170-assembly resolver construction. More
/// importantly the stub runtime is the only way to prove RUN-LEVEL behavior without a ~25 s
/// AOT publish, and Tasks 9 (collections), 10 (outbound copies) and 11 (delegates) all need
/// run-level proofs. Leaving it private to <c>NetProxyStubRunTests</c> meant the next fixture
/// would copy it, and a copied harness drifts.</para>
///
/// <para><b>The harness shape.</b> The real <c>blnet_startup.g.cpp</c> is EXCLUDED from the
/// translation-unit set; a per-test <c>stub.g.cpp</c> takes its place, defining <c>g_net</c>,
/// filling slots with recording C++ lambdas (canned statuses/values, <c>printf</c> to stdout
/// so ordering interleaves verifiably with program output — C++ streams are stdio-synchronized
/// by default), and filling the minimal <c>g_shim</c> members the runtime paths under test
/// touch (<c>free_</c> for string-return ownership, <c>last_error</c> for the NetException
/// chain). Slot names are re-derived per test from the SAME seams production uses (resolver
/// descriptors → <see cref="NetNameMangler"/>), so a mangling or overload-selection drift
/// breaks the C++ COMPILE — which is exactly the proxy-name-mismatch pin.</para>
/// </summary>
internal static class NetStubHarness
{
    /// <summary>
    /// ONE resolver for the whole assembly — construction reads ~170 framework assemblies, and
    /// <see cref="NetTypeResolver"/> caches every lookup, so sharing it is the difference
    /// between paying that once and paying it per fixture.
    /// </summary>
    internal static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    internal const string RegexFullName = "System.Text.RegularExpressions.Regex";

    /// <summary>
    /// The descriptor real overload resolution selects, asserted to have resolved. Fixture
    /// PROVENANCE, not a hard-coded expectation: every proxy/export name in these fixtures is
    /// re-derived from the seams production uses, so a mangler or overload-selection change
    /// churns the expectation automatically while a LOWERING change that stops agreeing with
    /// them goes red.
    /// </summary>
    internal static NetMemberDescriptor Winner(
        string typeFullName, NetCallForm form, string member, params string[] args)
    {
        var result = SharedResolver.Value.ResolveOverload(typeFullName, form, member, args);
        Assert.That(result.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            $"fixture provenance: {typeFullName}.{member}({string.Join(", ", args)}) must resolve");
        return result.Member!;
    }

    /// <summary>The one member of a type with this name and kind — for members with no overload axis.</summary>
    internal static NetMemberDescriptor SoleMember(
        string typeFullName, string memberName, NetMemberCategory kind) =>
        SharedResolver.Value.GetMembers(typeFullName)
            .Single(m => m.Name == memberName && m.Kind == kind);

    internal static (string exe, string argsTemplate) RequireCompiler()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null)
            Assert.Ignore("No C++ compiler available for run tests.");
        return compiler!.Value;
    }

    /// <summary>
    /// Compiles a BasicLang program to C++ AND collects its .NET surface through the production
    /// seams, asserting the program is clean and actually draws a surface (a program that draws
    /// none would run the stub against nothing and prove nothing).
    /// </summary>
    /// <param name="optimize">
    /// Run <c>OptimizationPipeline.AddStandardPasses</c> before codegen and before collecting
    /// the surface — the CLI-equivalent path, and the repo law that codegen is validated
    /// through the optimizer and not only through the non-optimizing helper.
    ///
    /// <para><b>Default false is a real choice, not laziness.</b> The surface is collected from
    /// the OPTIMIZED IR in production (a call the optimizer deleted must not cost a proxy
    /// slot), so an optimizing run is the more faithful one — but the optimizer can also delete
    /// the very call a stub scenario is asserting on, turning a genuine regression into a
    /// vacuous pass. Scenarios opt in where the optimizer is part of what is under test.</para>
    /// </param>
    internal static (string Cpp, NetSurface Surface) CompileWithSurface(
        string source, bool optimize)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "unexpected findings: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");

        if (optimize)
        {
            var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(module);
        }

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
        var surface = NetSurfaceCollector.Collect(
            new[] { module }, null, () => SharedResolver.Value,
            new List<NetReferenceDiagnostic>());
        Assert.That(surface.IsNonEmpty, Is.True, "the program under test must draw a surface");
        return (cpp, surface);
    }

    /// <summary>
    /// One stub slot: the mangled name plus the full C++ lambda text (its signature must match
    /// the slot's C ABI — a mismatch fails the compile, which is the pin).
    /// </summary>
    internal sealed record StubSlot(string SlotName, string Lambda);

    internal static string StubTranslationUnit(
        IEnumerable<StubSlot> slots, string shimSetup = "")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#include \"blnet.h\"");
        sb.AppendLine("#include \"blnet_runtime.hpp\"   /* g_shim — the members the stub fills */");
        sb.AppendLine("#include \"blnet_bindings.g.hpp\"");
        sb.AppendLine("#include <cstdio>");
        sb.AppendLine("#include <cstdlib>");
        sb.AppendLine("#include <cstring>");
        sb.AppendLine();
        sb.AppendLine("/* THE definition the bindings header declares extern; the REAL");
        sb.AppendLine("   blnet_startup.g.cpp is excluded from this build on purpose. */");
        sb.AppendLine("BlnetProxyTable g_net{};");
        sb.AppendLine();
        sb.AppendLine("static char* stub_strdup(const char* s) {");
        sb.AppendLine("    size_t n = std::strlen(s) + 1;");
        sb.AppendLine("    char* p = (char*)std::malloc(n);");
        sb.AppendLine("    std::memcpy(p, s, n);");
        sb.AppendLine("    return p;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("namespace {");
        sb.AppendLine("struct StubInit {");
        sb.AppendLine("    StubInit() {");
        if (!string.IsNullOrEmpty(shimSetup))
            sb.AppendLine(shimSetup);
        foreach (var slot in slots)
            sb.AppendLine($"        g_net.{slot.SlotName} = {slot.Lambda};");
        sb.AppendLine("    }");
        sb.AppendLine("};");
        sb.AppendLine("StubInit g_stub_init;");
        sb.AppendLine("} /* anonymous namespace */");
        return sb.ToString();
    }

    internal static string RunWithStub(string cpp, NetSurface surface, string stubTu)
    {
        var compiler = RequireCompiler();
        var artifacts = NetProxyEmitter.Emit(surface, "Stub.dll");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in artifacts)
        {
            if (kv.Key == NetProxyEmitter.StartupFileName)
                continue;   // the stub replaces the real startup TU
            files[kv.Key] = kv.Value;
        }
        files["prog.g.cpp"] = cpp;
        files["stub.g.cpp"] = stubTu;
        return CppCompile.CompileAndRunFiles(
            files, new[] { "prog.g.cpp", "stub.g.cpp" }, compiler);
    }
}
