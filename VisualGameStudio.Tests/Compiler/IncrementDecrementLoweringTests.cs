using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #141 — the SHAPE of the C# that `++` and `--` are lowered to. FAST: it emits and reads text and RUNS NOTHING, so a regression of
//  the fix cannot hang it (the hang, and the answers, are IncrementDecrementExecutionTests' job, through the time-limited runner).
//
//  ⭐ WHY TEXT TESTS EXIST FOR THIS ONE. The runs prove the answer on four backends; these kill the three mutants of the fix in the
//  fast tier, cheaply: `--` that adds (M2), a postfix that yields the NEW value (M1) and a C# loop condition that stores but is
//  written `while (cond)` with its stores hoisted above the loop (M3, which HANGS when run).
//
//  Three entry points that stay in process: the standard passes (what the CLI does), the aggressive passes (`--optimize`) and
//  `CompileProjectFiles` (the IDE / Release project build). Every case reads all three. The operand is a PARAMETER, so the folder has
//  nothing to propagate into it and the writes stay in the text.
// ================================================================================================

/// <summary>
/// #141: a statement `x++` is `x += 1` byte for byte; a used postfix value is read BEFORE the store and a used prefix value is the
/// one stored; a loop condition that stores is written `while (true)` with the condition inside.
/// </summary>
[TestFixture]
public class IncrementDecrementLoweringTests
{
    private static readonly (string Name, Func<string, string> Emit)[] Pipelines =
    {
        ("standard", ReturnCoercionTests.EmitCSharpForTest),
        ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest),
        ("project", source => TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source)),
    };

    private static string Flat(string csharp) => LoopConditionEmissionShapeTests.Flat(csharp);

    /// <summary>The body of the one function <paramref name="name"/>, from its opening brace to the next function (or the end).</summary>
    private static string BodyOf(string flat, string name)
    {
        var start = flat.IndexOf($" {name}(int x)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{name} is missing:\n{flat}");
        var end = flat.IndexOf("public static", start + 1, StringComparison.Ordinal);
        return end < 0 ? flat.Substring(start) : flat.Substring(start, end - start);
    }

    // ============================================================================================
    // 1. A STATEMENT `x++` IS `x += 1`, BYTE FOR BYTE (kills M2: `--` that adds).
    // ============================================================================================

    /// <summary>
    /// The four statement spellings against `x += 1` / `x -= 1`, in the same order: the emitted C# is identical, so the operator does
    /// exactly what the compound assignment does — it writes `x`, once, with the right sign. A dropped statement (the original bug: a
    /// `x++` line printed nothing), a `--` that adds, and a carrier minted for a value nobody reads each make the two differ.
    /// </summary>
    [Test]
    public void AStatementIncrement_LowersExactlyAs_PlusEqualsOne()
    {
        const string incDec = """
            Function F(x As Integer) As Integer
                x++
                ++x
                x--
                --x
                x--
                Return x
            End Function

            Sub Main()
                Console.WriteLine(F(5))
            End Sub
            """;
        const string compound = """
            Function F(x As Integer) As Integer
                x += 1
                x += 1
                x -= 1
                x -= 1
                x -= 1
                Return x
            End Function

            Sub Main()
                Console.WriteLine(F(5))
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
                Assert.That(Flat(emit(incDec)), Is.EqualTo(Flat(emit(compound))), $"`x++` must emit what `x += 1` emits, {name} passes");
        });
    }

    // ============================================================================================
    // 2. A USED VALUE: the POSTFIX value is read BEFORE the store, the PREFIX value is the one stored (kills M1 and M2).
    // ============================================================================================

    /// <summary>
    /// `y = x++` is `__inc = x; x = x + 1; y = __inc`, and `y = ++x` is `__inc = x + 1; x = __inc; y = __inc`; `--` the same with `- 1`.
    /// Both halves of each shape are asserted: WHAT the carrier holds and WHERE the store of `x` comes. Master wrote `t = ++x` for every
    /// one of them, so a postfix gave the new value.
    /// </summary>
    [Test]
    public void AUsedValue_ReadsTheOldOne_ForPostfix_AndStoresTheNewOne_ForPrefix()
    {
        const string source = """
            Function Post(x As Integer) As Integer
                Dim y As Integer = x++
                Return y * 100 + x
            End Function

            Function Pre(x As Integer) As Integer
                Dim y As Integer = ++x
                Return y * 100 + x
            End Function

            Function PostDec(x As Integer) As Integer
                Dim y As Integer = x--
                Return y * 100 + x
            End Function

            Function PreDec(x As Integer) As Integer
                Dim y As Integer = --x
                Return y * 100 + x
            End Function

            Sub Main()
                Console.WriteLine(Post(5) + Pre(5) + PostDec(5) + PreDec(5))
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(source));

                // postfix: the carrier is read from `x` FIRST, then `x` is stored, then the carrier is what `y` gets
                Assert.That(BodyOf(flat, "Post"), Does.Match(@"(__inc\d+) = x; x = x \+ 1; y = \1;"), $"x++ — {name} passes:\n{flat}");
                Assert.That(BodyOf(flat, "PostDec"), Does.Match(@"(__inc\d+) = x; x = x - 1; y = \1;"), $"x-- — {name} passes:\n{flat}");

                // prefix: the carrier IS the stored value, and `y` gets it
                Assert.That(BodyOf(flat, "Pre"), Does.Match(@"(__inc\d+) = x \+ 1; x = \1; y = \1;"), $"++x — {name} passes:\n{flat}");
                Assert.That(BodyOf(flat, "PreDec"), Does.Match(@"(__inc\d+) = x - 1; x = \1; y = \1;"), $"--x — {name} passes:\n{flat}");
            }
        });
    }

    // ============================================================================================
    // 3. A LOOP CONDITION THAT STORES IS `while (true)` WITH THE CONDITION INSIDE (kills M3, without spinning).
    // ============================================================================================

    /// <summary>
    /// `Do While j-- > 0`: the condition is ONE block, but it STORES (`j` and the carrier). Written `while (cond)` its statements land once,
    /// above the loop, and the loop tests a stale carrier forever — the HANG. So it is `while (true) { …condition…; if (!(c)) break; …body… }`:
    /// one `while`, no store of `j` outside it, one exit test, the store before the exit test.
    /// </summary>
    [Test]
    public void ALoopConditionThatStores_IsWrittenWhileTrue_WithTheStoreInsideTheLoop()
    {
        const string source = """
            Function CountDown(j As Integer) As Integer
                Dim c As Integer = 0
                Do While j-- > 0
                    c++
                Loop
                Return c
            End Function

            Sub Main()
                Console.WriteLine(CountDown(3))
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var (name, emit) in Pipelines)
            {
                var flat = Flat(emit(source));
                var open = flat.IndexOf("while (true) {", StringComparison.Ordinal);

                Assert.That(Regex.Matches(flat, @"\bwhile \(").Count, Is.EqualTo(1), $"one loop, one `while` — {name} passes:\n{flat}");
                Assert.That(open, Is.GreaterThanOrEqualTo(0), $"the loop is `while (true)`, not `while (cond)` over a stale carrier — {name} passes:\n{flat}");
                if (open < 0) continue;

                var before = flat.Substring(0, open);
                var inside = flat.Substring(open);
                var exit = inside.IndexOf("break;", StringComparison.Ordinal);

                Assert.That(before, Does.Not.Contain("j = j - 1"), $"the store of `j` is NOT written once above the loop — {name} passes:\n{flat}");
                Assert.That(Regex.Matches(inside, "break;").Count, Is.EqualTo(1), $"exactly one exit test — {name} passes:\n{flat}");
                Assert.That(exit, Is.GreaterThan(0));
                Assert.That(inside.Substring(0, Math.Max(exit, 0)), Does.Contain("j = j - 1"), $"the store of `j` runs on every test, BEFORE the exit test — {name} passes:\n{flat}");
            }
        });
    }
}
