using System.Collections.Generic;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Form-designer menu-editor defects (test-writer pass, owner report via the real IDE): "&amp;
/// does not put an underline on the first letter of the word." <see cref="FormAccelerator"/> is the
/// shared, pure answer to "what does an accelerator-marked caption display as, and which character
/// is underlined" — called by BOTH <see cref="FormAssetEmitter"/> (which used to strip accelerators
/// with its own private regex) and <c>FormCanvasControl</c> (whose <c>DrawControl</c> and
/// <c>FormCanvasTransform.Caption</c>/<c>CellWidth</c> now resolve an item's caption through it).
///
/// <para>⛔ NO LONGER RED BY DESIGN: <see cref="FormAccelerator.Display"/> is now the real WinForms
/// rule (the implementer landed it). The cases below pin that rule and guard it against regression —
/// <see cref="Display_MatchesTheOldRegexRule_OverEveryShortStringUpToLength5"/> is the proof the
/// implementer asked for, that the new shared answer is byte-identical to the emitter's old private
/// regex over an exhaustive short-string corpus, so no already-shipped page's caption text changes.
/// </para>
/// </summary>
[TestFixture]
public class FormAcceleratorTests
{
    // ==================================================================
    // A — the display/underline contract, cases from the task brief
    // ==================================================================

    [TestCase("&Open", "Open", 0)]
    [TestCase("E&xit", "Exit", 1)]
    [TestCase("Save && Quit", "Save & Quit", -1)]
    [TestCase("&&File", "&File", -1)]
    [TestCase("Plain", "Plain", -1)]
    [TestCase("&", "", -1)]
    [TestCase("a&", "a", -1)]
    [TestCase("&&&x", "&x", 1)]
    public void Display_MatchesTheWinFormsAcceleratorRule(string input, string expectedDisplay, int expectedUnderline)
    {
        var (display, underline) = FormAccelerator.Display(input);

        Assert.Multiple(() =>
        {
            Assert.That(display, Is.EqualTo(expectedDisplay),
                $"Display(\"{input}\") text — got \"{display}\"");
            Assert.That(underline, Is.EqualTo(expectedUnderline),
                $"Display(\"{input}\") underline index — got {underline}");
        });
    }

    // ==================================================================
    // B — the emitter and the canvas must never be free to disagree again
    // ==================================================================

    /// <summary>
    /// ⛔⛔ The MIRRORED-PAIR defect this repo keeps hitting: <c>FormAssetEmitter.AppendControl</c>
    /// strips accelerators with its OWN regex (<c>Regex.Replace(text, "&amp;(&amp;?)", "$1")</c>,
    /// ~line 229) while the canvas draws <c>Text</c> verbatim. Both must resolve to
    /// <see cref="FormAccelerator.Display"/>'s answer, or the two can drift again exactly as they
    /// have. Today the emitter's OWN regex already strips "&amp;Open..." down to "Open..." (correct,
    /// by luck) while the stub returns the raw text unchanged — so this fails on the MISMATCH, not on
    /// a missing feature, which is exactly the drift being pinned.
    /// </summary>
    [Test]
    public void WebEmitterOutput_ForAnItemsCaption_MatchesFormAcceleratorDisplay()
    {
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open..."/>
                </MenuStrip>
              </Controls>
            </WebForm>
            """);

        var html = FormAssetEmitter.Html(model, "Site.js");
        var expected = FormAccelerator.Display("&Open...").Display;

        Assert.That(html, Does.Contain(">" + System.Net.WebUtility.HtmlEncode(expected) + "<"),
            "the web emitter's rendered caption must equal FormAccelerator.Display's text exactly — " +
            $"expected the emitter to contain \">{System.Net.WebUtility.HtmlEncode(expected)}<\"");
    }

    // ==================================================================
    // C — the differential proof: byte-identical to the emitter's OLD private regex
    // ==================================================================

    /// <summary>
    /// ⛔⛔ The implementer's own request: prove <see cref="FormAccelerator.Display"/>'s output is
    /// BYTE-IDENTICAL to the web emitter's OLD private rule
    /// (<c>Regex.Replace(text, "&amp;(&amp;?)", "$1")</c>) over an EXHAUSTIVE corpus — every string up
    /// to length 5 built from the alphabet <c>{"&amp;", "a", "b"}</c> (3 + 9 + 27 + 81 + 243 = 363
    /// strings, plus the empty string) — never a handful of hand-picked cases that could miss a corner
    /// the old regex and the new rule disagree on. Passing this is what lets every already-emitted
    /// page's caption text be trusted to be unchanged now that the emitter calls the shared rule
    /// instead of its own regex.
    ///
    /// <para>⚠ This test is deliberately ABOUT the DISPLAY TEXT only, not the underline index — the
    /// old regex never computed one, so there is nothing on that side to differ against.</para>
    /// </summary>
    [Test]
    public void Display_MatchesTheOldRegexRule_OverEveryShortStringUpToLength5()
    {
        var failures = new List<string>();

        foreach (var input in AllStringsUpToLength(5, "&", "a", "b"))
        {
            var oldDisplay = Regex.Replace(input, "&(&?)", "$1");
            var (newDisplay, _) = FormAccelerator.Display(input);

            if (newDisplay != oldDisplay)
            {
                failures.Add($"Display(\"{input}\") = \"{newDisplay}\", old regex = \"{oldDisplay}\"");
            }
        }

        Assert.That(failures, Is.Empty,
            "FormAccelerator.Display must be byte-identical to the emitter's old regex over every " +
            "short string, or some already-emitted page's caption text changes silently:\n" +
            string.Join("\n", failures));
    }

    /// <summary>Every string of length 0..<paramref name="maxLength"/> over <paramref name="alphabet"/>.</summary>
    private static IEnumerable<string> AllStringsUpToLength(int maxLength, params string[] alphabet)
    {
        yield return "";

        var current = new List<string> { "" };
        for (var length = 1; length <= maxLength; length++)
        {
            var next = new List<string>();
            foreach (var prefix in current)
            {
                foreach (var symbol in alphabet)
                {
                    var s = prefix + symbol;
                    next.Add(s);
                    yield return s;
                }
            }

            current = next;
        }
    }

    // ==================================================================
    // helpers (copied from FormStripEmissionTests' own pattern)
    // ==================================================================

    private static FormDocument Model(string xml, string name = "F.blwebform")
    {
        var file = FormDocumentReader.Read(System.IO.Path.Combine("C:", "forms", name), xml);
        Assert.That(file.IsRefused, Is.False,
            "fixture refused: " + string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        return file.Model;
    }
}
