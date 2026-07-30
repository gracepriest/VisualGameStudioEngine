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
}
