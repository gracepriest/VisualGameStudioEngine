using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 7a — resolved-call lowering to <c>g_net</c> proxies (emit-level; the
/// stub-runtime run proofs live in <c>NetProxyStubRunTests</c>).
///
/// <para><b>Naming provenance:</b> every expected proxy name in this fixture is RE-DERIVED
/// from the same seams production uses — <c>NetTypeResolver.ResolveOverload</c> /
/// <c>GetMembers</c> for the descriptor, <c>NetNameMangler.Mangle</c> for the spelling —
/// never a hard-coded hash. A mangler or overload-selection change churns the expectation
/// automatically; a LOWERING change that stops agreeing with them is exactly what must go
/// red.</para>
///
/// <para><b>The name-only gate (the plan's mandatory blockquote):</b> a descriptor recorded
/// by name match alone (no overload probe) must never lower — that would call whichever
/// overload metadata order listed first, a silent miscompile. The analyzer now errors on the
/// unprobeable shapes at their positions (native path); the generator's own gate is the
/// defense-in-depth layer and <see cref="NameOnlyDescriptor_RefusesToLower"/> pins it
/// directly.</para>
/// </summary>
[TestFixture]
public class NetCallLoweringTests
{
    /// <summary>One resolver for the fixture — construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    private const string RegexFullName = "System.Text.RegularExpressions.Regex";

    // ------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------

    private static (SemanticAnalyzer Analyzer, IRModule Module) BuildIR(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        return (analyzer, new IRBuilder(analyzer).Build(ast, "TestModule"));
    }

    private static string CompileToCpp(string source, bool assertNoFindings = true)
    {
        var (analyzer, module) = BuildIR(source);
        if (assertNoFindings)
        {
            Assert.That(analyzer.NetDiagnostics, Is.Empty,
                "unexpected findings: " + string.Join(" | ",
                    analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        }
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
    }

    private static SemanticAnalyzer AnalyzeForFindings(string source, bool nativeBackend = true)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend);
        analyzer.Analyze(ast);
        return analyzer;
    }

    /// <summary>The generated proxy call spelling for a resolved member (§9.2 via §7.3).</summary>
    private static string ProxyCall(NetMemberDescriptor member) =>
        "BasicLang::net::" + NetNameMangler.Mangle(member);

    private static NetMemberDescriptor Winner(
        string typeFullName, NetCallForm form, string member, params string[] args)
    {
        var result = SharedResolver.Value.ResolveOverload(typeFullName, form, member, args);
        Assert.That(result.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            $"fixture provenance: {typeFullName}.{member}({string.Join(", ", args)}) must resolve");
        return result.Member!;
    }

    // ====================================================================================
    // The core shapes (plan Step 1)
    // ====================================================================================

    [Test]
    public void StaticResolvedCall_LowersToTheTypedProxy_WithUtf8Args()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim ok = Regex.IsMatch("aaa", "a+")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var winner = Winner(RegexFullName, NetCallForm.Static, "IsMatch",
            "System.String", "System.String");
        Assert.That(cpp, Does.Contain(ProxyCall(winner) + "(\"aaa\", \"a+\")"),
            "a resolved STATIC call lowers to the typed inline proxy (receiver-less), its "
            + "String arguments passed as UTF-8 const char* (§8.3 borrow — literals are "
            + "already const char*):\n" + cpp);
    }

    [Test]
    public void InstanceResolvedCall_PassesTheNetRefReceiverFirst()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim ok = r.IsMatch("aaa")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var winner = Winner(RegexFullName, NetCallForm.Instance, "IsMatch", "System.String");
        Assert.That(winner.IsStatic, Is.False, "fixture guard: the instance overload");
        Assert.That(cpp, Does.Contain(ProxyCall(winner) + "(r, \"aaa\")"),
            "an INSTANCE call passes the receiver NetRef first, per the §9.2 proxy "
            + "signature:\n" + cpp);
    }

    [Test]
    public void ResolvedConstruction_LowersToTheCtorProxy_IntoANetRefLocal()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var ctor = Winner(RegexFullName, NetCallForm.Constructor, ".ctor", "System.String");
        Assert.That(cpp, Does.Contain("BasicLang::NetRef r"),
            "the ManagedOwned local declares as NetRef (the D-P7 flip shape):\n" + cpp);
        // The construction lands in a NetRef-typed result (IR materializes it through a
        // NetRef temp, then copies into the declared local — a shared_ptr copy, sound).
        Assert.That(cpp, Does.Contain("= " + ProxyCall(ctor) + "(\"a+\")"),
            "`New` on a resolved type lowers to the constructor proxy, whose §8.2 export "
            + "returns the created object's handle into a NetRef:\n" + cpp);
    }

    [Test]
    public void PropertyGet_LowersToTheGetterShapedSlot()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim st As Stream
              Dim p = st.Position
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        // The getter IS the Property descriptor's own slot (NetProxyEmitter shapes a
        // parameterless Property as its getter) — provenance: the resolver's surface.
        var position = SharedResolver.Value.GetMembers("System.IO.Stream")
            .Single(m => m.Name == "Position" && m.Kind == NetMemberCategory.Property);
        Assert.That(cpp, Does.Contain(ProxyCall(position) + "(st)"),
            "a property READ lowers to the getter-shaped property slot with the receiver:\n" + cpp);
        Assert.That(cpp, Does.Contain("int64_t p"),
            "the local must type from the descriptor's TypeFullName (System.Int64 -> Long), "
            + "not degrade to Object:\n" + cpp);
    }

    [Test]
    public void PropertySet_LowersToTheSynthesizedSetterSlot()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim st As Stream
              st.Position = 5
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        // The setter slot convention: descriptors model accessors as METHODS — the CLR's
        // real accessor metadata name (set_X), void return, one value parameter of the
        // property's type. Derived here from the resolver's property descriptor so the
        // synthesis convention is pinned against provenance, not against itself.
        var position = SharedResolver.Value.GetMembers("System.IO.Stream")
            .Single(m => m.Name == "Position" && m.Kind == NetMemberCategory.Property);
        var setter = new NetMemberDescriptor(
            "set_Position", position.DeclaringTypeFullName, NetMemberCategory.Method,
            position.IsStatic, arity: 0, "System.Void",
            new[] { new NetParameterDescriptor(NetRefKind.None, position.TypeFullName) });
        Assert.That(cpp, Does.Contain(ProxyCall(setter) + "(st, 5)"),
            "a property WRITE lowers to the synthesized set_X accessor-method slot "
            + "(receiver, value):\n" + cpp);
    }

    [Test]
    public void NothingArgument_LowersToTheZeroHandle()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim s = Convert.ToBase64String(Nothing)
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("BasicLang::net::bl_net_System_Convert_ToBase64String"),
            "the Nothing-argument call must RESOLVE (the probe's null spelling) and lower "
            + "to a ToBase64String proxy:\n" + cpp);
        Assert.That(cpp, Does.Contain("(BasicLang::blnet::NetRef())"),
            "§8.2/§8.3: Nothing crosses a handle slot as the 0-handle — an empty NetRef, "
            + "never a Table lookup:\n" + cpp);
    }

    [Test]
    public void BooleanReturn_TypesTheLocalAsBool()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim ok = r.IsMatch("aaa")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("bool ok"),
            "the flip's documented Object-degrade is lifted: the winner's TypeFullName "
            + "(System.Boolean) types the inferred local as Boolean -> bool:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("void* ok"),
            "an Object-degraded `void* ok` is exactly the shape this test exists to kill:\n" + cpp);
    }

    [Test]
    public void StringReturn_ArrivesAsBasicLangString()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim s = Regex.Escape("a+b")
              Console.WriteLine(s)
             End Sub
            End Module
            """);

        var escape = Winner(RegexFullName, NetCallForm.Static, "Escape", "System.String");
        // Any destination shape is fine (IR may route through a String temp before the
        // declared local) — the load-bearing halves are the proxy call and the String local.
        Assert.That(cpp, Does.Contain("= " + ProxyCall(escape) + "(\"a+b\")"),
            "a returned string arrives through the proxy's transfer-buffer handling "
            + "(std::string IS BasicLang::String on this backend):\n" + cpp);
        Assert.That(cpp, Does.Contain("std::string s"),
            "the inferred local types as String:\n" + cpp);
    }

    [Test]
    public void DateTimeArgument_CrossesThroughTheTask6Converter()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim d As DateTime
              Dim u = TimeZoneInfo.ConvertTimeToUtc(d)
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("BasicLang::net::to_net_datetime(d)"),
            "§8.3's P1-NativeOwned row: a DateTime argument crosses through Task 6's "
            + "converter, never as a handle (§6.4 — the six must never become handles):\n" + cpp);
        Assert.That(cpp, Does.Contain("BasicLang::net::from_net_datetime("),
            "…and the DateTime RETURN reconstructs through the inverse converter:\n" + cpp);
        Assert.That(cpp, Does.Contain("BasicLang::DateTime u"),
            "the inferred local types as the native DateTime value:\n" + cpp);
    }

    [Test]
    public void LoweredModule_IncludesTheProxyAndMarshalHeaders()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim ok = Regex.IsMatch("aaa", "a+")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("#include \"blnet_proxies.g.hpp\""),
            "a module that lowers a resolved call must include the proxy header:\n" + cpp);
        Assert.That(cpp, Does.Contain("#include \"blnet_marshal.hpp\""),
            "…and the §6.4 marshal header (after the preamble — its include-order "
            + "contract needs the P1 splices in scope first):\n" + cpp);
    }

    [Test]
    public void SurfaceFreeModule_GetsNoBoundaryIncludes()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Not.Contain("blnet_proxies.g.hpp"),
            "the standing inertness rule: an empty-surface program's emission must not "
            + "reference boundary artifacts that do not exist:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("blnet_marshal.hpp"), cpp);
    }

    // ====================================================================================
    // The name-only gate (the plan's mandatory blockquote)
    // ====================================================================================

    /// <summary>
    /// Defense in depth for the miscompile class: even if a name-only descriptor slips
    /// through the analyzer (mutation: the analyzer errors are the first line), the
    /// generator itself must refuse rather than lower a name-matched-first-overload
    /// descriptor. Built by compiling a VALID program and then stripping the exactness
    /// bit off the carried annotation — the precise state a probe-bypass would produce.
    /// </summary>
    [Test]
    public void NameOnlyDescriptor_RefusesToLower()
    {
        var (_, module) = BuildIR("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim ok = r.IsMatch("aaa")
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var flipped = 0;
        foreach (var fn in module.Functions)
            foreach (var block in fn.Blocks)
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRInstanceMethodCall { ResolvedNetTarget: not null } call)
                    {
                        call.ResolvedNetTargetIsExact = false;
                        flipped++;
                    }
                }
        Assert.That(flipped, Is.GreaterThan(0), "fixture guard: the annotated call was found");

        var ex = Assert.Throws<CppCapabilityException>(() =>
            new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                .Generate(module));
        Assert.That(ex!.Message, Does.Contain("IsMatch"),
            "the refusal must name the member");
        Assert.That(ex.Message, Does.Contain("name"),
            "…and say WHY: the descriptor was matched by name only, so lowering it could "
            + "call the wrong overload");
    }

    // ====================================================================================
    // Analyzer-side gates: untypeable shapes error at their positions (native only)
    // ====================================================================================

    [Test]
    public void LambdaArgument_IsANativeError_AndSilentOnCSharp()
    {
        const string source = """
            Module M
             Sub Main()
              Dim ok = Regex.IsMatch("aaa", Function(x As Integer) x)
             End Sub
            End Module
            """;

        var native = AnalyzeForFindings(source, nativeBackend: true);
        var finding = native.NetDiagnostics.Where(d => d.Code == "BL6017").ToList();
        Assert.That(finding, Has.Count.EqualTo(1),
            "a lambda argument cannot be typed for overload resolution and cannot lower "
            + "(§8.4 delegate lowering is Task 11) — on the native path that is a "
            + "BL6017-class ERROR at the call site, never a silent name-only lowering. Got: "
            + string.Join(" | ", native.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(finding[0].IsWarning, Is.False);
        Assert.That(finding[0].Message, Does.Contain("argument 2").IgnoreCase);

        var csharp = AnalyzeForFindings(source, nativeBackend: false);
        Assert.That(csharp.NetDiagnostics, Is.Empty,
            "§6.3: the C# path keeps its permissive row — csc judges the call late");
    }

    [Test]
    public void RefOutParameter_DrawsBl6019_NamingTheParameter()
    {
        var analyzer = AnalyzeForFindings("""
            Module M
             Sub Main()
              Dim n As Integer
              Interlocked.Increment(n)
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var bl6019 = analyzer.NetDiagnostics.Where(d => d.Code == "BL6019").ToList();
        Assert.That(bl6019, Has.Count.EqualTo(1),
            "a resolved call whose winner takes ref/out cannot lower yet (§8.3 pointer "
            + "slots are Task 8) — the standard BL6019 naming the parameter. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6019[0].IsWarning, Is.False);
        Assert.That(bl6019[0].Message, Does.Contain("Increment"));
        Assert.That(bl6019[0].Message, Does.Contain("ref").IgnoreCase);
    }

    // ====================================================================================
    // Task-5 review carry-forward pins (plan Step 1)
    // ====================================================================================

    /// <summary>
    /// BL6023's native-ERROR behavior pinned DIRECTLY (it was covered only transitively
    /// through the shared severity seam): the same full name declared in two referenced
    /// assemblies is an ambiguous .NET TYPE reference — BL6023, never BL6016.
    /// </summary>
    [Test]
    public void AmbiguousTypeReference_DrawsBl6023_AsANativeError()
    {
        using var dir = new TempDir();
        var one = dir.EmitAssembly(SharedResolver.Value, "BlnetTwinOne",
            "namespace TwinLib { public static class TwinType { public static int A() => 1; } }");
        var two = dir.EmitAssembly(SharedResolver.Value, "BlnetTwinTwo",
            "namespace TwinLib { public static class TwinType { public static int A() => 2; } }");
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { one, two }));

        var parser = new Parser(new Lexer("""
            Using TwinLib

            Module M
             Sub Main()
              Dim t As TwinType
              Console.WriteLine("done")
             End Sub
            End Module
            """).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => resolver, nativeBackend: true);
        analyzer.Analyze(ast);

        var bl6023 = analyzer.NetDiagnostics.Where(d => d.Code == "BL6023").ToList();
        Assert.That(bl6023, Has.Count.EqualTo(1),
            "one full name in two references is BL6023 (ambiguous TYPE), never BL6016. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6023[0].IsWarning, Is.False,
            "P2a-2 THE FLIP: BL6023 is a native ERROR — pinned directly here");
        Assert.That(bl6023[0].Message, Does.Contain("TwinType"));
    }

    /// <summary>
    /// The ctor probe's resolved-ARBITRARY-name arm (zero coverage until now): a
    /// construction of a resolved .NET type OUTSIDE the ManagedOwned registry, inside a
    /// generic body, must draw BL6024 — post-flip nothing else rejects it before it would
    /// hand Task 7a a .NET call inside a C++ template.
    /// </summary>
    [Test]
    public void ResolvedArbitraryCtor_InsideAGenericBody_DrawsBl6024()
    {
        var analyzer = AnalyzeForFindings("""
            Module M
             Function F(Of T)(x As T) As Integer
              Dim fs As New FileStream("a.txt", FileMode.Open)
              Return 1
             End Function
             Sub Main()
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        // The FileMode.Open ARGUMENT access draws its own BL6024 first (the member half);
        // this pin is about the CONSTRUCTOR half's resolved-ARBITRARY-name arm, so search
        // the whole finding list for the construction message.
        var ctorFinding = analyzer.NetDiagnostics.FirstOrDefault(
            d => d.Code == "BL6024" && d.Message.Contains("New System.IO.FileStream"));
        Assert.That(ctorFinding, Is.Not.Null,
            "New FileStream(...) inside F(Of T): FileStream is RESOLVED (ambient System.IO) "
            + "but not a registry name — the ctor probe's arbitrary-name arm must fire. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(ctorFinding!.IsWarning, Is.False);
    }

    // ------------------------------------------------------------------------------------

    private sealed class TempDir : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "blnet-lower-" + Guid.NewGuid().ToString("N"));

        public TempDir() => System.IO.Directory.CreateDirectory(_path);

        public string EmitAssembly(NetTypeResolver _, string name, string source)
        {
            var path = System.IO.Path.Combine(_path, name + ".dll");
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                name,
                new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
                NetTypeResolverTestRefs.FrameworkPaths.Select(
                    p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.Emit.EmitResult emit;
            using (var stream = System.IO.File.Create(path))
                emit = compilation.Emit(stream);
            Assert.That(emit.Success, Is.True,
                "fixture probe assembly failed to build: " + string.Join("\n",
                    emit.Diagnostics.Where(d =>
                        d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
            return path;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_path, recursive: true); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
