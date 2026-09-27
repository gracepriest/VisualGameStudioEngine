using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ A Canvas page's RUN-TIME re-docking (owner decision 2026-09-27). When user code shows or hides a docked control
/// (or a strip) — <c>el.style.display</c>, <c>el.hidden</c>, a class, or piece 2's portable <c>Visible</c> — WinForms
/// re-docks the siblings; the page does too, in BOTH states. The stylesheet is the page's first state
/// (<see cref="FormDockMode.Runtime"/>); this script runs only on a change.
///
/// <para>⛔⛔ <see cref="Core"/> is a MIRROR of <see cref="FormDockLayout.ResolveSiblings"/>, <c>FormDockLayout.Walk</c>
/// (Runtime) and <see cref="FormAnchorCss.Docked"/>, in JavaScript: a mirrored pair across two languages — the
/// <c>Tracks</c>/<c>ParseTracks</c> scar, a third time, unless it is gated. <c>FormDockScriptTests</c> runs both over one
/// fixture table under node and requires byte-identical answers. Change both in ONE commit.</para>
///
/// <para>The hook is a <c>MutationObserver</c> on <c>.vgs-form</c> (attributes <c>style</c>/<c>class</c>/<c>hidden</c>,
/// subtree): there is no <c>Visible</c> setter on the web to hook — a control's field is a DOM <c>Element</c> and user
/// code writes the DOM directly (measured). Its callback is a microtask, so the re-dock lands before the next paint.
/// The live rules go into ONE <c>&lt;style id="vgs-dock-live"&gt;</c> in <c>&lt;head&gt;</c> (later in the cascade
/// than the page's stylesheet, same specificity), inside <c>@media (width &gt;= breakpoint)</c> so the phone column is
/// never overridden.</para>
/// </summary>
public static class FormDockScript
{
    /// <summary>The pure resolver, mirrored from C#. ⚠ Line endings normalised: a raw literal takes the source file's.</summary>
    public static readonly string Core = """
        // vgs-dock: re-docks a Canvas page when a docked control is shown or hidden at run time, as WinForms does.
        // MIRROR of BasicLang FormDockLayout.ResolveSiblings / Walk (Runtime) and FormAnchorCss.Docked.
        // FormDockScriptTests runs this and the C# over one table; change both in one commit.
        function vgsDockSiblings(nodes, width, height, hidden) {
          var cw = Math.max(0, width), ch = Math.max(0, height);
          var x = 0, y = 0, w = cw, h = ch, placed = [];
          for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (node.e === null || hidden(node)) continue;
            var aw = Math.max(0, w), ah = Math.max(0, h), b;
            if (node.e === "Top") { b = [x, y, aw, node.h]; y += node.h; h -= node.h; }
            else if (node.e === "Bottom") { b = [x, y + h - node.h, aw, node.h]; h -= node.h; }
            else if (node.e === "Left") { b = [x, y, node.w, ah]; x += node.w; w -= node.w; }
            else if (node.e === "Right") { b = [x + w - node.w, y, node.w, ah]; w -= node.w; }
            else { b = [x, y, aw, ah]; }
            placed.push({ n: node, e: node.e, b: b, cw: cw, ch: ch });
          }
          return placed;
        }
        function vgsDockWalk(nodes, width, height, hidden, out) {
          var placed = vgsDockSiblings(nodes, width, height, hidden);
          for (var p = 0; p < placed.length; p++) out.push(placed[p]);
          for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (!node.c || hidden(node)) continue;
            var iw = node.w, ih = node.h;
            for (var q = 0; q < placed.length; q++) {
              if (placed[q].n === node) { iw = placed[q].b[2]; ih = placed[q].b[3]; break; }
            }
            if (node.k.length > 0) vgsDockWalk(node.k, iw, ih, hidden, out);
          }
        }
        function vgsDockCss(d) {
          var b = d.b;
          function px(v) { return v + "px"; }
          var right = px(d.cw - (b[0] + b[2])), bottom = px(d.ch - (b[1] + b[3]));
          if (d.e === "Top") return [["left", px(b[0])], ["right", right], ["top", px(b[1])], ["height", px(b[3])]];
          if (d.e === "Bottom") return [["left", px(b[0])], ["right", right], ["bottom", bottom], ["height", px(b[3])]];
          if (d.e === "Left") return [["left", px(b[0])], ["width", px(b[2])], ["top", px(b[1])], ["bottom", bottom]];
          if (d.e === "Right") return [["right", right], ["width", px(b[2])], ["top", px(b[1])], ["bottom", bottom]];
          return [["left", px(b[0])], ["right", right], ["top", px(b[1])], ["bottom", bottom]];
        }

        """.ReplaceLineEndings("\n");

    /// <summary>The DOM glue: observe, re-walk, write the live rules.</summary>
    public static readonly string Bootstrap = """
        function vgsDockStart(root, breakpoint) {
          var form = document.querySelector(".vgs-form");
          if (!form || typeof MutationObserver === "undefined") return;
          var live = null, last = null;
          function hidden(node) {
            var el = document.getElementById(node.i);
            return el ? getComputedStyle(el).display === "none" : node.hd;
          }
          function reflow() {
            var out = [];
            vgsDockWalk(root.k, root.w, root.h, hidden, out);
            var rules = "";
            for (var r = 0; r < out.length; r++) {
              var decls = vgsDockCss(out[r]), text = "";
              for (var d = 0; d < decls.length; d++) text += decls[d][0] + ":" + decls[d][1] + ";";
              rules += "#" + out[r].n.i + "{" + text + "}";
            }
            var css = breakpoint > 0 ? "@media (width >= " + breakpoint + "px){" + rules + "}" : rules;
            if (css === last) return;
            last = css;
            if (!live) {
              live = document.createElement("style");
              live.id = "vgs-dock-live";
              document.head.appendChild(live);
            }
            live.textContent = css;
          }
          new MutationObserver(reflow).observe(form, { attributes: true, subtree: true, attributeFilter: ["style", "class", "hidden"] });
        }

        """.ReplaceLineEndings("\n");

    /// <summary>
    /// The inline classic script for <paramref name="form"/>'s page, or null when nothing on it docks. An IIFE, so
    /// nothing leaks into the page's globals beside App.js.
    /// </summary>
    public static string? PageScript(FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var root = Root(form);
        if (!Docks(root.Children))
        {
            return null;
        }

        var breakpoint = form.Layout?.EffectiveMobileBreakpoint ?? FormLayout.DefaultMobileBreakpoint;
        var sb = new StringBuilder();
        sb.Append("<script data-vgs=\"dock\">\n(function () {\n");
        sb.Append(Core).Append(Bootstrap);
        sb.Append("vgsDockStart(").Append(JsonSerializer.Serialize(root)).Append(", ")
          .Append(breakpoint.ToString(CultureInfo.InvariantCulture)).Append(");\n");
        sb.Append("})();\n</script>\n");
        return sb.ToString();
    }

    /// <summary>
    /// The script's data for <paramref name="form"/>: the root client size (<see cref="FormDocument.DesignSize"/>) and,
    /// per sibling list in DOCUMENT order, everything that docks or holds something that does — each node's edge
    /// (<see cref="FormDockLayout.EdgeOf"/>), own size (<see cref="FormDockLayout.OwnSizeOf"/>), whether it lays
    /// children out (<see cref="FormDockLayout.HasClientArea"/>) and whether it is designed hidden. ⛔ The resolver's
    /// own rules, never copies. ⚠ JSON-escaped, so no id can close the <c>&lt;script&gt;</c>.
    /// </summary>
    public static string DataJson(FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return JsonSerializer.Serialize(Root(form));
    }

    private static FormDockNode Root(FormDocument form)
    {
        var (width, height) = form.DesignSize;
        return new FormDockNode("", null, width, height, true, false, Nodes(form.Controls));
    }

    private static List<FormDockNode> Nodes(IReadOnlyList<FormControl> siblings)
    {
        var nodes = new List<FormDockNode>();
        foreach (var control in siblings)
        {
            var edge = FormDockLayout.EdgeOf(control);
            var hasClientArea = FormDockLayout.HasClientArea(control);
            var children = hasClientArea ? Nodes(control.Children) : new List<FormDockNode>();

            // Neither docks nor holds anything that does: it consumes nothing, so the script need not know it.
            if (edge == null && children.Count == 0)
            {
                continue;
            }

            var (width, height) = FormDockLayout.OwnSizeOf(control);
            nodes.Add(new FormDockNode(control.Id, edge?.ToString(), width, height, hasClientArea, control.IsHidden, children));
        }

        return nodes;
    }

    private static bool Docks(IEnumerable<FormDockNode> nodes) =>
        nodes.Any(n => n.Edge != null || Docks(n.Children));
}

/// <summary>One node of <see cref="FormDockScript.DataJson"/>. Short names: this ships in every docked page.</summary>
internal sealed record FormDockNode(
    [property: JsonPropertyName("i")] string Id,
    [property: JsonPropertyName("e")] string? Edge,
    [property: JsonPropertyName("w")] int Width,
    [property: JsonPropertyName("h")] int Height,
    [property: JsonPropertyName("c")] bool HasClientArea,
    [property: JsonPropertyName("hd")] bool DesignHidden,
    [property: JsonPropertyName("k")] IReadOnlyList<FormDockNode> Children);
