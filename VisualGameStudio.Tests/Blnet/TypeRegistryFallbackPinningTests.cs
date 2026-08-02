using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 2 — the pins that stand guard over spec §6.2's <c>ConfigureTypeRegistry</c>
/// deferral.
///
/// <para><b>What is pinned.</b> <c>SemanticAnalyzer.LookupNetTypeMember</c> (via its public
/// wrapper <c>ResolveNetMemberType</c>, and end-to-end through the REAL compile path,
/// <c>BasicCompiler.CompileProjectFiles</c> → <c>CompileUnit</c>) consults, in order: the P1
/// <c>NativeBclSurface</c>, the <c>TypeRegistry</c> (null on the compile path today), the
/// String-method fallback table (<c>GetStringMethodReturnType</c>), and the common-type
/// fallback table (<c>GetCommonMethodReturnType</c>). Every fallback arm is pinned here
/// against TODAY's compile-path behavior — the answers existing programs' types are built
/// from.</para>
///
/// <para><b>Why.</b> Spec §6.2 wants <c>ConfigureTypeRegistry</c> wired into
/// <c>Compiler.CompileUnit</c> so the compile path is configured identically to the LSP
/// (<c>DocumentManager.InitializeTypeRegistry</c>: <c>new TypeRegistry()</c> +
/// <c>PreloadCoreTypes()</c> + cache/index). But the registry branch SHADOWS both fallback
/// tables, and the two disagree:
/// <c>TypeRegistry.GetTypeName</c> spells arrays <c>"String()"</c> while
/// <c>ResolveNetTypeName</c> unwraps only <c>"[]"</c> — so a wired registry turns
/// <c>s.Split(…)</c> from a real <c>TypeKind.Array</c> into a synthetic CLASS named
/// <c>"String()"</c>, and answers members the fallbacks deliberately leave <c>null</c>/Object
/// (gap-filling — a type-inference change to existing programs).
/// <see cref="WiringAnLspConfiguredTypeRegistryChangesStringSplitsAnswer_TheTask2Blocker"/>
/// demonstrates the flip mechanically. Per the Task-2 plan rule — "any pin that flips is a
/// real behavior change; stop, do not update the pin" — the wiring is therefore WITHHELD until
/// the divergence is resolved; these pins are the acceptance gate any future wiring must pass
/// unmodified.</para>
///
/// <para>⛔ <b>Never construct a parameterless <c>new TypeRegistry()</c> in a test</b> — it
/// points at the developer's real <c>%LOCALAPPDATA%\BasicLang\namespace_index.json</c>, and
/// <c>BuildIndex</c>/<c>SaveIndexToCache</c> REPLACE that file. The canary below uses the
/// <c>internal TypeRegistry(string cacheFilePath)</c> seam with a per-test temp directory
/// (the <c>NetTypeResolverTests.NewRegistry</c> pattern).</para>
/// </summary>
[TestFixture]
public class TypeRegistryFallbackPinningTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-fallbackpin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------------------------
    // Seam-level pins: ResolveNetMemberType on an analyzer configured the way
    // Compiler.CompileUnit configures one today (no TypeRegistry — that is the pin).
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// An analyzer in the compile path's configuration: <c>CompileUnit</c> constructs
    /// <c>new SemanticAnalyzer()</c> and deliberately does NOT call
    /// <c>ConfigureTypeRegistry</c> (the P2a-1 deferral this fixture guards). A trivial
    /// Analyze run initializes the type manager exactly as a real unit's does.
    /// </summary>
    private static SemanticAnalyzer CompilePathAnalyzer()
    {
        var parser = new Parser(new Lexer("Module M\n Sub Main()\n End Sub\nEnd Module").Tokenize());
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, "guard: the trivial module must analyze");
        return analyzer;
    }

    private static void AssertName(SemanticAnalyzer analyzer, string type, string member, string expectedTypeName)
    {
        var answer = analyzer.ResolveNetMemberType(type, member);
        Assert.That(answer, Is.Not.Null,
            $"PIN: {type}.{member} must resolve from the fallback tables. If this went null the "
            + "fallback was removed; if it changed shape a TypeRegistry started shadowing the "
            + "fallbacks — either way this is a behavior change to existing programs. Fix the "
            + "code, not this pin.");
        Assert.That(answer!.Name, Is.EqualTo(expectedTypeName),
            $"PIN FLIPPED: {type}.{member} answered '{answer.Name}', the fallback table says "
            + $"'{expectedTypeName}'. A TypeRegistry wired into the compile path shadows "
            + "GetStringMethodReturnType/GetCommonMethodReturnType (SemanticAnalyzer.cs, "
            + "LookupNetTypeMember) — this is a real behavior change to existing programs. "
            + "STOP and fix the wiring; do not update this pin.");
    }

    [Test]
    public void StringMembersReturningString_ArePinnedToTheFallbackTable()
    {
        var analyzer = CompilePathAnalyzer();
        foreach (var member in new[]
        {
            "Trim", "TrimStart", "TrimEnd", "ToUpper", "ToLower", "ToUpperInvariant",
            "ToLowerInvariant", "Substring", "Replace", "Remove", "Insert", "PadLeft",
            "PadRight", "Normalize", "ToString", "Concat", "Join", "Format",
        })
        {
            AssertName(analyzer, "String", member, "String");
        }

        // The System.String spelling reaches the same fallback (the table matches both).
        AssertName(analyzer, "System.String", "Trim", "String");
    }

    [Test]
    public void StringMembersReturningInteger_ArePinnedToTheFallbackTable()
    {
        var analyzer = CompilePathAnalyzer();
        foreach (var member in new[] { "Length", "IndexOf", "LastIndexOf", "CompareTo", "GetHashCode" })
        {
            AssertName(analyzer, "String", member, "Integer");
        }
    }

    [Test]
    public void StringMembersReturningBooleanAndChar_ArePinnedToTheFallbackTable()
    {
        var analyzer = CompilePathAnalyzer();
        foreach (var member in new[]
        {
            "StartsWith", "EndsWith", "Contains", "Equals", "IsNullOrEmpty", "IsNullOrWhiteSpace",
        })
        {
            AssertName(analyzer, "String", member, "Boolean");
        }

        AssertName(analyzer, "String", "Chars", "Char");
    }

    /// <summary>
    /// THE shadow-sensitive pins. The fallback answers a REAL <c>TypeKind.Array</c> with an
    /// element type — which is what makes <c>parts(0)</c> index instead of degrade. A wired
    /// TypeRegistry answers the reflection spelling <c>"String()"</c>, which
    /// <c>ResolveNetTypeName</c> cannot unwrap (it handles only <c>"[]"</c>), producing a
    /// synthetic CLASS. This pin is the tripwire.
    /// </summary>
    [Test]
    public void StringArrayReturningMembers_SplitAndToCharArray_ArePinnedAsRealArrays()
    {
        var analyzer = CompilePathAnalyzer();

        var split = analyzer.ResolveNetMemberType("String", "Split");
        Assert.That(split, Is.Not.Null, "PIN: String.Split must resolve from the fallback table");
        Assert.That(split!.Kind, Is.EqualTo(TypeKind.Array),
            "PIN FLIPPED: String.Split no longer answers a real array. A TypeRegistry wired "
            + "into the compile path answers the reflection spelling 'String()', which "
            + "ResolveNetTypeName cannot unwrap — every program that indexes a Split result "
            + "changes behavior. STOP and fix the wiring; do not update this pin.");
        Assert.That(split.ElementType?.Name, Is.EqualTo("String"),
            "PIN: String.Split's element type must be String");

        var toCharArray = analyzer.ResolveNetMemberType("String", "ToCharArray");
        Assert.That(toCharArray, Is.Not.Null, "PIN: String.ToCharArray must resolve");
        Assert.That(toCharArray!.Kind, Is.EqualTo(TypeKind.Array),
            "PIN FLIPPED: String.ToCharArray no longer answers a real array (see Split's pin).");
        Assert.That(toCharArray.ElementType?.Name, Is.EqualTo("Char"),
            "PIN: String.ToCharArray's element type must be Char");
    }

    [Test]
    public void CollectionMemberRows_ArePinnedToTheFallbackTable()
    {
        var analyzer = CompilePathAnalyzer();

        // List.Add / Dictionary.Add return Void; HashSet.Add returns Boolean.
        AssertName(analyzer, "List", "Add", "Void");
        AssertName(analyzer, "Dictionary", "Add", "Void");
        AssertName(analyzer, "HashSet", "Add", "Boolean");

        foreach (var member in new[] { "Clear", "Insert", "RemoveAt" })
        {
            AssertName(analyzer, "List", member, "Void");
        }

        AssertName(analyzer, "List", "Count", "Integer");
        AssertName(analyzer, "List", "IndexOf", "Integer");
        AssertName(analyzer, "List", "Contains", "Boolean");
        AssertName(analyzer, "Dictionary", "ContainsKey", "Boolean");
        AssertName(analyzer, "Dictionary", "TryGetValue", "Boolean");

        // Remove returns Boolean on all three (whether an element/key was removed).
        AssertName(analyzer, "List", "Remove", "Boolean");
        AssertName(analyzer, "Dictionary", "Remove", "Boolean");
        AssertName(analyzer, "HashSet", "Remove", "Boolean");
    }

    [Test]
    public void CommonRowsOnArbitraryNetTypes_ArePinnedToTheFallbackTable()
    {
        var analyzer = CompilePathAnalyzer();

        // The type-agnostic tail of GetCommonMethodReturnType.
        AssertName(analyzer, "Uri", "ToString", "String");
        AssertName(analyzer, "Stream", "Length", "Integer");
        AssertName(analyzer, "Stream", "Count", "Integer");
        AssertName(analyzer, "Uri", "Equals", "Boolean");
        AssertName(analyzer, "Uri", "Contains", "Boolean");
        AssertName(analyzer, "Version", "Any", "Boolean");
        AssertName(analyzer, "Version", "All", "Boolean");
    }

    /// <summary>
    /// The GAP-FILL canaries: members NO fallback covers answer null today (callers then fall
    /// back to Object). A wired TypeRegistry would start answering these — reflection's Regex,
    /// List and String surfaces — silently changing type inference on existing programs.
    /// </summary>
    [Test]
    public void UncoveredMembersAnswerNull_TheGapFillCanary()
    {
        var analyzer = CompilePathAnalyzer();

        foreach (var (type, member) in new[]
        {
            ("List", "Reverse"),        // real List<T> member, not in the fallback tables
            ("Regex", "IsMatch"),       // real Regex member, no fallback row
            ("String", "GetTypeCode"),  // real String member, no fallback row
            ("Uri", "AbsolutePath"),    // real Uri member, no fallback row
        })
        {
            Assert.That(analyzer.ResolveNetMemberType(type, member), Is.Null,
                $"PIN FLIPPED: {type}.{member} has no fallback row and must answer null on "
                + "today's compile path (callers substitute Object). A non-null answer means a "
                + "TypeRegistry is now gap-filling member types — a type-inference change to "
                + "existing programs. STOP and fix the wiring; do not update this pin.");
        }
    }

    /// <summary>
    /// StringBuilder is P1 <c>NativeOwned</c>: <c>NativeBclSurface</c> is consulted BEFORE the
    /// registry in <c>LookupNetTypeMember</c>, so these rows are immune to any TypeRegistry
    /// wiring by construction. Pinned for completeness of the :2090-:2102 region (the
    /// GetCommonMethodReturnType StringBuilder rows are the surface's twin and unreachable
    /// for these members).
    /// </summary>
    [Test]
    public void StringBuilderRows_AreOwnedByTheP1SurfaceAndRegistryImmune()
    {
        var analyzer = CompilePathAnalyzer();

        AssertName(analyzer, "StringBuilder", "Append", "StringBuilder");
        AssertName(analyzer, "StringBuilder", "AppendLine", "StringBuilder");
        AssertName(analyzer, "StringBuilder", "ToString", "String");
        AssertName(analyzer, "StringBuilder", "Length", "Integer");
    }

    // ------------------------------------------------------------------------------------
    // End-to-end pins through the REAL compile path (BasicCompiler.CompileProjectFiles →
    // CompileUnit). If ConfigureTypeRegistry is ever wired into CompileUnit, THESE tests
    // exercise the wired configuration automatically — they are the "pins must still pass"
    // gate the Task-2 plan names.
    // ------------------------------------------------------------------------------------

    private IRModule CompileViaTheRealCompilePath(string source)
    {
        var path = Path.Combine(_dir, "Program.bas");
        File.WriteAllText(path, source);

        var compiler = new BasicCompiler(new CompilerOptions());
        var result = compiler.CompileProjectFiles(new[] { path });

        Assert.That(result.Success, Is.True,
            "guard: the pin program must compile clean on the real compile path. Errors:\n"
            + string.Join("\n", result.AllErrors.Select(e => e.Message)));
        var unit = result.Units.Single();
        Assert.That(unit.IR, Is.Not.Null, "guard: the unit must carry IR");
        return unit.IR;
    }

    private static IRInstanceMethodCall FindInstanceCall(IRModule module, string methodName) =>
        module.Functions
              .SelectMany(f => f.Blocks)
              .SelectMany(b => b.Instructions)
              .OfType<IRInstanceMethodCall>()
              .Single(c => c.MethodName == methodName);

    /// <summary>
    /// Split is pinned PROPERTY-STYLE (<c>s.Split</c>, no parens) deliberately: the fallback
    /// types the MEMBER as <c>String()</c>, so a parenthesized <c>s.Split(",")</c> is consumed
    /// by VB's array-index ambiguity (the parens index the member's array type — measured; a
    /// String index is even a compile error today). The member access is where
    /// <c>LookupNetTypeMember</c>'s answer lands, and the follow-on <c>parts(0)</c> into a
    /// <c>String</c> local only type-checks while that answer is a REAL array of String —
    /// which is exactly what a wired TypeRegistry's <c>"String()"</c> spelling would break.
    /// </summary>
    [Test]
    public void CompilePath_StringSplit_LowersWithARealArrayReturnType()
    {
        var module = CompileViaTheRealCompilePath(
            "Module M\n"
            + " Sub Main()\n"
            + "  Dim s As String = \"a,b\"\n"
            + "  Dim parts = s.Split\n"
            + "  Dim first As String = parts(0)\n"
            + " End Sub\n"
            + "End Module");

        var split = module.Functions
                          .SelectMany(f => f.Blocks)
                          .SelectMany(b => b.Instructions)
                          .OfType<IRFieldAccess>()
                          .Single(f => f.FieldName == "Split");
        Assert.That(split.Type?.Kind, Is.EqualTo(TypeKind.Array),
            "PIN FLIPPED on the real compile path: s.Split no longer lowers with a real "
            + "array type. If ConfigureTypeRegistry was just wired into "
            + "Compiler.CompileUnit, the registry's 'String()' spelling is shadowing the String "
            + "fallback table — a behavior change to every existing program that splits a "
            + "string. STOP and fix the wiring; do not update this pin.");
        Assert.That(split.Type?.ElementType?.Name, Is.EqualTo("String"),
            "PIN: the Split result's element type must be String on the real compile path.");
    }

    [Test]
    public void CompilePath_InstanceMemberTypes_MatchTheFallbackAnswers()
    {
        var module = CompileViaTheRealCompilePath(
            "Module M\n"
            + " Sub Main()\n"
            + "  Dim s As String = \"abc\"\n"
            + "  Dim u As String = s.ToUpper()\n"
            + "  Dim n As Integer = s.IndexOf(\"b\")\n"
            + "  Dim x = s.GetTypeCode()\n"
            + "  Dim l As New List(Of Integer)\n"
            + "  l.Add(3)\n"
            + " End Sub\n"
            + "End Module");

        Assert.That(FindInstanceCall(module, "ToUpper").Type?.Name, Is.EqualTo("String"),
            "PIN: s.ToUpper() must keep the fallback's String return type on the compile path.");
        Assert.That(FindInstanceCall(module, "IndexOf").Type?.Name, Is.EqualTo("Integer"),
            "PIN: s.IndexOf(...) must keep the fallback's Integer return type on the compile path.");
        Assert.That(FindInstanceCall(module, "GetTypeCode").Type?.Name, Is.EqualTo("Object"),
            "PIN: s.GetTypeCode() has no fallback row and must degrade to Object on today's "
            + "compile path. A different answer means a TypeRegistry started gap-filling — a "
            + "type-inference change to existing programs. STOP; do not update this pin.");
        Assert.That(FindInstanceCall(module, "Add").Type?.Name, Is.EqualTo("Void"),
            "PIN: l.Add(...) must keep the fallback's Void return type on the compile path.");
    }

    // ------------------------------------------------------------------------------------
    // The blocker, demonstrated mechanically.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// The Task-2 wiring experiment, run at the seam instead of in production: configure an
    /// analyzer with a TypeRegistry set up EXACTLY as the LSP sets one up
    /// (<c>DocumentManager.InitializeTypeRegistry</c>: fresh registry + PreloadCoreTypes; the
    /// cache path is a per-test temp file — see the fixture remarks for why the parameterless
    /// ctor is forbidden in tests) and ask the same question the Split pin asks. The answer
    /// DIVERGES from the fallback's real array: the registry's reflection producer spells the
    /// return type <c>"String()"</c> and <c>ResolveNetTypeName</c> cannot unwrap it, yielding a
    /// synthetic class. THIS is why <c>ConfigureTypeRegistry</c> stays un-wired in
    /// <c>Compiler.CompileUnit</c> (spec §6.2's move stays deferred): wiring it flips the pins
    /// above, and the plan forbids shipping a pin flip.
    ///
    /// <para>If this test ever goes RED, the divergence has been fixed (e.g.
    /// <c>ResolveNetTypeName</c> learned the <c>"()"</c> spelling, or the registry learned the
    /// compiler's) — revisit the wiring, re-run every pin in this fixture against it, and only
    /// then retire this canary.</para>
    /// </summary>
    [Test]
    public void WiringAnLspConfiguredTypeRegistryChangesStringSplitsAnswer_TheTask2Blocker()
    {
        var registry = new TypeRegistry(Path.Combine(_dir, "namespace_index.json"));
        registry.PreloadCoreTypes();   // exactly what DocumentManager.InitializeTypeRegistry does

        var parser = new Parser(new Lexer("Module M\n Sub Main()\n End Sub\nEnd Module").Tokenize());
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureTypeRegistry(registry);
        Assert.That(analyzer.Analyze(ast), Is.True, "guard: the trivial module must analyze");

        var wiredAnswer = analyzer.ResolveNetMemberType("String", "Split");
        Assert.That(wiredAnswer, Is.Not.Null,
            "guard: with a preloaded registry the String type must be found (PreloadCoreTypes "
            + "indexes typeof(string) under 'String'), or this canary demonstrates nothing");
        Assert.That(wiredAnswer!.Kind, Is.Not.EqualTo(TypeKind.Array),
            "The documented §6.2 blocker has EASED: an LSP-configured TypeRegistry now answers "
            + "String.Split as a real array, matching the fallback table. Re-run the wiring "
            + "experiment (wire ConfigureTypeRegistry into Compiler.CompileUnit, then every pin "
            + "in this fixture must pass unmodified) and retire this canary if it holds.");
        Assert.That(wiredAnswer.Name, Is.EqualTo("String()"),
            "The wired registry's divergent answer changed shape (was the synthetic class "
            + "'String()', reflection's array spelling that ResolveNetTypeName cannot unwrap). "
            + "Re-derive the §6.2 wiring analysis before touching this canary.");
    }
}
