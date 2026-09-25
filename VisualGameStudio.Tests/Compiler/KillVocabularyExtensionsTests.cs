using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  ADR-0006 D1 — PART 2 OF TWO (commit B — steps 2-7, S/adr6-d1/02-step2.patch .. 10-step7.patch),
//  sibling to KillVocabularyTotalityTests.cs (Part 1 — commit A, step 1 alone). Every kill this
//  file pins is an ADDITION D1 makes beyond the pre-existing vocabulary step 1 restructured; see
//  that file's header for why the split matters and which eight kinds live here rather than there.
//
//  Four parts:
//   1. KillVocabularyExtensionArmTests    — hand-built IR, one row per extension arm, at the exact
//      values MEASURED against this working tree (S/adr6-d1/ext/Program.cs, reflection over
//      OptimizationPass.NamesWrittenBy / IsCallVisible / ContainsLambda).
//   2. KillVocabularyPrecisionPinTests    — the three precision claims the fixture brief names by
//      name: the CSE self-exemption, a Const local's privacy surviving the closure rule, and the
//      lambda-scan cache's re-check-on-change behavior.
//   3. KillVocabularyExtensionsExecutionTests / KillVocabularyExtensionsAggressiveLoopExecutionTests
//      — real compiled-and-run programs, four backends, both compiler entry points where the CLI
//      matters. Probes B1r/B1/B2/A2c/A2f/A2m/A1o/A1p/P1/P3/Y1/IN_javascript/IN_cpp/B1L/W1L, verbatim
//      from S/adr6-d1/probes/*.bas, each verified against that probe's own .exp and against
//      S/adr6-d1/probes/matrix-final.txt cell by cell before being pinned here. A2b/A5b/A6/A1(JS)/L5
//      are NOT repeated here — they are covered by the promoted pins in
//      CseDestinationInvalidationTests.cs and LicmKillVocabularyTests.cs, which this same commit
//      touches.
//   4. The CLI / .blproj entry-point leg (inside KillVocabularyExtensionsExecutionTests), matching
//      CseDestinationInvalidationExecutionTests.RunThroughEntryPoint's convention.
// ================================================================================================

/// <summary>
/// Per-kind unit rows for each extension arm ADR-0006 D1 adds beyond step 1's restructuring —
/// the eight kinds whose classification CHANGES between step 1 alone and this working tree
/// (IRBaseMethodCall; IRUnaryOp Inc/Dec; IRForEach; IRInlineCode; IRYield; IRFieldAccess;
/// IRFieldStore; IRAwait), plus the extension RULES that do not correspond to one kind at all
/// (the ByRef-write rule and its converse; Select Case's When-guard call; the closure rule's
/// by-value-parameter half). Every value below is MEASURED against this exact working tree via an
/// isolated <c>dotnet run</c> of the same reflection call the fixture uses
/// (<c>S/adr6-d1/ext/Program.cs</c>) — not asserted from reading the source.
/// </summary>
[TestFixture]
public class KillVocabularyExtensionArmTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    /// <summary>A function with two by-value parameters, no <c>ByRef</c> — for arms the ByRef
    /// aliasing rules do not touch.</summary>
    private static IRFunction PlainFunction(string name = "F")
    {
        var f = new IRFunction(name, IntType);
        var p = new IRVariable("p", IntType) { IsParameter = true };
        var q = new IRVariable("q", IntType) { IsParameter = true };
        f.Parameters.Add(p);
        f.Parameters.Add(q);
        f.CreateBlock("entry");
        return f;
    }

    /// <summary>A function with an EXTRA <c>ByRef</c> parameter <c>n</c> — for the ByRef aliasing
    /// rule (D1 (b)) and its converse, which only compute anything when
    /// <c>OptimizationPass.HasByRefParameter</c> is true.</summary>
    private static IRFunction FunctionWithByRefParameter(string name = "F")
    {
        var f = PlainFunction(name);
        var n = new IRVariable("n", IntType) { IsParameter = true, IsByRef = true };
        f.Parameters.Add(n);
        return f;
    }

    private static WriteSet Classify(IRInstruction inst, IRFunction function) => OptimizationPass.NamesWrittenBy(inst, function);

    // ---- The eight kinds a later step changes, at their FINAL (post-D1) answer ------------------

    [Test]
    public void IRFieldStore_NamesItsMember_AndActsAsACall()
    {
        var f = PlainFunction();
        var store = new IRFieldStore(f.Parameters[0], "K", f.Parameters[1]);
        var w = Classify(store, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Kind, Is.EqualTo(WriteKind.Named));
            Assert.That(w.Names, Is.EquivalentTo(new[] { "K" }));
            Assert.That(w.IsCall, Is.True, "a field store lowers identically for a field and a "
                + "property Setter, which runs user code — every field store must act as a call.");
        });
    }

    [Test]
    public void IRFieldAccess_ActsAsACall()
    {
        var f = PlainFunction();
        var access = new IRFieldAccess("t0", f.Parameters[0], "K", IntType);
        var w = Classify(access, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Names, Is.EquivalentTo(new[] { "t0" }));
            Assert.That(w.IsCall, Is.True, "a field READ lowers identically for a field and a "
                + "property Getter, which can run user code (and write anything a call could).");
        });
    }

    [Test]
    public void IRBaseMethodCall_ActsAsACall_AndNamesItsVariableArguments()
    {
        var f = PlainFunction();
        var call = new IRBaseMethodCall("t0", "M", IntType);
        call.Arguments.Add(f.Parameters[0]); // a variable argument
        call.Arguments.Add(new IRConstant(1, IntType)); // not a variable — not named
        var w = Classify(call, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.IsCall, Is.True, "MyBase.Method() is a call like any other (ADR-0006 D1).");
            Assert.That(w.Names, Is.EquivalentTo(new[] { "t0", "p" }),
                "the node carries no ByRef flags, so every VARIABLE argument is treated as "
                + "written (VB may pass it by reference to a Sub New(ByRef n)); the constant "
                + "argument is not a variable and is not named.");
        });
    }

    [Test]
    public void IRForEach_NamesItsLoopVariable_AndActsAsACall()
    {
        var f = PlainFunction();
        var body = f.CreateBlock("body");
        var end = f.CreateBlock("end");
        var forEach = new IRForEach("item", IntType, f.Parameters[0], body, end);
        var w = Classify(forEach, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Names, Is.EquivalentTo(new[] { "item" }));
            Assert.That(w.IsCall, Is.True, "each iteration runs the enumerator (an Iterator "
                + "function's body, or a user collection's MoveNext/Current) — a call.");
        });
    }

    [Test]
    public void IRInlineCode_IsUniversal_ADeliberateClassificationNotAGap()
    {
        var f = PlainFunction();
        var w = Classify(new IRInlineCode("csharp", "// x"), f);

        Assert.Multiple(() =>
        {
            Assert.That(w.IsUniversal, Is.True, "raw target-language text can assign ANY variable.");
            Assert.That(w.IsClassified, Is.True,
                "Universal here is a DECISION (an explicit arm), not an unclassified gap — "
                + "Invariant V must NOT fire on it, unlike a truly foreign kind.");
        });
    }

    [Test]
    public void IRAwait_ActsAsACall()
    {
        var f = PlainFunction();
        var w = Classify(new IRAwait("t0", f.Parameters[0], IntType), f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Names, Is.EquivalentTo(new[] { "t0" }));
            Assert.That(w.IsCall, Is.True, "control leaves the function at Await and other code "
                + "runs before it resumes — a suspension point is a call.");
        });
    }

    [Test]
    public void IRYield_ActsAsACall()
    {
        var f = PlainFunction();
        var w = Classify(new IRYield(f.Parameters[0]), f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Kind, Is.EqualTo(WriteKind.None), "Yield names no variable itself.");
            Assert.That(w.IsCall, Is.True, "control leaves the function at Yield and the "
                + "iterator's consumer runs before it resumes — a suspension point is a call.");
        });
    }

    [Test]
    public void IRUnaryOp_IncDec_AlsoNamesItsOperand()
    {
        var f = PlainFunction();
        var w = Classify(new IRUnaryOp("t0", UnaryOpKind.Inc, f.Parameters[0], IntType), f);

        Assert.That(w.Names, Is.EquivalentTo(new[] { "t0", "p" }),
            "++x/--x writes its RESULT (t0, how C# emits `t = ++x`) as well as its OPERAND (p) — "
            + "contrast IRUnaryOp with any other operator, which is a pure definition (see "
            + "KillVocabularyPerKindStepOneTests.TodaysExactAnswer(\"IRUnaryOp\") for UnaryOpKind.Neg).");
    }

    // ---- Extension rules that are not a single node kind ------------------------------------------

    [Test]
    public void SelectCase_BindingVariable_NamesIt_NoWhenGuard_IsNotACall()
    {
        var f = PlainFunction();
        var target = f.CreateBlock("case1");
        var end = f.CreateBlock("default");
        var pattern = new IRBindingPatternCase(target) { BindingVariable = "item" };
        var sw = new IRSwitch(f.Parameters[0], end);
        sw.PatternCases.Add(pattern);
        var w = Classify(sw, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Names, Is.EquivalentTo(new[] { "item" }));
            Assert.That(w.IsCall, Is.False, "no When guard — the switch evaluates no user code.");
        });
    }

    [Test]
    public void SelectCase_WithAWhenGuard_IsACall()
    {
        var f = PlainFunction();
        var target = f.CreateBlock("case1");
        var end = f.CreateBlock("default");
        var guard = new IRCall("", "Bump", IntType);
        var pattern = new IRBindingPatternCase(target) { BindingVariable = "item", WhenGuard = guard };
        var sw = new IRSwitch(f.Parameters[0], end);
        sw.PatternCases.Add(pattern);
        var w = Classify(sw, f);

        Assert.That(w.IsCall, Is.True,
            "IRBuilder lowers a When guard with instruction emission suppressed — the guard's tree "
            + "sits in no block, so only the switch arm itself can classify it as running user code.");
    }

    [Test]
    public void TryCatch_NamesEachCatchVariable_IsNotACall()
    {
        var f = PlainFunction();
        var tryBlock = f.CreateBlock("try");
        var catchBlock = f.CreateBlock("catch");
        var end = f.CreateBlock("end");
        var clause = new IRCatchClause(null, "ex", catchBlock);
        var tryCatch = new IRTryCatch(tryBlock, new List<IRCatchClause> { clause }, null, end);
        var w = Classify(tryCatch, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Names, Is.EquivalentTo(new[] { "ex" }));
            Assert.That(w.IsCall, Is.False,
                "a Catch clause binds its variable when it catches; it does not itself run a call "
                + "— the try/catch/finally BODIES are separate blocks, classified on their own.");
        });
    }

    [Test]
    public void AllocaAddressedStore_WithAnAddrSuffix_AlsoNamesTheStrippedLocal()
    {
        var f = PlainFunction();
        var alloca = new IRAlloca("V_addr", IntType, 4);
        var store = new IRStore(new IRConstant(5, IntType), alloca);
        var w = Classify(store, f);

        Assert.That(w.Names, Is.EquivalentTo(new[] { "V_addr", "V" }),
            "an alloca is the backing slot of a local (V_addr for an array local V) — C# and C++ "
            + "write the local V itself, MSIL the slot; both names are recorded.");
    }

    [TestCase(typeof(IRArrayStore))]
    [TestCase(typeof(IRIndexerStore))]
    public void ElementStore_Escapes_ToAByRefParameter(System.Type kind)
    {
        var f = FunctionWithByRefParameter();
        var arr = new IRVariable("arr", IntType);
        IRInstruction store = kind == typeof(IRArrayStore)
            ? new IRArrayStore(arr, new IRConstant(0, IntType), f.Parameters[0])
            : new IRIndexerStore(arr, f.Parameters[0]);
        var w = Classify(store, f);

        Assert.That(w.Names, Is.EquivalentTo(new[] { "n" }),
            $"{kind.Name} writes storage no variable names (an array/collection slot), which a "
            + "ByRef parameter (n) may alias — the caller could have passed arr(0) as the ByRef "
            + "argument.");
    }

    [Test]
    public void ElementStore_WithNoByRefParameter_NamesNothing()
    {
        var f = PlainFunction(); // no ByRef parameter at all
        var arr = new IRVariable("arr", IntType);
        var w = Classify(new IRArrayStore(arr, new IRConstant(0, IntType), f.Parameters[0]), f);

        Assert.That(w.Kind, Is.EqualTo(WriteKind.None),
            "with no ByRef parameter to alias, an element store names nothing — this is what keeps "
            + "KillVocabularyPerKindStepOneTests.TodaysExactAnswer(\"IRArrayStore\") stable.");
    }

    [Test]
    public void Constructor_NamesItsVariableArguments_NotItsConstantOnes()
    {
        var f = PlainFunction();
        var newObject = new IRNewObject("t0", "Box", IntType);
        newObject.Arguments.Add(f.Parameters[0]); // a variable
        newObject.Arguments.Add(new IRConstant(1, IntType)); // not a variable
        var w = Classify(newObject, f);

        Assert.That(w.Names, Is.EquivalentTo(new[] { "t0", "p" }),
            "the node records no ByRef flags, so VB's pass-by-reference-to-Sub-New treats every "
            + "VARIABLE argument as written; the constant argument is not a variable.");
    }

    [Test]
    public void ByRefWrite_ActsAsACall()
    {
        var f = FunctionWithByRefParameter();
        var n = f.Parameters[2];
        var binop = new IRBinaryOp("n", BinaryOpKind.Add, f.Parameters[0], f.Parameters[1], IntType) { NamedAfterVariable = true };
        var w = Classify(binop, f);

        Assert.Multiple(() =>
        {
            Assert.That(w.Names, Is.EquivalentTo(new[] { "n" }));
            Assert.That(w.IsCall, Is.True,
                "a write through a ByRef parameter (n = p + q, ByRef n) may write any storage its "
                + "caller could pass — the same set a call can write — so it kills as a call does.");
        });
    }

    [Test]
    public void TheConverse_AWriteToEscapingStorage_NamesEveryByRefParameter()
    {
        var f = FunctionWithByRefParameter();
        var g = new IRVariable("G", IntType) { IsGlobal = true }; // escaping, non-const
        var assignment = new IRAssignment(g, new IRConstant(0, IntType));
        var w = Classify(assignment, f);

        Assert.That(w.Names, Is.EquivalentTo(new[] { "G", "n" }),
            "G = 0 writes storage a ByRef parameter (n) may alias — the caller could have called "
            + "Work(G) — so the write also names every ByRef parameter (implementer's completion "
            + "of D1 (b), ADR-0006's implementation note).");
    }

    [Test]
    public void ClosureRule_MakesAByValueParameter_CallVisible_WhenTheFunctionContainsALambda()
    {
        var f = PlainFunction();
        var lambdaRef = new IRVariable("__lambda_0", IntType); // IRBuilder's own lambda-reference spelling
        var use = new IRCall("", "Show", IntType);
        use.Arguments.Add(lambdaRef);
        f.Blocks[0].AddInstruction(use);

        Assert.That(OptimizationPass.IsCallVisible("p", f), Is.True,
            "a by-value parameter is a local of the frame too — a lambda may capture it by "
            + "reference the same way it captures a Dim'd local.");
    }

    [Test]
    public void ClosureRule_LeavesAByValueParameter_Private_WhenTheFunctionHasNoLambda()
    {
        var f = PlainFunction(); // no lambda reference anywhere
        Assert.That(OptimizationPass.IsCallVisible("p", f), Is.False);
    }
}

/// <summary>
/// The three precision claims the fixture brief names explicitly, each isolated from the others.
/// </summary>
[TestFixture]
public class KillVocabularyPrecisionPinTests
{
    /// <summary>
    /// <c>n = p + q : l(0) = p + q</c>, <c>n</c> a <c>ByRef</c> parameter — CSE must still make
    /// ONE merge. Without the self-exemption, the defining instruction's own rename to the ByRef
    /// name <c>n</c> would act as a call (D1 (b)) and immediately invalidate the very record it
    /// just created, so NOTHING would ever merge whenever the shared destination happens to be a
    /// ByRef parameter — <c>Invalidate</c>'s <c>self</c> check (skip the record for the
    /// instruction that just DEFINED it, exactly as it already does for the call arm) is what
    /// keeps this legal merge alive.
    /// </summary>
    [Test]
    public void SelfExemption_NByRef_StillMakesOneMerge()
    {
        const string source = """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed")
                Return v
            End Function

            Sub Work(ByRef n As Integer, p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                n = p + q
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(n))
            End Sub

            Sub Main()
                Dim v As Integer = 5
                Work(v, Seed(1), Seed(2))
            End Sub
            """;
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var pass = new CommonSubexpressionEliminationPass();
        pass.Run(module);

        Assert.That(pass.ModificationCount, Is.EqualTo(1),
            "n = p + q (n ByRef) : l(0) = p + q must still merge into one binop — the ByRef-write "
            + "rule must not invalidate the record the SAME instruction is defining.");
    }

    /// <summary>A <c>Const</c> local stays PRIVATE even in a function that creates a lambda — the
    /// closure rule's own carve-out ("nothing can write a Const"), unaffected by whether the
    /// function contains a lambda at all.</summary>
    [Test]
    public void ConstLocal_StaysPrivate_InAFunctionContainingALambda()
    {
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var function = new IRFunction("Work", intType);
        var k = new IRVariable("K", intType) { IsConst = true };
        function.LocalVariables.Add(k);
        function.CreateBlock("entry");
        var lambdaRef = new IRVariable("__lambda_0", intType);
        var use = new IRCall("", "Show", intType);
        use.Arguments.Add(lambdaRef);
        function.Blocks[0].AddInstruction(use);

        Assert.That(OptimizationPass.IsCallVisible("K", function), Is.False,
            "a Const is exempt regardless of the closure rule — nothing, lambda or otherwise, "
            + "can write it.");
    }

    /// <summary>
    /// <c>ContainsLambda</c>'s cache: a "no" is kept only while the function's instruction count
    /// is unchanged, and is RE-WALKED when it changes — even when the new instruction is not
    /// itself a lambda reference. A "yes" is never revisited.
    /// </summary>
    [Test]
    public void LambdaScanCache_ReChecksANo_WhenTheInstructionCountChanges()
    {
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var function = new IRFunction("F", intType);
        var block = function.CreateBlock("entry");
        block.AddInstruction(new IRCall("", "Show1", intType));

        Assert.That(OptimizationPass.ContainsLambda(function), Is.False,
            "one instruction, no lambda reference — cached as 'no' at count 1.");

        // A second, unrelated instruction: the count changed, so the cached "no" must be
        // RE-WALKED (still correctly "no" — this only proves the re-walk happens, not yet that it
        // can flip).
        block.AddInstruction(new IRCall("", "Show2", intType));
        Assert.That(OptimizationPass.ContainsLambda(function), Is.False,
            "count changed 1 -> 2, re-walked, still no lambda reference.");

        // Now a THIRD instruction that DOES reference a lambda: the count changed again, so the
        // stale "no" must not be trusted — this is the flip the cache exists to still catch.
        var lambdaRef = new IRVariable("__lambda_0", intType);
        var use = new IRCall("", "Show3", intType);
        use.Arguments.Add(lambdaRef);
        block.AddInstruction(use);
        Assert.That(OptimizationPass.ContainsLambda(function), Is.True,
            "count changed 2 -> 3, re-walked, and this time a lambda reference is there — a stale "
            + "'no' cached at count 1 or 2 must not have suppressed this re-walk.");
    }
}

// ================================================================================================
//  EXECUTION — real compiled-and-run programs, verifying the extension arms above through actual
//  backend output, not only through NamesWrittenBy's return value. Every source below is verbatim
//  from S/adr6-d1/probes/<name>.bas, and every expected string is that probe's own .exp, itself
//  cross-checked against S/adr6-d1/probes/matrix-final.txt cell by cell (both the "cli" and
//  "cli-O" columns, standard and aggressive, agree for every probe in this file).
// ================================================================================================

internal static class KillVocabularyExtensionsProbes
{
    /// <summary>MyBase.Bump() (returns a value) writes a field the caller reads bare, across a
    /// closure-rule shape too (the receiver's own instance). Exercises IRBaseMethodCall as a call
    /// naming a value, AND IRFieldStore naming its member.</summary>
    internal const string B1r = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Function Bump() As Integer
                K = K + 10
                Return K
            End Function
        End Class

        Class Box
            Inherits BaseBox

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Dim r As Integer = MyBase.Bump()
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(r))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string B1rExpected = "seed\nseed\n13,3,11";

    /// <summary>MyBase.Bump() as a plain Sub (statement call, no result read) — the shape whose C#
    /// leg drops the call entirely (task #139, unrelated to ADR-0006 D1).</summary>
    internal const string B1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Sub Bump()
                K = K + 10
            End Sub
        End Class

        Class Box
            Inherits BaseBox

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                MyBase.Bump()
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string B1Expected = "seed\nseed\n13,3";
    internal const string B1CSharpKnownWrong = "seed\nseed\n3,3";

    /// <summary>MyBase.SetIt(p) passes p BY REFERENCE — IRBaseMethodCall's variable-argument arm.</summary>
    internal const string B2 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Sub SetIt(ByRef n As Integer)
                n = 100
            End Sub
        End Class

        Class Box
            Inherits BaseBox

            Sub Work(p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                Dim a As Integer = p + q
                MyBase.SetIt(p)
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(1), Seed(2))
        End Sub
        """;

    internal const string B2Expected = "seed\nseed\n102,3";

    /// <summary>A2c — the converse (D1 (b)'s completion) seen through a MODULE GLOBAL: a write
    /// through the ByRef parameter n aliases G, but the shape here is a write TO G that must be
    /// seen as aliasing n.</summary>
    internal const string A2c = """
        Dim G As Integer

        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Work(ByRef n As Integer, p As Integer, q As Integer)
            Dim l As New List(Of Integer)()
            l.Add(0)
            n = p + q
            G = 0
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(n))
        End Sub

        Sub Main()
            G = 5
            Work(G, Seed(1), Seed(2))
        End Sub
        """;

    /// <summary>A2f — the same converse through a bare class field K.</summary>
    internal const string A2f = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Work(ByRef n As Integer, p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                n = p + q
                K = 0
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(n))
            End Sub

            Sub Go(p As Integer, q As Integer)
                K = 5
                Work(K, p, q)
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Go(Seed(1), Seed(2))
        End Sub
        """;

    /// <summary>A2m — the same converse through an EXPLICIT Me.K write.</summary>
    internal const string A2m = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Work(ByRef n As Integer, p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                n = p + q
                Me.K = 0
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(n))
            End Sub

            Sub Go(p As Integer, q As Integer)
                K = 5
                Work(K, p, q)
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Go(Seed(1), Seed(2))
        End Sub
        """;

    internal const string A2Expected = "seed\nseed\n3,0";

    /// <summary>A1o — the closure rule's by-value-parameter half, at MODULE (Main) scope: p is a
    /// Dim'd local, not a parameter, closer to A1's own shape but through a DIFFERENT lambda body.</summary>
    internal const string A1o = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim bump = Sub() p = p + 100
            Dim a As Integer = p + q
            bump()
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    /// <summary>A1p — the SAME shape as A1o, but p is a genuine BY-VALUE PARAMETER of an enclosing
    /// Sub — the exact shape MEASURED in D1's own doc comment (Work(p, q), bump = Sub() p = p+100).</summary>
    internal const string A1p = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Work(p As Integer, q As Integer)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim bump = Sub() p = p + 100
            Dim a As Integer = p + q
            bump()
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub

        Sub Main()
            Work(Seed(1), Seed(2))
        End Sub
        """;

    internal const string A1oExpected = "seed\nseed\n103,3";
    internal const string A1pExpected = "seed\nseed\n103,3";

    /// <summary>P1 — a property SETTER (Me.P = 10) writes the backing field K a bare read shares.</summary>
    internal const string P1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Me.P = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P1Expected = "seed\nseed\n12,3";

    /// <summary>P3 — a property GETTER (Me.Tick) that itself bumps K, so the mere READ must act as
    /// a call.</summary>
    internal const string P3 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            ReadOnly Property Tick As Integer
                Get
                    K = K + 10
                    Return K
                End Get
            End Property

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Dim t As Integer = Me.Tick
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a) & "," & CStr(t))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    internal const string P3Expected = "seed\nseed\n13,3,11";

    /// <summary>Y1 — an Iterator's Yield is a suspension point: the consumer bumps K between the
    /// two Yields, so the SECOND yielded value must recompute.</summary>
    internal const string Y1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Iterator Function Gen() As IEnumerable(Of Integer)
                Dim a As Integer = K + 1
                Yield a
                Yield K + 1
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            b.K = Seed(1)
            For Each x In b.Gen()
                Console.WriteLine(CStr(x))
                b.K = b.K + 10
            Next
        End Sub
        """;

    internal const string Y1Expected = "seed\n2\n12";

    /// <summary>IN_javascript — a javascript{ } inline-code block writes p; IRInlineCode's
    /// Universal classification must clear CSE's record for the shared p + q.</summary>
    internal const string InJavascript = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            javascript{ p = 100; }
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    /// <summary>IN_cpp — the same shape, a cpp{ } inline-code block.</summary>
    internal const string InCpp = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            cpp{ p = 100; }
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    internal const string InlineExpected = "seed\nseed\n102,3";

    /// <summary>B1L — LICM's own IRBaseMethodCall probe: MyBase.Bump() inside a loop must block
    /// hoisting K * 2. C#'s statement-call drop (task #139) applies here too, wrong FOR THAT
    /// reason, independent of LICM.</summary>
    internal const string B1L = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Sub Bump()
                K = K + 1
            End Sub
        End Class

        Class Box
            Inherits BaseBox

            Sub Work()
                K = Seed(1)
                Dim s As Integer = 0
                For i As Integer = 1 To 3
                    s = s + K * 2
                    MyBase.Bump()
                Next
                Console.WriteLine(CStr(s))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string B1LExpected = "seed\n12";
    internal const string B1LCSharpKnownWrong = "seed\n6";

    /// <summary>W1L — LICM's own Select-Case-With-When probe: the When guard's call (Bump, which
    /// bumps K) inside a loop must block hoisting K * 2.</summary>
    internal const string W1L = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Function Bump() As Boolean
                K = K + 1
                Return True
            End Function

            Sub Work()
                K = Seed(1)
                Dim s As Integer = 0
                For i As Integer = 1 To 3
                    s = s + K * 2
                    Select Case i
                        Case Is > 0 When Bump()
                            s = s + 0
                    End Select
                Next
                Console.WriteLine(CStr(s))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string W1LExpected = "seed\n12";
}

/// <summary>
/// B1r/B1/B2/A2c/A2f/A2m/A1o/A1p/P1/P3/Y1/IN_javascript/IN_cpp — every probe run through the
/// backend(s) that actually build it (a backend excluded for a structural, unrelated reason is
/// never asserted "wrong", matching this suite's convention throughout the D1 family).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class KillVocabularyExtensionsExecutionTests
{
    // ---- B1r: IRBaseMethodCall (function) + IRFieldStore, all four backends -----------------------

    [Test]
    public void B1r_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(KillVocabularyExtensionsProbes.B1r, KillVocabularyExtensionsProbes.B1rExpected);

    [Test]
    public void B1r_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(KillVocabularyExtensionsProbes.B1r, KillVocabularyExtensionsProbes.B1rExpected);

    // ---- B1: IRBaseMethodCall (Sub) — C++/JS/MSIL correct; C# is task #139, UNRELATED to D1 -------

    [Test]
    public void B1_StandardPipeline_CppJsMsilCorrect_CSharpTask139()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(KillVocabularyExtensionsProbes.B1))),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1Expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(KillVocabularyExtensionsProbes.B1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1Expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(KillVocabularyExtensionsProbes.B1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1Expected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(KillVocabularyExtensionsProbes.B1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1CSharpKnownWrong),
                "C# — task #139 (the backend DROPS a statement-level MyBase call entirely), "
                + "UNRELATED to ADR-0006 D1; if this changed, re-measure before touching it.");
        });

    [Test]
    public void B1_AggressivePipeline_CppJsMsilCorrect_CSharpTask139()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(KillVocabularyExtensionsProbes.B1))),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1Expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.B1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1Expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(KillVocabularyExtensionsProbes.B1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1Expected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(KillVocabularyExtensionsProbes.B1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1CSharpKnownWrong), "C# — task #139, same as standard pipeline");
        });

    // ---- B2: IRBaseMethodCall's variable-argument (ByRef) arm. C++ ONLY, per the fixture brief's
    //      own scope: JavaScript structurally refuses ByRef (BL7002); MSIL fails for an UNRELATED,
    //      pre-existing gap — a virtual MyBase call to an inherited method RUN-FAILS with
    //      "Method not found: Void BaseBox.SetIt(Int32)" (MEASURED, matrix-final.txt), nothing to
    //      do with the kill vocabulary; C# is task #139 (the SAME statement-call drop B1/B1L pin),
    //      also unrelated to D1. None of the three is asserted — matching this suite's convention
    //      of never asserting "wrong" against a leg for a reason this family does not cover.

    [Test]
    public void B2_Cpp_StandardAndAggressivePipeline()
    {
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(KillVocabularyExtensionsProbes.B2))),
            Is.EqualTo(KillVocabularyExtensionsProbes.B2Expected), "standard pipeline");
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(KillVocabularyExtensionsProbes.B2))),
            Is.EqualTo(KillVocabularyExtensionsProbes.B2Expected), "aggressive pipeline");
    }

    [Test]
    public void B2_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(KillVocabularyExtensionsProbes.B2);
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    // ---- A2c/A2f/A2m: the converse (D1 (b)'s completion), through a global/field/Me. field ---------
    //      JavaScript structurally refuses ByRef (BL7002) and is excluded; C# is not pinned
    //      (vacuous, as everywhere in this family — inline-always re-emits the expression as text).

    [TestCase(nameof(KillVocabularyExtensionsProbes.A2c), TestName = "A2c_GlobalConverse")]
    [TestCase(nameof(KillVocabularyExtensionsProbes.A2f), TestName = "A2f_BareFieldConverse")]
    [TestCase(nameof(KillVocabularyExtensionsProbes.A2m), TestName = "A2m_MeFieldConverse")]
    public void TheConverse_StandardPipeline_CppAndMsilAgree(string probeName)
    {
        var source = ProbeSource(probeName);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))),
                Is.EqualTo(KillVocabularyExtensionsProbes.A2Expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)),
                Is.EqualTo(KillVocabularyExtensionsProbes.A2Expected), "MSIL");
        });
    }

    [TestCase(nameof(KillVocabularyExtensionsProbes.A2c), TestName = "A2c_GlobalConverse")]
    [TestCase(nameof(KillVocabularyExtensionsProbes.A2f), TestName = "A2f_BareFieldConverse")]
    [TestCase(nameof(KillVocabularyExtensionsProbes.A2m), TestName = "A2m_MeFieldConverse")]
    public void TheConverse_AggressivePipeline_CppAndMsilAgree(string probeName)
    {
        var source = ProbeSource(probeName);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(source))),
                Is.EqualTo(KillVocabularyExtensionsProbes.A2Expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)),
                Is.EqualTo(KillVocabularyExtensionsProbes.A2Expected), "MSIL");
        });
    }

    // ---- A1o/A1p: the closure rule's by-value-parameter half, JavaScript (C++/MSIL fail to build
    //      the lambda shape at all, for unrelated pre-existing reasons — matching A1's own family) --

    [Test]
    public void A1o_JavaScript_StandardAndAggressive()
    {
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(KillVocabularyExtensionsProbes.A1o)),
            Is.EqualTo(KillVocabularyExtensionsProbes.A1oExpected), "standard pipeline");
        Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.A1o)),
            Is.EqualTo(KillVocabularyExtensionsProbes.A1oExpected), "aggressive pipeline");
    }

    [Test]
    public void A1p_JavaScript_StandardAndAggressive_TheByValueParameterShapeD1MeasuresByName()
    {
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(KillVocabularyExtensionsProbes.A1p)),
            Is.EqualTo(KillVocabularyExtensionsProbes.A1pExpected), "standard pipeline");
        Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.A1p)),
            Is.EqualTo(KillVocabularyExtensionsProbes.A1pExpected), "aggressive pipeline");
    }

    // ---- P1/P3: IRFieldAccess/IRFieldStore as calls, through a real Property — JS/MSIL ---------------
    //      (C++ cannot build a BasicLang Property at all — pre-existing, unrelated gap)

    [Test]
    public void P1_PropertySetter_JavaScriptAndMsil_StandardAndAggressive()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(KillVocabularyExtensionsProbes.P1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P1Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.P1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P1Expected), "JavaScript, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(KillVocabularyExtensionsProbes.P1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P1Expected), "MSIL, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(KillVocabularyExtensionsProbes.P1)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P1Expected), "MSIL, aggressive");
        });

    [Test]
    public void P3_PropertyGetter_JavaScriptAndMsil_StandardAndAggressive()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(KillVocabularyExtensionsProbes.P3)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P3Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.P3)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P3Expected), "JavaScript, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(KillVocabularyExtensionsProbes.P3)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P3Expected), "MSIL, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(KillVocabularyExtensionsProbes.P3)),
                Is.EqualTo(KillVocabularyExtensionsProbes.P3Expected), "MSIL, aggressive");
        });

    // ---- Y1: IRYield as a call, C++ (JavaScript/MSIL fail to build the Iterator, unrelated gaps) ----

    [Test]
    public void Y1_Iterator_Cpp_StandardAndAggressive()
    {
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(KillVocabularyExtensionsProbes.Y1))),
            Is.EqualTo(KillVocabularyExtensionsProbes.Y1Expected), "standard pipeline");
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(KillVocabularyExtensionsProbes.Y1))),
            Is.EqualTo(KillVocabularyExtensionsProbes.Y1Expected), "aggressive pipeline");
    }

    // ---- IN_javascript / IN_cpp: IRInlineCode's Universal classification, the ONE backend each -----
    //      inline-code block can even target (every other backend refuses it outright, structurally)

    [Test]
    public void InJavascript_JavaScript_StandardAndAggressive()
    {
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(KillVocabularyExtensionsProbes.InJavascript)),
            Is.EqualTo(KillVocabularyExtensionsProbes.InlineExpected), "standard pipeline");
        Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.InJavascript)),
            Is.EqualTo(KillVocabularyExtensionsProbes.InlineExpected), "aggressive pipeline");
    }

    [Test]
    public void InCpp_Cpp_StandardAndAggressive()
    {
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(KillVocabularyExtensionsProbes.InCpp))),
            Is.EqualTo(KillVocabularyExtensionsProbes.InlineExpected), "standard pipeline");
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(KillVocabularyExtensionsProbes.InCpp))),
            Is.EqualTo(KillVocabularyExtensionsProbes.InlineExpected), "aggressive pipeline");
    }

    // ---- One CLI / .blproj entry-point leg (B1r, rendered through C#), matching
    //      CseDestinationInvalidationExecutionTests.RunThroughEntryPoint's convention -----------------

    [Test]
    public void B1r_TheCliSingleFileEntryPoint()
        => Assert.That(RunThroughEntryPoint(KillVocabularyExtensionsProbes.B1r, asProject: false),
            Is.EqualTo(KillVocabularyExtensionsProbes.B1rExpected),
            "BasicCompiler.CompileFile printed the wrong answer.");

    [Test]
    public void B1r_TheProjectBuildEntryPoint()
        => Assert.That(RunThroughEntryPoint(KillVocabularyExtensionsProbes.B1r, asProject: true),
            Is.EqualTo(KillVocabularyExtensionsProbes.B1rExpected),
            "BasicCompiler.CompileProjectFiles printed the wrong answer.");

    private static string ProbeSource(string probeName) => probeName switch
    {
        nameof(KillVocabularyExtensionsProbes.A2c) => KillVocabularyExtensionsProbes.A2c,
        nameof(KillVocabularyExtensionsProbes.A2f) => KillVocabularyExtensionsProbes.A2f,
        nameof(KillVocabularyExtensionsProbes.A2m) => KillVocabularyExtensionsProbes.A2m,
        _ => throw new System.ArgumentOutOfRangeException(nameof(probeName)),
    };

    private static string RunThroughEntryPoint(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_KillVocabExtEntry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}

/// <summary>
/// B1L/W1L — LICM's own extension-arm probes, aggressive pipeline only (LICM is aggressive-only;
/// see LicmKillVocabularyTests.cs's own header for why). L5 is NOT repeated here — it is already
/// promoted (JavaScript) / re-attributed (C++, task #140) in LicmKillVocabularyKnownGapsTask122Tests.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class KillVocabularyExtensionsAggressiveLoopExecutionTests
{
    /// <summary>B1L — IRBaseMethodCall inside a loop blocks LICM's hoist. C++/JavaScript/MSIL
    /// correct; C# is task #139 (the SAME statement-call drop B1 pins), unrelated to LICM.</summary>
    [Test]
    public void B1L_CppJsMsilCorrect_CSharpTask139()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(KillVocabularyExtensionsProbes.B1L))),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1LExpected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.B1L)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1LExpected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(KillVocabularyExtensionsProbes.B1L)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1LExpected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(KillVocabularyExtensionsProbes.B1L)),
                Is.EqualTo(KillVocabularyExtensionsProbes.B1LCSharpKnownWrong),
                "C# — task #139 (the backend drops the statement-level MyBase.Bump() call "
                + "entirely), the SAME defect B1 pins, UNRELATED to LICM or ADR-0006 D1.");
        });

    /// <summary>W1L — a Select Case When guard's call inside a loop blocks LICM's hoist. C++
    /// and MSIL cannot build this shape at all (pre-existing, unrelated backend gaps: C++ "use of
    /// undeclared identifier 't5'"; MSIL "the 'When' guard node IRCall has no IL lowering") and are
    /// excluded, matching this suite's convention of never asserting against a leg that cannot
    /// even run the program.</summary>
    [Test]
    public void W1L_CSharpAndJavaScript_Correct()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(KillVocabularyExtensionsProbes.W1L)),
                Is.EqualTo(KillVocabularyExtensionsProbes.W1LExpected), "C#");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(KillVocabularyExtensionsProbes.W1L)),
                Is.EqualTo(KillVocabularyExtensionsProbes.W1LExpected), "JavaScript");
        });
}
