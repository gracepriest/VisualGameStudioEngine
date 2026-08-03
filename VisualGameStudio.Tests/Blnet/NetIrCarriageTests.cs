using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-1 Task 10 — the IR carriage (<see cref="IRCall.ResolvedNetTarget"/> /
/// <see cref="IRCall.NetCategory"/>) that nothing reads until P2a-2, and the optimizer's
/// obligation to preserve it. P2a-2's lowering dispatches on these fields; a copy path that
/// drops one makes the lowering fall back silently to name-based dispatch, which is the
/// wild-pointer class spec §8.5 exists to prevent.
///
/// <para><b>READ THIS BEFORE TRUSTING THE ROUND-TRIP TESTS — measured at
/// <c>00ff06e</c>.</b> There is <b>no code anywhere in the compiler that copies an existing
/// <see cref="IRCall"/> into a new one.</b> All 20 <c>new IRCall(...)</c> sites live in
/// <c>IRBuilder.cs</c> and build fresh nodes from the AST; <c>IROptimizer.cs</c> has zero. The
/// two clone helpers that exist —
/// <c>FunctionInliningPass.CloneAndRemap</c> (IROptimizer.cs:1381) and
/// <c>LoopUnrollingPass.CloneInstruction</c> (IROptimizer.cs:2245) — both fall through to
/// <c>default: return inst;</c> for a call, i.e. they return the SAME OBJECT. Every other pass
/// that touches a call mutates it in place (arguments, <c>IsTailCall</c>) or refuses to move it.
///
/// <para>So the fields survive today by <b>aliasing, not by copy logic</b>, and
/// <see cref="OptimizerPreservesTheResolvedNetTargetAndCategoryMarker"/> — the round trip the
/// plan specifies — <b>cannot fail at this commit no matter what the field-carrying code says,
/// because there is none</b>. It is kept as a regression guard for the day a pass starts
/// rebuilding calls, not as evidence that anything works. It is
/// <see cref="AggressivePipelinePreservesCarriageThroughTheInliningClonePath"/> that has teeth:
/// it drives the .NET call through <c>CloneAndRemap</c>, the one clone path an
/// <see cref="IRCall"/> can actually reach, so adding a dropping <c>case IRCall</c> there turns
/// it red. That is the mutation this file was verified against.</para>
///
/// <para><see cref="IrBuilderStampsTheReceiversBoundaryCategoryOnAFusedStaticCall"/> and
/// <see cref="AnOrdinaryUserCallCarriesNoNetCarriage"/> are the non-vacuous half: they pin that
/// population actually happens on the real IRBuilder path, and that it stays inert for calls
/// that have nothing to do with .NET.</para>
/// </summary>
[TestFixture]
public class NetIrCarriageTests
{
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Void);
    private static readonly TypeInfo BoolType = new TypeInfo("Boolean", TypeKind.Primitive);
    private static readonly TypeInfo StringType = new TypeInfo("String", TypeKind.Primitive);

    /// <summary>
    /// A descriptor standing in for what P2a-2's resolver will attach:
    /// <c>System.Text.RegularExpressions.Regex.IsMatch(String, String)</c>. Regex is the
    /// canonical ManagedOwned member — spec §4.2's <c>Regex_Match__string</c> slot — so this is
    /// the shape whose loss would actually cost a shim dispatch.
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

    /// <summary>
    /// A module whose <c>Main</c> holds ONE resolved .NET call. Hand-built rather than compiled
    /// from BasicLang on purpose: in P2a-1 <c>IRBuilder</c> has no <c>NetTypeResolver</c>, so no
    /// BL source can produce a non-null <see cref="IRCall.ResolvedNetTarget"/> — a
    /// source-compiled fixture would hit the plan's own <c>Is.Not.Null</c> guard and make the
    /// whole round trip vacuous.
    /// </summary>
    private static IRModule BuildModuleWithAResolvedNetCall(NetMemberDescriptor target)
    {
        var module = new IRModule("CarriageModule");
        var main = new IRFunction("Main", VoidType);
        var entry = new BasicBlock("entry");

        var call = new IRCall("_tmp_regex", "Regex.IsMatch", BoolType);
        call.Arguments.Add(new IRConstant("abc", StringType));
        call.Arguments.Add(new IRConstant("a.c", StringType));
        call.ResolvedNetTarget = target;
        call.NetCategory = BoundaryTypeCategory.ManagedOwned;

        entry.AddInstruction(call);
        entry.AddInstruction(new IRReturn());
        main.Blocks.Add(entry);
        main.EntryBlock = entry;
        module.Functions.Add(main);
        return module;
    }

    private static IRCall FindCall(IRModule module, string functionName = "Regex.IsMatch") =>
        module.Functions
              .SelectMany(f => f.Blocks)
              .SelectMany(b => b.Instructions)
              .OfType<IRCall>()
              .Single(c => c.FunctionName == functionName);

    // ------------------------------------------------------------------------------------
    // The plan's round trip. See the class remarks: this cannot fail at 00ff06e.
    // ------------------------------------------------------------------------------------

    [Test]
    public void OptimizerPreservesTheResolvedNetTargetAndCategoryMarker()
    {
        var module = BuildModuleWithAResolvedNetCall(RegexIsMatchDescriptor());

        // Capture VALUES, not the node. The pipeline mutates in place, so a captured node
        // reference would make both sides the same object and the assertion vacuous.
        var expectedTarget = FindCall(module).ResolvedNetTarget;
        var expectedCategory = FindCall(module).NetCategory;
        Assert.That(expectedTarget, Is.Not.Null, "guard: the fixture must build a RESOLVED call");

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        var after = FindCall(module);
        Assert.That(after.ResolvedNetTarget, Is.EqualTo(expectedTarget),
            "The optimizer dropped the resolved .NET target. FIX THE OPTIMIZER (BasicLang/"
            + "IROptimizer.cs), not this test: every IR node copy/clone path must carry "
            + "ResolvedNetTarget, or P2a-2's lowering silently falls back to name-based dispatch "
            + "— which is the wild-pointer class spec §8.5 exists to prevent.");
        Assert.That(after.NetCategory, Is.EqualTo(expectedCategory),
            "The optimizer dropped the boundary category marker. FIX THE OPTIMIZER (BasicLang/"
            + "IROptimizer.cs), not this test.");
    }

    // ------------------------------------------------------------------------------------
    // The one with teeth: drives the call through CloneAndRemap.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// The <b>only</b> clone path an <see cref="IRCall"/> can reach:
    /// <c>FunctionInliningPass.CloneAndRemap</c>. The .NET call is placed inside a small
    /// inlineable helper, so inlining copies the helper's body into <c>Main</c> and every
    /// instruction in it goes through the clone switch. Adding a <c>case IRCall</c> there that
    /// rebuilds the node without carrying the two fields fails THIS test — which is exactly the
    /// regression the carriage exists to prevent, and the mutation this file was verified
    /// against.
    ///
    /// <para>Note <c>FunctionInliningPass</c> is in <c>AddAggressivePasses</c>, NOT
    /// <c>AddStandardPasses</c> — so the standard-pipeline round trip above never reaches it.
    /// That asymmetry is the whole reason this test exists separately.</para>
    /// </summary>
    [Test]
    public void AggressivePipelinePreservesCarriageThroughTheInliningClonePath()
    {
        var target = RegexIsMatchDescriptor();

        var module = new IRModule("CarriageInlineModule");

        // Helper() — small enough to inline (<=10 instructions, <=2 blocks, no self-call).
        var helper = new IRFunction("Helper", VoidType);
        var helperEntry = new BasicBlock("entry");
        var netCall = new IRCall("_tmp_regex", "Regex.IsMatch", BoolType);
        netCall.Arguments.Add(new IRConstant("abc", StringType));
        netCall.ResolvedNetTarget = target;
        netCall.NetCategory = BoundaryTypeCategory.ManagedOwned;
        helperEntry.AddInstruction(netCall);
        helperEntry.AddInstruction(new IRReturn());
        helper.Blocks.Add(helperEntry);
        helper.EntryBlock = helperEntry;
        module.Functions.Add(helper);

        // Main() — calls Helper(), giving the inliner a site to rewrite.
        var main = new IRFunction("Main", VoidType);
        var mainEntry = new BasicBlock("entry");
        mainEntry.AddInstruction(new IRCall("_tmp_helper", "Helper", VoidType));
        mainEntry.AddInstruction(new IRReturn());
        main.Blocks.Add(mainEntry);
        main.EntryBlock = mainEntry;
        module.Functions.Add(main);

        var expectedTarget = target;
        var expectedCategory = BoundaryTypeCategory.ManagedOwned;

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        // Guard: the inliner must actually have fired, or this test proves nothing about the
        // clone path. After inlining, Main's block holds the .NET call and no call to Helper.
        var mainInstructions = module.Functions
                                     .Single(f => f.Name == "Main")
                                     .Blocks.SelectMany(b => b.Instructions)
                                     .ToList();
        Assert.That(mainInstructions.OfType<IRCall>().Any(c => c.FunctionName == "Regex.IsMatch"),
            Is.True,
            "guard: FunctionInliningPass did not inline Helper into Main, so CloneAndRemap never "
            + "saw the .NET call and this test proves nothing. FIX THE TEST FIXTURE (make Helper "
            + "inlineable again — see FunctionInliningPass.IsInlineable, IROptimizer.cs:1276), "
            + "not the optimizer.");

        var inlined = mainInstructions.OfType<IRCall>().Single(c => c.FunctionName == "Regex.IsMatch");
        Assert.That(inlined.ResolvedNetTarget, Is.EqualTo(expectedTarget),
            "Inlining rebuilt the .NET call and dropped ResolvedNetTarget. FIX THE OPTIMIZER's "
            + "clone path (FunctionInliningPass.CloneAndRemap, BasicLang/IROptimizer.cs:1381) — "
            + "any IRCall case there must carry ResolvedNetTarget and NetCategory across. Losing "
            + "it makes P2a-2's lowering fall back to name-based dispatch (spec §8.5).");
        Assert.That(inlined.NetCategory, Is.EqualTo(expectedCategory),
            "Inlining rebuilt the .NET call and dropped NetCategory. FIX THE OPTIMIZER's clone "
            + "path (FunctionInliningPass.CloneAndRemap, BasicLang/IROptimizer.cs:1381).");
    }

    /// <summary>
    /// P2a-2 Task 7a widened the carriage to <see cref="IRNewObject"/> /
    /// <see cref="IRFieldAccess"/> / <see cref="IRFieldStore"/> and added the name-only
    /// gate's <c>ResolvedNetTargetIsExact</c> bit. Same teeth as the call-node test above:
    /// all three ride through <c>FunctionInliningPass.CloneAndRemap</c> inside an inlined
    /// helper, so any future <c>case</c> added there that rebuilds one of them without
    /// copying all THREE fields goes red here.
    /// </summary>
    [Test]
    public void AggressivePipelinePreservesTask7aCarriageOnTheNewNodeTypes()
    {
        var ctor = new NetMemberDescriptor(
            ".ctor", "System.Text.RegularExpressions.Regex", NetMemberCategory.Constructor,
            isStatic: false, arity: 0, "System.Void",
            new List<NetParameterDescriptor> { new(NetRefKind.None, "System.String") });
        var property = new NetMemberDescriptor(
            "Position", "System.IO.Stream", NetMemberCategory.Property,
            isStatic: false, arity: 0, "System.Int64",
            new List<NetParameterDescriptor>());
        var setter = NetAccessorSynthesis.SetterFor(property);

        var module = new IRModule("CarriageInline7aModule");

        var helper = new IRFunction("Helper", VoidType);
        var helperEntry = new BasicBlock("entry");
        var recv = new IRVariable("st", new TypeInfo("Stream", TypeKind.Class));
        var construction = new IRNewObject("_tmp_new", "Regex",
            new TypeInfo("Regex", TypeKind.Class));
        construction.Arguments.Add(new IRConstant("a+", StringType));
        construction.ResolvedNetTarget = ctor;
        construction.NetCategory = BoundaryTypeCategory.ManagedOwned;
        construction.ResolvedNetTargetIsExact = true;
        var read = new IRFieldAccess("_tmp_get", recv, "Position",
            new TypeInfo("Long", TypeKind.Primitive));
        read.ResolvedNetTarget = property;
        read.NetCategory = BoundaryTypeCategory.ManagedOwned;
        read.ResolvedNetTargetIsExact = true;
        var write = new IRFieldStore(recv, "Position", new IRConstant(5L, new TypeInfo("Long", TypeKind.Primitive)));
        write.ResolvedNetTarget = setter;
        write.NetCategory = BoundaryTypeCategory.ManagedOwned;
        write.ResolvedNetTargetIsExact = true;
        helperEntry.AddInstruction(construction);
        helperEntry.AddInstruction(read);
        helperEntry.AddInstruction(write);
        helperEntry.AddInstruction(new IRReturn());
        helper.Blocks.Add(helperEntry);
        helper.EntryBlock = helperEntry;
        module.Functions.Add(helper);

        var main = new IRFunction("Main", VoidType);
        var mainEntry = new BasicBlock("entry");
        mainEntry.AddInstruction(new IRCall("_tmp_helper", "Helper", VoidType));
        mainEntry.AddInstruction(new IRReturn());
        main.Blocks.Add(mainEntry);
        main.EntryBlock = mainEntry;
        module.Functions.Add(main);

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        var mainInstructions = module.Functions
                                     .Single(f => f.Name == "Main")
                                     .Blocks.SelectMany(b => b.Instructions)
                                     .ToList();

        var inlinedNew = mainInstructions.OfType<IRNewObject>().SingleOrDefault();
        Assert.That(inlinedNew, Is.Not.Null,
            "guard: FunctionInliningPass did not inline Helper — the clone path was never "
            + "exercised (see the companion test's fixture notes)");
        Assert.That(inlinedNew!.ResolvedNetTarget, Is.EqualTo(ctor),
            "the clone path dropped IRNewObject.ResolvedNetTarget — FIX THE OPTIMIZER");
        Assert.That(inlinedNew.ResolvedNetTargetIsExact, Is.True,
            "the clone path dropped IRNewObject.ResolvedNetTargetIsExact — losing the bit "
            + "makes the name-only gate REFUSE a probe-verified construction");

        var inlinedRead = mainInstructions.OfType<IRFieldAccess>().SingleOrDefault();
        Assert.That(inlinedRead, Is.Not.Null, "guard: the read was not inlined");
        Assert.That(inlinedRead!.ResolvedNetTarget, Is.EqualTo(property),
            "the clone path dropped IRFieldAccess.ResolvedNetTarget — FIX THE OPTIMIZER");
        Assert.That(inlinedRead.ResolvedNetTargetIsExact, Is.True);

        var inlinedWrite = mainInstructions.OfType<IRFieldStore>().SingleOrDefault();
        Assert.That(inlinedWrite, Is.Not.Null, "guard: the write was not inlined");
        Assert.That(inlinedWrite!.ResolvedNetTarget, Is.EqualTo(setter),
            "the clone path dropped IRFieldStore.ResolvedNetTarget (the synthesized set_X "
            + "descriptor) — FIX THE OPTIMIZER");
        Assert.That(inlinedWrite.ResolvedNetTargetIsExact, Is.True);
    }

    // ------------------------------------------------------------------------------------
    // Population on the real IRBuilder path — the non-vacuous half.
    // ------------------------------------------------------------------------------------

    private static IRModule BuildIrFromSource(string body)
    {
        var source = "Module M\n Sub Main()\n" + body + "\n End Sub\nEnd Module";
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        return new IRBuilder(analyzer).Build(ast, "TestModule");
    }

    /// <summary>
    /// The fused-name site (IRBuilder.cs:3342) is the only place the receiver survives as its
    /// own token — after it, everything downstream sees the single string
    /// <c>"Receiver.Member"</c>. This pins that the receiver's spec-C1 category is recorded
    /// there. <c>Guid</c> is one of P1's six NativeOwned types, so a correct stamp is
    /// <c>NativeOwned</c> — and critically NOT the enum's implicit default, which happens to be
    /// <c>NativeOwned</c> too... hence the companion test below, which pins the case where the
    /// default would be WRONG.
    /// </summary>
    [Test]
    public void IrBuilderStampsTheReceiversBoundaryCategoryOnAFusedStaticCall()
    {
        var module = BuildIrFromSource("Dim g As Guid = Guid.NewGuid()");

        var call = module.Functions
                         .SelectMany(f => f.Blocks)
                         .SelectMany(b => b.Instructions)
                         .OfType<IRCall>()
                         .SingleOrDefault(c => c.FunctionName == "Guid.NewGuid");

        Assert.That(call, Is.Not.Null,
            "guard: 'Guid.NewGuid()' no longer lowers to a fused-name IRCall, so this test is no "
            + "longer exercising IRBuilder.cs:3342. FIX THE TEST to target whatever node the "
            + "static-call branch now emits, or FIX IRBuilder if the change was unintended.");
        Assert.That(call!.NetCategory, Is.EqualTo(BoundaryTypeCategory.NativeOwned),
            "IRBuilder did not stamp the receiver's boundary category on a fused static call. FIX "
            + "IRBuilder (BasicLang/IRBuilder.cs:3342), not this test: without the stamp, P2a-2 "
            + "cannot tell a natively-handled call from a shim-routed one.");
    }

    /// <summary>
    /// The inertness half, and the one that would catch the enum-default trap:
    /// <c>BoundaryTypeCategory.NativeOwned</c> is 0, so a bare
    /// <c>{ get; set; }</c> with no initializer would silently mark EVERY call in every program
    /// "natively handled". A user-defined call must come out <c>Unknown</c> with no resolved
    /// target.
    /// </summary>
    [Test]
    public void AnOrdinaryUserCallCarriesNoNetCarriage()
    {
        var source = "Module M\n"
                   + " Function Twice(ByVal n As Integer) As Integer\n"
                   + "  Return n * 2\n"
                   + " End Function\n"
                   + " Sub Main()\n"
                   + "  Dim r As Integer = Twice(21)\n"
                   + " End Sub\n"
                   + "End Module";
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        var module = new IRBuilder(analyzer).Build(ast, "TestModule");

        var call = module.Functions
                         .SelectMany(f => f.Blocks)
                         .SelectMany(b => b.Instructions)
                         .OfType<IRCall>()
                         .SingleOrDefault(c => c.FunctionName == "Twice");

        Assert.That(call, Is.Not.Null, "guard: the fixture must produce a call to Twice");
        Assert.That(call!.NetCategory, Is.EqualTo(BoundaryTypeCategory.Unknown),
            "A plain user-defined call came out of IRBuilder marked as something other than "
            + "Unknown. FIX IRCall (BasicLang/IRNodes.cs): NetCategory must be INITIALIZED to "
            + "BoundaryTypeCategory.Unknown, because the enum's implicit default is NativeOwned "
            + "(value 0) — i.e. 'natively handled', the most dangerous possible wrong answer.");
        Assert.That(call.ResolvedNetTarget, Is.Null,
            "A plain user-defined call acquired a resolved .NET target. FIX whatever populates "
            + "ResolvedNetTarget: in P2a-1 nothing may set it at all.");
    }

    // ====================================================================================
    // P2a-2 Task 2 — carriage on IRInstanceMethodCall / IRBaseMethodCall, and the analyzer
    // annotation table (SemanticAnalyzer.NetResolvedMembers) that populates it for real.
    // ====================================================================================

    /// <summary>
    /// The INSTANCE <c>Regex.IsMatch(String)</c> — the shape `r.IsMatch("x")` resolves to.
    /// </summary>
    private static NetMemberDescriptor RegexInstanceIsMatchDescriptor() =>
        new NetMemberDescriptor(
            "IsMatch",
            "System.Text.RegularExpressions.Regex",
            NetMemberCategory.Method,
            isStatic: false,
            arity: 0,
            typeFullName: "System.Boolean",
            parameters: new List<NetParameterDescriptor>
            {
                new NetParameterDescriptor(NetRefKind.None, "System.String"),
            });

    private static IRInstanceMethodCall FindInstanceCall(IRModule module, string methodName) =>
        module.Functions
              .SelectMany(f => f.Blocks)
              .SelectMany(b => b.Instructions)
              .OfType<IRInstanceMethodCall>()
              .Single(c => c.MethodName == methodName);

    private static IRBaseMethodCall FindBaseCall(IRModule module, string methodName) =>
        module.Functions
              .SelectMany(f => f.Blocks)
              .SelectMany(b => b.Instructions)
              .OfType<IRBaseMethodCall>()
              .Single(c => c.MethodName == methodName);

    /// <summary>
    /// The Task-2 round trip for the two new node types, mirror of
    /// <see cref="OptimizerPreservesTheResolvedNetTargetAndCategoryMarker"/> — and the same
    /// honesty note applies: measured at the Task-2 commit, IROptimizer.cs constructs NEITHER
    /// node type anywhere (zero `new IRInstanceMethodCall` / `new IRBaseMethodCall` sites), so
    /// both survive today by aliasing and this is the regression guard for the day a pass
    /// starts rebuilding them.
    /// </summary>
    [Test]
    public void OptimizerPreservesCarriageOnInstanceAndBaseMethodCalls()
    {
        var module = new IRModule("InstanceCarriageModule");
        var main = new IRFunction("Main", VoidType);
        var entry = new BasicBlock("entry");

        var receiver = new IRVariable("r", new TypeInfo("Regex", TypeKind.Class));
        var instanceCall = new IRInstanceMethodCall("_tmp_inst", receiver, "IsMatch", BoolType);
        instanceCall.Arguments.Add(new IRConstant("x", StringType));
        instanceCall.ResolvedNetTarget = RegexInstanceIsMatchDescriptor();
        instanceCall.NetCategory = BoundaryTypeCategory.ManagedOwned;

        var baseCall = new IRBaseMethodCall("_tmp_base", "IsMatch", BoolType);
        baseCall.Arguments.Add(new IRConstant("x", StringType));
        baseCall.ResolvedNetTarget = RegexInstanceIsMatchDescriptor();
        baseCall.NetCategory = BoundaryTypeCategory.ManagedOwned;

        entry.AddInstruction(instanceCall);
        entry.AddInstruction(baseCall);
        entry.AddInstruction(new IRReturn());
        main.Blocks.Add(entry);
        main.EntryBlock = entry;
        module.Functions.Add(main);

        var expectedInstanceTarget = instanceCall.ResolvedNetTarget;
        var expectedBaseTarget = baseCall.ResolvedNetTarget;

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        var instanceAfter = FindInstanceCall(module, "IsMatch");
        Assert.That(instanceAfter.ResolvedNetTarget, Is.EqualTo(expectedInstanceTarget),
            "The optimizer dropped an instance call's resolved .NET target. FIX THE OPTIMIZER "
            + "(BasicLang/IROptimizer.cs), not this test: every copy/clone path for "
            + "IRInstanceMethodCall must carry ResolvedNetTarget, or P2a-2's lowering silently "
            + "falls back to name-based dispatch (the §8.5 wild-pointer class).");
        Assert.That(instanceAfter.NetCategory, Is.EqualTo(BoundaryTypeCategory.ManagedOwned),
            "The optimizer dropped an instance call's boundary category marker. FIX THE "
            + "OPTIMIZER (BasicLang/IROptimizer.cs), not this test.");

        var baseAfter = FindBaseCall(module, "IsMatch");
        Assert.That(baseAfter.ResolvedNetTarget, Is.EqualTo(expectedBaseTarget),
            "The optimizer dropped a base call's resolved .NET target. FIX THE OPTIMIZER "
            + "(BasicLang/IROptimizer.cs), not this test.");
        Assert.That(baseAfter.NetCategory, Is.EqualTo(BoundaryTypeCategory.ManagedOwned),
            "The optimizer dropped a base call's boundary category marker. FIX THE OPTIMIZER "
            + "(BasicLang/IROptimizer.cs), not this test.");
    }

    /// <summary>
    /// The clone path with teeth, for the two new node types: the instance and base calls sit
    /// inside a small inlineable helper, so <c>FunctionInliningPass.CloneAndRemap</c> processes
    /// every instruction of the helper body. Both node types fall through that switch's
    /// <c>default: return inst;</c> arm — adding a case there that rebuilds either node without
    /// carrying the two fields turns THIS test red (verified by mutation at the Task-2 commit:
    /// a deliberately-dropping <c>case IRInstanceMethodCall</c> made it fail).
    /// </summary>
    [Test]
    public void AggressivePipelinePreservesInstanceAndBaseCallCarriageThroughTheInliningClonePath()
    {
        var target = RegexInstanceIsMatchDescriptor();

        var module = new IRModule("InstanceCarriageInlineModule");

        // Helper() — small enough to inline (<=10 instructions, <=2 blocks, no self-call).
        var helper = new IRFunction("Helper", VoidType);
        var helperEntry = new BasicBlock("entry");

        var receiver = new IRVariable("r", new TypeInfo("Regex", TypeKind.Class));
        var instanceCall = new IRInstanceMethodCall("_tmp_inst", receiver, "IsMatch", BoolType);
        instanceCall.Arguments.Add(new IRConstant("x", StringType));
        instanceCall.ResolvedNetTarget = target;
        instanceCall.NetCategory = BoundaryTypeCategory.ManagedOwned;
        helperEntry.AddInstruction(instanceCall);

        var baseCall = new IRBaseMethodCall("_tmp_base", "IsMatch", BoolType);
        baseCall.ResolvedNetTarget = target;
        baseCall.NetCategory = BoundaryTypeCategory.ManagedOwned;
        helperEntry.AddInstruction(baseCall);

        helperEntry.AddInstruction(new IRReturn());
        helper.Blocks.Add(helperEntry);
        helper.EntryBlock = helperEntry;
        module.Functions.Add(helper);

        // Main() — calls Helper(), giving the inliner a site to rewrite.
        var main = new IRFunction("Main", VoidType);
        var mainEntry = new BasicBlock("entry");
        mainEntry.AddInstruction(new IRCall("_tmp_helper", "Helper", VoidType));
        mainEntry.AddInstruction(new IRReturn());
        main.Blocks.Add(mainEntry);
        main.EntryBlock = mainEntry;
        module.Functions.Add(main);

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        var mainInstructions = module.Functions
                                     .Single(f => f.Name == "Main")
                                     .Blocks.SelectMany(b => b.Instructions)
                                     .ToList();
        Assert.That(mainInstructions.OfType<IRInstanceMethodCall>().Any(c => c.MethodName == "IsMatch"),
            Is.True,
            "guard: FunctionInliningPass did not inline Helper into Main, so CloneAndRemap never "
            + "saw the instance call and this test proves nothing. FIX THE TEST FIXTURE (make "
            + "Helper inlineable again — see FunctionInliningPass.IsInlineable), not the optimizer.");

        var inlinedInstance = mainInstructions.OfType<IRInstanceMethodCall>()
                                              .Single(c => c.MethodName == "IsMatch");
        Assert.That(inlinedInstance.ResolvedNetTarget, Is.EqualTo(target),
            "Inlining rebuilt an instance call and dropped ResolvedNetTarget. FIX THE OPTIMIZER's "
            + "clone path (FunctionInliningPass.CloneAndRemap, BasicLang/IROptimizer.cs) — any "
            + "IRInstanceMethodCall case there must carry ResolvedNetTarget and NetCategory "
            + "across (spec §8.5).");
        Assert.That(inlinedInstance.NetCategory, Is.EqualTo(BoundaryTypeCategory.ManagedOwned),
            "Inlining rebuilt an instance call and dropped NetCategory. FIX THE OPTIMIZER's clone "
            + "path (FunctionInliningPass.CloneAndRemap, BasicLang/IROptimizer.cs).");

        var inlinedBase = mainInstructions.OfType<IRBaseMethodCall>()
                                          .Single(c => c.MethodName == "IsMatch");
        Assert.That(inlinedBase.ResolvedNetTarget, Is.EqualTo(target),
            "Inlining rebuilt a base call and dropped ResolvedNetTarget. FIX THE OPTIMIZER's "
            + "clone path (FunctionInliningPass.CloneAndRemap, BasicLang/IROptimizer.cs).");
        Assert.That(inlinedBase.NetCategory, Is.EqualTo(BoundaryTypeCategory.ManagedOwned),
            "Inlining rebuilt a base call and dropped NetCategory. FIX THE OPTIMIZER's clone "
            + "path (FunctionInliningPass.CloneAndRemap, BasicLang/IROptimizer.cs).");
    }

    // ------------------------------------------------------------------------------------
    // Population on the REAL analyzer+IRBuilder path, with a real resolver over the real
    // framework closure — the non-vacuous half of Task 2.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// ONE resolver for the whole fixture: construction reads ~170 framework assemblies as
    /// Roslyn metadata (~0.6 s cold), which no test should pay twice.
    /// </summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    private static IRModule BuildIrWithResolver(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        return new IRBuilder(analyzer).Build(ast, "TestModule");
    }

    /// <summary>
    /// The plan's Task-2 acceptance shape: `Dim r As New Regex("a")` + `r.IsMatch("x")`. The
    /// analyzer's warning-only probe resolves `Regex` through the ambient namespaces
    /// (System.Text.RegularExpressions), name-matches `IsMatch`, and RECORDS the descriptor in
    /// its annotation table; IRBuilder's instance arm reads it back by node identity. Note
    /// `Regex` is still Rejected/unclaimed pre-flip — recording is severity-independent, so the
    /// annotation is written TODAY, before Task 5 moves the name to ManagedOwned.
    /// </summary>
    [Test]
    public void IrBuilderAttachesTheAnalyzersResolvedMemberToAnInstanceCall()
    {
        var source = "Module M\n"
                   + " Sub Main()\n"
                   + "  Dim r As New Regex(\"a\")\n"
                   + "  Dim ok = r.IsMatch(\"x\")\n"
                   + " End Sub\n"
                   + "End Module";

        var module = BuildIrWithResolver(source);
        var call = FindInstanceCall(module, "IsMatch");

        Assert.That(call.ResolvedNetTarget, Is.Not.Null,
            "The analyzer resolved Regex.IsMatch but the instance-call IR node carries no "
            + "ResolvedNetTarget. FIX the hand-off: SemanticAnalyzer.ProbeNetMemberAccess must "
            + "record into NetResolvedMembers and IRBuilder's instance arm must read it "
            + "(BasicLang/SemanticAnalyzer.cs, BasicLang/IRBuilder.cs).");
        Assert.That(call.ResolvedNetTarget!.DeclaringTypeFullName,
            Is.EqualTo("System.Text.RegularExpressions.Regex"),
            "The carried descriptor names the wrong declaring type.");
        Assert.That(call.ResolvedNetTarget.Name, Is.EqualTo("IsMatch"),
            "The carried descriptor names the wrong member.");

        // Deliberately asserted AGAINST THE REGISTRY, not against a literal category: Task 5
        // moves Regex from Rejected to ManagedOwned, and this pin must churn ZERO lines then.
        Assert.That(BoundaryTypeRegistry.Categorize("Regex"),
            Is.Not.EqualTo(BoundaryTypeCategory.Unknown),
            "guard: Regex must be a registry name, or the category assertion below is vacuous");
        Assert.That(call.NetCategory, Is.EqualTo(BoundaryTypeRegistry.Categorize("Regex")),
            "IRBuilder did not stamp the receiver's boundary category on the annotated instance "
            + "call. FIX IRBuilder's instance arm (BasicLang/IRBuilder.cs), not this test.");
    }

    /// <summary>
    /// The fused static arm's half of the same hand-off: `File.ReadAllText(...)` routes through
    /// IRBuilder's static arm (File is in KnownNetStaticTypes), is unclaimed (no
    /// EmitStdLibCall arm — NetClaimPredicate row (c) is per-call), resolves through the
    /// ambient System.IO, and the produced fused-name IRCall must carry the descriptor. This is
    /// the first production writer IRCall.ResolvedNetTarget has ever had.
    /// </summary>
    [Test]
    public void IrBuilderAttachesTheAnalyzersResolvedMemberToAFusedStaticCall()
    {
        var source = "Module M\n"
                   + " Sub Main()\n"
                   + "  Dim txt = File.ReadAllText(\"x.txt\")\n"
                   + " End Sub\n"
                   + "End Module";

        var module = BuildIrWithResolver(source);
        var call = FindCall(module, "File.ReadAllText");

        Assert.That(call.ResolvedNetTarget, Is.Not.Null,
            "The analyzer resolved File.ReadAllText but the fused static IRCall carries no "
            + "ResolvedNetTarget. FIX the hand-off (SemanticAnalyzer.ProbeNetMemberAccess "
            + "recording / IRBuilder's fused static arm).");
        Assert.That(call.ResolvedNetTarget!.DeclaringTypeFullName, Is.EqualTo("System.IO.File"),
            "The carried descriptor names the wrong declaring type.");
        Assert.That(call.ResolvedNetTarget.Name, Is.EqualTo("ReadAllText"),
            "The carried descriptor names the wrong member.");
    }

    /// <summary>
    /// The inertness half for the two new node types, and the enum-default trap
    /// (<c>NativeOwned == 0</c>): an ordinary user-defined instance call and a MyBase call must
    /// come out <c>Unknown</c> with no resolved target — even with a live resolver factory
    /// configured, because user-defined receivers never resolve from metadata.
    /// </summary>
    [Test]
    public void OrdinaryInstanceAndBaseCallsCarryNoNetCarriage()
    {
        var source = "Class Animal\n"
                   + " Public Sub Speak()\n"
                   + " End Sub\n"
                   + "End Class\n"
                   + "Class Dog\n"
                   + " Inherits Animal\n"
                   + " Public Sub Bark()\n"
                   + "  MyBase.Speak()\n"
                   + " End Sub\n"
                   + "End Class\n"
                   + "Module M\n"
                   + " Sub Main()\n"
                   + "  Dim d As New Dog()\n"
                   + "  d.Bark()\n"
                   + " End Sub\n"
                   + "End Module";

        var module = BuildIrWithResolver(source);

        var instanceCall = FindInstanceCall(module, "Bark");
        Assert.That(instanceCall.NetCategory, Is.EqualTo(BoundaryTypeCategory.Unknown),
            "A plain user-defined instance call came out of IRBuilder marked as something other "
            + "than Unknown. FIX IRInstanceMethodCall (BasicLang/IRNodes.cs): NetCategory must "
            + "be INITIALIZED to BoundaryTypeCategory.Unknown, because the enum's implicit "
            + "default is NativeOwned (value 0) — 'natively handled', the most dangerous "
            + "possible wrong answer.");
        Assert.That(instanceCall.ResolvedNetTarget, Is.Null,
            "A plain user-defined instance call acquired a resolved .NET target. FIX whatever "
            + "populates ResolvedNetTarget: user-defined receivers must never resolve.");

        var baseCall = FindBaseCall(module, "Speak");
        Assert.That(baseCall.NetCategory, Is.EqualTo(BoundaryTypeCategory.Unknown),
            "A MyBase call came out of IRBuilder marked as something other than Unknown. FIX "
            + "IRBaseMethodCall (BasicLang/IRNodes.cs): NetCategory must be INITIALIZED to "
            + "BoundaryTypeCategory.Unknown (NativeOwned is the enum's implicit default).");
        Assert.That(baseCall.ResolvedNetTarget, Is.Null,
            "A MyBase call acquired a resolved .NET target. No production writer exists for "
            + "IRBaseMethodCall.ResolvedNetTarget in Task 2; nothing may set it.");
    }
}
