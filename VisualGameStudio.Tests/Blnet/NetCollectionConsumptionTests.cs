using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 9 — spec §8.5, consuming HANDLE-represented collections.
///
/// <para><b>The two failure modes this fixture exists for, both of which are silent:</b></para>
/// <list type="number">
/// <item><description><b>The wild pointer.</b> A managed <c>List(Of Integer)</c> is spelled
/// <c>"List"</c> and a managed <c>Integer()</c> has <c>Kind == Array</c> — the two spellings
/// <c>CppCodeGenerator.MapType</c> lowers to <c>std::shared_ptr&lt;BasicLang::List&lt;…&gt;&gt;</c>
/// and <c>std::vector&lt;…&gt;</c>. Declaring a 64-bit GCHandle as either dereferences a handle
/// as an object pointer. <see cref="TheManagedMarkerMustPrecedeTheCollectionBranch"/> and
/// <see cref="TheManagedMarkerMustPrecedeTheArrayBranch"/> are the MUTATION targets: reorder
/// <c>MapType</c>'s arms so the collection/array branch runs first and they go red.</description></item>
/// <item><description><b>The boxed mutable-struct enumerator.</b> §8.5 drives every managed
/// enumeration through <c>IEnumerable&lt;T&gt;</c>/<c>IEnumerator&lt;T&gt;</c>, never the
/// concrete struct-returning <c>GetEnumerator()</c>. The two obvious tests — a .NET array, and
/// an <c>IEnumerable&lt;T&gt;</c> from a compiler-generated iterator (a CLASS) — BOTH PASS with
/// the bug present, so <see cref="AConcreteListEnumeratesThroughTheInterfaceNotTheStruct"/>
/// pins the CONCRETE <c>List&lt;T&gt;</c> case directly.</description></item>
/// </list>
///
/// <para><b>Naming provenance:</b> every expected proxy name is RE-DERIVED through the seams
/// production uses (<c>NetAccessorSynthesis</c> for the descriptor,
/// <c>NetNameMangler.Mangle</c> for the spelling), never a hard-coded hash.</para>
/// </summary>
[TestFixture]
public class NetCollectionConsumptionTests
{
    private static readonly Lazy<NetTypeResolver> SharedResolver = NetStubHarness.SharedResolver;

    private const string ListOfInt = "System.Collections.Generic.List<System.Int32>";

    /// <summary>
    /// Exposes the protected <c>MapType</c>. The three §8.5 mapping sites are reached through
    /// it, and testing them through a whole compile would only prove the ones a fixture program
    /// happens to hit — the marker rule has to hold for every shape, including ones no producer
    /// builds yet.
    /// </summary>
    private sealed class MapProbe : CppCodeGenerator
    {
        internal string Map(TypeInfo type) => MapType(type);
    }

    private static string Map(TypeInfo type) => new MapProbe().Map(type);

    /// <summary>A handle-represented .NET <c>List(Of Integer)</c>, exactly as
    /// <c>SemanticAnalyzer.NetHandleResultTypeInfo</c> builds it: the ORDINARY short name, plus
    /// the marker. The name is the point — it is what the collection branch keys on.</summary>
    private static TypeInfo ManagedListOfInt() =>
        new TypeInfo("List", TypeKind.Class)
        {
            GenericArguments = { new TypeInfo("Integer", TypeKind.Primitive) },
            NetHandleTypeFullName = ListOfInt,
        };

    private static TypeInfo ManagedIntArray() =>
        new TypeInfo("Integer", TypeKind.Array)
        {
            ElementType = new TypeInfo("Integer", TypeKind.Primitive),
            ArrayRank = 1,
            NetHandleTypeFullName = "System.Int32[]",
        };

    // ====================================================================================
    // 1. The wild-pointer kill — the three mapping sites (spec §8.5's category-marker table)
    // ====================================================================================

    [Test]
    public void TheManagedMarkerMustPrecedeTheCollectionBranch()
    {
        var managed = ManagedListOfInt();

        Assert.That(Map(managed), Is.EqualTo("BasicLang::NetRef"),
            "MUTATION TARGET. A handle-represented List(Of Integer) is NAMED \"List\", so "
            + "MapType's collection branch claims it unless the marker is tested FIRST — and "
            + "the result is std::shared_ptr<BasicLang::List<int32_t>> over a 64-bit GCHandle, "
            + "which is the wild pointer §8.5 exists to prevent. Reorder MapType's arms so the "
            + "collection branch runs first and this assertion fails.");

        // The SAME type without the marker is the native collection — proving the assertion
        // above is about the MARKER and not about some other property of the fixture type.
        var native = new TypeInfo("List", TypeKind.Class)
        {
            GenericArguments = { new TypeInfo("Integer", TypeKind.Primitive) },
        };
        Assert.That(Map(native), Is.EqualTo("std::shared_ptr<BasicLang::List<int32_t>>"),
            "the marker is the ONLY difference between these two types; a native List must be "
            + "unaffected by §8.5.");
    }

    [Test]
    public void TheManagedMarkerMustPrecedeTheArrayBranch()
    {
        Assert.That(Map(ManagedIntArray()), Is.EqualTo("BasicLang::NetRef"),
            "MUTATION TARGET (spec §8.5's `MapType` array-branch row). A .NET Int32() arrives "
            + "with Kind == Array, so the std::vector branch claims it unless the marker is "
            + "tested first.");

        var native = new TypeInfo("Integer", TypeKind.Array)
        {
            ElementType = new TypeInfo("Integer", TypeKind.Primitive),
            ArrayRank = 1,
        };
        Assert.That(Map(native), Is.EqualTo("std::vector<int32_t>"),
            "a native array must be unaffected by §8.5.");
    }

    [Test]
    public void AManagedCollectionComposesAsAHandleEverywhereItAppears()
    {
        // Composition is what makes ONE marker test enough: a managed List inside a native List
        // must be a NetRef element, not a nested native collection.
        var outer = new TypeInfo("List", TypeKind.Class)
        {
            GenericArguments = { ManagedListOfInt() },
        };
        Assert.That(Map(outer), Is.EqualTo("std::shared_ptr<BasicLang::List<BasicLang::NetRef>>"));
    }

    // ====================================================================================
    // 2. The enumeration protocol — through the INTERFACES (§8.5's mutable-struct rule)
    // ====================================================================================

    [Test]
    public void AConcreteListEnumeratesThroughTheInterfaceNotTheStruct()
    {
        // THE mandatory case. A .NET array and an iterator-produced IEnumerable<T> (a class)
        // both pass with the boxed-struct bug present; List<T>.Enumerator is a MUTABLE STRUCT.
        var element = SharedResolver.Value.EnumerableElementTypeName(ListOfInt);
        Assert.That(element, Is.EqualTo("System.Int32"),
            "the element type is a property of the CONSTRUCTION — the by-name door answers with "
            + "the DEFINITION, where it is the open T. This is what the symbol-carrying seam "
            + "(ConstructedTypeSymbol) exists for.");

        var getEnumerator = NetAccessorSynthesis.EnumerableGetEnumeratorFor(element);
        var current = NetAccessorSynthesis.EnumeratorCurrentFor(element);

        Assert.Multiple(() =>
        {
            Assert.That(getEnumerator.DeclaringTypeFullName,
                Is.EqualTo("System.Collections.Generic.IEnumerable<System.Int32>"),
                "obtained through IEnumerable<T> — NEVER List<T>'s own GetEnumerator(), whose "
                + "return type is the mutable struct List<T>.Enumerator.");
            Assert.That(getEnumerator.TypeFullName,
                Is.EqualTo("System.Collections.Generic.IEnumerator<System.Int32>"));
            Assert.That(NetAccessorSynthesis.EnumeratorMoveNext().DeclaringTypeFullName,
                Is.EqualTo("System.Collections.IEnumerator"),
                "MoveNext is declared on the NON-generic interface — an INTERFACE, i.e. a "
                + "reference type, which is what makes the shim reach the BOX rather than an "
                + "unboxed temporary.");
            Assert.That(current.TypeFullName, Is.EqualTo("System.Int32"),
                "Current comes from the GENERIC IEnumerator<T>, so the element arrives typed "
                + "rather than as the permanently-Rejected System.Object.");
        });

        // The generated shim must spell all four as ordinary interface casts. Unsafe.Unbox is
        // for value-type RECEIVERS; an interface receiver is a reference type, and that is
        // exactly the property that makes MoveNext mutate the box.
        var exports = NetShimGenerator.EmitExports(new NetSurface(
            new[]
            {
                getEnumerator,
                NetAccessorSynthesis.EnumeratorMoveNext(),
                current,
                NetAccessorSynthesis.EnumeratorDispose(),
            },
            Array.Empty<string>()));

        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain(
                "((global::System.Collections.Generic.IEnumerable<System.Int32>)self_!).GetEnumerator()"));
            Assert.That(exports, Does.Contain(
                "((global::System.Collections.IEnumerator)self_!).MoveNext()"));
            Assert.That(exports, Does.Contain(
                "((global::System.Collections.Generic.IEnumerator<System.Int32>)self_!).Current"));
            Assert.That(exports, Does.Not.Contain("Unsafe.Unbox"),
                "no interface receiver is a value type, so no unboxing appears at all — the "
                + "temporary that would have been mutated is never created.");
        });
    }

    [Test]
    public void AStructOnlyEnumeratorWithNoIEnumerableIsRefused()
    {
        // §8.5: "A type with only a struct enumerator and no IEnumerable is BL6019." The
        // resolver answering null is what the analyzer turns into that positioned refusal.
        Assert.That(SharedResolver.Value.EnumerableElementTypeName("System.Text.StringBuilder"),
            Is.Null,
            "StringBuilder has a GetChunks()/ChunkEnumerator shape but is not itself an "
            + "IEnumerable(Of T) — refusing beats guessing an element type.");
    }

    // ====================================================================================
    // 3. §8.5's synthetic ARRAY accessors — .NET arrays expose no indexer in metadata
    // ====================================================================================

    [Test]
    public void ArrayAccessorsAreSynthesizedAndSpelledAsRealIndexing()
    {
        var get = NetAccessorSynthesis.ArrayGetFor("System.Int32");
        var set = NetAccessorSynthesis.ArraySetFor("System.Int32");
        var length = NetAccessorSynthesis.ArrayLengthFor("System.Int32");

        Assert.Multiple(() =>
        {
            Assert.That(get.DeclaringTypeFullName, Is.EqualTo("System.Int32[]"));
            Assert.That(get.Synthesis, Is.EqualTo(NetSyntheticKind.ArrayGet));
            Assert.That(set.Synthesis, Is.EqualTo(NetSyntheticKind.ArraySet));
            Assert.That(length.Synthesis, Is.EqualTo(NetSyntheticKind.ArrayLength));
            Assert.That(set.Parameters.Select(p => p.TypeFullName),
                Is.EqualTo(new[] { "System.Int32", "System.Int32" }),
                "index FIRST, value LAST — the order the shim's `a[i] = v` spelling reads back.");
        });

        var exports = NetShimGenerator.EmitExports(
            new NetSurface(new[] { get, set, length }, Array.Empty<string>()));

        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain("var rv_ = ((global::System.Int32[])self_!)[a0];"),
                "the READ is real C# indexing. Array.GetValue(int) is NOT a fallback — it "
                + "returns Object, which is permanently Rejected.");
            Assert.That(exports, Does.Contain("((global::System.Int32[])self_!)[a0] = a1;"),
                "the WRITE must not be spelled `a.Item = a0, a1`, which is what the generic "
                + "synthesized-setter arm would produce if the ArraySet kind were not asked "
                + "for FIRST.");
            Assert.That(exports, Does.Contain("var rv_ = ((global::System.Int32[])self_!).Length;"));
        });
    }

    [Test]
    public void AnIndexerWriteSynthesizesSetItemWithTheIndicesFirst()
    {
        // Task 7b REFUSED this at the synthesis point; §8.5 is what lifts the refusal, and the
        // lift happened in the one shared predicate so the analyzer's read-only-member guard
        // moved with it.
        var indexer = new NetMemberDescriptor(
            "Item", ListOfInt, NetMemberCategory.Property, isStatic: false, arity: 0,
            "System.Int32",
            new[] { new NetParameterDescriptor(NetRefKind.None, "System.Int32") });

        Assert.That(NetAccessorSynthesis.HasSynthesizableSetter(indexer), Is.True,
            "the Parameters.Count == 0 clause WAS the indexer refusal.");

        var setter = NetAccessorSynthesis.SetterFor(indexer);
        Assert.Multiple(() =>
        {
            Assert.That(setter.Name, Is.EqualTo("set_Item"));
            Assert.That(setter.Synthesis, Is.EqualTo(NetSyntheticKind.Setter));
            Assert.That(setter.Parameters.Select(p => p.TypeFullName),
                Is.EqualTo(new[] { "System.Int32", "System.Int32" }));
            Assert.That(NetAccessorSynthesis.IsSyntheticSetterShape(setter), Is.True,
                "the recognizer was already written as \"last parameter is the value\" so a "
                + "correct set_Item would still read as synthetic when it arrived.");
        });

        var exports = NetShimGenerator.EmitExports(
            new NetSurface(new[] { indexer, setter }, Array.Empty<string>()));
        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain(
                "var rv_ = ((global::System.Collections.Generic.List<System.Int32>)self_!)[a0];"));
            Assert.That(exports, Does.Contain(
                "((global::System.Collections.Generic.List<System.Int32>)self_!)[a0] = a1;"));
        });
    }

    [Test]
    public void AnOrdinaryPropertySetterIsUnchangedByTheIndexerLift()
    {
        // Provenance for the claim "an ordinary property degenerates to the Task-7a shape byte
        // for byte" — the lift must not move an existing export name.
        var property = new NetMemberDescriptor(
            "Length", "System.Text.StringBuilder", NetMemberCategory.Property,
            isStatic: false, arity: 0, "System.Int32", Array.Empty<NetParameterDescriptor>());

        var setter = NetAccessorSynthesis.SetterFor(property);
        Assert.Multiple(() =>
        {
            Assert.That(setter.Parameters.Select(p => p.TypeFullName),
                Is.EqualTo(new[] { "System.Int32" }));
            Assert.That(NetShimGenerator.EmitExports(
                    new NetSurface(new[] { setter }, Array.Empty<string>())),
                Does.Contain("((global::System.Text.StringBuilder)self_!).Length = a0;"));
        });
    }

    // ====================================================================================
    // 4. The symbol-carrying seam, and the collision it makes REACHABLE (§7.3/§12.4)
    // ====================================================================================

    [Test]
    public void TheSeamConstructsRatherThanResolvingToTheDefinition()
    {
        var resolver = SharedResolver.Value;

        // The by-name door — unchanged, and still correct for what it answers.
        Assert.That(resolver.ResolveType(ListOfInt)?.FullName,
            Is.EqualTo("System.Collections.Generic.List<T>"),
            "a name resolves to the DEFINITION; that is right for existence/accessibility/kind "
            + "and is why nothing before §8.5 needed a seam.");

        var constructed = resolver.ConstructedTypeSymbol(ListOfInt);
        Assert.That(constructed, Is.Not.Null);
        Assert.That(NetTypeResolver.TypeName(constructed), Is.EqualTo(ListOfInt),
            "and the seam round-trips back to the spelling TypeName produced.");
    }

    [Test]
    public void TwoConstructionsOfOneNestedGenericStillMangleApart()
    {
        // Task 8b's CollisionFreedomOverTwoConstructionsOfOneNestedGeneric was THEORETICAL
        // because no path could produce a constructed spelling. The seam above makes it
        // reachable; this asserts it stays green FOR THE RIGHT REASON — the two spellings are
        // genuinely different strings and NetShimGenerator.Plan's `seen.Add` keeps both.
        var ofInt = NetAccessorSynthesis.EnumerableGetEnumeratorFor("System.Int32");
        var ofString = NetAccessorSynthesis.EnumerableGetEnumeratorFor("System.String");

        Assert.That(ofInt.DeclaringTypeFullName, Is.Not.EqualTo(ofString.DeclaringTypeFullName),
            "the constructions differ in the SPELLING, which is what the mangler hashes.");
        Assert.That(NetNameMangler.Mangle(ofInt), Is.Not.EqualTo(NetNameMangler.Mangle(ofString)));

        var exports = NetShimGenerator.SurfaceDerivedExportNames(
            new NetSurface(new[] { ofInt, ofString }, Array.Empty<string>()));
        Assert.That(exports, Has.Count.EqualTo(2),
            "Plan's `if (!seen.Add(name)) continue;` silently DROPS a collision — two "
            + "constructions must survive it as two exports.");
    }

    [Test]
    public void ANestedConstructionResolvesItsArgumentsRecursively()
    {
        var nested = "System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>";
        Assert.That(SharedResolver.Value.EnumerableElementTypeName(nested),
            Is.EqualTo(ListOfInt),
            "argument parsing is depth-aware and each argument is resolved through the same "
            + "Lookup that accepts this class's own spellings.");
    }

    [Test]
    public void AnArrayElementTypeNeedsNoLookupAtAll()
    {
        // T[] implements IEnumerable<T> by definition, and GetTypeByMetadataName has no array
        // syntax — so the spelling itself is the answer.
        Assert.That(SharedResolver.Value.EnumerableElementTypeName("System.String[]"),
            Is.EqualTo("System.String"));
        Assert.That(SharedResolver.Value.EnumerableElementTypeName("System.Int32[][]"),
            Is.Null, "§8.5 v1 is one-dimensional; refusing beats a wrong element type.");
    }

    // ====================================================================================
    // 5. End to end through the real front end (the by-name door, a .NET array result)
    // ====================================================================================

    private static string CompileToCpp(string source)
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
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
    }

    private const string ArrayProgram = """
        Module M
         Sub Main()
          Dim r As New Regex("a+")
          Dim parts = r.Split("a-b-c")
          Console.WriteLine(parts(0))
         End Sub
        End Module
        """;

    [Test]
    public void AnInferredNetArrayResultKeepsTheHandleAndIndexesThroughTheSyntheticExport()
    {
        var cpp = CompileToCpp(ArrayProgram);

        Assert.That(cpp, Does.Contain("BasicLang::NetRef parts"),
            "§8.6 row 1: `Dim a = obj.GetValues()` (inferred) KEEPS THE HANDLE. Declaring it "
            + "std::vector<std::string> is the wild pointer:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("std::vector<std::string> parts"));

        var arrayGet = NetAccessorSynthesis.ArrayGetFor("System.String");
        Assert.That(cpp, Does.Contain("BasicLang::net::" + NetNameMangler.Mangle(arrayGet)),
            "`parts(0)` lowers to §8.5's synthetic array getter, not to native pointer "
            + "indexing:\n" + cpp);
    }

    [Test]
    public void ArrayLengthRoutesToTheSyntheticExportRatherThanTheNameKeyedArm()
    {
        // Review item 1. A handle System.String[] is TypeInfo(Name: "String", Kind: Array), so
        // BOTH branches of CppCodeGenerator's name/Kind-keyed `.Length` arm claimed it and
        // emitted `.length()` / `.size()` on a BasicLang::NetRef. There is no annotation to
        // find either — a .NET array declares no members in metadata at all, which is exactly
        // why §8.5 synthesizes the accessor.
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim parts = r.Split("a-b-c")
              Console.WriteLine(parts.Length)
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::net::" + NetNameMangler.Mangle(
                    NetAccessorSynthesis.ArrayLengthFor("System.String"))));
            Assert.That(cpp, Does.Not.Contain("parts.size()"),
                "the std::vector spelling — the Kind == Array branch claiming a handle.");
            Assert.That(cpp, Does.Not.Contain("parts.length()"),
                "the std::string spelling — the Name == \"String\" branch claiming a handle. "
                + "Both branches matched, which is why a marker guard and a producer were "
                + "needed rather than either alone.");
        });
    }

    [Test]
    public void AnyOtherMemberOnAHandleReceiverRefusesRatherThanEmittingNativeCode()
    {
        // The marker guard behind the producer: `arr.Length` routes, everything else must
        // REFUSE. A handle supports only the operations §8.5 emits an export for, and the
        // alternative to refusing is C++ that either does not compile or reinterprets a
        // 64-bit GCHandle as an object pointer.
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim parts = r.Split("a-b-c")
              Console.WriteLine(parts.Rank)
             End Sub
            End Module
            """));

        Assert.That(ex!.DiagnosticCode, Is.EqualTo("BL6019"));
        Assert.That(string.Join(" ", ex.Diagnostics), Does.Contain("System.String[]"),
            "the message must name the handle type the member was read from.");
    }

    [Test]
    public void ConstructedTypeSymbolRefusesATypeNestedInAnOpenGeneric()
    {
        // Review item 2. `List<T>.Enumerator` resolves through the metadata form
        // `List`1+Enumerator` to a symbol whose OWN Arity is 0 — so an arity-0 early return
        // hands back an OPEN type from a method whose contract is "construct, or answer null;
        // never guess". EnumerableElementTypeName degrades to null on that, but
        // ConstructedIndexer would describe an open-T indexer: a wrong descriptor, a wrong
        // export, and CS0246/CS0012 inside generated C# AFTER the ~25 s AOT publish.
        //
        // ⚠ A round-trip test alone does NOT catch this, and that was measured rather than
        // assumed: TypeName spells `List<T>.Enumerator` back byte-identically, because the
        // spelling is FAITHFUL — it is just open. Openness is the property, so it is the test.
        var resolver = SharedResolver.Value;
        const string openNested = "System.Collections.Generic.List<T>.Enumerator";

        Assert.That(resolver.ResolveType(openNested), Is.Not.Null,
            "guard: the spelling must still RESOLVE, or this test proves nothing about the "
            + "arity-0 path — it would be passing for lack of a symbol.");
        Assert.That(NetTypeResolver.TypeName(resolver.TypeSymbol(openNested)),
            Is.EqualTo(openNested),
            "guard: and it must round-trip, so this test cannot pass for the WRONG reason "
            + "(a spelling mismatch) instead of the openness one.");
        Assert.That(resolver.ConstructedTypeSymbol(openNested), Is.Null,
            "an open nested generic must not be answered as though it were constructed.");

        // A genuinely non-generic type still round-trips, so the guard is not a blanket refusal.
        Assert.That(resolver.ConstructedTypeSymbol("System.Text.StringBuilder"), Is.Not.Null);
    }

    [Test]
    public void ANetOutParameterEmitsOutNotRefOnTheCSharpBackend()
    {
        // Task-8 quality review I5. ByRefArguments is a List<bool> and cannot tell ref from
        // out, so CSharpBackend emitted `ref x` for a .NET `out` — a raw CS1620.
        // NetArgumentRefKinds carries the CLR kind alongside.
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var call = new IRCall("_tmp", "Probe.TryGet", new TypeInfo("Boolean", TypeKind.Primitive));
        call.Arguments.Add(new IRVariable("n", intType));
        call.ByRefArguments.Add(true);
        call.NetArgumentRefKinds.Add(NetRefKind.Out);

        Assert.That(EmitCSharp(call), Does.Contain("out n"),
            "a .NET `out` parameter must be spelled `out`; `ref n` is CS1620.");

        // A VB user function records nothing in NetArgumentRefKinds and keeps `ref`, which is
        // VB's only by-reference form — the fallback must not have moved.
        var userCall = new IRCall("_tmp2", "UserSub", new TypeInfo("Void", TypeKind.Void));
        userCall.Arguments.Add(new IRVariable("m", intType));
        userCall.ByRefArguments.Add(true);

        Assert.That(EmitCSharp(userCall), Does.Contain("ref m"));
    }

    /// <summary>A one-call module rendered by the C# backend.</summary>
    private static string EmitCSharp(IRCall call)
    {
        var module = new IRModule("RefKindModule");
        var main = new IRFunction("Main", new TypeInfo("Void", TypeKind.Void));
        var entry = new BasicBlock("entry");
        entry.AddInstruction(call);
        entry.AddInstruction(new IRReturn());
        main.Blocks.Add(entry);
        main.EntryBlock = entry;
        module.Functions.Add(main);
        return new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(module);
    }

    [Test]
    public void ForEachOverANetArrayDrivesTheInterfaceEnumeratorProtocol()
    {
        var cpp = CompileToCpp("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim parts = r.Split("a-b-c")
              For Each p In parts
               Console.WriteLine(p)
              Next
             End Sub
            End Module
            """);

        var element = "System.String";
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::net::" + NetNameMangler.Mangle(
                    NetAccessorSynthesis.EnumerableGetEnumeratorFor(element))));
            Assert.That(cpp, Does.Contain("while (BasicLang::net::" + NetNameMangler.Mangle(
                    NetAccessorSynthesis.EnumeratorMoveNext())));
            Assert.That(cpp, Does.Contain("BasicLang::net::" + NetNameMangler.Mangle(
                    NetAccessorSynthesis.EnumeratorCurrentFor(element))));
            Assert.That(cpp, Does.Not.Contain("for (std::string p : parts)"),
                "a range-for over a handle would compile to nothing sound — a handle supports "
                + "no operation the surface collector emitted an export for, and there is no "
                + "begin()/end() to find.");
        });
    }

    [Test]
    public void TheEnumerationMembersReachTheCollectedSurface()
    {
        // All FOUR together: a surface holding MoveNext but not Current is a shim the native
        // side fails to link against, which is worse than not lowering the loop.
        var parser = new Parser(new Lexer("""
            Module M
             Sub Main()
              Dim r As New Regex("a+")
              Dim parts = r.Split("a-b-c")
              For Each p In parts
               Console.WriteLine(p)
              Next
             End Sub
            End Module
            """).Tokenize());
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True);

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        var surface = NetSurfaceCollector.Collect(
            new[] { module }, project: null, resolverFactory: null,
            diagnostics: new List<NetReferenceDiagnostic>());

        var exports = NetShimGenerator.SurfaceDerivedExportNames(surface);
        var expected = new[]
        {
            NetAccessorSynthesis.EnumerableGetEnumeratorFor("System.String"),
            NetAccessorSynthesis.EnumeratorMoveNext(),
            NetAccessorSynthesis.EnumeratorCurrentFor("System.String"),
            NetAccessorSynthesis.EnumeratorDispose(),
        }.Select(NetNameMangler.Mangle);

        Assert.That(exports, Is.SupersetOf(expected),
            "the collector must see a For Each's FOUR members; they ride a bundle rather than "
            + "INetCarrying, which holds one.");
    }
}
