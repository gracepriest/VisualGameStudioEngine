using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #256 — the SHAPE of the C# a While/Do loop is written as. FAST: it emits and reads text, and RUNS NOTHING (so a regression of the
//  fix cannot hang it — the hang is LoopConditionReevaluationExecutionTests' job, through the time-limited runner).
//
//  ⭐ WHY TEXT TESTS EXIST FOR THIS ONE. The runs prove the answer; these prove the PROPERTIES the fix is made of, and they kill the mutants of
//  the fix in the fast tier without a single spin: a condition emitted once above the loop, twice per iteration, in the wrong polarity, or after
//  the body; a counted For pulled into the new shape; a bottom-tested loop written as a peeled first iteration plus a copy of the body (#227).
//
//  ⭐ #227: a bottom-tested loop (`Do … Loop While/Until`) is written ONCE, from its body — `do { body } while (c);` for a condition that is one
//  block and writes nothing, `while (true) { body; …condition…; if (!c) break; }` for everything else. It used to be a peeled first iteration
//  followed by `while (c) { body }`, and the second copy dropped every block the first had written. The LW/LU rows below pin the new shapes.
//
//  The three entry points that stay in process: the standard passes (what the CLI does), the aggressive passes (`--optimize`), and
//  `CompileProjectFiles` (the IDE / Release project build). Every case reads all three.
// ================================================================================================

/// <summary>
/// #256: a condition with no control flow keeps `while (cond)` byte for byte; a condition holding AndAlso, OrElse or If() is written
/// `while (true) { …condition blocks…; if (!(c)) break; …body… }` — one exit test, nothing of the condition above the loop.
/// </summary>
[TestFixture]
public class LoopConditionEmissionShapeTests
{
    // ---------------------------------------------------------------------------- helpers

    private static readonly (string Name, Func<string, string> Emit)[] Pipelines =
    {
        ("standard", ReturnCoercionTests.EmitCSharpForTest),
        ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest),
        ("project", source => TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source)),
    };

    /// <summary>The emitted C# with its `#line` directives dropped and every run of whitespace collapsed to one space.</summary>
    internal static string Flat(string csharp)
        => Regex.Replace(Regex.Replace(csharp, @"^[ \t]*#line.*$", "", RegexOptions.Multiline), @"\s+", " ").Trim();

    /// <summary>
    /// The LOOP of a grid program: what the emitter wrote between `i = 0; body = 0;` and the closing `Console.WriteLine(` — the loop and nothing
    /// else, a bottom-tested loop included (#227: it is written once, with no peeled first iteration above it).
    /// </summary>
    private static string Loop(string csharp)
    {
        var flat = Flat(csharp);
        const string start = "i = 0; body = 0; ";
        var a = flat.IndexOf(start, StringComparison.Ordinal);
        Assert.That(a, Is.GreaterThanOrEqualTo(0), "the program's `i = 0; body = 0;` is missing:\n" + flat);
        a += start.Length;
        var b = flat.IndexOf(" Console.WriteLine(", a, StringComparison.Ordinal);
        Assert.That(b, Is.GreaterThan(a), "the loop's closing WriteLine is missing:\n" + flat);
        return flat.Substring(a, b - a);
    }

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    private const string Body = "i = i + 1; body = body + 1;";

    private static bool IsUntil(string form) => form is "DU" or "LU";

    private static bool IsBottomTested(string form) => form is "LW" or "LU";

    // ============================================================================================
    // 1. A ONE-BLOCK CONDITION IS UNCHANGED, BYTE FOR BYTE.
    // ============================================================================================

    /// <summary>
    /// ⭐ A plain compare (ctl), one call (se), `Not` (not), `And` (and) and `Or` (or) are ONE block, so the loop keeps `while (cond)` exactly as
    /// before #256 — measured: each of the 15 top-tested loops (W, DW, DU) emits the same bytes on 1ba6af20 (master) and on the fix, standard
    /// and `--optimize`. The expected text is that text; a loop pulled into the new shape by accident (every `while` rewritten, say) fails here.
    ///
    /// <para>#227: the 10 bottom-tested rows (LW, LU) are `do { body } while (cond);` — the condition is still written byte for byte (and still
    /// once: `while (cond);` is the loop's only test), but the body is written ONCE, above it, in the loop's braces. They used to be
    /// `body while (cond) { body }`, a peeled first iteration plus a copy. Until is `while (!(cond));`, the polarity the old text had.</para>
    /// </summary>
    private static readonly object[][] OneBlock =
    {
        new object[] { "W", "ctl", "while (i < 3) { i = i + 1; body = body + 1; }" },
        new object[] { "W", "se", "while (P(\"a\", i < 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "W", "not", "while (!(i >= 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "W", "and", "while ((i < 3) & (i < 9)) { i = i + 1; body = body + 1; }" },
        new object[] { "W", "or", "while ((i < 1) | (i < 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "DW", "ctl", "while (i < 3) { i = i + 1; body = body + 1; }" },
        new object[] { "DW", "se", "while (P(\"a\", i < 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "DW", "not", "while (!(i >= 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "DW", "and", "while ((i < 3) & (i < 9)) { i = i + 1; body = body + 1; }" },
        new object[] { "DW", "or", "while ((i < 1) | (i < 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "DU", "ctl", "while (!(i >= 3)) { i = i + 1; body = body + 1; }" },
        new object[] { "DU", "se", "while (!(P(\"a\", i >= 3))) { i = i + 1; body = body + 1; }" },
        new object[] { "DU", "not", "while (!(!(i < 3))) { i = i + 1; body = body + 1; }" },
        new object[] { "DU", "and", "while (!((i >= 3) & (i < 9))) { i = i + 1; body = body + 1; }" },
        new object[] { "DU", "or", "while (!((i >= 3) | (i < 0))) { i = i + 1; body = body + 1; }" },
        new object[] { "LW", "ctl", "do { i = i + 1; body = body + 1; } while (i < 3);" },
        new object[] { "LW", "se", "do { i = i + 1; body = body + 1; } while (P(\"a\", i < 3));" },
        new object[] { "LW", "not", "do { i = i + 1; body = body + 1; } while (!(i >= 3));" },
        new object[] { "LW", "and", "do { i = i + 1; body = body + 1; } while ((i < 3) & (i < 9));" },
        new object[] { "LW", "or", "do { i = i + 1; body = body + 1; } while ((i < 1) | (i < 3));" },
        new object[] { "LU", "ctl", "do { i = i + 1; body = body + 1; } while (!(i >= 3));" },
        new object[] { "LU", "se", "do { i = i + 1; body = body + 1; } while (!(P(\"a\", i >= 3)));" },
        new object[] { "LU", "not", "do { i = i + 1; body = body + 1; } while (!(!(i < 3)));" },
        new object[] { "LU", "and", "do { i = i + 1; body = body + 1; } while (!((i >= 3) & (i < 9)));" },
        new object[] { "LU", "or", "do { i = i + 1; body = body + 1; } while (!((i >= 3) | (i < 0)));" },
    };

    [TestCaseSource(nameof(OneBlock))]
    public void AOneBlockCondition_KeepsWhileCond_ByteForByte(string form, string kind, string expected)
    {
        var source = LoopConditionProbes.Program(form, kind);

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
                Assert.That(Loop(emit(source)), Is.EqualTo(expected), $"{form}_{kind}, {name} passes");
        });
    }

    // ============================================================================================
    // 2. A CONDITION HOLDING CONTROL FLOW: `while (true)`, the condition INSIDE, ONE exit test, then the body.
    // ============================================================================================

    private static IEnumerable<TestCaseData> ShortCircuitCells()
        => LoopConditionProbes.Forms.SelectMany(f => new[] { "aa", "oe", "iff", "nt", "cx" }.Select(k => new TestCaseData(f, k).SetName($"{f}_{k}")));

    /// <summary>How many times the counting call P(…) is WRITTEN for each kind — once per operand, not once per iteration and not twice.</summary>
    private static int OperandCalls(string kind) => kind switch { "aa" => 2, "oe" => 2, "iff" => 3, "nt" => 2, "cx" => 4, _ => throw new ArgumentException(kind) };

    /// <summary>
    /// The structure, for every form x kind, in every entry point:
    /// (1) ONE `while`, and it is `while (true)` — the stale `while (__sc0)` over a carrier nothing rewrites is the bug;
    /// (2) NOTHING is written before it — not the condition, and (#227) not a copy of the body either: a bottom-tested loop used to be a peeled
    ///     first iteration followed by the loop, and is written once, inside it;
    /// (3) every operand is written EXACTLY once (a condition emitted twice runs its left operand twice per iteration);
    /// (4) exactly ONE `break;`, the exit test, `if (!(c)) break;` for While and `if (c) break;` for Until — c is the carrier, or `!carrier` for Not;
    /// (5) the exit test comes AFTER every operand. A top-tested loop (W, DW, DU) is `condition; exit test; body` and the body ends the loop; a
    ///     bottom-tested one (LW, LU — #227) is `body; condition; exit test` and the exit test ends the loop, with the body written ONCE.
    /// </summary>
    [TestCaseSource(nameof(ShortCircuitCells))]
    public void AConditionHoldingControlFlow_IsWrittenInsideWhileTrue_WithOneExitTest(string form, string kind)
    {
        var source = LoopConditionProbes.Program(form, kind);
        var carrierRead = kind == "nt" ? "!__sc0" : "__sc0";
        var exit = IsUntil(form) ? $"if ({carrierRead}) break;" : $"if (!({carrierRead})) break;";

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var loop = Loop(emit(source));
                var label = $"{form}_{kind}, {name} passes:\n{loop}";

                var open = loop.IndexOf("while (true) { ", StringComparison.Ordinal);
                Assert.That(open, Is.GreaterThanOrEqualTo(0), "opens `while (true)` — " + label);
                Assert.That(Count(loop, @"\bwhile \("), Is.EqualTo(1), "the only `while` is `while (true)` (the stale `while (__sc0)` is #256) — " + label);

                Assert.That(loop.Substring(0, open), Is.EqualTo(""),
                    "above the loop there is nothing — no condition, and no peeled copy of the body of a bottom-tested loop (#227) — " + label);
                Assert.That(Count(loop, Regex.Escape(Body)), Is.EqualTo(1), "the body is written exactly once (#227: a peel plus a copy is two) — " + label);

                Assert.That(Count(loop, @"\bP\("), Is.EqualTo(OperandCalls(kind)), "every operand is written exactly once — " + label);
                Assert.That(Count(loop, @"\bbreak;"), Is.EqualTo(1), "exactly one break, the exit test — " + label);
                Assert.That(Count(loop, Regex.Escape(exit)), Is.EqualTo(1), $"the exit test is `{exit}` — " + label);

                var exitAt = loop.IndexOf(exit, StringComparison.Ordinal);
                Assert.That(loop.LastIndexOf("P(", exitAt, StringComparison.Ordinal), Is.GreaterThan(open), "every operand is inside the loop, before the exit test — " + label);
                if (IsBottomTested(form))
                {
                    Assert.That(loop, Does.StartWith("while (true) { " + Body + " "), "the body is the first thing in the loop — " + label);
                    Assert.That(loop, Does.EndWith(exit + " }"), "the exit test is the last thing in the loop, after the condition (#227) — " + label);
                    Assert.That(loop.IndexOf(Body, StringComparison.Ordinal), Is.LessThan(loop.IndexOf("P(", StringComparison.Ordinal)),
                        "the condition is written after the body, never before it — " + label);
                }
                else
                {
                    Assert.That(loop, Does.EndWith(exit + " " + Body + " }"), "the exit test is followed by the body, which ends the loop — " + label);
                }
            }
        });
    }

    /// <summary>
    /// ⭐ The text of the headline shapes, EXACT. What the structure test cannot see — the carrier's name, the order of the operand blocks, the
    /// If's `{ } else { … }` for OrElse, where the body sits (first in a bottom-tested loop, #227) — is pinned here. The expected text is what the fix emits.
    /// </summary>
    private static readonly object[][] Exact =
    {
        new object[] { "W", "aa", "while (true) { __sc0 = P(\"a\", i < 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(__sc0)) break; i = i + 1; body = body + 1; }" },
        new object[] { "DW", "oe", "while (true) { __sc0 = P(\"a\", i < 1); if (__sc0) { } else { __sc0 = P(\"b\", i < 3); } if (!(__sc0)) break; i = i + 1; body = body + 1; }" },
        new object[] { "DU", "aa", "while (true) { __sc0 = P(\"a\", i >= 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (__sc0) break; i = i + 1; body = body + 1; }" },
        new object[] { "LW", "aa", "while (true) { i = i + 1; body = body + 1; __sc0 = P(\"a\", i < 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(__sc0)) break; }" },
        new object[] { "LU", "aa", "while (true) { i = i + 1; body = body + 1; __sc0 = P(\"a\", i >= 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (__sc0) break; }" },
        new object[] { "LU", "oe", "while (true) { i = i + 1; body = body + 1; __sc0 = P(\"a\", i >= 3); if (__sc0) { } else { __sc0 = P(\"b\", i < 0); } if (__sc0) break; }" },
        new object[] { "W", "iff", "while (true) { if (P(\"c\", (i % 2) == 0)) { __sc0 = P(\"a\", i < 3); } else { __sc0 = P(\"b\", i < 3); } if (!(__sc0)) break; i = i + 1; body = body + 1; }" },
        new object[] { "DU", "iff", "while (true) { if (P(\"c\", (i % 2) == 0)) { __sc0 = P(\"a\", i >= 3); } else { __sc0 = P(\"b\", i >= 3); } if (__sc0) break; i = i + 1; body = body + 1; }" },
        new object[] { "LW", "iff", "while (true) { i = i + 1; body = body + 1; if (P(\"c\", (i % 2) == 0)) { __sc0 = P(\"a\", i < 3); } else { __sc0 = P(\"b\", i < 3); } if (!(__sc0)) break; }" },
        new object[] { "W", "nt", "while (true) { __sc0 = P(\"a\", i >= 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(!__sc0)) break; i = i + 1; body = body + 1; }" },
        new object[] { "DU", "nt", "while (true) { __sc0 = P(\"a\", i < 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!__sc0) break; i = i + 1; body = body + 1; }" },
        new object[] { "W", "cx", "while (true) { __sc1 = P(\"a\", i < 2); if (__sc1) { __sc1 = P(\"b\", i < 9); } __sc0 = __sc1; if (__sc0) { } else { __sc2 = P(\"c\", i == 2); if (__sc2) { __sc2 = P(\"d\", i < 9); } __sc0 = __sc2; } if (!(__sc0)) break; i = i + 1; body = body + 1; }" },
        new object[] { "LU", "cx", "while (true) { i = i + 1; body = body + 1; __sc1 = P(\"a\", i >= 3); if (__sc1) { __sc1 = P(\"b\", i < 9); } __sc0 = __sc1; if (__sc0) { } else { __sc2 = P(\"c\", i == 1); if (__sc2) { __sc2 = P(\"d\", i > 9); } __sc0 = __sc2; } if (__sc0) break; }" },
    };

    [TestCaseSource(nameof(Exact))]
    public void TheHeadlineShapes_AreWrittenExactly(string form, string kind, string expected)
    {
        var source = LoopConditionProbes.Program(form, kind);

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
                Assert.That(Loop(emit(source)), Is.EqualTo(expected), $"{form}_{kind}, {name} passes");
        });
    }

    // ============================================================================================
    // 3. WHAT THE FIX LEAVES ALONE: a counted For.
    // ============================================================================================

    /// <summary>
    /// ⭐ A counted <c>For</c> is not this shape: its <c>To</c> bound sits in its condition block, and an <c>If()</c> there is computed ONCE before
    /// the loop — which is what VB does (C++ and MSIL re-evaluate it every iteration, #261). Pulling the For into <c>while (true)</c> would
    /// re-run the bound per iteration on C# too (mutant M6). Pinned: the bound is evaluated above the loop, the loop is `while (j &lt;= __sc0)`,
    /// and there is no `while (true)` and no `break` at all.
    /// </summary>
    [Test]
    public void ACountedFor_WithAnIfBound_IsUnchanged_TheBoundIsComputedBeforeTheLoop()
    {
        var source = LoopConditionProbes.o_for.Source;

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(source));
                Assert.That(flat, Does.Contain(
                    "j = 1; if (P(\"c\", lim > 0)) { __sc0 = lim; } else { __sc0 = 0; } while (j <= __sc0) { body = body + 1; lim = 1; j = j + 1; }"),
                    $"o_for, {name} passes:\n{flat}");
                Assert.That(Count(flat, @"\bwhile \(true\)"), Is.EqualTo(0), $"a For is never `while (true)` — {name}");
                Assert.That(Count(flat, @"\bbreak;"), Is.EqualTo(0), $"…and has no exit test — {name}");
                Assert.That(Count(flat, "\\bP\\(\""), Is.EqualTo(1), $"the bound's operand is written once (the call, not P's own declaration) — {name}");
            }
        });
    }

    /// <summary>A plain counted For (no control flow anywhere) is the same `while (i &lt;= n)` it always was.</summary>
    [Test]
    public void APlainCountedFor_IsUnchanged()
    {
        const string source = """
            Sub Main()
                Dim total As Integer = 0
                For k As Integer = 1 To 4
                    total = total + k
                Next
                Console.WriteLine(total)
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(source));
                Assert.That(Count(flat, @"\bwhile \(true\)"), Is.EqualTo(0), name);
                Assert.That(flat, Does.Match(@"while \(k <= 4\) \{ total = total \+ k; k = k \+ 1; \}"), $"{name}:\n{flat}");
            }
        });
    }

    // ============================================================================================
    // 4. THE LOOPS AROUND THE CONDITION: Exit, nesting, a Try, a method, per-iteration Dims, If() in every loop form.
    // ============================================================================================

    /// <summary>
    /// <c>Exit While</c> adds ITS break after the exit test; the exit test stays first (a condition run once per COMPLETED iteration, not once in all).
    /// </summary>
    [Test]
    public void ExitWhile_IsASecondBreak_AfterTheExitTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(LoopConditionProbes.x_W.Source));
                Assert.That(flat, Does.Contain(
                    "while (true) { __sc0 = P(\"a\", i < 5); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(__sc0)) break; i = i + 1; if (i == 3) { break; } body = body + 1; }"),
                    $"x_W, {name} passes:\n{flat}");
            }
        });
    }

    /// <summary>
    /// <c>Exit Do</c> from a Select Case cannot be a `break` (it would leave the C# switch): it is a `goto` to a label after the loop, and the label is
    /// written once. The loop around it is the new shape.
    /// </summary>
    [Test]
    public void ExitDoInASelectCase_IsAGotoToOneLabelAfterTheLoop()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(LoopConditionProbes.x_sel.Source));
                Assert.That(Count(flat, @"\bwhile \("), Is.EqualTo(1), name);
                Assert.That(flat, Does.Contain("if (!(__sc0)) break; i = i + 1; switch (i) { case 3: goto __exit_do0_end;"), $"x_sel, {name}:\n{flat}");
                Assert.That(Count(flat, @"__exit_do0_end: ;"), Is.EqualTo(1), $"one label, after the loop — {name}");
                Assert.That(flat.IndexOf("__exit_do0_end: ;", StringComparison.Ordinal), Is.GreaterThan(flat.LastIndexOf("body = body + 1; break; } }", StringComparison.Ordinal)),
                    $"the label follows the loop's closing brace — {name}");
            }
        });
    }

    /// <summary>Two nested loops: two `while (true)`, each with its OWN carrier and its own exit test; the inner one sits inside the outer, after the outer's exit test.</summary>
    [Test]
    public void NestedLoops_EachWriteTheirOwnConditionInsideTheirOwnWhileTrue()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(LoopConditionProbes.n_WW.Source));
                Assert.That(flat, Does.Contain(
                    "while (true) { __sc0 = P(\"a\", i < 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(__sc0)) break; j = 0; " +
                    "while (true) { __sc1 = P(\"c\", j < 2); if (__sc1) { __sc1 = P(\"d\", i < 9); } if (!(__sc1)) break; j = j + 1; body = body + 1; } " +
                    "i = i + 1; }"),
                    $"n_WW, {name} passes:\n{flat}");
            }
        });
    }

    /// <summary>
    /// ADR-0014: a body with a captured per-iteration Dim is `int x = __carry_x; try { … } finally { __carry_x = x; }`. The CONDITION is outside it
    /// (it must not capture the iteration's local), so the exit test is written BEFORE the copy-forward and the `try`.
    /// </summary>
    [Test]
    public void APerIterationDimBody_KeepsTheConditionOutsideItsTry()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(LoopConditionProbes.d_W.Source));
                Assert.That(flat, Does.Contain(
                    "while (true) { __sc0 = P(\"a\", i < 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(__sc0)) break; int x = __carry_x; try { i = i + 1; x = x + i; fs.Add(() => x); } finally { __carry_x = x; } }"),
                    $"d_W, {name} passes:\n{flat}");
            }
        });
    }

    /// <summary>A loop inside a Try keeps the whole `while (true)` inside the `try`; a loop in a class method is the same shape in the method.</summary>
    [Test]
    public void ALoopInATry_AndInAClassMethod_AreTheNewShapeInPlace()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var inTry = Flat(emit(LoopConditionProbes.t_in.Source));
                Assert.That(inTry, Does.Contain(
                    "try { while (true) { __sc0 = P(\"a\", i < 3); if (__sc0) { __sc0 = P(\"b\", i < 9); } if (!(__sc0)) break; i = i + 1; } Console.WriteLine(\"in try \" + i); }"),
                    $"t_in, {name} passes:\n{inTry}");

                // the module class is named differently by each route (Prog, Program, ReturnCoercionProbe): the call is what is pinned
                var inMethod = Regex.Replace(Flat(emit(LoopConditionProbes.c_meth.Source)), @"\b(Prog|Program|ReturnCoercionProbe)\.P\(", "P(");
                Assert.That(inMethod, Does.Contain(
                    "while (true) { __sc0 = P(\"a\", i < limit); if (__sc0) { } else { __sc0 = P(\"b\", i < 0); } if (!(__sc0)) break; i = i + 1; Total = Total + i; } return i;"),
                    $"c_meth, {name} passes:\n{inMethod}");
            }
        });
    }

    /// <summary>
    /// <c>Exit Do</c> inside a Try/Finally in the body: the exit test is still the first thing in the loop, and the `try` (with the Exit's break and the
    /// `finally`) is the body.
    /// </summary>
    [Test]
    public void ExitDoInsideATryFinally_FollowsTheExitTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(LoopConditionProbes.t_body.Source));
                Assert.That(flat, Does.Contain(
                    "while (true) { __sc0 = P(\"a\", i >= 5); if (__sc0) { } else { __sc0 = P(\"b\", i < 0); } if (__sc0) break; " +
                    "try { i = i + 1; if (i == 3) { break; } } finally { body = body + 1; } }"),
                    $"t_body, {name} passes:\n{flat}");
            }
        });
    }

    /// <summary>
    /// i4loop's three loops, each over an If(): `While`, `Do While` and `Loop Until` all become `while (true)` with the If's blocks inside and ONE exit
    /// test (the `Loop Until`'s after its body, #227), while its counted For (with If() bounds) keeps computing both bounds before its `while (j &lt;= __sc4)`.
    /// </summary>
    [Test]
    public void IfInEveryLoopForm_AndInAForBound_I4loop()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(UntypedConstAndConditionalProbes.i4loop.Source));
                Assert.That(Count(flat, @"\bwhile \(true\)"), Is.EqualTo(3), $"While, Do While and Loop Until — {name}:\n{flat}");
                Assert.That(Count(flat, @"\bbreak;"), Is.EqualTo(3), $"one exit test each — {name}");
                Assert.That(flat, Does.Contain("while (true) { if (i < 3) { __sc0 = true; } else { __sc0 = false; } if (!(__sc0)) break; Console.WriteLine(\"while \" + i); i = i + 1; }"), name);
                Assert.That(flat, Does.Contain("while (true) { k = k - 3; if (k < 0) { __sc2 = true; } else { __sc2 = k == 1; } if (__sc2) break; }"),
                    $"Loop Until (#227: the body first, then the condition, then the exit test — written once) — {name}");
                Assert.That(flat, Does.Contain(
                    "if (useTick) { __sc3 = 1; } else { __sc3 = 5; } j = __sc3; if (useTick) { __sc4 = 3; } else { __sc4 = 9; } while (j <= __sc4) {"),
                    $"the For's bounds are computed once, before its loop — {name}");
            }
        });
    }

    // ============================================================================================
    // 5. THE GUARD: the InvalidOperationException ("never reached the loop's own branch"; #227's "never reached its condition's own branch")
    //    must never fire.
    // ============================================================================================

    /// <summary>
    /// <c>GenerateStructuredBlockCore</c> throws when it opens a `while (true)` and the walk out of the condition never reaches the loop's own branch
    /// (it would leave the loop open — broken C#); so does <c>GenerateBottomTestedLoop</c> (#227) when the walk out of a bottom-tested loop's body
    /// never reaches its condition's own branch (the exit test, without which the `while (true)` hangs). That must NEVER happen for a program the
    /// compiler accepts: every probe goes through all three
    /// entry points here, and every emitted program is handed to Roslyn. ALL of them compile: l_fn (a loop inside a Function lambda) was the
    /// one that did not until #136 (CS1643: a lambda's body lost every block after its entry block, its return paths with them) and was
    /// skipped here; it is compiled like the rest now.
    /// </summary>
    [Test]
    public void TheNeverReachedGuard_DoesNotFire_OnAnyProbe_AndTheEmittedCSharpCompiles()
    {
        var corpus = LoopConditionProbes.All.Select(p => (p.Id, p.Source))
            .Append(("i4loop", UntypedConstAndConditionalProbes.i4loop.Source))
            .Concat(LoopConditionProbes.Forms.SelectMany(f => LoopConditionProbes.ShapeOnlyOneBlockKinds.Select(k => ($"{f}_{k}", LoopConditionProbes.Program(f, k)))))
            .ToList();
        Assert.That(corpus, Has.Count.EqualTo(56 + 1 + 15));

        var failures = new List<string>();
        var compiled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, source) in corpus)
            foreach (var (name, emit) in Pipelines)
            {
                string csharp;
                try { csharp = emit(source); }
                catch (InvalidOperationException ex) when (IsTheGuard(ex))
                {
                    failures.Add($"{id} ({name}): THE GUARD FIRED — {ex.Message}");
                    continue;
                }

                var errors = RoslynErrorsOnce(csharp, compiled);
                if (errors.Length > 0) failures.Add($"{id} ({name}): the emitted C# does not compile: {errors[0]}");
            }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// The two guards of this work: #256's (a pre-test loop's condition whose walk never reached the loop's own branch) and #227's (a bottom-tested
    /// loop's body that never reached its condition's own branch — `GenerateBottomTestedLoop`). Either leaves a `while (true)` that never ends.
    /// </summary>
    private static bool IsTheGuard(InvalidOperationException ex)
        => ex.Message.Contains("never reached the loop's own branch") || ex.Message.Contains("never reached its condition's own branch");

    /// <summary>
    /// The three entry points emit the SAME text but for the module class's name (Prog, Program, ReturnCoercionProbe), and Roslyn is the cost of
    /// these sweeps, so a text already compiled under another name is not compiled again.
    /// </summary>
    private static string[] RoslynErrorsOnce(string csharp, HashSet<string> compiled)
        => compiled.Add(Regex.Replace(Flat(csharp), @"\b(Prog|Program|ReturnCoercionProbe)\b", "X")) ? RoslynErrors(csharp) : Array.Empty<string>();

    private static string[] RoslynErrors(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(a.Location))
            .Cast<Microsoft.CodeAnalysis.MetadataReference>()
            .ToArray();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "LoopShape_" + Guid.NewGuid().ToString("N"),
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(csharp) },
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.ConsoleApplication));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
    }

    /// <summary>
    /// The guard and Roslyn over the STRESS conditions (`LoopConditionProbes.StressKinds`: a chain of three, Not over an OrElse, an If with an AndAlso arm,
    /// an If as an argument or inside a compare, Not If(…), an AndAlso over an OrElse, an If over an AndAlso), in all five loop forms and all three
    /// entry points. NOTHING RUNS: a shape the grid has no row for cannot hang this test, and still cannot make the walk open a loop it never closes
    /// or emit C# that does not compile. (The execution fixture RUNS W and LW over the same ten, against vbc.)
    /// </summary>
    [Test]
    public void TheNeverReachedGuard_DoesNotFire_OnTheStressConditions_AndTheCSharpCompiles()
    {
        var failures = new List<string>();
        var compiled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in LoopConditionProbes.Forms)
            foreach (var kind in LoopConditionProbes.StressKinds)
            {
                var source = LoopConditionProbes.Program(form, kind);
                foreach (var (name, emit) in Pipelines)
                {
                    string csharp;
                    try { csharp = emit(source); }
                    catch (InvalidOperationException ex) when (IsTheGuard(ex))
                    {
                        failures.Add($"{form}_{kind} ({name}): THE GUARD FIRED — {ex.Message}");
                        continue;
                    }

                    var errors = RoslynErrorsOnce(csharp, compiled);
                    if (errors.Length > 0) failures.Add($"{form}_{kind} ({name}): the emitted C# does not compile: {errors[0]}");
                    // and it is the new shape, not the stale one: a `while (<carrier>)` over a carrier nothing rewrites
                    if (Regex.IsMatch(Flat(csharp), @"\bwhile \(!?\(?!?__sc\d")) failures.Add($"{form}_{kind} ({name}): a `while (__scN)` over a carrier is the #256 shape");
                }
            }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    // ============================================================================================
    // 6. THE RULE THIS WORK ADDED, kept: no loop test runs emitted C# in the TEST HOST.
    // ============================================================================================

    /// <summary>
    /// ⛔⛔ The in-process runner has no timeout, and a C# loop that hangs there freezes the whole host. So no test file of THIS work may call it for
    /// a program: every C# run goes through <c>CSharpProcessRunner</c> (a child process, killed), directly or as <c>hangSafe: true</c>. This reads
    /// the other four files (code only, comments dropped) and fails on a call to the in-process runner — except the ONE equivalence test in
    /// <c>CSharpProcessRunnerTests</c>, which runs a program that is not a loop — and on an <c>AssertMatchesInEveryEntryPoint</c> call that is not
    /// hang-safe. (This file runs nothing: it only emits text.) A guard on the rule is cheaper than a frozen run.
    /// </summary>
    [Test]
    public void NoLoopTestRunsEmittedCSharpInTheTestHost()
    {
        var dir = Path.Combine(SampleSources.RepoRoot(), "VisualGameStudio.Tests", "Compiler");
        string[] files = { "LoopConditionReevaluationExecutionTests.cs", "LoopConditionProbes.cs", "CSharpProcessRunnerTests.cs", "BottomTestedLoopCSharpExecutionTests.cs" };

        static string Code(string path)
            => string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var inProcess = new[] { "RunEmittedCSharp(", "RunEmittedCSharpText(", "RunEmittedCSharpAggressive(", "RunsOnEveryBackend(", "RunsOnEveryBackendAggressive(", "TempExec.Run(" };
        var problems = new List<string>();
        foreach (var file in files)
        {
            var code = Code(Path.Combine(dir, file));
            foreach (var call in inProcess)
            {
                var found = Count(code, Regex.Escape(call));
                var allowed = file == "CSharpProcessRunnerTests.cs" && call == "RunEmittedCSharpText(" ? 1 : 0;
                if (found != allowed) problems.Add($"{file}: {found} call(s) of {call} (allowed {allowed})");
            }

            foreach (Match m in Regex.Matches(code, @"AssertMatchesInEveryEntryPoint\((?<args>[^;]*);"))
                if (!m.Groups["args"].Value.Contains("hangSafe: true"))
                    problems.Add($"{file}: an AssertMatchesInEveryEntryPoint call that is not hang-safe: {m.Value}");
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
