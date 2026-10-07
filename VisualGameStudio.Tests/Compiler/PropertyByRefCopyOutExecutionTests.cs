using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #209, RUN. When VB passes a PROPERTY to a ByRef parameter it does not pass storage (a property is an accessor call): it reads the property into a temporary, passes the temporary by
//  reference, and after the call writes the temporary back through the setter. Get, then the call, then Set. A ReadOnly property is read and passed the same way, is NOT written back, and gets no
//  diagnostic. BasicLang passed the property's VALUE as if it were storage, and each backend did something different: C++ dropped the callee's write (L7 printed 10 where VB prints 22, a silent wrong
//  answer), C# wrote `ref b.P` (CS0206), MSIL refused the program, and C++ passed a plain auto-property's FIELD (right at the end, but the callee saw its own writes through the object mid-call).
//  The fix is in `IRBuilder` (`CopyOutPropertyArgument` / `CompletePropertyCopyOuts` and the pins) plus ONE analyzer predicate, `SemanticAnalyzer.IsCopyOutPropertyArgument`: the getter's value goes into a
//  `__copyout{n}` carrier, the carrier is passed in the property's place, and the writable carriers are stored back RIGHT TO LEFT right after the call. No backend changed: a carrier is an ordinary local.
//  ⛔ ONE EXCEPTION, `MyBase.New(...)`: a write-back cannot run inside a base-constructor call (the prologue is expression-only and CLOSED, ADR-0016 D5(c); C# has nowhere to put a statement before
//  `: base(...)`). So a WRITABLE ACCESSOR-BACKED property (Get/Set block, Overridable / Overrides, an interface's) passed ByRef anywhere in a `MyBase.New` argument list, direct or nested in a call, is
//  refused with BL4004 on every backend; a PLAIN auto-property there lowers exactly as on master (no carrier; C++ passes the backing field and prints VB's answer); a ReadOnly one is copied in, never back.
//
//  ORACLE: vbc. Every probe below is `S/t209/probes*/*.bas` verbatim (but C11m, the bare-field control, written for this fixture), wrapped in a VB Module and run with vbc (S/t136/tools/vbv2.py), and its
//  expected text is that run's output, never a backend's. The test-writer re-ran all of them through vbc again before writing these and every answer matched its `.exp`.
//
//  ENTRY POINTS: every probe goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on C#, C++ and MSIL: `TempExec.AssertMatchesInEveryEntryPoint`.
//  ⚠ `CompileProjectFiles` runs in the TEST process, where the post-optimizer IR verifier is ON (the runtime switch `BasicLang.VerifyIR`, as in a DEBUG build); the spawned CLI, as shipped, has it OFF. So a
//  shape that breaks an IR invariant is only seen on that leg: `MyBase.New(b.P)` first passed the CLI legs and failed there, on ADR-0016 Invariant P D5(c).
//  ⛔ Every probe is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never the in-process runner. A backend whose tool is missing (a C++ compiler, ilasm)
//  SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" but it runs NO Node: JavaScript refuses a ByRef parameter by design (BL7002), and the one JavaScript row only compiles through the CLI and the generator. So it is listed under
//  `JsExecutionTierRosterTests.NotJavaScriptExecution`, not in the roster, and the roster's count is unchanged.
//
//  MUTANTS (each built for real from a plain copy of the fix (as written, before it was cherry-picked onto #208) and run against THESE tests, through a copy of the test output with BasicLang.dll swapped; the cases that go red, measured, and NO other):
//    * M1 write-backs left to right (not right to left)        -> `TwoPropertyArguments_AreWrittenBackRightToLeft` ONLY (P09: C#, C++, MSIL print `111 102` where VB prints `21 102`).
//    * M2 no pin on the call's result before the first Set     -> `AFunctionUsedInAnExpression_...` (P08: C# CS1620), `AnInheritedStringProperty_...` (P16: C# CS1620), `APropertyInALambdaBody_...` (Q05: C# CS1620)
//                                                                 and `EveryCallArm_...` (P12 `New Holder(b.P)`: C# prints `11011` where VB prints `11111`). C# only.
//                                                                 ⚠ The three CS1620s are as measured BEFORE #232: an inlined call's ByRef argument had no `ref` then, and has one now (M2's C# cells are
//                                                                 not re-measured since).
//    * M3 no pin on the arguments BEFORE the property's Get     -> `AnEarlierArgument_IsEvaluatedBeforeThePropertysGet` ONLY (P14: C# runs `get 10` before `seed 1`).
//    * M4 no snapshot of a receiver variable the call rebinds  -> `AReboundReceiver_StillGetsTheWriteBack` ONLY (P15: the write-back lands on the NEW object, `first 10 second 11`, on C#, C++ and MSIL).
//    * M5 a ReadOnly property written back                     -> `AReadOnlyProperty_IsPassedACopy_AndNeverWrittenBack` (P06: C# CS0200 and C++ do not compile, MSIL does not run) and, since
//                                                                 #220, `AnExceptionsMessage_IsPassedACopy_AndNeverWrittenBack` (#220 re-ran M5 on those two rows: both red).
//    * M6 BL4004 never raised (the refusal removed)            -> `MyBaseNew_WithAGetSetPropertyArgument_IsRefusedWithBL4004_OnEveryBackend` ONLY: nothing refuses, the CLI builds, and `CompileProjectFiles` fails
//                                                                 on the verifier ("IR verification failed after optimization") instead of BL4004.
//    * M7 the accessor-backed narrowing dropped (a plain auto-property counts as a copy-out argument inside MyBase.New) -> `MyBaseNew_WithAPlainAutoPropertyArgument_StillRunsOnCpp_AsOnMaster` ONLY: the plain
//                                                                 property is refused with BL4004 on every entry point.
//    * #220: the built-in Exception.Message's ReadOnly flag removed (SymbolTable) -> `AnExceptionsMessage_IsPassedACopy_AndNeverWrittenBack` (C# CS0200, C++ does not compile).
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Each is outside what #209 fixes, and is the same before and after it:
//    * JavaScript refuses every ByRef parameter by design (BL7002), so no probe runs there; `JavaScript_StillRefusesTheByRefParameter_BL7002` is the one row that says so.
//    * A `Property` inside a `Structure` did not parse ("Expected member name but found Property", P07), so a struct's property could not be passed ByRef at all. (#230: it parses now; whether a struct's property passes ByRef is not measured here.)
//    * A .NET property is NOT a copy-out argument unless it is KNOWN ReadOnly. #222 made `l.Count` and `s.Length` ones (copied in, never written
//      back: `NetReadOnlyPropertyExecutionTests` runs `Change(l.Count)` and `Change(s.Length)`); every other .NET property is unchanged: a native-BCL-surface member has no property symbol, so the settable
//      `sb.Length` stays CS0206 on C#, refused on MSIL and a type-map failure on C++ (P10), and `Change(l.Capacity)` / `Change(a.Message)` are refused ("cannot convert from 'Object'").
//    * MSIL refuses `obj.Field` passed ByRef, a field READ through a receiver, by name (P11): "an expression's value lives in a temporary". That is a FIELD, and it predates #209
//      (`ConstructorByRefExecutionTests.MsilRefusesAFieldRead_...` pins the same refusal). The control below therefore runs P11 on C# and C++ only and its bare-field twin, C11m, on all three.
//    * ✅ FIXED by #232 (no row here): C# was CS1620 when an INSTANCE Function's result is used in an expression (`c.F(b.P) + 1`, Q02): the inline instance call wrote no `ref`. It prints vbc's
//      `45 22` on C#, C++ and MSIL now, and is the row `AnInstanceFunctionGivenAProperty_InsideAnExpression_...` of ByRefCallInExpressionCSharpExecutionTests.
//    * A List element passed ByRef (`Bump(l(0))`) is not a property, and is still silently wrong on C++ (prints 10 for 22, #296); C# is CS0206 and MSIL refuses it.
//    * BL4004 is a REFUSAL, not a write-back: `MyBase.New(b.P)` with a Get/Set property cannot be done until ADR-0016 D5(c) is amended to admit a write-back after the base call (follow-up task #297). A PLAIN
//      auto-property in `MyBase.New` is unchanged from master on the three backends that do not run: C# CS0206 (`MyBase.New(b.P)`; `MyBase.New(F(b.P))` was CS1620 until #232 wrote the inlined call's `ref`, and is CS0206 too now — measured), MSIL refuses it by name, JavaScript BL7002.
//      Only C++, which runs it, is pinned (`MyBaseNew_WithAPlainAutoPropertyArgument_StillRunsOnCpp_AsOnMaster`).
// ================================================================================================

/// <summary>#209 — a property passed ByRef is copied in and written back out, as VB does, on C#, C++ and MSIL.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C++, ilasm and C# child runs share the machine with the spawned CLI
public class PropertyByRefCopyOutExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names
    /// the probe, the backend and the entry point. (A copy of the helper `OperandEvaluationOrderExecutionTests` keeps: each fixture owns its own.)
    /// </summary>
    private static void AssertSingleFile(params TempProbe[] probes)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>
    /// The text the FRONT END refuses a program with, through <paramref name="entry"/>: the spawned CLI (<paramref name="backend"/>'s target) must exit non-zero, its output is returned; CompileProjectFiles
    /// must report errors, their messages are returned (it runs before any backend is chosen, so <paramref name="backend"/> does not matter there). A program that is NOT refused fails the calling test.
    /// </summary>
    private static string FrontEndRefusalText(Bk backend, EntryPoint entry, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t209-refuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            if (entry == EntryPoint.ProjectRelease)
            {
                var path = Path.Combine(dir, "Main.bas");
                File.WriteAllText(path, source);
                var result = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path });
                Assert.That(result.HasErrors, Is.True, "CompileProjectFiles must refuse the program");
                return string.Join("\n", result.AllErrors.Select(e => e.Message));
            }

            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + TempExec.TargetName(backend) };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            Assert.That(exit, Is.Not.Zero, $"CLI --target={TempExec.TargetName(backend)} {entry} must refuse the program:\n{stdout}{stderr}");
            return stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>
    /// The text JavaScript's GENERATOR refuses a program with, through <paramref name="entry"/>: the spawned CLI must exit non-zero (its output is returned), and CompileProjectFiles must compile and the
    /// generator throw <see cref="ForeignFeatureException"/> (its message is returned). A program that is NOT refused fails the calling test. (A copy of `ConstructorByRefExecutionTests`' helper.)
    /// </summary>
    private static string JavaScriptRefusalText(EntryPoint entry, string source)
    {
        if (entry != EntryPoint.ProjectRelease) return FrontEndRefusalText(Bk.JavaScript, entry, source);

        var dir = Path.Combine(Path.GetTempPath(), "bl-t209-refuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var result = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False, "the front end must accept the program (the refusal is the backend's): " + string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            var ir = result.CombinedIR;
            Assert.That(ir, Is.Not.Null, "the project entry point produced no combined IR");
            var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(ir), "JavaScript must refuse the program through CompileProjectFiles");
            return ex!.Message;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    // ============================================================================================
    //  THE SHAPES — every one prints vbc's answer on C#, C++ and MSIL
    // ============================================================================================

    /// <summary>
    /// (1) The headline, an auto-property and a Get/Set property passed ByRef. P01 writes the value back (22; C++ printed 22 before too, by passing the field). P02 prints its accessors, so the ORDER is
    /// visible: `get 10`, the call (`in 10`, `out 22`), `set 22`, and only then the `get 22` of the next read. C++ printed 10 for the Get/Set shape, C# was CS0206 and MSIL refused it.
    /// </summary>
    [Test]
    public void AnAutoPropertyAndAGetSetProperty_AreCopiedInAndWrittenBack_InGetCallSetOrder()
        => AssertSingleFile(PropertyByRefProbes.Auto, PropertyByRefProbes.Order);

    /// <summary>
    /// (2) The other spellings of the same property, from inside the class and from outside: a bare `P` and a bare auto-property `A` (P03), `Me.P` and `Me.A` (P04), and a Shared property read bare, qualified
    /// and through a Shared method (P05). The bare Get/Set form is the one the analyzer binds to a property symbol without a receiver, so it needs its own write-back (`EmitStoreToTarget`), not the
    /// qualified one's IRFieldStore.
    /// </summary>
    [Test]
    public void ABareMeQualifiedAndSharedProperty_AreWrittenBack()
        => AssertSingleFile(PropertyByRefProbes.Bare, PropertyByRefProbes.MeQualified, PropertyByRefProbes.SharedProperty);

    /// <summary>
    /// (3) A ReadOnly property (a Get-only one and a ReadOnly auto-property set in the constructor) is passed a COPY: the callee writes it (`in 22`, `in 19`), the object does not change (`10 7`), the
    /// getter runs once and there is no `set` anywhere, and vbc reports no error. M5 writes it back: a Set the property does not have (C# and C++ do not compile; MSIL does not run).
    /// </summary>
    [Test]
    public void AReadOnlyProperty_IsPassedACopy_AndNeverWrittenBack()
        => AssertSingleFile(PropertyByRefProbes.ReadOnly);

    /// <summary>
    /// (3b) #220 — the built-in <c>Exception.Message</c> is ReadOnly too (its <c>SymbolTable</c> declaration carries the flag), so <c>Change(ex.Message)</c> is the same copy-in, never-out: the callee
    /// sees `m` and writes its copy (`in m`, `now changed`), the exception keeps `m`. Before #220 the member was writable, IRBuilder wrote the copy back, and no backend ran it: C# CS0200, MSIL
    /// MissingFieldException (`System.Exception.Message`), C++ did not compile (S/t220/probes/EByRef.bas). Removing the flag from Message turns this red (measured: C# CS0200 on every
    /// entry point, C++ does not compile).
    /// </summary>
    [Test]
    public void AnExceptionsMessage_IsPassedACopy_AndNeverWrittenBack()
        => AssertSingleFile(PropertyByRefProbes.ExceptionMessage);

    /// <summary>
    /// (4) A Function with a ByRef parameter, used inside an expression: `r = F(b.P) + 1` prints `45 22` (F returns 2 * 22, plus 1; the property is 22). C# inlines a single-use value at its use, but the
    /// carrier and the write-back are STATEMENTS, so the call's result must be pinned before the first Set, or the Set runs before F (it was CS1620 on C# before #232). M2 removes the pin.
    /// </summary>
    [Test]
    public void AFunctionUsedInAnExpression_WritesTheProperty_BackAfterTheCallRan()
        => AssertSingleFile(PropertyByRefProbes.FunctionInExpression);

    /// <summary>
    /// (5) Two property arguments in one call are written back RIGHT TO LEFT, as vbc does: `Two(b.P, b.Q)` sets Q (102) BEFORE P (11), and `Two(b.P, b.P)` leaves the FIRST argument's value in P (21), the
    /// later write-back having run first. The Gets run left to right before the call. M1 writes them left to right: the Sets swap, and the same-property call ends with 111 in P.
    /// </summary>
    [Test]
    public void TwoPropertyArguments_AreWrittenBackRightToLeft()
        => AssertSingleFile(PropertyByRefProbes.TwoArguments);

    /// <summary>
    /// (6) Arguments are evaluated LEFT TO RIGHT around the property's Get: an EARLIER one runs before it (`Two(Seed(1), b.P)` prints `seed 1`, then `get 10`) and a LATER one after it
    /// (`Three(b.P, Seed(2))` prints `get 11`, then `seed 2`). C# inlines `Seed(1)` into the call, which is after the Get statement, so the earlier by-value arguments are pinned first. M3 removes the pin:
    /// C# runs `get 10` before `seed 1`.
    /// </summary>
    [Test]
    public void AnEarlierArgument_IsEvaluatedBeforeThePropertysGet()
        => AssertSingleFile(PropertyByRefProbes.ArgumentOrder);

    /// <summary>
    /// (7) A receiver that is a CALL is evaluated ONCE and before the Get: `Bump(GetBox().P)` prints `box` once and writes the property back to the object the Get read (P13, which also passes a nested
    /// `theBox.Inner.P`), and `MakeCalc().Add(b.P)` (Q04) runs `make` before `get 10`, the instance call's call-result receiver being pinned ahead of the Get on C#.
    /// </summary>
    [Test]
    public void ACallReceiver_IsEvaluatedOnce_BeforeTheGet()
        => AssertSingleFile(PropertyByRefProbes.CallReceiver, PropertyByRefProbes.CallReceiverOfTheCall);

    /// <summary>
    /// (8) A receiver VARIABLE the call itself rebinds: `Swap(b.P, b)` where Swap assigns `b = New Box("second")`. VB writes the temporary back to the object it READ, the first one (`first 11`), and the
    /// new object keeps its own value (`second 10`). The receiver is taken before the call (#203's `ValueBeforeLaterOperands`). M4 skips that: the write-back lands on the new object, on all three backends.
    /// </summary>
    [Test]
    public void AReboundReceiver_StillGetsTheWriteBack()
        => AssertSingleFile(PropertyByRefProbes.ReboundReceiver);

    /// <summary>
    /// (9) A String property, inherited, passed to a Sub twice and to a Function whose result is the condition of an `If` (`If F(d.Name) &gt; 3`): `long a!!?`. The Function's call sits in a
    /// condition, not an assignment, so a result with no pin is wrong on C# here too (it was CS1620 before #232; M2).
    /// </summary>
    [Test]
    public void AnInheritedStringProperty_InAnIfCondition_IsWrittenBack()
        => AssertSingleFile(PropertyByRefProbes.InheritedString);

    /// <summary>
    /// (10) Every call arm that takes a ByRef argument: a Shared method (`Util.SBump(b.P)`), a constructor (`New Holder(b.P)`), an instance method (`h.IBump(b.P)`) and `MyBase.VBump(b.P)`, then the
    /// control `MyBase.New(x)` with a local. Prints `11111`: the property starts at 10 and the four callees add 1, 100, 1000 and 10000 (`MyBase.VBump` is `Holder`'s, not `Kid`'s override). M2 leaves the constructor's result unpinned: C# prints the wrong answer.
    /// </summary>
    [Test]
    public void EveryCallArm_WritesAPropertyBack()
        => AssertSingleFile(PropertyByRefProbes.CallArms);

    /// <summary>(11) A property passed ByRef INSIDE a lambda body: `Function() F(b.P) * 2` prints `44 22`: the carrier lives in the lambda, and the write-back runs before the lambda returns.</summary>
    [Test]
    public void APropertyInALambdaBody_IsWrittenBack()
        => AssertSingleFile(PropertyByRefProbes.LambdaBody);

    /// <summary>
    /// (12) A Get/Set property (writable, accessor-backed) passed ByRef INSIDE a `MyBase.New` argument list is REFUSED with BL4004 on every backend: `MyBase.New(b.P)` into a ByRef base parameter (Q01g) and
    /// nested in a call (`MyBase.New(F(b.P))`, F's parameter ByRef, Q06g). The write-back cannot run inside a base-constructor call: the prologue is expression-only and CLOSED (ADR-0016 D5(c)), so nothing it
    /// computes may be used after the call and a Set is a statement, and C# has nowhere to put a statement before `: base(...)`. The code and the property's NAME are asserted, through the spawned CLI on all four
    /// targets, the CLI with `--optimize` on all four, and `CompileProjectFiles` (a front-end diagnostic, raised by the analyzer before any backend is chosen, so the LSP shows it too). Before the refusal C++ and
    /// MSIL ran it with the IR verifier off and broke Invariant P with it on, and on master C++ printed 10 where VB prints 22. M6 removes the refusal; M7 narrows it wrongly (see (13)).
    /// </summary>
    [Test]
    public void MyBaseNew_WithAGetSetPropertyArgument_IsRefusedWithBL4004_OnEveryBackend()
    {
        var failures = new List<string>();
        void Check(string cell, Func<string> refusal)
        {
            try
            {
                var text = refusal();
                if (!text.Contains("BL4004")) failures.Add($"{cell}: no BL4004 in [{text.Split('\n')[0]}]");
                if (!text.Contains("Property 'P' cannot be passed ByRef inside a 'MyBase.New' call")) failures.Add($"{cell}: the message does not name Property 'P' [{text.Split('\n')[0]}]");
                if (text.Contains("Unhandled exception")) failures.Add($"{cell}: a stack trace");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{cell}: {ex.Message.Split('\n')[0]}");
            }
        }

        foreach (var probe in new[] { PropertyByRefProbes.BaseNewGetSet, PropertyByRefProbes.BaseNewNestedGetSet })
        {
            foreach (var backend in new[] { Bk.CSharp, Bk.Cpp, Bk.JavaScript, Bk.Msil })
                foreach (var entry in new[] { EntryPoint.Cli, EntryPoint.CliOptimize })
                    Check($"{probe.Id} {backend} {entry}", () => FrontEndRefusalText(backend, entry, probe.Source));
            Check($"{probe.Id} CompileProjectFiles", () => FrontEndRefusalText(Bk.CSharp, EntryPoint.ProjectRelease, probe.Source));
        }

        Assert.That(failures, Is.Empty, "a Get/Set property passed ByRef in `MyBase.New(...)` must be refused with BL4004 naming the property:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// (13) A PLAIN auto-property in a `MyBase.New` argument list is NOT refused and NOT copied in: it lowers exactly as on master, as its storage (no carrier, no write-back; the callee's write lands in the
    /// backing field, which is VB's answer here). C++ prints vbc's `22` for `MyBase.New(b.P)` (Q01a) and `base 22` + `22` for `MyBase.New(F(b.P))` (Q06a) through the CLI, the CLI with `--optimize` and
    /// `CompileProjectFiles`; the last one runs the IR VERIFIER (ON in the test process, as in a DEBUG build, OFF in the spawned CLI as shipped), so it also holds this shape to ADR-0016 Invariant P D5(c).
    /// ⚠ Only C++ is pinned. The other three backends give master's outcomes (C# CS0206 for Q01a and for Q06a — Q06a was CS1620 until #232 —, MSIL refuses it by name, JavaScript BL7002) and asserting a pre-existing
    /// compile failure would pin a defect (the fixture header). M7 drops the accessor-backed narrowing: a plain property is then refused with BL4004 (and would be copied out), and this row goes red on every entry.
    /// </summary>
    [Test]
    public void MyBaseNew_WithAPlainAutoPropertyArgument_StillRunsOnCpp_AsOnMaster()
        => AssertSingleFile(PropertyByRefProbes.BaseNewAuto, PropertyByRefProbes.BaseNewNestedAuto);

    // ============================================================================================
    //  THE CONTROLS and THE REFUSAL
    // ============================================================================================

    /// <summary>
    /// (14) The CONTROLS, true references that must stay true references: a FIELD passed ByRef is the callee's storage (`mid F 22`, the object changes mid-call), a PROPERTY is a copy (`mid P 10`), and a
    /// local is storage again (`mid P 22`, `22 22 22`). P11 passes `b.F` qualified (C# and C++: MSIL refuses a field read through a receiver, see the header); C11m is the same program with the field and
    /// the property passed bare from inside the class, on all three.
    /// </summary>
    [Test]
    public void AFieldAndALocal_PassedByRef_StillPassTheStorage_WhileAPropertyIsACopy()
        => AssertSingleFile(PropertyByRefProbes.ControlsQualified, PropertyByRefProbes.ControlsBare);

    /// <summary>
    /// (15) JavaScript still REFUSES the ByRef parameter, by design: BL7002 ("JavaScript has no reference parameters"), through the CLI (exit code non-zero, no stack trace), the CLI with `--optimize` and
    /// CompileProjectFiles (the generator's refusal). #209 changes nothing here, and nothing runs under Node: this is the only JavaScript row.
    /// </summary>
    [Test]
    public void JavaScript_StillRefusesTheByRefParameter_BL7002()
    {
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            var text = JavaScriptRefusalText(entry, PropertyByRefProbes.Auto.Source);
            if (!text.Contains("BL7002")) failures.Add($"{entry}: [{text.Split('\n')[0]}]");
            if (text.Contains("Unhandled exception")) failures.Add($"{entry}: a stack trace");
        }
        Assert.That(failures, Is.Empty, "JavaScript must refuse the ByRef parameter with BL7002:\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// The #209 probes. Every source is the implementer's own (`S/t209/probes`, `probes2`, `probes5`, bar C11m) and every expected value is vbc's OWN output for it (see the fixture header). Every probe is HangSafe
/// (#256): its C# leg runs in a time-limited child process.
/// </summary>
internal static class PropertyByRefProbes
{
    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;

    private static TempProbe P(string id, string source, string vb, Bk agrees = Three) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>P01: `Bump(b.P)` on an auto-property, `x += 12`.</summary>
    internal static readonly TempProbe Auto = P("p01_auto", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Sub Bump(ByRef x As Integer)
            x += 12
        End Sub

        Sub Main()
            Dim b As New Box()
            Bump(b.P)
            Console.WriteLine(b.P)
        End Sub
        """, "22");

    /// <summary>P02: a Get/Set property that prints, so Get / call / Set is visible.</summary>
    internal static readonly TempProbe Order = P("p02_order", """
        Class Box
            Private _v As Integer = 10
            Public Property P As Integer
                Get
                    Console.WriteLine("get " & _v)
                    Return _v
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _v = value
                End Set
            End Property
        End Class

        Sub Bump(ByRef x As Integer)
            Console.WriteLine("in " & x)
            x += 12
            Console.WriteLine("out " & x)
        End Sub

        Sub Main()
            Dim b As New Box()
            Bump(b.P)
            Console.WriteLine("now " & b.P)
        End Sub
        """, "get 10\nin 10\nout 22\nset 22\nget 22\nnow 22");

    /// <summary>P03: a bare `P` (Get/Set) and a bare `A` (auto) inside the declaring class.</summary>
    internal static readonly TempProbe Bare = P("p03_bare", """
        Class Box
            Private _v As Integer = 10
            Public Property P As Integer
                Get
                    Console.WriteLine("get " & _v)
                    Return _v
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _v = value
                End Set
            End Property
            Public Property A As Integer
            Public Sub New()
                A = 5
            End Sub
            Public Sub Go()
                Bump(P)
                Bump(A)
                Console.WriteLine(_v & " " & A)
            End Sub
        End Class

        Sub Bump(ByRef x As Integer)
            x += 12
        End Sub

        Sub Main()
            Dim b As New Box()
            b.Go()
        End Sub
        """, "get 10\nset 22\n22 17");

    /// <summary>P04: the same two properties, `Me.`-qualified.</summary>
    internal static readonly TempProbe MeQualified = P("p04_me", """
        Class Box
            Private _v As Integer = 10
            Public Property P As Integer
                Get
                    Console.WriteLine("get " & _v)
                    Return _v
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _v = value
                End Set
            End Property
            Public Property A As Integer
            Public Sub New()
                A = 5
            End Sub
            Public Sub Go()
                Bump(Me.P)
                Bump(Me.A)
                Console.WriteLine(_v & " " & A)
            End Sub
        End Class

        Sub Bump(ByRef x As Integer)
            x += 12
        End Sub

        Sub Main()
            Dim b As New Box()
            b.Go()
        End Sub
        """, "get 10\nset 22\n22 17");

    /// <summary>P05: a Shared auto-property and a Shared Get/Set property, from `Main` (qualified) and from a Shared method (bare).</summary>
    internal static readonly TempProbe SharedProperty = P("p05_shared", """
        Class Cfg
            Public Shared Property S As Integer
            Private Shared _t As Integer
            Public Shared Property T As Integer
                Get
                    Console.WriteLine("get " & _t)
                    Return _t
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _t = value
                End Set
            End Property
            Public Shared Sub Go()
                Bump(S)
                Bump(T)
            End Sub
        End Class

        Sub Bump(ByRef x As Integer)
            x += 12
        End Sub

        Sub Main()
            Cfg.S = 10
            Cfg.T = 1
            Bump(Cfg.S)
            Bump(Cfg.T)
            Console.WriteLine(Cfg.S & " " & Cfg.T)
            Cfg.Go()
            Console.WriteLine(Cfg.S & " " & Cfg.T)
        End Sub
        """, "set 1\nget 1\nset 13\nget 13\n22 13\nget 13\nset 25\nget 25\n34 25");

    /// <summary>P06: a ReadOnly Get-only property and a ReadOnly auto-property: a copy, no write-back, no error.</summary>
    internal static readonly TempProbe ReadOnly = P("p06_readonly", """
        Class Box
            Private _v As Integer = 10
            Public ReadOnly Property R As Integer
                Get
                    Console.WriteLine("get " & _v)
                    Return _v
                End Get
            End Property
            Public ReadOnly Property Q As Integer
            Public Sub New()
                Q = 7
            End Sub
        End Class

        Sub Bump(ByRef x As Integer)
            x += 12
            Console.WriteLine("in " & x)
        End Sub

        Sub Main()
            Dim b As New Box()
            Bump(b.R)
            Bump(b.Q)
            Console.WriteLine(b.R & " " & b.Q)
        End Sub
        """, "get 10\nin 22\nin 19\nget 10\n10 7");

    /// <summary>P06e (#220, `S/t220/probes2/p06e.bas`, vbc-answered): the built-in Exception's ReadOnly Message passed ByRef — a copy, no write-back, no error.</summary>
    internal static readonly TempProbe ExceptionMessage = P("p06e_exception", """
        Sub Change(ByRef s As String)
            Console.WriteLine("in " & s)
            s = "changed"
            Console.WriteLine("now " & s)
        End Sub

        Sub Main()
            Try
                Throw New Exception("m")
            Catch ex As Exception
                Change(ex.Message)
                Console.WriteLine(ex.Message)
            End Try
        End Sub
        """, "in m\nnow changed\nm");

    /// <summary>P08: `r = F(b.P) + 1`, a module Function with a ByRef parameter inside an expression.</summary>
    internal static readonly TempProbe FunctionInExpression = P("p08_func", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Function F(ByRef x As Integer) As Integer
            x += 12
            Return x * 2
        End Function

        Sub Main()
            Dim b As New Box()
            Dim r As Integer = F(b.P) + 1
            Console.WriteLine(r & " " & b.P)
        End Sub
        """, "45 22");

    /// <summary>P09: `Two(b.P, b.Q)` and `Two(b.P, b.P)`, two property arguments in one call.</summary>
    internal static readonly TempProbe TwoArguments = P("p09_two", """
        Class Box
            Private _p As Integer = 1
            Private _q As Integer = 2
            Public Property P As Integer
                Get
                    Console.WriteLine("get P " & _p)
                    Return _p
                End Get
                Set(value As Integer)
                    Console.WriteLine("set P " & value)
                    _p = value
                End Set
            End Property
            Public Property Q As Integer
                Get
                    Console.WriteLine("get Q " & _q)
                    Return _q
                End Get
                Set(value As Integer)
                    Console.WriteLine("set Q " & value)
                    _q = value
                End Set
            End Property
        End Class

        Sub Two(ByRef x As Integer, ByRef y As Integer)
            x += 10
            y += 100
        End Sub

        Sub Main()
            Dim b As New Box()
            Two(b.P, b.Q)
            Two(b.P, b.P)
            Console.WriteLine(b.P & " " & b.Q)
        End Sub
        """, "get P 1\nget Q 2\nset Q 102\nset P 11\nget P 11\nget P 11\nset P 111\nset P 21\nget P 21\nget Q 102\n21 102");

    /// <summary>P14: `Two(Seed(1), b.P)` (the property AFTER a call) and `Three(b.P, Seed(2))` (the property BEFORE one).</summary>
    internal static readonly TempProbe ArgumentOrder = P("p14_argorder", """
        Class Box
            Private _v As Integer = 10
            Public Property P As Integer
                Get
                    Console.WriteLine("get " & _v)
                    Return _v
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _v = value
                End Set
            End Property
        End Class

        Function Seed(n As Integer) As Integer
            Console.WriteLine("seed " & n)
            Return n
        End Function

        Sub Two(a As Integer, ByRef x As Integer)
            Console.WriteLine("call " & a)
            x += a
        End Sub

        Sub Three(ByRef x As Integer, a As Integer)
            Console.WriteLine("call " & a)
            x += a
        End Sub

        Sub Main()
            Dim b As New Box()
            Two(Seed(1), b.P)
            Three(b.P, Seed(2))
            Console.WriteLine("now " & b.P)
        End Sub
        """, "seed 1\nget 10\ncall 1\nset 11\nget 11\nseed 2\ncall 2\nset 13\nget 13\nnow 13");

    /// <summary>P13: `Bump(GetBox().P)` and the nested `Bump(theBox.Inner.P)`; the receiver call prints once.</summary>
    internal static readonly TempProbe CallReceiver = P("p13_receiver", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
            Public Inner As Box
        End Class

        Dim theBox As Box

        Function GetBox() As Box
            Console.WriteLine("box")
            Return theBox
        End Function

        Sub Bump(ByRef x As Integer)
            x += 12
        End Sub

        Sub Main()
            theBox = New Box()
            theBox.Inner = New Box()
            Bump(GetBox().P)
            Bump(theBox.Inner.P)
            Console.WriteLine(theBox.P & " " & theBox.Inner.P)
        End Sub
        """, "box\n22 22");

    /// <summary>Q04: `MakeCalc().Add(b.P)`, an instance method called on a call's result.</summary>
    internal static readonly TempProbe CallReceiverOfTheCall = P("q04_recvcall", """
        Class Box
            Private _v As Integer = 10
            Public Property P As Integer
                Get
                    Console.WriteLine("get " & _v)
                    Return _v
                End Get
                Set(value As Integer)
                    Console.WriteLine("set " & value)
                    _v = value
                End Set
            End Property
        End Class

        Class Calc
            Public Sub Add(ByRef x As Integer)
                Console.WriteLine("call")
                x += 1
            End Sub
        End Class

        Function MakeCalc() As Calc
            Console.WriteLine("make")
            Return New Calc()
        End Function

        Sub Main()
            Dim b As New Box()
            MakeCalc().Add(b.P)
            Console.WriteLine("now " & b.P)
        End Sub
        """, "make\nget 10\ncall\nset 11\nget 11\nnow 11");

    /// <summary>P15: `Swap(b.P, b)`; Swap rebinds `b`, and VB writes back to the object it read.</summary>
    internal static readonly TempProbe ReboundReceiver = P("p15_rebind", """
        Class Box
            Public Property P As Integer
            Public Name As String
            Public Sub New(n As String)
                Name = n
                P = 10
            End Sub
        End Class

        Sub Swap(ByRef x As Integer, ByRef bb As Box)
            x += 1
            bb = New Box("second")
        End Sub

        Sub Main()
            Dim b As New Box("first")
            Dim keep As Box = b
            Swap(b.P, b)
            Console.WriteLine(keep.Name & " " & keep.P & " " & b.Name & " " & b.P)
        End Sub
        """, "first 11 second 10");

    /// <summary>P16: an inherited String property passed to a Sub twice and to a Function in an `If` condition.</summary>
    internal static readonly TempProbe InheritedString = P("p16_string", """
        Class Base
            Private _n As String = "a"
            Public Property Name As String
                Get
                    Return _n
                End Get
                Set(value As String)
                    _n = value
                End Set
            End Property
        End Class

        Class Derived
            Inherits Base
        End Class

        Sub App(ByRef s As String)
            s &= "!"
        End Sub

        Function F(ByRef s As String) As Integer
            s &= "?"
            Return s.Length
        End Function

        Sub Main()
            Dim d As New Derived()
            App(d.Name)
            App(d.Name)
            If F(d.Name) > 3 Then
                Console.WriteLine("long " & d.Name)
            End If
            Console.WriteLine(d.Name)
        End Sub
        """, "long a!!?\na!!?");

    /// <summary>P12: a Shared method, `New Holder(b.P)`, an instance method and `MyBase.VBump(b.P)`, beside `MyBase.New(x)` with a local.</summary>
    internal static readonly TempProbe CallArms = P("p12_arms", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Class Util
            Public Shared Sub SBump(ByRef x As Integer)
                x += 1
            End Sub
        End Class

        Class Holder
            Public Sub New(ByRef x As Integer)
                x += 100
            End Sub
            Public Sub IBump(ByRef x As Integer)
                x += 1000
            End Sub
            Public Overridable Sub VBump(ByRef x As Integer)
                x += 10000
            End Sub
        End Class

        Class Kid
            Inherits Holder
            Public Sub New(ByRef x As Integer)
                MyBase.New(x)
            End Sub
            Public Overrides Sub VBump(ByRef x As Integer)
                x += 1
            End Sub
            Public Sub Go(b As Box)
                MyBase.VBump(b.P)
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Util.SBump(b.P)
            Dim h As New Holder(b.P)
            h.IBump(b.P)
            Dim n As Integer = 0
            Dim k As New Kid(n)
            k.Go(b)
            Console.WriteLine(b.P)
        End Sub
        """, "11111");

    /// <summary>Q05: `Function() F(b.P) * 2`, a property passed ByRef inside a lambda body.</summary>
    internal static readonly TempProbe LambdaBody = P("q05_lambda", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Function F(ByRef x As Integer) As Integer
            x += 12
            Return x
        End Function

        Sub Main()
            Dim b As New Box()
            Dim g As Func(Of Integer) = Function() F(b.P) * 2
            Console.WriteLine(g() & " " & b.P)
        End Sub
        """, "44 22");

    /// <summary>Q01a: `MyBase.New(b.P)` with a PLAIN auto-property, the base constructor taking `ByRef x`. C++ only: it lowers as on master (C# CS0206, MSIL refuses, JavaScript BL7002).</summary>
    internal static readonly TempProbe BaseNewAuto = P("q01a_auto", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Class Base
            Public Sub New(ByRef x As Integer)
                x += 12
            End Sub
        End Class

        Class Kid
            Inherits Base
            Public Sub New(b As Box)
                MyBase.New(b.P)
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Dim k As New Kid(b)
            Console.WriteLine(b.P)
        End Sub
        """, "22", Bk.Cpp);

    /// <summary>Q06a: `MyBase.New(F(b.P))`, a PLAIN auto-property passed ByRef in a call that is itself a MyBase.New argument. C++ only: it lowers as on master (C# CS0206 — CS1620 until #232 —, MSIL refuses, JavaScript BL7002).</summary>
    internal static readonly TempProbe BaseNewNestedAuto = P("q06a_auto", """
        Class Box
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Class Base
            Public Sub New(n As Integer)
                Console.WriteLine("base " & n)
            End Sub
        End Class

        Function F(ByRef x As Integer) As Integer
            x += 12
            Return x
        End Function

        Class Kid
            Inherits Base
            Public Sub New(b As Box)
                MyBase.New(F(b.P))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Dim k As New Kid(b)
            Console.WriteLine(b.P)
        End Sub
        """, "base 22\n22", Bk.Cpp);

    /// <summary>Q01g: `MyBase.New(b.P)` with a Get/Set property: refused with BL4004 on every backend, so no backend runs it (`Agrees` is empty). vbc prints 22.</summary>
    internal static readonly TempProbe BaseNewGetSet = P("q01g_getset", """
        Class Box
            Private _p As Integer
            Public Property P As Integer
                Get
                    Return _p
                End Get
                Set(value As Integer)
                    _p = value
                End Set
            End Property
            Public Sub New()
                P = 10
            End Sub
        End Class

        Class Base
            Public Sub New(ByRef x As Integer)
                x += 12
            End Sub
        End Class

        Class Kid
            Inherits Base
            Public Sub New(b As Box)
                MyBase.New(b.P)
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Dim k As New Kid(b)
            Console.WriteLine(b.P)
        End Sub
        """, "22", agrees: 0);

    /// <summary>Q06g: `MyBase.New(F(b.P))` with a Get/Set property: refused with BL4004 on every backend, so no backend runs it. vbc prints `base 22` and 22.</summary>
    internal static readonly TempProbe BaseNewNestedGetSet = P("q06g_getset", """
        Class Box
            Private _p As Integer
            Public Property P As Integer
                Get
                    Return _p
                End Get
                Set(value As Integer)
                    _p = value
                End Set
            End Property
            Public Sub New()
                P = 10
            End Sub
        End Class

        Class Base
            Public Sub New(n As Integer)
                Console.WriteLine("base " & n)
            End Sub
        End Class

        Function F(ByRef x As Integer) As Integer
            x += 12
            Return x
        End Function

        Class Kid
            Inherits Base
            Public Sub New(b As Box)
                MyBase.New(F(b.P))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Dim k As New Kid(b)
            Console.WriteLine(b.P)
        End Sub
        """, "base 22\n22", agrees: 0);

    /// <summary>P11 CONTROL: a field (`b.F`) and a local passed ByRef are storage; a property (`b.P`) is a copy. C# and C++: MSIL refuses a field read through a receiver (the fixture header).</summary>
    internal static readonly TempProbe ControlsQualified = P("p11_controls", """
        Class Box
            Public F As Integer = 10
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
        End Class

        Sub PeekF(ByRef x As Integer, b As Box)
            x += 12
            Console.WriteLine("mid F " & b.F)
        End Sub

        Sub PeekP(ByRef x As Integer, b As Box)
            x += 12
            Console.WriteLine("mid P " & b.P)
        End Sub

        Sub Main()
            Dim b As New Box()
            Dim n As Integer = 10
            PeekF(b.F, b)
            PeekP(b.P, b)
            PeekP(n, b)
            Console.WriteLine(b.F & " " & b.P & " " & n)
        End Sub
        """, "mid F 22\nmid P 10\nmid P 22\n22 22 22", Bk.CSharp | Bk.Cpp);

    /// <summary>C11m CONTROL: P11's twin with the field and the property passed BARE from inside the class, so MSIL runs it too. Same four lines, same answer.</summary>
    internal static readonly TempProbe ControlsBare = P("c11m_controls_bare", """
        Class Box
            Public F As Integer = 10
            Public Property P As Integer
            Public Sub New()
                P = 10
            End Sub
            Public Sub Go()
                PeekF(F, Me)
                PeekP(P, Me)
            End Sub
        End Class

        Sub PeekF(ByRef x As Integer, b As Box)
            x += 12
            Console.WriteLine("mid F " & b.F)
        End Sub

        Sub PeekP(ByRef x As Integer, b As Box)
            x += 12
            Console.WriteLine("mid P " & b.P)
        End Sub

        Sub Main()
            Dim b As New Box()
            Dim n As Integer = 10
            b.Go()
            PeekP(n, b)
            Console.WriteLine(b.F & " " & b.P & " " & n)
        End Sub
        """, "mid F 22\nmid P 10\nmid P 22\n22 22 22");
}
