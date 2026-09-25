using System.Text.RegularExpressions;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 24, commit 24c, Task 16 Step 1 — the web emitter's chrome placement, wrappers, roles,
/// no-tabindex and accelerator-stripping (spec §4, §5).
///
/// <para>⛔ RED until Task 16 Step 3 makes <see cref="FormAssetEmitter"/> place-aware. Today
/// <c>Html</c> appends every control — strip, item or ordinary — inside
/// <c>&lt;div class="vgs-form"&gt;</c>, in document order, with an UNCONDITIONAL
/// <c>tabindex="{control.TabIndex}"</c> on every element and no support at all for
/// <c>HtmlChildrenWrapper</c>, <c>HtmlRole</c> or per-kind <c>WebCss</c>; <c>Css</c> never reads
/// <c>WebCss</c> at all. None of that is in dispute — it is read directly off
/// <c>FormAssetEmitter.cs</c> as it stands after Task 15.</para>
///
/// <para><b>The fixture is the WEB twin of <c>FormStripDocumentTests.WithStrips</c></b> — same
/// element grammar (Id, Dock, Text, the MenuStrip/ToolStrip/StatusStrip/Tool­Strip* nesting), a
/// <c>&lt;WebForm&gt;</c> root instead of <c>&lt;Form&gt;</c>, and <c>Col</c>/<c>Row</c> instead of
/// <c>X</c>/<c>Y</c> for the one Positioned control (spec §3: the two formats share the element
/// grammar and diverge only in layout vocabulary). It is built and READ through
/// <see cref="FormDocumentReader"/> — already Place-aware since Tasks 13–15 — rather than a
/// hand-built <c>FormControl</c> graph, so the model under test is exactly what a real
/// <c>.blwebform</c> produces: null Geometry and forced-zero TabIndex on every strip/item, Dock as
/// a Property, document-order Children.</para>
/// </summary>
[TestFixture]
public class FormStripEmissionTests
{
    /// <summary>
    /// The web twin of <c>FormStripDocumentTests.WithStrips</c>. Deliberately carries NO
    /// <c>ToolTipText</c> on <c>tsbOpen</c> — that is exercised by its own minimal fixture in
    /// <see cref="Web_ToolTipTextBecomesTitle"/> so the exact-attribute-string assertions here
    /// don't have to account for it.
    /// </summary>
    private const string WebWithStrips = """
        <WebForm Name="MainForm" Version="1">
          <Controls>
            <MenuStrip Id="menuStrip1" Dock="Top">
              <ToolStripMenuItem Id="mnuFile" Text="&amp;File">
                <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open..."/>
                <ToolStripSeparator Id="sep1"/>
                <ToolStripMenuItem Id="mnuExit" Text="E&amp;xit"/>
              </ToolStripMenuItem>
            </MenuStrip>
            <ToolStrip Id="toolStrip1" Dock="Top">
              <ToolStripButton Id="tsbOpen" Text="Open"/>
            </ToolStrip>
            <Button Id="btnGo" Text="Go" Col="0" Row="0" TabIndex="0"/>
            <StatusStrip Id="statusStrip1" Dock="Bottom">
              <ToolStripStatusLabel Id="lblStatus" Text="Ready"/>
            </StatusStrip>
          </Controls>
        </WebForm>
        """;

    private static FormFile Read(string xml, string name = "F.blwebform") =>
        FormDocumentReader.Read(Path.Combine("C:", "forms", name), xml);

    private static FormDocument Model(string xml, string name = "F.blwebform")
    {
        var file = Read(xml, name);
        Assert.That(file.IsRefused, Is.False,
            "fixture refused: " + string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        return file.Model;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>
    /// Task 24, commit 24c, Task 15 Step 1 — the region writer's host verb, document order and
    /// <c>Me.MainMenuStrip</c> (spec §5). Copied verbatim from
    /// <see cref="FormComponentEmissionTests"/>'s own <c>Emit</c> helper.
    /// </summary>
    private static string Emit(FormDocument doc)
    {
        var scaffold = FormScaffolder.Create("F", doc.Target).CodeText;
        var written = RegionWriter.Write("F.bas", scaffold, doc, "F" + doc.FileExtension);
        Assert.That(written.Refused, Is.False, string.Join("; ", written.Diagnostics.Select(d => d.Format())));
        return written.Text;
    }

    // ==================================================================
    // Task 15 — the region writer: host verb, document order, MainMenuStrip
    // ==================================================================

    [Test]
    public void WinForms_ItemsAreAddedWithTheHostVerb_InDocumentOrder()
    {
        var model = Model(FormStripDocumentTests.WithStrips, "MainForm.blform");
        var code = Emit(model);

        Assert.Multiple(() =>
        {
            // ⚠ Asserted with IndexOf comparisons, not one contiguous string — GenerateInit puts
            // newlines and indentation between fragments (RegionWriter.cs), exactly like the web
            // emitter does in FormAssetEmitter.
            var openAdd = code.IndexOf("mnuFile.DropDownItems.Add(mnuOpen)", StringComparison.Ordinal);
            var sepAdd = code.IndexOf("mnuFile.DropDownItems.Add(sep1)", StringComparison.Ordinal);
            var exitAdd = code.IndexOf("mnuFile.DropDownItems.Add(mnuExit)", StringComparison.Ordinal);
            var hostAdd = code.IndexOf("menuStrip1.Items.Add(mnuFile)", StringComparison.Ordinal);

            Assert.That(openAdd, Is.GreaterThanOrEqualTo(0), "mnuFile.DropDownItems.Add(mnuOpen) was not emitted");
            Assert.That(sepAdd, Is.GreaterThan(openAdd),
                "sep1 must be added after mnuOpen — items are ordered, not layered");
            Assert.That(exitAdd, Is.GreaterThan(sepAdd),
                "mnuExit must be added after sep1 — items are ordered, not layered");
            Assert.That(hostAdd, Is.GreaterThan(exitAdd),
                "mnuFile's own subtree is built before menuStrip1.Items.Add(mnuFile) runs");

            Assert.That(code, Does.Not.Contain("Controls.Add(mnuFile)"),
                "an item's host verb is DropDownItems.Add/Items.Add — never the plain-control Controls.Add");
            Assert.That(code, Does.Not.Contain("Controls.Add(mnuOpen)"),
                "an item's host verb is DropDownItems.Add/Items.Add — never the plain-control Controls.Add");
        });
    }

    [Test]
    public void WinForms_StripsAreAddedToTheFormReversed_AndMainMenuStripFollows()
    {
        var model = Model(FormStripDocumentTests.WithStrips, "MainForm.blform");
        var code = Emit(model);

        Assert.Multiple(() =>
        {
            var statusAdd = code.IndexOf("Me.Controls.Add(statusStrip1)", StringComparison.Ordinal);
            var toolAdd = code.IndexOf("Me.Controls.Add(toolStrip1)", StringComparison.Ordinal);
            var menuAdd = code.IndexOf("Me.Controls.Add(menuStrip1)", StringComparison.Ordinal);
            var mainMenu = code.IndexOf("Me.MainMenuStrip = menuStrip1", StringComparison.Ordinal);

            Assert.That(statusAdd, Is.GreaterThanOrEqualTo(0), "statusStrip1 was never added to the form");
            Assert.That(toolAdd, Is.GreaterThan(statusAdd),
                "top-level siblings are added to the form in REVERSE document order — statusStrip1 " +
                "(documented last) must be added first");
            Assert.That(menuAdd, Is.GreaterThan(toolAdd),
                "menuStrip1 (documented first) must be added to the form LAST of the three strips");
            Assert.That(mainMenu, Is.GreaterThan(menuAdd),
                "Me.MainMenuStrip is set AFTER the last Controls.Add — the strip must be populated " +
                "and parented before the form points at it");

            Assert.That(CountOccurrences(code, "MainMenuStrip"), Is.EqualTo(1),
                "exactly one MainMenuStrip line — the fixture has exactly one MenuStrip");
            Assert.That(code, Does.Contain("menuStrip1.Dock = DockStyle.Top"));
            Assert.That(code, Does.Not.Contain("menuStrip1.Location"), "a strip has no geometry");
        });
    }

    [Test]
    public void WinForms_AnItemsSubtreeIsBuiltBeforeItsHostIsAdded()
    {
        var model = Model(FormStripDocumentTests.WithStrips, "MainForm.blform");
        var code = Emit(model);

        var itemAdd = code.IndexOf("mnuFile.DropDownItems.Add(mnuOpen)", StringComparison.Ordinal);
        var hostAdd = code.IndexOf("menuStrip1.Items.Add(mnuFile)", StringComparison.Ordinal);

        Assert.That(itemAdd, Is.GreaterThanOrEqualTo(0), "mnuFile.DropDownItems.Add(mnuOpen) was not emitted");
        Assert.That(itemAdd, Is.LessThan(hostAdd),
            "mnuFile's own subtree (its DropDownItems.Add calls) must be fully built before " +
            "menuStrip1.Items.Add(mnuFile) adds the host itself");
    }

    [Test]
    public void Web_ItemsAreFetchedById_AndWired()
    {
        // The web twin of spec §3's document, trimmed to the one host/item chain this test needs,
        // but — unlike WebWithStrips above (built later, for Task 16, which never checks binds) —
        // carrying a <Bind> on mnuOpen, since the bind is exactly what this test pins. The web
        // event name is the row's WEB default ("click", lowercase) — AppendBinds writes bind.Event
        // verbatim with no case conversion, so this must match the web fixtures used elsewhere
        // (e.g. FormRetargetTests) rather than the WinForms fixture's "Click".
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="mnuFile" Text="&amp;File">
                    <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open...">
                      <Bind Event="click" Handler="mnuOpen_Click"/>
                    </ToolStripMenuItem>
                  </ToolStripMenuItem>
                </MenuStrip>
              </Controls>
            </WebForm>
            """, "F.blwebform");
        var code = Emit(model);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("mnuOpen = doc.getElementById(\"mnuOpen\")"));
            Assert.That(code, Does.Contain("mnuOpen.addEventListener(\"click\", AddressOf mnuOpen_Click)"));
            Assert.That(code, Does.Not.Contain("MainMenuStrip"), "MainMenuStrip is a WinForms-only concept");
            Assert.That(code, Does.Not.Contain("Items.Add"),
                "the web path fetches items by id — it never builds an Items collection");
        });
    }

    /// <summary>
    /// Asserts a control's own OPENING TAG — from its <c>id="..."</c> up to the tag's next
    /// <c>'&gt;'</c> — carries no <c>tabindex</c>. Scoped to one tag rather than the whole page
    /// because the fixture's one Positioned control (<c>btnGo</c>) legitimately carries one.
    /// </summary>
    private static void AssertNoTabIndex(string html, string id)
    {
        var marker = $"id=\"{id}\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"'{id}' was not found in the emitted page");
        var end = html.IndexOf('>', start);
        Assert.That(end, Is.GreaterThan(start), $"'{id}''s opening tag was never closed");
        var tag = html.Substring(start, end - start);
        Assert.That(tag, Does.Not.Contain("tabindex"),
            $"'{id}' is a strip or an item — it must not carry a tabindex");
    }

    // ==================================================================
    // Chrome placement
    // ==================================================================

    [Test]
    public void Web_StripsArePageChrome()
    {
        var model = Model(WebWithStrips, "MainForm.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        var divOpen = html.IndexOf("<div class=\"vgs-form\">", StringComparison.Ordinal);
        var divClose = html.IndexOf("</div>", StringComparison.Ordinal);
        Assert.That(divOpen, Is.GreaterThanOrEqualTo(0), "the form div itself must exist");
        Assert.That(divClose, Is.GreaterThan(divOpen));

        Assert.Multiple(() =>
        {
            var nav = html.IndexOf("<nav id=\"menuStrip1\"", StringComparison.Ordinal);
            var menu = html.IndexOf("<menu id=\"toolStrip1\"", StringComparison.Ordinal);
            var footer = html.IndexOf("<footer id=\"statusStrip1\"", StringComparison.Ordinal);

            Assert.That(nav, Is.GreaterThanOrEqualTo(0), "menuStrip1 was not emitted at all");
            Assert.That(menu, Is.GreaterThanOrEqualTo(0), "toolStrip1 was not emitted at all");
            Assert.That(footer, Is.GreaterThanOrEqualTo(0), "statusStrip1 was not emitted at all");

            Assert.That(nav, Is.LessThan(divOpen),
                "the MenuStrip docks to the top of the PAGE — it belongs before the form div");
            Assert.That(menu, Is.LessThan(divOpen),
                "the ToolStrip docks to the top of the PAGE — it belongs before the form div");
            Assert.That(footer, Is.GreaterThan(divClose),
                "the StatusStrip docks to the bottom — it belongs after the form div");

            Assert.That(html, Does.Contain("<body data-form=\"MainForm\">"));

            var inside = html.Substring(divOpen, divClose - divOpen);
            Assert.That(inside, Does.Not.Contain("menuStrip1"), "a top strip must not also land inside the div");
            Assert.That(inside, Does.Not.Contain("toolStrip1"), "a top strip must not also land inside the div");
            Assert.That(inside, Does.Not.Contain("statusStrip1"), "a bottom strip must not also land inside the div");
        });
    }

    [Test]
    public void Web_TwoBottomStrips_AreEmittedInReverseDocumentOrder()
    {
        // toolStrip2 is documented FIRST. Per the Bands algebra Task 17 introduces (spec §2), the
        // FIRST-documented Bottom strip stacks nearest the true edge; on the page that means it is
        // emitted LAST, closest to the very end of <body> — i.e. statusStrip1 (documented second)
        // comes right after </div>, and toolStrip2 comes after THAT.
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <ToolStrip Id="toolStrip2" Dock="Bottom"/>
                <StatusStrip Id="statusStrip1" Dock="Bottom">
                  <ToolStripStatusLabel Id="lblStatus" Text="Ready"/>
                </StatusStrip>
              </Controls>
            </WebForm>
            """, "F.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        var footer = html.IndexOf("<footer id=\"statusStrip1\"", StringComparison.Ordinal);
        var menu = html.IndexOf("<menu id=\"toolStrip2\"", StringComparison.Ordinal);

        Assert.That(footer, Is.GreaterThanOrEqualTo(0), "statusStrip1 was not emitted at all");
        Assert.That(menu, Is.GreaterThanOrEqualTo(0), "toolStrip2 was not emitted at all");
        Assert.That(footer, Is.LessThan(menu),
            "statusStrip1 (documented SECOND) must be emitted first, right after the div; " +
            "toolStrip2 (documented FIRST) must be emitted after it — reverse document order");
    }

    // ==================================================================
    // Nesting, wrappers, roles, no tabindex, accelerator stripping
    // ==================================================================

    [Test]
    public void Web_MenuNesting()
    {
        var model = Model(WebWithStrips, "MainForm.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        var pos = -1;

        // ⚠ The emitter writes a newline plus indent between a parent's opening tag and its
        // children (FormAssetEmitter.cs:260-269), so this asserts ORDERED fragments rather than one
        // contiguous string — IndexOf search resumes strictly after the previous match, so it
        // proves ordering without demanding adjacency.
        void ExpectNext(string fragment)
        {
            var idx = html.IndexOf(fragment, pos + 1, StringComparison.Ordinal);
            Assert.That(idx, Is.GreaterThan(pos), $"expected to find '{fragment}' after position {pos}");
            pos = idx;
        }

        Assert.Multiple(() =>
        {
            ExpectNext("<nav id=\"menuStrip1\" class=\"vgs-MenuStrip\"");
            ExpectNext("<ul>");
            ExpectNext("<li id=\"mnuFile\" class=\"vgs-ToolStripMenuItem\"");
            ExpectNext(">File");
            ExpectNext("<ul>");
            ExpectNext("<li id=\"mnuOpen\"");
            ExpectNext("<li id=\"sep1\" class=\"vgs-ToolStripSeparator\" role=\"separator\">");
            ExpectNext("</ul>");
            ExpectNext("</li>");
            ExpectNext("<menu id=\"toolStrip1\" class=\"vgs-ToolStrip\" role=\"toolbar\"");
            ExpectNext("<input id=\"tsbOpen\" class=\"vgs-ToolStripButton\" type=\"button\" value=\"Open\">");
            ExpectNext("<footer");
            ExpectNext("role=\"status\"");
            ExpectNext("<span id=\"lblStatus\"");
            ExpectNext(">Ready</span>");

            Assert.That(html, Does.Not.Contain("&amp;File"),
                "the accelerator '&' is stripped, not HTML-escaped");
            Assert.That(html, Does.Not.Contain("&File"),
                "a raw, unescaped '&' must not reach the page either — 'File' alone is correct");

            foreach (var id in new[]
                     {
                         "menuStrip1", "mnuFile", "mnuOpen", "sep1", "mnuExit",
                         "toolStrip1", "tsbOpen", "statusStrip1", "lblStatus"
                     })
            {
                AssertNoTabIndex(html, id);
            }
        });
    }

    [Test]
    public void Web_ToolTipTextBecomesTitle()
    {
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <ToolStrip Id="toolStrip1" Dock="Top">
                  <ToolStripButton Id="tsbOpen" Text="Open" ToolTipText="Open a file"/>
                </ToolStrip>
              </Controls>
            </WebForm>
            """, "F.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        // ⚠ NOT a tautology despite ToolTipText→title being pre-existing, generic property-loop
        // behaviour (FormAssetEmitter.cs:192-203, unrelated to Task 16): asserting the FULL,
        // CONTIGUOUS attribute sequence — id, class, type, value, title, with NOTHING between class
        // and type — fails today because AppendControl still writes an unconditional
        // tabindex="0" between them. Only Task 16 Step 3 (tabindex only when Positioned) makes this
        // exact string appear.
        Assert.That(html,
            Does.Contain(
                "<input id=\"tsbOpen\" class=\"vgs-ToolStripButton\" type=\"button\" value=\"Open\" title=\"Open a file\">"),
            "no tabindex may sit between class and the rest of an item's attributes");
    }

    // ==================================================================
    // Stylesheet
    // ==================================================================

    [Test]
    public void Web_TheStylesheetCarriesEachPresentStripsCss_Once()
    {
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <ToolStrip Id="toolStrip1" Dock="Top"/>
                <ToolStrip Id="toolStrip2" Dock="Bottom"/>
              </Controls>
            </WebForm>
            """, "F.blwebform");

        var css = FormAssetEmitter.Css(model);

        // Read off the catalog rather than hand-copied into the test, so a future edit to the
        // ToolStrip row's WebCss can't silently desync this assertion from what the row actually
        // says.
        var expected = FormControlCatalog.Find("ToolStrip")!.WebCss!;

        Assert.That(CountOccurrences(css, expected), Is.EqualTo(1),
            "two ToolStrips share ONE kind — the CSS block is appended per KIND, not per control");
    }

    // ==================================================================
    // Gap-closing pins (commit 24c coverage sweep) — measured gaps, not new features.
    // ==================================================================

    /// <summary>Every HTML5 void element the emitter can produce — no closing tag, never self-closed
    /// (<see cref="FormAssetEmitter.AppendControl"/> writes <c>"&gt;\n"</c>, never <c>"/&gt;\n"</c>).</summary>
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    private static readonly Regex TagPattern = new(@"<(?<close>/)?(?<name>[a-zA-Z][\w:-]*)(?<rest>[^>]*)>");

    /// <summary>
    /// Proves BALANCE across the whole page, not just the sequence of fragments
    /// <see cref="Web_MenuNesting"/> checks with <c>IndexOf</c> — a wrapper opened and never closed
    /// (or closed twice) passes every ordered-fragment assertion there.
    ///
    /// <para>⚠ Deliberately NOT <c>XDocument.Parse</c>: the page is not XML-clean. Every void
    /// element (<c>meta</c>, <c>link</c>, <c>input</c>, <c>img</c>) is emitted with a bare
    /// <c>&gt;</c> and no closing tag and no self-closing slash — valid HTML5, invalid strict XML —
    /// so a real XML parser refuses the whole document before it ever reaches the menu markup this
    /// gap is about. A tag-balance walk that knows the HTML5 void-element list is the honest check
    /// for what this emitter actually produces.</para>
    /// </summary>
    private static void AssertHtmlTagsAreBalanced(string html)
    {
        var stack = new Stack<string>();

        foreach (Match m in TagPattern.Matches(html))
        {
            var name = m.Groups["name"].Value;

            // Not a real tag: the doctype's "!DOCTYPE" can never match [a-zA-Z] as its first
            // character right after '<', so it is already excluded by the pattern itself; nothing
            // extra to skip here.
            var isClose = m.Groups["close"].Success;
            var isSelfClosed = m.Groups["rest"].Value.TrimEnd().EndsWith("/", StringComparison.Ordinal);

            if (isClose)
            {
                Assert.That(stack.Count, Is.GreaterThan(0), $"</{name}> has no matching opening tag");
                var opened = stack.Pop();
                Assert.That(string.Equals(opened, name, StringComparison.OrdinalIgnoreCase), Is.True,
                    $"</{name}> does not match the innermost open tag <{opened}> — the document is not properly nested");
                continue;
            }

            if (isSelfClosed || VoidElements.Contains(name))
            {
                continue;
            }

            stack.Push(name);
        }

        Assert.That(stack, Is.Empty,
            "unclosed tag(s) remain open at end of document: " + string.Join(", ", stack));
    }

    [Test]
    public void Web_EmittedPage_HasBalancedTags()
    {
        var model = Model(WebWithStrips, "MainForm.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        // Sanity: the fixture must actually exercise nested wrappers (menuStrip1's <ul>, plus
        // mnuFile's own nested <ul> for its dropdown) or this pin could pass by having nothing to
        // unbalance.
        Assert.That(CountOccurrences(html, "<ul>"), Is.GreaterThanOrEqualTo(2),
            "the fixture must nest at least two <ul> wrappers for this pin to mean anything");

        AssertHtmlTagsAreBalanced(html);
    }

    [Test]
    public void Web_StatusStripWithNoDockAttribute_StillDocksToTheRowDefault()
    {
        // No Dock= at all — the document-value arm of FormControl.DockEdge must fall through to the
        // catalog row's own default ("Bottom" for StatusStrip), not silently land at "Top".
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <StatusStrip Id="statusStrip1">
                  <ToolStripStatusLabel Id="lblStatus" Text="Ready"/>
                </StatusStrip>
              </Controls>
            </WebForm>
            """, "F.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        var divClose = html.IndexOf("</div>", StringComparison.Ordinal);
        var footer = html.IndexOf("<footer id=\"statusStrip1\"", StringComparison.Ordinal);

        Assert.That(divClose, Is.GreaterThanOrEqualTo(0), "the form div itself must exist");
        Assert.That(footer, Is.GreaterThan(divClose),
            "a StatusStrip with NO Dock attribute must still dock to its row's own Bottom default, " +
            "landing AFTER the form div — not at the top, before it");
    }

    [Test]
    public void Web_StatusStripWithEmptyDockAttribute_StillDocksToBottom()
    {
        // Dock="" is legal XML and is stored verbatim (D9 Degraded tier) — "" must still be treated
        // as "no value", falling back to the row default, not merely as "not Bottom" => Top.
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <StatusStrip Id="statusStrip1" Dock="">
                  <ToolStripStatusLabel Id="lblStatus" Text="Ready"/>
                </StatusStrip>
              </Controls>
            </WebForm>
            """, "F.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        var divClose = html.IndexOf("</div>", StringComparison.Ordinal);
        var footer = html.IndexOf("<footer id=\"statusStrip1\"", StringComparison.Ordinal);

        Assert.That(divClose, Is.GreaterThanOrEqualTo(0), "the form div itself must exist");
        Assert.That(footer, Is.GreaterThan(divClose),
            "Dock=\"\" must still fall back to the row's Bottom default — reading it as merely " +
            "'not Bottom' docks the strip to the TOP instead");
    }

    [Test]
    public void Web_DoubleAmpersandBecomesOneLiteralAmpersand_HtmlEscaped()
    {
        // "&&" is a LITERAL ampersand in a WinForms caption (never the accelerator mark). Fixtures
        // elsewhere in this file only ever use a LONE '&' ("&File", "E&xit"), so mutating the
        // accelerator regex to a blanket `text.Replace("&", "")` fails every other test in this
        // file — this is the one fixture that can tell "&&" collapsed to ONE '&' apart from "&&"
        // collapsing to NONE.
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="mnuSave" Text="Save &amp;&amp; Close"/>
                </MenuStrip>
              </Controls>
            </WebForm>
            """, "F.blwebform");
        var html = FormAssetEmitter.Html(model, "Site.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain(">Save &amp; Close<"),
                "'&&' must reach the page as ONE HTML-escaped ampersand");
            Assert.That(html, Does.Not.Contain("Save Close"),
                "the literal ampersand must not be silently dropped entirely");
            Assert.That(html, Does.Not.Contain("&amp;&amp;"),
                "the pair must collapse to ONE ampersand, not survive as two");
        });
    }
}
