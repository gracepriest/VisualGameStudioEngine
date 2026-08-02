using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BasicLang;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 3 — <see cref="NetSurfaceCollector"/> (spec §7.1 + §7.2) and the
/// <c>&lt;NetProxy&gt;</c> project element, wired into <see cref="CppProjectBuilder.EmitCore"/>'s
/// phase 3.
///
/// <para><b>The two claims under test.</b> (1) §7.1: the surface is USED-ONLY — exactly the
/// members whose IR call nodes carry Task-2 carriage with a category outside
/// {NativeOwned, Bridged}, deduped by mangled name, from every position a call can occupy
/// (top-level, argument, inside a Try). (2) §7.2: a declared type expands through
/// <c>NetTypeResolver.CandidateMembers</c> — the collector CONSUMES the seam, so the corrected
/// rules (ctor type-locality: the <c>FileNotFoundException</c> 5-vs-15 measurement; base-chain
/// methods/properties; signature-complete identity) hold here by construction and the 5-ctor
/// oracle pins that the seam is actually the source.</para>
///
/// <para><b>Inertness is guarded elsewhere and stays untouched:</b> <c>NetInertnessTests</c>
/// (every template/parity/shape program still collects empty — no new diagnostics) and
/// <c>NetBuildPipelineTests</c> (the empty-surface file set byte-for-byte) both now run through
/// the REAL collector, since production passes <c>surfaceOverride: null</c>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class NetSurfaceCollectorTests
{
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Void);
    private static readonly TypeInfo BoolType = new TypeInfo("Boolean", TypeKind.Primitive);
    private static readonly TypeInfo StringType = new TypeInfo("String", TypeKind.Primitive);

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    // ------------------------------------------------------------------------------------
    // Shared plumbing.
    // ------------------------------------------------------------------------------------

    /// <summary>One resolver for the whole fixture — construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    /// <summary>
    /// <c>System.Text.RegularExpressions.Regex.IsMatch(String, String)</c> — the canonical
    /// carriage descriptor (same shape <c>NetIrCarriageTests</c> uses).
    /// </summary>
    private static NetMemberDescriptor RegexIsMatchDescriptor() =>
        new NetMemberDescriptor(
            "IsMatch",
            "System.Text.RegularExpressions.Regex",
            NetMemberCategory.Method,
            isStatic: true,
            arity: 0,
            typeFullName: "System.Boolean",
            parameters: new List<NetParameterDescriptor>
            {
                new NetParameterDescriptor(NetRefKind.None, "System.String"),
                new NetParameterDescriptor(NetRefKind.None, "System.String"),
            });

    private static IRCall ResolvedCall(
        string name, NetMemberDescriptor target, BoundaryTypeCategory category)
    {
        var call = new IRCall(name, "Regex.IsMatch", BoolType);
        call.Arguments.Add(new IRConstant("abc", StringType));
        call.Arguments.Add(new IRConstant("a.c", StringType));
        call.ResolvedNetTarget = target;
        call.NetCategory = category;
        return call;
    }

    private static IRModule ModuleOf(params IRInstruction[] instructions)
    {
        var module = new IRModule("CollectorModule");
        var main = new IRFunction("Main", VoidType);
        var entry = new BasicBlock("entry");
        foreach (var instruction in instructions)
            entry.AddInstruction(instruction);
        entry.AddInstruction(new IRReturn());
        main.Blocks.Add(entry);
        main.EntryBlock = entry;
        module.Functions.Add(main);
        return module;
    }

    private static NetSurface Collect(
        IEnumerable<IRModule>? modules = null,
        ProjectFile? project = null,
        Func<NetTypeResolver>? resolverFactory = null,
        List<NetReferenceDiagnostic>? diagnostics = null) =>
        NetSurfaceCollector.Collect(
            modules ?? Array.Empty<IRModule>(),
            project ?? new ProjectFile(),
            resolverFactory ?? (() => SharedResolver.Value),
            diagnostics ?? new List<NetReferenceDiagnostic>());

    private static ProjectFile ProjectDeclaring(params string[] typeNames)
    {
        var project = new ProjectFile();
        project.NetProxyTypes.AddRange(typeNames);
        return project;
    }

    /// <summary>The <c>NetTypeResolverTests.EmitFreshAssembly</c> pattern, verbatim.</summary>
    private string EmitProbeAssembly(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_dir, assemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True,
            "the FIXTURE failed to build its probe assembly (not the collector): " + string.Join("\n",
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    // ====================================================================================
    // §7.1 — BL-inferred, used-only.
    // ====================================================================================

    [Test]
    public void EmptyInputs_CollectTheEmptySurface_AndNeverForceTheResolver()
    {
        var factoryCalls = 0;
        var surface = Collect(
            modules: Array.Empty<IRModule>(),
            project: new ProjectFile(),
            resolverFactory: () => { factoryCalls++; return SharedResolver.Value; });

        Assert.Multiple(() =>
        {
            Assert.That(surface.IsNonEmpty, Is.False,
                "A project with no carriage and no <NetProxy> must collect the empty surface — "
                + "this is the whole inertness claim at the collector level.");
            Assert.That(factoryCalls, Is.Zero,
                "The collector forced the resolver factory with no <NetProxy> declared. "
                + "Resolution must stay lazy: the BL-inferred walk reads descriptors already on "
                + "the IR and needs no Roslyn at all.");
        });
    }

    [Test]
    public void UsedOnly_SameMemberReachedTwice_ContributesOneEntry()
    {
        // Two DISTINCT descriptor instances with identical content: the dedup key must be the
        // mangled name (content-derived), not descriptor reference identity.
        var module = ModuleOf(
            ResolvedCall("_t0", RegexIsMatchDescriptor(), BoundaryTypeCategory.Rejected),
            ResolvedCall("_t1", RegexIsMatchDescriptor(), BoundaryTypeCategory.Rejected));

        var surface = Collect(modules: new[] { module });

        Assert.That(surface.Members, Has.Count.EqualTo(1),
            "The same member reached from two call sites must contribute ONE surface entry, "
            + "deduped by NetNameMangler.Mangle — the key NetProxyEmitter and the shim "
            + "generator both collapse on (§12.4).");
        Assert.That(surface.Members[0].Name, Is.EqualTo("IsMatch"));
    }

    /// <summary>
    /// The category filter, spelled as EXPLICIT set membership. <c>NativeOwned == 0</c> is the
    /// enum default, so the two natively-handled categories and the three that contribute are
    /// each pinned individually — a truthiness shortcut would silently pass the wrong ones.
    /// </summary>
    [TestCase(BoundaryTypeCategory.NativeOwned, false)]
    [TestCase(BoundaryTypeCategory.Bridged, false)]
    [TestCase(BoundaryTypeCategory.ManagedOwned, true)]
    [TestCase(BoundaryTypeCategory.Rejected, true)]
    [TestCase(BoundaryTypeCategory.Unknown, true)]
    public void CategoryFilter_OnlyNonNativelyHandledCategoriesContribute(
        BoundaryTypeCategory category, bool expectContribution)
    {
        var module = ModuleOf(ResolvedCall("_t0", RegexIsMatchDescriptor(), category));

        var surface = Collect(modules: new[] { module });

        Assert.That(surface.Members.Count, Is.EqualTo(expectContribution ? 1 : 0),
            $"A resolved call stamped {category} "
            + (expectContribution
                ? "must contribute its descriptor (it is not natively handled)."
                : "must NOT contribute — {NativeOwned, Bridged} calls are handled without a "
                  + "proxy slot. Filter with explicit set membership, never truthiness: "
                  + "NativeOwned is enum value 0."));
    }

    [Test]
    public void ANodeWithoutACarriedDescriptor_NeverContributes()
    {
        // ManagedOwned category but a NULL ResolvedNetTarget: category alone is not carriage.
        var bare = new IRCall("_t0", "Regex.IsMatch", BoolType);
        bare.NetCategory = BoundaryTypeCategory.ManagedOwned;

        var surface = Collect(modules: new[] { ModuleOf(bare) });

        Assert.That(surface.IsNonEmpty, Is.False,
            "A call with no ResolvedNetTarget contributed to the surface. The collector must "
            + "key on the descriptor, not on the category — 'resolved' does not imply "
            + "'annotated', and unannotated calls are the designed inert state.");
    }

    [Test]
    public void InstanceAndBaseCallCarriage_Contributes()
    {
        var instance = new IRInstanceMethodCall(
            "_ti", new IRVariable("r", new TypeInfo("Regex", TypeKind.Class)), "IsMatch", BoolType);
        instance.ResolvedNetTarget = RegexIsMatchDescriptor();
        instance.NetCategory = BoundaryTypeCategory.Rejected;

        var baseCall = new IRBaseMethodCall("_tb", "GetBaseException", BoolType);
        baseCall.ResolvedNetTarget = new NetMemberDescriptor(
            "GetBaseException", "System.Exception", NetMemberCategory.Method,
            isStatic: false, arity: 0, typeFullName: "System.Exception",
            parameters: new List<NetParameterDescriptor>());
        baseCall.NetCategory = BoundaryTypeCategory.Unknown;

        var surface = Collect(modules: new[] { ModuleOf(instance, baseCall) });

        Assert.That(surface.Members.Select(m => m.Name),
            Is.EquivalentTo(new[] { "IsMatch", "GetBaseException" }),
            "IRInstanceMethodCall / IRBaseMethodCall carriage (the Task-2 fields) must reach "
            + "the surface exactly like IRCall's.");
    }

    /// <summary>
    /// Traversal completeness: a resolved call in ARGUMENT position of an unresolved outer
    /// call, and one nested inside a <c>Try</c> block. Neither appears as a top-level
    /// instruction of a plain walk, and each has silently missed collection = a missing proxy
    /// slot = a link error (or worse) later.
    /// </summary>
    [Test]
    public void NestedPositions_ArgumentOperandsAndTryBlocks_AreCollected()
    {
        var inArgument = ResolvedCall("_targ", RegexIsMatchDescriptor(), BoundaryTypeCategory.Rejected);
        var outer = new IRCall("_touter", "TakesABoolean", VoidType);
        outer.Arguments.Add(inArgument);

        var inTry = new IRInstanceMethodCall(
            "_ttry", new IRVariable("e", new TypeInfo("Exception", TypeKind.Class)),
            "GetBaseException", BoolType);
        inTry.ResolvedNetTarget = new NetMemberDescriptor(
            "GetBaseException", "System.Exception", NetMemberCategory.Method,
            isStatic: false, arity: 0, typeFullName: "System.Exception",
            parameters: new List<NetParameterDescriptor>());
        inTry.NetCategory = BoundaryTypeCategory.Unknown;

        var tryBlock = new BasicBlock("try_body");
        tryBlock.AddInstruction(inTry);
        var tryCatch = new IRTryCatch(tryBlock, new List<IRCatchClause>(), null, new BasicBlock("end"));

        var surface = Collect(modules: new[] { ModuleOf(outer, tryCatch) });

        Assert.That(surface.Members.Select(m => m.Name),
            Is.EquivalentTo(new[] { "IsMatch", "GetBaseException" }),
            "The IR walk missed a call in argument position or inside a Try block. It must "
            + "recurse IROperandWalker.EnumerateOperands and IRTryCatch's nested instruction "
            + "lists — a missed call is a missing proxy slot.");
    }

    // ====================================================================================
    // §7.2 — declared types. The ctor 5-vs-15 oracle.
    // ====================================================================================

    [Test]
    public void DeclaredType_FileNotFoundException_YieldsExactlyItsFiveDeclaredConstructors()
    {
        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("System.IO.FileNotFoundException"),
            diagnostics: diagnostics);

        var constructors = surface.Members
            .Where(m => m.Kind == NetMemberCategory.Constructor)
            .ToList();

        Assert.Multiple(() =>
        {
            // THE 5-vs-15 measurement (spec §7.2 correction blockquote): constructors come
            // from the queried type ONLY. 15 here means base-chain ctors leaked in — the
            // collector re-derived member rules instead of consuming CandidateMembers.
            Assert.That(constructors, Has.Count.EqualTo(5),
                "FileNotFoundException must yield exactly the 5 constructors it declares, not "
                + "the 15 a base-chain walk invents (spec §7.2's corrected ctor type-locality; "
                + "the collector must CONSUME NetTypeResolver.CandidateMembers, never re-derive "
                + "the walk). Got: "
                + string.Join(" | ", constructors.Select(c => c.ToString())));
            Assert.That(constructors.Select(c => c.DeclaringTypeFullName),
                Is.All.EqualTo("System.IO.FileNotFoundException"),
                "A constructor was collected from a BASE type — constructors are not inherited "
                + "and such a member cannot be called.");

            // Methods/properties DO come from the whole base chain (minus System.Object):
            // Message is declared on System.Exception and must be present.
            Assert.That(surface.Members.Any(m =>
                    m.Kind == NetMemberCategory.Property
                    && m.Name == "Message"
                    && m.DeclaringTypeFullName == "System.IO.FileNotFoundException"),
                Is.True,
                "FileNotFoundException overrides Message; the override (most-derived "
                + "declaration) must win the collapse and be present.");
            Assert.That(surface.Members.Any(m => m.Name == "GetBaseException"),
                Is.True,
                "Exception.GetBaseException (an inherited base-chain method) is missing — "
                + "methods and properties must come from the whole base chain minus "
                + "System.Object.");

            Assert.That(surface.DeclaredTypeNames,
                Is.EqualTo(new[] { "System.IO.FileNotFoundException" }),
                "DeclaredTypeNames must carry the <NetProxy Include> value verbatim.");
            Assert.That(diagnostics.Where(d => !d.IsWarning), Is.Empty,
                "A clean framework declaration produced an error-severity diagnostic: "
                + string.Join(" | ", diagnostics.Where(d => !d.IsWarning)
                    .Select(d => d.Code + ": " + d.Message)));
        });
    }

    [Test]
    public void DeclaredType_Unknown_IsABl6022Error_AndTheDeclarationStillCounts()
    {
        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("No.Such.TypeZzz"),
            diagnostics: diagnostics);

        var bl6022 = diagnostics.Where(d => d.Code == "BL6022").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(bl6022, Has.Count.EqualTo(1),
                "<NetProxy Include=\"No.Such.TypeZzz\" /> must produce exactly one BL6022 "
                + "(§11.4: '<NetProxy> names an unknown type'). Got: "
                + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(bl6022[0].IsWarning, Is.False,
                "BL6022 is an ERROR — §11.4 marks only BL6026 as a warning.");
            Assert.That(bl6022[0].Message, Does.Contain("No.Such.TypeZzz"),
                "BL6022 must name the declaration the user wrote.");
            Assert.That(surface.Members, Is.Empty);
            Assert.That(surface.DeclaredTypeNames, Does.Contain("No.Such.TypeZzz"),
                "The failed declaration must STILL appear in DeclaredTypeNames (verbatim): "
                + "dropping it would let a Library project skip BL6025 and build a shim-less "
                + "binary that looks clean and does nothing.");
            Assert.That(surface.IsNonEmpty, Is.True);
        });
    }

    // ====================================================================================
    // §7.2 omission — BL6026, always a warning, read via Roslyn at phase 3.
    // ====================================================================================

    [Test]
    public void DeclaredType_RequiresDynamicCodeMember_IsOmittedWithABl6026Warning()
    {
        var probe = EmitProbeAssembly("BlColAot" + Guid.NewGuid().ToString("N"), """
            using System.Diagnostics.CodeAnalysis;
            namespace Contoso
            {
                public class Aot
                {
                    public int Fine() { return 1; }
                    [RequiresDynamicCode("emits IL at runtime")]
                    public int Jit() { return 2; }
                }
            }
            """);
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("Contoso.Aot"),
            resolverFactory: () => resolver,
            diagnostics: diagnostics);

        var bl6026 = diagnostics.Where(d => d.Code == "BL6026").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(surface.Members.Select(m => m.Name), Does.Contain("Fine"),
                "The unattributed member must survive.");
            Assert.That(surface.Members.Select(m => m.Name), Does.Not.Contain("Jit"),
                "A [RequiresDynamicCode] member must be OMITTED from the surface (spec §7.2) — "
                + "read via Roslyn at phase 3, never from ILC output (the phase-circularity "
                + "trap: the omission set must be final before any proxy header is emitted).");
            Assert.That(bl6026, Has.Count.EqualTo(1),
                "The omission must be announced with exactly one BL6026. Got: "
                + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(bl6026[0].IsWarning, Is.True,
                "BL6026 is ALWAYS a warning (§11.4) — an omitted declared member still builds.");
            Assert.That(bl6026[0].Message, Does.Contain("Contoso.Aot")
                    .And.Contain("Jit").And.Contain("RequiresDynamicCode"),
                "BL6026 must name the type, the member, and the offending signal.");
        });
    }

    [Test]
    public void DeclaredType_AotHostileDeclaringType_OmitsEveryMember()
    {
        var probe = EmitProbeAssembly("BlColRuc" + Guid.NewGuid().ToString("N"), """
            using System.Diagnostics.CodeAnalysis;
            namespace Contoso
            {
                [RequiresUnreferencedCode("walks arbitrary types via reflection")]
                public class Hostile
                {
                    public int Touch() { return 1; }
                }
            }
            """);
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("Contoso.Hostile"),
            resolverFactory: () => resolver,
            diagnostics: diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(surface.Members, Is.Empty,
                "[RequiresUnreferencedCode] on the DECLARING TYPE must omit every member — "
                + "§7.2's rule covers the member, its accessors, and its declaring type.");
            Assert.That(diagnostics.Where(d => d.Code == "BL6026"), Is.Not.Empty,
                "The omissions must be announced (BL6026), not silent.");
            Assert.That(diagnostics.Where(d => !d.IsWarning), Is.Empty,
                "AOT-hostility on a declared type is omission, never an error — the project "
                + "still builds (spec §7.2).");
            Assert.That(surface.IsNonEmpty, Is.True,
                "A declaration whose expansion omitted everything is still a declaration — "
                + "IsNonEmpty must key on DeclaredTypeNames too.");
        });
    }

    [Test]
    public void DeclaredType_Regex_OmitsSpanSignaturesButKeepsStringOverloads()
    {
        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("System.Text.RegularExpressions.Regex"),
            diagnostics: diagnostics);

        var surviving = surface.Members;
        Assert.Multiple(() =>
        {
            // The spec's own worked example: without omission, Regex fails on
            // IsMatch(ReadOnlySpan<Char>) — a ref struct §8.3 cannot carry.
            Assert.That(diagnostics.Any(d => d.Code == "BL6026"
                    && d.IsWarning
                    && d.Message.Contains("ReadOnlySpan")), Is.True,
                "Regex's ReadOnlySpan<Char> overloads must each be omitted with a BL6026 "
                + "warning naming the offending type (spec §7.2/§8.3: ref structs cannot be "
                + "boxed). Got: " + string.Join(" | ",
                    diagnostics.Select(d => d.Code + ": " + d.Message)));
            // "System.ReadOnlySpan<"/"System.Span<" exactly — NOT a bare "Span" contains,
            // which would false-positive on System.TimeSpan (a perfectly marshalable §6.4
            // NativeOwned pair). Measured: Regex.IsMatch(String, String, RegexOptions,
            // TimeSpan) legitimately survives.
            Assert.That(surviving.Where(m => m.Parameters.Any(
                    p => p.TypeFullName.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal)
                         || p.TypeFullName.StartsWith("System.Span<", StringComparison.Ordinal))),
                Is.Empty,
                "A ref-struct Span-bearing signature survived into the surface — the generated "
                + "proxy overload set must be a SUBSET of the .NET overload set.");
            Assert.That(surviving.Any(m => m.Name == "IsMatch"
                    && m.IsStatic
                    && m.Parameters.Select(p => p.TypeFullName)
                        .SequenceEqual(new[] { "System.String", "System.String" })), Is.True,
                "The marshalable static IsMatch(String, String) overload must survive.");
            Assert.That(diagnostics.Where(d => !d.IsWarning), Is.Empty,
                "Unmarshalable members of a DECLARED type are omissions (BL6026 warnings), "
                + "never errors: " + string.Join(" | ", diagnostics.Where(d => !d.IsWarning)
                    .Select(d => d.Code + ": " + d.Message)));
        });
    }

    // ====================================================================================
    // Decision-path guards (spec-review adjudicated additions beyond the plan text —
    // each of these four arms was added by Task 3 and deserves its own test).
    // ====================================================================================

    /// <summary>
    /// Decision: an AMBIGUOUS declared type is BL6023 (the "ambiguous .NET TYPE reference"
    /// code, §6.5/§11.4), an ERROR on this path — never a fall-through to BL6022's "unknown",
    /// and never a silent zero-member expansion.
    /// </summary>
    [Test]
    public void DeclaredType_DeclaredInTwoReferences_IsABl6023Error()
    {
        var salt = Guid.NewGuid().ToString("N");
        var a = EmitProbeAssembly("BlColDupA" + salt,
            "namespace Contoso { public class Dup { public int FromA() { return 1; } } }");
        var b = EmitProbeAssembly("BlColDupB" + salt,
            "namespace Contoso { public class Dup { public int FromB() { return 2; } } }");
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { a, b }));

        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("Contoso.Dup"),
            resolverFactory: () => resolver,
            diagnostics: diagnostics);

        var bl6023 = diagnostics.Where(d => d.Code == "BL6023").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(bl6023, Has.Count.EqualTo(1),
                "A <NetProxy> type declared in TWO referenced assemblies must produce exactly "
                + "one BL6023 (ambiguous .NET type reference) — not BL6022, which would tell "
                + "the user a type they can see declared twice was 'not found'. Got: "
                + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(bl6023[0].IsWarning, Is.False,
                "Ambiguity of a DECLARED type is an error: the declaration cannot be honored.");
            Assert.That(bl6023[0].Message,
                Does.Contain("Contoso.Dup").And.Contain("more than one referenced assembly"),
                "BL6023 must name the declaration and say WHY it cannot be expanded.");
            Assert.That(surface.Members, Is.Empty,
                "An ambiguous declaration must expand to nothing — picking a winner silently "
                + "binds the build to whichever assembly enumerated first.");
            Assert.That(surface.DeclaredTypeNames, Does.Contain("Contoso.Dup"));
        });
    }

    /// <summary>
    /// Decision: a type that RESOLVES but is not effectively public is BL6022 with its own
    /// message — the generated shim would reference it and die in csc with CS0122, the exact
    /// late-failure shape phase 3 exists to move earlier.
    /// </summary>
    [Test]
    public void DeclaredType_ResolvedButNotPublic_IsABl6022Error_WithTheNonPublicMessage()
    {
        var probe = EmitProbeAssembly("BlColHid" + Guid.NewGuid().ToString("N"),
            "namespace Contoso { internal class Hidden { public int Peek() { return 1; } } }");
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("Contoso.Hidden"),
            resolverFactory: () => resolver,
            diagnostics: diagnostics);

        var bl6022 = diagnostics.Where(d => d.Code == "BL6022").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(bl6022, Has.Count.EqualTo(1),
                "An internal declared type must produce exactly one BL6022. Got: "
                + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(bl6022[0].IsWarning, Is.False);
            Assert.That(bl6022[0].Message,
                Does.Contain("Contoso.Hidden").And.Contain("not publicly accessible"),
                "The non-public case must carry its DISTINCT message — telling the user a type "
                + "that exists was 'not found' sends them hunting the wrong bug.");
            Assert.That(bl6022[0].Message, Does.Not.Contain("unknown"),
                "The non-public message must not be the unknown-type message.");
            Assert.That(surface.Members, Is.Empty,
                "No member of a non-public type may be expanded — the shim cannot reference "
                + "any of them (CS0122).");
        });
    }

    /// <summary>
    /// Decision: a GENERIC METHOD (arity &gt; 0) is BL6026-omitted even before its signature
    /// is walked — its type parameters are types §8.3 has no wire form for, whether or not
    /// they appear in the parameter list; a monomorphic C export cannot carry an open T.
    /// </summary>
    [Test]
    public void DeclaredType_GenericMethod_IsOmittedWithBl6026_NamingTheGenericRule()
    {
        var probe = EmitProbeAssembly("BlColGen" + Guid.NewGuid().ToString("N"), """
            namespace Contoso
            {
                public class Gen
                {
                    public T Echo<T>(T value) { return value; }
                    public int Plain() { return 1; }
                }
            }
            """);
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("Contoso.Gen"),
            resolverFactory: () => resolver,
            diagnostics: diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(surface.Members.Select(m => m.Name), Does.Contain("Plain"),
                "The non-generic sibling must survive.");
            Assert.That(surface.Members.Select(m => m.Name), Does.Not.Contain("Echo"),
                "A generic method must be OMITTED — an open T has no wire form (§8.3).");
            Assert.That(diagnostics.Any(d => d.Code == "BL6026"
                    && d.IsWarning
                    && d.Message.Contains("Echo")
                    && d.Message.Contains("generic method")), Is.True,
                "The omission must be announced by BL6026 naming the member AND the "
                + "generic-method rule (not merely a signature-type finding). Got: "
                + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message)));
        });
    }

    /// <summary>
    /// Decision: an ARRAY is judged by its ELEMENT type — <c>int[]</c> crosses as a handle
    /// (§8.6's representation rule: a .NET array VALUE is always a handle) and survives, while
    /// <c>object[]</c> is omitted because its element is permanently Rejected: every element a
    /// Task-9 accessor could ever hand out would be an <c>Object</c> handle no export can
    /// consume. This deliberately pins the <c>params Object[]</c> casualty class.
    /// </summary>
    [Test]
    public void DeclaredType_Arrays_AreJudgedByElement_IntSurvivesObjectIsOmitted()
    {
        var probe = EmitProbeAssembly("BlColArr" + Guid.NewGuid().ToString("N"), """
            namespace Contoso
            {
                public class Arrays
                {
                    public int[] GetInts() { return new int[0]; }
                    public object[] GetObjects() { return new object[0]; }
                }
            }
            """);
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = Collect(
            project: ProjectDeclaring("Contoso.Arrays"),
            resolverFactory: () => resolver,
            diagnostics: diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(surface.Members.Select(m => m.Name), Does.Contain("GetInts"),
                "int[] must survive: a .NET array value is always a handle (§8.6), and an "
                + "int element has a §8.3 row.");
            Assert.That(surface.Members.Select(m => m.Name), Does.Not.Contain("GetObjects"),
                "object[] must be OMITTED — the element is permanently Rejected, so the "
                + "handle's contents are unreachable by construction.");
            Assert.That(diagnostics.Any(d => d.Code == "BL6026"
                    && d.IsWarning
                    && d.Message.Contains("GetObjects")
                    && d.Message.Contains("uses 'object'")), Is.True,
                "The omission must name the member and the offending ELEMENT type (the array "
                + "is judged by its element, and the diagnostic must say which one). Got: "
                + string.Join(" | ", diagnostics.Select(d => d.Code + ": " + d.Message)));
        });
    }

    // ====================================================================================
    // <NetProxy> in the project file — parse + round-trip.
    // ====================================================================================

    [Test]
    public void NetProxy_ParsesFromBlproj_AndSurvivesLoadSaveLoad()
    {
        var path = Path.Combine(_dir, "Declared.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Declared</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <NetProxy Include="MyLib.Customer" />
                <NetProxy Include="System.Text.RegularExpressions.Regex" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var loaded = ProjectFile.Load(path);
        Assert.That(loaded.NetProxyTypes,
            Is.EqualTo(new[] { "MyLib.Customer", "System.Text.RegularExpressions.Regex" }),
            "<NetProxy Include> must parse verbatim, in declaration order (spec §7.2's own "
            + "worked example).");

        var savedPath = Path.Combine(_dir, "Declared.roundtrip.blproj");
        loaded.Save(savedPath);
        var reloaded = ProjectFile.Load(savedPath);

        Assert.That(reloaded.NetProxyTypes, Is.EqualTo(loaded.NetProxyTypes),
            "<NetProxy> declarations were lost in a load/save/load round trip — "
            + "ProjectFile.Save must emit them the way it emits <Reference> items.");
    }

    // ====================================================================================
    // The wiring: CppProjectBuilder.EmitCore phase 3 runs the real collector.
    // ====================================================================================

    private string WriteProject(
        IReadOnlyDictionary<string, string> files, string outputType, string itemGroupXml)
    {
        foreach (var file in files)
        {
            var full = Path.Combine(_dir, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, file.Value);
        }
        var projectPath = Path.Combine(_dir, "SurfaceProbe.blproj");
        File.WriteAllText(projectPath, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>SurfaceProbe</ProjectName>
                <OutputType>{outputType}</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              {itemGroupXml}
            </BasicLangProject>
            """);
        return projectPath;
    }

    /// <summary>Resolvable but never run — the NetBuildPipelineTests pattern.</summary>
    private static CppToolchain FakeToolchain() =>
        CppToolchain.FromExplicit("llvm", Path.Combine(Path.GetTempPath(), "no-such-clang++.exe"));

    private (CppProjectBuildResult Result, CppEmitOutcome Outcome) Emit(
        IReadOnlyDictionary<string, string> files, string outputType, string itemGroupXml)
    {
        var projectPath = WriteProject(files, outputType, itemGroupXml);
        var result = new CppProjectBuildResult();
        var outcome = CppProjectBuilder.EmitCore(
            ProjectFile.Load(projectPath), "Release", result,
            resolveToolchain: FakeToolchain, forIntelliSense: false);
        return (result, outcome);
    }

    private const string LibrarySource = """
        Module Logic
         Function Twice(n As Integer) As Integer
          Return n + n
         End Function
        End Module
        """;

    [Test]
    public void EmitCore_UnknownNetProxy_FailsWithBl6022_OnBothChannels()
    {
        var (result, outcome) = Emit(
            new Dictionary<string, string> { ["main.cpp"] = "int main() { return 0; }\n" },
            outputType: "Exe",
            itemGroupXml: """
                <ItemGroup>
                  <NetProxy Include="No.Such.TypeZzz" />
                </ItemGroup>
                """);

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Code),
                Does.Contain("BL6022"),
                "An unknown <NetProxy> type must fail the BUILD with BL6022 — the same "
                + "error-mapping channel the reference resolver's BL6021 uses.");
            Assert.That(outcome.Completed, Is.False,
                "BL6022 fired but EmitCore carried on regardless.");
            Assert.That((outcome.NetReferences?.Diagnostics
                        ?? Array.Empty<NetReferenceDiagnostic>()).Select(d => d.Code),
                Does.Contain("BL6022"),
                "Collector diagnostics must ALSO be merged into the closure — "
                + "outcome.NetReferences.Diagnostics is pinned as the complete record.");
        });
    }

    /// <summary>
    /// BL6025 reachable FOR REAL for the first time: no <c>surfaceOverride</c> — the surface is
    /// non-empty because the project DECLARES a type (the gate at
    /// <c>CppProjectBuilder.EmitCore</c>'s BL6025 block keys on <c>surface.IsNonEmpty</c>).
    /// </summary>
    [Test]
    public void EmitCore_LibraryWithADeclaredNetProxy_FailsWithBl6025()
    {
        var (result, outcome) = Emit(
            new Dictionary<string, string> { ["Logic.bas"] = LibrarySource },
            outputType: "Library",
            itemGroupXml: """
                <ItemGroup>
                  <NetProxy Include="System.Text.RegularExpressions.Regex" />
                </ItemGroup>
                """);

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Code),
                Does.Contain("BL6025"),
                "A Library output with a real (collector-produced) non-empty .NET surface must "
                + "be rejected with BL6025 (§9.5) — this is the first time the gate is "
                + "reachable without the test seam.");
            Assert.That(outcome.Completed, Is.False);
        });
    }

    /// <summary>
    /// The "surfaces live" proof end-to-end: a pure-C++ Exe declaring a framework type gets
    /// <c>NetProxyEmitter</c>'s five artifacts in <c>obj/gen</c> and the startup TU in its
    /// compile inputs — with NO override anywhere.
    /// </summary>
    [Test]
    public void EmitCore_DeclaredNetProxyOnAnExe_EmitsTheProxyArtifactsFromTheRealCollector()
    {
        var (result, outcome) = Emit(
            new Dictionary<string, string> { ["main.cpp"] = "int main() { return 0; }\n" },
            outputType: "Exe",
            itemGroupXml: """
                <ItemGroup>
                  <NetProxy Include="System.IO.FileNotFoundException" />
                </ItemGroup>
                """);

        var objGen = Path.Combine(_dir, "obj", "gen");
        var generated = Directory.Exists(objGen)
            ? Directory.EnumerateFiles(objGen).Select(Path.GetFileName).ToList()
            : new List<string>();

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning), Is.Empty,
                "A clean framework <NetProxy> declaration failed the build: " + string.Join(" | ",
                    result.Diagnostics.Where(d => !d.IsWarning)
                        .Select(d => d.Code + ": " + d.Message)));
            Assert.That(outcome.Completed, Is.True);
            Assert.That(generated, Is.SupersetOf(new[]
                {
                    NetProxyEmitter.ContractHeaderFileName,
                    NetProxyEmitter.RuntimeHeaderFileName,
                    NetProxyEmitter.BindingsFileName,
                    NetProxyEmitter.ProxiesFileName,
                    NetProxyEmitter.StartupFileName,
                }),
                "The declared surface did not reach NetProxyEmitter — phase 3's collector is "
                + "not wired into EmitCore, or the artifact merge lost its output.");
            Assert.That(outcome.Request.SourceFiles,
                Does.Contain(Path.Combine(objGen, NetProxyEmitter.StartupFileName)),
                "blnet_startup.g.cpp must be in the compile inputs (§9.5's TU union).");
        });
    }
}
