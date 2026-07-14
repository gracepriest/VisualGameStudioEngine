using BasicLang.Compiler;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 3 (C++ Phase 2): cross-language entry-point analysis. CountCppMains is
/// the D9 textual heuristic (comment/string-stripped scan for main/wmain/WinMain
/// DEFINITIONS); Apply enforces the spec rule — Exe needs exactly one entry
/// point across both languages (BL6011 on zero, BL6012 on many), Library needs
/// zero (BL6013). Enforced pre-link because 0/2-main linker errors don't parse
/// into clickable diagnostics.
/// </summary>
[TestFixture]
public class NativeEntryPointTests
{
    // ========================================================================
    // CountCppMains — the D9 textual heuristic
    // ========================================================================

    [TestCase("int main() { return 0; }", 1)]
    [TestCase("int main(int argc, char* argv[]) { }", 1)]
    [TestCase("auto main() -> int { }", 1)]
    [TestCase("int wmain(int argc, wchar_t** argv) { }", 1)]
    [TestCase("int WinMain(void*, void*, char*, int) { }", 1)]
    [TestCase("// int main() { }", 0)]
    [TestCase("/* int main() { } */", 0)]
    [TestCase("const char* s = \"int main()\";", 0)]
    [TestCase("void mainframe(); int domain();", 0)]           // word boundaries
    [TestCase("int main(); ", 0)]                              // declaration only (no body brace)
    [TestCase("int main() { } int wmain() { }", 2)]
    public void CountMains_TextualHeuristic(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // --- extras: stripper edge cases -----------------------------------

    // Escaped quote inside a string literal must not terminate the string early.
    [TestCase("const char* q = \"\\\"int main() { }\\\"\";", 0)]
    // Escaped backslash right before the CLOSING quote: the string is `path\\`
    // and the literal DOES end there — a naive stripper that treats `\\` + `"`
    // as an escaped quote swallows the real main that follows.
    [TestCase("const char* p = \"path\\\\\"; int main() { }", 1)]
    // Char literals (with and without escapes) are stripped without derailing the scan.
    [TestCase("char c = 'm'; char d = '\\''; int main() { }", 1)]
    // Block comment spanning multiple lines.
    [TestCase("/* line1\nint main() { }\nline2 */ void helper();", 0)]
    // Line comment terminated by EOF (no trailing newline) after a real main.
    [TestCase("int main() { } // int wmain() { }", 1)]
    // Declaration-only forms of the other entry-point names.
    [TestCase("int wmain(int argc, wchar_t** argv);", 0)]
    [TestCase("int WinMain(void*, void*, char*, int);", 0)]
    // Whitespace/newline between the parameter list and the body brace.
    [TestCase("int main(int argc,\n         char* argv[])\n{\n}", 1)]
    // Unterminated block comment at EOF hides everything after it.
    [TestCase("/* int main() { }", 0)]
    // Empty / null-ish input.
    [TestCase("", 0)]
    public void CountMains_TextualHeuristic_Extras(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // ========================================================================
    // Task 3 hardening: digit separators, raw strings, missing entry-point
    // forms (wWinMain/noexcept/try), and // line-continuation splicing. A
    // spec reviewer found these as real-world-plausible false BL6011/BL6012s
    // (BL6011/BL6012 are hard, build-blocking errors).
    // ========================================================================

    // --- Fix 1: C++14/17 digit separators must not be misread as char literals ---
    [TestCase("constexpr int N = 10'000; int main() { }", 1)]
    [TestCase("constexpr int N = 0xFF'FF; int main() { }", 1)]
    [TestCase("constexpr int N = 1'000'000; int main() { }", 1)]
    [TestCase("char c = 'a'; int main() { }", 1)]
    [TestCase("char c = '{'; int main() { }", 1)]
    [TestCase("wchar_t c = L'x'; int main() { }", 1)]
    [TestCase("auto c = u8'x'; int main() { }", 1)]
    [TestCase("char c = '\\''; int main() { }", 1)]
    public void CountMains_DigitSeparatorsAndCharLiterals(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // --- Fix 2: raw string literals must be scanned as a delimiter-matched unit ---
    [TestCase("auto q = R\"(say \"hello)\"; int main() { }", 1)]
    [TestCase("auto q = LR\"(x)\"; int main() { }", 1)]
    [TestCase("const char* s = R\"(int main(){})\"; ", 0)]
    [TestCase("auto q = R\"xyz(int main(){})xyz\"; int main() { }", 1)]
    public void CountMains_RawStringLiterals(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // --- Fix 3: missing entry-point forms (wWinMain, noexcept, function-try-block) ---
    [TestCase("int wWinMain(void*, void*, char*, int) { }", 1)]
    [TestCase("int main() noexcept { }", 1)]
    [TestCase("int main() noexcept(true) { }", 1)]
    [TestCase("int main() try { }", 1)]
    [TestCase("int main() noexcept;", 0)]
    public void CountMains_NewEntryPointForms(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // --- Fix 4: a trailing backslash at the end of a // comment line splices ---
    // the next physical line into the comment (translation phase 2), hiding it.
    [Test]
    public void CountMains_LineCommentSplice_HidesSplicedMain()
    {
        var source = "// see C:\\build\\\nint main() { }";
        Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(0));
    }

    [Test]
    public void CountMains_LineComment_NoTrailingBackslash_DoesNotHideNextLine()
    {
        var source = "// plain comment\nint main() { }";
        Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(1));
    }

    // ========================================================================
    // Task 3 hardening, review follow-up: balanced-paren noexcept, bounded
    // char-literal desync, and precedence-hazard pins.
    // ========================================================================

    // --- Follow-up 1: noexcept condition may itself nest parens ---
    [TestCase("int main() noexcept(noexcept(foo())) { }", 1)]
    [TestCase("int main() noexcept(true) { }", 1)]
    [TestCase("int main() noexcept(noexcept(x));", 0)]   // declaration: no body brace
    public void CountMains_NoexceptNestedParens(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // --- Follow-up 2: a mis-classified char-literal quote must not swallow
    // a real main on a LATER line. `u8'a'` closing quote is seen as a char-
    // literal opener by the memoryless separator rule; without the newline
    // cap in char-literal consumption it runs to EOF and hides the main.
    [Test]
    public void CountMains_CharLiteralDesync_IsBoundedToOneLine()
    {
        var source = "auto c = u8'a';\nint main() { }";
        Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(1));
    }

    // The coordinator's `0xa'A` form does not actually desync (both quote
    // neighbors are hex digits, so it's classified as a separator), but pin
    // it too: the following-line main is counted regardless.
    [Test]
    public void CountMains_HexNeighborQuote_DoesNotSwallowNextLineMain()
    {
        var source = "auto x = 0xa'A;\nint main() { }";
        Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(1));
    }

    // --- Follow-up 4: precedence hazards (currently pass; pin them) ---
    // Comment look-alikes INSIDE a raw string are text, not comments/code.
    [TestCase("const char* s = R\"(// int main(){} /* int main(){} */)\"; int main(){}", 1)]
    // An `R\"` sequence INSIDE an ordinary string literal is not a raw string.
    [TestCase("const char* s = \"x R\\\"(y)\\\" z\"; int main(){}", 1)]
    // Word-boundary: names merely containing WinMain/wWinMain are not entry points.
    [TestCase("int MyWinMainWrapper() { }", 0)]
    [TestCase("int wWinMainish() { }", 0)]
    public void CountMains_PrecedenceHazards(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    // ========================================================================
    // Apply — the Exe/Library entry-point rule
    // ========================================================================

    [Test]
    public void Rule_Exe_ZeroMains_IsBL6011()
    {
        var d = NativeEntryPoints.Apply(isExe: true, basicLangMainCount: 0,
            cppMains: new List<(string file, int count)>());
        Assert.That(d, Has.Count.EqualTo(1));
        Assert.That(d[0].Code, Is.EqualTo("BL6011"));
    }

    [Test]
    public void Rule_Exe_BasicLangMainPlusCppMain_IsBL6012_ListingCandidates()
    {
        var d = NativeEntryPoints.Apply(true, 1, new() { ("main.cpp", 1) });
        Assert.That(d[0].Code, Is.EqualTo("BL6012"));
        Assert.That(d[0].Message, Does.Contain("main.cpp").And.Contain("Main"));
    }

    [Test]
    public void Rule_Exe_ExactlyOneEither_IsClean()
    {
        Assert.That(NativeEntryPoints.Apply(true, 1, new()), Is.Empty);
        Assert.That(NativeEntryPoints.Apply(true, 0, new() { ("main.cpp", 1) }), Is.Empty);
    }

    [Test]
    public void Rule_Library_AnyMain_IsBL6013()
    {
        Assert.That(NativeEntryPoints.Apply(false, 1, new())[0].Code, Is.EqualTo("BL6013"));
        Assert.That(NativeEntryPoints.Apply(false, 0, new() { ("a.cpp", 1) })[0].Code, Is.EqualTo("BL6013"));
        Assert.That(NativeEntryPoints.Apply(false, 0, new()), Is.Empty);
    }

    // --- extras: pinned message/shape contract --------------------------

    [Test]
    public void Rule_BL6011_SaysWhatWasSearched_AndHasErrorShape()
    {
        var d = NativeEntryPoints.Apply(true, 0, new())[0];
        Assert.That(d.Message, Does.Contain("Sub Main").And.Contain("main").And.Contain("wmain").And.Contain("WinMain").And.Contain("wWinMain"));
        Assert.That(d.IsWarning, Is.False);
        Assert.That(d.FilePath, Is.Empty);
        Assert.That(d.Line, Is.EqualTo(0));
        Assert.That(d.Column, Is.EqualTo(0));
    }

    // Contract: a single file containing N>=2 mains is listed ONCE with its
    // count as "<file>: main (xN)"; count==1 files are listed as "<file>: main".
    [Test]
    public void Rule_Exe_TwoMainsInOneCppFile_IsBL6012_ListedOnceWithCount()
    {
        var d = NativeEntryPoints.Apply(true, 0, new() { ("game.cpp", 2) });
        Assert.That(d, Has.Count.EqualTo(1));
        Assert.That(d[0].Code, Is.EqualTo("BL6012"));
        Assert.That(d[0].Message, Does.Contain("game.cpp: main (x2)"));
        Assert.That(d[0].FilePath, Is.EqualTo("game.cpp"));
    }

    // Contract: candidate order is deterministic — BasicLang Main first, then
    // C++ files in the order the caller supplied them (no re-sorting).
    [Test]
    public void Rule_BL6012_CandidateList_IsDeterministic_BasicLangFirst_ThenCallerOrder()
    {
        var d = NativeEntryPoints.Apply(true, 1, new() { ("b.cpp", 1), ("a.cpp", 1) });
        var msg = d[0].Message;
        var basicIdx = msg.IndexOf("Main (BasicLang)", StringComparison.Ordinal);
        var bIdx = msg.IndexOf("b.cpp: main", StringComparison.Ordinal);
        var aIdx = msg.IndexOf("a.cpp: main", StringComparison.Ordinal);
        Assert.That(basicIdx, Is.GreaterThanOrEqualTo(0));
        Assert.That(bIdx, Is.GreaterThan(basicIdx));
        Assert.That(aIdx, Is.GreaterThan(bIdx));
        // FilePath points at the first C++ candidate so the Error List has a
        // clickable target even when BasicLang's Main is listed first.
        Assert.That(d[0].FilePath, Is.EqualTo("b.cpp"));
    }

    [Test]
    public void Rule_Library_BL6013_ListsCandidates_AndZeroCountFilesAreIgnored()
    {
        var d = NativeEntryPoints.Apply(false, 1, new() { ("empty.cpp", 0), ("a.cpp", 1) });
        Assert.That(d, Has.Count.EqualTo(1));
        Assert.That(d[0].Code, Is.EqualTo("BL6013"));
        Assert.That(d[0].Message, Does.Contain("Main (BasicLang)").And.Contain("a.cpp: main"));
        Assert.That(d[0].Message, Does.Not.Contain("empty.cpp"));
        Assert.That(d[0].FilePath, Is.EqualTo("a.cpp"));

        // Zero-count entries alone are not entry points at all.
        Assert.That(NativeEntryPoints.Apply(false, 0, new() { ("empty.cpp", 0) }), Is.Empty);
    }

    // --- review follow-up 3: fold repeated BasicLang mains like the C++ side ---
    [Test]
    public void Rule_Exe_TwoBasicLangMains_IsBL6012_FoldedWithCount()
    {
        var d = NativeEntryPoints.Apply(true, 2, new());
        Assert.That(d, Has.Count.EqualTo(1));
        Assert.That(d[0].Code, Is.EqualTo("BL6012"));
        Assert.That(d[0].Message, Does.Contain("Main (BasicLang) (x2)"));
        Assert.That(d[0].Message, Does.Contain("(2)")); // total entry-point count
        // Folded — the label appears exactly once, not repeated.
        Assert.That(d[0].Message.IndexOf("Main (BasicLang)", StringComparison.Ordinal),
            Is.EqualTo(d[0].Message.LastIndexOf("Main (BasicLang)", StringComparison.Ordinal)));
    }

    [Test]
    public void Rule_Exe_TwoBasicLangMainsPlusCpp_IsBL6012_TotalThree()
    {
        var d = NativeEntryPoints.Apply(true, 2, new() { ("game.cpp", 1) });
        Assert.That(d[0].Code, Is.EqualTo("BL6012"));
        Assert.That(d[0].Message, Does.Contain("Main (BasicLang) (x2)").And.Contain("game.cpp: main"));
        Assert.That(d[0].Message, Does.Contain("(3)")); // total across both languages
    }

    // --- review follow-up 5: duplicate C++ file paths are merged (counts summed) ---
    [Test]
    public void Rule_DuplicateCppFileEntries_AreMergedByPath_WithSummedCount()
    {
        var d = NativeEntryPoints.Apply(true, 0, new() { ("a.cpp", 1), ("a.cpp", 1) });
        Assert.That(d, Has.Count.EqualTo(1));
        Assert.That(d[0].Code, Is.EqualTo("BL6012"));
        Assert.That(d[0].Message, Does.Contain("a.cpp: main (x2)"));
        Assert.That(d[0].Message, Does.Contain("(2)")); // total
        // Listed exactly once after merge.
        Assert.That(d[0].Message.IndexOf("a.cpp", StringComparison.Ordinal),
            Is.EqualTo(d[0].Message.LastIndexOf("a.cpp", StringComparison.Ordinal)));
        Assert.That(d[0].FilePath, Is.EqualTo("a.cpp"));
    }

    // ========================================================================
    // Probe (recon open question, PINNED): what does the BasicLang FRONTEND do
    // with a duplicate Sub Main split across two .bas files on the cpp backend?
    //
    // OBSERVED (Jul 13 2026): it SILENTLY SUCCEEDS — no duplicate-definition
    // semantic error is reported, and the combined IR contains exactly ONE
    // function named Main (the combiner drops the duplicate, first-wins).
    // This silent pass is exactly why Task 4 must count BasicLang Mains from
    // the PER-UNIT IRs (one per source file, pre-merge) rather than from the
    // combined IR — by combine time the second Main has already vanished and
    // BL6012 could never fire for the two-.bas-Mains case.
    // ========================================================================

    [Test]
    public void Probe_DuplicateSubMain_AcrossTwoBasFiles_FrontendSilentlySucceeds_CombinerKeepsOneMain()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bl-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var a = Path.Combine(tempDir, "A.bas");
            var b = Path.Combine(tempDir, "B.bas");
            File.WriteAllText(a, "Sub Main()\n    Dim x As Integer = 1\nEnd Sub\n");
            File.WriteAllText(b, "Sub Main()\n    Dim y As Integer = 2\nEnd Sub\n");

            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(new[] { a, b });

            Assert.That(result.Success, Is.True,
                "expected the frontend to silently accept duplicate cross-file Mains, got: "
                + string.Join(" | ", result.AllErrors));
            Assert.That(result.AllErrors, Is.Empty);
            Assert.That(result.CombinedIR, Is.Not.Null);
            Assert.That(result.CombinedIR!.Functions.Count(f => f.Name == "Main"), Is.EqualTo(1),
                "combined IR was expected to keep exactly one Main (duplicate dropped by the merge)");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
