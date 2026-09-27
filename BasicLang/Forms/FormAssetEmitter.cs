using System.Globalization;
using System.Text;

namespace BasicLang.Forms;

/// <summary>
/// Emits a form document's page and stylesheet at <b>build</b> time (D6).
///
/// <para>⛔ Build time, not designer-save time. Generated-on-save artifacts go stale:
/// <c>basiclang build</c> on a clean checkout, or any build with the designer closed, would produce
/// a page with no markup and no styling.</para>
///
/// <para>⛔ It writes into <b>the directory it is handed</b> and never computes one. The two entry
/// points disagree — the IDE uses <c>bin\Debug</c> and the CLI <c>bin\Debug\&lt;tfm&gt;</c> — so
/// reusing the JavaScript emitter's own output directory makes them agree automatically instead of
/// adding a third path computation to keep in step.</para>
/// </summary>
public static class FormAssetEmitter
{
    /// <summary>
    /// The one generated top-level name in the whole project (D7). Fixed spelling so
    /// <c>design --check</c> can collision-check it once with <c>BL8031</c>; per-control fields are
    /// class members and cannot collide across forms.
    ///
    /// <para>⛔⛔ <b>It is a SHARED METHOD ON A CLASS</b>, and the two shapes that look more
    /// natural were each measured and each rejected — with a real <c>BasicLang build</c>, and then
    /// by RUNNING what came out:</para>
    /// <list type="number">
    ///   <item><b>A bare top-level Sub</b> (what D7 describes) does not COMPILE across files. A
    ///   <c>Sub Helper()</c> in one .bas called as <c>Helper()</c> from another fails with <i>"no
    ///   lowering for 'Helper.Helper'"</i>, and nothing in that message names the cause. Not
    ///   specific to generated code — two hand-written files do it too.</item>
    ///   <item><b><c>Public Module</c> compiles and then throws at RUN TIME.</b> The JavaScript
    ///   backend FLATTENS a module's members to bare globals (<c>function VgsDispatchForm()</c>)
    ///   while emitting the call site qualified (<c>VgsForms.VgsDispatchForm()</c>), so the emitted
    ///   script references a <c>VgsForms</c> that appears nowhere in the file and every page died
    ///   on load with <i>ReferenceError: VgsForms is not defined</i>. The build was green, every
    ///   expected string was present, and the feature did not work. That is a COMPILER bug, not a
    ///   designer one — see docs/form-designer-followups.md.</item>
    /// </list>
    /// <para>A class with a <c>Shared</c> method emits a real <c>class VgsForms</c> with a
    /// <c>static</c> member, which is what the qualified call resolves against.
    /// <c>FormBuildEmissionTests</c> now RUNS the emitted script under node instead of reading it,
    /// because reading it is exactly what missed this.</para>
    /// </summary>
    public const string DispatchSubName = "VgsDispatchForm";

    /// <summary>The class the dispatch lives on — see <see cref="DispatchSubName"/> for why.</summary>
    public const string DispatchModuleName = "VgsForms";

    /// <summary>Exactly what a user writes in <c>Main()</c> to hand control to the dispatch.</summary>
    public const string DispatchCall = DispatchModuleName + "." + DispatchSubName + "()";

    /// <summary>
    /// Writes <c>&lt;Name&gt;.html</c> and <c>&lt;Name&gt;.css</c> for each form.
    ///
    /// <para>Form pages <b>always overwrite</b>: every form needs a starting point on every build,
    /// and they are generated files rather than a harness. <c>index.html</c> and
    /// <c>package.json</c> remain the only never-overwrite outputs and are not touched here.</para>
    /// </summary>
    public static IReadOnlyList<string> Emit(
        string outputDirectory, string scriptFileName, IEnumerable<FormDocument> forms)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        foreach (var form in forms)
        {
            if (form.Target != FormTarget.Web)
            {
                // A .blform is a desktop window; it has no page. Skipping silently is correct —
                // a mixed project is normal.
                continue;
            }

            var htmlPath = Path.Combine(outputDirectory, form.Name + ".html");
            File.WriteAllText(htmlPath, Html(form, scriptFileName), new UTF8Encoding(false));
            written.Add(htmlPath);

            var cssPath = Path.Combine(outputDirectory, form.Name + ".css");
            File.WriteAllText(cssPath, Css(form), new UTF8Encoding(false));
            written.Add(cssPath);
        }

        return written;
    }

    // ==================================================================
    // Markup
    // ==================================================================

    /// <summary>
    /// The page. Ids are stable — they are the control ids from the document, which is what lets the
    /// generated <c>InitializeComponent</c> find each element with <c>getElementById</c>.
    /// </summary>
    public static string Html(FormDocument form, string scriptFileName)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        // Text ?? Name (spec §2.3): the page title follows the caption the user set, or the form's name.
        sb.Append($"<title>{Text(form.Text ?? form.Name)}</title>\n");
        sb.Append($"<link rel=\"stylesheet\" href=\"{Attr(form.Name)}.css\">\n");
        sb.Append("</head>\n");

        // data-form is how Main() knows which form to initialise (D7). One page per form, each
        // naming itself, with a single shared script.
        sb.Append($"<body data-form=\"{Attr(form.Name)}\">\n");

        // ⛔ ONE vocabulary test (spec 2026-09-27 §2.1): a Canvas page speaks pixels, Grid/Flow speak cells.
        if (FormVocabulary.IsPixel(form))
        {
            AppendCanvasBody(sb, form);
        }
        else
        {
            AppendGridOrFlowBody(sb, form);
        }

        sb.Append($"<script type=\"module\" src=\"{Attr(scriptFileName)}\"></script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>
    /// A Grid/Flow page's body. ⛔ A Docked strip is PAGE CHROME, not form content (spec §4) — on a Grid/Flow page.
    /// It sits OUTSIDE <c>&lt;div class="vgs-form"&gt;</c> because that div is the layout container — a Grid or Flow
    /// box whose tracks the user authored for their own controls. A <c>&lt;nav&gt;</c> placed inside it would consume
    /// a cell nobody declared and push every control one place along, from a green build. (A Canvas page puts strips
    /// INSIDE: <see cref="AppendCanvasBody"/>.)
    /// </summary>
    private static void AppendGridOrFlowBody(StringBuilder sb, FormDocument form)
    {
        var top = new List<FormControl>();
        var bottom = new List<FormControl>();
        var rest = new List<FormControl>();

        foreach (var control in form.Controls)
        {
            if (control.Definition?.Place != FormPlace.Docked)
            {
                rest.Add(control);
            }
            else if (control.IsDockedToBottom)
            {
                bottom.Add(control);
            }
            else
            {
                top.Add(control);
            }
        }

        // Top chrome in DOCUMENT order: the first-documented strip is nearest the top edge, which on
        // the page means first.
        foreach (var control in top)
        {
            AppendControl(sb, control, indent: "");
        }

        sb.Append("<div class=\"vgs-form\">\n");

        foreach (var control in rest)
        {
            AppendControl(sb, control, indent: "  ");
        }

        AppendLiteral(sb, form);

        sb.Append("</div>\n");

        // ⛔ Bottom chrome in REVERSE document order. Same algebra as the canvas bands: the
        // FIRST-documented Bottom strip stacks nearest the true bottom edge, so on the page it is
        // emitted LAST, closest to the end of <body>. Forward order here would put the status bar
        // below a bottom toolbar on the page and above it in the designer — the same document
        // rendering two ways.
        for (var i = bottom.Count - 1; i >= 0; i--)
        {
            AppendControl(sb, bottom[i], indent: "");
        }
    }

    /// <summary>
    /// A Canvas page's body (spec 2026-09-27 §3). ⛔ The coordinate space is the WinForms CLIENT AREA, strips
    /// included: every strip is an absolutely positioned band INSIDE <c>.vgs-form</c> at
    /// <see cref="FormDockLayout"/>'s rectangle (written by <see cref="CanvasCss"/>), so a control at Y=30 sits 6px
    /// below a 24px menu exactly as in WinForms. Everything — strips too — in DOCUMENT order: absolutely positioned
    /// siblings paint later-on-top, which is WinForms' "last in the list is in front" (scope call S12).
    /// </summary>
    private static void AppendCanvasBody(StringBuilder sb, FormDocument form)
    {
        sb.Append("<div class=\"vgs-form\">\n");

        foreach (var control in form.Controls)
        {
            AppendControl(sb, control, indent: "  ");
        }

        // <Literal> flows at the form area's top-left, under the positioned controls (spec §3).
        AppendLiteral(sb, form);

        sb.Append("</div>\n");

        // ⛔ Owner decision 2026-09-27: the page re-docks when user code shows or hides a docked control. A CLASSIC
        // script after the form area (the elements exist) and before the module (it observes InitializeComponent).
        if (FormDockScript.PageScript(form) is { } dock)
        {
            sb.Append(dock);
        }
    }

    private static void AppendLiteral(StringBuilder sb, FormDocument form)
    {
        if (!string.IsNullOrEmpty(form.Literal))
        {
            // ⛔ Passes through UNTOUCHED — the runat="server" inversion (D9). Not escaped, because
            // it is markup the user wrote to be markup; the canvas shows it read-only for the same
            // reason.
            sb.Append(form.Literal);
            if (!form.Literal.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.Append('\n');
            }
        }
    }

    private static void AppendControl(StringBuilder sb, FormControl control, string indent)
    {
        var definition = control.Definition;
        var tag = definition?.HtmlTag;
        if (tag == null)
        {
            // No catalog row for this target — the document round-trips it, but there is no markup
            // we can honestly emit for it. Left as a comment so the gap is visible in the output
            // rather than silently absent from the page.
            sb.Append($"{indent}<!-- {Text(control.Id)}: '{Text(control.Kind)}' has no web catalog row -->\n");
            return;
        }

        sb.Append($"{indent}<{tag} id=\"{Attr(control.Id)}\"");
        sb.Append($" class=\"vgs-{Attr(control.Kind)}\"");

        // Chrome is the one part of a form whose meaning the DOM cannot infer from its tag — a
        // <menu> is not a toolbar and an <li> is not a separator to a screen reader. Stated on the
        // ROW, so a kind that needs a role declares one rather than the emitter carrying a list.
        if (definition!.HtmlRole != null)
        {
            sb.Append($" role=\"{Attr(definition.HtmlRole)}\"");
        }

        // ⛔ Only a POSITIONED control takes a tab stop. A strip and an item have no TabIndex in the
        // document at all (Task 14 stopped writing one), so an unconditional tabindex="0" here put
        // every menu item into the page's tab order at the same rank — Tab walking a dozen dead
        // <li>s before reaching the first real control, from a document that says nothing of the
        // sort.
        if (definition.Place == FormPlace.Positioned)
        {
            sb.Append($" tabindex=\"{Number(control.TabIndex)}\"");
        }

        if (definition.HtmlInputType != null)
        {
            sb.Append($" type=\"{Attr(definition.HtmlInputType)}\"");
        }

        var text = control.Properties.TryGetValue("Text", out var t) ? t : null;

        // ⛔ An item's caption carries a WinForms ACCELERATOR mark: "&File" means File with F
        // underlined, and "&&" is a literal ampersand. The generic escaper turns both into
        // "&amp;" — "&amp;File" reaches the page as the visible text "&File". ⛔ FormAccelerator is
        // the ONE rule — the designer canvas draws the same display text, and FormPlacement.ItemId
        // derives an item's id from it. A private copy here is how the page and the designer came
        // to disagree about what "&Open" looks like.
        if (text != null && definition.Place == FormPlace.Item)
        {
            text = FormAccelerator.Display(text).Display;
        }

        // ⛔ A <select> takes <option> children and NOTHING else. Its Text was being written as a
        // bare text node inside the element, which browsers drop or render as stray text above the
        // list. A ComboBox's Text is its label, not its content, so it becomes a title attribute.
        var isSelect = string.Equals(tag, "select", StringComparison.Ordinal);

        // An <input> has no children, so its Text is a value; everything else carries it as content.
        var textIsValue = string.Equals(tag, "input", StringComparison.Ordinal);
        if (textIsValue && text != null)
        {
            sb.Append($" value=\"{Attr(text)}\"");
        }

        // A select's Text labels the control; it cannot be its content.
        if (isSelect && text != null)
        {
            sb.Append($" title=\"{Attr(text)}\"");
        }

        if (Flag(control, "Enabled") == false) sb.Append(" disabled");
        if (Flag(control, "Checked") == true) sb.Append(" checked");
        if (Flag(control, "ReadOnly") == true) sb.Append(" readonly");
        if (Flag(control, "MultiSelect") == true) sb.Append(" multiple");

        // ⛔ Driven from the CATALOG, not from a list here. WinForms `Minimum` is HTML `min`, and an
        // `if` per property would be a second list beside FormControlCatalog that goes stale the day
        // a row is added — the exact failure the catalog exists to prevent. A row declares its
        // HtmlAttribute or it does not reach the page at all.
        //
        // ⚠ Skipped where the property does not apply to the web (Targets), so a WinForms-only row
        // like DecimalPlaces cannot leak an attribute <input type="number"> has never heard of.
        foreach (var property in definition.Properties)
        {
            if (property.HtmlAttributeName is not { } attribute ||
                !property.AppliesTo(FormTarget.Web) ||
                !control.Properties.TryGetValue(property.Name, out var raw) ||
                string.IsNullOrEmpty(raw))
            {
                continue;
            }

            // ⛔ An Int row (min/max/value/step/maxlength) is re-emitted from the PARSED number, as
            // WinForms emits it — and a value the catalog refuses is not emitted at all, because
            // WinForms emits nothing for a Degraded value and the two targets must agree on what the
            // document says. Verbatim text let "5\r\n" or "−5" reach the page as an attribute.
            if (property.Type == FormPropertyType.Int)
            {
                if (!FormPropertyDef.TryParseInt(raw, out var number))
                {
                    continue;
                }

                raw = Number(number);
            }

            sb.Append($" {attribute}=\"{Attr(raw)}\"");
        }

        if (control.Properties.TryGetValue("GroupName", out var group))
        {
            // The DOM groups radios by name=; WinForms groups them by container. Modeled explicitly
            // so the two targets can be made to agree rather than diverging silently.
            sb.Append($" name=\"{Attr(group)}\"");
        }

        if (string.Equals(tag, "img", StringComparison.Ordinal) &&
            control.Properties.TryGetValue("Image", out var image))
        {
            sb.Append($" src=\"{Attr(image)}\" alt=\"{Attr(control.Id)}\"");
        }

        // Void elements take no closing tag and no children.
        if (string.Equals(tag, "input", StringComparison.Ordinal) ||
            string.Equals(tag, "img", StringComparison.Ordinal))
        {
            sb.Append(">\n");
            return;
        }

        sb.Append('>');

        // ⛔⛔ Items reach the page as <option>s or they do not reach it at all. The WinForms side
        // emits Items.Add(...) per entry, so without this a ComboBox rendered as an EMPTY dropdown
        // on the web and a populated one on the desktop — from the same document. That is the
        // designer/runtime divergence D9 exists to prevent, across targets instead of within one.
        if (isSelect && control.Properties.TryGetValue("Items", out var items))
        {
            // ⛔ SelectedIndex is the SAME divergence one property over. WinForms emits
            // `cmb.SelectedIndex = 2`; the page has no such property, and without marking the
            // option the desktop opened on the user's chosen entry while the web opened on the
            // first one. It is an INDEX into this list, so it can only be resolved here, where the
            // list is being written.
            //
            // ⚠ Read with the catalog's TryParseInt, never int.TryParse: a "1\r\n" the desktop judges
            // Degraded (and emits nothing for) must not mark an option on the page either.
            var selected = control.Properties.TryGetValue("SelectedIndex", out var raw) &&
                           FormPropertyDef.TryParseInt(raw, out var parsed)
                ? parsed
                : -1;

            sb.Append('\n');
            var position = 0;
            foreach (var item in FormPropertyDef.SplitItems(items))
            {
                var mark = position == selected ? " selected" : "";
                sb.Append($"{indent}  <option{mark}>{Text(item)}</option>\n");
                position++;
            }

            sb.Append(indent);
        }
        else if (text != null && !isSelect)
        {
            sb.Append(Text(text));
        }

        if (control.Children.Count > 0)
        {
            sb.Append('\n');

            // A menu's children are LIST ITEMS, and an <li> outside a list is not a list item —
            // the wrapper is the row's, not the control's own tag, because the same <li> nests
            // inside another <li>'s <ul> to make a submenu. Declared on the row (spec §4) so the
            // emitter never asks what kind this is.
            var wrapper = definition.HtmlChildrenWrapper;
            var childIndent = indent + "  ";

            if (wrapper != null)
            {
                sb.Append($"{childIndent}<{wrapper}>\n");
                childIndent += "  ";
            }

            foreach (var child in control.Children)
            {
                AppendControl(sb, child, childIndent);
            }

            if (wrapper != null)
            {
                sb.Append($"{indent}  </{wrapper}>\n");
            }

            sb.Append(indent);
        }

        sb.Append($"</{tag}>\n");
    }

    // ==================================================================
    // Stylesheet
    // ==================================================================

    /// <summary>
    /// The layout, as CSS. Grid and Flow are the CELL vocabularies (D3); a <c>Canvas</c> page speaks PIXELS (spec
    /// 2026-09-27) and takes <see cref="CanvasCss"/>. ⛔ The one vocabulary test is
    /// <see cref="FormVocabulary.IsPixel(FormDocument)"/>, asked first — never the target, never the switch below.
    /// </summary>
    public static string Css(FormDocument form)
    {
        if (FormVocabulary.IsPixel(form))
        {
            return CanvasCss(form);
        }

        var sb = new StringBuilder();
        var layout = form.Layout ?? new FormLayout();

        sb.Append($"/* Generated from {form.Name}{form.FileExtension}. Edits here are overwritten on build. */\n");
        sb.Append(".vgs-form {\n");

        switch (layout.Kind)
        {
            case FormLayoutKind.Grid:
                sb.Append("  display: grid;\n");
                // ⛔ The document stores a CSS track list comma-separated ("120px,1fr") because it is
                // one XML attribute; CSS wants it space-separated. Converting here rather than
                // storing it CSS-ready keeps the attribute readable and un-ambiguous to split.
                if (!string.IsNullOrEmpty(layout.Cols))
                {
                    sb.Append($"  grid-template-columns: {Tracks(layout.Cols)};\n");
                }
                if (!string.IsNullOrEmpty(layout.Rows))
                {
                    sb.Append($"  grid-template-rows: {Tracks(layout.Rows)};\n");
                }
                break;

            case FormLayoutKind.Flow:
                sb.Append("  display: flex;\n");
                sb.Append($"  flex-direction: {(string.Equals(layout.Dir, "Vertical", StringComparison.OrdinalIgnoreCase) ? "column" : "row")};\n");
                sb.Append("  flex-wrap: wrap;\n");
                break;
        }

        if (!string.IsNullOrEmpty(layout.Gap))
        {
            sb.Append($"  gap: {layout.Gap};\n");
        }

        sb.Append("}\n");

        foreach (var control in form.AllControls())
        {
            AppendControlCss(sb, control, layout);
        }

        AppendKindCss(sb, form);

        return sb.ToString();
    }

    /// <summary>
    /// Per-KIND chrome styling, appended ONCE however many controls of that kind the page has (spec §4). A menu is the
    /// one control whose appearance is not optional — an unstyled <c>&lt;ul&gt;</c> of <c>&lt;li&gt;</c>s is a
    /// bulleted vertical list, not a menu bar, and its submenus are all open at once. Distinct() on the block itself,
    /// because the rule is "one block per kind present" and two ToolStrips are one kind.
    /// </summary>
    private static void AppendKindCss(StringBuilder sb, FormDocument form)
    {
        foreach (var css in form.AllControls()
                     .Select(c => c.Definition?.WebCss)
                     .Where(s => s != null)
                     .Distinct())
        {
            sb.Append(css).Append('\n');
        }
    }

    private static void AppendControlCss(StringBuilder sb, FormControl control, FormLayout layout)
    {
        var rules = new List<string>();

        if (control.Geometry is GridGeometry grid && layout.Kind == FormLayoutKind.Grid)
        {
            // ⚠ CSS grid lines are 1-based; the document's Col/Row are 0-based (the spec's worked
            // example puts the first control at Col="0" Row="0"). Off by one here puts every
            // control one cell down and to the right, which looks like a layout bug and is an
            // indexing one.
            //
            // ⚠ Invariant, like every number this emitter writes: a U+2212 minus is not CSS.
            rules.Add("grid-column: " + Number(grid.Col + 1) + (grid.ColSpan > 1 ? " / span " + Number(grid.ColSpan) : ""));
            rules.Add("grid-row: " + Number(grid.Row + 1) + (grid.RowSpan > 1 ? " / span " + Number(grid.RowSpan) : ""));
        }

        // ⛔⛔ Driven from the CATALOG (spec §2.1). The four rules that used to be hard-coded here —
        // ForeColor, BackColor, TextAlign, Visible — MOVED onto each row's CssProperty in the same
        // commit: two sources would emit duplicate or conflicting declarations. A row declares its CSS
        // meaning or it does not reach the stylesheet, exactly as HtmlAttribute works for markup.
        // (Visible=false stays CSS, not a missing element: getElementById must still find it.)
        //
        // ⚠ Order is CATALOG order now (it was fore/back/align/display). No test depends on the order —
        // FormAssetEmitterTests asserts the exact text only for grid-only rules.
        rules.AddRange(CatalogDeclarations(control));

        if (rules.Count == 0)
        {
            return;
        }

        sb.Append($"#{control.Id} {{ {string.Join("; ", rules)}; }}\n");
    }

    /// <summary>
    /// The row-driven declarations for <paramref name="control"/> (spec §2.1), as <c>property: value</c>, in CATALOG
    /// order — the one walk both vocabularies use. <see cref="FormCss"/> converts each value.
    /// </summary>
    private static List<string> CatalogDeclarations(FormControl control)
    {
        var declarations = new List<string>();
        if (control.Definition is not { } definition)
        {
            return declarations;
        }

        foreach (var property in definition.Properties)
        {
            if (!property.AppliesTo(FormTarget.Web) ||
                !control.Properties.TryGetValue(property.Name, out var raw))
            {
                continue;
            }

            if (FormCss.Declaration(property, raw) is { } declaration)
            {
                declarations.Add($"{declaration.Property}: {declaration.Value}");
            }
        }

        return declarations;
    }

    // ==================================================================
    // A Canvas page (spec 2026-09-27 §3, §4, §5)
    // ==================================================================

    /// <summary>
    /// A Canvas page's stylesheet. The form area fills the window with the DESIGN SIZE as its minimum (larger →
    /// controls follow their anchors; smaller → the page scrolls, never squashes). Every positioned control and strip
    /// is absolutely placed by the pure deciders — <see cref="FormAnchorCss"/> for anchors,
    /// <see cref="FormDockLayout"/> for everything docked — and nothing here re-derives either.
    ///
    /// <para>⛔ The page's FIRST state is <see cref="FormDockMode.Runtime"/> (owner decision 2026-09-27): a hidden
    /// control takes no space, so the next docked control closes the gap. Where Runtime has no answer — a hidden docked
    /// control, a child of a hidden container — the Designer answer is written instead, and the page's reflow script
    /// (<see cref="FormDockScript"/>) overwrites it the moment that control is shown.</para>
    ///
    /// <para>⚠ A non-positive Width/Height writes no size (spec §3), so such a control is CONTENT-sized here and
    /// invisible on WinForms — an accepted divergence. ⚠ Children are positioned against their container's PADDING
    /// box, i.e. inside any CSS border (a GroupBox's fieldset), as WinForms positions them inside its
    /// DisplayRectangle; the amounts differ and the Task 12/13 harness measures them.</para>
    /// </summary>
    private static string CanvasCss(FormDocument form)
    {
        var sb = new StringBuilder();
        var runtime = FormDockLayout.Resolve(form, FormDockMode.Runtime);
        var designer = FormDockLayout.Resolve(form, FormDockMode.Designer);
        var (width, height) = runtime.RootClientSize;

        sb.Append($"/* Generated from {form.Name}{form.FileExtension}. Edits here are overwritten on build. */\n");
        sb.Append("body { margin: 0; }\n");
        sb.Append(".vgs-form {\n");
        sb.Append("  position: relative;\n");
        sb.Append("  width: 100%;\n");
        sb.Append($"  min-width: {Number(width)}px;\n");
        sb.Append("  height: 100vh;\n");
        sb.Append($"  min-height: {Number(height)}px;\n");
        sb.Append("  box-sizing: border-box;\n");
        sb.Append("}\n");

        // ⛔ The UA's [hidden]{display:none} loses to the phone query's display:flex on a container, so a control
        // user code hid with `el.hidden = True` would reappear below the breakpoint.
        sb.Append(".vgs-form [hidden] { display: none !important; }\n");

        AppendCanvasControls(sb, form.Controls, parent: null, runtime, designer);
        AppendKindCss(sb, form);
        AppendStackedQuery(sb, form, designer);
        return sb.ToString();
    }

    /// <summary>
    /// Below the phone breakpoint (spec §5) the form area is ONE flex column. Every control goes
    /// <c>position: static</c> (otherwise <c>order</c> does nothing), in <see cref="FormReadingOrder"/>'s order per
    /// sibling list; top strips first and bottom strips last; a docked control is an ordinary row. HTML order is
    /// unchanged. ⛔ The breakpoint is <see cref="FormLayout.EffectiveMobileBreakpoint"/> — the one rule; 0 never
    /// stacks, a Degraded value gives the default.
    /// </summary>
    private static void AppendStackedQuery(StringBuilder sb, FormDocument form, FormDockLayoutResult designer)
    {
        var breakpoint = form.Layout?.EffectiveMobileBreakpoint ?? FormLayout.DefaultMobileBreakpoint;
        if (breakpoint == 0)
        {
            return;
        }

        sb.Append($"@media (width < {Number(breakpoint)}px) {{\n");
        sb.Append("  .vgs-form { display: flex; flex-direction: column; gap: 8px; height: auto; min-width: 0; min-height: 0; }\n");
        AppendStacked(sb, form.Controls, designer);
        sb.Append("}\n");
    }

    /// <summary>
    /// One sibling list's stacked rules, then each container's children inside it.
    ///
    /// <para>⚠ Every rectangle here is the DESIGNER picture (a docked control's resolved rect; an undocked control's
    /// stored one). CSS <c>order</c> is static, so ordering by the design keeps it the same whichever controls user
    /// code has hidden; a hidden control stays <c>display:none</c> and its order only matters once shown.</para>
    /// </summary>
    private static void AppendStacked(
        StringBuilder sb, IReadOnlyList<FormControl> siblings, FormDockLayoutResult designer)
    {
        FormRect? RectOf(FormControl control) =>
            designer.TryGet(control, out var docked) ? docked.Bounds
            : control.Geometry is PixelGeometry pixel ? new FormRect(pixel.X, pixel.Y, pixel.Width, pixel.Height)
            : null;

        // A strip is always Top or Bottom here (FormDockLayout.EdgeOf); ordered by where it DOCKS, so the first
        // Top strip comes first and the first-documented Bottom strip (nearest the true bottom edge) comes last.
        var strips = siblings
            .Where(c => c.Definition?.Place == FormPlace.Docked && designer.TryGet(c, out _))
            .ToList();
        var top = strips.Where(c => FormDockLayout.EdgeOf(c) == FormDockEdge.Top).OrderBy(c => RectOf(c)!.Value.Y);
        var bottom = strips.Where(c => FormDockLayout.EdgeOf(c) == FormDockEdge.Bottom).OrderBy(c => RectOf(c)!.Value.Y);
        var positioned = siblings
            .Where(c => (c.Definition?.Place ?? FormPlace.Positioned) == FormPlace.Positioned && RectOf(c) != null)
            .ToList();

        var ordered = top
            .Concat(FormReadingOrder.Order(positioned, c => RectOf(c)!.Value))
            .Concat(bottom)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            AppendStackedRule(sb, ordered[i], i, RectOf(ordered[i])!.Value);
        }

        foreach (var container in positioned.Where(c => c.Children.Count > 0))
        {
            AppendStacked(sb, container.Children, designer);
        }
    }

    private static void AppendStackedRule(StringBuilder sb, FormControl control, int order, FormRect rect)
    {
        var definition = control.Definition;
        var isContainer = definition?.IsContainer == true;
        var rules = new List<string> { "position: static", $"order: {Number(order)}" };

        if (definition?.Place == FormPlace.Docked)
        {
            // A strip spans the column, as it spans the form.
            rules.Add("align-self: stretch");
        }
        else if (definition?.StretchesWhenStacked == true)
        {
            // ⛔ The catalog's facet (spec §5), never a Kind switch.
            rules.Add("align-self: stretch");
            rules.Add("width: auto");
            if (rect.Height > 0 && !isContainer)
            {
                rules.Add($"height: {Number(rect.Height)}px");
            }
        }
        else
        {
            // Small controls keep their designed size, left-aligned.
            rules.Add("align-self: flex-start");
            if (rect.Width > 0)
            {
                rules.Add($"width: {Number(rect.Width)}px");
            }

            if (rect.Height > 0 && !isContainer)
            {
                rules.Add($"height: {Number(rect.Height)}px");
            }
        }

        // A control wider than the phone never makes it scroll sideways.
        rules.Add("max-width: 100%");

        if (isContainer)
        {
            rules.Add("height: auto");

            // ⛔ Never write display for a control whose own catalog CSS writes it (Visible=false → display:none):
            // this later declaration would un-hide it on phones.
            if (!CatalogDeclarations(control).Any(d => d.StartsWith("display:", StringComparison.Ordinal)))
            {
                rules.Add("display: flex");
                rules.Add("flex-direction: column");
                rules.Add("gap: 8px");
            }
        }

        sb.Append($"  #{control.Id} {{ {string.Join("; ", rules)}; }}\n");
    }

    private static void AppendCanvasControls(
        StringBuilder sb, IReadOnlyList<FormControl> siblings, FormControl? parent,
        FormDockLayoutResult runtime, FormDockLayoutResult designer)
    {
        foreach (var control in siblings)
        {
            var rules = new List<string>();

            if (CanvasPlacement(control, parent, runtime, designer) is { } placement)
            {
                // ⛔ margin: 0 — an absolutely positioned box is placed by its MARGIN edge, and the UA gives a
                // checkbox/radio `margin: 3px 3px 0 5px` and a fieldset `0 2px`: the control would land off its X/Y.
                // box-sizing: the outer box IS the design Width x Height, WinForms' Size.
                rules.Add("position: absolute");
                rules.Add("box-sizing: border-box");
                rules.Add("margin: 0");
                rules.AddRange(placement.Select(d => $"{d.Property}: {d.Value}"));
            }

            rules.AddRange(CatalogDeclarations(control));

            if (rules.Count > 0)
            {
                sb.Append($"#{control.Id} {{ {string.Join("; ", rules)}; }}\n");
            }

            // ⛔ A strip's OPEN dropdown must not open under the controls after it (strips are in document order,
            // S12): WinForms opens it as its own window. Its row's children-wrapper lists are lifted — the bar's own
            // list is static, where z-index does nothing; every nested one is absolutely positioned.
            if (control.Definition is { Place: FormPlace.Docked, HtmlChildrenWrapper: { } wrapper })
            {
                sb.Append($"#{control.Id} {wrapper} {{ z-index: 1; }}\n");
            }

            AppendCanvasControls(sb, control.Children, control, runtime, designer);
        }
    }

    /// <summary>
    /// Where <paramref name="control"/> sits, or null when it has no pixel place (an item, a tray component, a
    /// positioned control with no pixel geometry, or a control inside something with no client area).
    /// </summary>
    private static IReadOnlyList<(string Property, string Value)>? CanvasPlacement(
        FormControl control, FormControl? parent, FormDockLayoutResult runtime, FormDockLayoutResult designer)
    {
        var place = control.Definition?.Place ?? FormPlace.Positioned;
        if (place is not (FormPlace.Positioned or FormPlace.Docked))
        {
            return null;
        }

        // Runtime first: the page opens as the running form. Designer only where Runtime has no answer.
        if (runtime.TryGet(control, out var docked) || designer.TryGet(control, out docked))
        {
            return FormAnchorCss.Docked(docked);
        }

        if (place == FormPlace.Docked || control.Geometry is not PixelGeometry pixel)
        {
            return null;
        }

        // ⛔ The container's size is FormDockLayoutResult's — ClientSizeOf's one answer (a docked Panel's RESOLVED
        // bounds), never re-derived here and never the stored size of a docked container.
        (int Width, int Height) client;
        if (parent == null)
        {
            client = runtime.RootClientSize;
        }
        else if (!runtime.TryGetClientSize(parent, out client) && !designer.TryGetClientSize(parent, out client))
        {
            return null;
        }

        return FormAnchorCss.Positioned(pixel, client.Width, client.Height);
    }

    // ==================================================================
    // The Main() dispatch (D7)
    // ==================================================================

    /// <summary>
    /// The generated dispatch helper: one per project, fixed spelling.
    ///
    /// <para>⛔⛔ The body attribute is read in <b>TWO STEPS</b>. Measured:
    /// <c>Dim s As String = doc.body.getAttribute("data-form")</c> fails with <i>"Cannot assign value
    /// of type 'Object' to variable of type 'String'"</i> — chained access through a declared
    /// Property loses the declared type, even though both <c>Document.body As Element</c> and
    /// <c>Element.getAttribute(…) As String</c> are declared. The two-step form compiles and
    /// runs.</para>
    /// </summary>
    public static string DispatchSource(IEnumerable<string> formNames)
    {
        var sb = new StringBuilder();
        sb.Append($"Public Class {DispatchModuleName}\n");
        sb.Append($"    Public Shared Sub {DispatchSubName}()\n");
        sb.Append("        Dim doc As Document = ::document\n");
        sb.Append("        Dim b As Element = doc.body\n");
        sb.Append("        Dim formName As String = b.getAttribute(\"data-form\")\n");

        var first = true;
        foreach (var name in formNames)
        {
            // Through the one escape, not `\"{name}\"`: a form name is an identifier today, but this is a
            // BasicLang string literal and every one of those goes through StringLiteral.
            sb.Append($"        {(first ? "If" : "ElseIf")} formName = {FormPropertyDef.StringLiteral(name)} Then\n");
            // ⛔ Constructing the form is ENOUGH — the scaffolded `Public Sub New()` already calls
            // InitializeComponent (FormScaffolder.CodeBehind). Calling it again here ran the whole
            // init body TWICE, so every addEventListener registered its handler twice and one
            // click fired it twice. It is also generated Private, so the second call was reaching
            // for a member this module has no business touching.
            sb.Append($"            Dim f As New {name}()\n");
            first = false;
        }

        if (!first)
        {
            sb.Append("        End If\n");
        }

        sb.Append("    End Sub\n");
        sb.Append("End Class\n");
        return sb.ToString();
    }

    // ==================================================================
    // Escaping
    // ==================================================================

    private static string Tracks(string commaSeparated) =>
        string.Join(" ", commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>True/false for a boolean-ish property, or null when it is unset or unparseable.</summary>
    private static bool? Flag(FormControl control, string name) =>
        control.Properties.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static string Text(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    /// <summary>
    /// Attribute escaping. Escapes <c>"</c> as well as the three <see cref="Text"/> handles — an
    /// unescaped quote closes the attribute early and makes the page unparseable, and control ids
    /// and property values are user text.
    /// </summary>
    private static string Attr(string value) => Text(value).Replace("\"", "&quot;");

    /// <summary>
    /// A number as markup/CSS text. ⛔ Invariant: sv-SE/fi-FI/nb-NO spell a negative with U+2212, which
    /// neither HTML nor CSS reads as a minus.
    /// </summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
